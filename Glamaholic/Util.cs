using Dalamud.Interface;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.NativeWrapper;
using Lumina.Excel.Sheets;
using System;

namespace Glamaholic {
    internal static class Util {
        internal const string PlateAddon = "MiragePrismMiragePlate";
        private const string BoxAddon = "MiragePrismPrismBox";
        private const string ArmoireAddon = "CabinetWithdraw";

        internal const uint HqItemOffset = 1_000_000; // The XIV code uses 500,000 but I'll trust this
        internal const uint ItemModifierMod = 500_000;

        private static bool IsOpen(AtkUnitBasePtr addon) {
            return !addon.IsNull && addon.IsVisible;
        }

        private static unsafe bool IsOpen(IGameGui gui, string name) {
            var addon = gui.GetAddonByName(name, 1);
            return IsOpen(addon);
        }

        internal static bool IsEditingPlate(IGameGui gui) {
            var plateOpen = IsOpen(gui, PlateAddon);
            var boxOpen = IsOpen(gui, BoxAddon);
            var armoireOpen = IsOpen(gui, ArmoireAddon);

            return plateOpen && (boxOpen || armoireOpen);
        }

        internal static bool DrawTextInput(string id, ref string input, int max = 512, string message = "按 Enter 儲存。", ImGuiInputTextFlags flags = ImGuiInputTextFlags.None) {
            ImGui.SetNextItemWidth(-1);
            var ret = ImGui.InputText($"##{id}", ref input, max, ImGuiInputTextFlags.EnterReturnsTrue | flags);

            ImGui.TextUnformatted(message);

            return ret && input.Length > 0;
        }

        internal static bool IconButton(FontAwesomeIcon icon, string? id = null, string? tooltip = null, bool small = false) {
            var label = icon.ToIconString();
            if (id != null) {
                label += $"##{id}";
            }

            ImGui.PushFont(UiBuilder.IconFont);
            var ret = small
                ? ImGui.SmallButton(label)
                : ImGui.Button(label);
            ImGui.PopFont();

            if (tooltip != null && ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(tooltip);
                ImGui.EndTooltip();
            }

            return ret;
        }

        internal static void TextUnformattedWrapped(string text) {
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }

        internal static PlateSlot? GetSlot(Item item) {
            if (!item.EquipSlotCategory.IsValid) {
                return null;
            }

            var category = item.EquipSlotCategory.Value;

            if (category.MainHand > 0) {
                return PlateSlot.MainHand;
            }

            if (category.OffHand > 0) {
                return PlateSlot.OffHand;
            }

            if (category.Head > 0) {
                return PlateSlot.Head;
            }

            if (category.Body > 0) {
                return PlateSlot.Body;
            }

            if (category.Gloves > 0) {
                return PlateSlot.Hands;
            }

            if (category.Legs > 0) {
                return PlateSlot.Legs;
            }

            if (category.Feet > 0) {
                return PlateSlot.Feet;
            }

            if (category.Ears > 0) {
                return PlateSlot.Ears;
            }

            if (category.Neck > 0) {
                return PlateSlot.Neck;
            }

            if (category.Wrists > 0) {
                return PlateSlot.Wrists;
            }

            if (category.FingerR > 0) {
                return PlateSlot.RightRing;
            }

            if (category.FingerL > 0) {
                return PlateSlot.LeftRing;
            }

            return null;
        }

        internal static bool MatchesSlot(EquipSlotCategory category, PlateSlot slot) {
            return slot switch {
                PlateSlot.MainHand => category.MainHand > 0,
                PlateSlot.OffHand => category.OffHand > 0,
                PlateSlot.Head => category.Head > 0,
                PlateSlot.Body => category.Body > 0,
                PlateSlot.Hands => category.Gloves > 0,
                PlateSlot.Legs => category.Legs > 0,
                PlateSlot.Feet => category.Feet > 0,
                PlateSlot.Ears => category.Ears > 0,
                PlateSlot.Neck => category.Neck > 0,
                PlateSlot.Wrists => category.Wrists > 0,
                PlateSlot.RightRing => category.FingerR > 0,
                PlateSlot.LeftRing => category.FingerL > 0,
                _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
            };
        }

        internal static string EmperorFillTooltip(bool forToggle) {
            var name = GameData.Sheet<Item>()?.GetRowOrDefault(10032)?.Name.ExtractText();
            var series = string.IsNullOrEmpty(name) ? "各部位對應的預設物品" : $"「{name}」同一系列的官方物品";
            if (!forToggle) {
                return $"用{series}填滿空欄。";
            }

            return $"不會改寫投影範本本身。\n\n啟用後，試穿或套用到投影台時，空欄會填入{series}。";
        }

        internal static uint GetEmperorItemForSlot(PlateSlot slot) {
            switch (slot) {
                case PlateSlot.Head:
                    return 10032;
                case PlateSlot.Body:
                    return 10033;
                case PlateSlot.Hands:
                    return 10034;
                case PlateSlot.Legs:
                    return 10035;
                case PlateSlot.Feet:
                    return 10036;
                case PlateSlot.Ears:
                    return 9293;
                case PlateSlot.Neck:
                    return 9292;
                case PlateSlot.Wrists:
                    return 9294;
                case PlateSlot.RightRing:
                case PlateSlot.LeftRing:
                    return 9295;
                default:
                    return 0;
            }
        }

        internal static bool IsItemMiddleOrCtrlClicked() {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Middle)) {
                return true;
            }

            return ImGui.GetIO().KeyCtrl && ImGui.IsItemClicked(ImGuiMouseButton.Left);
        }

        // https://github.com/ufx/GarlandTools/blob/5b2ec54dc792175a1d565fddb6c6b975b9a9ff64/Garland.Data/Hacks.cs#L89
        internal static bool IsItemSkipped(Item item) {
            if (item.RowId == 17557) {
                return false;
            }

            if (item.RowId == 22357) {
                return true;
            }

            if (item.Name.ExtractText().Length == 0) {
                return true;
            }

            // "Dated …" is the English sheet prefix for retired event items. The check stays on
            // the English row so the filter does not depend on inventing a Chinese prefix.
            var english = GameData.EnglishSheet<Item>()?.GetRowOrDefault(item.RowId);
            var englishName = english?.Name.ExtractText() ?? string.Empty;
            return englishName.StartsWith("Dated", StringComparison.Ordinal);
        }

        internal static void TextIcon(FontAwesomeIcon icon) {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(icon.ToIconString());
            ImGui.PopFont();
        }

        internal static string GetClipboardText() {
            try {
                return ImGui.GetClipboardText();
            } catch (Exception) {
                return string.Empty;
            }
        }
    }
}
