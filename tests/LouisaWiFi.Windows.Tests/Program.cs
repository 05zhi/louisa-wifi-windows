using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using LouisaWiFi.Core;
using LouisaWiFi.Windows;

var count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
Check(Marshal.SizeOf<Native.InterfaceInfo>() == 532, "WLAN_INTERFACE_INFO layout");
Check(Marshal.SizeOf<Native.BssEntry>() == 360, "WLAN_BSS_ENTRY layout");
Check(Marshal.SizeOf<Native.ConnectionAttributes>() == 604, "WLAN_CONNECTION_ATTRIBUTES layout");
Check(Marshal.SizeOf<Native.AvailableNetwork>() == 628, "WLAN_AVAILABLE_NETWORK layout");
Check(Marshal.SizeOf<Native.BssidList>() == 20 && Marshal.OffsetOf<Native.BssidList>(nameof(Native.BssidList.Bssid)).ToInt32() == 12, "DOT11_BSSID_LIST full header size and MAC offset");
Check(Marshal.OffsetOf<Native.BssEntry>(nameof(Native.BssEntry.IeOffset)).ToInt32() == 352, "BSS IE offset");
var password = "test<&>12345";
var xml = XDocument.Parse(NativeWifi.ProfileXml("unique-own-profile", password));
XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
Check(xml.Descendants(ns + "keyMaterial").Single().Value == password && xml.Descendants(ns + "connectionMode").Single().Value == "manual", "profile escapes XML and uses manual mode");
var encrypted = Dpapi.Protect(Encoding.UTF8.GetBytes(password));
Check(Encoding.UTF8.GetString(Dpapi.Unprotect(encrypted)) == password && !Encoding.UTF8.GetString(encrypted).Contains(password), "current-user DPAPI round trip");
var root = Path.Combine(Path.GetTempPath(), "LouisaWiFi-test-" + Guid.NewGuid().ToString("N"));
try
{
    var data = new LocalData(root); var store = new Store("test", "店", "地址", "", 0, 0, []);
    data.Remember("AA:BB", store, password); data.SetOverride(store.Id, password);
    var restored = new LocalData(root);
    Check(restored.Find("aa:bb")?.Password == password && restored.Overrides[store.Id] == password, "encrypted learned router and override persistence");
    Check(!File.ReadAllText(Path.Combine(root, "routers.dat")).Contains(password), "no cleartext secrets on disk");
    File.WriteAllText(Path.Combine(root, "settings.json"), "broken-json");
    restored = new LocalData(root);
    Check(!restored.Settings.AutoConnect && Directory.EnumerateFiles(root, "settings.json.corrupt-*").Any(), "corrupt data preserved and defaults restored");
    restored.SaveSettings(); Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "atomic save cleans temporary file");
    File.WriteAllText(Path.Combine(root, "unrelated.txt"), "keep"); restored.Clear();
    Check(File.Exists(Path.Combine(root, "unrelated.txt")) && !File.Exists(Path.Combine(root, "routers.dat")), "clear only application-owned data");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
Check(UpdateService.CompareVersions("v0.1.0", "0.1.0-preview.10") > 0, "stable is newer than preview");
Check(UpdateService.CompareVersions("0.1.0-preview.10", "0.1.0-preview.2") > 0, "numeric prerelease version ordering");
Check(UpdateService.CompareVersions("v0.10.0", "0.9.9") > 0, "numeric version ordering");
var baseUrl = "https://github.com/" + UpdateService.Repository + "/releases/";
var release = new ReleaseInfo("v0.2.0", new(baseUrl + "tag/v0.2.0"), new(baseUrl + "download/v0.2.0/LouisaWiFi-Setup-x64.exe"), new(baseUrl + "download/v0.2.0/SHA256SUMS.txt"), "LouisaWiFi-Setup-x64.exe");
var bytes = Encoding.UTF8.GetBytes("test-installer-not-executable");
var hash = Convert.ToHexString(SHA256.HashData(bytes));
var downloadRoot = Path.Combine(Path.GetTempPath(), "LouisaWiFi-update-test-" + Guid.NewGuid().ToString("N"));
using var validHttp = new HttpClient(new FakeHttp(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(".txt") ? new StringContent(hash + "  LouisaWiFi-Setup-x64.exe\n") : new ByteArrayContent(bytes) }));
try
{
    var path = await new UpdateService(validHttp).DownloadAsync(release, downloadRoot, CancellationToken.None);
    Check(File.ReadAllBytes(path).SequenceEqual(bytes), "verified installer download succeeds"); File.Delete(path);
    using var tampered = new HttpClient(new FakeHttp(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(".txt") ? new StringContent(new string('0', 64) + "  LouisaWiFi-Setup-x64.exe\n") : new ByteArrayContent(bytes) }));
    try { await new UpdateService(tampered).DownloadAsync(release, downloadRoot, CancellationToken.None); throw new Exception("accepted wrong checksum"); }
    catch (InvalidDataException) { Check(!Directory.EnumerateFiles(downloadRoot).Any(), "checksum mismatch removes download and preserves installed app"); }
    try { await new UpdateService(validHttp).DownloadAsync(release with { Installer = new Uri("https://malicious.invalid/update.exe") }, downloadRoot, CancellationToken.None); throw new Exception("accepted untrusted source"); }
    catch (InvalidDataException) { Check(true, "untrusted update source rejected before HTTP"); }
    using var rateLimited = new HttpClient(new FakeHttp(_ => new(HttpStatusCode.Forbidden)));
    try { await new UpdateService(rateLimited).CheckAsync(CancellationToken.None); throw new Exception("accepted rate limit"); }
    catch (InvalidOperationException) { Check(true, "GitHub rate limit produces actionable failure"); }
    var json = JsonSerializer.Serialize(new[] { new { draft = false, prerelease = false, tag_name = "v0.2.0", html_url = release.Page.AbsoluteUri, assets = new[] { new { name = release.InstallerName, browser_download_url = release.Installer.AbsoluteUri }, new { name = "SHA256SUMS.txt", browser_download_url = release.Checksums.AbsoluteUri } } } });
    using var feed = new HttpClient(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new StringContent(json) }));
    Check((await new UpdateService(feed).CheckAsync(CancellationToken.None))?.Tag == "v0.2.0", "newer complete release selected");
}
finally { if (Directory.Exists(downloadRoot)) Directory.Delete(downloadRoot, true); }
Console.WriteLine($"{count} Windows tests passed; no Wi-Fi connection attempted");

sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); } }
