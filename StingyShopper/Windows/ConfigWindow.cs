using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace StingyShopper.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Configuration configuration;

        public ConfigWindow(Configuration configuration) : base("StingyShopper Configuration###StingyShopperConfigWindow")
        {
            this.configuration = configuration;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(420, 360),
                MaximumSize = new Vector2(650, 550)
            };
        }

        public void Dispose() { }

        public override void Draw()
        {
            ImGui.TextUnformatted("Stinginess & Search Settings");
            ImGui.Separator();
            ImGui.Spacing();

            var currentMode = this.configuration.StinginessMode;
            ImGui.TextUnformatted("Stinginess Mode");
            ImGui.SetNextItemWidth(300.0f);
            if (ImGui.BeginCombo("##StinginessModeCombo", GetModeDisplayName(currentMode)))
            {
                foreach (StinginessMode mode in Enum.GetValues(typeof(StinginessMode)))
                {
                    bool isSelected = mode == currentMode;
                    if (ImGui.Selectable(GetModeDisplayName(mode), isSelected))
                    {
                        this.configuration.StinginessMode = mode;
                        this.configuration.Save();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(GetModeDescription(mode));
                    }
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Spacing();
            ImGui.PushTextWrapPos(0.0f);
            ImGui.TextDisabled(GetModeDescription(currentMode));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            if (this.configuration.StinginessMode == StinginessMode.Balanced)
            {
                float threshold = this.configuration.BalancedSavingsThresholdPercent;
                ImGui.TextUnformatted("Balanced Split Savings Threshold (%)");
                ImGui.SetNextItemWidth(300.0f);
                if (ImGui.SliderFloat("##BalancedThreshold", ref threshold, 5.0f, 50.0f, "%.1f%%"))
                {
                    this.configuration.BalancedSavingsThresholdPercent = threshold;
                    this.configuration.Save();
                }
                ImGui.PushTextWrapPos(0.0f);
                ImGui.TextDisabled("Only splits purchases across servers if overall gil savings exceed this %.");
                ImGui.PopTextWrapPos();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            bool enableRegion = this.configuration.EnableRegionWideSearch;
            if (ImGui.Checkbox("Enable Region-Wide Search (e.g. Europe / NA)", ref enableRegion))
            {
                this.configuration.EnableRegionWideSearch = enableRegion;
                this.configuration.Save();
            }

            if (enableRegion)
            {
                float crossDcThreshold = this.configuration.CrossDCSavingsThresholdPercent;
                ImGui.TextUnformatted("Cross-DC Savings Travel Threshold (%)");
                ImGui.SetNextItemWidth(300.0f);
                if (ImGui.SliderFloat("##CrossDCThreshold", ref crossDcThreshold, 5.0f, 50.0f, "%.1f%%"))
                {
                    this.configuration.CrossDCSavingsThresholdPercent = crossDcThreshold;
                    this.configuration.Save();
                }
                ImGui.PushTextWrapPos(0.0f);
                ImGui.TextDisabled("Only triggers cross-DC travel recommendations if savings exceed this threshold.");
                ImGui.PopTextWrapPos();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            bool autoCopy = this.configuration.AutoCopyItemNameToClipboard;
            if (ImGui.Checkbox("Auto-Copy Item Name to Clipboard on Select", ref autoCopy))
            {
                this.configuration.AutoCopyItemNameToClipboard = autoCopy;
                this.configuration.Save();
            }

            bool trackChat = this.configuration.TrackPurchasesFromChat;
            if (ImGui.Checkbox("Track Purchases Automatically via In-Game Chat", ref trackChat))
            {
                this.configuration.TrackPurchasesFromChat = trackChat;
                this.configuration.Save();
            }

            bool autoRemove = this.configuration.AutoRemovePurchasedItems;
            if (ImGui.Checkbox("Automatically Remove Fully Purchased Items from List", ref autoRemove))
            {
                this.configuration.AutoRemovePurchasedItems = autoRemove;
                this.configuration.Save();
            }
        }

        private static string GetModeDisplayName(StinginessMode mode) => mode switch
        {
            StinginessMode.MaxConvenience => "Maximum Convenience",
            StinginessMode.Balanced => "Balanced",
            StinginessMode.MaxStingy => "Maximum Stingy",
            _ => mode.ToString()
        };

        private static string GetModeDescription(StinginessMode mode) => mode switch
        {
            StinginessMode.MaxConvenience => "Bulk single-server optimization. Evaluates entire list costs\nacross all servers to find the single cheapest server to buy\nthe whole quantity, minimizing travel.",
            StinginessMode.Balanced => "Greedy threshold splitting. Starts at the cheapest single listing\nand splits to other servers only if their prices are within\nthe threshold % (unfulfilled fallback stays on the first server).",
            StinginessMode.MaxStingy => "Absolute minimum cost. Splits purchases across any number of\nservers, buying only the cheapest available units in the DC\nuntil target quantity is satisfied.",
            _ => string.Empty
        };
    }
}
