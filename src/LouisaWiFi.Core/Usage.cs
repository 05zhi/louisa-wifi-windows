namespace LouisaWiFi.Core;

public sealed record UsageSlice(string Date, string StoreId, string StoreName, double Seconds);
public sealed class UsageTracker(TimeZoneInfo zone)
{
    private DateTimeOffset? last;
    private string? storeId;
    private string storeName = "未知門市";
    public List<UsageSlice> Usage { get; } = [];
    public void Suspend() { last = null; storeId = null; }
    public void Observe(DateTimeOffset now, bool connected, string? id = null, string? name = null)
    {
        var nextId = connected ? id ?? "unknown" : null;
        if (last is { } from && storeId != null && nextId == storeId && now > from && now - from <= TimeSpan.FromSeconds(90))
        {
            while (from < now)
            {
                var local = TimeZoneInfo.ConvertTime(from, zone);
                var midnight = local.Date.AddDays(1);
                var end = new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
                if (end > now) end = now;
                var date = local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var index = Usage.FindIndex(u => u.Date == date && u.StoreId == storeId);
                var seconds = (end - from).TotalSeconds;
                if (index < 0) Usage.Add(new(date, storeId, storeName, seconds));
                else Usage[index] = Usage[index] with { Seconds = Usage[index].Seconds + seconds };
                from = end;
            }
        }
        last = connected ? now : null;
        storeId = nextId;
        storeName = name ?? "未知門市";
    }
}
