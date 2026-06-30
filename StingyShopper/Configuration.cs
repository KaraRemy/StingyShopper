using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace StingyShopper
{
    public enum StinginessMode
    {
        MaxConvenience = 0,
        Balanced = 1,
        MaxStingy = 2
    }

    [Serializable]
    public class SavedPurchaseAllocation
    {
        public string WorldName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public uint UnitPrice { get; set; }
        public uint TotalCost { get; set; }
    }

    [Serializable]
    public class SavedShoppingItem
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int TargetQuantity { get; set; }
        public int BoughtQuantity { get; set; }
        public List<SavedPurchaseAllocation> Allocations { get; set; } = new();
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public StinginessMode StinginessMode { get; set; } = StinginessMode.MaxStingy;
        public float BalancedSavingsThresholdPercent { get; set; } = 15.0f;

        public bool EnableRegionWideSearch { get; set; } = false;
        public float CrossDCSavingsThresholdPercent { get; set; } = 20.0f;

        public bool AutoCopyItemNameToClipboard { get; set; } = true;
        public bool TrackPurchasesFromChat { get; set; } = true;

        public List<SavedShoppingItem> SavedItems { get; set; } = new();

        public bool HasAcknowledgedLivePricesWarning { get; set; } = false;

        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface?.SavePluginConfig(this);
        }
    }
}
