using System.Text.Json;
using LouisaWiFi.Core;

var passed = 0;
async Task Test(string name, Func<Task> check) { await check(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool ok, string message = "assertion failed") { if (!ok) throw new Exception(message); }
var store = new Store("taipei", "臺北門市", "臺北市 信義區 信義路1號", "02-2345-8613", 25.03, 121.56, ["23458613", "0223458613", "223458613"]);
var catalog = new StoreCatalog(new(1, "test", [store]));
var clock = new FakeClock();
ConnectionCoordinator Coordinator(FakeWifi wifi, FakeLearning learned, Position? position = null) => new(wifi, new FakeLocation(position), learned, catalog, clock) { AutoEnabled = true };
await Test("telephone formats, extension, multiple numbers and deduplication", () =>
{
    Assert(PasswordRules.FromPhone("(02) 2345-8613 分機12 / 03-5555555").SequenceEqual(store.Passwords));
    Assert(PasswordRules.FromPhone("03-555-5555#12").SequenceEqual(new[] { "35555555", "035555555" }));
    Assert(PasswordRules.FromPhone("").Length == 0); Assert(PasswordRules.FromPhone("123").Length == 0);
    Assert(!PasswordRules.IsValid("中文中文中文中文")); return Task.CompletedTask;
});
await Test("normalized search, stable identity, missing coordinates and invalid data", () =>
{
    Assert(catalog.Search(" 台 北 ").Single().Id == store.Id);
    Assert(store.City == "台北市");
    Assert(StoreCatalog.StableId("臺北 店", "信義 路1號") == StoreCatalog.StableId("台北店", "信義路1號"));
    Assert(StoreCatalog.StableId("同名", "地址1") != StoreCatalog.StableId("同名", "地址2"));
    var missing = store with { Lat = 0, Lng = 0 };
    var c = new StoreCatalog(new(1, "test", [missing])); Assert(c.Search("台北").Any()); Assert(!c.Nearest(new(25, 121, 10, clock.GetUtcNow())).Any());
    try { StoreCatalog.Load(JsonSerializer.Serialize(new StoreDataset(1, "test", [store with { Lat = 999 }]), JsonDefaults.Options)); throw new Exception("accepted invalid coordinates"); }
    catch (InvalidDataException) { }
    return Task.CompletedTask;
});
await Test("protect an existing Wi-Fi or hotspot without scanning", async () =>
{
    var wifi = new FakeWifi { Current = [new(Guid.Empty, "PhoneHotspot", "aa", "user-profile")] };
    await Coordinator(wifi, new()).RunAutomaticAsync(); Assert(wifi.Scans == 0 && wifi.Attempts == 0);
});
await Test("learn only the verified BSSID, never neighboring routers", async () =>
{
    var wifi = new FakeWifi { Success = true }; var learned = new FakeLearning();
    await Coordinator(wifi, learned, new(25.03, 121.56, 20, clock.GetUtcNow())).RunAutomaticAsync();
    Assert(learned.Values.Count == 1 && learned.Values.ContainsKey("aa")); Assert(wifi.Attempts == 1);
});
await Test("cached BSSID reconnects with no location and migrates changed store id", async () =>
{
    var wifi = new FakeWifi { Success = true }; var learned = new FakeLearning();
    learned.Values["aa"] = new("old-id", store.Name, store.Address, "23458613");
    await Coordinator(wifi, learned).RunAutomaticAsync(); Assert(wifi.Attempts == 1 && learned.Values["aa"].StoreId == store.Id);
});
await Test("wrong actual BSSID is never learned", async () =>
{
    var wifi = new FakeWifi { Success = true, WrongBssid = true }; var learned = new FakeLearning();
    await Coordinator(wifi, learned).RunManualAsync(store); Assert(learned.Values.Count == 0);
});
await Test("failed passwords cool down, manual selection bypasses cooldown", async () =>
{
    var wifi = new FakeWifi(); var c = Coordinator(wifi, new(), new(25.03, 121.56, 20, clock.GetUtcNow()));
    await c.RunAutomaticAsync(); var attempts = wifi.Attempts; Assert(attempts == 6);
    await c.RunAutomaticAsync(); Assert(wifi.Attempts == attempts && c.Status.State == ConnectionState.Cooldown);
    await c.RunManualAsync(store); Assert(wifi.Attempts > attempts);
});
await Test("stale or inaccurate location requires manual store", async () =>
{
    foreach (var p in new[] { new Position(25, 121, 20, clock.GetUtcNow().AddMinutes(-6)), new Position(25, 121, 1500, clock.GetUtcNow()) })
    { var wifi = new FakeWifi(); await Coordinator(wifi, new(), p).RunAutomaticAsync(); Assert(wifi.Attempts == 0); }
});
await Test("coalesce concurrent automatic triggers and stop immediately on cancel", async () =>
{
    var wifi = new FakeWifi { Block = true }; var c = Coordinator(wifi, new());
    var first = c.RunManualAsync(store); await wifi.Entered.Task;
    await c.RunAutomaticAsync(); c.Cancel(); await first;
    Assert(wifi.Attempts == 1 && wifi.MaxActive == 1 && c.Status.State == ConnectionState.Cancelled);
});
await Test("manual selection preempts automatic work without overlapping connections", async () =>
{
    var wifi = new FakeWifi { Block = true }; var c = Coordinator(wifi, new(), new(25.03, 121.56, 10, clock.GetUtcNow()));
    var auto = c.RunAutomaticAsync(); await wifi.Entered.Task;
    wifi.Block = false; var manual = c.RunManualAsync(store, "23458613"); await Task.WhenAll(auto, manual);
    Assert(wifi.MaxActive == 1 && wifi.Attempts >= 2);
});
await Test("external network change and disabled auto stop later password attempts", async () =>
{
    var wifi = new FakeWifi { OnAttempt = w => w.Current = [new(Guid.Empty, "user-network", "zz", "user")] };
    await Coordinator(wifi, new()).RunManualAsync(store); Assert(wifi.Attempts == 1);
    wifi = new FakeWifi { Block = true }; var c = Coordinator(wifi, new()); var run = c.RunManualAsync(store); await wifi.Entered.Task; c.AutoEnabled = false; await run; Assert(wifi.Attempts == 1);
});
await Test("usage splits midnight, deduplicates events and excludes sleep/disconnected time", () =>
{
    var usage = new UsageTracker(TimeZoneInfo.Utc); var before = new DateTimeOffset(2026, 10, 13, 23, 59, 30, TimeSpan.Zero);
    usage.Observe(before, true, store.Id, store.Name); usage.Observe(before, true, store.Id, store.Name);
    usage.Observe(before.AddMinutes(1), true, store.Id, store.Name);
    Assert(usage.Usage.Count == 2 && usage.Usage.All(u => u.Seconds == 30));
    usage.Suspend(); usage.Observe(before.AddHours(8), true, store.Id, store.Name); Assert(usage.Usage.Sum(u => u.Seconds) == 60);
    usage.Observe(before.AddHours(9), true, store.Id, store.Name); Assert(usage.Usage.Sum(u => u.Seconds) == 60);
    usage.Observe(before.AddHours(9).AddSeconds(30), false); Assert(usage.Usage.Sum(u => u.Seconds) == 60);
    return Task.CompletedTask;
});
await Test("permission denial blocks further auto work until explicitly re-enabled", async () =>
{
    var denied = new DeniedWifi();
    var c = new ConnectionCoordinator(denied, new FakeLocation(null), new FakeLearning(), catalog) { AutoEnabled = true };
    await c.RunAutomaticAsync(); await c.RunAutomaticAsync(); Assert(denied.Calls == 1);
    c.AutoEnabled = false; c.AutoEnabled = true; await c.RunAutomaticAsync(); Assert(denied.Calls == 2);
});
await Test("overall deadline stops pending connection and applies cooldown", async () =>
{
    var wifi = new FakeWifi { Block = true };
    var c = new ConnectionCoordinator(wifi, new FakeLocation(new(25.03, 121.56, 20, clock.GetUtcNow())), new FakeLearning(), catalog, clock,
        new ConnectionLimits(TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(10))) { AutoEnabled = true };
    await c.RunAutomaticAsync(); Assert(wifi.Attempts == 1 && c.Status.State == ConnectionState.Failed);
    await c.RunAutomaticAsync();
    // One untouched AP remains eligible; the timed-out AP itself must not be retried.
    Assert(wifi.Attempts == 2);
    await c.RunAutomaticAsync(); Assert(wifi.Attempts == 2 && c.Status.State == ConnectionState.Cooldown);
});
Console.WriteLine($"{passed} core tests passed");

sealed class FakeClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero); }
sealed class DeniedWifi : IWifi
{
    public int Calls;
    public event Action? Changed { add { } remove { } }
    public Task<IReadOnlyList<WifiConnection>> CurrentAsync(CancellationToken token) { Calls++; throw new System.ComponentModel.Win32Exception(5, "permission denied"); }
    public Task<IReadOnlyList<AccessPoint>> ScanAsync(CancellationToken token) => throw new Exception("must not scan");
    public Task<WifiConnection?> ConnectAsync(AccessPoint point, string password, CancellationToken token) => throw new Exception("must not connect");
}
sealed class FakeLocation(Position? position) : ILocation { public Task<Position?> LocateAsync(CancellationToken token) => Task.FromResult(position); }
sealed class FakeLearning : ILearning
{
    public Dictionary<string, LearnedRouter> Values { get; } = [];
    public LearnedRouter? Find(string bssid) => Values.GetValueOrDefault(bssid);
    public void Remember(string bssid, Store store, string password) => Values[bssid] = new(store.Id, store.Name, store.Address, password);
}
sealed class FakeWifi : IWifi
{
    public event Action? Changed { add { } remove { } }
    public IReadOnlyList<WifiConnection> Current = [];
    public bool Success, WrongBssid, Block;
    public int Scans, Attempts, MaxActive, Active;
    public Action<FakeWifi>? OnAttempt;
    public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<IReadOnlyList<WifiConnection>> CurrentAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Current); }
    public Task<IReadOnlyList<AccessPoint>> ScanAsync(CancellationToken token) { Scans++; return Task.FromResult<IReadOnlyList<AccessPoint>>([new(Guid.Empty, "aa", 99), new(Guid.Empty, "bb", 80)]); }
    public async Task<WifiConnection?> ConnectAsync(AccessPoint point, string password, CancellationToken token)
    {
        Attempts++; Active++; MaxActive = Math.Max(MaxActive, Active); Entered.TrySetResult();
        try
        {
            if (Block) await Task.Delay(Timeout.Infinite, token);
            OnAttempt?.Invoke(this);
            if (!Success) return null;
            var c = new WifiConnection(point.InterfaceId, "LouisaCoffee", WrongBssid ? "different" : point.Bssid, "own-profile");
            Current = [c]; return c;
        }
        finally { Active--; }
    }
}
