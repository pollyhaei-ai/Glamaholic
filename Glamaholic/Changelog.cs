using System;
using System.Collections.Generic;
using System.Linq;

namespace Glamaholic {
    internal static class Changelog {
        internal record ChangelogEntry(Version Version, string[] Changes);

        internal static readonly List<ChangelogEntry> Entries = [
            new ChangelogEntry(new Version(1, 12, 0, 0), [
                "資料夾可以用右鍵選單重新命名。",
                "投影範本與資料夾可以拖曳排序，已移除自動排序。",
                "改善與其他外掛的配合。",
            ]),

            new ChangelogEntry(new Version(1, 13, 0, 0), [
                "套用投影範本時，會把貴重染色納入物品選擇。",
                "-先前必須染色完全一致才算符合。現在會優先找貴重染色一致的物品，其次才是任一染色一致。",
                "已經在目前範本上、染色相同的物品不會被重染，避免浪費染色道具。",
                "投影範本應可在第一次套用時完整套上並更新。",
            ]),

            new ChangelogEntry(new Version(1, 14, 0, 0), [
                "匯出到剪貼簿的文字會附上染色名稱，方便與其他外掛對照（感謝 @SubaruYashiro）。",
                "建立投影範本時可以選擇資料夾（感謝 @firstmagic）。",
                "-「新投影範本」可以指定要放進哪個資料夾。",
                "-從剪貼簿與 Eorzea Collection 匯入時可以選擇資料夾。",
                "-試穿與檢查裝備的選單建立範本時可以選擇資料夾。",
                "-可以在清單的右鍵選單把投影範本移入資料夾。",
            ]),

            new ChangelogEntry(new Version(1, 15, 2, 0), [
                "台灣伺服器移植版本 1.15.2-tw（Dalamud API 13、.NET 9）。",
                "介面、說明與指令改為繁體中文。物品、染色、職業與部位名稱改從遊戲資料表讀取。",
                "投影台套用與複製改用此版本 FFXIVClientStructs 的投影範本代理結構。",
                "收藏櫃編號改查 Cabinet 資料表，不再使用未確認的特徵碼。",
                "試衣間的「保存外觀」位址在此 API 無法確認，因此不會寫入該記憶體。",
                "沒有安裝 Glamourer 時，相關功能會停用，不會阻止外掛載入。",
            ]),
        ];

        /// <summary>
        /// Gets all changelog entries that are newer than the specified version.
        /// </summary>
        internal static List<ChangelogEntry> GetEntriesSince(Version? lastSeenVersion) {
            if (lastSeenVersion == null) {
                return Entries;
            }

            return Entries
                .Where(e => e.Version > lastSeenVersion)
                .OrderByDescending(e => e.Version)
                .ToList();
        }

        /// <summary>
        /// Gets the latest version from the changelog.
        /// </summary>
        internal static Version? GetLatestVersion() {
            return Entries.Count > 0
                ? Entries.Max(e => e.Version)
                : null;
        }
    }
}
