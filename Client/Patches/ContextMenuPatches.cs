using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace viniHNS.itemUpgrader
{
    /// <summary>
    /// Adds the "Upgrade item" entry to the item context menu as a dynamic interaction, the same mechanism
    /// the game uses for insurers and UIFixes uses for "Search in stash", so no enum extension is needed.
    /// </summary>
    public class ContextMenuPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.GetItemContextInteractions));
        }

        [PatchPostfix]
        private static void Postfix(ItemContext itemContext, ContextInteractions<EItemInfoButton> __result)
        {
            try
            {
                Debug($"menu for {itemContext?.Item?.StringTemplateId ?? "null"}: view={itemContext?.ViewType}, interactions={__result?.GetType().Name ?? "null"}, ready={Plugin.Ready}, enabled={Plugin.EnableMod.Value}, inRaid={InRaid()}");
                if (!Plugin.Ready || !Plugin.EnableMod.Value || InRaid() || __result == null || itemContext?.Item == null)
                {
                    return;
                }

                // Only real item menus 
                if (!(__result is BaseItemContextInteractions) || __result.GetType().FullName == "UIFixes.EmptySlotMenu")
                {
                    Debug($"skip: interactions type {__result.GetType().FullName}");
                    return;
                }

                var item = itemContext.Item;
                switch (itemContext.ViewType)
                {
                    case EItemViewType.Inventory:
                    case EItemViewType.TradingPlayer:
                    case EItemViewType.Search:
                    case EItemViewType.SearchMinimal:
                        break;
                    default:
                        Debug($"skip {item.StringTemplateId}: view type {itemContext.ViewType}");
                        return;
                }

                var inventory = ItemUiContext.Instance?.ClientSession?.Profile?.Inventory;
                if (inventory == null)
                {
                    Debug($"skip {item.StringTemplateId}: no inventory on the session");
                    return;
                }

                if (!UpgraderData.CanUpgrade(item, inventory, out var reason))
                {
                    var root = UpgraderData.GetRootItem(item);
                    Debug($"skip {item.StringTemplateId} ({item.Name.Localized()}): {reason}; root={root?.GetType().Name}:{root?.Id}, stash={inventory.Stash?.Id}, sortingTable={inventory.SortingTable?.Id}, equipment={inventory.Equipment?.Id}, parent={item.Parent?.GetType().Name}, container={item.Parent?.Container?.GetType().Name}");
                    return;
                }

                Debug($"offer upgrade for {item.StringTemplateId} ({item.Name.Localized()})");

                var caption = UpgradeWindow.MenuCaption;
                if (string.IsNullOrWhiteSpace(caption))
                {
                    caption = "UPGRADE ITEM";
                }

                __result._dynamicInteractions[caption] = new DynamicContextInteraction(
                    "ItemUpgrade",
                    caption,
                    () => UpgradeWindow.Open(item),
                    Plugin.MenuIcon);
                Debug($"added dynamic interaction '{caption}' (now {__result._dynamicInteractions.Count} dynamic entries)");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"itemUpgrader: context menu patch failed: {ex}");
            }
        }

        private static bool InRaid()
        {
            var game = Singleton<AbstractGame>.Instance;
            return game != null && game.InRaid;
        }

        private static void Debug(string message)
        {
            if (Plugin.DebugLog.Value)
            {
                Plugin.LogSource.LogInfo("itemUpgrader: " + message);
            }
        }
    }

    /// <summary>
    /// Dynamic buttons are appended after every regular one, below DISCARD. Move ours right above it.
    /// </summary>
    public class ContextButtonOrderPatch : ModulePatch
    {
        private static Transform _discardButton;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(InteractionButtonsContainer),
                nameof(InteractionButtonsContainer.CreateContextButton),
                new[]
                {
                    typeof(string), typeof(string), typeof(SimpleContextMenuButton), typeof(RectTransform),
                    typeof(Sprite), typeof(Action), typeof(Action), typeof(bool), typeof(bool),
                });
        }

        [PatchPostfix]
        private static void Postfix(string key, SimpleContextMenuButton __result)
        {
            if (__result == null)
            {
                return;
            }

            if (key == EItemInfoButton.Discard.ToString())
            {
                _discardButton = __result.Transform;
                return;
            }

            if (!Plugin.Ready || key != UpgradeWindow.MenuCaption)
            {
                return;
            }

            var mine = __result.Transform;
            if (_discardButton != null && mine != null && _discardButton.parent == mine.parent)
            {
                mine.SetSiblingIndex(_discardButton.GetSiblingIndex());
            }
        }
    }

    /// <summary>
    /// Escape closes the upgrader window instead of the screen under it, like the split dialog does.
    /// </summary>
    public class EscapePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.TranslateCommand));
        }

        [PatchPrefix]
        private static bool Prefix(ItemUiContext __instance, ECommand command, ref InputNode.ETranslateResult __result)
        {
            if (!UpgradeWindow.IsOpen)
            {
                return true;
            }

            if (command != ECommand.Escape)
            {
                // Typing in the amount or search field must not trigger inventory hotkeys (same as the stash search window)
                if (UpgradeWindow.Instance.HasFocusedField)
                {
                    __result = InputNode.ETranslateResult.BlockAll;
                    return false;
                }

                return true;
            }

            // Let the confirmation dialog and the context menu take Escape first
            if (__instance._messageWindow != null && __instance._messageWindow.gameObject.activeSelf)
            {
                return true;
            }

            if (__instance.ContextMenu != null && __instance.ContextMenu.gameObject.activeSelf)
            {
                return true;
            }

            UpgradeWindow.Instance.RequestClose();
            __result = InputNode.ETranslateResult.Block;
            return false;
        }
    }
}
