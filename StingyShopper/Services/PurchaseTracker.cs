using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;

namespace StingyShopper.Services
{
    public class PurchaseTracker : IDisposable
    {
        private readonly IChatGui chatGui;
        private readonly ShoppingListManager listManager;
        private readonly Configuration configuration;
        private readonly IPluginLog pluginLog;

        public PurchaseTracker(IChatGui chatGui, ShoppingListManager listManager, Configuration configuration, IPluginLog pluginLog)
        {
            this.chatGui = chatGui;
            this.listManager = listManager;
            this.configuration = configuration;
            this.pluginLog = pluginLog;

            this.chatGui.ChatMessage += OnChatMessage;
        }

        private void OnChatMessage(IHandleableChatMessage message)
        {
            if (!this.configuration.TrackPurchasesFromChat) return;

            var payloads = message.Message.Payloads;
            ItemPayload? itemPayload = null;
            int itemPayloadIndex = -1;

            for (int i = 0; i < payloads.Count; i++)
            {
                if (payloads[i] is ItemPayload ip)
                {
                    itemPayload = ip;
                    itemPayloadIndex = i;
                    break;
                }
            }

            if (itemPayload == null) return;

            // Combine all TextPayloads before the ItemPayload
            string textBefore = string.Empty;
            for (int i = 0; i < itemPayloadIndex; i++)
            {
                if (payloads[i] is TextPayload tp)
                {
                    textBefore += tp.Text;
                }
            }

            // We expect a message containing "purchase" (case-insensitive)
            if (!textBefore.Contains("purchase", StringComparison.OrdinalIgnoreCase)) return;

            // Extract the quantity. For example, "You purchase 25 " -> 25.
            var qtyMatch = Regex.Match(textBefore, @"\b(\d+)\b");
            int qty = 1;
            if (qtyMatch.Success)
            {
                int.TryParse(qtyMatch.Groups[1].Value, out qty);
            }

            // Register the purchase by ItemId!
            this.listManager.RegisterPurchase(itemPayload.ItemId, qty);
            this.pluginLog.Info($"[StingyShopper] Tracked purchase: {qty}x ItemID {itemPayload.ItemId}");
        }

        public void Dispose()
        {
            this.chatGui.ChatMessage -= OnChatMessage;
        }
    }
}
