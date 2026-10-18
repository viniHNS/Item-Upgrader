using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using JsonType;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace viniHNS.itemUpgrader
{
    /// <summary>
    /// Builds uGUI pieces out of the game's own sprites, fonts and widgets (slot hatch and borders, slot headers,
    /// skill ring, dialog buttons), so the upgrader reads as a vanilla screen.
    /// </summary>
    internal static class UiKit
    {
        // Palette sampled from the game's own screens
        public static readonly Color TextPrimary = Hex("#D7D7CF");
        public static readonly Color TextName = Hex("#B6C1C7");
        public static readonly Color TextDim = Hex("#8A8A84");
        public static readonly Color Khaki = Hex("#BCB79B");
        public static readonly Color KhakiText = Hex("#1B1B1B");
        public static readonly Color Green = Hex("#8FB33F");
        public static readonly Color Red = Hex("#C84B4B");
        public static readonly Color RowBg = new Color(0f, 0f, 0f, 0.42f);
        public static readonly Color RowHover = new Color(0f, 0f, 0f, 0.15f);
        public static readonly Color Selected = new Color(0.74f, 0.72f, 0.61f, 0.22f);
        public static readonly Color ChipBg = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color FieldBg = new Color(0f, 0f, 0f, 0.6f);
        public static readonly Color HairLine = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color RingTrack = Hex("#2A2A2A");
        public static readonly Color RingFill = Hex("#BCB79B");
        public static readonly Color SlotBack = Hex("#0B0C10");

        /// <summary>Vanilla stash cell pitch: 63 px per cell plus one.</summary>
        public const int CellPitch = 63;

        private static Sprite _ringSprite;

        private static ItemUiContext Context => ItemUiContext.Instance;

        private static MessageWindow Template => Context != null ? Context._messageWindow : null;

        /// <summary>Where the game puts its dialogs (split dialog, message window); null outside the menus.</summary>
        public static RectTransform WindowsContainer => Context != null ? Context._infoWindowsContainer : null;

        private static TextMeshProUGUI BodyTemplate => Template != null ? Template._message : null;

        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.white;
        }

        public static string L(string key)
        {
            return key.Localized();
        }

        public static string LU(string key)
        {
            return key.Localized(EStringCase.Upper);
        }

        public static string Roubles(double value)
        {
            return Math.Round(value).ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ") + " ₽";
        }

        public static string Percent(double chance)
        {
            return chance.ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        public static int CellsToPixels(int cells)
        {
            return cells * CellPitch + 1;
        }

        public static void PlaySound(EUISoundType sound)
        {
            if (!Plugin.PlaySounds.Value)
            {
                return;
            }

            try
            {
                Singleton<GUISounds>.Instance?.PlayUISound(sound);
            }
            catch
            {
                // sounds are never worth an exception
            }
        }

        // ----- rect helpers -------------------------------------------------------------------------

        public static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>Anchors to the parent's top-left corner; y grows downwards.</summary>
        public static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        public static void Center(RectTransform rect, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        public static Image CreateImage(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var image = CreateRect(parent, name).gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Copies everything that makes an image look the way it does.</summary>
        public static void CopyLook(Image target, Image source, bool keepColor = false)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.sprite = source.sprite;
            target.material = source.material;
            target.type = source.type;
            target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            target.fillCenter = source.fillCenter;
            target.preserveAspect = source.preserveAspect;
            if (!keepColor)
            {
                target.color = source.color;
            }
        }

        // ----- text ---------------------------------------------------------------------------------

        public static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions alignment)
        {
            var label = CreateRect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            var template = BodyTemplate;
            if (template != null)
            {
                label.font = template.font;
                label.fontSharedMaterial = template.fontSharedMaterial;
                label.fontStyle = template.fontStyle;
                label.characterSpacing = template.characterSpacing;
            }
            else
            {
                var any = Object.FindObjectOfType<TextMeshProUGUI>();
                if (any != null)
                {
                    label.font = any.font;
                }
            }

            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            label.richText = true;
            return label;
        }

        // ----- window chrome ------------------------------------------------------------------------

        /// <summary>
        /// The game's dialog chrome: an opaque black panel with the sliced border_shadow sprite hanging a few
        /// pixels outside it (taken from the message window prefab; a plain dark panel when not found).
        /// </summary>
        public static Image CreateWindowBackground(RectTransform window)
        {
            var fill = CreateImage(window, "Fill", new Color(0f, 0f, 0f, 0.97f));
            Stretch(fill.rectTransform);
            fill.raycastTarget = true;

            var source = FindBackdrop(Template != null ? Template.WindowTransform : null);
            if (source != null)
            {
                var backdrop = CreateImage(window, "Border", source.color);
                Stretch(backdrop.rectTransform, -5f);
                CopyLook(backdrop, source);
            }
            else
            {
                var frame = CreateImage(window, "Frame", HairLine);
                Stretch(frame.rectTransform, -1f);
                frame.transform.SetAsFirstSibling();
            }

            return fill;
        }

        private static Image FindBackdrop(RectTransform windowRect)
        {
            if (windowRect == null)
            {
                return null;
            }

            Image best = null;
            var bestArea = 0f;
            foreach (var image in windowRect.GetComponentsInChildren<Image>(true))
            {
                var rect = image.rectTransform.rect;
                var area = Mathf.Abs(rect.width * rect.height);
                if (image.sprite != null && area > bestArea)
                {
                    bestArea = area;
                    best = image;
                }
            }

            return best;
        }

        public const float CaptionHeight = 26f;

        /// <summary>
        /// Title strip like the game's dialogs: translucent grey band, white uppercase caption at the left,
        /// vanilla close button at the right. Returns the close button.
        /// </summary>
        public static Button CreateCaptionBar(RectTransform window, string title)
        {
            var bar = CreateImage(window, "CaptionBar", new Color(0.33f, 0.345f, 0.357f, 0.30f));
            bar.raycastTarget = true;
            bar.rectTransform.anchorMin = new Vector2(0f, 1f);
            bar.rectTransform.anchorMax = new Vector2(1f, 1f);
            bar.rectTransform.pivot = new Vector2(0.5f, 1f);
            bar.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            bar.rectTransform.sizeDelta = new Vector2(-4f, CaptionHeight);

            var caption = CreateLabel(bar.transform, "Caption", title, 16f, Color.white, TextAlignmentOptions.MidlineLeft);
            Stretch(caption.rectTransform);
            caption.rectTransform.offsetMin = new Vector2(10f, 0f);
            caption.rectTransform.offsetMax = new Vector2(-48f, 0f);
            caption.characterSpacing = 1f;

            return CreateCloseButton(window);
        }

        private static Button CreateCloseButton(RectTransform window)
        {
            var template = Template != null ? Template.CloseButton : null;
            Button button;
            if (template != null)
            {
                var clone = Object.Instantiate(template.gameObject, window, false);
                clone.name = "CloseButton";
                clone.SetActive(true);
                button = clone.GetComponent<Button>();
                var rect = (RectTransform)clone.transform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                var size = rect.rect.size;
                if (size.x < 4f || size.y < 4f)
                {
                    size = new Vector2(27f, 17f);
                }

                rect.sizeDelta = size;
                rect.anchoredPosition = new Vector2(-8f, -2f - (CaptionHeight - size.y) / 2f);
            }
            else
            {
                var image = CreateImage(window, "CloseButton", new Color(1f, 1f, 1f, 0.08f));
                image.raycastTarget = true;
                TopLeft(image.rectTransform, window.sizeDelta.x - 40f, 8f, 32f, 32f);
                var label = CreateLabel(image.transform, "X", "X", 20f, TextPrimary, TextAlignmentOptions.Center);
                Stretch(label.rectTransform);
                button = image.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
            }

            button.onClick = new Button.ButtonClickedEvent();
            return button;
        }

        // ----- vanilla widgets ----------------------------------------------------------------------

        /// <summary>Height of the slot header strip, from the game's prefab when available.</summary>
        public static float HeaderHeight
        {
            get
            {
                var header = VanillaAssets.Header;
                if (header != null)
                {
                    var rect = (RectTransform)header.transform;
                    if (rect.rect.height > 4f)
                    {
                        return rect.rect.height;
                    }
                }

                return 26f;
            }
        }

        /// <summary>Section label in the style of the equipment slot headers ("EARPIECE", "POCKETS").</summary>
        public static RectTransform CreateHeader(Transform parent, string text, float x, float y, float width)
        {
            var template = VanillaAssets.Header;
            if (template != null)
            {
                try
                {
                    var clone = Object.Instantiate(template.gameObject, parent, false);
                    clone.name = "Header";
                    var header = clone.GetComponent<SlotViewHeader>();
                    var label = header._headerText;
                    var arrow = header._arrow;
                    var idle = header._backgroundIdle;
                    var selected = header._backgroundSelected;
                    var idleColor = header._idleColor;
                    Object.DestroyImmediate(header);
                    foreach (var feedback in clone.GetComponents<ButtonFeedback>())
                    {
                        Object.DestroyImmediate(feedback);
                    }

                    if (arrow != null)
                    {
                        arrow.gameObject.SetActive(false);
                    }

                    if (selected != null)
                    {
                        selected.SetActive(false);
                    }

                    if (idle != null)
                    {
                        idle.SetActive(true);
                    }

                    if (label != null)
                    {
                        foreach (var localized in label.GetComponents<LocalizedText>())
                        {
                            Object.DestroyImmediate(localized);
                        }

                        label.text = text;
                        label.color = idleColor.a > 0f ? idleColor : TextName;
                        label.gameObject.SetActive(true);
                    }

                    var rect = (RectTransform)clone.transform;
                    var height = rect.rect.height > 4f ? rect.rect.height : HeaderHeight;
                    TopLeft(rect, x, y, width, height);
                    clone.SetActive(true);
                    return rect;
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogWarning($"itemUpgrader: header clone failed, using a plain label: {ex.Message}");
                }
            }

            var strip = CreateImage(parent, "Header", new Color(0f, 0f, 0f, 0.5f));
            TopLeft(strip.rectTransform, x, y, width, HeaderHeight);
            var plain = CreateLabel(strip.transform, "Text", text, 15f, TextName, TextAlignmentOptions.MidlineLeft);
            Stretch(plain.rectTransform);
            plain.rectTransform.offsetMin = new Vector2(8f, 0f);
            return strip.rectTransform;
        }

        /// <summary>An item slot that looks like an equipment slot: hatched when empty, framed when filled.</summary>
        public sealed class SlotVisual
        {
            public RectTransform Rect;
            public Image Back;
            public Image Tint;
            public Image Icon;
            public Image EmptyBorder;
            public Image FullBorder;
            public CanvasGroup Group;

            public void SetItem(Item item, IconBinder icons)
            {
                var filled = item != null;
                Tint.color = filled ? CellColor(item) : Color.clear;
                icons.Bind(Icon, item);
                if (EmptyBorder != null)
                {
                    EmptyBorder.gameObject.SetActive(!filled);
                }

                if (FullBorder != null)
                {
                    FullBorder.gameObject.SetActive(filled);
                }

                if (Back != null)
                {
                    Back.gameObject.SetActive(!filled || Back.name == "FlatBack");
                }
            }
        }

        public static SlotVisual CreateSlot(Transform parent, string name, float x, float y, float size)
        {
            var visual = new SlotVisual();
            visual.Rect = CreateRect(parent, name);
            TopLeft(visual.Rect, x, y, size, size);
            visual.Group = visual.Rect.gameObject.AddComponent<CanvasGroup>();

            // Dark cell under everything, like a filled slot
            var flat = CreateImage(visual.Rect, "FlatBack", SlotBack);
            Stretch(flat.rectTransform);

            var template = VanillaAssets.Slot;
            if (template != null)
            {
                visual.Back = CreateImage(visual.Rect, "Hatch", Color.white);
                Stretch(visual.Back.rectTransform);
                CopyLook(visual.Back, template._slotBackground);

                visual.Tint = CreateImage(visual.Rect, "Tint", Color.clear);
                Stretch(visual.Tint.rectTransform);

                visual.Icon = CreateImage(visual.Rect, "Icon", Color.white);
                visual.Icon.preserveAspect = true;
                Stretch(visual.Icon.rectTransform, 6f);
                visual.Icon.enabled = false;

                visual.EmptyBorder = CreateImage(visual.Rect, "EmptyBorder", Color.white);
                Stretch(visual.EmptyBorder.rectTransform);
                CopyLook(visual.EmptyBorder, template._emptyBorder);

                visual.FullBorder = CreateImage(visual.Rect, "FullBorder", Color.white);
                Stretch(visual.FullBorder.rectTransform);
                CopyLook(visual.FullBorder, template.FullBorder);
                visual.FullBorder.gameObject.SetActive(false);
            }
            else
            {
                visual.Back = flat;
                visual.Tint = CreateImage(visual.Rect, "Tint", Color.clear);
                Stretch(visual.Tint.rectTransform);
                visual.Icon = CreateImage(visual.Rect, "Icon", Color.white);
                visual.Icon.preserveAspect = true;
                Stretch(visual.Icon.rectTransform, 6f);
                visual.Icon.enabled = false;
                visual.EmptyBorder = CreateImage(visual.Rect, "EmptyBorder", HairLine);
                Stretch(visual.EmptyBorder.rectTransform);
                var inner = CreateImage(visual.EmptyBorder.transform, "Inner", SlotBack);
                Stretch(inner.rectTransform, 1f);
                inner.transform.SetAsFirstSibling();
            }

            return visual;
        }

        /// <summary>Frame around a small item icon, from the stash cell border sprite when found.</summary>
        public static Image CreateCellFrame(Transform parent)
        {
            var frame = CreateImage(parent, "Frame", HairLine);
            Stretch(frame.rectTransform);
            var cell = VanillaAssets.Cell;
            if (cell != null && cell._border != null)
            {
                CopyLook(frame, cell._border);
                if (frame.color.a < 0.05f)
                {
                    frame.color = Color.white;
                }
            }
            else
            {
                var inner = CreateImage(frame.transform, "Inner", new Color(0f, 0f, 0f, 0f));
                Stretch(inner.rectTransform, 1f);
            }

            return frame;
        }

        /// <summary>
        /// Track and radial fill of the dial. The game has no large ring sprite (its progress spinner is 53 px),
        /// so the ring is generated, in the game's colors.
        /// </summary>
        public static void CreateRing(Transform parent, float x, float y, float size, out Image track, out Image fill)
        {
            track = CreateImage(parent, "Track", RingTrack, RingSprite());
            TopLeft(track.rectTransform, x, y, size, size);
            fill = CreateImage(parent, "Fill", RingFill, RingSprite());
            TopLeft(fill.rectTransform, x, y, size, size);

            track.type = Image.Type.Simple;
            track.preserveAspect = true;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            fill.preserveAspect = true;
        }

        /// <summary>
        /// Wide action button in the style of the trader's DEAL! button, composed from its sprites with the
        /// idle/hover swap done here (the vanilla prefab swaps whole sub-objects with their own captions).
        /// </summary>
        public sealed class TradeButton
        {
            public RectTransform Rect;
            public Button Button;
            public CanvasGroup Group;
            public GameObject Idle;
            public GameObject Hover;
            public TextMeshProUGUI IdleLabel;
            public TextMeshProUGUI HoverLabel;

            private bool _interactable = true;

            public bool Interactable
            {
                get => _interactable;
                set
                {
                    _interactable = value;
                    Button.interactable = value;
                    Group.alpha = value ? 1f : 0.45f;
                    if (!value)
                    {
                        SetHover(false);
                    }
                }
            }

            public void SetText(string text)
            {
                IdleLabel.text = text;
                HoverLabel.text = text;
            }

            public void SetHover(bool on)
            {
                Idle.SetActive(!on || !_interactable);
                Hover.SetActive(on && _interactable);
            }
        }

        private static Sprite FindSpriteIn(Component root, string spriteName)
        {
            if (root == null)
            {
                return null;
            }

            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite != null && image.sprite.name == spriteName)
                {
                    return image.sprite;
                }
            }

            return null;
        }

        public static TradeButton CreateTradeButton(Transform parent, string name, string text, float x, float y, float width, float height, Action onClick)
        {
            var deal = VanillaAssets.DealButton;
            var idleSprite = FindSpriteIn(deal, "Trade_Buyl_Button_Background_Idle");
            var hoverSprite = FindSpriteIn(deal, "Trade_Buyl_Button_Background_Hover");
            var patternSprite = FindSpriteIn(deal, "Trade_Buyl_Button_Pattern");

            var button = new TradeButton();
            button.Rect = CreateRect(parent, name);
            TopLeft(button.Rect, x, y, width, height);
            button.Group = button.Rect.gameObject.AddComponent<CanvasGroup>();

            var hit = button.Rect.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            button.Idle = CreateRect(button.Rect, "Default").gameObject;
            Stretch((RectTransform)button.Idle.transform);
            var idleBack = CreateImage(button.Idle.transform, "Background", idleSprite != null ? Color.white : new Color(0f, 0f, 0f, 0.7f), idleSprite);
            idleBack.type = Image.Type.Tiled;
            Stretch(idleBack.rectTransform);
            if (patternSprite != null)
            {
                var pattern = CreateImage(button.Idle.transform, "Pattern", new Color(1f, 1f, 1f, 0.045f), patternSprite);
                pattern.type = Image.Type.Tiled;
                Stretch(pattern.rectTransform, 1f);
            }

            button.IdleLabel = CreateLabel(button.Idle.transform, "Label", text, 20f, Color.white, TextAlignmentOptions.Center);
            Stretch(button.IdleLabel.rectTransform);

            button.Hover = CreateRect(button.Rect, "Hover").gameObject;
            Stretch((RectTransform)button.Hover.transform);
            var hoverBack = CreateImage(button.Hover.transform, "Background", hoverSprite != null ? Color.white : Khaki, hoverSprite);
            hoverBack.type = Image.Type.Sliced;
            Stretch(hoverBack.rectTransform);
            button.HoverLabel = CreateLabel(button.Hover.transform, "Label", text, 20f, Color.black, TextAlignmentOptions.Center);
            Stretch(button.HoverLabel.rectTransform);
            button.Hover.SetActive(false);

            button.Button = button.Rect.gameObject.AddComponent<Button>();
            button.Button.transition = Selectable.Transition.None;
            button.Button.targetGraphic = hit;
            button.Button.onClick.AddListener(() =>
            {
                if (!button.Interactable)
                {
                    return;
                }

                PlaySound(EUISoundType.ButtonClick);
                onClick?.Invoke();
            });

            var relay = button.Rect.gameObject.AddComponent<PointerHoverRelay>();
            relay.Hovered = on =>
            {
                if (on && button.Interactable)
                {
                    PlaySound(EUISoundType.ButtonOver);
                }

                button.SetHover(on);
            };

            return button;
        }

        /// <summary>A DefaultUIButton cloned from a vanilla one (message window OK by default).</summary>
        public static DefaultUIButton CreateButton(Transform parent, string name, string text, float x, float y, float width, float height, Action onClick, DefaultUIButton template = null)
        {
            if (template == null)
            {
                template = Template != null ? Template.AcceptButton : null;
            }

            if (template == null)
            {
                return null;
            }

            var clone = Object.Instantiate(template.gameObject, parent, false);
            clone.name = name;
            clone.SetActive(true);
            var button = clone.GetComponent<DefaultUIButton>();
            var rect = (RectTransform)clone.transform;
            TopLeft(rect, x, y, width, height);

            var layout = clone.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.minHeight = height;
                layout.preferredHeight = height;
            }

            // The label the idle/hover animation paints can be a different object from _headerLabel (or the only one);
            // both are kept and both get the caption, otherwise the button shows no text at all
            var animation = clone.GetComponentInChildren<DefaultUIButtonAnimation>(true);
            var painted = animation != null ? animation.Label : null;
            StripButtonExtras(clone, button, painted);

            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener(() => onClick?.Invoke());
            button.SetRawText(text, 24);
            if (painted != null && painted != button._headerLabel)
            {
                painted.text = text.ToUpper();
            }

            ShowChain(button._headerLabel != null ? button._headerLabel.transform : null, clone.transform);
            ShowChain(painted != null ? painted.transform : null, clone.transform);
            button.Interactable = true;
            if (animation != null)
            {
                // Re-apply the idle look: puts the per-state objects back in place after ShowChain
                animation.SetState(EButtonAnimationState.Normal);
            }

            if (Plugin.DebugLog.Value)
            {
                Plugin.LogSource.LogInfo($"itemUpgrader: button '{name}' header={(button._headerLabel != null ? button._headerLabel.name : "null")}, painted={(painted != null ? painted.name : "null")}, same={painted == button._headerLabel}, normalLabel={(animation != null ? ColorUtility.ToHtmlStringRGBA(animation._normalLabelColor) : "-")}, bgAlpha={(animation != null ? animation._backgorundNormalStateAplha : -1f):0.##}");
                VanillaAssets.LogTree(name, clone, 6);
            }

            return button;
        }

        /// <summary>Activates a node and its parents up to (not including) the root.</summary>
        private static void ShowChain(Transform node, Transform root)
        {
            for (var current = node; current != null && current != root; current = current.parent)
            {
                current.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Vanilla buttons carry extras (hotkey hint, icon, price label). Keep only the background and the caption:
        /// every top-level child that does not contain the header label is switched off.
        /// </summary>
        private static void StripButtonExtras(GameObject clone, DefaultUIButton button, TMP_Text painted)
        {
            if (button._iconContainer != null)
            {
                button._iconContainer.SetActive(false);
            }

            if (button._sizeLabel != null)
            {
                button._sizeLabel.text = string.Empty;
                button._sizeLabel.gameObject.SetActive(false);
            }

            if (button._iconImage != null)
            {
                button._iconImage.gameObject.SetActive(false);
            }

            if (button._iconIdleImage != null)
            {
                button._iconIdleImage.gameObject.SetActive(false);
            }

            var keep = new List<Transform>();
            if (button._headerLabel != null)
            {
                keep.Add(button._headerLabel.transform);
            }

            if (painted != null && !keep.Contains(painted.transform))
            {
                keep.Add(painted.transform);
            }

            foreach (var text in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (keep.Any(k => text.transform == k || k.IsChildOf(text.transform)))
                {
                    continue;
                }

                var top = TopLevelChild(clone.transform, text.transform);
                if (top != null && !keep.Any(k => k.IsChildOf(top)))
                {
                    top.gameObject.SetActive(false);
                }
                else
                {
                    text.gameObject.SetActive(false);
                }
            }
        }

        private static Transform TopLevelChild(Transform root, Transform node)
        {
            var current = node;
            while (current != null && current.parent != root)
            {
                current = current.parent;
            }

            return current;
        }

        /// <summary>Flat dark button for the filter tabs and the +/- steppers; khaki when selected, like the game's tabs.</summary>
        public static Button CreateFlatButton(Transform parent, string name, string text, float x, float y, float width, float height, Action onClick, out Image background, out TextMeshProUGUI label)
        {
            background = CreateImage(parent, name, ChipBg);
            background.raycastTarget = true;
            TopLeft(background.rectTransform, x, y, width, height);

            label = CreateLabel(background.transform, "Label", text, 17f, TextName, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                PlaySound(EUISoundType.ButtonClick);
                onClick?.Invoke();
            });
            return button;
        }

        /// <summary>Single line text field built on TMP_InputField; created inactive so the component wires up cleanly.</summary>
        public static TMP_InputField CreateInputField(Transform parent, string name, string placeholder, float x, float y, float width, float height, TMP_InputField.ContentType contentType, Action<string> onChanged, Action<string> onSubmit = null)
        {
            var root = CreateRect(parent, name);
            root.gameObject.SetActive(false);
            TopLeft(root, x, y, width, height);
            var background = root.gameObject.AddComponent<Image>();
            background.color = FieldBg;
            background.raycastTarget = true;
            var line = CreateImage(root, "Line", HairLine);
            line.rectTransform.anchorMin = new Vector2(0f, 0f);
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.pivot = new Vector2(0.5f, 0f);
            line.rectTransform.anchoredPosition = Vector2.zero;
            line.rectTransform.sizeDelta = new Vector2(0f, 1f);

            var viewport = CreateRect(root, "Text Area");
            Stretch(viewport, 0f);
            viewport.offsetMin = new Vector2(10f, 2f);
            viewport.offsetMax = new Vector2(-10f, -2f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = CreateLabel(viewport, "Text", string.Empty, 18f, TextPrimary, TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;

            var hint = CreateLabel(viewport, "Placeholder", placeholder, 18f, TextDim, TextAlignmentOptions.MidlineLeft);
            Stretch(hint.rectTransform);
            hint.fontStyle = FontStyles.Italic;

            var input = root.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = hint;
            input.contentType = contentType;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretColor = TextPrimary;
            input.selectionColor = new Color(0.74f, 0.72f, 0.61f, 0.4f);
            input.customCaretColor = true;
            input.pointSize = 18f;
            input.targetGraphic = background;
            input.onValueChanged.AddListener(value => onChanged?.Invoke(value));
            if (onSubmit != null)
            {
                input.onSubmit.AddListener(value => onSubmit(value));
            }

            root.gameObject.SetActive(true);
            return input;
        }

        // ----- generated fallback sprite ------------------------------------------------------------

        /// <summary>Anti-aliased thin white ring, used only when no vanilla ring sprite is found.</summary>
        public static Sprite RingSprite()
        {
            if (_ringSprite != null)
            {
                return _ringSprite;
            }

            const int size = 256;
            const float thickness = 14f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var center = (size - 1) / 2f;
            var outer = size / 2f - 2f;
            var inner = outer - thickness;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    var alpha = Mathf.Clamp01(outer - distance + 0.5f) * Mathf.Clamp01(distance - inner + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            _ringSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _ringSprite;
        }

        // ----- items --------------------------------------------------------------------------------

        private static readonly Dictionary<string, Item> PreviewItems = new Dictionary<string, Item>();

        /// <summary>Item used only to render an icon; weapons come assembled like the preset the player would get.</summary>
        public static Item GetPreviewItem(string tpl)
        {
            if (tpl == null)
            {
                return null;
            }

            if (PreviewItems.TryGetValue(tpl, out var cached))
            {
                return cached;
            }

            Item item = null;
            try
            {
                item = Singleton<ItemFactory>.Instance.GetPresetItem(tpl);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"itemUpgrader: no preview item for {tpl}: {ex.Message}");
            }

            PreviewItems[tpl] = item;
            return item;
        }

        /// <summary>Background tint of an item cell, as the grid draws it.</summary>
        public static Color CellColor(Item item)
        {
            if (item == null)
            {
                return new Color(0f, 0f, 0f, 0f);
            }

            var color = item.BackgroundColor.ToColor();
            color.a = 0.3019608f;
            return color;
        }
    }

    /// <summary>Forwards pointer enter/exit to a callback, for hover swaps done in code.</summary>
    internal sealed class PointerHoverRelay : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public Action<bool> Hovered;

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Hovered?.Invoke(true);
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Hovered?.Invoke(false);
        }

        private void OnDisable()
        {
            Hovered?.Invoke(false);
        }
    }

    /// <summary>Keeps item icons bound to images and releases every subscription on dispose.</summary>
    internal sealed class IconBinder : IDisposable
    {
        private readonly Dictionary<Image, Action> _unbinds = new Dictionary<Image, Action>();

        public void Bind(Image image, Item item)
        {
            if (image == null)
            {
                return;
            }

            Unbind(image);
            image.sprite = null;
            image.enabled = false;
            if (item == null)
            {
                return;
            }

            ItemIcon icon;
            try
            {
                icon = ItemViewFactory.LoadItemIcon(item, 1, false);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"itemUpgrader: icon failed for {item.StringTemplateId}: {ex.Message}");
                return;
            }

            if (icon == null)
            {
                return;
            }

            _unbinds[image] = icon.Changed.Bind(() =>
            {
                if (image == null)
                {
                    return;
                }

                image.sprite = icon.Sprite;
                image.enabled = icon.Sprite != null;
            });
        }

        public void Unbind(Image image)
        {
            if (image != null && _unbinds.TryGetValue(image, out var unbind))
            {
                unbind();
                _unbinds.Remove(image);
            }
        }

        public void Dispose()
        {
            foreach (var unbind in _unbinds.Values)
            {
                unbind();
            }

            _unbinds.Clear();
        }
    }
}
