# Hardware acceptance checklist

Run as a standard Windows 11 x64 user. Use only an AP and credentials you are authorized to test. Record OS build, adapter and driver, permission settings, expected/actual behavior, and program version. Do not include passwords, exact coordinates or full BSSID in public reports.

1. With an existing Wi-Fi/hotspot connected, enable auto; verify no disconnection or profile overwrite.
2. On a controlled WPA2/AES AP named `LouisaCoffee`, disconnect existing Wi-Fi, choose a store, enter that AP's supplied password and connect. Verify actual SSID/BSSID/profile, learning and reconnect without location.
3. Enter a wrong password; confirm completion notification failure, bounded attempts and ten-minute cooldown. Confirm manual retry bypasses cooldown.
4. Place two same-SSID APs with different passwords together; verify desired BSSID behavior and only the actually verified BSSID gets learned. Confirm mismatches never mark nearby routers as one store.
5. Cancel during scan and connection, switch to another network during connection, turn auto off during attempts. Confirm no later password attempts, no takeover, and no pending reconnect after cancellation.
6. Test location allowed, allowed with unavailable/stale/inaccurate position, app access denied, system location off and policy-controlled states. Search must work offline; denied Wi-Fi calls must not keep retrying.
7. Disconnect/disable/remove adapter and stop WLAN service in a controlled machine; check distinct diagnostics. With radio off, confirm status and bounded scanning.
8. Use captive portal / no upstream internet. A successful Wi-Fi connection must stay successful and never cause more password attempts.
9. Sleep/hibernate, wake, cross local midnight, duplicate network events and close/reopen app. Verify no sleep time or duplicate sessions.
10. Launch twice; second launch activates existing window. Close window; tray remains. Exit tray; process and owned profiles close. Test login startup and path with spaces.
11. Install then upgrade with test data, enable login startup, uninstall. Verify settings survive upgrade/uninstall and Run value plus shortcuts are removed by uninstall. Test in-use process handoff.
12. Test newer stable/preview versions, offline/timeout/rate limits, missing installer, bad checksum and interrupted download. Only verified download may launch, existing version remains usable on failure.
13. Finally test in a real Louisa branch; record store name only with consent. Report success/failure and version; do not claim all branches work from one result.

Current automated results and remaining hardware items are in `TEST-REPORT.md`. Use `LouisaWiFi.exe --diagnose --output diagnostics.json` for read-only adapter diagnostics.
