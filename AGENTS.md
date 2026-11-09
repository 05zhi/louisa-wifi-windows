# 專案規則

共用規則見 `D:\Codex\Projects\AGENTS.md`（Claude 與 Codex 共用，含自動 commit 與 push），先讀它。本檔只寫此專案特有的規則；衝突時以本檔為準。計畫與交接放在 `docs/plans/`。

## 專案概要

Windows 11 x64 的 WPF 應用程式：搜尋路易莎門市、嘗試電話衍生密碼連線 `LouisaCoffee`，並記住成功連過的路由器。程式在 `src/`（App、Core、Windows），測試在 `tests/`，建置與封裝腳本在 `scripts/`。push 到 `05zhi/louisa-wifi-windows` 的目前分支。

## 開始前閱讀

- `README.md`
- `docs/ARCHITECTURE.md`
- 涉及實機或網卡行為時讀 `docs/HARDWARE-TESTS.md`

## 驗證方式

- `./scripts/build.ps1`：驗證門市資料、建置並執行核心、Windows 與隔離 UI 流程測試（`dotnet test` 不會執行這些測試程式）。
- 需要產生安裝程式或 ZIP 時才執行 `./scripts/package.ps1`。

## 專案特有限制

- 自動測試不得連線或切換真實 Wi-Fi、修改登入啟動登錄或系統隱私設定；需要實機的項目列在 `docs/HARDWARE-TESTS.md`，未實測不宣稱通過。
- 不覆寫使用者既有的 Wi-Fi 連線；測試只用合成密碼與自己建立的暫存資料。
- 不提交真實密碼、完整 BSSID、定位座標或使用者資料。
- 發布 GitHub Release 只透過版本 tag 的 workflow，並經使用者明確要求。

## 備份分支（2026-11-09 整理）

- `backup/local-main-2026-11-09`：GitHub 歷史改寫前的本機 `main`。
- `backup/followup-2026-10-14`：2026-10-14 未提交的本機追加測試與文件（更新器失敗情境測試、追加驗證紀錄）。尚未併入 `main`：`origin/main` 之後已刪除 `docs/TEST-REPORT.md`，並修改過 `docs/HARDWARE-TESTS.md`。是否移植由使用者決定。
