using LouisaWiFi.Core;
using Windows.Devices.Geolocation;

namespace LouisaWiFi.Windows;

public enum LocationState { NotEnabled, Available, Unavailable, Denied, Disabled }
public sealed class LocationService(Func<bool> enabled) : ILocation
{
    public LocationState State { get; private set; } = LocationState.NotEnabled;
    public Position? Last { get; private set; }
    public async Task<Position?> LocateAsync(CancellationToken token)
    {
        if (!enabled()) { State = LocationState.NotEnabled; Last = null; return null; }
        try
        {
            var locator = new Geolocator { DesiredAccuracyInMeters = 200 };
            if (locator.LocationStatus == PositionStatus.Disabled) { State = LocationState.Disabled; Last = null; return null; }
            var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(8)).AsTask(token);
            var p = position.Coordinate;
            Last = new(p.Point.Position.Latitude, p.Point.Position.Longitude, p.Accuracy, p.Timestamp);
            State = Last.IsUsable(DateTimeOffset.UtcNow) ? LocationState.Available : LocationState.Unavailable;
            return State == LocationState.Available ? Last : null;
        }
        catch (UnauthorizedAccessException) { State = LocationState.Denied; Last = null; return null; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TimeoutException) { State = LocationState.Unavailable; Last = null; return null; }
    }
}
