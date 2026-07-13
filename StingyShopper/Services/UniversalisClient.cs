using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Networking.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingyShopper.Services
{
    public class UniversalisListing
    {
        [JsonProperty("pricePerUnit")]
        public uint PricePerUnit { get; set; }

        [JsonProperty("quantity")]
        public uint Quantity { get; set; }

        [JsonProperty("worldName")]
        public string WorldName { get; set; } = string.Empty;

        [JsonProperty("worldID")]
        public uint? WorldID { get; set; }

        [JsonProperty("hq")]
        public bool IsHq { get; set; }
    }

    public class UniversalisItemData
    {
        [JsonProperty("itemID")]
        public uint ItemID { get; set; }

        [JsonProperty("worldName")]
        public string? WorldName { get; set; }

        [JsonProperty("listings")]
        public List<UniversalisListing> Listings { get; set; } = new();
    }

    public class UniversalisClient : IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly HappyEyeballsCallback happyEyeballsCallback;
        private const int MaxItemsPerRequest = 5; // Reduced to 5 for detailed listings to prevent 504 timeouts

        public UniversalisClient()
        {
            this.happyEyeballsCallback = new HappyEyeballsCallback();
            this.httpClient = new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectCallback = this.happyEyeballsCallback.ConnectCallback
            });
            this.httpClient.Timeout = TimeSpan.FromSeconds(15);
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StingyShopper-DalamudPlugin/1.0");
        }

        public async Task<Dictionary<uint, UniversalisItemData>> FetchMarketDataAsync(string worldOrDC, IEnumerable<uint> itemIds, Action<string>? statusCallback = null)
        {
            var result = new Dictionary<uint, UniversalisItemData>();
            var idList = itemIds.Distinct().ToList();
            if (idList.Count == 0 || string.IsNullOrWhiteSpace(worldOrDC)) return result;

            int failedBatches = 0;
            int totalItems = idList.Count;

            for (int i = 0; i < idList.Count; i += MaxItemsPerRequest)
            {
                var chunk = idList.GetRange(i, Math.Min(MaxItemsPerRequest, idList.Count - i));
                string idsParam = string.Join(",", chunk);
                string url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(worldOrDC)}/{idsParam}?listings=15&entries=0";

                const int MaxRetries = 3;

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        if (attempt > 1)
                        {
                            statusCallback?.Invoke($"Fetching: {i}/{totalItems} processed. Retrying failed batch (Attempt {attempt}/{MaxRetries})...");
                            await Task.Delay(2000 * (attempt - 1));
                        }

                        string json = await this.httpClient.GetStringAsync(url);
                        var jObj = JObject.Parse(json);

                        if (chunk.Count == 1)
                        {
                            var itemData = jObj.ToObject<UniversalisItemData>();
                            if (itemData != null)
                            {
                                result[itemData.ItemID] = itemData;
                            }
                        }
                        else
                        {
                            var itemsToken = jObj["items"];
                            if (itemsToken != null)
                            {
                                foreach (var prop in itemsToken.Children<JProperty>())
                                {
                                    if (uint.TryParse(prop.Name, out uint itemId))
                                    {
                                        var itemData = prop.Value.ToObject<UniversalisItemData>();
                                        if (itemData != null)
                                        {
                                            result[itemId] = itemData;
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == MaxRetries)
                        {
                            Plugin.PluginLog.Warning(ex, $"[StingyShopper] Universalis API fetch failed for url: {url} after {MaxRetries} attempts.");
                            failedBatches++;
                        }
                        else
                        {
                            Plugin.PluginLog.Warning($"[StingyShopper] Universalis API request failed (Attempt {attempt}/{MaxRetries}): {ex.Message}");
                        }
                    }
                }

                // Invoke progress status callback
                int currentProcessed = Math.Min(i + chunk.Count, idList.Count);
                if (failedBatches > 0)
                {
                    statusCallback?.Invoke($"Fetching: {currentProcessed}/{totalItems} processed ({failedBatches} batches failed)...");
                }
                else
                {
                    statusCallback?.Invoke($"Fetching: {currentProcessed}/{totalItems} items processed...");
                }

                // Add 500ms delay between batches to avoid 504 Gateway Timeouts on Universalis database
                if (i + MaxItemsPerRequest < idList.Count)
                {
                    await Task.Delay(500);
                }
            }

            return result;
        }

        public void Dispose()
        {
            this.httpClient.Dispose();
        }
    }
}
