Windows 11 x64 預覽版，提供系統匣、門市搜尋、手動／自動 Wi-Fi 連線、BSSID 記憶、加密覆寫密碼與本機使用統計。

此版修正連線時 Windows 顯示 `LouisaWiFi-…` 的問題。程式改用名為 `LouisaCoffee` 的暫時設定檔，保留 Windows 裡已儲存的網路設定。

下載 `LouisaWiFi-Setup-x64.exe` 安裝，或解壓 `LouisaWiFi-win-x64.zip` 執行。內含 .NET 10 runtime。程式尚未簽章，安裝前可核對 `SHA256SUMS.txt`。

自動連線與定位預設關閉。Wi-Fi API 可能需要 Windows 精確定位與桌面應用程式權限；已連到其他 Wi-Fi 時會保留現有連線。首版僅支援 WPA2-Personal／AES。

實際門市與各種定位權限情境仍待完整實測。驗證步驟見 `docs/HARDWARE-TESTS.md`。

參考 madeyexz/louisa-wifi，保留 Ian Hsiao 的 MIT 授權；與路易莎咖啡官方無隸屬關係。
