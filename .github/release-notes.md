Windows 11 x64 預覽版，提供門市搜尋、手動／自動 Wi-Fi 連線、系統匣與本機連線統計。

此版修正部分門市連線失敗的問題：

- 04 區碼搭配八位電話的門市，優先嘗試不含區碼的八位電話。
- 連線逾時後等待 Windows 結束上一輪，減少舊通知干擾下一輪重試。
- 顯示嘗試進度與 Windows 失敗代碼，方便判斷連線問題。

已通過自動測試與單一實際門市的連線驗證，其他網卡及門市仍可能有差異。網路名稱維持 LouisaCoffee，使用暫時設定檔，保留 Windows 已儲存的網路。

下載 LouisaWiFi-Setup-x64.exe 安裝，或解壓 LouisaWiFi-win-x64.zip 執行。更新前請從系統匣選單選擇「結束」以關閉舊版。內含 .NET 10 runtime，程式尚未簽章，可用 SHA256SUMS.txt 核對檔案。

自動連線與定位預設關閉。已連到 Wi-Fi 時會保留現有連線。支援 WPA2-Personal／AES；店內密碼與電話不同時，請使用自訂密碼。

參考 madeyexz/louisa-wifi，保留 Ian Hsiao 的 MIT 授權；與路易莎咖啡官方無隸屬關係。
