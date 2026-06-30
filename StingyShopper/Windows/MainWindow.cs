using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using StingyShopper.Services;

namespace StingyShopper.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private readonly Configuration configuration;
        private readonly ItemLookupService itemLookup;
        private readonly ShoppingListManager listManager;
        private readonly LifestreamIPC lifestreamIPC;
        private readonly IClientState clientState;
        private readonly IObjectTable objectTable;
        private readonly FileDialogManager fileDialogManager;

        private string searchInput = string.Empty;
        private int addQuantity = 1;
        private uint? selectedSearchItemId;
        private string batchTextImport = string.Empty;
        private string makePlaceFilePath = string.Empty;
        private bool includeDyes = true;
        private string importStatusMessage = string.Empty;
        private bool showLifestreamPopup = false;
        private bool showClearAllPopup = false;

        private bool importCrystals = true;
        private bool importTimedNodes = true;
        private bool importTomesTokensScrips = true;
        private bool importGathering = true;
        private bool importDungeonsDropsGC = true;
        private bool importPreCrafts = true;
        private bool importOther = true;

        public MainWindow(
            Configuration configuration,
            ItemLookupService itemLookup,
            ShoppingListManager listManager,
            LifestreamIPC lifestreamIPC,
            IClientState clientState,
            IObjectTable objectTable,
            FileDialogManager fileDialogManager,
            Action toggleConfig) : base("StingyShopper###StingyShopperMainWindow")
        {
            this.configuration = configuration;
            this.itemLookup = itemLookup;
            this.listManager = listManager;
            this.lifestreamIPC = lifestreamIPC;
            this.clientState = clientState;
            this.objectTable = objectTable;
            this.fileDialogManager = fileDialogManager;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(540, 440),
                MaximumSize = new Vector2(900, 700)
            };

            TitleBarButtons.Add(new TitleBarButton
            {
                Icon = FontAwesomeIcon.Cog,
                IconOffset = new Vector2(1, 1),
                Click = _ => toggleConfig(),
                ShowTooltip = () => ImGui.SetTooltip("Settings")
            });
        }

        public void Dispose() { }

        private bool showLiveWarningPopup = false;

        public override void Draw()
        {
            if (!this.configuration.HasAcknowledgedLivePricesWarning)
            {
                this.showLiveWarningPopup = true;
                ImGui.OpenPopup("Universalis Outdated Data Warning");
            }

            if (ImGui.BeginPopupModal("Universalis Outdated Data Warning", ref this.showLiveWarningPopup, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("Important: Universalis Data is Not Live");
                ImGui.Spacing();
                ImGui.TextUnformatted("Market board data fetched from the Universalis API is crowd-sourced and may be outdated.\n" +
                                     "Prices and stock quantities can differ from what is currently listed in-game.\n" +
                                     "Always double-check listings on the board before completing major purchases.");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("I understand"))
                {
                    this.configuration.HasAcknowledgedLivePricesWarning = true;
                    this.configuration.Save();
                    ImGui.CloseCurrentPopup();
                    this.showLiveWarningPopup = false;
                }
                ImGui.EndPopup();
            }

            if (ImGui.BeginTabBar("StingyShopperTabs"))
            {
                if (ImGui.BeginTabItem("Shopping List"))
                {
                    DrawShoppingListTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Shopping Plan & Travel"))
                {
                    DrawShoppingPlanTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Import Lists"))
                {
                    DrawImportTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawShoppingListTab()
        {
            ImGui.TextUnformatted("Add Items to Shopping List");
            ImGui.Spacing();

            ImGui.SetNextItemWidth(260);
            if (ImGui.InputText("Item Search", ref this.searchInput, 100))
            {
                this.selectedSearchItemId = null;
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(70);
            ImGui.InputInt("Qty", ref this.addQuantity);
            if (this.addQuantity < 1) this.addQuantity = 1;

            if (!string.IsNullOrWhiteSpace(this.searchInput))
            {
                var matches = this.itemLookup.SearchItems(this.searchInput, 10).ToList();
                if (matches.Count > 0)
                {
                    if (ImGui.BeginListBox("##SearchResults", new Vector2(340, 120)))
                    {
                        foreach (var kvp in matches)
                        {
                            bool isSelected = (this.selectedSearchItemId == kvp.Key);
                            if (ImGui.Selectable($"{kvp.Value} (ID: {kvp.Key})", isSelected))
                            {
                                this.selectedSearchItemId = kvp.Key;
                                this.searchInput = kvp.Value;
                                if (this.configuration.AutoCopyItemNameToClipboard)
                                {
                                    ImGui.SetClipboardText(kvp.Value);
                                }
                            }
                        }
                        ImGui.EndListBox();
                    }
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Add to List"))
            {
                uint? targetId = this.selectedSearchItemId ?? this.itemLookup.GetItemIdByName(this.searchInput);
                if (targetId.HasValue)
                {
                    this.listManager.AddItem(targetId.Value, this.addQuantity);
                    this.searchInput = string.Empty;
                    this.selectedSearchItemId = null;
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextUnformatted($"Current Items ({this.listManager.Items.Count})");
            ImGui.SameLine();
            if (ImGui.Button("Clear All"))
            {
                this.showClearAllPopup = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear Completed"))
            {
                this.listManager.ClearPurchased();
            }

            ImGui.Spacing();
            if (ImGui.BeginChild("##ListTableChild", new Vector2(0, 0), true))
            {
                if (ImGui.BeginTable("ShoppingListTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn("Item Name", ImGuiTableColumnFlags.WidthStretch, 0, 0);
                ImGui.TableSetupColumn("Target Qty", ImGuiTableColumnFlags.WidthFixed, 0, 1);
                ImGui.TableSetupColumn("Bought", ImGuiTableColumnFlags.WidthFixed, 0, 2);
                ImGui.TableSetupColumn("Remaining", ImGuiTableColumnFlags.WidthFixed, 0, 3);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 0, 4);
                ImGui.TableHeadersRow();

                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty)
                {
                    var spec = sortSpecs.Specs;
                    var direction = spec.SortDirection;
                    var colIndex = spec.ColumnIndex;

                    if (colIndex == 0) // Item Name
                    {
                        if (direction == ImGuiSortDirection.Ascending)
                            this.listManager.Items.Sort((a, b) => string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase));
                        else
                            this.listManager.Items.Sort((a, b) => string.Compare(b.ItemName, a.ItemName, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (colIndex == 1) // Target Qty
                    {
                        if (direction == ImGuiSortDirection.Ascending)
                            this.listManager.Items.Sort((a, b) => a.TargetQuantity.CompareTo(b.TargetQuantity));
                        else
                            this.listManager.Items.Sort((a, b) => b.TargetQuantity.CompareTo(a.TargetQuantity));
                    }
                    else if (colIndex == 2) // Bought
                    {
                        if (direction == ImGuiSortDirection.Ascending)
                            this.listManager.Items.Sort((a, b) => a.BoughtQuantity.CompareTo(b.BoughtQuantity));
                        else
                            this.listManager.Items.Sort((a, b) => b.BoughtQuantity.CompareTo(a.BoughtQuantity));
                    }
                    else if (colIndex == 3) // Remaining
                    {
                        if (direction == ImGuiSortDirection.Ascending)
                            this.listManager.Items.Sort((a, b) => a.RemainingQuantity.CompareTo(b.RemainingQuantity));
                        else
                            this.listManager.Items.Sort((a, b) => b.RemainingQuantity.CompareTo(a.RemainingQuantity));
                    }

                    sortSpecs.SpecsDirty = false;
                }

                for (int i = 0; i < this.listManager.Items.Count; i++)
                {
                    var item = this.listManager.Items[i];
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (ImGui.Selectable($"{item.ItemName}##list_{item.ItemId}"))
                    {
                        if (this.configuration.AutoCopyItemNameToClipboard)
                        {
                            ImGui.SetClipboardText(item.ItemName);
                        }
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"-##TargetMinus_{i}"))
                    {
                        item.TargetQuantity = Math.Max(1, item.TargetQuantity - 1);
                        this.listManager.SaveToConfig();
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted(item.TargetQuantity.ToString());
                    ImGui.SameLine();
                    if (ImGui.Button($"+##TargetPlus_{i}"))
                    {
                        item.TargetQuantity++;
                        this.listManager.SaveToConfig();
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"-##BoughtMinus_{i}"))
                    {
                        item.BoughtQuantity = Math.Max(0, item.BoughtQuantity - 1);
                        this.listManager.SaveToConfig();
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted(item.BoughtQuantity.ToString());
                    ImGui.SameLine();
                    if (ImGui.Button($"+##BoughtPlus_{i}"))
                    {
                        item.BoughtQuantity++;
                        this.listManager.SaveToConfig();
                    }

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.RemainingQuantity.ToString());

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Remove##{i}"))
                    {
                        this.listManager.RemoveItem(item.ItemId);
                    }
                }

                ImGui.EndTable();
            }
            ImGui.EndChild();
            }

            if (this.showClearAllPopup)
            {
                ImGui.OpenPopup("Confirm Clear All");
            }

            if (ImGui.BeginPopupModal("Confirm Clear All", ref this.showClearAllPopup, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("Are you sure you want to clear your entire shopping list?");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("Yes, Clear All"))
                {
                    this.listManager.ClearAll();
                    ImGui.CloseCurrentPopup();
                    this.showClearAllPopup = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                    this.showClearAllPopup = false;
                }
                ImGui.EndPopup();
            }
        }

        private void DrawShoppingPlanTab()
        {
            string currentWorld = this.objectTable.LocalPlayer?.CurrentWorld.Value.Name.ExtractText() ?? "Unknown";
            string currentDC = "Light";
            try
            {
                var world = this.objectTable.LocalPlayer?.CurrentWorld.Value;
                var dcName = world?.DataCenter.Value.Name.ExtractText();
                if (!string.IsNullOrEmpty(dcName))
                {
                    currentDC = dcName;
                }
            }
            catch
            {
                // Fallback to Light
            }

            ImGui.TextUnformatted($"Current Location: World ({currentWorld}) | Mode: {this.configuration.StinginessMode}");
            ImGui.SameLine();
            if (ImGui.Button("Fetch / Refresh Prices"))
            {
                uint homeWorldId = this.objectTable.LocalPlayer?.CurrentWorld.RowId ?? 80;
                _ = this.listManager.RefreshMarketDataAsync(homeWorldId, currentWorld);
            }

            ImGui.TextDisabled($"Status: {this.listManager.LastFetchStatus}");
            ImGui.TextColored(new Vector4(1.0f, 0.82f, 0.12f, 1.0f), "Warning: Price fetching may take longer due to API rate-limiting.");
            ImGui.Spacing();

            if (ImGui.Button("Export Remaining (Clipboard)"))
            {
                var lines = this.listManager.Items
                    .Where(i => i.RemainingQuantity > 0)
                    .Select(i => $"{i.RemainingQuantity} x {i.ItemName}");
                ImGui.SetClipboardText(string.Join("\n", lines));
            }
            ImGui.SameLine();
            if (ImGui.Button("Export Remaining (File)..."))
            {
                string initialPath = string.Empty;
                try
                {
                    if (!string.IsNullOrEmpty(this.makePlaceFilePath))
                    {
                        initialPath = Path.GetDirectoryName(this.makePlaceFilePath) ?? string.Empty;
                    }
                }
                catch
                {
                    // Safe fallback
                }

                this.fileDialogManager.SaveFileDialog(
                    "Save Remaining List",
                    "Text File (*.txt){.txt}",
                    "remaining_shopping_list",
                    ".txt",
                    (success, path) =>
                    {
                        if (success && !string.IsNullOrEmpty(path))
                        {
                            try
                            {
                                if (!path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                                {
                                    path += ".txt";
                                }

                                var lines = this.listManager.Items
                                    .Where(i => i.RemainingQuantity > 0)
                                    .Select(i => $"{i.RemainingQuantity} x {i.ItemName}");
                                File.WriteAllLines(path, lines);
                            }
                            catch (Exception ex)
                            {
                                Plugin.PluginLog.Error(ex, "Failed to save exported file");
                            }
                        }
                    },
                    initialPath);
            }

            ImGui.Spacing();

            // Calculate Travelled Worlds and Total Cost
            int travelWorlds = this.listManager.GroupedPlan.Count(g => 
                !g.WorldName.Equals(currentWorld, StringComparison.OrdinalIgnoreCase) && 
                !g.WorldName.Equals("Untradable", StringComparison.OrdinalIgnoreCase) && 
                !g.WorldName.Equals("Not Listed", StringComparison.OrdinalIgnoreCase));
            uint totalCost = (uint)this.listManager.GroupedPlan.Sum(g => g.TotalCost);

            ImGui.TextUnformatted($"Total Est. Cost: {totalCost:N0} gil | Travelled Servers: {travelWorlds}");

            if (this.configuration.AutoCopyItemNameToClipboard)
            {
                ImGui.TextDisabled("Tip: Clicking an item name copies it to your clipboard for easy market search.");
            }
            else
            {
                ImGui.TextDisabled("Tip: Enable 'Auto-Copy' in settings to copy item names to clipboard on click.");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (this.listManager.GroupedPlan.Count == 0)
            {
                ImGui.TextUnformatted("No shopping plan generated yet. Add items and click Refresh Prices.");
                return;
            }

            if (ImGui.BeginChild("##PlanGroupsChild", new Vector2(0, 0), false))
            {

            foreach (var group in this.listManager.GroupedPlan)
            {
                bool isSpecialGroup = group.WorldName.Equals("Untradable", StringComparison.OrdinalIgnoreCase) || 
                                     group.WorldName.Equals("Not Listed", StringComparison.OrdinalIgnoreCase);
                bool isCurrentWorld = !isSpecialGroup && group.WorldName.Equals(currentWorld, StringComparison.OrdinalIgnoreCase);
                
                string headerLabel = $"{group.WorldName} - Total Est. Cost: {group.TotalCost:N0} gil ({group.Items.Count} items)";
                if (isCurrentWorld) headerLabel += " [CURRENT WORLD]";

                if (ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    if (!isSpecialGroup)
                    {
                        if (isCurrentWorld)
                        {
                            if (ImGui.Button("Go to Market Board via Lifestream"))
                            {
                                if (this.lifestreamIPC.IsLifestreamInstalled())
                                {
                                    this.lifestreamIPC.TeleportToMarketBoard();
                                }
                                else
                                {
                                    this.showLifestreamPopup = true;
                                }
                            }
                        }
                        else
                        {
                            if (ImGui.Button($"Teleport to {group.WorldName} via Lifestream"))
                            {
                                if (this.lifestreamIPC.IsLifestreamInstalled())
                                {
                                    this.lifestreamIPC.ChangeWorld(group.WorldName);
                                }
                                else
                                {
                                    this.showLifestreamPopup = true;
                                }
                            }
                        }
                        ImGui.Spacing();
                    }

                    if (ImGui.BeginTable($"PlanTable_{group.WorldName}", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Buy Qty");
                        ImGui.TableSetupColumn("Est. Unit Price");
                        ImGui.TableHeadersRow();

                        foreach (var item in group.Items)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            if (ImGui.Selectable($"{item.ItemName}##plan_{item.ItemId}_{group.WorldName}"))
                            {
                                if (this.configuration.AutoCopyItemNameToClipboard)
                                {
                                    ImGui.SetClipboardText(item.ItemName);
                                }
                            }

                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(item.RemainingQuantity.ToString());

                            ImGui.TableNextColumn();
                            if (item.IsUntradable)
                            {
                                ImGui.TextUnformatted("Untradable");
                            }
                            else if (item.IsUnlisted)
                            {
                                ImGui.TextUnformatted("Not Listed");
                            }
                            else
                            {
                                ImGui.TextUnformatted($"{item.EstimatedUnitPrice:N0} gil");
                            }
                        }
                        ImGui.EndTable();
                    }
                    ImGui.Spacing();
                }
            }
            ImGui.EndChild();
            }

            if (this.showLifestreamPopup)
            {
                ImGui.OpenPopup("Lifestream Missing");
            }

            if (ImGui.BeginPopupModal("Lifestream Missing", ref this.showLifestreamPopup, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("Lifestream plugin is not installed or loaded.");
                ImGui.TextUnformatted("This feature requires Lifestream to automatically teleport across servers.");
                ImGui.Spacing();

                if (ImGui.Button("Open Lifestream GitHub Page"))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://github.com/NightmareXIV/Lifestream",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Error(ex, "Failed to open link");
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Close"))
                {
                    ImGui.CloseCurrentPopup();
                    this.showLifestreamPopup = false;
                }
                ImGui.EndPopup();
            }
        }

        private void DrawImportTab()
        {
            ImGui.TextUnformatted("Import MakePlace List File (.list.txt)");
            ImGui.Spacing();

            ImGui.Checkbox("Include Dyes from MakePlace list", ref this.includeDyes);
            ImGui.Spacing();

            ImGui.SetNextItemWidth(300);
            ImGui.InputText("File Path", ref this.makePlaceFilePath, 260);
            ImGui.SameLine();
            if (ImGui.Button("Browse..."))
            {
                this.fileDialogManager.OpenFileDialog(
                    "Select MakePlace List File",
                    "List Files{.list.txt,.txt}",
                    (success, selectedPath) =>
                    {
                        if (success && !string.IsNullOrWhiteSpace(selectedPath))
                        {
                            this.makePlaceFilePath = selectedPath;
                        }
                    });
            }

            ImGui.SameLine();
            if (ImGui.Button("Import File"))
            {
                string cleanPath = this.makePlaceFilePath.Trim('"', ' ');
                if (File.Exists(cleanPath))
                {
                    try
                    {
                        string content = File.ReadAllText(cleanPath);
                        var parsed = this.itemLookup.ParseMakePlaceList(content, this.includeDyes);
                        foreach (var (itemId, qty) in parsed)
                        {
                            this.listManager.AddItem(itemId, qty);
                        }
                        this.importStatusMessage = $"Successfully imported {parsed.Count} items (including dyes: {this.includeDyes}) from MakePlace file.";
                    }
                    catch (Exception ex)
                    {
                        this.importStatusMessage = $"Error loading file: {ex.Message}";
                    }
                }
                else
                {
                    this.importStatusMessage = "File does not exist at specified path.";
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextUnformatted("Paste Raw List Text (MakePlace format or Plain text)");
            ImGui.Spacing();

            ImGui.InputTextMultiline("##BatchImportText", ref this.batchTextImport, 4000, new Vector2(-1, 160));

            if (ImGui.Button("Import as Plain Text"))
            {
                var parsed = this.itemLookup.ParseTextList(this.batchTextImport);
                foreach (var (itemId, qty) in parsed)
                {
                    this.listManager.AddItem(itemId, qty);
                }
                this.importStatusMessage = $"Imported {parsed.Count} items from text.";
                this.batchTextImport = string.Empty;
            }

            ImGui.SameLine();
            if (ImGui.Button("Import as MakePlace List"))
            {
                var parsed = this.itemLookup.ParseMakePlaceList(this.batchTextImport, this.includeDyes);
                foreach (var (itemId, qty) in parsed)
                {
                    this.listManager.AddItem(itemId, qty);
                }
                this.importStatusMessage = $"Imported {parsed.Count} unique items from MakePlace text.";
                this.batchTextImport = string.Empty;
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextUnformatted("Import Teamcraft List");
            ImGui.TextDisabled("Choose which categories to import from the clipboard:");
            ImGui.Spacing();

            ImGui.Checkbox("Crystals##tccrystals", ref this.importCrystals);
            ImGui.SameLine();
            ImGui.Checkbox("Timed Nodes##tctimed", ref this.importTimedNodes);
            ImGui.SameLine();
            ImGui.Checkbox("Tomes/Tokens/Scrips##tctokens", ref this.importTomesTokensScrips);
            ImGui.SameLine();
            ImGui.Checkbox("Gathering##tcgathering", ref this.importGathering);

            ImGui.Checkbox("Dungeons/Drops or GC##tcdungeons", ref this.importDungeonsDropsGC);
            ImGui.SameLine();
            ImGui.Checkbox("Pre Crafts##tcprecrafts", ref this.importPreCrafts);
            ImGui.SameLine();
            ImGui.Checkbox("Other Categories##tcother", ref this.importOther);

            ImGui.Spacing();
            if (ImGui.Button("Import Teamcraft List from Clipboard"))
            {
                try
                {
                    string clipText = ImGui.GetClipboardText();
                    if (string.IsNullOrWhiteSpace(clipText))
                    {
                        this.importStatusMessage = "Clipboard is empty.";
                    }
                    else
                    {
                        int count = ImportTeamcraftListText(clipText);
                        this.importStatusMessage = $"Successfully imported {count} items from Teamcraft clipboard.";
                    }
                }
                catch (Exception ex)
                {
                    this.importStatusMessage = $"Clipboard import error: {ex.Message}";
                }
            }

            if (!string.IsNullOrEmpty(this.importStatusMessage))
            {
                ImGui.Spacing();
                ImGui.TextDisabled(this.importStatusMessage);
            }
        }

        private int ImportTeamcraftListText(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string currentCategory = "Other";
            int importedCount = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var headerMatch = Regex.Match(line, @"^\s*(.+?)\s*:\s*$");
                if (headerMatch.Success)
                {
                    currentCategory = headerMatch.Groups[1].Value.Trim();
                    continue;
                }

                var itemMatch = Regex.Match(line, @"^\s*(\d+)x\s+(.+)$");
                if (itemMatch.Success)
                {
                    int qty = int.Parse(itemMatch.Groups[1].Value);
                    string itemName = itemMatch.Groups[2].Value.Trim();

                    if (IsCategoryEnabled(currentCategory))
                    {
                        var itemId = this.itemLookup.GetItemIdByName(itemName);
                        if (itemId.HasValue)
                        {
                            this.listManager.AddItem(itemId.Value, qty);
                            importedCount++;
                        }
                    }
                }
            }

            return importedCount;
        }

        private bool IsCategoryEnabled(string category)
        {
            if (category.Equals("Crystals", StringComparison.OrdinalIgnoreCase)) return this.importCrystals;
            if (category.Equals("Timed nodes", StringComparison.OrdinalIgnoreCase)) return this.importTimedNodes;
            if (category.Equals("Tomes/Tokens/Scrips", StringComparison.OrdinalIgnoreCase)) return this.importTomesTokensScrips;
            if (category.Equals("Gathering", StringComparison.OrdinalIgnoreCase)) return this.importGathering;
            if (category.Equals("Dungeons/Drops or GC", StringComparison.OrdinalIgnoreCase) || 
                category.Contains("Dungeon", StringComparison.OrdinalIgnoreCase) || 
                category.Contains("GC", StringComparison.OrdinalIgnoreCase)) return this.importDungeonsDropsGC;
            if (category.Equals("Pre crafts", StringComparison.OrdinalIgnoreCase) || 
                category.Contains("Pre craft", StringComparison.OrdinalIgnoreCase)) return this.importPreCrafts;
            return this.importOther;
        }
    }
}
