using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using EFT.UI;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace viniHNS.itemUpgrader
{
    public class ValuedItem
    {
        [JsonProperty("tpl")]
        public string Tpl;

        [JsonProperty("count")]
        public int Count;

        [JsonProperty("factor")]
        public double Factor;

        [JsonProperty("value")]
        public double Value;
    }

    public class QuoteResponse
    {
        [JsonProperty("ok")]
        public bool Ok;

        [JsonProperty("error")]
        public string Error;

        [JsonProperty("baseValue")]
        public double BaseValue;

        [JsonProperty("items")]
        public List<ValuedItem> Items = new List<ValuedItem>();
    }

    public class LastResult
    {
        [JsonProperty("success")]
        public bool Success;

        [JsonProperty("chance")]
        public double Chance;

        [JsonProperty("roll")]
        public double Roll;

        [JsonProperty("baseValue")]
        public double BaseValue;

        [JsonProperty("targetValue")]
        public double TargetValue;

        [JsonProperty("targetTpl")]
        public string TargetTpl;

        [JsonProperty("newItemIds")]
        public List<string> NewItemIds = new List<string>();
    }

    /// <summary>
    /// The "ItemUpgrade" item event. Serialized by the game like any vanilla operation
    /// (the Action property is the route on the server).
    /// </summary>
    public class UpgradeOperationParams
    {
        [JsonProperty("Action")]
        public string Action = "ItemUpgrade";

        [JsonProperty("item")]
        public string Item;

        [JsonProperty("tpl")]
        public string Tpl;

        [JsonProperty("count")]
        public int Count;
    }

    public static class UpgraderApi
    {
        /// <summary>Asks the server how much the base item is worth (same valuation the upgrade will use).</summary>
        public static async Task<QuoteResponse> QuoteAsync(string itemId)
        {
            try
            {
                var json = await RequestHandler.PostJsonAsync("/itemUpgrader/quote", JsonConvert.SerializeObject(new { item = itemId }));
                return JsonConvert.DeserializeObject<QuoteResponse>(json) ?? new QuoteResponse { Error = "itemupgrader_err_server" };
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"itemUpgrader: quote failed: {ex}");
                return new QuoteResponse { Error = "itemupgrader_err_server" };
            }
        }

        /// <summary>Result of the last upgrade of this session (roll and chance), for display only.</summary>
        public static async Task<LastResult> LastAsync()
        {
            try
            {
                var json = await RequestHandler.GetJsonAsync("/itemUpgrader/last");
                return JsonConvert.DeserializeObject<LastResult>(json);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"itemUpgrader: could not read the last result: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sends the upgrade through the regular item event pipeline. When the task completes, the profile
        /// changes (base item removed, result added) are already applied to the client inventory.
        /// </summary>
        public static Task<IResult> SendUpgradeAsync(string itemId, string tpl, int count)
        {
            var completion = new TaskCompletionSource<IResult>();
            var session = ItemUiContext.Instance?.Session;
            if (session == null)
            {
                completion.SetResult(new FailedResult("itemupgrader_err_server", 0));
                return completion.Task;
            }

            var operation = new UpgradeOperationParams
            {
                Item = itemId,
                Tpl = tpl,
                Count = count,
            };

            try
            {
                session.SendOperationRightNow(operation, new Callback(result => completion.TrySetResult(result)));
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"itemUpgrader: could not send the upgrade: {ex}");
                completion.TrySetResult(new FailedResult("itemupgrader_err_server", 0));
            }

            return completion.Task;
        }
    }
}
