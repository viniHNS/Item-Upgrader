using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace viniHNS.itemUpgrader
{
    /// <summary>
    /// The upgrader dialog: base item on the left, chance dial in the middle, result on the right and the
    /// result picker below. Lives in the game's dialog container and is built from vanilla UI parts.
    /// </summary>
    public class UpgradeWindow : MonoBehaviour
    {
        private const float WindowWidth = 1200f;
        private const float WindowHeight = 800f;
        private const float Margin = 30f;
        private const float ColumnTop = 62f;
        private const float SideWidth = 330f;
        private const float RingSize = 210f;
        private const float PointerOverhang = 4f;
        private const float GaugeGap = 16f;

        private enum State
        {
            Loading,
            Idle,
            Ready,
            Rolling,
            Done,
        }

        public static UpgradeWindow Instance { get; private set; }

        public static bool IsOpen => Instance != null;

        public static string MenuCaption => UiKit.LU("itemupgrader_menu");

        private readonly IconBinder _icons = new IconBinder();

        private RectTransform _window;
        private GameObject _fallbackCanvas;

        private UiKit.SlotVisual _baseSlot;
        private TextMeshProUGUI _baseName;
        private TextMeshProUGUI _baseValueLabel;

        private UiKit.SlotVisual _targetSlot;
        private TextMeshProUGUI _targetName;
        private TextMeshProUGUI _targetValueLabel;
        private RectTransform _amountRow;
        private TMP_InputField _amountField;
        private TMP_InputField _searchField;

        private Image _ringFill;
        private RectTransform _pointer;
        private TextMeshProUGUI _chanceLabel;
        private TextMeshProUGUI _statusLabel;
        private TextMeshProUGUI _messageLabel;

        private UiKit.TradeButton _upgradeButton;
        private DefaultUIButton _closeButton;
        private TargetListView _list;
        private readonly Dictionary<TargetListView.Band, Image> _chips = new Dictionary<TargetListView.Band, Image>();
        private readonly Dictionary<TargetListView.Band, TextMeshProUGUI> _chipLabels = new Dictionary<TargetListView.Band, TextMeshProUGUI>();

        private State _state = State.Loading;
        private Item _baseItem;
        private double _baseValue;
        private TargetItem _target;
        private int _amount = 1;
        private double _chance;

        private bool _resultKnown;
        private bool _success;
        private Item _wonItem;
        private string _resultError;
        private LastResult _lastResult;
        private int _quoteToken;

        public static void Open(Item item)
        {
            if (item == null)
            {
                return;
            }

            if (Instance != null)
            {
                Object.Destroy(Instance.gameObject);
                Instance = null;
            }

            var container = UiKit.WindowsContainer;
            var root = new GameObject("itemUpgrader_Window", typeof(RectTransform));
            var window = root.AddComponent<UpgradeWindow>();
            if (container != null)
            {
                root.transform.SetParent(container, false);
            }
            else
            {
                window.CreateFallbackCanvas(root);
            }

            Instance = window;
            try
            {
                VanillaAssets.Dump();
                window.Build();
                window.SetBase(item);
                root.transform.SetAsLastSibling();
                UiKit.PlaySound(EUISoundType.MenuInspectorWindowOpen);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"itemUpgrader: could not open the window: {ex}");
                Object.Destroy(root);
                Instance = null;
            }
        }

        private void CreateFallbackCanvas(GameObject root)
        {
            _fallbackCanvas = new GameObject("itemUpgrader_Canvas");
            var canvas = _fallbackCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = _fallbackCanvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _fallbackCanvas.AddComponent<GraphicRaycaster>();
            root.transform.SetParent(_fallbackCanvas.transform, false);
        }

        private void OnDestroy()
        {
            _list?.Dispose();
            _icons.Dispose();
            if (_fallbackCanvas != null)
            {
                Object.Destroy(_fallbackCanvas);
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private GameObject _lastSelected;
        private bool _clickedThisFrame;

        private void Update()
        {
            _clickedThisFrame = Input.GetMouseButtonDown(0);
            if (!Plugin.DebugLog.Value)
            {
                return;
            }

            if (_clickedThisFrame)
            {
                LogClick();
            }

            if (HasFocusedField && Input.anyKeyDown && !Input.GetMouseButtonDown(0))
            {
                Plugin.LogSource.LogInfo($"itemUpgrader: key while typing: inputString='{Input.inputString}', amount='{_amountField?.text}' (readOnly={_amountField != null && _amountField.readOnly}), search='{_searchField?.text}' (readOnly={_searchField != null && _searchField.readOnly})");
            }
        }

        /// <summary>
        /// Runs after the EventSystem handled the click of this frame. A click inside one of the text fields that did
        /// not give it focus (something else took the pointer) focuses the field here, so typing always works.
        /// </summary>
        private void LateUpdate()
        {
            if (_clickedThisFrame)
            {
                EnsureFocus(_amountField);
                EnsureFocus(_searchField);
            }

            // UI Fixes makes the selected text field read-only while an item is hovered; there are no items to
            // hover in this window, so a read-only flag left on one of its fields only blocks typing
            ClearReadOnly(_amountField);
            ClearReadOnly(_searchField);

            if (Plugin.DebugLog.Value)
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected != _lastSelected)
                {
                    _lastSelected = selected;
                    Plugin.LogSource.LogInfo($"itemUpgrader: selection -> {PathOf(selected)}");
                }
            }
        }

        private void EnsureFocus(TMP_InputField field)
        {
            if (field == null || !field.isActiveAndEnabled || !field.interactable || field.isFocused || !PointerInside(field))
            {
                return;
            }

            if (Plugin.DebugLog.Value)
            {
                Plugin.LogSource.LogInfo($"itemUpgrader: click inside '{field.name}' did not focus it (selected={PathOf(EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null)}); focusing it");
            }

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(field.gameObject);
            }

            field.ActivateInputField();
        }

        private void ClearReadOnly(TMP_InputField field)
        {
            if (field == null || !field.readOnly || !field.isFocused)
            {
                return;
            }

            field.readOnly = false;
            if (Plugin.DebugLog.Value)
            {
                Plugin.LogSource.LogInfo($"itemUpgrader: '{field.name}' was read-only while focused; cleared");
            }
        }

        private static bool PointerInside(Component target)
        {
            var rect = target.transform as RectTransform;
            if (rect == null)
            {
                return false;
            }

            var canvas = target.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, camera);
        }

        private void LogClick()
        {
            var system = EventSystem.current;
            var sb = new System.Text.StringBuilder();
            sb.Append($"itemUpgrader: click at {Input.mousePosition}: eventSystem={(system != null ? system.name : "null")} (enabled={system != null && system.enabled}, module={(system != null && system.currentInputModule != null ? system.currentInputModule.GetType().Name : "null")}, count={Object.FindObjectsOfType<EventSystem>().Length}), selected={PathOf(system != null ? system.currentSelectedGameObject : null)}");
            foreach (var field in new[] { _amountField, _searchField })
            {
                if (field != null)
                {
                    sb.AppendLine().Append($"  field '{field.name}': inside={PointerInside(field)}, active={field.isActiveAndEnabled}, interactable={field.interactable}/{field.IsInteractable()}, focused={field.isFocused}, readOnly={field.readOnly}");
                }
            }

            if (system != null)
            {
                var results = new List<RaycastResult>();
                system.RaycastAll(new PointerEventData(system) { position = Input.mousePosition }, results);
                for (var i = 0; i < results.Count && i < 6; i++)
                {
                    var hit = results[i];
                    var handler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit.gameObject);
                    sb.AppendLine().Append($"  hit {i}: {PathOf(hit.gameObject)} (depth={hit.depth}, order={hit.sortingOrder}, pointerDown={PathOf(handler)})");
                }

                if (results.Count == 0)
                {
                    sb.AppendLine().Append("  no raycast hits");
                }
            }

            Plugin.LogSource.LogInfo(sb.ToString());
        }

        private static string PathOf(GameObject target)
        {
            if (target == null)
            {
                return "null";
            }

            var parts = new List<string>();
            for (var current = target.transform; current != null && parts.Count < 5; current = current.parent)
            {
                parts.Add(current.name);
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>True while the player is typing in the amount or the search field.</summary>
        public bool HasFocusedField => (_amountField != null && _amountField.isFocused) || (_searchField != null && _searchField.isFocused);

        /// <summary>Close request from the X button, CLOSE or Escape. Ignored while the roll is in flight.</summary>
        public void RequestClose()
        {
            if (_state == State.Rolling)
            {
                return;
            }

            UiKit.PlaySound(EUISoundType.MenuInspectorWindowClose);
            Object.Destroy(gameObject);
        }

        // ----- construction -------------------------------------------------------------------------

        private void Build()
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);

            // Dims and blocks everything under the dialog, well past the container bounds
            var blocker = UiKit.CreateImage(root, "Blocker", new Color(0f, 0f, 0f, 0.6f));
            blocker.raycastTarget = true;
            UiKit.Stretch(blocker.rectTransform);
            blocker.rectTransform.offsetMin = new Vector2(-4000f, -4000f);
            blocker.rectTransform.offsetMax = new Vector2(4000f, 4000f);

            _window = UiKit.CreateRect(root, "Window");
            UiKit.Center(_window, Vector2.zero, new Vector2(WindowWidth, WindowHeight));
            UiKit.CreateWindowBackground(_window);

            var close = UiKit.CreateCaptionBar(_window, UiKit.L("itemupgrader_title"));
            close.onClick.AddListener(RequestClose);

            var headerHeight = UiKit.HeaderHeight;
            var slotSize = UiKit.CellsToPixels(3);
            var slotTop = ColumnTop + headerHeight + 10f;
            var leftBottom = BuildSidePanel(Margin, headerHeight, slotTop, slotSize, true);
            var rightBottom = BuildSidePanel(WindowWidth - Margin - SideWidth, headerHeight, slotTop, slotSize, false);
            // The dial hangs below the CHANCE header with a gap, so neither the ring nor the needle tip touches it
            BuildGauge(headerHeight, ColumnTop + headerHeight + GaugeGap + PointerOverhang + RingSize / 2f);
            BuildPicker(Mathf.Max(leftBottom, rightBottom) + 14f);

            var buttonY = WindowHeight - 62f;
            var upgradeWidth = 300f;
            var closeWidth = 180f;
            var startX = (WindowWidth - upgradeWidth - 20f - closeWidth) / 2f;
            _upgradeButton = UiKit.CreateTradeButton(_window, "UpgradeButton", UiKit.L("itemupgrader_upgrade"), startX, buttonY, upgradeWidth, 45f, OnUpgradeClicked);
            _closeButton = UiKit.CreateButton(_window, "CloseButton", UiKit.L("itemupgrader_close"), startX + upgradeWidth + 20f, buttonY, closeWidth, 45f, RequestClose);
            SetUpgradeEnabled(false);
        }

        /// <summary>Header, slot, name, value and (result only) the amount row. Returns the bottom y of the column.</summary>
        private float BuildSidePanel(float x, float headerHeight, float slotTop, float slotSize, bool isBase)
        {
            UiKit.CreateHeader(_window, UiKit.L(isBase ? "itemupgrader_base" : "itemupgrader_target"), x, ColumnTop, SideWidth);

            var slot = UiKit.CreateSlot(_window, isBase ? "BaseSlot" : "TargetSlot", x + (SideWidth - slotSize) / 2f, slotTop, slotSize);
            var y = slotTop + slotSize + 8f;

            var name = UiKit.CreateLabel(_window, "Name", string.Empty, 17f, UiKit.TextName, TextAlignmentOptions.Center);
            UiKit.TopLeft(name.rectTransform, x, y, SideWidth, 44f);
            name.enableWordWrapping = true;
            name.overflowMode = TextOverflowModes.Ellipsis;
            y += 46f;

            var value = UiKit.CreateLabel(_window, "Value", string.Empty, 18f, UiKit.TextPrimary, TextAlignmentOptions.Center);
            UiKit.TopLeft(value.rectTransform, x, y, SideWidth, 24f);
            y += 26f;

            if (isBase)
            {
                _baseSlot = slot;
                _baseName = name;
                _baseValueLabel = value;
                return y;
            }

            _targetSlot = slot;
            _targetName = name;
            _targetName.text = UiKit.L("itemupgrader_select_target");
            _targetName.color = UiKit.TextDim;
            _targetValueLabel = value;

            // Amount row for stackable results: Amount [-] [ 32 ] [+]
            _amountRow = UiKit.CreateRect(_window, "AmountRow");
            UiKit.TopLeft(_amountRow, x, y, SideWidth, 28f);
            var amountLabel = UiKit.CreateLabel(_amountRow, "Label", UiKit.L("itemupgrader_amount"), 15f, UiKit.TextDim, TextAlignmentOptions.MidlineRight);
            UiKit.TopLeft(amountLabel.rectTransform, 24f, 0f, 86f, 28f);
            UiKit.CreateFlatButton(_amountRow, "Minus", "-", 118f, 0f, 28f, 28f, () => ChangeAmount(-StepSize()), out _, out _);
            _amountField = UiKit.CreateInputField(_amountRow, "Count", "1", 150f, 0f, 90f, 28f, TMP_InputField.ContentType.IntegerNumber, OnAmountTyped, OnAmountSubmitted);
            _amountField.onEndEdit.AddListener(OnAmountSubmitted);
            _amountField.onSelect.AddListener(_ => LogAmount("focus"));
            _amountField.characterLimit = 6;
            _amountField.textComponent.alignment = TextAlignmentOptions.Center;
            if (_amountField.placeholder is TextMeshProUGUI amountHint)
            {
                amountHint.alignment = TextAlignmentOptions.Center;
            }
            UiKit.CreateFlatButton(_amountRow, "Plus", "+", 244f, 0f, 28f, 28f, () => ChangeAmount(StepSize()), out _, out _);
            _amountRow.gameObject.SetActive(false);
            return y + 30f;
        }

        private void BuildGauge(float headerHeight, float centerY)
        {
            var left = Margin + SideWidth;
            var width = WindowWidth - 2f * left;
            var centerX = left + width / 2f;

            UiKit.CreateHeader(_window, UiKit.L("itemupgrader_chance"), left + 20f, ColumnTop, width - 40f);

            UiKit.CreateRing(_window, centerX - RingSize / 2f, centerY - RingSize / 2f, RingSize, out _, out _ringFill);

            _chanceLabel = UiKit.CreateLabel(_window, "Chance", "--", 40f, UiKit.TextPrimary, TextAlignmentOptions.Center);
            _chanceLabel.fontStyle = FontStyles.Bold;
            UiKit.TopLeft(_chanceLabel.rectTransform, centerX - 90f, centerY - 34f, 180f, 50f);

            _statusLabel = UiKit.CreateLabel(_window, "Status", string.Empty, 15f, UiKit.TextDim, TextAlignmentOptions.Center);
            UiKit.TopLeft(_statusLabel.rectTransform, centerX - 90f, centerY + 16f, 180f, 22f);

            // Pointer: a short bar across the ring band, rotated around the ring center
            _pointer = UiKit.CreateRect(_window, "Pointer");
            _pointer.anchorMin = new Vector2(0f, 1f);
            _pointer.anchorMax = new Vector2(0f, 1f);
            _pointer.pivot = new Vector2(0.5f, 0f);
            _pointer.anchoredPosition = new Vector2(centerX, -centerY);
            _pointer.sizeDelta = new Vector2(4f, RingSize / 2f + PointerOverhang);
            var tip = UiKit.CreateImage(_pointer, "Tip", UiKit.TextPrimary);
            tip.rectTransform.anchorMin = new Vector2(0f, 1f);
            tip.rectTransform.anchorMax = new Vector2(1f, 1f);
            tip.rectTransform.pivot = new Vector2(0.5f, 1f);
            tip.rectTransform.anchoredPosition = Vector2.zero;
            tip.rectTransform.sizeDelta = new Vector2(0f, 26f);

            _messageLabel = UiKit.CreateLabel(_window, "Message", string.Empty, 16f, UiKit.TextDim, TextAlignmentOptions.Center);
            UiKit.TopLeft(_messageLabel.rectTransform, left, centerY + RingSize / 2f + 10f, width, 44f);
            _messageLabel.enableWordWrapping = true;
        }

        private void BuildPicker(float y)
        {
            var x = Margin;
            foreach (var band in new[] { TargetListView.Band.All, TargetListView.Band.P70, TargetListView.Band.P50, TargetListView.Band.P30, TargetListView.Band.P10 })
            {
                var text = band == TargetListView.Band.All ? UiKit.L("itemupgrader_all") : band.ToString().Substring(1) + "%";
                var captured = band;
                UiKit.CreateFlatButton(_window, "Chip" + band, text, x, y, 88f, 32f, () => SelectBand(captured), out var background, out var label);
                _chips[band] = background;
                _chipLabels[band] = label;
                x += 92f;
            }

            _searchField = UiKit.CreateInputField(_window, "Search", UiKit.L("itemupgrader_search"), WindowWidth - Margin - 460f, y, 460f, 32f, TMP_InputField.ContentType.Standard, OnSearchChanged);

            var listTop = y + 42f;
            var listHeight = WindowHeight - 84f - listTop;
            _list = new TargetListView(_window, Margin, listTop, WindowWidth - 2f * Margin, listHeight, _icons);
            _list.Selected += SelectTarget;
            PaintChips();
        }

        // ----- base item ----------------------------------------------------------------------------

        private void SetBase(Item item)
        {
            _baseItem = item;
            _baseValue = 0;
            _baseSlot.Group.alpha = 1f;
            _baseSlot.SetItem(item, _icons);
            _baseName.text = item.Name.Localized();
            _baseValueLabel.text = UiKit.L("itemupgrader_loading");
            _baseValueLabel.color = UiKit.TextDim;
            SetState(State.Loading);
            RequestQuote(item);
        }

        private async void RequestQuote(Item item)
        {
            var token = ++_quoteToken;
            var quote = await UpgraderApi.QuoteAsync(item.Id);
            if (this == null || token != _quoteToken)
            {
                return;
            }

            if (quote == null || !quote.Ok)
            {
                _baseValueLabel.text = UiKit.L(quote?.Error ?? "itemupgrader_err_server");
                _baseValueLabel.color = UiKit.Red;
                SetState(State.Loading);
                return;
            }

            _baseValue = quote.BaseValue;
            _baseValueLabel.text = UiKit.Roubles(_baseValue);
            _baseValueLabel.color = UiKit.TextPrimary;
            _list.SetBaseValue(_baseValue);
            if (_target != null)
            {
                _amount = Math.Min(_amount, _target.MaxAmount);
                UpdateChance();
            }
            else
            {
                SetState(State.Idle);
            }
        }

        // ----- target -------------------------------------------------------------------------------

        private void SelectTarget(TargetItem target, int amount)
        {
            if (_state == State.Rolling || _state == State.Done)
            {
                return;
            }

            _target = target;
            _amount = Math.Max(1, Math.Min(target.MaxAmount, amount));
            var preview = UiKit.GetPreviewItem(target.Tpl);
            _targetSlot.Group.alpha = 1f;
            _targetSlot.SetItem(preview, _icons);
            _targetName.text = target.Name;
            _targetName.color = UiKit.TextName;
            _amountRow.gameObject.SetActive(target.Stackable);
            _amountField.SetTextWithoutNotify(_amount.ToString());
            UpdateChance();
        }

        private void UpdateChance()
        {
            if (_target == null || _baseValue <= 0)
            {
                _targetValueLabel.text = string.Empty;
                _chanceLabel.text = "--";
                _ringFill.fillAmount = 0f;
                SetState(_baseValue > 0 ? State.Idle : State.Loading);
                return;
            }

            var targetValue = _target.Entry.Value * _amount;
            _chance = UpgraderData.Chance(_baseValue, targetValue);
            _targetValueLabel.text = UiKit.Roubles(targetValue);
            _chanceLabel.text = UiKit.Percent(_chance);
            _chanceLabel.color = UiKit.TextPrimary;
            _ringFill.fillAmount = (float)(_chance / 100.0);
            _statusLabel.text = UiKit.L("itemupgrader_chance");
            _statusLabel.color = UiKit.TextDim;
            SetState(State.Ready);
        }

        private int StepSize()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                return 100;
            }

            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 10 : 1;
        }

        private void ChangeAmount(int delta)
        {
            if (_target == null || !_target.Stackable || _state == State.Rolling || _state == State.Done)
            {
                return;
            }

            _amount = Math.Max(1, Math.Min(_target.MaxAmount, _amount + delta));
            _amountField.SetTextWithoutNotify(_amount.ToString());
            UpdateChance();
        }

        private bool AmountEditable => _target != null && _target.Stackable && _state != State.Rolling && _state != State.Done;

        /// <summary>Live update while typing: the chance follows every key, the text is only rewritten when over the max.</summary>
        private void OnAmountTyped(string text)
        {
            LogAmount("typed '" + text + "'");
            if (!AmountEditable)
            {
                return;
            }

            if (!int.TryParse(text, out var parsed) || parsed < 1)
            {
                // Empty or "0" while the player is still typing: keep the last valid amount until the edit ends
                return;
            }

            if (parsed > _target.MaxAmount)
            {
                parsed = _target.MaxAmount;
                _amountField.SetTextWithoutNotify(parsed.ToString());
                _amountField.MoveTextEnd(false);
            }

            if (parsed != _amount)
            {
                _amount = parsed;
                UpdateChance();
            }
        }

        /// <summary>Enter or focus lost: settle the text on the clamped amount.</summary>
        private void OnAmountSubmitted(string text)
        {
            LogAmount("end edit '" + text + "'");
            if (!AmountEditable)
            {
                if (_target != null)
                {
                    _amountField.SetTextWithoutNotify(_amount.ToString());
                }

                return;
            }

            if (!int.TryParse(text, out var parsed))
            {
                parsed = _amount;
            }

            _amount = Math.Max(1, Math.Min(_target.MaxAmount, parsed));
            _amountField.SetTextWithoutNotify(_amount.ToString());
            UpdateChance();
        }

        private void LogAmount(string what)
        {
            if (Plugin.DebugLog.Value)
            {
                Plugin.LogSource.LogInfo($"itemUpgrader: amount field {what} (amount={_amount}, max={_target?.MaxAmount ?? 0}, state={_state}, focused={_amountField != null && _amountField.isFocused})");
            }
        }

        private void SelectBand(TargetListView.Band band)
        {
            _list.SetBand(band);
            PaintChips();
        }

        private void PaintChips()
        {
            foreach (var pair in _chips)
            {
                var on = pair.Key == _list.CurrentBand;
                pair.Value.color = on ? UiKit.Khaki : UiKit.ChipBg;
                _chipLabels[pair.Key].color = on ? UiKit.KhakiText : UiKit.TextName;
            }
        }

        private void OnSearchChanged(string text)
        {
            _list.SetSearch(text);
        }

        // ----- state --------------------------------------------------------------------------------

        private void SetState(State state)
        {
            _state = state;
            if (_amountField != null)
            {
                _amountField.interactable = state != State.Rolling && state != State.Done;
            }

            switch (state)
            {
                case State.Loading:
                case State.Idle:
                    _messageLabel.text = string.Empty;
                    _messageLabel.color = UiKit.TextDim;
                    SetUpgradeEnabled(false);
                    SetUpgradeText(UiKit.L("itemupgrader_upgrade"));
                    break;
                case State.Ready:
                    _messageLabel.text = string.Empty;
                    SetUpgradeEnabled(true);
                    SetUpgradeText(UiKit.L("itemupgrader_upgrade"));
                    break;
                case State.Rolling:
                    _messageLabel.text = string.Empty;
                    SetUpgradeEnabled(false);
                    if (_closeButton != null)
                    {
                        _closeButton.Interactable = false;
                    }

                    break;
                case State.Done:
                    if (_closeButton != null)
                    {
                        _closeButton.Interactable = true;
                    }

                    SetUpgradeText(UiKit.L("itemupgrader_again"));
                    SetUpgradeEnabled(_success && _wonItem != null);
                    break;
            }
        }

        private void SetUpgradeEnabled(bool enabled)
        {
            if (_upgradeButton != null)
            {
                _upgradeButton.Interactable = enabled;
            }
        }

        private void SetUpgradeText(string text)
        {
            if (_upgradeButton != null)
            {
                _upgradeButton.SetText(text);
            }
        }

        // ----- the roll -----------------------------------------------------------------------------

        private void OnUpgradeClicked()
        {
            if (_state == State.Done)
            {
                if (_success && _wonItem != null)
                {
                    UpgradeAgain();
                }

                return;
            }

            if (_state != State.Ready || _target == null || _baseItem == null)
            {
                return;
            }

            if (!Plugin.ConfirmBeforeUpgrade.Value)
            {
                StartRoll();
                return;
            }

            var context = ItemUiContext.Instance;
            if (context == null)
            {
                StartRoll();
                return;
            }

            var resultName = _amount > 1 ? _target.Name + " x" + _amount : _target.Name;
            var text = string.Format(UiKit.L("itemupgrader_confirm"), _baseItem.Name.Localized(), resultName, UiKit.Percent(_chance));
            context.ShowMessageWindow(text, StartRoll, null, UiKit.L("itemupgrader_title"));
            if (context._messageWindow != null)
            {
                context._messageWindow.transform.SetAsLastSibling();
            }
        }

        private void StartRoll()
        {
            if (this == null || _state != State.Ready || _target == null || _baseItem == null)
            {
                return;
            }

            _resultKnown = false;
            _success = false;
            _wonItem = null;
            _resultError = null;
            _lastResult = null;
            SetState(State.Rolling);
            _statusLabel.text = UiKit.L("itemupgrader_rolling");
            _statusLabel.color = UiKit.TextDim;

            StartCoroutine(Spin());
            SendUpgrade();
        }

        private async void SendUpgrade()
        {
            var inventory = ItemUiContext.Instance?.ClientSession?.Profile?.Inventory;
            var before = new HashSet<string>(ItemIds(inventory));
            var baseId = _baseItem.Id;
            var targetTpl = _target.Tpl;

            var result = await UpgraderApi.SendUpgradeAsync(baseId, targetTpl, _amount);
            if (this == null)
            {
                return;
            }

            if (result == null || result.Failed)
            {
                _resultError = string.IsNullOrEmpty(result?.Error) ? "itemupgrader_err_server" : result.Error;
                Plugin.LogSource.LogWarning($"itemUpgrader: upgrade of {baseId} rejected: {_resultError}");
                _resultKnown = true;
                return;
            }

            // The profile changes are already applied: the base item is gone and, on success, the result is in the stash
            var after = inventory != null ? inventory.GetPlayerItems(EPlayerItems.All).ToList() : new List<Item>();
            var newItems = after.Where(i => !before.Contains(i.Id)).ToList();
            var newIds = new HashSet<string>(newItems.Select(i => i.Id));
            _wonItem = newItems.FirstOrDefault(i =>
            {
                if (i.StringTemplateId != targetTpl)
                {
                    return false;
                }

                var parent = i.Parent?.Container?.ParentItem;
                return parent == null || !newIds.Contains(parent.Id);
            });
            _success = _wonItem != null;

            if (!_success && after.Any(i => i.Id == baseId))
            {
                // Nothing changed at all: the server did not handle the action
                _resultError = "itemupgrader_err_server";
            }

            _lastResult = await UpgraderApi.LastAsync();
            _resultKnown = true;
        }

        private static IEnumerable<string> ItemIds(Inventory inventory)
        {
            return inventory == null ? Enumerable.Empty<string>() : inventory.GetPlayerItems(EPlayerItems.All).Select(i => i.Id);
        }

        private IEnumerator Spin()
        {
            var angle = 0f;
            var waited = 0f;
            var lastTick = 0;

            // Free spin until the server answers
            while (!_resultKnown && waited < 20f)
            {
                var dt = Time.unscaledDeltaTime;
                waited += dt;
                angle += 540f * dt;
                SetPointer(angle);
                Tick(angle, ref lastTick);
                yield return null;
            }

            if (!_resultKnown)
            {
                _resultError = "itemupgrader_err_server";
            }

            if (_resultError != null)
            {
                SetPointer(0f);
                ShowError(_resultError);
                yield break;
            }

            // Ease out onto a point inside the winning or the losing arc
            var chanceDegrees = (float)(_chance * 3.6);
            var landing = _success
                ? UnityEngine.Random.Range(chanceDegrees * 0.1f, chanceDegrees * 0.9f)
                : chanceDegrees + UnityEngine.Random.Range((360f - chanceDegrees) * 0.1f, (360f - chanceDegrees) * 0.9f);

            var duration = Mathf.Max(1.2f, Plugin.SpinSeconds.Value);
            var start = angle;
            var turns = Mathf.Max(2, Mathf.RoundToInt(duration * 0.8f));
            var current = Mathf.Repeat(start, 360f);
            var final = start - current + turns * 360f + landing;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                angle = Mathf.Lerp(start, final, eased);
                SetPointer(angle);
                Tick(angle, ref lastTick);
                yield return null;
            }

            SetPointer(final);
            Reveal();
        }

        private void SetPointer(float angle)
        {
            if (_pointer != null)
            {
                _pointer.localEulerAngles = new Vector3(0f, 0f, -angle);
            }
        }

        private static void Tick(float angle, ref int lastTick)
        {
            var tick = Mathf.FloorToInt(angle / 30f);
            if (tick != lastTick)
            {
                lastTick = tick;
                UiKit.PlaySound(EUISoundType.ButtonOver);
            }
        }

        private void ShowError(string error)
        {
            _statusLabel.text = UiKit.L("itemupgrader_chance");
            _statusLabel.color = UiKit.TextDim;
            UiKit.PlaySound(EUISoundType.ErrorMessage);
            SetState(State.Ready);
            if (_closeButton != null)
            {
                _closeButton.Interactable = true;
            }

            _messageLabel.text = UiKit.L(error);
            _messageLabel.color = UiKit.Red;
        }

        private void Reveal()
        {
            if (_success)
            {
                _statusLabel.text = UiKit.L("itemupgrader_success");
                _statusLabel.color = UiKit.Green;
                _chanceLabel.color = UiKit.Green;
                _messageLabel.text = string.Format(UiKit.L("itemupgrader_won"), _amount > 1 ? _target.Name + " x" + _amount : _target.Name);
                _messageLabel.color = UiKit.Green;
                _baseSlot.Group.alpha = 0.35f;
                UiKit.PlaySound(EUISoundType.TradeOperationComplete);
            }
            else
            {
                _statusLabel.text = UiKit.L("itemupgrader_failed");
                _statusLabel.color = UiKit.Red;
                _chanceLabel.color = UiKit.Red;
                _messageLabel.text = string.Format(UiKit.L("itemupgrader_lost"), _baseItem != null ? _baseItem.Name.Localized() : string.Empty);
                _messageLabel.color = UiKit.Red;
                _baseSlot.Group.alpha = 0.35f;
                _targetSlot.Group.alpha = 0.35f;
                UiKit.PlaySound(EUISoundType.ErrorMessage);
            }

            if (_lastResult != null && _lastResult.Chance > 0)
            {
                _messageLabel.text += "  (" + UiKit.Percent(_lastResult.Roll) + " / " + UiKit.Percent(_lastResult.Chance) + ")";
            }

            SetState(State.Done);
        }

        /// <summary>After a win, the won item becomes the new base so the chain can continue.</summary>
        private void UpgradeAgain()
        {
            var next = _wonItem;
            _wonItem = null;
            _success = false;
            _target = null;
            _amount = 1;
            _chance = 0;
            _targetSlot.Group.alpha = 1f;
            _targetSlot.SetItem(null, _icons);
            _targetName.text = UiKit.L("itemupgrader_select_target");
            _targetName.color = UiKit.TextDim;
            _targetValueLabel.text = string.Empty;
            _amountRow.gameObject.SetActive(false);
            _chanceLabel.text = "--";
            _chanceLabel.color = UiKit.TextPrimary;
            _statusLabel.text = string.Empty;
            _messageLabel.text = string.Empty;
            _messageLabel.color = UiKit.TextDim;
            _ringFill.fillAmount = 0f;
            SetPointer(0f);
            _list.SetSelected(null);
            SetBase(next);
        }
    }
}
