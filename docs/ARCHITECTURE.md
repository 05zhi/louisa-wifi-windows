# Architecture and source map

| Upstream | Windows implementation |
|---|---|
| `Joiner.swift` | `Core/Connection.cs`, `Windows/NativeWifi.cs`: bounded single-flight coordinator, cancellations, router learning, native notifications and post-connection verification |
| `Locator.swift` | `Windows/LocationService.cs`: Windows geolocation, explicit feature toggle, access denied/disabled/unavailable states and freshness/accuracy policy |
| `MenuController.swift` | `App/MainWindow.cs`, `App/Program.cs`: WPF, Windows Forms NotifyIcon, search, city/nearby/known filters, single instance activation |
| `Tracker.swift`, `StatsView.swift` | `Core/Usage.cs`, WPF statistics: local-day splitting, suspend resets, gap bounds, daily heatmap and rankings |
| `Updater.swift` | `Windows/UpdateService.cs`: GitHub version checks, user-triggered installer download, SHA256 verification and upgrade handoff |
| `data/stores.json` | Versioned embedded offline data; stable IDs and original fields |
| `scripts/fetch_stores.py` | Standard-library Python fetch/parser with update guards and identity retention |
| Raycast | WPF search and tray; no CLI connection interface in first release |

Core targets .NET 10 and has no Windows dependency. Windows uses `wlanapi.dll`, DPAPI, current-user Run registry and Windows geolocation. App targets Windows 10 SDK 19041 API surface, while installers require Windows 11 and validation/support focus on Windows 11 x64. No service or elevated resident process.

`WlanConnect` acceptance is never success. The native adapter registers ACM notifications before calling it, waits for matching profile completion, then queries the actual connection. The coordinator repeats SSID/BSSID/profile checks before learning. A random, manual, current-user profile is created per attempt; unrelated profiles are untouched. Desired BSSID list and all native memory are released deterministically; safe handle owns WLAN client lifetime, callback remains rooted.

Auto work is coalesced; manual work cancels older work, then waits for one semaphore. Each Wi-Fi attempt is cancellable, timed and capped by an overall two-minute budget. An externally started profile or disconnection stops further attempts. Auto never competes with any existing Wi-Fi, even without internet. AP failure cooldown is in-memory ten minutes; permissions errors require user action before auto retries.

Store IDs are initially a normalized name/address SHA256 prefix. Refresh retains an old ID on exact branch match, or a unique address plus phone match after a spelling change. Legacy learned IDs can be resolved by exact normalized name/address. Ambiguous matches are never guessed. Store refreshes arrive through application releases; no runtime download can replace the embedded dataset.

State files use schema version 1, write-through temporary files and rename replacement. Learned routers and overrides are DPAPI-encrypted for the current Windows user. This protects secrets at rest from other accounts; code executing as the same user can decrypt them. Logs record only enum state, rotate at 256 KiB, and retain one prior log. Corrupt-file backups are preserved until the user clears data.

Statistics aggregate seconds per store per local date. They intentionally omit visit counts. The tracker counts only adjacent verified observations no more than ninety seconds apart; power events reset it. Sampling can undercount transitions by up to thirty seconds. Internet availability is a separate Windows system-level indicator, not password validation.
