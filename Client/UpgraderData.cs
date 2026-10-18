using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace viniHNS.itemUpgrader
{
    public class TargetEntry
    {
        /// <summary>Roubles for one unit (weapons: the default preset).</summary>
        [JsonProperty("v")]
        public double Value;

        /// <summary>StackMaxSize of the template, 1 when it does not stack.</summary>
        [JsonProperty("s")]
        public int StackMaxSize = 1;

        /// <summary>The player receives the default preset.</summary>
        [JsonProperty("p")]
        public bool HasPreset;
    }

    public class ClientConfig
    {
        [JsonProperty("houseEdge")]
        public double HouseEdge = 0.05;

        [JsonProperty("minChance")]
        public double MinChance = 0.5;

        [JsonProperty("maxChance")]
        public double MaxChance = 95;

        [JsonProperty("maxTargetStacks")]
        public int MaxTargetStacks = 10;

        [JsonProperty("allowMoneyAsBase")]
        public bool AllowMoneyAsBase = true;

        [JsonProperty("allowEquippedItems")]
        public bool AllowEquippedItems = false;

        [JsonProperty("priceSource")]
        public string PriceSource = "flea";
    }

    public class DataResponse
    {
        [JsonProperty("config")]
        public ClientConfig Config = new ClientConfig();

        [JsonProperty("targets")]
        public Dictionary<string, TargetEntry> Targets = new Dictionary<string, TargetEntry>();
    }

    /// <summary>One result item, with its localized names resolved once.</summary>
    public class TargetItem
    {
        public string Tpl;
        public TargetEntry Entry;
        public string Name;
        public string ShortName;
        public string SearchKey;

        public bool Stackable => Entry.StackMaxSize > 1;

        public int MaxAmount => Stackable ? Entry.StackMaxSize * Math.Max(1, UpgraderData.Config.MaxTargetStacks) : 1;
    }

    /// <summary>
    /// Config and result catalog fetched from the server mod (/itemUpgrader/data), plus the chance formula
    /// mirrored from the server so the window can show live numbers without a round trip.
    /// </summary>
    public static class UpgraderData
    {
        public static ClientConfig Config = new ClientConfig();

        private static Dictionary<string, TargetEntry> _targets = new Dictionary<string, TargetEntry>();
        private static List<TargetItem> _index;

        public static int TargetCount => _targets.Count;

        public static bool Load()
        {
            try
            {
                var json = RequestHandler.GetJson("/itemUpgrader/data");
                var response = JsonConvert.DeserializeObject<DataResponse>(json);
                if (response?.Targets == null || response.Targets.Count == 0)
                {
                    return false;
                }

                Config = response.Config ?? new ClientConfig();
                _targets = response.Targets;
                _index = null;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"itemUpgrader: failed to load data from the server: {ex}");
                return false;
            }
        }

        public static TargetEntry GetEntry(string tpl)
        {
            return tpl != null && _targets.TryGetValue(tpl, out var entry) ? entry : null;
        }

        /// <summary>All result items sorted by value. Names are localized on first use, so call it from the menu, not from Awake.</summary>
        public static IReadOnlyList<TargetItem> Targets
        {
            get
            {
                if (_index == null)
                {
                    _index = BuildIndex();
                }

                return _index;
            }
        }

        private static List<TargetItem> BuildIndex()
        {
            var list = new List<TargetItem>(_targets.Count);
            foreach (var pair in _targets)
            {
                var name = Localize(pair.Key + " Name");
                var shortName = Localize(pair.Key + " ShortName");
                if (name == null && shortName == null)
                {
                    continue;
                }

                name = name ?? shortName;
                shortName = shortName ?? name;
                list.Add(new TargetItem
                {
                    Tpl = pair.Key,
                    Entry = pair.Value,
                    Name = name,
                    ShortName = shortName,
                    SearchKey = (name + " " + shortName).ToLowerInvariant(),
                });
            }

            list.Sort((a, b) => a.Entry.Value.CompareTo(b.Entry.Value));
            return list;
        }

        /// <summary>Localized text, or null when the key has no translation.</summary>
        private static string Localize(string key)
        {
            try
            {
                var text = key.Localized();
                return string.IsNullOrEmpty(text) || text == key ? null : text;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Same formula as the server: base / target * (1 - edge), in percent, clamped.</summary>
        public static double Chance(double baseValue, double targetValue)
        {
            if (baseValue <= 0 || targetValue <= 0)
            {
                return Config.MinChance;
            }

            var chance = baseValue / targetValue * (1 - Config.HouseEdge) * 100;
            return Math.Min(Config.MaxChance, Math.Max(Config.MinChance, chance));
        }

        /// <summary>Units of a stackable result that give roughly the wanted chance (1 for anything else).</summary>
        public static int AmountForChance(TargetItem target, double baseValue, double chancePercent)
        {
            if (target == null || !target.Stackable || target.Entry.Value <= 0 || baseValue <= 0)
            {
                return 1;
            }

            var wantedValue = baseValue * (1 - Config.HouseEdge) * 100 / Math.Max(0.01, chancePercent);
            var amount = (int)Math.Round(wantedValue / target.Entry.Value);
            return Math.Max(1, Math.Min(target.MaxAmount, amount));
        }

        /// <summary>
        /// Cheap client-side checks that decide whether the menu entry appears. The server runs the same rules again.
        /// </summary>
        public static bool CanUpgrade(Item item, Inventory inventory, out string errorKey)
        {
            errorKey = null;
            if (item == null || inventory == null)
            {
                errorKey = "itemupgrader_err_not_in_stash";
                return false;
            }

            if (item.QuestItem)
            {
                errorKey = "itemupgrader_err_quest_item";
                return false;
            }

            if (!Config.AllowMoneyAsBase && item is Money)
            {
                errorKey = "itemupgrader_err_money";
                return false;
            }

            // Secure containers, pockets and the like are never a base; the server repeats this check
            if (item is MobContainer || item is Pockets || item is Stash || item is SortingTable || item is InventoryEquipment)
            {
                errorKey = "itemupgrader_err_not_in_stash";
                return false;
            }

            // The stash (or sorting table) must be somewhere up the parent chain. The chain does not stop
            // at the stash on the client (it goes on to the equipment), so test membership, not the root.
            var owned = IsInside(item, inventory.Stash)
                        || IsInside(item, inventory.SortingTable)
                        || (Config.AllowEquippedItems && IsInside(item, inventory.Equipment));
            if (!owned)
            {
                errorKey = "itemupgrader_err_not_in_stash";
                return false;
            }

            if (HasLooseContents(item))
            {
                errorKey = "itemupgrader_err_container_not_empty";
                return false;
            }

            return true;
        }

        /// <summary>True when <paramref name="container"/> is one of the item's ancestors.</summary>
        public static bool IsInside(Item item, Item container)
        {
            if (item == null || container == null)
            {
                return false;
            }

            var current = item;
            var guard = 0;
            while (current != null && guard++ < 64)
            {
                var parent = current.Parent?.Container?.ParentItem;
                if (parent == null)
                {
                    return false;
                }

                if (parent == container)
                {
                    return true;
                }

                current = parent;
            }

            return false;
        }

        public static Item GetRootItem(Item item)
        {
            var current = item;
            var guard = 0;
            while (current != null && guard++ < 64)
            {
                var parent = current.Parent?.Container?.ParentItem;
                if (parent == null)
                {
                    return current;
                }

                current = parent;
            }

            return current;
        }

        /// <summary>True when the item, or anything attached to it, has loose items inside a grid.</summary>
        public static bool HasLooseContents(Item item)
        {
            foreach (var nested in item.GetAllItems())
            {
                if (nested is CompoundItem compound && compound.Grids != null && compound.Grids.Any(grid => grid.Items.Any()))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
