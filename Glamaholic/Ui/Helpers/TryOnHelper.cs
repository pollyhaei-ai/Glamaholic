using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Glamaholic.Ui.Helpers {
    internal class TryOnHelper {
        private const string PlateName = "試衣間";

        private PluginUi Ui { get; }
        private string _nameInput = PlateName;

        internal TryOnHelper(PluginUi ui) {
            this.Ui = ui;
        }

        internal unsafe void Draw() {
            if (!this.Ui.Plugin.Config.ShowTryOnMenu) {
                return;
            }

            var tryOnAddon = Service.GameGui.GetAddonByName("Tryon", 1);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (tryOnAddon.IsNull || !tryOnAddon.IsVisible) {
                return;
            }

            var right = Service.Interface.InstalledPlugins.Any(state => state.InternalName == "ItemSearchPlugin");
            HelperUtil.DrawHelper(tryOnAddon, "glamaholic-helper-try-on", right, this.DrawDropdown);
        }

        private void DrawDropdown() {
            if (ImGui.Selectable($"開啟 {Plugin.Name}")) {
                this.Ui.OpenMainInterface();
            }

            if (ImGui.IsWindowAppearing()) {
                this._nameInput = PlateName;
            }

            if (HelperUtil.DrawCreatePlateMenu(this.Ui, GetTryOnItems, ref this._nameInput)) {
                this._nameInput = PlateName;
            }

            if (Interop.Glamourer.IsAvailable()) {
                if (ImGui.Selectable("用 Glamourer 試穿")) {
                    this.TryOnGlamourer(false);
                }

                if (ImGui.Selectable("用 Glamourer 試穿（填滿空欄）")) {
                    this.TryOnGlamourer(true);
                }
            }
        }

        private void TryOnGlamourer(bool withEmperor) {
            if (Service.ObjectTable.LocalPlayer == null) return;
            var plate = new SavedPlate("試穿") {
                Items = GetTryOnItems(),
                FillWithNewEmperor = withEmperor
            };

            Configuration.SanitisePlate(plate);
            Interop.Glamourer.TryOn(Service.ObjectTable.LocalPlayer.ObjectIndex, plate);
        }

        private static Dictionary<PlateSlot, SavedGlamourItem> GetTryOnItems() {
            return ReadTryOnPreview() ?? new Dictionary<PlateSlot, SavedGlamourItem>();
        }

        public void TryOnPlate(SavedPlate plate) {
            var count = 0;
            foreach (var slot in Enum.GetValues<PlateSlot>()) {
                if (plate.Items.TryGetValue(slot, out var item) && item.ItemId != 0) {
                    count++;
                } else if (plate.FillWithNewEmperor && Util.GetEmperorItemForSlot(slot) != 0) {
                    count++;
                }
            }

            this.Ui.Plugin.Functions.NotifyUnconfirmedTryOnSave(count);

            foreach (var slot in Enum.GetValues<PlateSlot>()) {
                if (!plate.Items.TryGetValue(slot, out var item) || item.ItemId == 0) {
                    if (plate.FillWithNewEmperor) {
                        uint emperor = Util.GetEmperorItemForSlot(slot);
                        if (emperor != 0) {
                            this.Ui.Plugin.Functions.TryOn(emperor, 0, 0);
                        }
                    }

                    continue;
                }

                this.Ui.Plugin.Functions.TryOn(item.ItemId, item.Stain1, item.Stain2);
            }
        }

        // AgentTryon in yanmucorp FFXIVClientStructs 0.0.6966.0 does not declare the
        // try-on item array or the save-outfit flag. Reading CharaView.Items uses the
        // named preview slots instead of the international 0x370 / 0x366 offsets.
        private static unsafe Dictionary<PlateSlot, SavedGlamourItem>? ReadTryOnPreview() {
            var agent = AgentTryon.Instance();
            if (agent == null) {
                return null;
            }

            var items = new Dictionary<PlateSlot, SavedGlamourItem>();
            var viewItems = agent->CharaView.Items;
            var length = Math.Min(viewItems.Length, 14);
            for (var i = 0; i < length; i++) {
                if (i == 5 || i >= 13) {
                    continue;
                }

                var item = viewItems[i];
                uint itemId;
                byte stain1;
                byte stain2;
                if (item.GlamourItemId != 0) {
                    itemId = item.GlamourItemId;
                    stain1 = item.GlamourStain0Id;
                    stain2 = item.GlamourStain1Id;
                } else {
                    itemId = item.ItemId;
                    stain1 = item.Stain0Id;
                    stain2 = item.Stain1Id;
                }

                if (itemId == 0) {
                    continue;
                }

                var slot = i > 5 ? i - 1 : i;
                items[(PlateSlot) slot] = new SavedGlamourItem {
                    ItemId = itemId % Util.HqItemOffset,
                    Stain1 = stain1,
                    Stain2 = stain2,
                };
            }

            return items;
        }
    }
}