using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StingyShopper.Services
{
    public class ShoppingListItem
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int TargetQuantity { get; set; }
        public int BoughtQuantity { get; set; }
        public int RemainingQuantity => Math.Max(0, TargetQuantity - BoughtQuantity);

        // Optimization outputs
        public string RecommendedWorld { get; set; } = "Unknown";
        public uint EstimatedUnitPrice { get; set; }
        public uint EstimatedTotalCost { get; set; }

        // Purchase splits (Allocations)
        public List<SavedPurchaseAllocation> Allocations { get; set; } = new();

        // Availability indicators
        public bool IsUntradable { get; set; }
        public bool IsUnlisted { get; set; }
    }

    public class WorldShoppingGroup
    {
        public string WorldName { get; set; } = string.Empty;
        public List<ShoppingListItem> Items { get; set; } = new();
        public uint TotalCost => (uint)Items.Sum(i => i.EstimatedTotalCost);
    }

    public class ShoppingListManager
    {
        private readonly Configuration configuration;
        private readonly ItemLookupService itemLookup;
        private readonly UniversalisClient universalisClient;

        public List<ShoppingListItem> Items { get; } = new();
        public List<WorldShoppingGroup> GroupedPlan { get; private set; } = new();
        public bool IsFetching { get; private set; }
        public string LastFetchStatus { get; private set; } = "Ready";

        public ShoppingListManager(Configuration configuration, ItemLookupService itemLookup, UniversalisClient universalisClient)
        {
            this.configuration = configuration;
            this.itemLookup = itemLookup;
            this.universalisClient = universalisClient;

            // Load saved list from configuration
            foreach (var saved in this.configuration.SavedItems)
            {
                var item = new ShoppingListItem
                {
                    ItemId = saved.ItemId,
                    ItemName = saved.ItemName,
                    TargetQuantity = saved.TargetQuantity,
                    BoughtQuantity = saved.BoughtQuantity
                };

                foreach (var alloc in saved.Allocations)
                {
                    item.Allocations.Add(new SavedPurchaseAllocation
                    {
                        WorldName = alloc.WorldName,
                        Quantity = alloc.Quantity,
                        UnitPrice = alloc.UnitPrice,
                        TotalCost = alloc.TotalCost
                    });
                }

                Items.Add(item);
            }

            // Reconstruct the grouped plan on startup if items have resolved recommendations
            ReconstructGroupedPlan();
        }

        public void SaveToConfig()
        {
            this.configuration.SavedItems = Items.Select(i => new SavedShoppingItem
            {
                ItemId = i.ItemId,
                ItemName = i.ItemName,
                TargetQuantity = i.TargetQuantity,
                BoughtQuantity = i.BoughtQuantity,
                Allocations = i.Allocations.Select(a => new SavedPurchaseAllocation
                {
                    WorldName = a.WorldName,
                    Quantity = a.Quantity,
                    UnitPrice = a.UnitPrice,
                    TotalCost = a.TotalCost
                }).ToList()
            }).ToList();
            this.configuration.Save();
        }

        public void AddItem(uint itemId, int quantity)
        {
            var existing = Items.FirstOrDefault(i => i.ItemId == itemId);
            if (existing != null)
            {
                existing.TargetQuantity += quantity;
            }
            else
            {
                Items.Add(new ShoppingListItem
                {
                    ItemId = itemId,
                    ItemName = this.itemLookup.GetItemName(itemId),
                    TargetQuantity = quantity,
                    BoughtQuantity = 0
                });
            }
            SaveToConfig();
        }

        public void RemoveItem(uint itemId)
        {
            Items.RemoveAll(i => i.ItemId == itemId);
            SaveToConfig();
        }

        public void ClearAll()
        {
            Items.Clear();
            GroupedPlan.Clear();
            SaveToConfig();
        }

        public void ClearPurchased()
        {
            Items.RemoveAll(i => i.RemainingQuantity <= 0);
            foreach (var item in Items)
            {
                item.BoughtQuantity = 0;
            }
            SaveToConfig();
        }

        public async Task RefreshMarketDataAsync(uint homeWorldId, string currentWorld)
        {
            if (Items.Count == 0)
            {
                GroupedPlan.Clear();
                LastFetchStatus = "Shopping list is empty.";
                return;
            }

            IsFetching = true;
            LastFetchStatus = "Fetching prices from Universalis...";

            try
            {
                string searchScope = this.configuration.EnableRegionWideSearch ? "Europe" : this.itemLookup.GetWorldInfo(homeWorldId).DcName;
                if (string.IsNullOrWhiteSpace(searchScope) || searchScope.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    searchScope = currentWorld;
                }

                var itemIds = Items.Select(i => i.ItemId).Distinct().ToList();
                var marketDataMap = await this.universalisClient.FetchMarketDataAsync(
                    searchScope, 
                    itemIds,
                    (status) =>
                    {
                        LastFetchStatus = status;
                    });

                OptimizeShoppingPlan(marketDataMap, currentWorld);
                LastFetchStatus = $"Updated at {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                LastFetchStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsFetching = false;
            }
        }

        private void OptimizeShoppingPlan(Dictionary<uint, UniversalisItemData> marketDataMap, string currentWorld)
        {
            foreach (var item in Items)
            {
                item.Allocations.Clear();

                if (item.RemainingQuantity <= 0) continue;

                bool isUntradable = this.itemLookup.IsItemUntradable(item.ItemId);
                item.IsUntradable = isUntradable;

                if (isUntradable)
                {
                    item.Allocations.Add(new SavedPurchaseAllocation
                    {
                        WorldName = "Untradable",
                        Quantity = item.RemainingQuantity,
                        UnitPrice = 0,
                        TotalCost = 0
                    });
                    continue;
                }

                if (!marketDataMap.TryGetValue(item.ItemId, out var data) || data.Listings == null || data.Listings.Count == 0)
                {
                    item.IsUnlisted = true;
                    item.Allocations.Add(new SavedPurchaseAllocation
                    {
                        WorldName = "Not Listed",
                        Quantity = item.RemainingQuantity,
                        UnitPrice = 0,
                        TotalCost = 0
                    });
                    continue;
                }

                item.IsUnlisted = false;
                var validListings = data.Listings.Where(l => !string.IsNullOrEmpty(l.WorldName)).ToList();

                if (validListings.Count == 0)
                {
                    item.IsUnlisted = true;
                    item.Allocations.Add(new SavedPurchaseAllocation
                    {
                        WorldName = "Not Listed",
                        Quantity = item.RemainingQuantity,
                        UnitPrice = 0,
                        TotalCost = 0
                    });
                    continue;
                }

                if (this.configuration.StinginessMode == StinginessMode.MaxConvenience)
                {
                    // Max Convenience: Find the single server that gives the cheapest cost for the whole quantity (or most of it)
                    var worldGroups = validListings.GroupBy(l => l.WorldName, StringComparer.OrdinalIgnoreCase);
                    string bestWorld = currentWorld;
                    uint bestCost = uint.MaxValue;
                    uint bestUnitPrice = 0;

                    foreach (var wGroup in worldGroups)
                    {
                        string wName = wGroup.Key;
                        var sortedWorldListings = wGroup.OrderBy(l => l.PricePerUnit).ToList();
                        int needed = item.RemainingQuantity;
                        uint cost = 0;
                        uint maxUnit = 0;

                        foreach (var l in sortedWorldListings)
                        {
                            if (needed <= 0) break;
                            int buy = Math.Min(needed, (int)l.Quantity);
                            cost += (uint)buy * l.PricePerUnit;
                            maxUnit = Math.Max(maxUnit, l.PricePerUnit);
                            needed -= buy;
                        }

                        if (needed > 0)
                        {
                            cost += (uint)needed * (maxUnit > 0 ? maxUnit : 1000000); // Penalty for incomplete
                        }

                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestWorld = wName;
                            bestUnitPrice = maxUnit;
                        }
                    }

                    item.Allocations.Add(new SavedPurchaseAllocation
                    {
                        WorldName = bestWorld,
                        Quantity = item.RemainingQuantity,
                        UnitPrice = bestUnitPrice,
                        TotalCost = bestCost < uint.MaxValue ? bestCost : 0
                    });
                }
                else if (this.configuration.StinginessMode == StinginessMode.MaxStingy)
                {
                    // Max Stingy: Buy the cheapest listings across all worlds, splitting as much as possible
                    var sortedListings = validListings.OrderBy(l => l.PricePerUnit).ToList();
                    int remainingNeeded = item.RemainingQuantity;

                    foreach (var listing in sortedListings)
                    {
                        if (remainingNeeded <= 0) break;

                        int buyQty = Math.Min(remainingNeeded, (int)listing.Quantity);
                        if (buyQty <= 0) continue;

                        var existing = item.Allocations.FirstOrDefault(a => a.WorldName.Equals(listing.WorldName, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            existing.Quantity += buyQty;
                            existing.TotalCost += (uint)buyQty * listing.PricePerUnit;
                            existing.UnitPrice = existing.TotalCost / (uint)existing.Quantity;
                        }
                        else
                        {
                            item.Allocations.Add(new SavedPurchaseAllocation
                            {
                                WorldName = listing.WorldName,
                                Quantity = buyQty,
                                UnitPrice = listing.PricePerUnit,
                                TotalCost = (uint)buyQty * listing.PricePerUnit
                            });
                        }

                        remainingNeeded -= buyQty;
                    }

                    if (remainingNeeded > 0)
                    {
                        uint fallbackPrice = sortedListings[0].PricePerUnit;
                        if (item.Allocations.Count > 0)
                        {
                            var firstAlloc = item.Allocations[0];
                            firstAlloc.Quantity += remainingNeeded;
                            firstAlloc.TotalCost += (uint)remainingNeeded * fallbackPrice;
                            firstAlloc.UnitPrice = firstAlloc.TotalCost / (uint)firstAlloc.Quantity;
                        }
                        else
                        {
                            item.Allocations.Add(new SavedPurchaseAllocation
                            {
                                WorldName = sortedListings[0].WorldName,
                                Quantity = remainingNeeded,
                                UnitPrice = fallbackPrice,
                                TotalCost = (uint)remainingNeeded * fallbackPrice
                            });
                        }
                    }
                }
                else
                {
                    // Balanced: Split only if subsequent server unit price does not exceed cheapest by threshold %
                    var sortedListings = validListings.OrderBy(l => l.PricePerUnit).ToList();
                    uint cheapestPrice = sortedListings[0].PricePerUnit;
                    double thresholdMultiplier = 1.0 + (this.configuration.BalancedSavingsThresholdPercent / 100.0);
                    uint maxPriceAllowed = (uint)(cheapestPrice * thresholdMultiplier);

                    int remainingNeeded = item.RemainingQuantity;

                    // First pass: buy within the threshold limits
                    foreach (var listing in sortedListings)
                    {
                        if (remainingNeeded <= 0) break;
                        if (listing.PricePerUnit > maxPriceAllowed) break; // Exceeds threshold

                        int buyQty = Math.Min(remainingNeeded, (int)listing.Quantity);
                        if (buyQty <= 0) continue;

                        var existing = item.Allocations.FirstOrDefault(a => a.WorldName.Equals(listing.WorldName, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            existing.Quantity += buyQty;
                            existing.TotalCost += (uint)buyQty * listing.PricePerUnit;
                            existing.UnitPrice = existing.TotalCost / (uint)existing.Quantity;
                        }
                        else
                        {
                            item.Allocations.Add(new SavedPurchaseAllocation
                            {
                                WorldName = listing.WorldName,
                                Quantity = buyQty,
                                UnitPrice = listing.PricePerUnit,
                                TotalCost = (uint)buyQty * listing.PricePerUnit
                            });
                        }

                        remainingNeeded -= buyQty;
                    }

                    // Second pass: if we still need items, buy them from the remaining listings without splitting (group on fallbackWorld)
                    if (remainingNeeded > 0)
                    {
                        string fallbackWorld = item.Allocations.Count > 0 ? item.Allocations[0].WorldName : sortedListings[0].WorldName;
                        int alreadyAllocated = item.Allocations.Sum(a => a.Quantity);
                        var unboughtListings = sortedListings.Skip(alreadyAllocated).ToList();

                        var targetAlloc = item.Allocations.FirstOrDefault(a => a.WorldName.Equals(fallbackWorld, StringComparison.OrdinalIgnoreCase));
                        if (targetAlloc == null)
                        {
                            targetAlloc = new SavedPurchaseAllocation
                            {
                                WorldName = fallbackWorld,
                                Quantity = 0,
                                UnitPrice = cheapestPrice,
                                TotalCost = 0
                            };
                            item.Allocations.Add(targetAlloc);
                        }

                        foreach (var listing in unboughtListings)
                        {
                            if (remainingNeeded <= 0) break;

                            int buyQty = Math.Min(remainingNeeded, (int)listing.Quantity);
                            if (buyQty <= 0) continue;

                            targetAlloc.Quantity += buyQty;
                            targetAlloc.TotalCost += (uint)buyQty * listing.PricePerUnit;
                            remainingNeeded -= buyQty;
                        }

                        if (remainingNeeded > 0)
                        {
                            uint lastPrice = sortedListings.Last().PricePerUnit;
                            targetAlloc.Quantity += remainingNeeded;
                            targetAlloc.TotalCost += (uint)remainingNeeded * lastPrice;
                        }

                        targetAlloc.UnitPrice = targetAlloc.TotalCost / (uint)targetAlloc.Quantity;
                    }
                }
            }

            ReconstructGroupedPlan();
            SaveToConfig();
        }

        public void ReconstructGroupedPlan()
        {
            var groups = new Dictionary<string, WorldShoppingGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in Items)
            {
                if (item.RemainingQuantity <= 0) continue;

                item.IsUntradable = this.itemLookup.IsItemUntradable(item.ItemId);
                item.IsUnlisted = !item.IsUntradable && (item.Allocations.Count == 0 || (item.Allocations.Count == 1 && item.Allocations[0].WorldName.Equals("Not Listed", StringComparison.OrdinalIgnoreCase)));

                if (item.IsUntradable)
                {
                    AddToGroup(groups, "Untradable", item, item.RemainingQuantity, 0, 0);
                }
                else if (item.IsUnlisted)
                {
                    AddToGroup(groups, "Not Listed", item, item.RemainingQuantity, 0, 0);
                }
                else
                {
                    foreach (var alloc in item.Allocations)
                    {
                        if (alloc.Quantity <= 0) continue;
                        AddToGroup(groups, alloc.WorldName, item, alloc.Quantity, alloc.UnitPrice, alloc.TotalCost);
                    }
                }
            }

            GroupedPlan = groups.Values.OrderByDescending(g => g.TotalCost).ToList();
        }

        private void AddToGroup(Dictionary<string, WorldShoppingGroup> groups, string worldName, ShoppingListItem item, int quantity, uint unitPrice, uint totalCost)
        {
            if (!groups.TryGetValue(worldName, out var group))
            {
                group = new WorldShoppingGroup { WorldName = worldName };
                groups[worldName] = group;
            }

            group.Items.Add(new ShoppingListItem
            {
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                TargetQuantity = quantity,
                BoughtQuantity = 0,
                RecommendedWorld = worldName,
                EstimatedUnitPrice = unitPrice,
                EstimatedTotalCost = totalCost,
                IsUntradable = item.IsUntradable,
                IsUnlisted = item.IsUnlisted
            });
        }

        public void RegisterPurchase(uint itemId, int quantityBought)
        {
            var item = Items.FirstOrDefault(i => i.ItemId == itemId);
            if (item == null)
            {
                string name = this.itemLookup.GetItemName(itemId);
                if (!string.IsNullOrEmpty(name))
                {
                    item = Items.FirstOrDefault(i => i.ItemName.Equals(name, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (item != null)
            {
                item.BoughtQuantity += quantityBought;
                if (this.configuration.AutoRemovePurchasedItems && item.RemainingQuantity <= 0)
                {
                    Items.Remove(item);
                }
                else
                {
                    int toDeduct = quantityBought;

                    // 1. Try to deduct from the current world's allocation first to keep the plan in sync
                    string currentWorld = StingyShopper.Plugin.ObjectTable.LocalPlayer?.CurrentWorld.Value.Name.ExtractText() ?? string.Empty;
                    if (!string.IsNullOrEmpty(currentWorld))
                    {
                        var currentWorldAlloc = item.Allocations.FirstOrDefault(a => a.WorldName.Equals(currentWorld, StringComparison.OrdinalIgnoreCase));
                        if (currentWorldAlloc != null && currentWorldAlloc.Quantity > 0)
                        {
                            int deduct = Math.Min(toDeduct, currentWorldAlloc.Quantity);
                            currentWorldAlloc.Quantity -= deduct;

                            if (currentWorldAlloc.Quantity == 0)
                            {
                                currentWorldAlloc.TotalCost = 0;
                            }
                            else
                            {
                                currentWorldAlloc.TotalCost = (uint)Math.Max(0, (long)currentWorldAlloc.TotalCost - (deduct * currentWorldAlloc.UnitPrice));
                            }
                            toDeduct -= deduct;
                        }
                    }

                    // 2. Deduct remaining quantity sequentially from the rest of the allocations
                    for (int i = 0; i < item.Allocations.Count; i++)
                    {
                        if (toDeduct <= 0) break;
                        var alloc = item.Allocations[i];
                        if (alloc.Quantity <= 0) continue;

                        int deduct = Math.Min(toDeduct, alloc.Quantity);
                        alloc.Quantity -= deduct;
                        
                        if (alloc.Quantity == 0)
                        {
                            alloc.TotalCost = 0;
                        }
                        else
                        {
                            alloc.TotalCost = (uint)Math.Max(0, (long)alloc.TotalCost - (deduct * alloc.UnitPrice));
                        }
                        toDeduct -= deduct;
                    }

                    // Remove empty allocations
                    item.Allocations.RemoveAll(a => a.Quantity <= 0);
                }

                ReconstructGroupedPlan();
                SaveToConfig();
            }
        }

        public void RegisterPurchase(string itemName, int quantityBought)
        {
            var item = Items.FirstOrDefault(i => i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                RegisterPurchase(item.ItemId, quantityBought);
            }
        }
    }
}
