using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LouisaWiFi.Core;
using LouisaWiFi.Windows;

namespace LouisaWiFi.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--diagnose"))
        {
            object report;
            try { using var wifi = new NativeWifi(); var current = wifi.CurrentAsync(CancellationToken.None).GetAwaiter().GetResult(); report = new { os = Environment.OSVersion.VersionString, adapters = wifi.Interfaces().Select(i => new { i.Description, i.State }).ToArray(), connectedWifiCount = current.Count, bssSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.BssEntry>() }; }
            catch (Exception ex) { report = new { os = Environment.OSVersion.VersionString, error = ex.Message }; }
            var text = JsonSerializer.Serialize(report, JsonDefaults.Options);
            var output = Array.IndexOf(args, "--output");
            if (output >= 0 && output + 1 < args.Length) File.WriteAllText(args[output + 1], text);
            return 0;
        }
        using var mutex = new Mutex(true, @"Local\LouisaWiFi.Windows.Singleton", out var first);
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LouisaWiFi.Windows.Activate");
        if (!first) { activate.Set(); return 0; }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.DispatcherUnhandledException += (_, e) =>
        {
            if (args.Contains("--smoke")) { File.WriteAllText("smoke-error.txt", e.Exception.ToString()); application.Shutdown(1); }
            else MessageBox.Show("操作失敗：" + e.Exception.Message, "Louisa Wi-Fi");
            e.Handled = true;
        };
        MainWindow? window = null;
        RegisteredWaitHandle? wait = null;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LouisaWiFi.App.stores.json") ?? throw new InvalidDataException("找不到內建門市資料");
            using var reader = new StreamReader(stream);
            var catalog = StoreCatalog.Load(reader.ReadToEnd());
            window = new MainWindow(catalog);
            application.MainWindow = window;
            wait = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => application.Dispatcher.BeginInvoke(() => window.Reveal()), null, -1, false);
            if (!args.Contains("--background")) window.Show();
            if (args.Contains("--smoke"))
            {
                // Isolated UI smoke check: no auto-connect, registry changes, or user data writes.
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var output = Array.IndexOf(args, "--output");
                    if (output >= 0 && output + 1 < args.Length)
                    {
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var file = File.Create(args[output + 1]); encoder.Save(file);
                    }
                    window.Exit();
                }; timer.Start();
            }
            application.Run();
            return 0;
        }
        catch (Exception ex) { if (args.Contains("--smoke")) File.WriteAllText("smoke-error.txt", ex.ToString()); else MessageBox.Show(ex.Message, "Louisa Wi-Fi 無法啟動"); return 1; }
        finally { wait?.Unregister(null); window?.Dispose(); mutex.ReleaseMutex(); }
    }
}
