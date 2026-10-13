using System.Runtime.InteropServices;
using System.Text.Json;
using LouisaWiFi.Core;
using Microsoft.Win32;

namespace LouisaWiFi.Windows;

public sealed class Settings
{
    public bool AutoConnect { get; set; }
    public bool UseLocation { get; set; }
    public bool KnownOnly { get; set; }
    public bool StartAtLogin { get; set; }
}
public sealed record Envelope<T>(int SchemaVersion, T Data);
public sealed class LocalData : ILearning
{
    private readonly object sync = new();
    public string Root { get; }
    public Settings Settings { get; private set; }
    public Dictionary<string, LearnedRouter> Routers { get; private set; }
    public Dictionary<string, string> Overrides { get; private set; }
    public LocalData(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LouisaWiFi");
        Directory.CreateDirectory(Root);
        Settings = Load("settings.json", () => new Settings());
        Routers = Load("routers.dat", () => new Dictionary<string, LearnedRouter>(), true);
        Overrides = Load("overrides.dat", () => new Dictionary<string, string>(), true);
    }
    public T Load<T>(string name, Func<T> fallback, bool encrypted = false)
    {
        lock (sync)
        {
            var path = Path.Combine(Root, name);
            if (!File.Exists(path)) return fallback();
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (encrypted) bytes = Dpapi.Unprotect(bytes);
                var value = JsonSerializer.Deserialize<Envelope<T>>(bytes, JsonDefaults.Options);
                if (value is null || value.SchemaVersion != 1 || value.Data is null) throw new InvalidDataException();
                return value.Data;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or System.ComponentModel.Win32Exception or IOException)
            {
                File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                Log("偵測到損壞的本機資料，已保留備份：" + name);
                return fallback();
            }
        }
    }
    public void Save<T>(string name, T value, bool encrypted = false)
    {
        lock (sync)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope<T>(1, value), JsonDefaults.Options);
            if (encrypted) bytes = Dpapi.Protect(bytes);
            var path = Path.Combine(Root, name);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { file.Write(bytes); file.Flush(true); }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public void SaveSettings() => Save("settings.json", Settings);
    public LearnedRouter? Find(string bssid) { lock (sync) return Routers.GetValueOrDefault(bssid.ToLowerInvariant()); }
    public void Remember(string bssid, Store store, string password)
    {
        lock (sync)
        {
            Routers[bssid.ToLowerInvariant()] = new(store.Id, store.Name, store.Address, password);
            Save("routers.dat", Routers, true);
        }
    }
    public void SetOverride(string id, string? password)
    {
        lock (sync)
        {
            if (string.IsNullOrEmpty(password)) Overrides.Remove(id);
            else if (PasswordRules.IsValid(password)) Overrides[id] = password;
            else throw new ArgumentException("密碼需為 8 至 63 個可列印英數符號");
            Save("overrides.dat", Overrides, true);
        }
    }
    public bool IsKnown(string id) { lock (sync) return Routers.Values.Any(r => r.StoreId == id); }
    public void Clear()
    {
        lock (sync)
        {
            // Only delete files owned by this application inside its dedicated directory.
            foreach (var file in Directory.EnumerateFiles(Root))
            {
                var name = Path.GetFileName(file);
                if (new[] { "settings.json", "routers.dat", "overrides.dat", "usage.json", "app.log" }.Any(n => name == n || name.StartsWith(n + ".", StringComparison.Ordinal))) File.Delete(file);
            }
            Settings = new(); Routers.Clear(); Overrides.Clear();
        }
    }
    public void Log(string message)
    {
        lock (sync)
        {
            var path = Path.Combine(Root, "app.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Move(path, path + ".1", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
    }
}
public static class StartupRegistration
{
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("LouisaWiFi", $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue("LouisaWiFi", false);
    }
}
public static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint ptr);
    public static byte[] Protect(byte[] bytes) => Transform(bytes, true);
    public static byte[] Unprotect(byte[] bytes) => Transform(bytes, false);
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = protect ? CryptProtectData(ref input, null, 0, 0, 0, 1, out var output) : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, output.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length); Marshal.FreeHGlobal(input.Data); }
    }
}
