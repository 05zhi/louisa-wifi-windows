using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using LouisaWiFi.Core;
using Microsoft.Win32.SafeHandles;

namespace LouisaWiFi.Windows;

public sealed class NativeWifi : IWifi, IDisposable
{
    private readonly WlanHandle handle;
    private readonly Native.Notify callback;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object eventLock = new();
    private readonly HashSet<(Guid Interface, string Profile)> ownProfiles = [];
    private event Action<Native.Notification>? Notification;
    public event Action? Changed;
    private bool disposed;
    public NativeWifi()
    {
        Check(Native.WlanOpenHandle(2, 0, out _, out handle));
        callback = (ref Native.Notification n, nint _) =>
        {
            try
            {
                lock (eventLock) Notification?.Invoke(n);
                if (n.Code is 10 or 11 or 21 or 26 or 27) ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke());
            }
            catch { /* Exceptions must never cross the unmanaged callback boundary. */ }
        };
        try { Check(Native.WlanRegisterNotification(handle, 8, true, callback, 0, 0, out _)); }
        catch { handle.Dispose(); throw; }
    }
    private static void Check(uint code)
    {
        if (code == 0) return;
        throw new Win32Exception((int)code, code switch
        {
            5 => "Wi-Fi 存取被拒，請在 Windows 定位隱私設定允許桌面應用程式存取位置",
            1062 => "Windows WLAN AutoConfig 服務未啟動",
            5023 => "Wi-Fi 已關閉或網卡目前無法使用",
            _ => $"Windows Wi-Fi 錯誤 {code}：{new Win32Exception((int)code).Message}"
        });
    }
    public IReadOnlyList<(Guid Id, string Description, int State)> Interfaces()
    {
        Check(Native.WlanEnumInterfaces(handle, 0, out var ptr));
        try
        {
            var result = new List<(Guid, string, int)>();
            var count = Marshal.ReadInt32(ptr);
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<Native.InterfaceInfo>(ptr + 8 + i * Marshal.SizeOf<Native.InterfaceInfo>());
                result.Add((info.Id, info.Description, info.State));
            }
            return result;
        }
        finally { Native.WlanFreeMemory(ptr); }
    }
    public Task<IReadOnlyList<WifiConnection>> CurrentAsync(CancellationToken token) => Task.Run<IReadOnlyList<WifiConnection>>(() =>
    {
        token.ThrowIfCancellationRequested();
        var result = new List<WifiConnection>();
        foreach (var info in Interfaces())
        {
            if (info.State is not (1 or 2)) continue;
            var id = info.Id;
            var code = Native.WlanQueryInterface(handle, ref id, 7, 0, out _, out var ptr, out _);
            if (code == 5023) continue;
            Check(code);
            try
            {
                var c = Marshal.PtrToStructure<Native.ConnectionAttributes>(ptr);
                if (c.State is 1 or 2) result.Add(new(id, c.Association.Ssid.Text, Mac(c.Association.Bssid), c.Profile));
            }
            finally { Native.WlanFreeMemory(ptr); }
        }
        return result;
    }, token);

    public async Task<IReadOnlyList<AccessPoint>> ScanAsync(CancellationToken token)
    {
        await operations.WaitAsync(token);
        try
        {
            var interfaces = Interfaces();
            if (interfaces.Count == 0) throw new InvalidOperationException("找不到無線網卡");
            var result = new List<AccessPoint>();
            foreach (var info in interfaces)
            {
                var id = info.Id;
                var done = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnScan(Native.Notification n) { if (n.Interface == info.Id && n.Code is 7 or 8) done.TrySetResult(n.Code); }
                lock (eventLock) Notification += OnScan;
                try
                {
                    Check(Native.WlanScan(handle, ref id, 0, 0, 0));
                    try { if (await done.Task.WaitAsync(TimeSpan.FromSeconds(8), token) == 8) throw new InvalidOperationException("Wi-Fi 掃描失敗，請確認無線網卡與定位設定"); }
                    catch (TimeoutException) { throw new InvalidOperationException("Wi-Fi 掃描逾時，請稍後重試"); }
                    var security = GetSecurity(id);
                    if (security == null) continue;
                    Check(Native.WlanGetNetworkBssList(handle, ref id, 0, 1, true, 0, out var ptr));
                    try
                    {
                        var total = Marshal.ReadInt32(ptr);
                        var count = Marshal.ReadInt32(ptr, 4);
                        var size = Marshal.SizeOf<Native.BssEntry>();
                        if (count < 0 || count > (total - 8) / size) throw new InvalidDataException("無效的 Wi-Fi 掃描資料");
                        for (var i = 0; i < count; i++)
                        {
                            var b = Marshal.PtrToStructure<Native.BssEntry>(ptr + 8 + i * size);
                            if (b.Ssid.Text == "LouisaCoffee") result.Add(new(id, Mac(b.Bssid), (int)b.Signal, security.Value.Auth, security.Value.Cipher));
                        }
                    }
                    finally { Native.WlanFreeMemory(ptr); }
                }
                finally { lock (eventLock) Notification -= OnScan; }
            }
            return result;
        }
        finally { operations.Release(); }
    }
    private (uint Auth, uint Cipher)? GetSecurity(Guid id)
    {
        Check(Native.WlanGetAvailableNetworkList(handle, ref id, 0, 0, out var ptr));
        try
        {
            var count = Marshal.ReadInt32(ptr);
            for (var i = 0; i < count; i++)
            {
                var n = Marshal.PtrToStructure<Native.AvailableNetwork>(ptr + 8 + i * Marshal.SizeOf<Native.AvailableNetwork>());
                if (n.Ssid.Text == "LouisaCoffee" && n.Security && n.Auth == 7 && n.Cipher == 4) return (n.Auth, n.Cipher);
            }
            return null;
        }
        finally { Native.WlanFreeMemory(ptr); }
    }
    public static string ProfileXml(string name, string password)
    {
        var authentication = Element("authEncryption", Element("authentication", "WPA2PSK"), Element("encryption", "AES"), Element("useOneX", "false"));
        var key = Element("sharedKey", Element("keyType", "passPhrase"), Element("protected", "false"), Element("keyMaterial", password));
        return new XDocument(Element("WLANProfile", Element("name", name),
            Element("SSIDConfig", Element("SSID", Element("name", "LouisaCoffee"))),
            Element("connectionType", "ESS"), Element("connectionMode", "manual"),
            Element("MSM", Element("security", authentication, key)))).ToString();
    }
    private static XElement Element(string name, params object[] content) => new(XName.Get(name, "http://www.microsoft.com/networking/WLAN/profile/v1"), content);

    public async Task<WifiConnection?> ConnectAsync(AccessPoint point, string password, CancellationToken token)
    {
        if (!PasswordRules.IsValid(password)) throw new ArgumentException("密碼需為 8 至 63 個可列印英數符號");
        if (point.Auth != 7 || point.Cipher != 4) throw new InvalidOperationException("首版僅支援 WPA2-Personal / AES 網路");
        await operations.WaitAsync(token);
        var id = point.InterfaceId;
        var name = "LouisaWiFi-" + Guid.NewGuid().ToString("N");
        nint desired = 0;
        var completed = false;
        var stoppedByUser = false;
        var started = false;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnConnect(Native.Notification n)
        {
            if (n.Interface != point.InterfaceId) return;
            if (n.Code is 9 or 10 or 11 && n.Data != 0 && n.Size >= 520)
            {
                // WLAN_CONNECTION_NOTIFICATION_DATA profile starts after its 4-byte mode.
                var profile = Marshal.PtrToStringUni(n.Data + 4, 256)?.TrimEnd('\0');
                if (profile != name) { stoppedByUser = true; done.TrySetCanceled(); return; }
                if (n.Code == 10) done.TrySetResult(true);
                if (n.Code == 11) done.TrySetResult(false);
            }
            if (n.Code == 21 && started) { stoppedByUser = true; done.TrySetCanceled(); }
        }
        try
        {
            token.ThrowIfCancellationRequested();
            // Do not overwrite an existing user profile. Random per-attempt names are registered for cleanup.
            Check(Native.WlanSetProfile(handle, ref id, 2, ProfileXml(name, password), null, false, 0, out _));
            ownProfiles.Add((id, name));
            // NDIS_OBJECT_HEADER + number/total counts + one DOT11_MAC_ADDRESS.
            var mac = Convert.FromHexString(point.Bssid.Replace(":", ""));
            var bssidList = new Native.BssidList { Type = 0x80, Revision = 1, Size = (ushort)Marshal.SizeOf<Native.BssidList>(), Count = 1, Total = 1, Bssid = mac };
            desired = Marshal.AllocHGlobal(Marshal.SizeOf<Native.BssidList>());
            Marshal.StructureToPtr(bssidList, desired, false);
            var parameters = new Native.ConnectionParameters { Mode = 0, Profile = name, Desired = desired, BssType = 1 };
            lock (eventLock) Notification += OnConnect;
            Check(Native.WlanConnect(handle, ref id, ref parameters, 0));
            started = true;
            var succeeded = await done.Task.WaitAsync(token);
            if (!succeeded) return null;
            token.ThrowIfCancellationRequested();
            var current = (await CurrentAsync(token)).FirstOrDefault(c => c.InterfaceId == id && c.Profile == name && c.Ssid == "LouisaCoffee");
            completed = current != null;
            // A different BSSID may have accepted the same password. Keep Wi-Fi, but never learn the requested AP.
            return current?.Bssid.Equals(point.Bssid, StringComparison.OrdinalIgnoreCase) == true ? current : null;
        }
        catch (OperationCanceledException) when (stoppedByUser && !token.IsCancellationRequested)
        { throw new InvalidOperationException("偵測到外部連線變更或手動中斷，已停止嘗試"); }
        finally
        {
            lock (eventLock) Notification -= OnConnect;
            if (!completed)
            {
                // Disconnect only a pending/active connection owned by this exact profile.
                if (started && !stoppedByUser && OwnsCurrentOrPending(id, name)) Native.WlanDisconnect(handle, ref id, 0);
                Native.WlanDeleteProfile(handle, ref id, name, 0);
                ownProfiles.Remove((id, name));
            }
            if (desired != 0) Marshal.FreeHGlobal(desired);
            operations.Release();
        }
    }
    private bool OwnsCurrentOrPending(Guid id, string profile)
    {
        var code = Native.WlanQueryInterface(handle, ref id, 7, 0, out _, out var ptr, out _);
        // Query returns invalid-state while associating; abort our still-owned pending request too.
        if (code == 5023) return true;
        if (code != 0) return false;
        try { return Marshal.PtrToStructure<Native.ConnectionAttributes>(ptr).Profile == profile; }
        finally { Native.WlanFreeMemory(ptr); }
    }
    private static string Mac(byte[] bytes) => string.Join(":", bytes.Select(b => b.ToString("x2")));
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Native.WlanRegisterNotification(handle, 0, true, null, 0, 0, out _);
        foreach (var entry in ownProfiles) { var id = entry.Interface; Native.WlanDeleteProfile(handle, ref id, entry.Profile, 0); }
        handle.Dispose();
        GC.KeepAlive(callback);
    }
}

public sealed class WlanHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public WlanHandle() : base(true) { }
    protected override bool ReleaseHandle() => Native.WlanCloseHandle(handle, 0) == 0;
}
public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct BssidList { public byte Type, Revision; public ushort Size; public uint Count, Total; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct InterfaceInfo { public Guid Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description; public int State; }
    [StructLayout(LayoutKind.Sequential)] public struct Ssid { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes; public readonly string Text => Encoding.UTF8.GetString(Bytes, 0, (int)Math.Min(Length, 32)); }
    [StructLayout(LayoutKind.Sequential)] public struct RateSet { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)] public ushort[] Rates; }
    [StructLayout(LayoutKind.Sequential)] public struct BssEntry
    {
        public Ssid Ssid; public uint PhyId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public uint BssType, PhyType; public int Rssi; public uint Signal; public byte InRegDomain; public ushort Beacon;
        public ulong Timestamp, HostTimestamp; public ushort Capability; public uint Frequency; public RateSet Rates; public uint IeOffset, IeSize;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Association
    {
        public Ssid Ssid; public uint BssType; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public uint PhyType, PhyIndex, Signal, RxRate, TxRate;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Security { public int Enabled, OneX; public uint Auth, Cipher; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct ConnectionAttributes
    { public int State, Mode; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile; public Association Association; public Security Security; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct AvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile; public Ssid Ssid; public uint BssType, BssidCount;
        [MarshalAs(UnmanagedType.Bool)] public bool Connectable; public uint Reason, PhyCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] Phys;
        [MarshalAs(UnmanagedType.Bool)] public bool MorePhy; public uint Signal; [MarshalAs(UnmanagedType.Bool)] public bool Security;
        public uint Auth, Cipher, Flags, Reserved;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct ConnectionParameters
    { public uint Mode; [MarshalAs(UnmanagedType.LPWStr)] public string Profile; public nint Ssid, Desired; public uint BssType, Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct Notification { public uint Source, Code; public Guid Interface; public uint Size; public nint Data; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void Notify(ref Notification notification, nint context);
    [DllImport("wlanapi.dll")] public static extern uint WlanOpenHandle(uint version, nint reserved, out uint negotiated, out WlanHandle handle);
    [DllImport("wlanapi.dll")] public static extern uint WlanCloseHandle(nint handle, nint reserved);
    [DllImport("wlanapi.dll")] public static extern void WlanFreeMemory(nint memory);
    [DllImport("wlanapi.dll")] public static extern uint WlanEnumInterfaces(WlanHandle handle, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] public static extern uint WlanQueryInterface(WlanHandle handle, ref Guid id, uint opcode, nint reserved, out uint size, out nint data, out uint type);
    [DllImport("wlanapi.dll")] public static extern uint WlanScan(WlanHandle handle, ref Guid id, nint ssid, nint ie, nint reserved);
    [DllImport("wlanapi.dll")] public static extern uint WlanGetNetworkBssList(WlanHandle handle, ref Guid id, nint ssid, uint type, [MarshalAs(UnmanagedType.Bool)] bool security, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] public static extern uint WlanGetAvailableNetworkList(WlanHandle handle, ref Guid id, uint flags, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] public static extern uint WlanRegisterNotification(WlanHandle handle, uint source, [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate, Notify? callback, nint context, nint reserved, out uint previous);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] public static extern uint WlanSetProfile(WlanHandle handle, ref Guid id, uint flags, string xml, string? security, [MarshalAs(UnmanagedType.Bool)] bool overwrite, nint reserved, out uint reason);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] public static extern uint WlanConnect(WlanHandle handle, ref Guid id, ref ConnectionParameters parameters, nint reserved);
    [DllImport("wlanapi.dll")] public static extern uint WlanDisconnect(WlanHandle handle, ref Guid id, nint reserved);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] public static extern uint WlanDeleteProfile(WlanHandle handle, ref Guid id, string name, nint reserved);
}
