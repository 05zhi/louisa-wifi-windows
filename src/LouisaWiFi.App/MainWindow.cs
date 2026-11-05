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
    private readonly IWifi? wifi;
    private readonly ConnectionCoordinator? coordinator;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly TextBlock status = Text("準備就緒", 16);
    private readonly TextBlock connection = Text("尚未檢查連線", 12);
    private readonly TextBox search = new() { MinWidth = 180, Padding = new Thickness(8), Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox city = new() { MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
    private readonly CheckBox known = new() { Content = "只顯示連過的門市", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox automatic = new() { Content = "沒有 Wi-Fi 連線時，自動嘗試連線" };
    private readonly CheckBox useLocation = new() { Content = "允許本程式用定位尋找門市" };
    private readonly CheckBox login = new() { Content = "登入 Windows 時啟動" };
    private readonly DataGrid stores = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 36, MinHeight = 160 };
    private readonly TextBlock detail = Text("先選擇你所在的門市，再按「連線到這家店」。", 13);
    private readonly TabControl tabs = new();
    private readonly TextBlock locationStatus = Text("", 14, FontWeights.SemiBold);
    private readonly TextBlock locationHint = Text("", 12);
    private readonly TextBlock results = Text("", 12);
    private readonly Expander passwordOptions = new() { Header = "店內密碼不同？使用自訂密碼", Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock emptyResults = Text("找不到符合條件的門市。試著縮短關鍵字，或按「清除篩選」。", 14);
    private Button connectButton = null!, cancelButton = null!, testLocationButton = null!, findNearbyButton = null!;
    private Button savePasswordButton = null!, forgetPasswordButton = null!;
    private bool connecting, locating, returningFromPermissions;
    private CancellationTokenSource? locationRequest;
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
    public MainWindow(StoreCatalog catalog, IWifi? wifiOverride = null)
    {
        this.catalog = catalog;
        if (smoke) smokeRoot = Path.Combine(Path.GetTempPath(), "LouisaWiFi-smoke-" + Guid.NewGuid().ToString("N"));
        data = new(smokeRoot);
        tracker.Usage.AddRange(data.Load("usage.json", () => new List<UsageSlice>()).Where(u => u.Seconds is >= 0 and <= 86400 && DateOnly.TryParse(u.Date, out _)));
        location = new(() => data.Settings.UseLocation);
        Title = "Louisa Wi-Fi for Windows" + (smoke ? " · UI test" : ""); Width = 1000; Height = 820; MinWidth = 820; MinHeight = 680;
        FontFamily = new FontFamily("Microsoft JhengHei UI"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(250, 248, 244));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { wifi = wifiOverride ?? new NativeWifi(); coordinator = new(wifi, location, data, catalog); coordinator.StatusChanged += OnStatus; wifi.Changed += OnNetworkChanged; if (wifi is NativeWifi native) native.ConnectionFailed += reason => data.Log($"wlan-failure: 0x{reason:X}"); }
        catch (Exception ex) { status.Text = ex.Message; }
        var layout = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(Text("☕  Louisa Wi-Fi", 28, FontWeights.Bold));
        header.Children.Add(status); header.Children.Add(connection);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        tabs.Items.Add(new TabItem { Header = "  尋找門市  ", Content = BuildSearch() });
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
        ready = true; RefreshSearch(); RefreshStats(); RefreshLocation(); UpdateSelection();
        Activated += async (_, _) =>
        {
            if (!returningFromPermissions || !ready || exiting) return;
            returningFromPermissions = false;
            if (data.Settings.UseLocation) await CheckLocation(false);
        };
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; Hide(); } };
        timer.Tick += async (_, _) => await Tick();
        if (!smoke) { timer.Start(); SystemEvents.PowerModeChanged += PowerChanged; _ = Tick(); }
    }
    private UIElement BuildSearch()
    {
        var panel = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(Text("1  找到你所在的門市", 18, FontWeights.SemiBold));
        System.Windows.Automation.AutomationProperties.SetName(search, "搜尋店名、地址或電話");
        System.Windows.Automation.AutomationProperties.SetName(city, "縣市");
        search.ToolTip = "搜尋店名、地址或電話";
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 6) };
        var searchArea = new Grid(); searchArea.Children.Add(search);
        var searchHint = Text("搜尋店名、地址或電話", 13); searchHint.Margin = new Thickness(10, 0, 0, 0);
        searchHint.VerticalAlignment = VerticalAlignment.Center; searchHint.IsHitTestVisible = false;
        searchHint.Foreground = Brushes.Gray; searchArea.Children.Add(searchHint);
        search.TextChanged += (_, _) => searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        row.Children.Add(city); row.Children.Add(searchArea); top.Children.Add(row);
        city.Items.Add("所有縣市"); foreach (var c in catalog.Stores.Select(s => s.City).Where(c => c.Length > 0).Distinct().Order()) city.Items.Add(c); city.SelectedIndex = 0;
        search.TextChanged += (_, _) => RefreshSearch(); city.SelectionChanged += (_, _) => RefreshSearch();
        known.Click += (_, _) => { data.Settings.KnownOnly = known.IsChecked == true; data.SaveSettings(); RefreshSearch(); };
        var actions = new WrapPanel(); actions.Children.Add(Button("尋找附近門市", async () => await ShowNearby()));
        actions.Children.Add(Button("清除篩選", ResetFilters));
        known.Margin = new Thickness(12, 0, 0, 0); actions.Children.Add(known); top.Children.Add(actions);
        top.Children.Add(results);
        DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        bottom.Children.Add(Text("2  確認門市並連線", 18, FontWeights.SemiBold)); bottom.Children.Add(detail);
        var custom = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        customPassword.Content = "改用店內提供的密碼";
        System.Windows.Automation.AutomationProperties.SetName(overridePassword, "店內提供的 Wi-Fi 密碼");
        customPassword.Click += (_, _) => RefreshConnectionControls();
        custom.Children.Add(customPassword); custom.Children.Add(overridePassword);
        savePasswordButton = Button("記住此店密碼", SavePassword);
        forgetPasswordButton = Button("忘記自訂密碼", () =>
        {
            if (stores.SelectedItem is not Store selected) return;
            data.SetOverride(selected.Id, null); overridePassword.Clear(); customPassword.IsChecked = false;
            detail.Text = "已移除自訂密碼，下次會使用電話衍生密碼。"; RefreshConnectionControls();
        });
        custom.Children.Add(savePasswordButton); custom.Children.Add(forgetPasswordButton); passwordOptions.Content = custom; bottom.Children.Add(passwordOptions);
        var connectActions = new WrapPanel();
        connectButton = PrimaryButton("連線到這家店", ConnectSelected);
        cancelButton = Button("取消連線", () => coordinator?.Cancel());
        connectActions.Children.Add(connectButton); connectActions.Children.Add(cancelButton); bottom.Children.Add(connectActions);
        DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        foreach (var (title, field, width) in new[] { ("門市", "Name", 2.0), ("地址", "Address", 3.0), ("電話", "Phone", 1.3) }) stores.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(field), Width = new DataGridLength(width, DataGridLengthUnitType.Star) });
        stores.SelectionChanged += (_, _) => UpdateSelection();
        var list = new Grid(); list.Children.Add(stores);
        emptyResults.HorizontalAlignment = HorizontalAlignment.Center; emptyResults.VerticalAlignment = VerticalAlignment.Center;
        emptyResults.Margin = new Thickness(20); emptyResults.IsHitTestVisible = false; list.Children.Add(emptyResults);
        panel.Children.Add(list); return panel;
    }
    private UIElement BuildSettings()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var locationPanel = new StackPanel();
        locationPanel.Children.Add(Text("定位與附近門市", 20, FontWeights.SemiBold));
        locationPanel.Children.Add(Text("定位用來找五公里內最近的三家門市。不開啟，也能手動搜尋店名。", 13));
        useLocation.Margin = new Thickness(0, 10, 0, 8); locationPanel.Children.Add(useLocation);
        locationPanel.Children.Add(locationStatus); locationPanel.Children.Add(locationHint);
        var locationActions = new WrapPanel();
        findNearbyButton = PrimaryButton("啟用定位並尋找", async () => { if (!data.Settings.UseLocation) SetLocationEnabled(true); await ShowNearby(); });
        testLocationButton = Button("重新檢查定位", async () => await CheckLocation(false));
        locationActions.Children.Add(findNearbyButton); locationActions.Children.Add(testLocationButton);
        locationActions.Children.Add(Button("開啟 Windows 定位權限", () => { returningFromPermissions = true; Open("ms-settings:privacy-location"); }));
        locationPanel.Children.Add(locationActions);
        locationPanel.Children.Add(Text("Windows 權限與上方開關是兩回事。若連線或定位被拒，請在 Windows 開啟「位置服務」及適用的應用程式／桌面應用程式定位權限，再回來檢查。", 12));
        panel.Children.Add(Card(locationPanel));
        var connectionPanel = new StackPanel();
        connectionPanel.Children.Add(Text("自動連線", 20, FontWeights.SemiBold));
        automatic.Margin = new Thickness(0, 10, 0, 8); connectionPanel.Children.Add(automatic);
        connectionPanel.Children.Add(Text("優先使用記住的門市路由器；開啟定位後，也能嘗試附近門市。已連上其他 Wi-Fi 或手機熱點時，不會切換網路。", 13));
        connectionPanel.Children.Add(Text("第一次到店，建議先手動選店連線。失敗後自動重試會暫停十分鐘，仍可手動重試。", 12));
        panel.Children.Add(Card(connectionPanel));
        var appPanel = new StackPanel();
        appPanel.Children.Add(Text("啟動與關閉", 20, FontWeights.SemiBold));
        login.Margin = new Thickness(0, 10, 0, 8); appPanel.Children.Add(login);
        appPanel.Children.Add(Text("按視窗的 × 會縮到系統匣，程式仍在執行。更新、刪除程式資料夾前，請按「完全結束程式」。", 13));
        appPanel.Children.Add(Button("完全結束程式", Exit)); panel.Children.Add(Card(appPanel));
        var support = new StackPanel();
        support.Children.Add(Text("版本、說明與本機資料", 20, FontWeights.SemiBold));
        support.Children.Add(Text($"版本 {UpdateService.CurrentVersion}  ·  {catalog.Stores.Count} 家門市  ·  門市資料 {catalog.Dataset.Version}", 12));
        var controls = new WrapPanel();
        controls.Children.Add(Button("檢查更新", async () => await CheckUpdate())); controls.Children.Add(Button("使用說明", () => Open("https://github.com/" + UpdateService.Repository + "#使用說明")));
        controls.Children.Add(Button("診斷資訊", Diagnose)); support.Children.Add(controls);
        support.Children.Add(Text("密碼加密保存在此電腦；不儲存定位座標。清除資料會移除設定、記住的路由器、自訂密碼與連線統計。", 12));
        support.Children.Add(Button("清除本機資料…", ClearData));
        support.Children.Add(Text("支援 WPA2-Personal／AES。連上 Wi-Fi 後，部分門市仍需完成網頁驗證。\n參考 madeyexz/louisa-wifi · MIT · 與路易莎官方無隸屬關係", 12));
        panel.Children.Add(Card(support));
        automatic.Click += async (_, _) => { if (!ready) return; data.Settings.AutoConnect = automatic.IsChecked == true; data.SaveSettings(); autoTray.Item.Checked = data.Settings.AutoConnect; if (coordinator != null) { coordinator.AutoEnabled = data.Settings.AutoConnect; if (coordinator.AutoEnabled) await coordinator.RunAutomaticAsync(); } };
        useLocation.Click += (_, _) => SetLocationEnabled(useLocation.IsChecked == true);
        login.Click += (_, _) => { StartupRegistration.Set(login.IsChecked == true); data.Settings.StartAtLogin = login.IsChecked == true; data.SaveSettings(); };
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    public void ShowSettings() { tabs.SelectedIndex = 2; }
    private void SetLocationEnabled(bool enabled)
    {
        locationRequest?.Cancel(); location.Reset();
        data.Settings.UseLocation = enabled; useLocation.IsChecked = enabled; data.SaveSettings();
        if (!enabled) { nearIds = null; RefreshSearch(); }
        RefreshLocation();
    }
    private void RefreshLocation()
    {
        var enabled = data.Settings.UseLocation;
        locationStatus.Text = !enabled ? "程式定位已關閉" : locating ? "正在取得位置，請稍候…" : location.State switch
        {
            LocationState.Available => "定位可用，可以尋找附近門市",
            LocationState.Denied => "Windows 尚未允許定位存取",
            LocationState.Disabled => "Windows 定位已關閉，或由組織管理",
            LocationState.Unavailable => "目前無法取得足夠準確的位置",
            _ => "程式定位已開啟，尚未檢查 Windows 權限"
        };
        locationHint.Text = !enabled ? "按「啟用定位並尋找」即可開始；也可以回到「尋找門市」手動搜尋。" : location.State switch
        {
            LocationState.Denied or LocationState.Disabled => "下一步：開啟 Windows 定位權限，調整後回到這裡重新檢查。",
            LocationState.Unavailable => "請稍後重新檢查，或手動搜尋門市。這不一定是權限問題。",
            LocationState.Available => "只用於附近門市排序，不會儲存定位座標。",
            _ => "按「重新檢查定位」確認是否可用；程式不會替你更改 Windows 權限。"
        };
        findNearbyButton.Content = enabled ? "尋找附近門市" : "啟用定位並尋找";
        findNearbyButton.IsEnabled = !locating; testLocationButton.IsEnabled = enabled && !locating;
    }
    private async Task CheckLocation(bool showNearby)
    {
        if (locating || !data.Settings.UseLocation) { RefreshLocation(); return; }
        locating = true; locationRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var request = locationRequest; RefreshLocation();
        try
        {
            var position = await location.LocateAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (position == null) { ShowSettings(); return; }
            if (showNearby)
            {
                nearIds = catalog.Nearest(position).Select(s => s.Id).ToHashSet();
                city.SelectedIndex = 0; search.Clear(); known.IsChecked = false;
                data.Settings.KnownOnly = false; data.SaveSettings(); RefreshSearch(); tabs.SelectedIndex = 0;
                status.Text = nearIds.Count > 0 ? "已找到附近門市，請選擇你所在的店。" : "五公里內找不到門市，請按「清除篩選」再搜尋店名。";
            }
        }
        catch (OperationCanceledException) { }
        finally { locating = false; if (ReferenceEquals(locationRequest, request)) locationRequest = null; request.Dispose(); RefreshLocation(); }
    }
    private void ResetFilters()
    {
        nearIds = null; search.Clear(); city.SelectedIndex = 0; known.IsChecked = false;
        data.Settings.KnownOnly = false; data.SaveSettings(); RefreshSearch();
    }
    private void UpdateSelection()
    {
        if (stores.SelectedItem is Store selected)
        {
            detail.Text = selected.Name + "  ·  電話衍生密碼：" + string.Join(" / ", selected.Passwords);
            overridePassword.Password = data.Overrides.GetValueOrDefault(selected.Id, "");
            customPassword.IsChecked = overridePassword.Password.Length > 0;
        }
        else { detail.Text = "先選擇你所在的門市，再按「連線到這家店」。"; overridePassword.Clear(); customPassword.IsChecked = false; }
        passwordOptions.IsExpanded = customPassword.IsChecked == true;
        RefreshConnectionControls();
    }
    private void RefreshConnectionControls()
    {
        if (connectButton == null) return;
        var selected = stores.SelectedItem is Store;
        connectButton.IsEnabled = selected && !connecting && coordinator != null;
        cancelButton.IsEnabled = connecting;
        customPassword.IsEnabled = selected && !connecting;
        overridePassword.IsEnabled = selected && !connecting && customPassword.IsChecked == true;
        savePasswordButton.IsEnabled = overridePassword.IsEnabled;
        forgetPasswordButton.IsEnabled = selected && !connecting && data.Overrides.ContainsKey(((Store)stores.SelectedItem).Id);
    }
    private void SavePassword()
    {
        if (stores.SelectedItem is not Store selected) return;
        if (!PasswordRules.IsValid(overridePassword.Password)) { detail.Text = "請輸入 8 至 63 個英數字或符號的密碼，再按記住。"; overridePassword.Focus(); return; }
        data.SetOverride(selected.Id, overridePassword.Password); customPassword.IsChecked = true;
        detail.Text = "已加密記住此店密碼。按「連線到這家店」開始連線。"; RefreshConnectionControls();
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
        var selectedId = (stores.SelectedItem as Store)?.Id;
        var matches = catalog.Search(search.Text, city.SelectedIndex > 0 ? city.SelectedItem as string : null)
            .Where(s => known.IsChecked != true || data.IsKnown(s.Id)).Where(s => nearIds == null || nearIds.Contains(s.Id)).ToArray();
        stores.ItemsSource = matches;
        if (selectedId != null) stores.SelectedItem = matches.FirstOrDefault(s => s.Id == selectedId);
        results.Text = (nearIds != null ? "附近門市" : "搜尋結果") + $" · {matches.Length} 家";
        emptyResults.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task ShowNearby()
    {
        if (!data.Settings.UseLocation)
        {
            ShowSettings(); RefreshLocation(); useLocation.Focus();
            status.Text = "尋找附近門市需要定位。請選擇「啟用定位並尋找」，或回到搜尋手動選店。";
            return;
        }
        await CheckLocation(true);
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
        connecting = change.State is ConnectionState.Checking or ConnectionState.Scanning or ConnectionState.Selecting or ConnectionState.Connecting or ConnectionState.Verifying;
        RefreshConnectionControls(); RefreshLocation();
        tray.Text = change.Message.Length > 60 ? change.Message[..60] : change.Message;
        // Log only states and native reason codes, never passwords, positions or router IDs.
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
        var adapters = wifi is NativeWifi native ? string.Join("\n", native.Interfaces().Select(i => $"{i.Description} / state={i.State}")) : "Wi-Fi 服務無法使用";
        MessageBox.Show($"版本：{UpdateService.CurrentVersion}\n系統：{Environment.OSVersion.Version}\n門市：{catalog.Stores.Count} / {catalog.Dataset.Version}\n定位：{LocationText(location.State)}\n{adapters}\n\n診斷不包含密碼、位置或 BSSID。", "診斷資訊");
    }
    private void ClearData()
    {
        if (coordinator?.Busy == true) { status.Text = "請先取消目前連線，等流程結束後再清除"; return; }
        if (MessageBox.Show("清除設定、路由器記憶、覆寫密碼與使用統計？此操作無法復原。", "清除本機資料", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (coordinator != null) coordinator.AutoEnabled = false;
        StartupRegistration.Set(false); data.Clear(); tracker.Suspend(); tracker.Usage.Clear();
        automatic.IsChecked = false; useLocation.IsChecked = false; login.IsChecked = false; known.IsChecked = false; autoTray.Item.Checked = false;
        locationRequest?.Cancel(); location.Reset(); nearIds = null;
        overridePassword.Clear(); customPassword.IsChecked = false; RefreshSearch(); RefreshStats(); RefreshLocation(); status.Text = "已清除本機資料";
    }
    private static string LocationText(LocationState state) => state switch { LocationState.Available => "已取得有效位置", LocationState.Denied => "使用者拒絕存取", LocationState.Disabled => "系統定位關閉或受政策限制", LocationState.NotEnabled => "尚未啟用", _ => "暫時無法取得有效位置" };
    private static TextBlock Text(string value, double size, FontWeight? weight = null) => new() { Text = value, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5), Foreground = new SolidColorBrush(Color.FromRgb(69, 45, 29)) };
    private static Button Button(string label, Action action) { var b = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 4, 8, 4) }; b.Click += (_, _) => action(); return b; }
    private static Border Card(UIElement content) => new()
    {
        Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 219, 210)),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(18),
        Margin = new Thickness(0, 0, 0, 14), Child = content
    };
    private static Button PrimaryButton(string label, Action action)
    {
        var button = Button(label, action); button.Background = new SolidColorBrush(Color.FromRgb(107, 65, 35));
        var style = new Style(typeof(Button)); style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.DimGray)); style.Triggers.Add(disabled);
        button.Style = style; button.FontWeight = FontWeights.SemiBold;
        return button;
    }
    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    public void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }
    public async void Exit()
    {
        if (exiting) return;
        exiting = true; timer.Stop(); lifetime.Cancel(); coordinator?.Cancel();
        // Allow native operations to observe cancellation before closing their handle.
        while (coordinator?.Busy == true || ticking || locating) await Task.Delay(50);
        if (!smoke) data.Save("usage.json", tracker.Usage);
        Dispose(); Application.Current.Shutdown();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        timer.Stop(); lifetime.Cancel(); debounce.Cancel();
        SystemEvents.PowerModeChanged -= PowerChanged;
        if (wifi != null) { wifi.Changed -= OnNetworkChanged; (wifi as IDisposable)?.Dispose(); }
        tray.Dispose(); debounce.Dispose(); lifetime.Dispose();
        if (smokeRoot != null && Directory.Exists(smokeRoot)) Directory.Delete(smokeRoot, true);
    }
    private sealed class ToolStripCheckItem
    {
        public Forms.ToolStripMenuItem Item { get; }
        public ToolStripCheckItem(string text, bool value, Action<bool> changed) { Item = new(text) { Checked = value, CheckOnClick = true }; Item.Click += (_, _) => changed(Item.Checked); }
    }
}
