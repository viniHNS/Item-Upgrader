using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace ItemUpgrader;

/// <summary>
/// Routes read by the client plugin: result catalog + config, valuation of one base item, and the last roll.
/// </summary>
[Injectable]
public class UpgraderRouter(JsonUtil jsonUtil, UpgraderRouterCallbacks callbacks) : StaticRouter(
    jsonUtil,
    [
        new RouteAction<EmptyRequestData>(
            "/itemUpgrader/data",
            (url, info, sessionId, output, cancellationToken) => callbacks.GetData()
        ),
        new RouteAction<QuoteRequest>(
            "/itemUpgrader/quote",
            (url, info, sessionId, output, cancellationToken) => callbacks.Quote(info, sessionId)
        ),
        new RouteAction<EmptyRequestData>(
            "/itemUpgrader/last",
            (url, info, sessionId, output, cancellationToken) => callbacks.GetLast(sessionId)
        ),
    ]
);

public record QuoteRequest : IRequestData
{
    [JsonPropertyName("item")]
    public string? Item { get; set; }
}

public class DataResponse
{
    [JsonPropertyName("config")]
    public ClientConfig Config { get; set; } = new();

    /// <summary>Result tpl -> value / stack size / preset flag.</summary>
    [JsonPropertyName("targets")]
    public IReadOnlyDictionary<string, TargetEntry> Targets { get; set; } = new Dictionary<string, TargetEntry>();
}

/// <summary>The part of the server config the client needs to mirror the chance formula.</summary>
public class ClientConfig
{
    [JsonPropertyName("houseEdge")]
    public double HouseEdge { get; set; }

    [JsonPropertyName("minChance")]
    public double MinChance { get; set; }

    [JsonPropertyName("maxChance")]
    public double MaxChance { get; set; }

    [JsonPropertyName("maxTargetStacks")]
    public int MaxTargetStacks { get; set; }

    [JsonPropertyName("allowMoneyAsBase")]
    public bool AllowMoneyAsBase { get; set; }

    [JsonPropertyName("allowEquippedItems")]
    public bool AllowEquippedItems { get; set; }

    [JsonPropertyName("priceSource")]
    public string PriceSource { get; set; } = "flea";
}

public class QuoteResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("baseValue")]
    public double BaseValue { get; set; }

    [JsonPropertyName("items")]
    public List<ValuedItem> Items { get; set; } = [];
}

[Injectable]
public class UpgraderRouterCallbacks(HttpResponseUtil httpResponseUtil, UpgradeService upgradeService, UpgraderConfigLoader configLoader, ProfileHelper profileHelper)
{
    public ValueTask<string> GetData()
    {
        var config = configLoader.Config;
        var response = new DataResponse
        {
            Config = new ClientConfig
            {
                HouseEdge = config.HouseEdge,
                MinChance = config.MinChance,
                MaxChance = config.MaxChance,
                MaxTargetStacks = config.MaxTargetStacks,
                AllowMoneyAsBase = config.AllowMoneyAsBase,
                AllowEquippedItems = config.AllowEquippedItems,
                PriceSource = config.PriceSource,
            },
            Targets = upgradeService.Catalog,
        };

        return new ValueTask<string>(httpResponseUtil.NoBody(response));
    }

    public ValueTask<string> Quote(QuoteRequest request, MongoId sessionId)
    {
        var response = new QuoteResponse();
        try
        {
            var pmcData = sessionId.IsEmpty ? null : profileHelper.GetPmcProfile(sessionId);
            if (pmcData is null || string.IsNullOrEmpty(request?.Item) || !MongoId.IsValidMongoId(request.Item))
            {
                response.Error = "itemupgrader_err_not_in_stash";
                return new ValueTask<string>(httpResponseUtil.NoBody(response));
            }

            var valued = upgradeService.ValueBaseItem(pmcData, new MongoId(request.Item), out var error, out _);
            if (valued is null)
            {
                response.Error = error;
                return new ValueTask<string>(httpResponseUtil.NoBody(response));
            }

            response.Ok = true;
            response.Items = valued;
            response.BaseValue = Math.Round(valued.Sum(v => v.Value), 2);
        }
        catch (Exception ex)
        {
            response.Ok = false;
            response.Error = "itemupgrader_err_server";
            response.Items = [];
            _ = ex;
        }

        return new ValueTask<string>(httpResponseUtil.NoBody(response));
    }

    public ValueTask<string> GetLast(MongoId sessionId)
    {
        var last = upgradeService.GetLastResult(sessionId);
        return new ValueTask<string>(httpResponseUtil.NoBody(last ?? new UpgradeResult()));
    }
}
