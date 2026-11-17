# Glamaholic（台灣伺服器 API 13 移植）

版本 **1.15.2-tw**。顯示名稱維持 Glamaholic，以免和「投影台整理助手」（DresserAnalyze）混淆。內部名稱仍是 `Glamaholic`，指令仍是 `/glamaholic`。

本倉庫移植自 [caitlyn-gg/Glamaholic](https://github.com/caitlyn-gg/Glamaholic)（與 goatcorp D17 stable 的 `7ba1ae37416b30252c89cdcd019a3cf2f5b87e88` 相同）。原作者是 [Anna](https://git.anna.lgbt/anna/Glamaholic)，目前上游維護者是 Caitlyn。授權為 EUPL-1.2，見 `LICENCE`。本移植不主張原作著作權。

針對 XIVTCLauncher 與 yanmucorp Dalamud API 13（`Dalamud.NET.Sdk` 13.1.0、`net9.0-windows7.0`）。請用 yanmucorp Dalamud `13.0.0.16`（release `26-10-03-01`）組建，不要用 goatcorp 的國際服 Dalamud。介面為繁體中文。Dalamud 介面語言 `tw` 對應 zh-Hant。物品、染色、職業與裝備部位名稱在遊戲中從 Lumina 資料表讀取，優先 `ChineseTraditional`，其次 yanmucorp 的 `TraditionalChinese`。

## 安裝

把發行壓縮檔裡的 `Glamaholic` 資料夾放到：

`%AppData%\FFXIVSimpleLauncher\Dalamud\Config\devPlugins\Glamaholic\`

資料夾內需要有 `Glamaholic.dll` 與 `Glamaholic.json`。此版本沒有上游附帶的官方圖示，因此壓縮檔不含 `images/icon.png`。

## 遊戲內測試風險

此環境無法啟動台灣客戶端，下列行為尚未在遊戲中驗證：

- 投影台複製與套用使用 yanmucorp `FFXIVClientStructs` 0.0.6966.0 的 `AgentMiragePrismMiragePlate`、`AgentMiragePrismPrismBox` 與 `Cabinet`。函式特徵碼若與你的客戶端不符，套用會中止並顯示錯誤，不會猜測記憶體位置。
- 收藏櫃來源改查 `Cabinet` 資料表與 `IsItemInCabinet`，不再掃描未確認的 `GetCabinetItemId` 特徵碼。
- 染色道具編號來自 `StainTransient`。此 API 的 `Stain` 表沒有物品欄。
- `AgentTryon` 沒有已確認的「保存外觀」欄位或試穿物品陣列。試穿只呼叫 `AgentTryon.TryOn`。整套試穿可能只留下最後一件，需先在遊戲試衣間打開保存外觀。從試衣間建立範本時，讀的是 `CharaView.Items`，欄位順序仍需在遊戲中確認。
- 沒有 Glamourer 時，Glamourer 相關功能會停用，不應阻止外掛載入。Eorzea Collection 匯入仍用英文站點名稱對照物品編號。
