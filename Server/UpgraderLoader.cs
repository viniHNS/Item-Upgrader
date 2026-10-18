using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ItemUpgrader;

/// <summary>
/// Runs after every other mod: builds the result catalog (so modded items and presets are included)
/// and adds the UI texts to the locales.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class UpgraderLoader(ISptLogger<UpgraderLoader> logger, UpgradeService upgradeService, LocaleTable localeTable) : IOnLoad
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["itemupgrader_menu"] = "Upgrade item",
        ["itemupgrader_title"] = "ITEM UPGRADER",
        ["itemupgrader_base"] = "ITEM TO UPGRADE",
        ["itemupgrader_target"] = "RESULT",
        ["itemupgrader_chance"] = "CHANCE",
        ["itemupgrader_value"] = "Value",
        ["itemupgrader_amount"] = "Amount",
        ["itemupgrader_upgrade"] = "UPGRADE",
        ["itemupgrader_again"] = "UPGRADE AGAIN",
        ["itemupgrader_close"] = "CLOSE",
        ["itemupgrader_cancel"] = "CANCEL",
        ["itemupgrader_success"] = "SUCCESS",
        ["itemupgrader_failed"] = "FAILED",
        ["itemupgrader_rolling"] = "ROLLING...",
        ["itemupgrader_search"] = "Search item...",
        ["itemupgrader_all"] = "ALL",
        ["itemupgrader_select_target"] = "Choose the result item from the list below",
        ["itemupgrader_loading"] = "Evaluating item...",
        ["itemupgrader_no_results"] = "No item matches this filter",
        ["itemupgrader_confirm"] = "Upgrade <b>{0}</b> into <b>{1}</b> with a <b>{2}%</b> chance?\n\nIf it fails, the item is lost.",
        ["itemupgrader_won"] = "You received {0}",
        ["itemupgrader_lost"] = "{0} was lost",
        ["itemupgrader_err_not_in_stash"] = "This item cannot be upgraded from here",
        ["itemupgrader_err_container_not_empty"] = "Empty the container first",
        ["itemupgrader_err_quest_item"] = "Quest items cannot be upgraded",
        ["itemupgrader_err_no_value"] = "This item has no value",
        ["itemupgrader_err_money"] = "Money cannot be upgraded",
        ["itemupgrader_err_target"] = "Invalid result item or amount",
        ["itemupgrader_err_space"] = "Not enough space in the stash for the result",
        ["itemupgrader_err_server"] = "The server rejected the upgrade",
    };

    private static readonly Dictionary<string, string> Portuguese = new()
    {
        ["itemupgrader_menu"] = "Melhorar item",
        ["itemupgrader_title"] = "MELHORIA DE ITEM",
        ["itemupgrader_base"] = "ITEM A MELHORAR",
        ["itemupgrader_target"] = "RESULTADO",
        ["itemupgrader_chance"] = "CHANCE",
        ["itemupgrader_value"] = "Valor",
        ["itemupgrader_amount"] = "Quantidade",
        ["itemupgrader_upgrade"] = "MELHORAR",
        ["itemupgrader_again"] = "MELHORAR DE NOVO",
        ["itemupgrader_close"] = "FECHAR",
        ["itemupgrader_cancel"] = "CANCELAR",
        ["itemupgrader_success"] = "SUCESSO",
        ["itemupgrader_failed"] = "FALHOU",
        ["itemupgrader_rolling"] = "SORTEANDO...",
        ["itemupgrader_search"] = "Buscar item...",
        ["itemupgrader_all"] = "TODOS",
        ["itemupgrader_select_target"] = "Escolha o item resultante na lista abaixo",
        ["itemupgrader_loading"] = "Avaliando item...",
        ["itemupgrader_no_results"] = "Nenhum item neste filtro",
        ["itemupgrader_confirm"] = "Melhorar <b>{0}</b> para <b>{1}</b> com <b>{2}%</b> de chance?\n\nSe falhar, o item é perdido.",
        ["itemupgrader_won"] = "Você recebeu {0}",
        ["itemupgrader_lost"] = "{0} foi perdido",
        ["itemupgrader_err_not_in_stash"] = "Este item não pode ser melhorado daqui",
        ["itemupgrader_err_container_not_empty"] = "Esvazie o container primeiro",
        ["itemupgrader_err_quest_item"] = "Itens de quest não podem ser melhorados",
        ["itemupgrader_err_no_value"] = "Este item não tem valor",
        ["itemupgrader_err_money"] = "Dinheiro não pode ser melhorado",
        ["itemupgrader_err_target"] = "Item resultante ou quantidade inválidos",
        ["itemupgrader_err_space"] = "Sem espaço no stash para o resultado",
        ["itemupgrader_err_server"] = "O servidor recusou a melhoria",
    };

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        upgradeService.EnsureCatalog();

        foreach (var (language, lazy) in localeTable.Global)
        {
            var texts = language == "po" ? Portuguese : English;
            lazy.AddTransformer(data =>
            {
                if (data is null)
                {
                    return data;
                }

                foreach (var (key, text) in texts)
                {
                    data[key] = text;
                }

                return data;
            });
        }

        logger.Info("[itemUpgrader] loaded");
        return Task.CompletedTask;
    }
}
