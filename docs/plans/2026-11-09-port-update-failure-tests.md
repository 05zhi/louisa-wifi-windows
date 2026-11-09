---
status: ready
planner: claude
executor: codex
branch: main
created: 2026-11-09
---

# 移植 2026-10-14 的更新器失敗情境測試

## 目標

把備份分支 `backup/followup-2026-10-14` 裡的 8 個更新器失敗情境自動測試移植到目前的 `main`，讓 `scripts/build.ps1` 涵蓋這些情境。只移植測試，不還原內部驗證報告。

## 範圍

- 做：
  - 在 `tests/LouisaWiFi.Windows.Tests/Program.cs` 加入 8 個測試與輔助類別 `InterruptedStream`。
  - 在 `docs/HARDWARE-TESTS.md` 第 12 項補一句：哪些情境已有自動測試、哪些仍需實機。
  - 更新 `AGENTS.md` 的「備份分支」段落，記錄移植結果。
- 不做：
  - 不還原 `docs/TEST-REPORT.md`，也不移植備份分支 `docs/HARDWARE-TESTS.md` 的「Follow-up status — 2026-10-14」整段。
  - 不修改 `src/` 底下的程式碼。
  - 不 cherry-pick 或 merge 備份分支，因為這樣會把 `docs/TEST-REPORT.md` 帶回來。
  - 不刪除備份分支，除非使用者另外要求。
  - 不發布 Release、不打 tag。

## 背景與現況

- 本機 `main` 在 2026-11-09 已對齊 `origin/main`，舊內容保存在兩個只存在本機的分支：
  - `backup/local-main-2026-11-09`（`34bb53e`）：GitHub 歷史改寫前的本機 `main`。
  - `backup/followup-2026-10-14`（`1d87f46`）：以 `34bb53e` 為基礎，加上 10/14 未提交的測試與文件。
- 要移植的內容用這個指令查看：`git diff 34bb53e backup/followup-2026-10-14 -- tests/LouisaWiFi.Windows.Tests/Program.cs`
- 這個 repo 是公開的。`docs/TEST-REPORT.md` 不存在於目前 `origin/main` 的任何歷史，也就是發布前刻意移除的內部報告，因此不還原。
- 這 8 個測試對應的行為仍在 `src/LouisaWiFi.Windows/UpdateService.cs`：
  - HTTP 403/429 時丟出含「稍後再試」的 `InvalidOperationException`
  - 跳過草稿與不夠新的版本
  - 缺少校驗碼時丟出 `InvalidDataException`
  - 安裝檔超過 400 MiB 時拒絕
  - 失敗時刪除下載到一半的檔案
- `UpdateService.CurrentVersion` 已從 `0.1.0-preview.1` 改為 `0.1.0-preview.4`。備份測試的 `skippedFeed` 寫死 `v0.1.0-preview.1` 當作「目前版本」，移植時必須改用常數。
- `scripts/build.ps1` 用 `dotnet run --project tests/LouisaWiFi.Windows.Tests -c Release` 執行 Windows 測試；`dotnet test` 不會執行它。
- 此專案沒有 `docs/PROGRESS.md`，進度以本計畫的「執行紀錄」為準，不另外建立。

## 會改的檔案

- `tests/LouisaWiFi.Windows.Tests/Program.cs`：加入 8 個測試與 `InterruptedStream`。
- `docs/HARDWARE-TESTS.md`：第 12 項補一句自動測試涵蓋範圍。
- `AGENTS.md`：更新「備份分支」段落。
- `docs/plans/2026-11-09-port-update-failure-tests.md`：狀態與執行紀錄。

## 步驟

1. **準備**
   - 讀 `D:\Codex\Projects\AGENTS.md` 與專案 `AGENTS.md`。
   - 確認目前在 `main`、工作目錄乾淨，`git fetch` 後與 `origin/main` 一致；有差異時先檢查，不覆蓋他人變更。
   - 把本計畫 `status` 改為 `in-progress`。
   - 先執行一次 `./scripts/build.ps1`，在執行紀錄寫下移植前的狀態與 Windows 測試數（最後一行 `N Windows tests passed`）。
   - 驗收：基準建置通過並記錄 N。基準建置就失敗時停止，標為 `blocked` 並記錄錯誤，不修。

2. **移植測試**
   - 從上面的 diff 取出新增區塊，放在 `Program.cs` 中 `"newer complete release selected"` 那個 `Check` 之後、`finally` 之前。依序共 8 個測試：
     - 離線
     - HTTP 逾時
     - HTTP 429
     - 跳過草稿、目前版本與缺安裝檔的 release
     - 缺校驗碼
     - 下載中途取消
     - 下載中途連線中斷
     - 安裝檔過大
   - 把 `InterruptedStream` 類別加到檔案末尾、`FakeHttp` 之後。
   - 在 `skippedFeed` 中，把 prerelease 項目的 `tag_name = "v0.1.0-preview.1"` 改為 `tag_name = "v" + UpdateService.CurrentVersion`，讓測試真的驗證「跳過目前版本」；其他內容照原樣。
   - 驗收：`./scripts/build.ps1` 全部通過，Windows 測試數為 N + 8，每個新測試都有 `PASS` 行。
   - 若有測試失敗，表示正式程式的行為已經改變：不改 `src/` 去配合測試，也不刪掉失敗的測試。把失敗的測試名稱與錯誤寫進執行紀錄，狀態標為 `blocked`，等使用者決定。

3. **補充文件**
   - 在 `docs/HARDWARE-TESTS.md` 第 12 項末尾補一句英文（維持該檔語言），說明：
     - 離線、逾時、限流、缺安裝檔、缺或錯誤校驗碼、下載中斷或取消、檔案過大，已由 Windows tests 自動涵蓋。
     - 互動式更新對話框與真正較新的公開 release，仍需實機驗證。
   - 不加入日期型的驗證紀錄段落。
   - 把 `AGENTS.md`「備份分支」段落中 `backup/followup-2026-10-14` 那一項改寫為：測試已移植到 `main`（附 commit SHA）；`TEST-REPORT.md` 與追加驗證紀錄刻意不移植；分支保留到使用者決定刪除。
   - 驗收：`git diff --check` 無錯誤，連結與編碼正常。

4. **Commit 與 push**
   - 用明確路徑 stage 上述 4 個檔案，檢查 `git diff --cached` 與 `git diff --cached --check`。
   - 一個 commit，例如 `test: cover update check and download failure paths`。
   - `git fetch` 後 push 到 `origin/main`，確認遠端 SHA 與本機一致。
   - 驗收：遠端 `main` 為新 commit；工作目錄乾淨；兩個備份分支仍在本機。

5. **收尾**
   - 填寫「執行紀錄」，把 `status` 改為 `done`；這次的狀態更新可併入步驟 4 的 commit，或另開一個 `docs:` commit 再 push。

## 給執行者的 prompt

> 執行 `docs/plans/2026-11-09-port-update-failure-tests.md`。依步驟實作，每完成一個可驗收部分就 commit 並 push。完成後填寫「執行紀錄」並把 status 改為 done；受阻時改為 blocked 並寫明需要的決定。

補充注意：

- 這是只移植測試的工作。看到備份分支其他內容（`docs/TEST-REPORT.md`、Follow-up status 段落）時不要帶回 `main`。
- 新測試若失敗，不改正式程式來配合測試，記錄後停下。
- 不刪備份分支、不打 tag、不發布 Release。

## 執行紀錄

由執行者填寫。

- 執行者／日期：
- 移植前 Windows 測試數 N：
- 移植後 Windows 測試數：
- commit：
- 測試結果：
- 偏離計畫之處：
- 未完成或未驗證：
