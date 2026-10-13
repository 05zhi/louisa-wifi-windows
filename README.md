# Louisa Wi-Fi for Windows

繁體中文 Windows 系統匣工具，搜尋路易莎門市，使用內建的電話衍生密碼候選連上 `LouisaCoffee`，並記住成功連線的路由器。

**目前為 `0.1.0-preview.1` 測試版本。** 目標 Windows 11 x64。自動測試與桌面啟動已驗證；受控無線 AP、密碼錯誤、定位權限與實際門市測試仍待完成。詳見 [測試紀錄](docs/TEST-REPORT.md)。

## 下載與安裝

[前往 Releases 下載](https://github.com/05zhi/louisa-wifi-windows/releases)。預覽版請開啟對應的 prerelease。

- `LouisaWiFi-Setup-x64.exe`：目前使用者安裝，不需要系統管理員權限。
- `LouisaWiFi-win-x64.zip`：解壓後執行 `LouisaWiFi.exe`。
- `SHA256SUMS.txt`：SHA256 完整性校驗碼。

程式包含 .NET 10 runtime，無須另裝 .NET。尚未購買程式碼簽章憑證，Windows 可能顯示未識別發行者；下載後可先核對 Release 與校驗碼。首次執行會解開 .NET 原生元件到目前使用者的暫存目錄。

```powershell
Get-FileHash .\LouisaWiFi-Setup-x64.exe -Algorithm SHA256
```

## 使用

1. 開啟程式，搜尋店名、地址、電話或選取縣市，選取正確門市。
2. 按「連線到選取門市」。如已連上其他 Wi-Fi，程式會保留連線；需要換網路時，先在 Windows 中斷現有連線。
3. 門市密碼有變更時，勾選「使用自訂密碼」並輸入店內提供的密碼。可加密儲存覆寫值，空值或「移除覆寫」會刪除紀錄。
4. 視需要在設定啟用「自動連線」與「使用定位」。兩項預設皆關閉。自動連線會先嘗試成功學習的 BSSID，再選擇五公里內最近三家門市。
5. 關閉視窗後仍在系統匣執行；雙擊圖示再次開啟。真正結束請使用系統匣「結束」。再次啟動程式會顯示既有視窗。

最多處理三個 AP、十二組 AP／密碼組合，每次連線最多十八秒，整個流程最多兩分鐘。候選用完後，失敗路由器冷卻十分鐘；手動選店可以立即重試。關閉自動連線或按取消會停止後續嘗試。只學習已驗證成功的實際 BSSID。

## 定位與 Wi-Fi 權限

Windows 11 的 Wi-Fi 掃描、BSSID 與部分連線查詢可能需要精確定位。請在設定頁按「開啟定位隱私設定」，依 Windows 提示允許定位與桌面應用程式存取。程式不會修改系統隱私設定。

定位需在五分鐘內且精度一公里內才用來選店。權限被拒或系統定位關閉時，仍能離線搜尋門市與查看候選密碼；是否能在程式內連線取決於 Native Wi-Fi API 權限。Wi-Fi 權限錯誤後暫停自動嘗試，調整系統設定後請關閉再開啟自動連線，或手動重試。

## 統計與隱私

設定、路由器記憶、自訂密碼、每日各門市時間與輪替日誌存放在 `%LOCALAPPDATA%\LouisaWiFi\`。路由器與覆寫密碼使用目前 Windows 使用者的 DPAPI 保護。採版本化格式與原子寫入；損壞資料會保留 `.corrupt-*` 備份並還原預設。

每三十秒觀察已連線狀態；只計確認連上 `LouisaCoffee` 的時間。睡眠／休眠會重設計時，超過九十秒的觀察間隔不補算；跨午夜按本機日期拆分。每次切換或中斷可能少計至多一個觀察週期。未知路由器歸入「未知門市」。統計支援年份、月份、門市篩選、每日方格與門市排行，沒有宣稱造訪次數。

位置不存入檔案；設定、位置與連線紀錄不主動上傳。日誌只記錄狀態代碼，不記錄密碼、座標或 BSSID。更新檢查／下載只連到本專案 GitHub Release。網際網路狀態取自 Windows，不主動傳送探測請求；系統整體網際網路狀態不代表目前 Wi-Fi 必然可上網。

設定頁可清除程式擁有的本機資料；如果有連線流程進行中，先取消並等候結束。

## 更新與解除安裝

系統匣或設定頁可「檢查更新」。找到新版本後，經使用者點選才下載安裝程式，核對 SHA256，啟動安裝並結束舊版本。下載失敗會保留目前程式。更新來源固定為本 repository，預覽版接受較新的預覽版或正式版。

安裝升級保留 `%LOCALAPPDATA%\LouisaWiFi\`。從 Windows「已安裝的應用程式」解除安裝會移除程式、捷徑與登入啟動項目；本機資料保留，若需刪除請先使用程式的清除功能。ZIP 使用者移除解壓目錄即可；若啟用過登入啟動，移除前先關閉該設定。

## 已知限制

- 首版只支援 `LouisaCoffee`、WPA2-Personal／AES。WPA3-only、WEP、企業驗證與開放網路不在支援範圍。
- 未在實際路易莎門市驗證，請回報結果；內建電話候選不保證適用每家門市。
- 同名 AP 的指定 BSSID 依賴無線網卡驅動；程式會再次驗證結果，BSSID 不符不會學習。
- Windows 10、ARM64 尚未驗證；安裝程式要求 Windows 11。只有 x64 發布產物。
- 自動連線保留任何現有 Wi-Fi，包含已連上但尚需驗證頁的網路；不會用上網探測失敗來判定密碼錯誤。
- 程式只建立隨機名稱的 `LouisaWiFi-*` 手動 profile，正常退出會清理本次建立的 profile。異常終止可能留下該 profile，可在 Windows 管理已知網路中自行刪除；不覆寫使用者的 `LouisaCoffee` profile。

## 建置與驗證

需要 Windows、[.NET SDK 10.0.401](https://dotnet.microsoft.com/download/dotnet/10.0)、Python 3.11+ 與 PowerShell。沒有外部 Python 或測試框架套件；Windows SDK .NET 參考由 SDK 透過 NuGet 還原。

```powershell
./scripts/build.ps1
./scripts/package.ps1
```

`build.ps1` 會驗證門市資料、執行 Python 測試、編譯 WPF、執行 Core 與 Windows 測試。測試使用可直接執行的驗證程式，請使用此腳本，單獨 `dotnet test` 不會執行它們。測試不會連線、修改登錄或更改系統定位。

`package.ps1` 會先執行完整驗證，再 self-contained 發布與 Inno Setup 封裝到 `artifacts/`。若未指定 `-Iscc`，會下載固定版本 Inno Setup 6.7.3，驗證 SHA256 與數位簽章後準備編譯器。可傳入 `-DotNet`、`-Python`、`-Iscc` 指定工具完整路徑。固定 SDK 與 Inno 版本可重現建置步驟；安裝包的時間戳與壓縮不承諾位元組完全相同。

診斷只讀取 OS、網卡描述與狀態，不掃描或改變連線：

```powershell
./LouisaWiFi.exe --diagnose --output ./diagnostics.json
```

CI／Release／門市更新流程位於 `.github/workflows/`。推送 `v*` tag 時先驗證 tag 與程式版本一致，完成測試封裝後才發布。每週門市更新發出 PR，通過 CI 並合併後需調整版本與發 tag 才會產生新版下載；不會以排程直接發布未審查門市資料。空資料、超過 15% 門市消失及格式錯誤都會停止更新並保留現有資料。

```powershell
python scripts/fetch_stores.py --validate
python scripts/fetch_stores.py
```

## 來源與授權

本專案參考 [madeyexz/louisa-wifi](https://github.com/madeyexz/louisa-wifi)，重新實作為 Windows 桌面應用程式。門市資料格式、電話衍生密碼候選規則與部分功能設計沿用或改編自原專案。原專案由 Ian Hsiao 開發，採 MIT 授權。本專案為獨立衍生作品，與路易莎咖啡官方無隸屬關係。

參考版本 `ca8f59e3708f8f8208084a0b63013be6d41cc04d`。保留上游完整 [MIT 授權](LICENSE) 與 `Copyright (c) 2026 Ian Hsiao`。詳見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 與 [功能對照](docs/ARCHITECTURE.md)。
