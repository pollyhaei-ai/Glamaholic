using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Glamaholic.Interop;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel;

namespace Glamaholic.Ui {
    internal class MainInterface {
        internal const int IconSize = 48;

        private static readonly PlateSlot[] LeftSide = {
            PlateSlot.MainHand,
            PlateSlot.Head,
            PlateSlot.Body,
            PlateSlot.Hands,
            PlateSlot.Legs,
            PlateSlot.Feet,
        };

        private static readonly PlateSlot[] RightSide = {
            PlateSlot.OffHand,
            PlateSlot.Ears,
            PlateSlot.Neck,
            PlateSlot.Wrists,
            PlateSlot.RightRing,
            PlateSlot.LeftRing,
        };

        private PluginUi Ui { get; }
        private ImmutableList<Item> FilteredItems { get; set; }

        private FilterInfo? PlateFilter { get; set; }

        private bool _visible;
        private int _dragging = -1;
        private Guid? _selectedPlateId = null;
        private bool _scrollToSelected;
        private string _plateFilter = string.Empty;
        private bool _showRename;
        private string _renameInput = string.Empty;
        private bool _deleteConfirm;
        private bool _editing;
        private SavedPlate? _editingPlate;
        private Guid? _editingPlateId = null;
        private string _itemFilter = string.Empty;
        private string _dyeFilter = string.Empty;
        private volatile bool _ecImporting;
        private readonly Dictionary<string, Stopwatch> _timedMessages = new();
        private string _tagInput = string.Empty;
        private int _editingItemDyeCount = 0;
        private DateTime _dyesCopiedTime = DateTime.MinValue;
        private bool _massImport = false;
        private string _massImportLastURL = string.Empty;
        private string _massImportMessage = string.Empty;
        private int _massImportTarget = 0;
        private string _newFolderNameInput = string.Empty;

        private Guid? _contextMenuNodeId = null;
        private bool _confirmDeleteNode = false;
        private bool _confirmDeleteFolder = false;
        private int? _pendingFolderAction = null; // 0 = delete all, 1 = move out
        private bool _showFolderRename = false;
        private string _folderRenameInput = string.Empty;

        private Guid? _dragGuid = null;
        private Guid? _newPlateTargetFolder = null;

        // Changelog state
        private bool _showChangelog = false;
        private List<Changelog.ChangelogEntry>? _changelogEntries = null;
        private bool _changelogChecked = false;

        internal MainInterface(PluginUi ui) {
            this.Ui = ui;
        }

        internal void Open() {
            this._visible = true;
        }

        internal void Toggle() {
            this._visible ^= true;
        }

        internal void Draw() {
            this.HandleTimers();

            if (this._massImport)
                this.DrawMassImportWindow();

            if (this._showChangelog)
                this.DrawChangelog();

            if (!this._visible) {
                return;
            }

            // Check for new changelog entries on first open
            if (!this._changelogChecked) {
                this._changelogChecked = true;
                this.CheckForNewChangelog();
            }

            ImGui.SetNextWindowSize(new Vector2(415, 650), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin(Plugin.Name, ref this._visible, ImGuiWindowFlags.MenuBar)) {
                ImGui.End();
                return;
            }

            this.DrawInner();

            ImGui.End();
        }

        private static bool IsValidEorzeaCollectionUrl(string urlString) {
            if (!Uri.TryCreate(urlString, UriKind.Absolute, out var url)) {
                return false;
            }

            return url.Host == "ffxiv.eorzeacollection.com" && url.AbsolutePath.StartsWith("/glamour/");
        }

        private void DrawMenuBar() {
            if (!ImGui.BeginMenuBar()) {
                return;
            }

            if (ImGui.BeginMenu("投影範本")) {
                if (ImGui.MenuItem("新增")) {
                    var id = this.Ui.Plugin.Config.AddPlate(new SavedPlate("未命名投影範本"));
                    this.Ui.Plugin.SaveConfig();
                    this.SwitchPlate(id, true);
                }

                if (ImGui.BeginMenu("從剪貼簿匯入")) {
                    SharedPlate? clipPlate = null;
                    try {
                        clipPlate = JsonConvert.DeserializeObject<SharedPlate>(Util.GetClipboardText());
                    } catch {
                        // ignored
                    }

                    if (clipPlate == null) {
                        ImGui.TextDisabled("剪貼簿裡沒有可用的投影範本資料。");
                    } else {
                        if (ImGui.MenuItem("放到最上層")) {
                            var id = this.Ui.Plugin.Config.AddPlate(clipPlate.ToPlate());
                            this.Ui.Plugin.SaveConfig();
                            this.Ui.SwitchPlate(id);
                        }

                        var folders = TreeUtils.GetAllFolders(this.Ui.Plugin.Config.Plates);
                        foreach (var (folderId, path) in folders) {
                            if (ImGui.MenuItem($"放到 {path}")) {
                                var id = this.Ui.Plugin.Config.AddPlate(clipPlate.ToPlate(), folderId);
                                this.Ui.Plugin.SaveConfig();
                                this.Ui.SwitchPlate(id);
                            }
                        }
                    }

                    ImGui.EndMenu();
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Eorzea Collection")) {
                var validUrl = IsValidEorzeaCollectionUrl(Util.GetClipboardText());
                if (ImGui.BeginMenu("從網址匯入", validUrl && !this._ecImporting)) {
                    if (ImGui.BeginMenu("新投影範本")) {
                        if (ImGui.MenuItem("放到最上層"))
                            this.ImportEorzeaCollection(Util.GetClipboardText(), ECImportTarget.NewPlate);

                        var folders = TreeUtils.GetAllFolders(this.Ui.Plugin.Config.Plates);
                        foreach (var (folderId, path) in folders) {
                            if (ImGui.MenuItem($"放到 {path}"))
                                this.ImportEorzeaCollection(Util.GetClipboardText(), ECImportTarget.NewPlate, folderId);
                        }

                        ImGui.EndMenu();
                    }

                    if (ImGui.MenuItem("試穿"))
                        this.ImportEorzeaCollection(Util.GetClipboardText(), ECImportTarget.TryOn);

                    if (Interop.Glamourer.IsAvailable() && ImGui.MenuItem("用 Glamourer 試穿"))
                        this.ImportEorzeaCollection(Util.GetClipboardText(), ECImportTarget.TryOnGlamourer);

                    ImGui.EndMenu();
                }

                if (ImGui.MenuItem("連續匯入")) {
                    _massImport = true;
                    _massImportLastURL = Util.GetClipboardText();
                    _massImportMessage = "請複製網址……";
                }

                ImGui.EndMenu();
            }

            var anyChanged = false;
            if (ImGui.BeginMenu("設定")) {
                anyChanged |= ImGui.MenuItem("顯示投影台編輯選單", "", ref this.Ui.Plugin.Config.ShowEditorMenu);
                anyChanged |= ImGui.MenuItem("顯示檢查裝備視窗選單", "", ref this.Ui.Plugin.Config.ShowExamineMenu);
                anyChanged |= ImGui.MenuItem("顯示試穿選單", "", ref this.Ui.Plugin.Config.ShowTryOnMenu);
                ImGui.Separator();
                anyChanged |= ImGui.MenuItem("顯示 Ko-fi 按鈕", "", ref this.Ui.Plugin.Config.ShowKofiButton);
                ImGui.Separator();
                anyChanged |= ImGui.MenuItem("疑難排解模式", "", ref this.Ui.Plugin.Config.TroubleshootingMode);

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("說明")) {
                foreach (var (title, content) in this.Ui.Help) {
                    if (!ImGui.BeginMenu(title)) {
                        continue;
                    }

                    ImGui.PushTextWrapPos(ImGui.CalcTextSize("0").X * 60f * ImGuiHelpers.GlobalScale);
                    ImGui.TextUnformatted(content);
                    ImGui.PopTextWrapPos();

                    ImGui.EndMenu();
                }

                ImGui.Separator();

                if (ImGui.MenuItem("檢視更新紀錄")) {
                    this._changelogEntries = Changelog.Entries
                        .OrderByDescending(e => e.Version)
                        .ToList();
                    this._showChangelog = true;
                }

                ImGui.EndMenu();
            }

            if (this.Ui.Plugin.Config.ShowKofiButton) {
                const string kofiText = "到 Ko-fi 支持 Anna";
                var kofiTextSize = ImGui.CalcTextSize(kofiText);
                ImGui.GetWindowDrawList().AddRectFilled(
                    ImGui.GetCursorScreenPos(),
                    ImGui.GetCursorScreenPos() + kofiTextSize + ImGui.GetStyle().ItemInnerSpacing * 2,
                    0xFF5B5EFF
                );
                ImGui.PushStyleColor(ImGuiCol.Text, 0xFFFFFFFF);
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0x00000000);
                if (ImGui.MenuItem(kofiText)) {
                    Process.Start(new ProcessStartInfo("https://ko-fi.com/lojewalo") {
                        UseShellExecute = true,
                    });
                }

                ImGui.PopStyleColor(2);
            }

            if (anyChanged) {
                this.Ui.Plugin.SaveConfig();
            }

            ImGui.EndMenuBar();
        }

        private enum ECImportTarget {
            NewPlate,
            TryOn,
            TryOnGlamourer,
        }

        private void ImportEorzeaCollection(string url, ECImportTarget target, Guid? targetFolderId = null) {
            if (!IsValidEorzeaCollectionUrl(url) || Service.ObjectTable.LocalPlayer == null) {
                return;
            }

            this._ecImporting = true;

            int playerIndex = Service.ObjectTable.LocalPlayer.ObjectIndex;

            Task.Run(async () => {
                var import = await EorzeaCollection.ImportFromURL(url);

                this._ecImporting = false;

                if (import == null) {
                    if (this._massImport)
                        this._massImportMessage = "匯入失敗，請再複製一個連結。";

                    return;
                }

                if (this._massImport)
                    this._massImportMessage = $"已匯入 {import.Name}";

                switch (target) {
                    case ECImportTarget.NewPlate:
                        import.Tags.Add("Eorzea Collection");
                        var id = this.Ui.Plugin.Config.AddPlate(import, targetFolderId);
                        this.Ui.Plugin.SaveConfig();
                        this.SwitchPlate(id, true);
                        break;
                    case ECImportTarget.TryOn:
                        this.Ui.TryOnHelper.TryOnPlate(import);
                        break;
                    case ECImportTarget.TryOnGlamourer:
                        Interop.Glamourer.TryOn(playerIndex, import);
                        break;
                }
            });
        }

        private void DrawPlateList() {
            if (!ImGui.BeginChild("plate list", new Vector2(205 * ImGuiHelpers.GlobalScale, 0), true)) {
                return;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##plate-filter", "搜尋…", ref this._plateFilter, 512, ImGuiInputTextFlags.AutoSelectAll)) {
                this.PlateFilter = this._plateFilter.Length == 0
                    ? null
                    : new FilterInfo(this._plateFilter);
            }

            float buttonBarHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().FramePadding.Y * 3;
            float scrollAreaHeight = ImGui.GetContentRegionAvail().Y - buttonBarHeight;
            if (scrollAreaHeight < 50f) scrollAreaHeight = 50f;

            // Drag & drop state
            Guid? pendingMoveNodeId = null;
            Guid? pendingMoveTargetId = null;
            bool pendingInsertAfter = false;
            bool pendingMoveIntoFolder = false;
            bool pendingMoveToRoot = false;

            List<(Guid nodeId, int? folderAction)> deferredActions = new();

            if (ImGui.BeginChild("plate tree", new Vector2(0, scrollAreaHeight), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                void DrawPlateNodes(List<TreeNode> nodes, int depth = 0, string parentPath = "") {
                    foreach (var node in nodes) {
                        if (node is PlateNode leaf) {
                            if (this.PlateFilter != null && !this.PlateFilter.Matches(leaf.Plate)) {
                                continue;
                            }

                            Guid? switchTo = null;
                            bool isSelected = this._selectedPlateId == node.Id;

                            // Actual selectable item
                            if (ImGui.Selectable($"{new string(' ', depth * 2)}{leaf.Name}##{node.Id}", isSelected)) {
                                switchTo = node.Id;
                            }

                            // Drag source
                            if (ImGui.BeginDragDropSource()) {
                                this._dragGuid = node.Id;
                                unsafe {
                                    ImGui.SetDragDropPayload("TREE_NODE", null, 0);
                                }
                                ImGui.Text($"正在移動「{leaf.Name}」");
                                ImGui.EndDragDropSource();
                            }

                            // Drop target
                            if (ImGui.BeginDragDropTarget()) {
                                var payload = ImGui.AcceptDragDropPayload("TREE_NODE");
                                if (_dragGuid != null && _dragGuid.Value != node.Id) {
                                    Guid draggedNodeId = _dragGuid.Value;
                                    var draggedNode = TreeUtils.FindNodeById(this.Ui.Plugin.Config.Plates, draggedNodeId);

                                    // Only allow plates to be reordered among plates
                                    if (draggedNode is PlateNode) {
                                        // Check mouse position to determine if we insert before or after
                                        var itemMin = ImGui.GetItemRectMin();
                                        var itemMax = ImGui.GetItemRectMax();
                                        var mouseY = ImGui.GetMousePos().Y;
                                        var midY = (itemMin.Y + itemMax.Y) / 2;

                                        pendingMoveNodeId = draggedNodeId;
                                        pendingMoveTargetId = node.Id;
                                        pendingInsertAfter = mouseY > midY;
                                    }
                                }
                                ImGui.EndDragDropTarget();
                            }

                            if (this._scrollToSelected && isSelected) {
                                this._scrollToSelected = false;
                                ImGui.SetScrollHereY(1f);
                            }

                            // Right-click context menu for plate node
                            if (ImGui.BeginPopupContextItem($"plate-node-context-{node.Id}")) {
                                if (ImGui.BeginMenu("移到資料夾")) {
                                    var selected = Helpers.HelperUtil.DrawFolderMenuItems(this.Ui.Plugin.Config.Plates);
                                    if (selected.HasValue) {
                                        TreeUtils.MoveNodeToFolder(this.Ui.Plugin.Config.Plates, node.Id,
                                            selected.Value == Guid.Empty ? null : selected.Value);
                                        this.Ui.Plugin.SaveConfig();
                                        ImGui.CloseCurrentPopup();
                                    }
                                    ImGui.EndMenu();
                                }

                                ImGui.Separator();

                                if (ImGui.MenuItem("刪除")) {
                                    _contextMenuNodeId = node.Id;
                                    _confirmDeleteNode = true;
                                }
                                ImGui.EndPopup();
                            }

                            if (_confirmDeleteNode && _contextMenuNodeId == node.Id) {
                                ImGui.OpenPopup($"confirm-delete-plate-{node.Id}");
                            }

                            if (ImGui.BeginPopup($"confirm-delete-plate-{node.Id}")) {
                                ImGui.TextUnformatted("要刪除這個投影範本嗎？此動作無法復原。");
                                if (ImGui.Button("刪除")) {
                                    deferredActions.Add((node.Id, null));
                                    _confirmDeleteNode = false;
                                    _contextMenuNodeId = null;
                                    ImGui.CloseCurrentPopup();
                                }

                                ImGui.SameLine();

                                if (ImGui.Button("取消")) {
                                    _confirmDeleteNode = false;
                                    _contextMenuNodeId = null;
                                    ImGui.CloseCurrentPopup();
                                }
                                ImGui.EndPopup();
                            }

                            if (switchTo != null) {
                                this.SwitchPlate(switchTo.Value);
                            }
                        } else if (node is FolderNode folder) {
                            string folderPath = parentPath + "/" + folder.Name;
                            ImGui.PushID(folderPath);

                            bool isOpen = ImGui.TreeNodeEx($"{folder.Name}");

                            // Drag source for folders
                            if (ImGui.BeginDragDropSource()) {
                                this._dragGuid = node.Id;
                                unsafe {
                                    ImGui.SetDragDropPayload("TREE_NODE", null, 0);
                                }
                                ImGui.Text($"正在移動資料夾「{folder.Name}」");
                                ImGui.EndDragDropSource();
                            }

                            // Drop target for folders
                            if (ImGui.BeginDragDropTarget()) {
                                var payload = ImGui.AcceptDragDropPayload("TREE_NODE");
                                if (_dragGuid != null && _dragGuid.Value != node.Id) {
                                    Guid draggedNodeId = _dragGuid.Value;
                                    var draggedNode = TreeUtils.FindNodeById(this.Ui.Plugin.Config.Plates, draggedNodeId);
                                    bool isValidDrop = true;

                                    // Prevent dropping a folder into itself or its descendants
                                    if (draggedNode is FolderNode) {
                                        isValidDrop = !IsDescendantOf(this.Ui.Plugin.Config.Plates, node.Id, draggedNodeId);
                                    }

                                    if (isValidDrop) {
                                        // Reorder folders or move into folder
                                        var itemMin = ImGui.GetItemRectMin();
                                        var itemMax = ImGui.GetItemRectMax();
                                        var mouseY = ImGui.GetMousePos().Y;
                                        var itemHeight = itemMax.Y - itemMin.Y;

                                        if (draggedNode is FolderNode) {
                                            // Folders can be reordered among folders
                                            var midY = (itemMin.Y + itemMax.Y) / 2;
                                            pendingMoveNodeId = draggedNodeId;
                                            pendingMoveTargetId = node.Id;
                                            pendingInsertAfter = mouseY > midY;
                                        } else {
                                            // Plates dropped on folders go into the folder
                                            pendingMoveNodeId = draggedNodeId;
                                            pendingMoveTargetId = node.Id;
                                            pendingMoveIntoFolder = true;
                                        }
                                    }
                                }
                                ImGui.EndDragDropTarget();
                            }

                            // Right-click context menu for folder node
                            if (ImGui.BeginPopupContextItem($"folder-node-context-{node.Id}")) {
                                if (ImGui.MenuItem("重新命名")) {
                                    _contextMenuNodeId = node.Id;
                                    _showFolderRename = true;
                                    _folderRenameInput = folder.Name;
                                }

                                ImGui.Separator();

                                if (ImGui.MenuItem("刪除資料夾（包含內容）")) {
                                    _contextMenuNodeId = node.Id;
                                    _pendingFolderAction = 0;
                                    _confirmDeleteFolder = true;
                                }

                                if (ImGui.MenuItem("刪除資料夾（內容移出）")) {
                                    _contextMenuNodeId = node.Id;
                                    _pendingFolderAction = 1;
                                    _confirmDeleteFolder = true;
                                }

                                ImGui.EndPopup();
                            }

                            if (_confirmDeleteFolder && _contextMenuNodeId == node.Id) {
                                ImGui.OpenPopup($"confirm-delete-folder-{node.Id}");
                            }

                            if (ImGui.BeginPopup($"confirm-delete-folder-{node.Id}")) {
                                if (_pendingFolderAction == 0) {
                                    ImGui.TextUnformatted("要刪除此資料夾及其中的全部內容嗎？此動作無法復原。");
                                } else {
                                    ImGui.TextUnformatted("要刪除此資料夾，並把內容移到外面嗎？");
                                }

                                if (ImGui.Button("刪除")) {
                                    deferredActions.Add((node.Id, _pendingFolderAction));
                                    _confirmDeleteFolder = false;
                                    _contextMenuNodeId = null;
                                    _pendingFolderAction = null;
                                    ImGui.CloseCurrentPopup();
                                }

                                ImGui.SameLine();

                                if (ImGui.Button("取消")) {
                                    _confirmDeleteFolder = false;
                                    _contextMenuNodeId = null;
                                    _pendingFolderAction = null;
                                    ImGui.CloseCurrentPopup();
                                }

                                ImGui.EndPopup();
                            }

                            // Folder rename popup
                            if (_showFolderRename && _contextMenuNodeId == node.Id) {
                                ImGui.OpenPopup($"rename-folder-{node.Id}");
                            }

                            if (ImGui.BeginPopup($"rename-folder-{node.Id}")) {
                                ImGui.TextUnformatted("資料夾名稱：");
                                ImGui.SetNextItemWidth(250);

                                if (ImGui.IsWindowAppearing()) {
                                    ImGui.SetKeyboardFocusHere();
                                }

                                ImGui.InputText("##folder-rename-input", ref _folderRenameInput, 128);

                                if (ImGui.Button("重新命名") && !string.IsNullOrWhiteSpace(_folderRenameInput)) {
                                    folder.Name = _folderRenameInput.Trim();
                                    this.Ui.Plugin.SaveConfig();
                                    _showFolderRename = false;
                                    _contextMenuNodeId = null;
                                    ImGui.CloseCurrentPopup();
                                }

                                ImGui.SameLine();

                                if (ImGui.Button("取消")) {
                                    _showFolderRename = false;
                                    _contextMenuNodeId = null;
                                    ImGui.CloseCurrentPopup();
                                }

                                ImGui.EndPopup();
                            }

                            if (isOpen) {
                                if (folder.Children.Count > 0) {
                                    DrawPlateNodes(folder.Children, depth + 1, folderPath);
                                }
                                ImGui.TreePop();
                            }

                            ImGui.PopID();
                        }
                    }
                }

                DrawPlateNodes(this.Ui.Plugin.Config.Plates);

                // Add invisible drop target for moving to root (at the end of the list)
                ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, 20));
                if (ImGui.BeginDragDropTarget()) {
                    var payload = ImGui.AcceptDragDropPayload("TREE_NODE");
                    if (_dragGuid != null) {
                        Guid draggedNodeId = _dragGuid.Value;
                        pendingMoveNodeId = draggedNodeId;
                        pendingMoveToRoot = true;
                    }
                    ImGui.EndDragDropTarget();
                }

                // Clear drag guid when mouse is released
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                    this._dragGuid = null;
                    if (this._dragging != -1) {
                        this._dragging = -1;
                        this.Ui.Plugin.SaveConfig();
                    }
                }

                ImGui.EndChild();
            }

            // Process drag & drop moves
            if (pendingMoveNodeId.HasValue) {
                if (pendingMoveToRoot) {
                    TreeUtils.MoveNodeToRoot(this.Ui.Plugin.Config.Plates, pendingMoveNodeId.Value);
                } else if (pendingMoveIntoFolder && pendingMoveTargetId.HasValue) {
                    TreeUtils.MoveNodeIntoFolder(this.Ui.Plugin.Config.Plates, pendingMoveNodeId.Value, pendingMoveTargetId.Value);
                } else if (pendingMoveTargetId.HasValue) {
                    TreeUtils.MoveNodeToPosition(this.Ui.Plugin.Config.Plates, pendingMoveNodeId.Value, pendingMoveTargetId.Value, pendingInsertAfter);
                }
                this.Ui.Plugin.SaveConfig();
            }

            // Process deferred delete actions
            if (deferredActions.Count > 0) {
                foreach (var (nodeId, folderAction) in deferredActions) {
                    if (folderAction == null) {
                        // Delete plate node
                        TreeUtils.RemoveNode(this.Ui.Plugin.Config.Plates, nodeId);

                        // If we deleted the selected plate, clear selection
                        if (_selectedPlateId == nodeId) {
                            _selectedPlateId = null;
                        }
                    } else if (folderAction == 0) {
                        // Delete folder and all contents
                        TreeUtils.DeleteFolder(this.Ui.Plugin.Config.Plates, nodeId, false);
                    } else if (folderAction == 1) {
                        // Delete folder but move contents out
                        TreeUtils.DeleteFolder(this.Ui.Plugin.Config.Plates, nodeId, true);
                    }
                }

                this.Ui.Plugin.SaveConfig();
            }

            ImGui.Separator();

            // Button bar for adding new plates or folders
            ImGui.BeginGroup();
            ImGui.Dummy(new Vector2(0, 1));

            ImGui.PushItemWidth(-1);
            if (Util.IconButton(FontAwesomeIcon.Plus, tooltip: "新投影範本")) {
                ImGui.OpenPopup("new-plate-folder-popup");
                _newPlateTargetFolder = null;
            }

            ImGui.SameLine();
            if (Util.IconButton(FontAwesomeIcon.FolderPlus, tooltip: "新資料夾")) {
                ImGui.OpenPopup("new-folder-popup");
                _newFolderNameInput = string.Empty;
            }

            // caitlyn: intention was to have right-aligned expand/collapse but it's a PITA with ImGui
            //float buttonWidth = ImGui.GetFrameHeight();
            //float spacing = ImGui.GetStyle().ItemSpacing.X * 2;
            //float totalWidth = buttonWidth * 2 + spacing;
            //float availWidth = ImGui.GetContentRegionAvail().X;
            //ImGui.SameLine(availWidth - totalWidth);

            ImGui.PopItemWidth();

            if (ImGui.BeginPopup("new-folder-popup")) {
                ImGui.TextUnformatted("資料夾名稱：");
                ImGui.SetNextItemWidth(-1);

                // Caitlyn: Dalamud API 14 broke EnterReturnsTrue. Once that's fixed, this should be restored.
                //if (ImGui.InputText("##new-folder-name", ref _newFolderName, 128, ImGuiInputTextFlags.EnterReturnsTrue)) {
                //    if (!string.IsNullOrWhiteSpace(_newFolderName)) {
                //        this.Ui.Plugin.Config.Plates.Insert(0, new FolderNode {
                //            Name = _newFolderName.Trim(),
                //            Children = new List<TreeNode>()
                //        });
                //        this.Ui.Plugin.SaveConfig();
                //        ImGui.CloseCurrentPopup();
                //    }
                //}

                ImGui.InputText("##new-folder-name", ref _newFolderNameInput, 128);

                if (ImGui.Button("建立") && !string.IsNullOrWhiteSpace(_newFolderNameInput)) {
                    this.Ui.Plugin.Config.Plates.Insert(0, new FolderNode {
                        Name = _newFolderNameInput.Trim(),
                        Children = new List<TreeNode>()
                    });
                    this.Ui.Plugin.SaveConfig();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("取消")) {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            if (ImGui.BeginPopup("new-plate-folder-popup")) {
                ImGui.TextUnformatted("資料夾：");
                ImGui.SetNextItemWidth(200);
                var folderChoice = Helpers.HelperUtil.DrawFolderCombo(
                    this.Ui.Plugin.Config.Plates, _newPlateTargetFolder, "##new-plate-folder");
                if (folderChoice.HasValue)
                    _newPlateTargetFolder = folderChoice.Value;

                if (ImGui.Button("建立")) {
                    Guid? target = (_newPlateTargetFolder.HasValue && _newPlateTargetFolder.Value != Guid.Empty)
                        ? _newPlateTargetFolder : null;
                    var id = this.Ui.Plugin.Config.AddPlate(new SavedPlate("未命名投影範本"), target);
                    this.Ui.Plugin.SaveConfig();
                    this.SwitchPlate(id, true);
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("取消")) {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            ImGui.EndGroup();
            ImGui.EndChild();
        }

        private void DrawDyePopup(string dyePopup, SavedGlamourItem mirage) {
            if (!ImGui.BeginPopup(dyePopup)) {
                return;
            }

            ImGui.PushItemWidth(-1);
            ImGui.InputText("##dye-filter", ref this._dyeFilter, 512);

            if (ImGui.IsWindowAppearing()) {
                ImGui.SetKeyboardFocusHere(-1);
            }

            ImGui.TextUnformatted("染色 1");
            ImGui.NewLine();

            if (ImGui.BeginChild("dye 1 picker", new Vector2(250, 350), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                if (ImGui.Selectable("無", mirage.Stain1 == 0)) {
                    mirage.Stain1 = 0;
                    ImGui.CloseCurrentPopup();
                }

                var filter = this._dyeFilter.ToLowerInvariant();

                foreach (var stain in GameData.Sheet<Stain>()!) {
                    if (stain.RowId == 0 || stain.Shade == 0) {
                        continue;
                    }

                    if (filter.Length > 0 && !stain.Name.ExtractText().ToLowerInvariant().Contains(filter)) {
                        continue;
                    }

                    if (ImGui.Selectable($"{stain.Name}##{stain.RowId}", mirage.Stain1 == stain.RowId)) {
                        mirage.Stain1 = (byte) stain.RowId;
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.EndChild();
            }

            if (_editingItemDyeCount == 2) {
                ImGui.NewLine();
                ImGui.Separator();
                ImGui.NewLine();

                ImGui.TextUnformatted("染色 2");
                ImGui.NewLine();

                if (ImGui.BeginChild("dye 2 picker", new Vector2(250, 350), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                    if (ImGui.Selectable("無", mirage.Stain2 == 0)) {
                        mirage.Stain2 = 0;
                        ImGui.CloseCurrentPopup();
                    }

                    var filter = this._dyeFilter.ToLowerInvariant();

                    foreach (var stain in GameData.Sheet<Stain>()!) {
                        if (stain.RowId == 0 || stain.Shade == 0) {
                            continue;
                        }

                        if (filter.Length > 0 && !stain.Name.ExtractText().ToLowerInvariant().Contains(filter)) {
                            continue;
                        }

                        if (ImGui.Selectable($"{stain.Name}##{stain.RowId}", mirage.Stain2 == stain.RowId)) {
                            mirage.Stain2 = (byte) stain.RowId;
                            ImGui.CloseCurrentPopup();
                        }
                    }

                    ImGui.EndChild();
                }
            }

            ImGui.EndPopup();
        }

        private unsafe void DrawItemPopup(string itemPopup, SavedPlate plate, PlateSlot slot) {
            if (!ImGui.BeginPopup(itemPopup)) {
                return;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##item-filter", "搜尋…", ref this._itemFilter, 512, ImGuiInputTextFlags.AutoSelectAll)) {
                this.FilterItems(slot);
            }

            if (ImGui.IsWindowAppearing()) {
                ImGui.SetKeyboardFocusHere(-1);
            }

            if (GameFunctions.DresserContents.Count > 0) {
                if (ImGui.Checkbox("只顯示收藏櫃或投影台裡的物品", ref this.Ui.Plugin.Config.ItemFilterShowObtainedOnly)) {
                    this.Ui.Plugin.SaveConfig();
                    this.FilterItems(slot);
                }

                ImGui.Separator();
            }

            if (ImGui.BeginChild("item search", new Vector2(250, 450), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                uint? id;
                if (plate.Items.TryGetValue(slot, out var slotMirage)) {
                    id = slotMirage.ItemId;
                } else {
                    id = null;
                }

                if (ImGui.Selectable("##none-keep", id == null)) {
                    plate.Items.Remove(slot);
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                Util.TextIcon(FontAwesomeIcon.Box);
                ImGui.SameLine();
                ImGui.TextUnformatted("無（保留現有）");

                if (ImGui.Selectable("##none-remove)", id == 0)) {
                    plate.Items[slot] = new SavedGlamourItem();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                Util.TextIcon(FontAwesomeIcon.Box);
                ImGui.SameLine();
                ImGui.TextUnformatted("無（清除現有）");

                var clipper = ImGui.ImGuiListClipper();

                clipper.Begin(this.FilteredItems.Count);
                while (clipper.Step()) {
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++) {
                        var item = this.FilteredItems[i];

                        if (ImGui.Selectable($"##{item.RowId}", item.RowId == id)) {
                            if (!plate.Items.ContainsKey(slot)) {
                                plate.Items[slot] = new SavedGlamourItem();
                            }

                            plate.Items[slot].ItemId = item.RowId;
                            if (item.DyeCount == 0) {
                                plate.Items[slot].Stain1 = 0;
                                plate.Items[slot].Stain2 = 0;
                            }

                            ImGui.CloseCurrentPopup();
                        }

                        if (Util.IsItemMiddleOrCtrlClicked()) {
                            this.Ui.AlternativeFinders.Add(new AlternativeFinder(this.Ui, item));
                        }

                        ImGui.SameLine();

                        var has = GameFunctions.DresserContents.Any(saved => saved.ItemId % Util.HqItemOffset == item.RowId) || this.Ui.Plugin.Functions.IsInArmoire(item.RowId);

                        if (!has) {
                            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int) ImGuiCol.TextDisabled]);
                        }

                        Util.TextIcon(FontAwesomeIcon.Box);

                        if (!has) {
                            ImGui.PopStyleColor();
                        }

                        ImGui.SameLine();

                        ImGui.TextUnformatted($"{item.Name}");
                    }
                }

                ImGui.EndChild();
            }

            ImGui.EndPopup();
        }

        private unsafe void DrawIcon(PlateSlot slot, SavedPlate plate, int iconSize, int paddingSize) {
            var drawCursor = ImGui.GetCursorScreenPos();
            var tooltip = slot.Name();
            ImGui.BeginGroup();

            plate.Items.TryGetValue(slot, out var mirage);

            var borderColour = *ImGui.GetStyleColorVec4(ImGuiCol.Border);

            // check for item
            if (mirage != null && mirage.ItemId != 0 && GameFunctions.DresserContents.Count > 0) {
                var has = GameFunctions.DresserContents.Any(saved => saved.ItemId % Util.HqItemOffset == mirage.ItemId) || this.Ui.Plugin.Functions.IsInArmoire(mirage.ItemId);
                if (!has) {
                    borderColour = ImGuiColors.DalamudYellow;
                }
            }

            ImGui.GetWindowDrawList().AddRect(drawCursor, drawCursor + new Vector2(iconSize + paddingSize), ImGui.ColorConvertFloat4ToU32(borderColour));

            var cursorBefore = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"preview {slot}", new Vector2(iconSize + paddingSize));
            var cursorAfter = ImGui.GetCursorPos();

            if (mirage != null && mirage.ItemId != 0) {
                if (GameData.Sheet<Item>()!.TryGetRow(mirage.ItemId, out var item)) {
                    tooltip += $"\n{item.Name}";

                    var icon = this.Ui.GetIcon(item.Icon);
                    if (icon != null) {
                        ImGui.SetCursorPos(cursorBefore + new Vector2(paddingSize / 2f));
                        ImGui.Image(icon.Handle, new Vector2(iconSize));
                        ImGui.SetCursorPos(cursorAfter);

                        var circleCentre = drawCursor + new Vector2(iconSize, 4 + paddingSize / 2f);
                        if (mirage.Stain1 != 0) {
                            if (GameData.Sheet<Stain>()!.TryGetRow(mirage.Stain1, out var stain)) {
                                var colour = stain.Color;
                                var abgr = 0xFF000000;
                                abgr |= (colour & 0xFF) << 16;
                                abgr |= ((colour >> 8) & 0xFF) << 8;
                                abgr |= (colour >> 16) & 0xFF;
                                ImGui.GetWindowDrawList().AddCircleFilled(circleCentre, 4, abgr);

                                tooltip += $"\n{stain.Name}";
                            } else
                                tooltip += $"\n(?)";
                        } else {
                            tooltip += $"\n（未染色）";

                            if (item.DyeCount != 0)
                                ImGui.GetWindowDrawList().AddCircle(circleCentre, 5, 0xFF000000);
                        }

                        circleCentre = drawCursor + new Vector2(iconSize, 16 + paddingSize / 2f);
                        if (mirage.Stain2 != 0) {
                            if (GameData.Sheet<Stain>()!.TryGetRow(mirage.Stain2, out var stain)) {
                                var colour = stain.Color;
                                var abgr = 0xFF000000;
                                abgr |= (colour & 0xFF) << 16;
                                abgr |= ((colour >> 8) & 0xFF) << 8;
                                abgr |= (colour >> 16) & 0xFF;
                                ImGui.GetWindowDrawList().AddCircleFilled(circleCentre, 4, abgr);

                                tooltip += $"\n{stain.Name}";
                            } else
                                tooltip += $"\n(?)";
                        } else {
                            if (item.DyeCount == 2)
                                ImGui.GetWindowDrawList().AddCircle(circleCentre, 5, 0xFF000000);
                        }

                        tooltip += "\n\n＊左鍵點擊可將名稱複製到剪貼簿";
                    }
                }
            } else if (mirage != null) {
                // remove
                ImGui.GetWindowDrawList().AddLine(
                    drawCursor + new Vector2(paddingSize / 2f),
                    drawCursor + new Vector2(paddingSize / 2f) + new Vector2(iconSize),
                    ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int) ImGuiCol.Text])
                );

                ImGui.GetWindowDrawList().AddLine(
                    drawCursor + new Vector2(paddingSize / 2f) + new Vector2(iconSize, 0),
                    drawCursor + new Vector2(paddingSize / 2f) + new Vector2(0, iconSize),
                    ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int) ImGuiCol.Text])
                );
            }

            ImGui.EndGroup();

            // fix spacing
            ImGui.SetCursorPos(cursorAfter);

            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(tooltip);
                ImGui.EndTooltip();
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !this._editing && mirage != null) {
                if (GameData.Sheet<Item>()!.TryGetRow(mirage!.ItemId, out var item)) {
                    string name = item.Name.ExtractText();
                    ImGui.SetClipboardText(name);
                    Service.ChatGui.Print($"已將「{name}」複製到剪貼簿。");
                }
            }

            var itemPopup = $"plate item edit {slot}";
            var dyePopup = $"plate item dye {slot}";
            if (this._editing && ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                ImGui.OpenPopup(itemPopup);
                this.FilterItems(slot);
            }

            if (this._editing && ImGui.IsItemClicked(ImGuiMouseButton.Right) && mirage != null) {
                bool ok = GameData.Sheet<Item>()!.TryGetRow(mirage.ItemId, out var itemData);
                var dyeable = ok && itemData.DyeCount != 0;
                if (dyeable) {
                    _editingItemDyeCount = itemData!.DyeCount;
                    ImGui.OpenPopup(dyePopup);
                }
            }

            if (mirage != null && mirage.ItemId != 0 && Util.IsItemMiddleOrCtrlClicked()) {
                if (GameData.Sheet<Item>()!.TryGetRow(mirage.ItemId, out var item)) {
                    this.Ui.AlternativeFinders.Add(new AlternativeFinder(this.Ui, item));
                }
            }

            this.DrawItemPopup(itemPopup, plate, slot);

            if (mirage != null) {
                this.DrawDyePopup(dyePopup, mirage);
            }
        }

        private void DrawPlatePreview(SavedPlate plate) {
            const int paddingSize = 12;

            if (!ImGui.BeginTable("plate item preview", 2, ImGuiTableFlags.SizingFixedFit)) {
                return;
            }

            ImGui.TableNextRow();
            foreach (var (left, right) in LeftSide.Zip(RightSide)) {
                ImGui.TableNextColumn();
                this.DrawIcon(left, plate, IconSize, paddingSize);
                ImGui.TableNextColumn();
                this.DrawIcon(right, plate, IconSize, paddingSize);
            }

            ImGui.EndTable();
        }

        private void DrawPlateButtons(PlateNode node) {
            var plate = node.Plate!;
            if (this._editing || !ImGui.BeginTable("plate buttons", 7, ImGuiTableFlags.SizingFixedFit)) {
                return;
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.Check, tooltip: "套用")) {
                if (!Util.IsEditingPlate(Service.GameGui)) {
                    this.AddTimedMessage("請先打開遊戲內的投影範本編輯畫面。");
                } else {
                    Service.Framework.RunOnFrameworkThread(() => this.Ui.Plugin.Functions.LoadPlate(plate));
                }
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.Search, tooltip: "試穿")) {
                this.Ui.TryOnHelper.TryOnPlate(plate);
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.Font, tooltip: "重新命名")) {
                this._showRename ^= true;
                this._renameInput = plate.Name;
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.PencilAlt, tooltip: "編輯")) {
                this._editing = true;
                this._editingPlate = plate.Clone();
                this._editingPlateId = node.Id;
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.ShareAltSquare, tooltip: "分享")) {
                ImGui.SetClipboardText(JsonConvert.SerializeObject(new SharedPlate(plate)));
                this.AddTimedMessage("已複製到剪貼簿。");
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.FileExport, tooltip: "匯出成文字")) {
                ImGui.SetClipboardText(ConvertToText(plate));
                this.AddTimedMessage("已複製到剪貼簿。");
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.FolderOpen, tooltip: "移到資料夾")) {
                ImGui.OpenPopup("move-to-folder-popup");
            }

            if (ImGui.BeginPopup("move-to-folder-popup")) {
                var selected = Helpers.HelperUtil.DrawFolderMenuItems(this.Ui.Plugin.Config.Plates);
                if (selected.HasValue) {
                    TreeUtils.MoveNodeToFolder(this.Ui.Plugin.Config.Plates, node.Id,
                        selected.Value == Guid.Empty ? null : selected.Value);
                    this.Ui.Plugin.SaveConfig();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            ImGui.EndTable();
        }

        private void DrawPlateTags(SavedPlate plate) {
            if (this._editing) {
                return;
            }

            if (!ImGui.CollapsingHeader($"標籤 ({plate.Tags.Count})###plate-tags")) {
                return;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##tag-input", "輸入標籤後按 Enter", ref this._tagInput, 128, ImGuiInputTextFlags.EnterReturnsTrue)) {
                if (!string.IsNullOrWhiteSpace(this._tagInput)) {
                    var tag = this._tagInput.Trim();

                    if (!plate.Tags.Contains(tag)) {
                        plate.Tags.Add(tag);
                        plate.Tags.Sort();
                        this.Ui.Plugin.SaveConfig();
                    }
                }

                this._tagInput = string.Empty;
            }

            if (ImGui.BeginChild("tag-list")) {
                var toRemove = -1;
                for (var i = 0; i < plate.Tags.Count; i++) {
                    var tag = plate.Tags[i];

                    if (Util.IconButton(FontAwesomeIcon.Times, $"remove-tag-{i}")) {
                        toRemove = i;
                    }

                    ImGui.SameLine();
                    ImGui.TextUnformatted(tag);
                }

                if (toRemove > -1) {
                    plate.Tags.RemoveAt(toRemove);
                    plate.Tags.Sort();
                    this.Ui.Plugin.SaveConfig();
                }

                ImGui.EndChild();
            }
        }

        private void DrawPlateDetail() {
            if (!ImGui.BeginChild("plate detail")) {
                return;
            }

            if (_selectedPlateId == null) {
                ImGui.EndChild();
                return;
            }

            var node = TreeUtils.FindNodeById(this.Ui.Plugin.Config.Plates, (Guid) _selectedPlateId!);
            if (node is not PlateNode selectedNode) {
                ImGui.EndChild();
                return;
            }

            var plate = _editingPlate ?? selectedNode.Plate;

            {
                ImGui.BeginGroup();
                this.DrawPlatePreview(plate);
                ImGui.EndGroup();
            }

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);

            {
                ImGui.BeginGroup();
                this.DrawRightPanel(plate);
                ImGui.EndGroup();
            }

            var renameWasVisible = this._showRename;

            this.DrawPlateButtons(selectedNode);

            foreach (var (msg, _) in this._timedMessages) {
                Util.TextUnformattedWrapped(msg);
            }

            if (this._showRename && Util.DrawTextInput("plate-rename", ref this._renameInput, flags: ImGuiInputTextFlags.AutoSelectAll)) {
                plate.Name = this._renameInput;
                this.Ui.Plugin.SaveConfig();
                this._showRename = false;
            }

            if (this._showRename && !renameWasVisible) {
                ImGui.SetKeyboardFocusHere(-1);
            }

            if (this._editing) {
                Util.TextUnformattedWrapped("左鍵點物品可編輯，右鍵可染色。");

                if (ImGui.Button("儲存") && this._editingPlate != null) {
                    selectedNode.Plate = _editingPlate.Clone();

                    TreeUtils.Sort(this.Ui.Plugin.Config.Plates);
                    this.Ui.Plugin.SaveConfig();
                    this.ResetEditing();
                }

                ImGui.SameLine();

                if (ImGui.Button("取消")) {
                    this.ResetEditing();
                }
            }

            this.DrawPlateTags(plate);

            ImGui.EndChild();
        }

        private void DrawRightPanel(SavedPlate plate) {
            // Something broke ImGui separator positioning or I'm an idiot
            // so we're just going to draw our own heading separators
            var drawList = ImGui.GetWindowDrawList();
            void SeparatorHack(int? width = null) {
                var cursorPos = ImGui.GetCursorScreenPos();
                drawList.AddLine(cursorPos + new Vector2(0, -3), cursorPos + new Vector2(width ?? ImGui.GetContentRegionAvail().X, -3), ImGui.GetColorU32(ImGuiCol.Separator));
            }

            ImGui.TextUnformatted(plate.Name);
            SeparatorHack();

            DrawDyeListLabel(plate);

            ImGui.NewLine();
            ImGui.TextUnformatted("調整");

            bool fillSlots = ImGui.Button("填滿空欄");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Util.EmperorFillTooltip(false));

            if (fillSlots)
                this.FillEmptySlots(plate);

            if (Interop.Glamourer.IsAvailable()) {
                ImGui.NewLine();
                ImGui.TextUnformatted("Glamourer");
                SeparatorHack();

                if (ImGui.Button("試穿") && Service.ObjectTable.LocalPlayer != null)
                    Interop.Glamourer.TryOn(Service.ObjectTable.LocalPlayer!.ObjectIndex, plate);

                ImGui.SameLine();

                if (ImGui.Button("還原") && Service.ObjectTable.LocalPlayer != null)
                    Interop.Glamourer.RevertState(Service.ObjectTable.LocalPlayer!.ObjectIndex);
            }

            ImGui.NewLine();
            ImGui.TextUnformatted("設定");
            SeparatorHack();

            bool newEmperor = plate.FillWithNewEmperor;
            if (ImGui.Checkbox("試穿與套用時填滿空欄", ref newEmperor)) {
                plate.FillWithNewEmperor = newEmperor;
                this.Ui.Plugin.SaveConfig();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Util.EmperorFillTooltip(true));
        }

        private void DrawDyeListLabel(SavedPlate plate) {
            bool showDyes = false;
            bool copyDyes = false;
            Util.TextIcon(FontAwesomeIcon.Mouse);
            showDyes |= ImGui.IsItemHovered();
            copyDyes |= ImGui.IsItemClicked(ImGuiMouseButton.Left);

            ImGui.SameLine();

            ImGui.TextUnformatted("游標停留可查看染色（點擊可複製）");
            showDyes |= ImGui.IsItemHovered();
            copyDyes |= ImGui.IsItemClicked(ImGuiMouseButton.Left);

            if (showDyes) {
                Dictionary<uint, (int, CachedStain)> stainItems = new();
                
                foreach (var (_, item) in plate.Items) {
                    if (item.Stain1 != 0) {
                        if (!DataCache.Stains.Value.TryGetValue(item.Stain1, out var stain))
                            continue;

                        if (stainItems.TryGetValue(stain.ItemIds[0], out var existing))
                            stainItems[stain.ItemIds[0]] = (existing.Item1 + 1, existing.Item2);
                        else
                            stainItems[stain.ItemIds[0]] = (1, stain);
                    }

                    if (item.Stain2 != 0) {
                        if (!DataCache.Stains.Value.TryGetValue(item.Stain2, out var stain))
                            continue;
                        
                        if (stainItems.TryGetValue(stain.ItemIds[0], out var existing))
                            stainItems[stain.ItemIds[0]] = (existing.Item1 + 1, existing.Item2);
                        else
                            stainItems[stain.ItemIds[0]] = (1, stain);
                    }
                }

                StringBuilder export = new();

                ImGui.BeginTooltip();

                foreach (var (stain, track) in stainItems.OrderBy(kvp => -kvp.Value.Item1)) {
                    string itemName = track.Item2.ItemName;
                    
                    string line = $"{track.Item1}x {itemName}";
                    ImGui.TextUnformatted(line);

                    if (copyDyes)
                        export.AppendLine(line);
                }

                if (copyDyes || DateTime.Now.Subtract(_dyesCopiedTime).TotalSeconds < 1.5) {
                    ImGui.NewLine();
                    ImGui.TextUnformatted("已複製到剪貼簿！");
                }

                ImGui.EndTooltip();

                if (copyDyes) {
                    ImGui.SetClipboardText(export.ToString().Substring(0, export.Length - 2));
                    _dyesCopiedTime = DateTime.Now;
                }
            }
        }

        private void DrawWarnings() {
            var warnings = new List<string>();

            if (!this.Ui.Plugin.Functions.ArmoireLoaded) {
                warnings.Add("收藏櫃尚未載入。請先打開一次收藏櫃，才能使用收藏櫃裡的物品。");
            }

            if (GameFunctions.DresserContents.Count == 0) {
                warnings.Add("投影台是空的，或尚未打開過。Glamaholic 還不知道你有哪些物品。");
            }

            if (warnings.Count == 0) {
                return;
            }

            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            var header = ImGui.CollapsingHeader($"提醒 ({warnings.Count})###warnings");
            ImGui.PopStyleColor();

            if (!header) {
                return;
            }

            for (var i = 0; i < warnings.Count; i++) {
                if (i != 0) {
                    ImGui.Separator();
                }

                Util.TextUnformattedWrapped(warnings[i]);
            }
        }

        private void DrawInner() {
            this.DrawMenuBar();

            this.DrawWarnings();

            this.DrawPlateList();

            ImGui.SameLine();

            this.DrawPlateDetail();
        }

        private void DrawMassImportWindow() {
            var displaySize = ImGui.GetIO().DisplaySize;
            var windowSize = new Vector2(500, 300);
            var windowPos = (displaySize - windowSize) / 2;

            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.Appearing);

            if (!ImGui.Begin("連續匯入", ref this._massImport)) {
                ImGui.End();
                return;
            }

            ImGui.TextUnformatted("Eorzea Collection 連續匯入");
            ImGui.Separator();

            {
                ImGui.TextUnformatted("目標：");
                ImGui.RadioButton("新投影範本", ref this._massImportTarget, 0); ImGui.SameLine();
                ImGui.RadioButton("試穿", ref this._massImportTarget, 1);

                if (Interop.Glamourer.IsAvailable()) {
                    ImGui.SameLine();
                    ImGui.RadioButton("用 Glamourer 試穿", ref this._massImportTarget, 2);

                    if (this._massImportTarget == 2) {
                        ImGui.Separator();

                        if (ImGui.Button("還原 Glamourer")) {
                            if (Service.ObjectTable.LocalPlayer != null)
                                Interop.Glamourer.RevertState(Service.ObjectTable.LocalPlayer.ObjectIndex);
                        }
                    }
                }
            }

            var target = (ECImportTarget) this._massImportTarget;

            if (!this._ecImporting) {
                var clipboard = Util.GetClipboardText().Trim();
                if (clipboard != _massImportLastURL && IsValidEorzeaCollectionUrl(clipboard)) {
                    _massImportLastURL = clipboard;
                    _massImportMessage = "匯入中……";
                    ImportEorzeaCollection(clipboard, target);
                }
            }

            ImGui.NewLine();
            ImGui.TextUnformatted("複製 Eorzea Collection 網址即可匯入。");

            ImGui.NewLine();
            if (this._ecImporting)
                ImGui.TextUnformatted("匯入中……");
            else
                ImGui.TextUnformatted(_massImportMessage);

            ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetFrameHeightWithSpacing() - ImGui.GetStyle().ItemSpacing.Y);
            if (ImGui.Button("關閉")) {
                this._massImport = false;
            }

            ImGui.End();
        }

        private void HandleTimers() {
            if (this._timedMessages.Count == 0) return;

            var keys = this._timedMessages.Keys.ToArray();
            foreach (var key in keys) {
                if (this._timedMessages[key].Elapsed > TimeSpan.FromSeconds(5)) {
                    this._timedMessages.Remove(key);
                }
            }
        }

        private void AddTimedMessage(string message) {
            var timer = new Stopwatch();
            timer.Start();
            this._timedMessages[message] = timer;
        }

        internal void SwitchPlate(Guid plateId, bool scrollTo = false) {
            this._selectedPlateId = plateId;
            this._scrollToSelected = scrollTo;
            this._renameInput = string.Empty;
            this._showRename = false;
            this._deleteConfirm = false;
            this._timedMessages.Clear();
            this.ResetEditing();
        }

        private void ResetEditing() {
            this._editing = false;
            this._editingPlate = null;
            this._editingPlateId = null;
            this._itemFilter = string.Empty;
            this._dyeFilter = string.Empty;
        }

        private void FilterItems(PlateSlot slot) {
            var filter = this._itemFilter.ToLowerInvariant();

            IEnumerable<Item> items;
            if (GameFunctions.DresserContents.Count > 0 && this.Ui.Plugin.Config.ItemFilterShowObtainedOnly) {
                var sheet = GameData.Sheet<Item>()!;
                items = GameFunctions.DresserContents
                    .Select(item => sheet.GetRowOrDefault(item.ItemId))
                    .Where(item => item != null)
                    .Cast<Item>();
            } else {
                items = DataCache.EquippableItems.Value;
            }

            this.FilteredItems = items
                .Where(item => !Util.IsItemSkipped(item))
                .Where(item => Util.MatchesSlot(item.EquipSlotCategory.Value!, slot))
                .Where(item => this._itemFilter.Length == 0 || item.Name.ExtractText().ToLowerInvariant().Contains(filter))
                .ToImmutableList();
        }

        private void FillEmptySlots(SavedPlate plate) {
            foreach (var slot in Enum.GetValues<PlateSlot>()) {
                if (plate.Items.ContainsKey(slot))
                    continue;

                uint emperor = Util.GetEmperorItemForSlot(slot);
                if (emperor == 0)
                    continue;

                plate.Items[slot] = new SavedGlamourItem {
                    ItemId = emperor,
                    Stain1 = 0,
                    Stain2 = 0,
                };
            }

            this.Ui.Plugin.SaveConfig();
        }

        private string ConvertToText(SavedPlate plate) {
            var stainSheet = GameData.Sheet<Stain>()!;

            StringBuilder sb = new();
            sb.AppendLine(plate.Name);
            sb.AppendLine("---");
            foreach (var kvp in plate.Items) {
                if (kvp.Value.ItemId == 0) {
                    continue;
                } else {
                    sb.Append(kvp.Key.ToString()).Append(": ");

                    if (!DataCache.EquippableItems.Value.TryGetFirst(i => i.RowId == kvp.Value.ItemId, out Item item)) {
                        sb.AppendLine("未知物品");
                    } else {
                        sb.Append(item.Name);

                        bool hasStain = kvp.Value.Stain1 != 0 || kvp.Value.Stain2 != 0;
                        if (!hasStain) {
                            sb.AppendLine();
                            continue;
                        }

                        sb.Append(" (");
                        if (kvp.Value.Stain1 != 0) {
                            if (!stainSheet.TryGetRow(kvp.Value.Stain1, out var stain)) {
                                sb.Append("未知染色");
                            } else {
                                sb.Append(stain.Name);
                            }
                        } else
                            sb.Append("無, ");

                        if (kvp.Value.Stain2 != 0) {
                            if (kvp.Value.Stain1 != 0)
                                sb.Append(", ");

                            if (!stainSheet.TryGetRow(kvp.Value.Stain2, out var stain)) {
                                sb.Append("未知染色");
                            } else {
                                sb.Append(stain.Name);
                            }
                        }

                        sb.AppendLine(")");
                    }
                }
            }

            return sb.ToString();
        }

        private bool IsDescendantOf(List<TreeNode> rootNodes, Guid potentialDescendantId, Guid ancestorId) {
            var ancestorNode = TreeUtils.FindNodeById(rootNodes, ancestorId);
            if (ancestorNode is not FolderNode ancestorFolder)
                return false;

            return IsDescendantOfRecursive(ancestorFolder.Children, potentialDescendantId);
        }

        private bool IsDescendantOfRecursive(List<TreeNode> nodes, Guid targetId) {
            foreach (var node in nodes) {
                if (node.Id == targetId)
                    return true;

                if (node is FolderNode folder && IsDescendantOfRecursive(folder.Children, targetId))
                    return true;
            }
            return false;
        }

        private void CheckForNewChangelog() {
            Version? lastSeen = null;
            if (!string.IsNullOrEmpty(this.Ui.Plugin.Config.LastSeenChangelogVersion)) {
                Version.TryParse(this.Ui.Plugin.Config.LastSeenChangelogVersion, out lastSeen);
            }

            this._changelogEntries = Changelog.GetEntriesSince(lastSeen);
            if (this._changelogEntries.Count > 0) {
                this._showChangelog = true;
            }
        }

        private void DrawChangelog() {
            var displaySize = ImGui.GetIO().DisplaySize;
            var windowSize = new Vector2(450, 400);
            var windowPos = (displaySize - windowSize) / 2;

            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.Appearing);

            if (!ImGui.Begin("Glamaholic 更新內容", ref this._showChangelog, ImGuiWindowFlags.NoCollapse)) {
                ImGui.End();
                return;
            }

            if (this._changelogEntries == null || this._changelogEntries.Count == 0) {
                ImGui.TextUnformatted("沒有新的變更。");
            } else {
                ImGui.TextUnformatted("自上次打開後的變更");
                ImGui.Separator();
                ImGui.NewLine();

                float footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2;
                if (ImGui.BeginChild("changelog-content", new Vector2(0, -footerHeight), false)) {
                    foreach (var entry in this._changelogEntries) {
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
                        ImGui.TextUnformatted($"版本 {entry.Version}");
                        ImGui.PopStyleColor();

                        foreach (var change in entry.Changes) {
                            int indent = change.TakeWhile(c => c == '-').Count();
                            string formatted = change.Substring(indent);
                            for (int i = 0; i < indent; i++)
                                ImGui.Indent();
                            
                            ImGui.Bullet();
                            ImGui.SameLine();
                            Util.TextUnformattedWrapped(formatted);
                            
                            for (int i = 0; i < indent; i++)
                                ImGui.Unindent();
                        }

                        ImGui.NewLine();
                    }

                    ImGui.EndChild();
                }
            }

            ImGui.Separator();
            if (ImGui.Button("關閉")) {
                this.CloseChangelog();
            }

            ImGui.End();
        }

        private void CloseChangelog() {
            this._showChangelog = false;

            var latestVersion = Changelog.GetLatestVersion();
            if (latestVersion != null) {
                this.Ui.Plugin.Config.LastSeenChangelogVersion = latestVersion.ToString();
                this.Ui.Plugin.SaveConfig();
            }
        }
    }
}
