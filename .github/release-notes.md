Windows 11 x64 預覽版，提供系統匣、門市搜尋、手動／自動 Wi-Fi 連線、BSSID 記憶、加密覆寫密碼與本機使用統計。

下載 `LouisaWiFi-Setup-x64.exe` 安裝，或解壓 `LouisaWiFi-win-x64.zip` 執行。內含 .NET 10 runtime。程式尚未簽章，安裝前可核對 `SHA256SUMS.txt`。

自動連線與定位預設關閉。Wi-Fi API 可能需要 Windows 精確定位與桌面應用程式權限；已連到其他 Wi-Fi 時會保留現有連線。首版僅支援 WPA2-Personal／AES。

建置、核心測試、Windows 結構／DPAPI／儲存測試與桌面啟動已驗證。受控 AP、同名多 AP、定位權限矩陣與實際路易莎門市測試尚未完成。請參閱 repository 的 `docs/TEST-REPORT.md` 與 `docs/HARDWARE-TESTS.md`。

參考 madeyexz/louisa-wifi，保留 Ian Hsiao 的 MIT 授權；與路易莎咖啡官方無隸屬關係。
