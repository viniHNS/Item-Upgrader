<div align="center">

# Item Upgrader

Gamble an item from your stash for a more valuable one. The odds come from the prices of the two items.

![Version](https://img.shields.io/badge/version-1.0.0-blue) ![SPT](https://img.shields.io/badge/SPT-4.1.6-green) ![.NET](https://img.shields.io/badge/.NET-10-purple) ![License](https://img.shields.io/badge/license-MIT-lightgrey)

[Features](#features) · [Install](#install) · [Configuration](#configuration) · [Build from source](#build-from-source) · [Project structure](#project-structure)

</div>

## Features

- Right-click any item in your stash and pick **Upgrade item**. The entry sits right above Discard, like a vanilla action.
- A dialog built from the game's own window parts opens: the item on the left, a chance dial in the middle and the result on the right.
- Choose the result from a searchable list of every item in the handbook (weapons come assembled), with quick filters for ~70%, ~50%, ~30% and ~10% odds. The list is sorted by chance, best odds first.
- Chance = value of your item ÷ value of the result × (1 − house edge), clamped between 0.5% and 95%. Values come from the flea price list with the handbook as fallback (configurable).
- Stackable results (ammo, money, barter stacks) have an amount field: type a number or use the - and + steppers (Shift steps by 10, Ctrl by 100). The chance updates as you type, and the quick filters pick the amount that lands on that chance.
- Weapons and armor are delivered assembled, as their default preset, and are valued as the sum of their parts.
- The base item is valued with its attachments and its condition (durability, medkit charges, food, fuel, key uses).
- On success the result goes to your stash as found in raid and the won item can be upgraded again right away. On failure the item is gone.
- Everything is decided by the server: the client only shows the dial and the roll. Works alongside UI Fixes.

## Install

Extract `itemUpgrader.zip` into your SPT folder. It contains:

```text
SPT_Runtime/user/mods/itemUpgrader/itemUpgrader.dll
SPT_Runtime/user/mods/itemUpgrader/config.json
BepInEx/plugins/itemUpgrader.dll
```

Both parts are required. Removing the mod never breaks profiles: it adds no items of its own.

## Configuration

### `SPT_Runtime/user/mods/itemUpgrader/config.json` (server)

| Key | Default | Description |
| --- | --- | --- |
| `priceSource` | `"flea"` | `"flea"` uses `prices.json` with the handbook as fallback; `"handbook"` the other way around. |
| `houseEdge` | `0.05` | Fraction kept by the house. `chance = base / target × (1 − houseEdge)`. |
| `minChance` | `0.5` | Lowest chance, in percent. |
| `maxChance` | `95` | Highest chance, in percent. |
| `foundInRaid` | `true` | The won item is marked found in raid. |
| `scaleByCondition` | `true` | Base value scales with durability, medkit resource, food, fuel and key uses. |
| `allowMoneyAsBase` | `true` | Money stacks can be upgraded. |
| `allowMoneyAsTarget` | `true` | Money can be the result. |
| `allowEquippedItems` | `false` | Also allow worn gear and the contents of pockets, rig and backpack. When off, only items in the stash can be upgraded. |
| `minTargetValue` | `500` | Items whose full stack is worth less than this are never offered. |
| `maxTargetStacks` | `10` | How many full stacks of a stackable result can be requested at once. |
| `targetParentBlacklist` | stashes, pockets, sorting table, map containers, built-in armor inserts, special weapons | Base classes never offered as results: things that cannot exist as a loose item. |
| `targetTplBlacklist` | every secure container (Alpha to Kappa, Theta, waist pouch, dev and tournament ones) and every event or random loot crate (sealed weapon cases, Twitch drop cases, Arena crates, Christmas gifts) | Single items never offered as results. Remove an id to allow that item, or add any item id to block it. |
| `logRolls` | `true` | One server log line per upgrade attempt. |

Restart the server after editing it.

### F12 menu (client)

| Setting | Default | Description |
| --- | --- | --- |
| Enable | on | Show the context menu entry. |
| Confirm before upgrading | on | Ask before the item is consumed. |
| Spin duration | 3.5 s | How long the needle spins before the result. |
| Sounds | on | Game UI sounds while spinning and on the result. |

## Build from source

Requires the .NET 10 SDK and an SPT 4.1.6 install. The client project references the game assemblies from `D:\Jogos\SPT4.1` by default; pass `-p:SptGameDir=<your SPT folder>` to change it. Close the SPT server before building, otherwise the deploy step cannot overwrite the DLL.

```sh
dotnet build itemUpgrader.sln -c Release
```

The build copies both DLLs into the SPT install and writes `itemUpgrader.zip` next to the solution.

## Project structure

```text
itemUpgrader/
├── itemUpgrader.sln
├── Server/                     .NET 10 server mod
│   ├── Mod.cs                  metadata
│   ├── UpgraderConfig.cs       config.json model and loader
│   ├── UpgradeService.cs       item values, chance, the upgrade itself
│   ├── UpgradeItemEventRouter.cs  "ItemUpgrade" item event
│   ├── UpgraderRouter.cs       /itemUpgrader/data, /quote, /last
│   ├── UpgraderLoader.cs       catalog build and locales (EN, PT-BR)
│   └── config.json
└── Client/                     BepInEx plugin
    ├── Plugin.cs               F12 settings, server data, patches
    ├── UpgraderData.cs         catalog, chance formula, eligibility
    ├── UpgraderApi.cs          quote, item event, last result
    ├── Patches/ContextMenuPatches.cs  menu entry, button order, Escape
    ├── UI/UpgradeWindow.cs     the dialog
    ├── UI/TargetListView.cs    result list
    └── UI/UiKit.cs             vanilla-styled UI helpers
```

## Resources

| Resource | URL |
|---|---|
| SPT Server C# | https://github.com/SP-Tushonka/server-csharp |
| Server Mod Examples | https://github.com/SP-Tushonka/server-mod-examples |
| SPT Wiki — Modding Resources | https://wiki.sp-tushonka.com/en/modding/Modding_Resources |
| SPT Scaffold | https://github.com/viniHNS/spt-scaffold |

## Credits

- Tyfon's UI Fixes for the dynamic context menu pattern.
