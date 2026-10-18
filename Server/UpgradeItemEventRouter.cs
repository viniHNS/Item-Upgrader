using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace ItemUpgrader;

/// <summary>
/// Body of the "ItemUpgrade" item event sent by the client plugin through /client/game/profile/items/moving.
/// </summary>
public record UpgradeRequestData : BaseInteractionRequestData
{
    /// <summary>Id of the item instance being consumed.</summary>
    [JsonPropertyName("item")]
    public MongoId Item { get; set; }

    /// <summary>Template of the wanted result.</summary>
    [JsonPropertyName("tpl")]
    public MongoId Tpl { get; set; }

    /// <summary>Units of the result (1 for anything that does not stack).</summary>
    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;
}

/// <summary>
/// Handles the "ItemUpgrade" action inside the regular item event pipeline, so the response carries
/// profile changes that the client applies to its inventory like any vanilla operation.
/// </summary>
[Injectable(InjectionType.Transient, int.MaxValue)]
public sealed class UpgradeItemEventRouter(UpgradeService upgradeService) : ItemEventRouter(
    [
        new ItemRouteAction<UpgradeRequestData>(
            UpgradeService.ActionName,
            (url, pmcData, body, sessionId, output, cancellationToken) =>
                new ValueTask<ItemEventRouterResponse>(upgradeService.Upgrade(pmcData, body, sessionId, output))
        ),
    ]
);
