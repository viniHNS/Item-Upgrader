using System.Text.Json.Serialization;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Utils;

namespace ItemUpgrader;

/// <summary>One item template that can be chosen as the result of an upgrade.</summary>
public class TargetEntry
{
    /// <summary>Roubles for one unit (for weapons/armors: the default preset, fully assembled).</summary>
    [JsonPropertyName("v")]
    public double Value { get; set; }

    /// <summary>StackMaxSize of the template (1 = not stackable).</summary>
    [JsonPropertyName("s")]
    public int StackMaxSize { get; set; }

    /// <summary>True when the player receives the default preset instead of the bare item.</summary>
    [JsonPropertyName("p")]
    public bool HasPreset { get; set; }
}

/// <summary>Value of one item of the base item tree, for the client breakdown.</summary>
public class ValuedItem
{
    [JsonPropertyName("tpl")]
    public string Tpl { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("factor")]
    public double Factor { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }
}

/// <summary>Result of the last upgrade of a session, for the client to display.</summary>
public class UpgradeResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("chance")]
    public double Chance { get; set; }

    [JsonPropertyName("roll")]
    public double Roll { get; set; }

    [JsonPropertyName("baseValue")]
    public double BaseValue { get; set; }

    [JsonPropertyName("targetValue")]
    public double TargetValue { get; set; }

    [JsonPropertyName("baseTpl")]
    public string BaseTpl { get; set; } = "";

    [JsonPropertyName("targetTpl")]
    public string TargetTpl { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("newItemIds")]
    public List<string> NewItemIds { get; set; } = [];
}

/// <summary>
/// Item values, chance formula and the upgrade itself. Shared by the HTTP routes and the item event router.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class UpgradeService(
    ISptLogger<UpgradeService> logger,
    UpgraderConfigLoader configLoader,
    TemplateTable templateTable,
    HandbookHelper handbookHelper,
    PresetHelper presetHelper,
    ItemHelper itemHelper,
    ItemBaseClassService itemBaseClassService,
    ItemFilterService itemFilterService,
    InventoryHelper inventoryHelper,
    HttpResponseUtil httpResponseUtil,
    RandomUtil randomUtil
)
{
    public const string ActionName = "ItemUpgrade";

    private readonly Dictionary<string, TargetEntry> _catalog = [];
    private readonly Dictionary<MongoId, double> _unitValues = [];
    private readonly Dictionary<MongoId, double> _templateValues = [];
    private readonly Dictionary<MongoId, UpgradeResult> _lastResults = [];
    private readonly object _lock = new();
    private bool _built;

    private UpgraderConfig Config => configLoader.Config;

    /// <summary>Target tpl (string) -> entry. Built once, after every mod added its items.</summary>
    public IReadOnlyDictionary<string, TargetEntry> Catalog
    {
        get
        {
            EnsureCatalog();
            return _catalog;
        }
    }

    public UpgradeResult? GetLastResult(MongoId sessionId)
    {
        lock (_lock)
        {
            return _lastResults.GetValueOrDefault(sessionId);
        }
    }

    /// <summary>Builds the result catalog from the handbook. Safe to call more than once.</summary>
    public void EnsureCatalog()
    {
        if (_built)
        {
            return;
        }

        lock (_lock)
        {
            if (_built)
            {
                return;
            }

            var blacklistParents = Config.TargetParentBlacklist
                .Where(MongoId.IsValidMongoId)
                .Select(id => new MongoId(id))
                .ToList();
            var blacklistTpls = Config.TargetTplBlacklist.ToHashSet();
            var skipped = 0;

            foreach (var handbookItem in templateTable.Handbook.Items)
            {
                var tpl = handbookItem.Id;
                if (blacklistTpls.Contains(tpl.ToString()))
                {
                    continue;
                }

                if (!templateTable.Items.TryGetValue(tpl, out var template) || template.Type != "Item" || template.Properties is null)
                {
                    continue;
                }

                if (template.Properties.QuestItem == true || itemFilterService.IsItemBlacklisted(tpl))
                {
                    skipped++;
                    continue;
                }

                if (blacklistParents.Count > 0 && itemBaseClassService.ItemHasBaseClass(tpl, blacklistParents))
                {
                    skipped++;
                    continue;
                }

                if (!Config.AllowMoneyAsTarget && itemBaseClassService.ItemHasBaseClass(tpl, BaseClasses.MONEY))
                {
                    skipped++;
                    continue;
                }

                var stackMax = Math.Max(1, template.Properties.StackMaxSize ?? 1);
                var value = GetTemplateValue(tpl);
                if (value <= 0 || value * stackMax < Config.MinTargetValue)
                {
                    skipped++;
                    continue;
                }

                _catalog[tpl.ToString()] = new TargetEntry
                {
                    Value = Math.Round(value, 2),
                    StackMaxSize = stackMax,
                    HasPreset = HasUsablePreset(tpl),
                };

                // Secure containers and loot crates are blocked one by one in targetTplBlacklist; one that is not
                // listed (usually added by another mod) is allowed, so say it to whoever reads the log
                if (itemBaseClassService.ItemHasBaseClass(tpl, BaseClasses.MOB_CONTAINER)
                    || itemBaseClassService.ItemHasBaseClass(tpl, BaseClasses.RANDOM_LOOT_CONTAINER))
                {
                    logger.Warning($"[itemUpgrader] {tpl} ({template.Name}) is a secure container or loot crate and can be won; add it to targetTplBlacklist to block it");
                }
            }

            _built = true;
            logger.Info($"[itemUpgrader] {_catalog.Count} result items available ({skipped} skipped), price source: {Config.PriceSource}");
        }
    }

    /// <summary>Roubles for one unit of a template, by the configured price source, 0 when unknown.</summary>
    public double GetUnitValue(MongoId tpl)
    {
        if (_unitValues.TryGetValue(tpl, out var cached))
        {
            return cached;
        }

        var handbook = handbookHelper.GetTemplatePrice(tpl);
        var flea = templateTable.Prices.GetValueOrDefault(tpl, 0);
        var value = string.Equals(Config.PriceSource, "handbook", StringComparison.OrdinalIgnoreCase)
            ? (handbook > 0 ? handbook : flea)
            : (flea > 0 ? flea : handbook);

        _unitValues[tpl] = value;
        return value;
    }

    /// <summary>Value the player actually receives: the default preset summed when there is one, otherwise the bare item.</summary>
    public double GetTemplateValue(MongoId tpl)
    {
        if (_templateValues.TryGetValue(tpl, out var cached))
        {
            return cached;
        }

        var value = GetUnitValue(tpl);
        var preset = HasUsablePreset(tpl) ? presetHelper.GetDefaultPreset(tpl) : null;
        if (preset?.Items is { Count: > 0 })
        {
            value = preset.Items.Sum(part => GetUnitValue(part.Template) * Math.Max(1, (int)(part.Upd?.StackObjectsCount ?? 1)));
        }

        _templateValues[tpl] = value;
        return value;
    }

    private bool HasUsablePreset(MongoId tpl)
    {
        try
        {
            return presetHelper.HasPreset(tpl) && presetHelper.GetDefaultPreset(tpl)?.Items is { Count: > 0 };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Chance in percent, clamped to the configured range.</summary>
    public double ComputeChance(double baseValue, double targetValue)
    {
        if (baseValue <= 0 || targetValue <= 0)
        {
            return Config.MinChance;
        }

        var chance = baseValue / targetValue * (1 - Config.HouseEdge) * 100;
        return Math.Clamp(chance, Config.MinChance, Config.MaxChance);
    }

    /// <summary>
    /// Checks the base item and values it. Returns null and an error key when it cannot be upgraded.
    /// </summary>
    public List<ValuedItem>? ValueBaseItem(PmcData pmcData, MongoId itemId, out string? error, out List<Item> itemWithChildren)
    {
        error = null;
        itemWithChildren = [];
        var items = pmcData.Inventory?.Items;
        if (items is null)
        {
            error = "itemupgrader_err_not_in_stash";
            return null;
        }

        var byId = new Dictionary<string, Item>();
        foreach (var item in items)
        {
            byId[item.Id.ToString()] = item;
        }

        if (!byId.TryGetValue(itemId.ToString(), out var root))
        {
            error = "itemupgrader_err_not_in_stash";
            return null;
        }

        if (!IsOwnedByPlayer(pmcData, root, byId))
        {
            error = "itemupgrader_err_not_in_stash";
            return null;
        }

        if (!templateTable.Items.TryGetValue(root.Template, out var rootTemplate) || rootTemplate.Properties is null)
        {
            error = "itemupgrader_err_no_value";
            return null;
        }

        // Secure containers and structural containers are never a base
        if (itemBaseClassService.ItemHasBaseClass(root.Template, NeverBaseClasses))
        {
            error = "itemupgrader_err_not_in_stash";
            return null;
        }

        if (rootTemplate.Properties.QuestItem == true)
        {
            error = "itemupgrader_err_quest_item";
            return null;
        }

        if (!Config.AllowMoneyAsBase && itemBaseClassService.ItemHasBaseClass(root.Template, BaseClasses.MONEY))
        {
            error = "itemupgrader_err_money";
            return null;
        }

        itemWithChildren = items.GetItemWithChildren(itemId);
        if (HasLooseContents(itemWithChildren, byId))
        {
            error = "itemupgrader_err_container_not_empty";
            return null;
        }

        var valued = new List<ValuedItem>();
        foreach (var item in itemWithChildren)
        {
            var unit = GetUnitValue(item.Template);
            var count = Math.Max(1, (int)(item.Upd?.StackObjectsCount ?? 1));
            var factor = ConditionFactor(item);
            valued.Add(new ValuedItem
            {
                Tpl = item.Template.ToString(),
                Count = count,
                Factor = Math.Round(factor, 4),
                Value = Math.Round(unit * count * factor, 2),
            });
        }

        if (valued.Sum(v => v.Value) <= 0)
        {
            error = "itemupgrader_err_no_value";
            return null;
        }

        return valued;
    }

    private static readonly List<MongoId> NeverBaseClasses =
    [
        BaseClasses.MOB_CONTAINER,
        BaseClasses.POCKETS,
        BaseClasses.STASH,
        BaseClasses.SORTING_TABLE,
        BaseClasses.INVENTORY,
    ];

    /// <summary>Stash, sorting table or (when allowed) worn gear and whatever is inside it.</summary>
    private bool IsOwnedByPlayer(PmcData pmcData, Item item, Dictionary<string, Item> byId)
    {
        var stashId = pmcData.Inventory?.Stash?.ToString();
        var sortingTableId = pmcData.Inventory?.SortingTable?.ToString();
        var equipmentId = Config.AllowEquippedItems ? pmcData.Inventory?.Equipment?.ToString() : null;
        var current = item;
        var guard = 0;
        while (current.ParentId is not null && guard++ < 64)
        {
            if (current.ParentId == stashId || current.ParentId == sortingTableId || (equipmentId is not null && current.ParentId == equipmentId))
            {
                return true;
            }

            if (!byId.TryGetValue(current.ParentId, out var parent))
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>True when any child sits in a grid of its parent (loose loot inside a container), as opposed to a slot (mods, plates, cartridges).</summary>
    private bool HasLooseContents(List<Item> itemWithChildren, Dictionary<string, Item> byId)
    {
        foreach (var child in itemWithChildren.Skip(1))
        {
            if (child.ParentId is null || child.SlotId is null || !byId.TryGetValue(child.ParentId, out var parent))
            {
                continue;
            }

            if (!templateTable.Items.TryGetValue(parent.Template, out var parentTemplate))
            {
                continue;
            }

            var grids = parentTemplate.Properties?.Grids;
            if (grids is not null && grids.Any(g => g.Name == child.SlotId))
            {
                return true;
            }
        }

        return false;
    }

    private double ConditionFactor(Item item)
    {
        if (!Config.ScaleByCondition || item.Upd is null)
        {
            return 1;
        }

        if (!templateTable.Items.TryGetValue(item.Template, out var template) || template.Properties is null)
        {
            return 1;
        }

        var props = template.Properties;
        var upd = item.Upd;

        if (upd.Repairable?.Durability is double durability)
        {
            var max = props.MaxDurability ?? upd.Repairable.MaxDurability ?? 0;
            if (max > 0)
            {
                return Math.Clamp(durability / max, 0, 1);
            }
        }

        if (upd.MedKit?.HpResource is double hp && props.MaxHpResource is int maxHp && maxHp > 0)
        {
            return Math.Clamp(hp / maxHp, 0, 1);
        }

        if (upd.FoodDrink?.HpPercent is double food && props.MaxResource is int maxFood && maxFood > 0)
        {
            return Math.Clamp(food / maxFood, 0, 1);
        }

        if (upd.Resource?.Value is double resource && props.MaxResource is int maxResource && maxResource > 0)
        {
            return Math.Clamp(resource / maxResource, 0, 1);
        }

        if (upd.Key?.NumberOfUsages is int used && props.MaximumNumberOfUsage is int maxUses && maxUses > 0)
        {
            return Math.Clamp((maxUses - used) / (double)maxUses, 0, 1);
        }

        return 1;
    }

    /// <summary>Validates the requested result. Returns null and an error key when it is not allowed.</summary>
    public TargetEntry? ValidateTarget(MongoId tpl, int count, out string? error)
    {
        error = null;
        if (!Catalog.TryGetValue(tpl.ToString(), out var entry))
        {
            error = "itemupgrader_err_target";
            return null;
        }

        var maxCount = entry.StackMaxSize > 1 ? entry.StackMaxSize * Config.MaxTargetStacks : 1;
        if (count < 1 || count > maxCount)
        {
            error = "itemupgrader_err_target";
            return null;
        }

        return entry;
    }

    /// <summary>Items the player receives on success, each root with its children in its own list.</summary>
    public List<List<Item>> BuildRewardItems(MongoId tpl, int count, TargetEntry entry)
    {
        var rewards = new List<List<Item>>();
        if (entry.HasPreset)
        {
            var preset = presetHelper.GetDefaultPreset(tpl);
            if (preset?.Items is { Count: > 0 })
            {
                var presetItems = preset.Items.ToList();
                presetItems.ReplaceIDs();
                presetItems[0].ParentId = null;
                presetItems[0].SlotId = null;
                presetItems[0].Location = null;
                presetItems[0].Upd ??= new Upd();
                presetItems[0].Upd!.StackObjectsCount = 1;
                rewards.Add(presetItems);
                return rewards;
            }
        }

        var item = new Item
        {
            Id = new MongoId(),
            Template = tpl,
            Upd = new Upd { StackObjectsCount = count },
        };

        foreach (var stack in itemHelper.SplitStack(item))
        {
            rewards.Add([stack]);
        }

        return rewards;
    }

    /// <summary>
    /// The upgrade itself: consumes the base item, rolls, and adds the result on success.
    /// Every failure before the roll leaves the profile untouched.
    /// </summary>
    public ItemEventRouterResponse Upgrade(PmcData pmcData, UpgradeRequestData request, MongoId sessionId, ItemEventRouterResponse output)
    {
        var valued = ValueBaseItem(pmcData, request.Item, out var error, out var itemWithChildren);
        if (valued is null)
        {
            return httpResponseUtil.AppendErrorToOutput(output, ErrorText(error), BackendErrorCodes.UnknownError);
        }

        var entry = ValidateTarget(request.Tpl, request.Count, out error);
        if (entry is null)
        {
            return httpResponseUtil.AppendErrorToOutput(output, ErrorText(error), BackendErrorCodes.UnknownError);
        }

        var baseValue = valued.Sum(v => v.Value);
        var targetValue = entry.Value * request.Count;
        var chance = ComputeChance(baseValue, targetValue);

        var rewards = BuildRewardItems(request.Tpl, request.Count, entry);

        // Remove first so the freed cells count, then make sure the result fits before anything is rolled.
        var removedIds = itemWithChildren.Select(i => i.Id).ToHashSet();
        var removedInsurance = pmcData.InsuredItems?.Where(i => i.ItemId.HasValue && removedIds.Contains(i.ItemId.Value)).ToList() ?? [];
        inventoryHelper.RemoveItem(pmcData, request.Item, sessionId, output);

        if (!inventoryHelper.CanPlaceItemsInInventory(sessionId, rewards))
        {
            Rollback(pmcData, request.Item, itemWithChildren, removedInsurance, sessionId, output);
            return httpResponseUtil.AppendErrorToOutput(output, ErrorText("itemupgrader_err_space"), BackendErrorCodes.NotEnoughSpace);
        }

        var roll = Math.Round(randomUtil.GetDouble(0, 100), 4);
        var success = roll < chance;
        var result = new UpgradeResult
        {
            Success = success,
            Chance = Math.Round(chance, 4),
            Roll = roll,
            BaseValue = Math.Round(baseValue, 2),
            TargetValue = Math.Round(targetValue, 2),
            BaseTpl = itemWithChildren[0].Template.ToString(),
            TargetTpl = request.Tpl.ToString(),
            Count = request.Count,
        };

        if (success)
        {
            inventoryHelper.AddItemsToStash(sessionId, new AddItemsDirectRequest
            {
                ItemsWithModsToAdd = rewards,
                FoundInRaid = Config.FoundInRaid,
                UseSortingTable = false,
                Callback = null,
            }, pmcData, output);

            if (output.Warnings is { Count: > 0 })
            {
                // Should not happen after the placement check; give the base item back rather than lose it.
                Rollback(pmcData, request.Item, itemWithChildren, removedInsurance, sessionId, output);
                return httpResponseUtil.AppendErrorToOutput(output, ErrorText("itemupgrader_err_space"), BackendErrorCodes.NotEnoughSpace);
            }

            result.NewItemIds = rewards.Select(r => r[0].Id.ToString()).ToList();
        }

        lock (_lock)
        {
            _lastResults[sessionId] = result;
        }

        if (Config.LogRolls)
        {
            logger.Info($"[itemUpgrader] {result.BaseTpl} ({result.BaseValue:N0}) -> {result.TargetTpl} x{request.Count} ({result.TargetValue:N0}): chance {result.Chance:F2}%, roll {roll:F2} => {(success ? "SUCCESS" : "FAILED")}");
        }

        return output;
    }

    private static void Rollback(PmcData pmcData, MongoId rootId, List<Item> itemWithChildren, List<InsuredItem> insurance, MongoId sessionId, ItemEventRouterResponse output)
    {
        pmcData.Inventory!.Items!.AddRange(itemWithChildren);
        if (insurance.Count > 0)
        {
            (pmcData.InsuredItems ??= []).AddRange(insurance);
        }

        if (output.ProfileChanges.TryGetValue(sessionId, out var change) && change.Items?.DeletedItems is not null)
        {
            change.Items.DeletedItems.RemoveAll(d => d.Id == rootId);
        }
    }

    private static string ErrorText(string? key)
    {
        return key switch
        {
            "itemupgrader_err_not_in_stash" => "Item Upgrader: the item must be in your stash.",
            "itemupgrader_err_container_not_empty" => "Item Upgrader: empty the container first.",
            "itemupgrader_err_quest_item" => "Item Upgrader: quest items cannot be upgraded.",
            "itemupgrader_err_no_value" => "Item Upgrader: this item has no value.",
            "itemupgrader_err_money" => "Item Upgrader: money cannot be upgraded.",
            "itemupgrader_err_target" => "Item Upgrader: invalid result item or amount.",
            "itemupgrader_err_space" => "Not enough space in the stash for the result.",
            _ => "Item Upgrader: unknown error.",
        };
    }
}
