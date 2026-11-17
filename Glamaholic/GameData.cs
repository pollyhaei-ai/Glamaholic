using Dalamud.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Glamaholic {
    /// <summary>
    /// Official names come from Lumina row IDs. ChineseTraditional (cht) is preferred,
    /// then yanmucorp's TraditionalChinese, then the running client language.
    /// </summary>
    internal static class GameData {
        // Addon rows 738-750 are the equipment slot labels (EN: Main Hand ... Left Ring).
        // Waist (743) is skipped because glamour plates have no belt slot.
        // Confirmed against xivapi en/Addon.csv and thewakingsands ffxiv-datamining-tc Addon.csv.
        private static ClientLanguage? _chosen;

        private static readonly uint[] SlotAddonRows = [
            738, // 主手
            739, // 副手
            740, // 頭部
            741, // 身體
            742, // 手臂
            744, // 腿部
            745, // 腳部
            746, // 耳部
            747, // 頸部
            748, // 腕部
            749, // 右指
            750, // 左指
        ];

        internal static ExcelSheet<T>? Sheet<T>() where T : struct, IExcelRow<T> {
            var sheet = TryGetSheet<T>(ChosenLanguage());
            if (sheet != null) {
                return sheet;
            }

            try {
                return Service.DataManager.GetExcelSheet<T>();
            } catch (Exception ex) {
                Service.Log.Debug(ex, "GetExcelSheet<{0}> failed", typeof(T).Name);
                return null;
            }
        }

        // Pick the first language whose Item 10032 name is actually Traditional Chinese.
        // yanmucorp exposes both ChineseTraditional and TraditionalChinese; an empty sheet
        // must not win just because it was requested first.
        private static ClientLanguage ChosenLanguage() {
            if (_chosen != null) {
                return _chosen.Value;
            }

            foreach (var language in NameLanguages()) {
                var probe = TryGetSheet<Item>(language)?.GetRowOrDefault(10032)?.Name.ExtractText() ?? string.Empty;
                if (ContainsCjk(probe)) {
                    _chosen = language;
                    return language;
                }
            }

            try {
                return Service.ClientState.ClientLanguage;
            } catch (Exception) {
                return ClientLanguage.ChineseTraditional;
            }
        }

        private static bool ContainsCjk(string text) {
            foreach (var c in text) {
                if (c >= 0x4E00 && c <= 0x9FFF) {
                    return true;
                }
            }

            return false;
        }

        internal static ExcelSheet<T>? EnglishSheet<T>() where T : struct, IExcelRow<T> {
            return TryGetSheet<T>(ClientLanguage.English);
        }

        internal static string SlotName(PlateSlot slot) {
            var index = (int) slot;
            if ((uint) index >= SlotAddonRows.Length) {
                return $"#{index}";
            }

            var rowId = SlotAddonRows[index];
            string? latin = null;
            foreach (var language in new[] { ChosenLanguage() }.Concat(NameLanguages())) {
                var sheet = TryGetSheet<Addon>(language);
                if (sheet == null || !sheet.TryGetRow(rowId, out var row)) {
                    continue;
                }

                var text = row.Text.ExtractText();
                if (text.Length == 0) {
                    continue;
                }

                if (ContainsCjk(text)) {
                    return text;
                }

                latin ??= text;
            }

            return latin ?? $"#{rowId}";
        }

        private static IEnumerable<ClientLanguage> NameLanguages() {
            yield return ClientLanguage.ChineseTraditional;
            yield return ClientLanguage.TraditionalChinese;

            ClientLanguage client;
            try {
                client = Service.ClientState.ClientLanguage;
            } catch (Exception) {
                yield break;
            }

            if (client != ClientLanguage.ChineseTraditional && client != ClientLanguage.TraditionalChinese) {
                yield return client;
            }
        }

        private static ExcelSheet<T>? TryGetSheet<T>(ClientLanguage language) where T : struct, IExcelRow<T> {
            try {
                return Service.DataManager.GetExcelSheet<T>(language);
            } catch (Exception ex) {
                Service.Log.Debug(ex, "GetExcelSheet<{0}>({1}) failed", typeof(T).Name, language);
                return null;
            }
        }
    }
}
