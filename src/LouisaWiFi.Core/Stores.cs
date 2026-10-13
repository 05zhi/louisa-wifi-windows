using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LouisaWiFi.Core;

public sealed record Store(string Id, string Name, string Address, string Phone, double Lat, double Lng, string[] Passwords)
{
    public string City => Regex.Match(StoreCatalog.Normalize(Address), @"^.{2,3}?[縣市]").Value;
    public bool HasLocation => Lat is > 21 and < 26.5 && Lng is > 118 and < 122.5;
}
public sealed record Position(double Lat, double Lng, double Accuracy, DateTimeOffset Timestamp)
{
    public bool IsUsable(DateTimeOffset now) => double.IsFinite(Lat) && double.IsFinite(Lng) &&
        Lat is >= -90 and <= 90 && Lng is >= -180 and <= 180 && Accuracy is >= 0 and <= 1000 &&
        now - Timestamp >= TimeSpan.Zero && now - Timestamp <= TimeSpan.FromMinutes(5);
}
public sealed record StoreDataset(int SchemaVersion, string Version, Store[] Stores);
public sealed class StoreCatalog(StoreDataset dataset)
{
    public StoreDataset Dataset { get; } = dataset;
    public IReadOnlyList<Store> Stores => Dataset.Stores;
    public static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC).Replace('臺', '台'), @"\s+", "").ToUpperInvariant();
    public static string StableId(string name, string address) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(name) + "|" + Normalize(address))))[..20].ToLowerInvariant();
    public IEnumerable<Store> Search(string query, string? city = null) => Stores.Where(s =>
        (string.IsNullOrEmpty(city) || s.City == city) && Normalize(s.Name + s.Address + s.Phone).Contains(Normalize(query)));
    public IEnumerable<Store> Nearest(Position p, int limit = 3) => Stores.Where(s => s.HasLocation)
        .Select(s => (Store: s, Km: Distance(p.Lat, p.Lng, s.Lat, s.Lng)))
        .Where(x => x.Km <= 5).OrderBy(x => x.Km).Take(limit).Select(x => x.Store);
    public static double Distance(double a, double b, double c, double d)
    {
        static double R(double v) => v * Math.PI / 180;
        var h = Math.Pow(Math.Sin(R(c - a) / 2), 2) + Math.Cos(R(a)) * Math.Cos(R(c)) * Math.Pow(Math.Sin(R(d - b) / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
    }
    public static StoreCatalog Load(string json)
    {
        var data = JsonSerializer.Deserialize<StoreDataset>(json, JsonDefaults.Options) ?? throw new InvalidDataException("門市資料為空");
        if (data.SchemaVersion != 1 || string.IsNullOrWhiteSpace(data.Version) || data.Stores is not { Length: > 0 }) throw new InvalidDataException("不支援的門市資料版本");
        var ids = new HashSet<string>();
        foreach (var s in data.Stores)
            if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Name) || s.Address is null || s.Phone is null || !ids.Add(s.Id) ||
                !double.IsFinite(s.Lat) || !double.IsFinite(s.Lng) || !(s.HasLocation || s.Lat == 0 && s.Lng == 0) ||
                s.Passwords is null || s.Passwords.Any(p => !PasswordRules.IsValid(p))) throw new InvalidDataException("門市資料驗證失敗");
        return new(data);
    }
}
public static class PasswordRules
{
    public static bool IsValid(string? password) => password is { Length: >= 8 and <= 63 } && password.All(c => c is >= ' ' and <= '~');
    public static string[] FromPhone(string phone)
    {
        var first = Regex.Split(phone, @"[#轉,、/]|分機|ext", RegexOptions.IgnoreCase)[0];
        var digits = Regex.Replace(first, @"[^0-9]", "");
        if (digits.Length == 0) return [];
        string[] candidates = digits.StartsWith("02", StringComparison.Ordinal)
            ? [digits[2..], digits, digits[1..]] : [digits[1..], digits, digits[Math.Max(0, digits.Length - 8)..]];
        return candidates.Where(IsValid).Distinct(StringComparer.Ordinal).ToArray();
    }
}
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
}
