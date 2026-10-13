# Validation report — 0.1.0-preview.1

Date: 2026-10-14 (Asia/Taipei). Upstream reference: `ca8f59e3708f8f8208084a0b63013be6d41cc04d`.

Environment: Microsoft Windows 11 Home x64, build `10.0.26200`; .NET SDK `10.0.401`, bundled runtime `10.0.12`; Inno Setup `6.7.3`; Python `3.14`. Intel Wi-Fi 6E AX211 160MHz is present and already connected to an existing network. Native enumeration and read-only current connection query are checked separately from active wireless testing.

| Check | Result |
|---|---|
| Release WPF/Core/Windows build | Pass, zero warnings and errors |
| Python store data tests | 4 pass; normalization, phone rules, parser, malformed data and disappearance guard |
| Core behavior tests | 13 pass; existing Wi-Fi protection, verified-only learning, legacy identity migration, wrong BSSID, cooldown/manual bypass, stale location, trigger coalescing, manual priority, cancellation, external network changes, permission retry blocking and midnight/sleep statistics |
| Windows integration tests | 16 pass; native struct sizes/offsets, profile XML escaping/manual mode, current-user DPAPI, encrypted round-trip, corruption recovery, atomic writes, scoped deletion and version ordering |
| Embedded data validation | Pass, 519 stores; 45 lack coordinates but remain searchable |
| Native WLAN client / adapter enumeration | Pass, AX211 reported connected; no network change made |
| Framework-dependent WPF launch/render/exit | Pass, isolated `--smoke` exits 0; all store columns rendered; screenshot reviewed |
| Read-only native current-connection query | Pass, one existing Wi-Fi connection; no identifiers exported |
| Self-contained ZIP and installer | Pass, bundled application launches and exits 0 without system .NET 10 |
| Installer install/upgrade/uninstall | Pass in an isolated workspace installation; install/reinstall exit 0, unrelated sentinel preserved, uninstall exit 0, executable/uninstall registry/startup value/shortcuts removed |
| GitHub CI/Release | To be recorded after initial publication |

The test programs are executable assertion suites, run by `scripts/build.ps1`; `dotnet test` alone does not run them. They do not connect to an AP, edit the login-startup registry or modify system privacy settings. DPAPI tests use unique temporary directories and clear those directories afterward.

## Remaining physical tests

No controlled AP or known test credentials are available in this session. No disconnection, password attempts, permission-setting changes or sleep/hibernate tests were performed on the user's connected network. The following remain unverified on hardware:

- Successful and wrong-password Native Wi-Fi connection notification sequences, desired BSSID enforcement, cancellation of pending association and external network switches.
- Location allowed/denied/system-disabled/policy matrix; freshness/accuracy behavior under actual location signals.
- Two same-SSID APs with different credentials, radio-off/no-adapter paths and actual captive portals.
- Physical sleep/hibernate/resume and real midnight transitions; core timing behavior is covered by tests.
- Login startup across an actual Windows sign-in, update to a future version, and installation while the old tray process is still running.
- Actual Louisa branch results; Windows 10 and ARM64.

Use [HARDWARE-TESTS.md](HARDWARE-TESTS.md) to record these. This is a preview release, not a claim of full real-world Wi-Fi validation.

## Milestones

| Milestone | State |
|---|---|
| M0 source/license/architecture | Complete |
| M1 Native Wi-Fi prototype | Implemented; adapter enumeration and read-only state verified; controlled AP acceptance pending |
| M2 coordinator | Implemented and core behavior tested; active wireless tests pending |
| M3 desktop/storage/statistics | Implemented, build/render/DPAPI/storage tested; real sign-in and power transitions pending |
| M4 packaging/update/CI | Implemented; local install/reinstall/uninstall verified; future-version update and hosted CI verification pending |
| M5 GitHub delivery | Pending initial publish |
