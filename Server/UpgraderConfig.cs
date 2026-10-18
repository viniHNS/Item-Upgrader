using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using Path = System.IO.Path;

namespace ItemUpgrader;

/// <summary>
/// Shape of config.json in the mod folder. Property names are matched case-insensitively.
/// </summary>
public class UpgraderConfig
{
    /// <summary>"flea" = prices.json with handbook fallback, "handbook" = handbook with flea fallback.</summary>
    public string PriceSource { get; set; } = "flea";

    /// <summary>Fraction kept by the house: chance = base / target * (1 - HouseEdge).</summary>
    public double HouseEdge { get; set; } = 0.05;

    /// <summary>Lowest chance shown/used, in percent.</summary>
    public double MinChance { get; set; } = 0.5;

    /// <summary>Highest chance shown/used, in percent.</summary>
    public double MaxChance { get; set; } = 95;

    /// <summary>Mark the won item as found in raid.</summary>
    public bool FoundInRaid { get; set; } = true;

    /// <summary>Scale the base item value by durability / resource / uses left.</summary>
    public bool ScaleByCondition { get; set; } = true;

    public bool AllowMoneyAsBase { get; set; } = true;

    public bool AllowMoneyAsTarget { get; set; } = true;

    /// <summary>Worn gear, pockets, rig and backpack contents can be upgraded too (default: only the stash).</summary>
    public bool AllowEquippedItems { get; set; } = false;

    /// <summary>Items whose full-stack value is below this (roubles) are not offered as results.</summary>
    public double MinTargetValue { get; set; } = 500;

    /// <summary>How many full stacks of a stackable result can be requested at once.</summary>
    public int MaxTargetStacks { get; set; } = 10;

    /// <summary>
    /// Base classes (items.json parents) never offered as results. This default only applies when the key is
    /// missing from config.json; the shipped file keeps the structural classes here and lists every secure
    /// container and event crate one by one in targetTplBlacklist, so each can be allowed on its own.
    /// </summary>
    public List<string> TargetParentBlacklist { get; set; } =
    [
        "5448bf274bdc2dfc2f8b456a", // secure containers (MobContainer)
        "566abbb64bdc2d144c8b457d", // stash
        "557596e64bdc2dc2118b4571", // pockets
        "55d720f24bdc2d88028b456d", // inventory
        "6050cac987d3f925bf016837", // sorting table
        "566965d44bdc2d814c8b4571", // loot containers
        "567583764bdc2d98058b456e", // stationary containers
        "63da6da4784a55176c018dba", // hideout area containers
        "65649eb40bf0ed77b8044453", // built-in inserts
        "5447bee84bdc2dc3278b4569", // special weapons
        "62f109593b54472778797866", // random loot containers (sealed cases)
    ];

    /// <summary>Specific templates never offered as results.</summary>
    public List<string> TargetTplBlacklist { get; set; } = [];

    /// <summary>Write one log line per upgrade attempt.</summary>
    public bool LogRolls { get; set; } = true;
}

/// <summary>
/// Reads config.json once and shares it with every class that needs it.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class UpgraderConfigLoader(ISptLogger<UpgraderConfigLoader> logger, ModHelper modHelper)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private UpgraderConfig? _config;

    public UpgraderConfig Config => _config ??= Load();

    private UpgraderConfig Load()
    {
        var path = Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "config.json");
        try
        {
            var config = JsonSerializer.Deserialize<UpgraderConfig>(File.ReadAllText(path), JsonOptions) ?? new UpgraderConfig();
            config.HouseEdge = Math.Clamp(config.HouseEdge, 0, 0.95);
            config.MinChance = Math.Clamp(config.MinChance, 0.01, 100);
            config.MaxChance = Math.Clamp(config.MaxChance, config.MinChance, 100);
            config.MaxTargetStacks = Math.Max(1, config.MaxTargetStacks);
            return config;
        }
        catch (Exception ex)
        {
            logger.Error($"[itemUpgrader] Could not read {path}, using defaults: {ex.Message}");
            return new UpgraderConfig();
        }
    }
}
