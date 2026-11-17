using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Glamaholic {
    // Lazy loading and caching of important data which requires filtering or transforming.
    // Display names use the Traditional Chinese sheets. Eorzea Collection's API is English,
    // so import matching keeps a separate English name index and still stores item IDs.
    internal class DataCache {
        public static Lazy<ImmutableList<Item>> EquippableItems { get; } =
            new(() => LoadEquippable(GameData.Sheet<Item>()));

        public static Lazy<ImmutableList<Item>> EnglishEquippableItems { get; } =
            new(() => LoadEquippable(GameData.EnglishSheet<Item>()));

        public static Lazy<ImmutableDictionary<string, byte>> EnglishStainsByName { get; } =
            new(() => {
                var sheet = GameData.EnglishSheet<Stain>();
                if (sheet == null) {
                    return ImmutableDictionary<string, byte>.Empty;
                }

                return sheet
                    .Where(static row => row.RowId != 0 && !row.Name.IsEmpty)
                    .GroupBy(static row => row.Name.ExtractText().Trim().ToLowerInvariant())
                    .ToImmutableDictionary(static group => group.Key, static group => (byte) group.First().RowId);
            });

        public static Lazy<ImmutableDictionary<byte, CachedStain>> Stains { get; } =
            new(() => {
                var stains = GameData.Sheet<Stain>();
                var transient = GameData.Sheet<StainTransient>();
                var items = GameData.Sheet<Item>();
                if (stains == null) {
                    return ImmutableDictionary<byte, CachedStain>.Empty;
                }

                return stains
                    .Where(static row => row.RowId != 0 && !row.Name.IsEmpty)
                    .ToImmutableDictionary(static row => (byte) row.RowId, row => {
                        uint[] itemIds = DyeItemIds(transient, row.RowId);
                        string itemName = "(沒有對應的染色道具)";
                        if (itemIds.Length != 0 && items != null) {
                            var dye = items.GetRowOrDefault(itemIds[0]);
                            if (dye != null) {
                                var name = dye.Value.Name.ExtractText();
                                if (name.Length > 0) {
                                    itemName = name;
                                }
                            }
                        }

                        return new CachedStain(row.Name.ExtractText(), itemIds, itemName);
                    });
            });

        public static int GetNumStainSlots(uint itemId) {
            var (id, _) = ItemUtil.GetBaseId(itemId);
            return GameData.Sheet<Item>()?.GetRowOrDefault(id)?.DyeCount ?? 0;
        }

        public static uint[] DyeItemIds(byte stainId) {
            return DyeItemIds(GameData.Sheet<StainTransient>(), stainId);
        }

        // Pure White / Jet Black / Pastel Pink. Official TC names come from the Stain sheet (101 無瑕白, 102 煤玉黑, 103 柔彩粉).
        public static HashSet<byte> ValuableStains { get; } = [101, 102, 103];

        private static ImmutableList<Item> LoadEquippable(Lumina.Excel.ExcelSheet<Item>? sheet) {
            if (sheet == null) {
                return ImmutableList<Item>.Empty;
            }

            return sheet
                .Where(row => row.EquipSlotCategory.RowId != 0 &&
                       row.EquipSlotCategory.Value.SoulCrystal == 0)
                .ToImmutableList();
        }

        private static uint[] DyeItemIds(Lumina.Excel.ExcelSheet<StainTransient>? transient, uint stainId) {
            if (transient == null || !transient.TryGetRow(stainId, out var row)) {
                return [];
            }

            var ids = new List<uint>(2);
            if (row.Item1.RowId != 0) {
                ids.Add(row.Item1.RowId);
            }

            if (row.Item2.RowId != 0) {
                ids.Add(row.Item2.RowId);
            }

            return ids.ToArray();
        }
    }

    internal class CachedStain {
        public string StainName { get; }
        public uint[] ItemIds { get; }
        public string ItemName { get; }

        public CachedStain(string stainName, uint[] itemIds, string itemName) {
            StainName = stainName;
            ItemIds = itemIds;
            ItemName = itemName;
        }
    }
}
