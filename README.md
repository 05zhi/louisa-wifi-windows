# Louisa Wi-Fi for Windows

到不同的路易莎門市時，Wi-Fi 名稱通常都叫 LouisaCoffee，但密碼可能不同。這個小工具讓你在 Windows 搜尋門市、嘗試該店的電話衍生密碼，並記住成功連上的路由器，減少換店時重新設定的麻煩。

## 可以做什麼？

- 用店名、地址、電話或縣市找門市；內建資料離線也能查。
- 選好門市後連線，也可以輸入店內提供的密碼。
- 記住成功連過的路由器，下次自動優先使用。
- 選擇開啟定位，尋找附近門市。
- 在系統匣常駐，查看連線狀態與每天在門市的連線時間。
- 檢查新版本，經你同意後下載更新。

## 下載與使用

前往 [Releases](https://github.com/05zhi/louisa-wifi-windows/releases) 下載安裝程式，或下載 ZIP 解壓後執行 LouisaWiFi.exe。內含 .NET，無須另外安裝。

1. 搜尋並選擇所在門市，按「連線到選取門市」。02 區碼及 04 區碼搭配八位電話的門市，會先嘗試不含區碼的八位電話。
2. 如果店內密碼不同，勾選「使用自訂密碼」再輸入。
3. 有需要再開啟「自動連線」或「使用定位」，兩者預設關閉。
4. 關閉視窗後仍在系統匣運作；要完全關閉，請選系統匣的「結束」。

已連上其他 Wi-Fi 時，程式會保留目前連線。若要換網路，請先自行中斷。連上 Wi-Fi 後仍可能需要完成門市的網頁驗證。

## 支援與限制

目前為 Windows 11 x64 預覽版，支援 WPA2-Personal／AES。實際門市與各種定位權限情境仍待完整實測，電話衍生密碼不保證適用每家店。Windows 10、ARM64 與 WPA3-only 尚未驗證。程式尚未簽章，可使用 Release 附的 SHA256SUMS.txt 核對下載完整性。

部分 Windows Wi-Fi 功能需要定位權限，可從程式設定開啟 Windows 定位隱私設定。拒絕定位時仍可搜尋門市，但程式內連線功能可能受限。

## 隱私與移除

設定、連線記憶與統計保存在本機，不主動上傳。記住的密碼以 Windows 目前使用者的 DPAPI 加密；位置不寫入檔案。更新檢查使用本專案的 GitHub Release。

設定頁可清除本機資料。解除安裝會移除程式、捷徑與登入啟動項目，保留原有資料；如要一併清除，請先使用程式內的清除功能。ZIP 使用者移除程式前，請先關閉登入啟動。

## 開發

需要 Windows、global.json 指定的 .NET SDK、Python 3.11+ 與 PowerShell。

```powershell
./scripts/build.ps1
./scripts/package.ps1
```

第一個指令會建置並執行測試，第二個會產生安裝包與 ZIP。封裝工具會下載並驗證固定版本的 Inno Setup。門市更新流程會產生待審閱資料與測試包；維護者確認後再發布。

## 來源與授權

參考 [madeyexz/louisa-wifi](https://github.com/madeyexz/louisa-wifi) 改寫為 Windows 應用程式，沿用或改編門市資料與電話衍生密碼規則。保留原作者 Ian Hsiao 的 MIT 授權，詳見 [LICENSE](LICENSE) 與 [第三方授權說明](THIRD_PARTY_NOTICES.md)。本專案與路易莎咖啡官方無隸屬關係。
