using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Glamaholic.Interop {
    /// <summary>
    /// Optional Glamourer IPC. Channel names match Glamourer.Api
    /// (Glamourer.SetItem.V3 / Glamourer.RevertState). Missing Glamourer must not
    /// throw during plugin load.
    /// </summary>
    internal class Glamourer {
        private const string SetItemLabel = "Glamourer.SetItem.V3";
        private const string RevertStateLabel = "Glamourer.RevertState";
        private const ulong ApplyEquipment = 0x02;

        private static ICallGateSubscriber<int, byte, ulong, IReadOnlyList<byte>, uint, ulong, int>? _setItem;
        private static ICallGateSubscriber<int, uint, ulong, int>? _revertState;

        private static bool Initialized { get; set; }
        private static bool Available { get; set; }

        public static void TryOn(int playerIndex, SavedPlate plate) {
            if (!IsAvailable() || _setItem == null || _revertState == null) {
                return;
            }

            try {
                Service.Framework.Run(() => {
                    _revertState.InvokeFunc(playerIndex, 0, ApplyEquipment);

                    foreach (var slot in Enum.GetValues<PlateSlot>()) {
                        if (!plate.Items.TryGetValue(slot, out var item)) {
                            if (!plate.FillWithNewEmperor) {
                                continue;
                            }

                            uint empItem = Util.GetEmperorItemForSlot(slot);
                            if (empItem != 0) {
                                _setItem.InvokeFunc(playerIndex, ConvertSlot(slot), empItem, [0, 0], 0, ApplyEquipment);
                            }

                            continue;
                        }

                        _setItem.InvokeFunc(playerIndex, ConvertSlot(slot), item.ItemId, [item.Stain1, item.Stain2], 0, ApplyEquipment);
                    }
                });
            } catch (Exception ex) {
                Service.Log.Debug(ex, "Glamourer TryOn IPC failed");
            }
        }

        public static void RevertState(int playerIndex) {
            if (!IsAvailable() || _revertState == null) {
                return;
            }

            try {
                Service.Framework.Run(() => {
                    _revertState.InvokeFunc(playerIndex, 0, ApplyEquipment);
                });
            } catch (Exception ex) {
                Service.Log.Debug(ex, "Glamourer RevertState IPC failed");
            }
        }

        // ApiEquipSlot values from Glamourer.Api. Plate slots skip the removed belt.
        private static byte ConvertSlot(PlateSlot slot) {
            return slot switch {
                PlateSlot.LeftRing => 14,
                >= (PlateSlot) 5 => (byte) ((int) slot + 2),
                _ => (byte) ((int) slot + 1),
            };
        }

        private static void TryOnCommand(string _, string args) {
            if (!IsAvailable()) {
                return;
            }

            string url = args.Trim();
            if (url.Length == 0 || !EorzeaCollection.IsEorzeaCollectionURL(url)) {
                url = Util.GetClipboardText();
                if (url.Length == 0 || !EorzeaCollection.IsEorzeaCollectionURL(url)) {
                    Service.ChatGui.PrintError("[Glamaholic] 沒有可用的 Eorzea Collection 網址（指令或剪貼簿）。");
                    return;
                }
            }

            Task.Run(async () => {
                var import = await EorzeaCollection.ImportFromURL(url);
                if (import == null) {
                    Service.ChatGui.PrintError("[Glamaholic] 無法從 Eorzea Collection 網址匯入，請看 /xllog。");
                    return;
                }

                TryOn(0, import);
            });
        }

        public static void Initialize(IDalamudPluginInterface pluginInterface) {
            if (Initialized) {
                return;
            }

            try {
                _setItem = pluginInterface.GetIpcSubscriber<int, byte, ulong, IReadOnlyList<byte>, uint, ulong, int>(SetItemLabel);
                _revertState = pluginInterface.GetIpcSubscriber<int, uint, ulong, int>(RevertStateLabel);
            } catch (Exception ex) {
                Service.Log.Warning(ex, "Glamourer IPC subscribers could not be created; Glamourer features stay disabled.");
                _setItem = null;
                _revertState = null;
            }

            Initialized = true;
            RefreshStatus(pluginInterface);
        }

        public static void RefreshStatus(IDalamudPluginInterface pluginInterface) {
            var prev = Available;
            Available = false;

            try {
                foreach (var plugin in pluginInterface.InstalledPlugins) {
                    if (plugin.InternalName == "Glamourer" || plugin.Name == "Glamourer") {
                        Available = plugin.IsLoaded;
                        break;
                    }
                }
            } catch (Exception ex) {
                Service.Log.Debug(ex, "Could not query installed plugins for Glamourer");
                Available = false;
            }

            if (prev == Available) {
                return;
            }

            if (Available && IsIPCValid()) {
                Service.CommandManager.AddHandler("/tryonglamourer", new CommandInfo(TryOnCommand) {
                    ShowInHelp = true,
                    HelpMessage = "用 Glamourer 試穿 Eorzea Collection 外觀。網址可寫在指令後面，或先複製到剪貼簿。",
                });
                return;
            }

            Service.CommandManager.RemoveHandler("/tryonglamourer");
        }

        public static bool IsAvailable() {
            return Available && IsIPCValid();
        }

        public static bool IsIPCValid() {
            try {
                return _setItem is ICallGateSubscriber setItem && setItem.HasFunction
                    && _revertState is ICallGateSubscriber revertState && revertState.HasFunction;
            } catch (Exception) {
                return false;
            }
        }
    }
}
