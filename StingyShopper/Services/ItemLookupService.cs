using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace StingyShopper.Services
{
    public class ItemLookupService
    {
        private readonly IDataManager dataManager;
        private readonly Dictionary<uint, string> itemCache = new();
        private readonly Dictionary<string, uint> nameToIdMap = new(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> StandaloneDyes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pure White", "Jet Black", "Metallic Gold", "Metallic Silver", "Metallic Red",
            "Metallic Blue", "Metallic Green", "Metallic Purple", "Metallic Yellow",
            "Pastel Pink", "Pastel Green", "Pastel Blue", "Pastel Purple",
            "Dark Red", "Dark Brown", "Dark Blue", "Dark Purple", "Dark Green",
            "General-purpose Pure White", "General-purpose Jet Black",
            "General-purpose Metallic Gold", "General-purpose Metallic Silver"
        };

        private static readonly HashSet<string> WideSpectrum1Dyes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Ruby Red", "Cherry Pink", "Canary Yellow", "Vanilla Yellow", "Dragoon Blue",
            "Turquoise Blue", "Gunmetal Black", "Pearl White", "Metallic Brass"
        };

        private static readonly HashSet<string> WideSpectrum2Dyes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Sunset Orange", "Moss Green", "Midnight Blue", "Slate Grey"
        };

        private readonly Dictionary<uint, (string Name, string DcName)> worldCache = new();

        public ItemLookupService(IDataManager dataManager)
        {
            this.dataManager = dataManager;
            InitializeCache();
        }

        private void InitializeCache()
        {
            var sheet = this.dataManager.GetExcelSheet<Item>();
            if (sheet != null)
            {
                foreach (var item in sheet)
                {
                    string name = item.Name.ExtractText();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    uint rowId = item.RowId;
                    this.itemCache[rowId] = name;
                    if (!this.nameToIdMap.ContainsKey(name))
                    {
                        this.nameToIdMap[name] = rowId;
                    }
                }
            }

            var worldSheet = this.dataManager.GetExcelSheet<World>();
            if (worldSheet != null)
            {
                foreach (var w in worldSheet)
                {
                    string name = w.Name.ExtractText();
                    if (string.IsNullOrEmpty(name)) continue;

                    string dcName = "Unknown";
                    try
                    {
                        dcName = w.DataCenter.Value.Name.ExtractText();
                    }
                    catch
                    {
                        // Safe fallback
                    }

                    this.worldCache[w.RowId] = (name, dcName);
                }
            }
        }

        public (string Name, string DcName) GetWorldInfo(uint worldId)
        {
            return this.worldCache.TryGetValue(worldId, out var info) ? info : ("Unknown", "Unknown");
        }

        public string GetItemName(uint itemId)
        {
            return this.itemCache.TryGetValue(itemId, out var name) ? name : $"Item #{itemId}";
        }

        public uint? GetItemIdByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (this.nameToIdMap.TryGetValue(name.Trim(), out var id))
            {
                return id;
            }
            return null;
        }

        public IEnumerable<KeyValuePair<uint, string>> SearchItems(string query, int maxResults = 20)
        {
            if (string.IsNullOrWhiteSpace(query)) return Enumerable.Empty<KeyValuePair<uint, string>>();

            string trimmed = query.Trim();
            return this.itemCache
                .Where(kvp => kvp.Value.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                .Take(maxResults);
        }

        public List<(uint ItemId, int Quantity)> ParseTextList(string textInput)
        {
            var results = new List<(uint ItemId, int Quantity)>();
            if (string.IsNullOrWhiteSpace(textInput)) return results;

            var lines = textInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                int quantity = 1;
                string itemName = line;

                var matchPrefix = Regex.Match(line, @"^(\d+)\s*x?\s+(.+)$", RegexOptions.IgnoreCase);
                if (matchPrefix.Success)
                {
                    int.TryParse(matchPrefix.Groups[1].Value, out quantity);
                    itemName = matchPrefix.Groups[2].Value.Trim();
                }
                else
                {
                    var matchSuffix = Regex.Match(line, @"^(.+?)\s+x?\s*(\d+)$", RegexOptions.IgnoreCase);
                    if (matchSuffix.Success)
                    {
                        itemName = matchSuffix.Groups[1].Value.Trim();
                        int.TryParse(matchSuffix.Groups[2].Value, out quantity);
                    }
                }

                var itemId = GetItemIdByName(itemName);
                if (itemId.HasValue)
                {
                    results.Add((itemId.Value, Math.Max(1, quantity)));
                }
            }

            return results;
        }

        public List<(uint ItemId, int Quantity)> ParseMakePlaceList(string content, bool includeDyes = true)
        {
            var itemTotals = new Dictionary<uint, int>();
            if (string.IsNullOrWhiteSpace(content)) return new List<(uint ItemId, int Quantity)>();

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool inDyeSection = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("="))
                {
                    continue;
                }

                // Section detection
                if (line.Equals("Dyes", StringComparison.OrdinalIgnoreCase))
                {
                    inDyeSection = true;
                    continue;
                }
                else if (line.StartsWith("Furniture", StringComparison.OrdinalIgnoreCase) || line.Contains("With Dye", StringComparison.OrdinalIgnoreCase))
                {
                    inDyeSection = false;
                    continue;
                }

                if (inDyeSection && !includeDyes)
                {
                    continue;
                }

                var match = Regex.Match(line, @"^(.+?):\s*(\d+)$");
                if (match.Success)
                {
                    string rawName = match.Groups[1].Value.Trim();
                    if (!int.TryParse(match.Groups[2].Value, out int quantity)) continue;

                    if (inDyeSection)
                    {
                        // Allocate dye to standalone or consolidated spectrum dye
                        ResolveAndAddDye(rawName, quantity, itemTotals);
                    }
                    else
                    {
                        // Clean parenthetical dye names for furniture e.g. "Swag Valance (Rolanberry Red)" -> "Swag Valance"
                        string cleanItem = Regex.Replace(rawName, @"\s*\([^)]+\)$", "").Trim();
                        var itemId = GetItemIdByName(cleanItem) ?? GetItemIdByName(rawName);
                        if (itemId.HasValue)
                        {
                            if (itemTotals.ContainsKey(itemId.Value))
                            {
                                itemTotals[itemId.Value] = Math.Max(itemTotals[itemId.Value], quantity);
                            }
                            else
                            {
                                itemTotals[itemId.Value] = quantity;
                            }
                        }
                    }
                }
            }

            return itemTotals.Select(kvp => (kvp.Key, kvp.Value)).ToList();
        }

        private void ResolveAndAddDye(string dyeName, int quantity, Dictionary<uint, int> itemTotals)
        {
            uint? resolvedItemId = null;

            // 1. Check standalone/general purpose dyes
            if (StandaloneDyes.Contains(dyeName))
            {
                resolvedItemId = GetItemIdByName(dyeName) ?? GetItemIdByName($"{dyeName} Dye") ?? GetItemIdByName($"General-purpose {dyeName} Dye");
            }

            // 2. If not standalone or standalone ID not found, resolve via spectrum categories
            if (!resolvedItemId.HasValue)
            {
                if (WideSpectrum1Dyes.Contains(dyeName))
                {
                    resolvedItemId = GetItemIdByName("Wide Spectrum #1 Dye");
                }
                else if (WideSpectrum2Dyes.Contains(dyeName))
                {
                    resolvedItemId = GetItemIdByName("Wide Spectrum #2 Dye");
                }
                else
                {
                    // Default to Standard Spectrum Dye
                    resolvedItemId = GetItemIdByName("Standard Spectrum Dye");
                }
            }

            // 3. Fallback: if spectrum item ID not found in current sheet, lookup by direct dye name
            if (!resolvedItemId.HasValue)
            {
                resolvedItemId = GetItemIdByName(dyeName) ?? GetItemIdByName($"{dyeName} Dye");
            }

            if (resolvedItemId.HasValue)
            {
                if (itemTotals.ContainsKey(resolvedItemId.Value))
                {
                    itemTotals[resolvedItemId.Value] += quantity;
                }
                else
                {
                    itemTotals[resolvedItemId.Value] = quantity;
                }
            }
        }

        public bool IsItemUntradable(uint itemId)
        {
            var sheet = this.dataManager.GetExcelSheet<Item>();
            if (sheet == null) return false;

            try
            {
                var row = sheet.GetRow(itemId);
                return row.IsUntradable;
            }
            catch
            {
                return false;
            }
        }
    }
}
