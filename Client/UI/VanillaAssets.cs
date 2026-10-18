using System;
using System.Text;
using EFT.UI;
using EFT.UI.DragAndDrop;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace viniHNS.itemUpgrader
{
    /// <summary>
    /// Finds inactive instances of the game's own UI prefabs (they all live in the menu UI, loaded but hidden)
    /// so the upgrader can clone their sprites and widgets instead of drawing its own.
    /// </summary>
    internal static class VanillaAssets
    {
        private static SlotView _slot;
        private static SlotViewHeader _header;
        private static GridItemView _cell;
        private static SkillPanel _skill;
        private static ProgressSpinner _spinner;
        private static DefaultUIButton _dealButton;
        private static bool _dumped;

        /// <summary>Equipment slot: hatched empty background, empty and full borders.</summary>
        public static SlotView Slot
        {
            get
            {
                if (_slot == null)
                {
                    _slot = Find<SlotView>(s => s._slotBackground != null && s._emptyBorder != null && s.FullBorder != null);
                }

                return _slot;
            }
        }

        /// <summary>The "EARPIECE &gt;" style label above equipment slots.</summary>
        public static SlotViewHeader Header
        {
            get
            {
                if (_header == null)
                {
                    _header = Slot != null && Slot._slotHeader != null
                        ? Slot._slotHeader
                        : Find<SlotViewHeader>(h => h._headerText != null && h._backgroundIdle != null);
                }

                return _header;
            }
        }

        /// <summary>A stash cell item view: its border sprite frames item icons.</summary>
        public static GridItemView Cell
        {
            get
            {
                if (_cell == null)
                {
                    _cell = Find<GridItemView>(v => v._border != null && v._border.sprite != null);
                }

                return _cell;
            }
        }

        /// <summary>Skill progress ring of the character screen.</summary>
        public static SkillPanel Skill
        {
            get
            {
                if (_skill == null)
                {
                    _skill = Find<SkillPanel>(s => s._currentProgressFill != null && s._currentProgressFill.sprite != null && s._baseProgressFill != null);
                }

                return _skill;
            }
        }

        public static ProgressSpinner Spinner
        {
            get
            {
                if (_spinner == null)
                {
                    _spinner = Find<ProgressSpinner>(s => s._fillImage != null && s._fillImage.sprite != null);
                }

                return _spinner;
            }
        }

        /// <summary>The wide "DEAL!" button of the trader screen.</summary>
        public static DefaultUIButton DealButton
        {
            get
            {
                if (_dealButton == null)
                {
                    var screen = Find<TraderDealScreen>(t => t._dealButton != null);
                    _dealButton = screen != null ? screen._dealButton : null;
                }

                return _dealButton;
            }
        }

        private static T Find<T>(Func<T, bool> accept) where T : Object
        {
            try
            {
                foreach (var candidate in Resources.FindObjectsOfTypeAll<T>())
                {
                    if (candidate != null && accept(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"itemUpgrader: lookup of {typeof(T).Name} failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>One-time description of the borrowed prefabs, to tune the window against the real assets.</summary>
        public static void Dump()
        {
            if (_dumped || !Plugin.DebugLog.Value)
            {
                return;
            }

            _dumped = true;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("itemUpgrader: vanilla asset dump");
                Describe(sb, "SlotView", Slot != null ? Slot.gameObject : null, 3);
                Describe(sb, "SlotViewHeader", Header != null ? Header.gameObject : null, 3);
                Describe(sb, "GridItemView", Cell != null ? Cell.gameObject : null, 2);
                Describe(sb, "SkillPanel", Skill != null ? Skill.gameObject : null, 2);
                Describe(sb, "ProgressSpinner", Spinner != null ? Spinner.gameObject : null, 2);
                Describe(sb, "DealButton", DealButton != null ? DealButton.gameObject : null, 3);
                var context = ItemUiContext.Instance;
                Describe(sb, "MessageWindow", context != null && context._messageWindow != null ? context._messageWindow.gameObject : null, 6);
                var grid = Find<GridView>(g => g.gameObject != null);
                Describe(sb, "GridView", grid != null ? grid.gameObject : null, 2);
                Plugin.LogSource.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"itemUpgrader: asset dump failed: {ex.Message}");
            }
        }

        /// <summary>Same format as the asset dump, for one object built at runtime (debug log only).</summary>
        public static void LogTree(string label, GameObject root, int maxDepth)
        {
            if (!Plugin.DebugLog.Value)
            {
                return;
            }

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("itemUpgrader: tree of " + label);
                Describe(sb, label, root, maxDepth);
                Plugin.LogSource.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"itemUpgrader: tree dump of {label} failed: {ex.Message}");
            }
        }

        private static void Describe(StringBuilder sb, string label, GameObject root, int maxDepth)
        {
            sb.Append("--- ").Append(label).Append(": ");
            if (root == null)
            {
                sb.AppendLine("NOT FOUND");
                return;
            }

            sb.AppendLine(root.name);
            DescribeNode(sb, root.transform, 0, maxDepth);
        }

        private static void DescribeNode(StringBuilder sb, Transform node, int depth, int maxDepth)
        {
            var rect = node as RectTransform;
            sb.Append(new string(' ', depth * 2)).Append(node.name);
            if (rect != null)
            {
                sb.Append($" [{rect.rect.width:0}x{rect.rect.height:0}]");
            }

            if (!node.gameObject.activeSelf)
            {
                sb.Append(" (inactive)");
            }

            foreach (var group in node.GetComponents<CanvasGroup>())
            {
                sb.Append($" CanvasGroup(alpha={group.alpha:0.##})");
            }

            foreach (var image in node.GetComponents<Image>())
            {
                sb.Append($" Image(sprite={(image.sprite != null ? image.sprite.name : "null")}, type={image.type}, color={ColorUtility.ToHtmlStringRGBA(image.color)})");
            }

            foreach (var raw in node.GetComponents<RawImage>())
            {
                sb.Append($" RawImage(tex={(raw.texture != null ? raw.texture.name : "null")}, color={ColorUtility.ToHtmlStringRGBA(raw.color)})");
            }

            foreach (var text in node.GetComponents<TextMeshProUGUI>())
            {
                sb.Append($" TMP(font={(text.font != null ? text.font.name : "null")}, size={text.fontSize}, color={ColorUtility.ToHtmlStringRGBA(text.color)}, text='{text.text}')");
            }

            sb.AppendLine();
            if (depth >= maxDepth)
            {
                return;
            }

            for (var i = 0; i < node.childCount; i++)
            {
                DescribeNode(sb, node.GetChild(i), depth + 1, maxDepth);
            }
        }
    }
}
