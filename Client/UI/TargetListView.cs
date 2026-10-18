using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace viniHNS.itemUpgrader
{
    /// <summary>
    /// Scrollable list of result items in the style of the flea market rows: icon cell, name, amount,
    /// price and the chance against the current base item. Every match is listed: only the rows on screen
    /// exist and they are recycled while scrolling, so even the full catalog stays cheap.
    /// </summary>
    internal sealed class TargetListView
    {
        public enum Band
        {
            All,
            P70,
            P50,
            P30,
            P10,
        }

        private const float RowHeight = 56f;
        private const float IconSize = 46f;

        private sealed class Row
        {
            public RectTransform Rect;
            public Image Background;
            public Image Selection;
            public Image IconTint;
            public Image Icon;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Amount;
            public TextMeshProUGUI Value;
            public TextMeshProUGUI Chance;
            public Button Button;
            public TargetItem Item;
            public int Count;
            public int Index = -1;
        }

        public event Action<TargetItem, int> Selected;

        private readonly IconBinder _icons;
        private readonly RectTransform _content;
        private readonly ScrollRect _scroll;
        private readonly TextMeshProUGUI _empty;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Match> _matches = new List<Match>();
        private readonly float _viewportHeight;
        private int _firstRendered = -1;

        private struct Match
        {
            public TargetItem Target;
            public int Amount;
            public double Chance;
        }

        private double _baseValue;
        private Band _band = Band.P50;
        private string _search = string.Empty;
        private string _selectedTpl;

        public Band CurrentBand => _band;

        public TargetListView(Transform parent, float x, float y, float width, float height, IconBinder icons)
        {
            _icons = icons;

            var frame = UiKit.CreateImage(parent, "TargetList", new Color(0f, 0f, 0f, 0.25f));
            UiKit.TopLeft(frame.rectTransform, x, y, width, height);

            var scrollRoot = UiKit.CreateImage(frame.transform, "Scroll", new Color(0f, 0f, 0f, 0f));
            scrollRoot.raycastTarget = true;
            UiKit.Stretch(scrollRoot.rectTransform, 1f);
            scrollRoot.gameObject.AddComponent<RectMask2D>();

            _content = UiKit.CreateRect(scrollRoot.transform, "Content");
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(0f, 0f);

            _scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _content;
            _scroll.viewport = scrollRoot.rectTransform;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 45f;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.1f;
            _scroll.onValueChanged.AddListener(_ => RenderVisible(false));
            _viewportHeight = Mathf.Max(RowHeight, height - 2f);

            _empty = UiKit.CreateLabel(frame.transform, "Empty", UiKit.L("itemupgrader_no_results"), 18f, UiKit.TextDim, TextAlignmentOptions.Center);
            UiKit.Stretch(_empty.rectTransform);
            _empty.gameObject.SetActive(false);
        }

        public void SetBaseValue(double baseValue)
        {
            _baseValue = baseValue;
            Refresh();
        }

        public void SetBand(Band band)
        {
            _band = band;
            Refresh();
        }

        public void SetSearch(string text)
        {
            _search = (text ?? string.Empty).Trim().ToLowerInvariant();
            Refresh();
        }

        public void SetSelected(string tpl)
        {
            _selectedTpl = tpl;
            foreach (var row in _rows)
            {
                row.Selection.enabled = row.Rect.gameObject.activeSelf && row.Item != null && row.Item.Tpl == tpl;
            }
        }

        /// <summary>Chance a band aims at; stackable results get the amount that lands there.</summary>
        private static double BandCenter(Band band)
        {
            switch (band)
            {
                case Band.P70: return 70;
                case Band.P30: return 30;
                case Band.P10: return 10;
                default: return 50;
            }
        }

        private static void BandRange(Band band, out double low, out double high)
        {
            var max = UpgraderData.Config.MaxChance - 0.01;
            switch (band)
            {
                case Band.P70: low = 60; high = max; break;
                case Band.P50: low = 40; high = 60; break;
                case Band.P30: low = 20; high = 40; break;
                case Band.P10: low = 5; high = 20; break;
                default: low = 0; high = 101; break;
            }
        }

        /// <summary>
        /// Rebuilds the matches for the current base, band and search. Sorted by chance, best first: that is the
        /// cheapest total first, with stackables already at the amount the band picked, so ammo and weapons mix by
        /// odds instead of by unit price.
        /// </summary>
        public void Refresh()
        {
            _matches.Clear();

            if (_baseValue > 0)
            {
                BandRange(_band, out var low, out var high);
                var center = BandCenter(_band);
                var searching = _search.Length > 0;
                var pinnedAtMax = UpgraderData.Config.MaxChance - 0.0001;

                foreach (var target in UpgraderData.Targets)
                {
                    if (searching && !target.SearchKey.Contains(_search))
                    {
                        continue;
                    }

                    var amount = target.Stackable ? UpgraderData.AmountForChance(target, _baseValue, center) : 1;
                    var chance = UpgraderData.Chance(_baseValue, target.Entry.Value * amount);

                    if (_band != Band.All && (chance < low || chance >= high))
                    {
                        continue;
                    }

                    // Without a search, "all" skips items cheaper than the base: they all sit at the max chance
                    if (_band == Band.All && !searching && chance >= pinnedAtMax)
                    {
                        continue;
                    }

                    _matches.Add(new Match { Target = target, Amount = amount, Chance = chance });
                }

                _matches.Sort((a, b) =>
                {
                    var byChance = b.Chance.CompareTo(a.Chance);
                    return byChance != 0 ? byChance : string.Compare(a.Target.Name, b.Target.Name, StringComparison.OrdinalIgnoreCase);
                });
            }

            _content.sizeDelta = new Vector2(0f, _matches.Count * RowHeight);
            _content.anchoredPosition = Vector2.zero;
            _scroll.StopMovement();
            _empty.gameObject.SetActive(_matches.Count == 0);
            RenderVisible(true);
        }

        /// <summary>Binds the pooled rows to the matches currently inside the viewport.</summary>
        private void RenderVisible(bool force)
        {
            var poolSize = Mathf.CeilToInt(_viewportHeight / RowHeight) + 2;
            var maxFirst = Math.Max(0, _matches.Count - 1);
            var first = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, _content.anchoredPosition.y) / RowHeight), 0, maxFirst);
            if (!force && first == _firstRendered)
            {
                return;
            }

            _firstRendered = first;
            for (var i = 0; i < poolSize; i++)
            {
                var index = first + i;
                if (index < _matches.Count)
                {
                    var row = i < _rows.Count ? _rows[i] : CreateRow();
                    var match = _matches[index];
                    if (force || row.Item != match.Target || row.Count != match.Amount || row.Index != index)
                    {
                        FillRow(row, match.Target, match.Amount, match.Chance, index);
                    }
                }
                else if (i < _rows.Count && _rows[i].Rect.gameObject.activeSelf)
                {
                    HideRow(_rows[i]);
                }
            }

            for (var i = poolSize; i < _rows.Count; i++)
            {
                if (_rows[i].Rect.gameObject.activeSelf)
                {
                    HideRow(_rows[i]);
                }
            }
        }

        private void HideRow(Row row)
        {
            _icons.Unbind(row.Icon);
            row.Item = null;
            row.Index = -1;
            row.Rect.gameObject.SetActive(false);
        }

        private Row CreateRow()
        {
            var row = new Row();

            // The button tints the renderer, so the image stays black and the ColorBlock sets the alpha
            var background = UiKit.CreateImage(_content, "Row", Color.black);
            background.raycastTarget = true;
            row.Background = background;
            row.Rect = background.rectTransform;
            row.Rect.anchorMin = new Vector2(0f, 1f);
            row.Rect.anchorMax = new Vector2(1f, 1f);
            row.Rect.pivot = new Vector2(0.5f, 1f);

            row.Button = background.gameObject.AddComponent<Button>();
            row.Button.targetGraphic = background;
            var colors = row.Button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, UiKit.RowBg.a);
            colors.highlightedColor = new Color(1f, 1f, 1f, UiKit.RowHover.a);
            colors.pressedColor = new Color(1f, 1f, 1f, UiKit.RowHover.a);
            colors.selectedColor = new Color(1f, 1f, 1f, UiKit.RowBg.a);
            colors.disabledColor = new Color(1f, 1f, 1f, UiKit.RowBg.a);
            colors.fadeDuration = 0.08f;
            row.Button.colors = colors;

            row.Selection = UiKit.CreateImage(row.Rect, "Selected", UiKit.Selected);
            UiKit.Stretch(row.Selection.rectTransform);
            row.Selection.enabled = false;

            var cell = UiKit.CreateImage(row.Rect, "Cell", UiKit.SlotBack);
            cell.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            cell.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            cell.rectTransform.pivot = new Vector2(0f, 0.5f);
            cell.rectTransform.anchoredPosition = new Vector2(6f, 0f);
            cell.rectTransform.sizeDelta = new Vector2(IconSize, IconSize);
            row.IconTint = UiKit.CreateImage(cell.transform, "Tint", Color.clear);
            UiKit.Stretch(row.IconTint.rectTransform);
            row.Icon = UiKit.CreateImage(cell.transform, "Icon", Color.white);
            row.Icon.preserveAspect = true;
            UiKit.Stretch(row.Icon.rectTransform, 3f);
            row.Icon.enabled = false;
            UiKit.CreateCellFrame(cell.transform);

            row.Name = UiKit.CreateLabel(row.Rect, "Name", string.Empty, 18f, UiKit.TextName, TextAlignmentOptions.MidlineLeft);
            row.Name.rectTransform.anchorMin = new Vector2(0f, 0f);
            row.Name.rectTransform.anchorMax = new Vector2(1f, 1f);
            row.Name.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            row.Name.rectTransform.offsetMin = new Vector2(IconSize + 18f, 0f);
            row.Name.rectTransform.offsetMax = new Vector2(-350f, 0f);

            row.Amount = UiKit.CreateLabel(row.Rect, "Amount", string.Empty, 16f, UiKit.TextDim, TextAlignmentOptions.MidlineRight);
            row.Amount.rectTransform.anchorMin = new Vector2(1f, 0f);
            row.Amount.rectTransform.anchorMax = new Vector2(1f, 1f);
            row.Amount.rectTransform.pivot = new Vector2(1f, 0.5f);
            row.Amount.rectTransform.anchoredPosition = new Vector2(-262f, 0f);
            row.Amount.rectTransform.sizeDelta = new Vector2(84f, 0f);

            row.Value = UiKit.CreateLabel(row.Rect, "Value", string.Empty, 17f, UiKit.TextPrimary, TextAlignmentOptions.MidlineRight);
            row.Value.rectTransform.anchorMin = new Vector2(1f, 0f);
            row.Value.rectTransform.anchorMax = new Vector2(1f, 1f);
            row.Value.rectTransform.pivot = new Vector2(1f, 0.5f);
            row.Value.rectTransform.anchoredPosition = new Vector2(-108f, 0f);
            row.Value.rectTransform.sizeDelta = new Vector2(150f, 0f);

            row.Chance = UiKit.CreateLabel(row.Rect, "Chance", string.Empty, 18f, UiKit.TextPrimary, TextAlignmentOptions.MidlineRight);
            row.Chance.fontStyle = FontStyles.Bold;
            row.Chance.rectTransform.anchorMin = new Vector2(1f, 0f);
            row.Chance.rectTransform.anchorMax = new Vector2(1f, 1f);
            row.Chance.rectTransform.pivot = new Vector2(1f, 0.5f);
            row.Chance.rectTransform.anchoredPosition = new Vector2(-14f, 0f);
            row.Chance.rectTransform.sizeDelta = new Vector2(88f, 0f);

            var captured = row;
            row.Button.onClick.AddListener(() =>
            {
                if (captured.Item == null)
                {
                    return;
                }

                UiKit.PlaySound(EFT.UI.EUISoundType.ButtonClick);
                SetSelected(captured.Item.Tpl);
                Selected?.Invoke(captured.Item, captured.Count);
            });

            _rows.Add(row);
            return row;
        }

        private void FillRow(Row row, TargetItem target, int amount, double chance, int index)
        {
            row.Item = target;
            row.Count = amount;
            row.Index = index;
            row.Rect.gameObject.SetActive(true);
            row.Rect.anchoredPosition = new Vector2(0f, -index * RowHeight);
            row.Rect.sizeDelta = new Vector2(0f, RowHeight - 1f);

            var preview = UiKit.GetPreviewItem(target.Tpl);
            row.IconTint.color = UiKit.CellColor(preview);
            _icons.Bind(row.Icon, preview);

            row.Name.text = target.Name;
            row.Amount.text = amount > 1 ? "x" + amount : string.Empty;
            row.Value.text = UiKit.Roubles(target.Entry.Value * amount);
            row.Chance.text = UiKit.Percent(chance);
            row.Selection.enabled = target.Tpl == _selectedTpl;
        }

        public void Dispose()
        {
            foreach (var row in _rows)
            {
                _icons.Unbind(row.Icon);
            }

            _rows.Clear();
        }
    }
}
