using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LouisaWiFi.App;
using LouisaWiFi.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!args.Contains("--smoke")) throw new InvalidOperationException("UI tests require isolated --smoke data");
        var app = new Application();
        var store = new Store("test", "測試門市", "台北市測試地址", "02-2345-6789", 25, 121, ["23456789"]);
        using var window = new MainWindow(new(new(1, "test", [store])), new NoNetworkWifi());
        app.MainWindow = window;
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
        IEnumerable<T> All<T>() where T : DependencyObject => Descendants(window).OfType<T>();
        Button Button(string text) => All<Button>().Single(b => Equals(b.Content, text));
        void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        bool TextContains(string text) => All<TextBlock>().Any(t => t.Text.Contains(text));
        var grid = All<DataGrid>().Single(); var tabs = All<TabControl>().Single();
        Check(!Button("連線到這家店").IsEnabled && !Button("取消連線").IsEnabled, "no selection disables connection actions");
        Click(Button("尋找附近門市"));
        Check(tabs.SelectedIndex == 2 && TextContains("程式定位已關閉") && Button("啟用定位並尋找").IsEnabled && !Button("重新檢查定位").IsEnabled,
            "nearby search guides to settings without silently enabling location");
        tabs.SelectedIndex = 0;
        var query = All<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "搜尋店名、地址或電話");
        query.Text = "不存在的門市";
        Check(grid.Items.Count == 0 && All<TextBlock>().Any(t => t.Text.StartsWith("找不到符合條件") && t.Visibility == Visibility.Visible), "empty search has recovery guidance");
        Click(Button("清除篩選")); Check(grid.Items.Count == 1 && query.Text == "", "clear filters restores all stores");
        grid.SelectedItem = store;
        Check(Button("連線到這家店").IsEnabled && TextContains("電話衍生密碼"), "selecting a store enables connection and shows candidate explanation");
        var custom = All<CheckBox>().Single(c => Equals(c.Content, "改用店內提供的密碼"));
        custom.IsChecked = true; Click(custom);
        var password = All<PasswordBox>().Single(); password.Password = "short";
        Click(Button("記住此店密碼"));
        Check(TextContains("請輸入 8 至 63") && !Button("忘記自訂密碼").IsEnabled, "invalid custom password explains how to fix without saving");
        password.Password = "test-only-123"; Click(Button("記住此店密碼"));
        Check(TextContains("已加密記住") && Button("忘記自訂密碼").IsEnabled, "remember custom password confirms local save");
        Click(Button("忘記自訂密碼"));
        Check(password.Password.Length == 0 && custom.IsChecked == false && !password.IsEnabled, "forget password restores phone candidate mode");
        grid.SelectedItem = null;
        Check(!Button("連線到這家店").IsEnabled && !custom.IsEnabled, "clearing selection resets actions");
        Console.WriteLine($"{count} UI flow tests passed; no Wi-Fi or location request attempted");
        app.Shutdown();
        return 0;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}

// UI tests must not depend on a physical adapter or initiate network operations.
internal sealed class NoNetworkWifi : IWifi
{
    public event Action? Changed { add { } remove { } }
    public Task<IReadOnlyList<WifiConnection>> CurrentAsync(CancellationToken token) => throw new InvalidOperationException("Unexpected Wi-Fi query in UI test");
    public Task<IReadOnlyList<AccessPoint>> ScanAsync(CancellationToken token) => throw new InvalidOperationException("Unexpected Wi-Fi scan in UI test");
    public Task<WifiConnection?> ConnectAsync(AccessPoint point, string password, CancellationToken token) => throw new InvalidOperationException("Unexpected connection in UI test");
}
