namespace LouisaWiFi.Core;

public sealed record AccessPoint(Guid InterfaceId, string Bssid, int Signal, uint Auth = 7, uint Cipher = 4);
public sealed record WifiConnection(Guid InterfaceId, string Ssid, string Bssid, string Profile);
public sealed record LearnedRouter(string StoreId, string StoreName, string Address, string Password);
public interface IWifi
{
    event Action? Changed;
    Task<IReadOnlyList<WifiConnection>> CurrentAsync(CancellationToken token);
    Task<IReadOnlyList<AccessPoint>> ScanAsync(CancellationToken token);
    Task<WifiConnection?> ConnectAsync(AccessPoint point, string password, CancellationToken token);
}
public interface ILocation { Task<Position?> LocateAsync(CancellationToken token); }
public interface ILearning
{
    LearnedRouter? Find(string bssid);
    void Remember(string bssid, Store store, string password);
}
public enum ConnectionState { Idle, Checking, Scanning, Selecting, Connecting, Verifying, Connected, Failed, Cooldown, Cancelled }
public sealed record ConnectionStatus(ConnectionState State, string Message);
public sealed record ConnectionLimits(TimeSpan Total, TimeSpan Attempt);

public sealed class ConnectionCoordinator(IWifi wifi, ILocation location, ILearning learning, StoreCatalog catalog, TimeProvider? clock = null, ConnectionLimits? limits = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private readonly Dictionary<string, DateTimeOffset> cooldown = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? active;
    private int generation;
    public event Action<ConnectionStatus>? StatusChanged;
    public ConnectionStatus Status { get; private set; } = new(ConnectionState.Idle, "請選擇門市，或啟用自動連線");
    public bool Busy { get { lock (sync) return active != null; } }
    private bool autoEnabled;
    private bool permissionBlocked;
    public bool AutoEnabled { get => autoEnabled; set { autoEnabled = value; if (!value) Cancel(); else permissionBlocked = false; } }
    public void Cancel() { lock (sync) { generation++; active?.Cancel(); } }
    private void Report(ConnectionState state, string message) { Status = new(state, message); StatusChanged?.Invoke(Status); }
    public Task RunAutomaticAsync() => AutoEnabled && !permissionBlocked ? RunAsync(null, null, false) : Task.CompletedTask;
    public Task RunManualAsync(Store store, string? password = null) => RunAsync(store, password, true);
    private async Task RunAsync(Store? selected, string? password, bool manual)
    {
        int epoch;
        lock (sync)
        {
            if (!manual && active != null) return;
            if (manual) { generation++; active?.Cancel(); }
            epoch = generation;
        }
        if (manual) await gate.WaitAsync(); else if (!await gate.WaitAsync(0)) return;
        using var cts = new CancellationTokenSource(limits?.Total ?? TimeSpan.FromMinutes(2));
        lock (sync)
        {
            if (epoch != generation) { gate.Release(); return; }
            active = cts;
        }
        var token = cts.Token;
        var attemptedBssids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var current = await wifi.CurrentAsync(token);
            if (current.Count > 0)
            {
                if (manual) Report(current.Any(c => c.Ssid == "LouisaCoffee") ? ConnectionState.Connected : ConnectionState.Idle,
                    current.Any(c => c.Ssid == "LouisaCoffee") ? "已連上 LouisaCoffee" : "保留目前 Wi-Fi 連線；如需換網路，請先在 Windows 中斷連線");
                return;
            }
            Report(ConnectionState.Scanning, "掃描附近 LouisaCoffee…");
            var points = (await wifi.ScanAsync(token)).OrderByDescending(p => p.Signal).ToArray();
            if (points.Length == 0) { Report(ConnectionState.Idle, "附近找不到 LouisaCoffee，請確認 Wi-Fi 已開啟"); return; }
            var available = points.Where(p => manual || !cooldown.TryGetValue(p.Bssid, out var until) || until <= time.GetUtcNow()).Take(3).ToArray();
            if (available.Length == 0) { Report(ConnectionState.Cooldown, "連線失敗後冷卻十分鐘；仍可手動選店重試"); return; }
            Report(ConnectionState.Selecting, "選擇門市與密碼候選…");
            var candidates = new List<(AccessPoint Point, Store Store, string Password)>();
            foreach (var p in available)
            {
                var learned = learning.Find(p.Bssid);
                var s = learned is null ? null : catalog.Stores.FirstOrDefault(s => s.Id == learned.StoreId) ??
                    catalog.Stores.FirstOrDefault(s => StoreCatalog.Normalize(s.Name) == StoreCatalog.Normalize(learned.StoreName) && StoreCatalog.Normalize(s.Address) == StoreCatalog.Normalize(learned.Address));
                if (s != null && (selected == null || s.Id == selected.Id) && password == null) candidates.Add((p, s, learned!.Password));
            }
            Store[] nearby;
            if (selected != null) nearby = [selected];
            else
            {
                var position = await location.LocateAsync(token);
                nearby = position?.IsUsable(time.GetUtcNow()) == true ? catalog.Nearest(position).ToArray() : [];
            }
            for (var rank = 0; rank < 3; rank++)
                foreach (var s in nearby)
                {
                    var passwords = password == null ? s.Passwords : [password];
                    if (rank >= passwords.Length) continue;
                    foreach (var p in available) candidates.Add((p, s, passwords[rank]));
                }
            var tries = candidates.Where(t => PasswordRules.IsValid(t.Password)).DistinctBy(t => (t.Point.Bssid.ToUpperInvariant(), t.Password)).Take(12).ToArray();
            if (tries.Length == 0) { foreach (var p in available) cooldown[p.Bssid] = time.GetUtcNow().AddMinutes(10); Report(ConnectionState.Failed, "無法辨識門市或沒有可用密碼，請手動選店；附近門市需要有效定位"); return; }
            foreach (var attempt in tries)
            {
                token.ThrowIfCancellationRequested();
                // A connection made outside this flow always wins, including another Louisa profile.
                if ((await wifi.CurrentAsync(token)).Count > 0) { Report(ConnectionState.Cancelled, "偵測到現有連線，已停止後續嘗試"); return; }
                Report(ConnectionState.Connecting, $"連線中：{attempt.Store.Name}…");
                attemptedBssids.Add(attempt.Point.Bssid);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(limits?.Attempt ?? TimeSpan.FromSeconds(18));
                WifiConnection? result;
                try { result = await wifi.ConnectAsync(attempt.Point, attempt.Password, timeout.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { continue; }
                token.ThrowIfCancellationRequested();
                if (result == null) continue;
                Report(ConnectionState.Verifying, "驗證實際連線…");
                var verified = (await wifi.CurrentAsync(token)).FirstOrDefault(c => c.InterfaceId == result.InterfaceId && c.Ssid == "LouisaCoffee" &&
                    c.Bssid.Equals(attempt.Point.Bssid, StringComparison.OrdinalIgnoreCase) && c.Profile == result.Profile);
                if (verified == null) { Report(ConnectionState.Cancelled, "連線已變更，未儲存路由器資料"); return; }
                learning.Remember(verified.Bssid, attempt.Store, attempt.Password);
                cooldown.Remove(verified.Bssid);
                Report(ConnectionState.Connected, $"已連上：{attempt.Store.Name}（網際網路狀態另行檢查）");
                return;
            }
            foreach (var p in available) cooldown[p.Bssid] = time.GetUtcNow().AddMinutes(10);
            Report(ConnectionState.Failed, "候選密碼已用完或連線逾時，請確認門市與密碼；自動重試暫停十分鐘");
        }
        catch (OperationCanceledException)
        {
            bool userCancelled; lock (sync) userCancelled = epoch != generation;
            if (!userCancelled) foreach (var bssid in attemptedBssids) cooldown[bssid] = time.GetUtcNow().AddMinutes(10);
            Report(userCancelled ? ConnectionState.Cancelled : ConnectionState.Failed,
                userCancelled ? "已取消，停止後續嘗試" : "連線流程超過兩分鐘，已停止；失敗路由器冷卻十分鐘");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception { NativeErrorCode: 5 }) permissionBlocked = true;
            Report(ConnectionState.Failed, ex.Message);
        }
        finally { lock (sync) { if (ReferenceEquals(active, cts)) active = null; } gate.Release(); }
    }
}
