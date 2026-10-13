using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using LouisaWiFi.Core;
using LouisaWiFi.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LouisaWiFi.App;

public sealed class MainWindow : Window, IDisposable
{
    private readonly StoreCatalog catalog;
    private readonly LocalData data;
    private readonly LocationService location;
    private readonly UsageTracker tracker = new(TimeZoneInfo.Local);
    private readonly NativeWifi? wifi;
    private readonly ConnectionCoordinator? coordinator;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly TextBlock status = Text("準備就緒", 16);
    private readonly TextBlock connection = Text("尚未檢查連線", 12);
    private readonly TextBox search = new() { MinWidth = 180, Padding = new Thickness(8), Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox city = new() { MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
    private readonly CheckBox known = new() { Content = "只顯示連過的門市", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox automatic = new() { Content = "自動連線" };
    private readonly CheckBox useLocation = new() { Content = "使用定位尋找附近門市（最多三家、五公里內）" };
    private readonly CheckBox login = new() { Content = "登入 Windows 時啟動" };
    private readonly DataGrid stores = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 36, MinHeight = 160 };
    private readonly TextBlock detail = Text("選擇門市查看候選密碼", 13);
    private readonly PasswordBox overridePassword = new() { Width = 170, Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 0) };
    private readonly CheckBox customPassword = new() { Content = "使用自訂密碼", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox years = new() { Width = 100, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox months = new() { Width = 100, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox statsStore = new() { Width = 220 };
    private readonly TextBlock statsSummary = Text("", 16);
    private readonly UniformGrid heatmap = new() { Columns = 31, Margin = new Thickness(0, 16, 0, 16) };
    private readonly ListBox ranking = new();
    private readonly ToolStripCheckItem autoTray;
    private CancellationTokenSource debounce = new();
    private readonly CancellationTokenSource lifetime = new();
    private bool exiting, ticking, ready, disposed;
    private int ticks;
    private readonly bool smoke = Environment.GetCommandLineArgs().Contains("--smoke");
    private HashSet<string>? nearIds;
    private readonly string? smokeRoot;
    public MainWindow(StoreCatalog catalog)
    {
        this.catalog = catalog;
        if (smoke) smokeRoot = Path.Combine(Path.GetTempPath(), "LouisaWiFi-smoke-" + Guid.NewGuid().ToString("N"));
        data = new(smokeRoot);
        tracker.Usage.AddRange(data.Load("usage.json", () => new List<UsageSlice>()).Where(u => u.Seconds is >= 0 and <= 86400 && DateOnly.TryParse(u.Date, out _)));
        location = new(() => data.Settings.UseLocation);
        Title = "Louisa Wi-Fi for Windows"; Width = 980; Height = 740; MinWidth = 780; MinHeight = 580;
        FontFamily = new FontFamily("Microsoft JhengHei UI"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(250, 248, 244));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { wifi = new(); coordinator = new(wifi, location, data, catalog); coordinator.StatusChanged += OnStatus; wifi.Changed += OnNetworkChanged; }
        catch (Exception ex) { status.Text = ex.Message; }
        var layout = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(Text("☕  Louisa Wi-Fi", 28, FontWeights.Bold));
        header.Children.Add(Text("Windows 預覽版  ·  " + catalog.Stores.Count + " 家內建門市  ·  資料 " + catalog.Dataset.Version, 12));
        header.Children.Add(status); header.Children.Add(connection);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "  尋找門市  ", Content = BuildSearch() });
        tabs.Items.Add(new TabItem { Header = "  連線統計  ", Content = BuildStats() });
        tabs.Items.Add(new TabItem { Header = "  設定與說明  ", Content = BuildSettings() }); layout.Children.Add(tabs); Content = layout;
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("開啟門市搜尋", null, (_, _) => Reveal());
        menu.Items.Add("附近門市", null, async (_, _) => { Reveal(); await ShowNearby(); });
        menu.Items.Add("依縣市瀏覽", null, (_, _) => { Reveal(); tabs.SelectedIndex = 0; city.IsDropDownOpen = true; });
        autoTray = new ToolStripCheckItem("自動連線", data.Settings.AutoConnect, value => { automatic.IsChecked = value; automatic.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }); menu.Items.Add(autoTray.Item);
        menu.Items.Add("取消連線嘗試", null, (_, _) => coordinator?.Cancel());
        menu.Items.Add("連線統計", null, (_, _) => { Reveal(); tabs.SelectedIndex = 1; RefreshStats(); });
        menu.Items.Add("設定與登入啟動", null, (_, _) => { Reveal(); tabs.SelectedIndex = 2; });
        menu.Items.Add("檢查更新", null, async (_, _) => await CheckUpdate());
        menu.Items.Add("診斷資訊", null, (_, _) => Diagnose());
        menu.Items.Add("結束", null, (_, _) => Exit());
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Louisa Wi-Fi", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Reveal();
        automatic.IsChecked = data.Settings.AutoConnect; useLocation.IsChecked = data.Settings.UseLocation;
        known.IsChecked = data.Settings.KnownOnly; login.IsChecked = data.Settings.StartAtLogin;
        if (coordinator != null) coordinator.AutoEnabled = data.Settings.AutoConnect;
        ready = true; RefreshSearch(); RefreshStats();
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; Hide(); } };
        timer.Tick += async (_, _) => await Tick();
        if (!smoke) { timer.Start(); SystemEvents.PowerModeChanged += PowerChanged; _ = Tick(); }
    }
    private UIElement BuildSearch()
    {
        var panel = new DockPanel { Margin = new Thickness(14) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(Text("搜尋店名、地址或電話；連線功能需 Windows 允許 Wi-Fi／定位存取。", 12));
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 6) };
        row.Children.Add(city); row.Children.Add(known); row.Children.Add(search); top.Children.Add(row);
        city.Items.Add("所有縣市"); foreach (var c in catalog.Stores.Select(s => s.City).Where(c => c.Length > 0).Distinct().Order()) city.Items.Add(c); city.SelectedIndex = 0;
        search.TextChanged += (_, _) => RefreshSearch(); city.SelectionChanged += (_, _) => RefreshSearch();
        known.Click += (_, _) => { data.Settings.KnownOnly = known.IsChecked == true; data.SaveSettings(); RefreshSearch(); };
        var actions = new WrapPanel(); actions.Children.Add(Button("附近門市", async () => await ShowNearby()));
        actions.Children.Add(Button("所有門市", () => { nearIds = null; RefreshSearch(); }));
        actions.Children.Add(Button("連線到選取門市", ConnectSelected));
        actions.Children.Add(Button("取消目前嘗試", () => coordinator?.Cancel())); top.Children.Add(actions);
        DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; bottom.Children.Add(detail);
        var custom = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        custom.Children.Add(customPassword); custom.Children.Add(overridePassword);
        custom.Children.Add(Button("儲存覆寫密碼", () => { if (stores.SelectedItem is Store s) { data.SetOverride(s.Id, overridePassword.Password); detail.Text = "已加密儲存；勾選使用自訂密碼後連線。"; } }));
        custom.Children.Add(Button("移除覆寫", () => { if (stores.SelectedItem is Store s) { data.SetOverride(s.Id, null); overridePassword.Clear(); customPassword.IsChecked = false; } }));
        bottom.Children.Add(custom); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        foreach (var (title, field, width) in new[] { ("門市", "Name", 2.0), ("地址", "Address", 3.0), ("電話", "Phone", 1.3) }) stores.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(field), Width = new DataGridLength(width, DataGridLengthUnitType.Star) });
        stores.SelectionChanged += (_, _) => { if (stores.SelectedItem is Store s) { detail.Text = s.Name + "  ·  候選密碼：" + string.Join(" / ", s.Passwords); overridePassword.Password = data.Overrides.GetValueOrDefault(s.Id, ""); customPassword.IsChecked = overridePassword.Password.Length > 0; } };
        panel.Children.Add(stores); return panel;
    }
    private UIElement BuildSettings()
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Text("連線與隱私", 20, FontWeights.Bold));
        panel.Children.Add(Text("開啟自動連線後，程式只在沒有現有 Wi-Fi 連線時嘗試 LouisaCoffee。\nWi-Fi 掃描與 BSSID 在部分 Windows 版本需要精確定位權限。\n定位僅用於尋找最近門市，設定、密碼與統計保存在此電腦。", 13));
        foreach (var checkbox in new[] { automatic, useLocation, login }) { checkbox.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(checkbox); }
        automatic.Click += async (_, _) => { if (!ready) return; data.Settings.AutoConnect = automatic.IsChecked == true; data.SaveSettings(); autoTray.Item.Checked = data.Settings.AutoConnect; if (coordinator != null) { coordinator.AutoEnabled = data.Settings.AutoConnect; if (coordinator.AutoEnabled) await coordinator.RunAutomaticAsync(); } };
        useLocation.Click += (_, _) => { data.Settings.UseLocation = useLocation.IsChecked == true; data.SaveSettings(); };
        login.Click += (_, _) => { StartupRegistration.Set(login.IsChecked == true); data.Settings.StartAtLogin = login.IsChecked == true; data.SaveSettings(); };
        var controls = new WrapPanel { Margin = new Thickness(0, 16, 0, 16) };
        controls.Children.Add(Button("開啟定位隱私設定", () => Open("ms-settings:privacy-location")));
        controls.Children.Add(Button("檢查更新", async () => await CheckUpdate())); controls.Children.Add(Button("診斷資訊", Diagnose));
        panel.Children.Add(controls);
        panel.Children.Add(Text("關閉視窗會繼續在系統匣執行；請從系統匣選單選擇「結束」。\n首版支援 WPA2-Personal / AES；WPA3 與企業驗證尚未支援。\n連上 Wi-Fi 與可上網是兩個狀態，門市驗證頁不會被當成密碼錯誤。\n預覽版尚待受控 AP 與實際門市驗證。", 13));
        panel.Children.Add(Button("清除本機設定、路由器與統計", ClearData));
        panel.Children.Add(Button("開啟 GitHub 與下載頁", () => Open("https://github.com/" + UpdateService.Repository)));
        panel.Children.Add(Text("參考 madeyexz/louisa-wifi 重新實作 · MIT · 與路易莎官方無隸屬關係", 12));
        return new ScrollViewer { Content = panel };
    }
    private UIElement BuildStats()
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(Text("在路易莎的時間", 20, FontWeights.Bold));
        panel.Children.Add(Text("僅計入確認連上 LouisaCoffee 的時間。睡眠不累計，跨日依本機日期拆分。", 12));
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        foreach (var year in tracker.Usage.Select(u => int.Parse(u.Date[..4])).Append(DateTime.Now.Year).Distinct().OrderDescending()) years.Items.Add(year);
        years.SelectedItem = DateTime.Now.Year; months.Items.Add("全年"); for (var i = 1; i <= 12; i++) months.Items.Add(i + " 月"); months.SelectedIndex = 0;
        statsStore.Items.Add("所有門市"); foreach (var s in tracker.Usage.Select(u => u.StoreName).Distinct().Order()) statsStore.Items.Add(s); statsStore.SelectedIndex = 0;
        row.Children.Add(years); row.Children.Add(months); row.Children.Add(statsStore); panel.Children.Add(row);
        years.SelectionChanged += (_, _) => RefreshStats(); months.SelectionChanged += (_, _) => RefreshStats(); statsStore.SelectionChanged += (_, _) => RefreshStats();
        panel.Children.Add(statsSummary); panel.Children.Add(heatmap); panel.Children.Add(Text("門市排行（累計時間）", 15, FontWeights.Bold)); panel.Children.Add(ranking);
        return new ScrollViewer { Content = panel };
    }
    private void RefreshSearch()
    {
        if (!ready) return;
        stores.ItemsSource = catalog.Search(search.Text, city.SelectedIndex > 0 ? city.SelectedItem as string : null)
            .Where(s => known.IsChecked != true || data.IsKnown(s.Id)).Where(s => nearIds == null || nearIds.Contains(s.Id)).ToArray();
    }
    private async Task ShowNearby()
    {
        if (!data.Settings.UseLocation) { status.Text = "請先在設定勾選使用定位，或直接搜尋門市"; return; }
        status.Text = "正在取得位置…";
        var p = await location.LocateAsync(lifetime.Token);
        if (p == null) { status.Text = "定位狀態：" + LocationText(location.State) + "；仍可手動搜尋門市"; return; }
        nearIds = catalog.Nearest(p).Select(s => s.Id).ToHashSet(); city.SelectedIndex = 0; search.Clear(); RefreshSearch();
        status.Text = nearIds.Count > 0 ? "已顯示五公里內最近三家門市" : "五公里內沒有有效門市資料，請搜尋所有門市";
    }
    private async void ConnectSelected()
    {
        if (stores.SelectedItem is not Store s) { status.Text = "請先選擇門市"; return; }
        var password = customPassword.IsChecked == true ? overridePassword.Password : null;
        if (password != null && !PasswordRules.IsValid(password)) { status.Text = "密碼需為 8 至 63 個可列印英數符號"; return; }
        if (coordinator != null) await coordinator.RunManualAsync(s, password);
    }
    private void OnStatus(ConnectionStatus change) => Dispatcher.BeginInvoke(() =>
    {
        status.Text = change.Message;
        tray.Text = change.Message.Length > 60 ? change.Message[..60] : change.Message;
        // Log only enum states. Passwords, positions, SSIDs and BSSIDs never enter logs.
        data.Log("connection-state: " + change.State);
        if (change.State is ConnectionState.Connected or ConnectionState.Failed && !IsVisible)
        { tray.BalloonTipTitle = "Louisa Wi-Fi"; tray.BalloonTipText = change.Message; tray.ShowBalloonTip(4000); }
        if (change.State == ConnectionState.Connected) RefreshSearch();
    });
    private void OnNetworkChanged()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            debounce.Cancel(); debounce.Dispose(); debounce = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var token = debounce.Token;
            try { await Task.Delay(2000, token); await Tick(); }
            catch (OperationCanceledException) { }
        });
    }
    private async Task Tick()
    {
        if (ticking || exiting || wifi == null) return;
        ticking = true;
        try
        {
            var current = await wifi.CurrentAsync(lifetime.Token);
            var louisa = current.FirstOrDefault(c => c.Ssid == "LouisaCoffee");
            var learned = louisa == null ? null : data.Find(louisa.Bssid);
            tracker.Observe(DateTimeOffset.UtcNow, louisa != null, learned?.StoreId, learned?.StoreName);
            data.Save("usage.json", tracker.Usage);
            var internet = global::Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel();
            connection.Text = (current.Count > 0 ? louisa != null ? "Wi-Fi：已連上 LouisaCoffee" : "Wi-Fi：保留其他網路連線" : "Wi-Fi：未連線") + "  ·  Windows 網際網路狀態：" + (internet == global::Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess ? "可用" : "未確認／可能需要驗證頁");
            RefreshStats();
            if (coordinator != null && ++ticks % 2 == 0) await coordinator.RunAutomaticAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { connection.Text = ex.Message; tracker.Suspend(); }
        finally { ticking = false; }
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => { tracker.Suspend(); coordinator?.Cancel(); if (e.Mode == PowerModes.Resume) _ = Tick(); });
    }
    private void RefreshStats()
    {
        if (!ready || years.SelectedItem is not int year) return;
        var selectedMonth = months.SelectedIndex;
        var selectedStore = statsStore.SelectedIndex > 0 ? statsStore.SelectedItem as string : null;
        foreach (var name in tracker.Usage.Select(u => u.StoreName).Distinct()) if (!statsStore.Items.Contains(name)) statsStore.Items.Add(name);
        var usage = tracker.Usage.Where(u => u.Date.StartsWith(year.ToString() + "-", StringComparison.Ordinal) &&
            (selectedMonth == 0 || u.Date.Substring(5, 2) == selectedMonth.ToString("00")) && (selectedStore == null || u.StoreName == selectedStore)).ToArray();
        statsSummary.Text = $"共 {usage.Sum(u => u.Seconds) / 3600:0.0} 小時  ·  {usage.Select(u => u.Date).Distinct().Count()} 天";
        heatmap.Children.Clear();
        var start = new DateTime(year, selectedMonth > 0 ? selectedMonth : 1, 1);
        var end = selectedMonth > 0 ? start.AddMonths(1) : start.AddYears(1);
        for (var date = start; date < end; date = date.AddDays(1))
        {
            var key = date.ToString("yyyy-MM-dd"); var entries = usage.Where(u => u.Date == key).ToArray(); var seconds = entries.Sum(u => u.Seconds);
            var color = seconds == 0 ? Color.FromRgb(232, 229, 224) : seconds < 1800 ? Color.FromRgb(207, 178, 143) : seconds < 7200 ? Color.FromRgb(159, 108, 64) : Color.FromRgb(92, 53, 29);
            heatmap.Children.Add(new Border { Height = 16, Margin = new Thickness(2), CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color), ToolTip = key + $"  {seconds / 60:0} 分鐘\n" + string.Join("\n", entries.Select(u => $"{u.StoreName} {u.Seconds / 60:0} 分鐘")) });
        }
        ranking.ItemsSource = usage.GroupBy(u => u.StoreName).OrderByDescending(g => g.Sum(u => u.Seconds)).Select(g => $"{g.Key}　{g.Sum(u => u.Seconds) / 3600:0.0} 小時").ToArray();
    }
    private async Task CheckUpdate()
    {
        try
        {
            status.Text = "正在檢查更新…";
            var updater = new UpdateService(); var release = await updater.CheckAsync(lifetime.Token);
            if (release == null) { status.Text = "目前已是最新版本 " + UpdateService.CurrentVersion; return; }
            if (MessageBox.Show($"發現 {release.Tag}，下載並啟動安裝程式？\n將以 SHA256 檢查下載完整性。", "更新", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { Open(release.Page.AbsoluteUri); return; }
            status.Text = "正在下載並驗證更新…";
            var path = await updater.DownloadAsync(release, Path.Combine(data.Root, "updates"), lifetime.Token);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); Exit();
        }
        catch (OperationCanceledException) { status.Text = "更新已取消"; }
        catch (Exception ex) { status.Text = "更新失敗，保留目前版本：" + ex.Message; }
    }
    private void Diagnose()
    {
        var adapters = wifi == null ? "Wi-Fi 服務無法使用" : string.Join("\n", wifi.Interfaces().Select(i => $"{i.Description} / state={i.State}"));
        MessageBox.Show($"版本：{UpdateService.CurrentVersion}\n系統：{Environment.OSVersion.Version}\n門市：{catalog.Stores.Count} / {catalog.Dataset.Version}\n定位：{LocationText(location.State)}\n{adapters}\n\n診斷不包含密碼、位置或 BSSID。", "診斷資訊");
    }
    private void ClearData()
    {
        if (coordinator?.Busy == true) { status.Text = "請先取消目前連線，等流程結束後再清除"; return; }
        if (MessageBox.Show("清除設定、路由器記憶、覆寫密碼與使用統計？此操作無法復原。", "清除本機資料", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (coordinator != null) coordinator.AutoEnabled = false;
        StartupRegistration.Set(false); data.Clear(); tracker.Suspend(); tracker.Usage.Clear();
        automatic.IsChecked = false; useLocation.IsChecked = false; login.IsChecked = false; known.IsChecked = false; autoTray.Item.Checked = false;
        overridePassword.Clear(); customPassword.IsChecked = false; RefreshSearch(); RefreshStats(); status.Text = "已清除本機資料";
    }
    private static string LocationText(LocationState state) => state switch { LocationState.Available => "已取得有效位置", LocationState.Denied => "使用者拒絕存取", LocationState.Disabled => "系統定位關閉或受政策限制", LocationState.NotEnabled => "尚未啟用", _ => "暫時無法取得有效位置" };
    private static TextBlock Text(string value, double size, FontWeight? weight = null) => new() { Text = value, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5), Foreground = new SolidColorBrush(Color.FromRgb(69, 45, 29)) };
    private static Button Button(string label, Action action) { var b = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 4, 8, 4) }; b.Click += (_, _) => action(); return b; }
    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    public void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }
    public async void Exit()
    {
        if (exiting) return;
        exiting = true; timer.Stop(); lifetime.Cancel(); coordinator?.Cancel();
        // Allow native operations to observe cancellation before closing their handle.
        while (coordinator?.Busy == true || ticking) await Task.Delay(50);
        if (!smoke) data.Save("usage.json", tracker.Usage);
        Dispose(); Application.Current.Shutdown();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        timer.Stop(); lifetime.Cancel(); debounce.Cancel();
        SystemEvents.PowerModeChanged -= PowerChanged;
        if (wifi != null) { wifi.Changed -= OnNetworkChanged; wifi.Dispose(); }
        tray.Dispose(); debounce.Dispose(); lifetime.Dispose();
        if (smokeRoot != null && Directory.Exists(smokeRoot)) Directory.Delete(smokeRoot, true);
    }
    private sealed class ToolStripCheckItem
    {
        public Forms.ToolStripMenuItem Item { get; }
        public ToolStripCheckItem(string text, bool value, Action<bool> changed) { Item = new(text) { Checked = value, CheckOnClick = true }; Item.Click += (_, _) => changed(Item.Checked); }
    }
}
