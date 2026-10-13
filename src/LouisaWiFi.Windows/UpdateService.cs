using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LouisaWiFi.Windows;

public sealed record ReleaseInfo(string Tag, Uri Page, Uri Installer, Uri Checksums, string InstallerName);
public sealed class UpdateService
{
    public const string Repository = "05zhi/louisa-wifi-windows";
    public const string CurrentVersion = "0.1.0-preview.1";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(45), DefaultRequestHeaders = { { "User-Agent", "LouisaWiFi-Windows/" + CurrentVersion } } };
    public static int CompareVersions(string left, string right)
    {
        static (Version V, string[] Pre) Parse(string tag)
        {
            var parts = tag.TrimStart('v').Split('-', 2);
            if (!Version.TryParse(parts[0], out var v) || v.Build < 0) throw new InvalidDataException("版本格式無效");
            return (v, parts.Length == 1 ? [] : parts[1].Split('.'));
        }
        var a = Parse(left); var b = Parse(right);
        var major = a.V.CompareTo(b.V); if (major != 0) return major;
        if (a.Pre.Length == 0 || b.Pre.Length == 0) return a.Pre.Length == b.Pre.Length ? 0 : a.Pre.Length == 0 ? 1 : -1;
        for (var i = 0; i < Math.Min(a.Pre.Length, b.Pre.Length); i++)
        {
            var an = int.TryParse(a.Pre[i], out var ai); var bn = int.TryParse(b.Pre[i], out var bi);
            var diff = an && bn ? ai.CompareTo(bi) : an != bn ? an ? -1 : 1 : string.CompareOrdinal(a.Pre[i], b.Pre[i]);
            if (diff != 0) return diff;
        }
        return a.Pre.Length.CompareTo(b.Pre.Length);
    }
    private static Uri Validate(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/" + Repository + "/releases/", StringComparison.Ordinal)) throw new InvalidDataException("更新網址不受信任");
        return uri;
    }
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken token)
    {
        using var response = await Client.GetAsync("https://api.github.com/repos/" + Repository + "/releases?per_page=10", token);
        if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429) throw new InvalidOperationException("GitHub 暫時限制請求，請稍後再試");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        ReleaseInfo? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.GetProperty("draft").GetBoolean()) continue;
            if (r.GetProperty("prerelease").GetBoolean() && !CurrentVersion.Contains('-')) continue;
            var tag = r.GetProperty("tag_name").GetString()!;
            if (CompareVersions(tag, CurrentVersion) <= 0 || best != null && CompareVersions(tag, best.Tag) <= 0) continue;
            var assets = r.GetProperty("assets").EnumerateArray().ToArray();
            var installer = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "LouisaWiFi-Setup-x64.exe");
            var checksums = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "SHA256SUMS.txt");
            if (installer.ValueKind == JsonValueKind.Undefined || checksums.ValueKind == JsonValueKind.Undefined) continue;
            best = new(tag, Validate(r.GetProperty("html_url").GetString()!), Validate(installer.GetProperty("browser_download_url").GetString()!), Validate(checksums.GetProperty("browser_download_url").GetString()!), "LouisaWiFi-Setup-x64.exe");
        }
        return best;
    }
    public async Task<string> DownloadAsync(ReleaseInfo release, string directory, CancellationToken token)
    {
        Validate(release.Installer.AbsoluteUri); Validate(release.Checksums.AbsoluteUri);
        var checksums = await Client.GetStringAsync(release.Checksums, token);
        var match = Regex.Match(checksums, @"(?m)^([a-fA-F0-9]{64})\s+\*?LouisaWiFi-Setup-x64\.exe\r?$");
        if (!match.Success) throw new InvalidDataException("更新檔案缺少 SHA256 校驗碼");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using var response = await Client.GetAsync(release.Installer, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 400 * 1024 * 1024) throw new InvalidDataException("更新檔案過大");
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            await using (var stream = await response.Content.ReadAsStreamAsync(token))
            {
                var buffer = new byte[81920]; long length = 0; int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                { length += read; if (length > 400 * 1024 * 1024) throw new InvalidDataException("更新檔案過大"); await file.WriteAsync(buffer.AsMemory(0, read), token); }
            }
            await using var check = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(check, token));
            if (!hash.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新完整性檢查失敗");
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
}
