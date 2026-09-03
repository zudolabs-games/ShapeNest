using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation-only Undo booster button. Activates <see cref="UndoBooster"/>
/// through <see cref="BoosterManager"/>.
/// </summary>
[DisallowMultipleComponent]
public class UndoBoosterButton : MonoBehaviour
{
    [SerializeField]
    private UndoBooster undoBooster;

    [SerializeField]
    private BoardUndoHistory undoHistory;

    [SerializeField]
    private Button button;

    [SerializeField]
    private Image backgroundImage;

    [SerializeField]
    private TMP_Text iconText;

    [SerializeField]
    private TMP_Text labelText;

    [SerializeField]
    private TMP_Text chargeText;

    [SerializeField]
    private ShapeNestTheme theme;

    [SerializeField]
    private Color normalTint = new Color(0.45f, 0.4f, 0.75f, 1f);

    [SerializeField]
    private Color activeTint = new Color(0.62f, 0.52f, 0.95f, 1f);

    [SerializeField]
    private Color disabledTint = new Color(0.45f, 0.4f, 0.75f, 0.45f);

    private RectTransform pressRect;
    private Vector3 pressRestScale = Vector3.one;
    private bool pressRestCaptured;
    private Tween pressTween;

    private const float ActivatePressScale = 0.96f;
    private const float ActivatePressDownSeconds = 0.06f;
    private const float ActivatePressUpSeconds = 0.08f;
    private const float InvalidPressScale = 0.97f;
    private const float InvalidPressSeconds = 0.10f;

    private void Awake()
    {
        CacheRefs();
        ApplyThemeDefaults();
    }

    private void OnEnable()
    {
        CacheRefs();
        if (button != null)
        {
            button.onClick.RemoveListener(OnClicked);
            button.onClick.AddListener(OnClicked);
        }

        ResolveUndo();
        BindEvents();
        Refresh();
    }

    private void Start()
    {
        ResolveUndo();
        BindEvents();
        Refresh();
    }

    private void OnDisable()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClicked);
        }

        UnbindEvents();
        KillPressTween(false);
        RestorePressScale();
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClicked);
        }

        KillPressTween(false);
        RestorePressScale();
    }

    private void OnClicked()
    {
        ResolveUndo();
        if (undoBooster == null)
        {
            return;
        }

        if (undoBooster.UndoCharges <= 0)
        {
            PlayInvalidPress();
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Undo, BoosterFailureReason.NoCharges);
            Refresh();
            return;
        }

        if (undoHistory == null || !undoHistory.HasUndoableSnapshot)
        {
            PlayInvalidPress();
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Undo, BoosterFailureReason.NoUndoAvailable);
            Refresh();
            return;
        }

        BoosterFailureReason reason;
        bool activated;
        BoosterManager manager = FindFirstObjectByType<BoosterManager>();
        if (manager != null)
        {
            activated = manager.TryActivate(BoosterType.Undo, out reason);
        }
        else
        {
            activated = undoBooster.TryBeginActivation(out reason);
        }

        if (activated)
        {
            PlayActivatePulse();
        }
        else
        {
            PlayInvalidPress();
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Undo, reason);
        }

        Refresh();
    }

    private void OnChargesChanged(int _)
    {
        Refresh();
    }

    private void OnPhaseChanged(UndoBooster.UndoPhase _)
    {
        Refresh();
    }

    private void OnHistoryChanged()
    {
        Refresh();
    }

    public void Refresh()
    {
        CacheRefs();
        ResolveUndo();

        int charges = undoBooster != null ? undoBooster.UndoCharges : 0;
        bool executing = undoBooster != null && undoBooster.Phase == UndoBooster.UndoPhase.Executing;
        bool hasHistory = undoHistory != null && undoHistory.HasUndoableSnapshot;
        bool canUse = hasHistory && charges > 0 && !executing;

        if (chargeText != null)
        {
            chargeText.text = charges.ToString();
        }

        if (labelText != null)
        {
            labelText.text = "UNDO";
        }

        if (iconText != null && string.IsNullOrEmpty(iconText.text))
        {
            iconText.text = "U";
        }

        if (button != null)
        {
            button.interactable = !executing;
        }

        if (backgroundImage != null)
        {
            if (charges <= 0 || !hasHistory)
            {
                backgroundImage.color = disabledTint;
            }
            else if (executing)
            {
                backgroundImage.color = activeTint;
            }
            else
            {
                backgroundImage.color = normalTint;
            }
        }

        float iconAlpha = canUse ? 1f : 0.45f;
        for (int i = 0; i < transform.childCount; i++)
        {
            Image childImage = transform.GetChild(i).GetComponent<Image>();
            if (childImage == null || childImage == backgroundImage)
            {
                continue;
            }

            Color c = childImage.color;
            c.a = iconAlpha;
            childImage.color = c;
        }
    }

    private void CacheRefs()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (backgroundImage == null)
        {
            backgroundImage = GetComponent<Image>();
        }

        if (labelText == null)
        {
            labelText = FindChildText("LabelText") ?? FindChildText("Label");
        }

        if (chargeText == null)
        {
            chargeText = FindChildText("ChargeText") ?? FindChildText("Charge");
        }
    }

    private TMP_Text FindChildText(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<TMP_Text>() : null;
    }

    private void ResolveUndo()
    {
        if (undoBooster == null)
        {
            undoBooster = FindFirstObjectByType<UndoBooster>();
        }

        if (undoHistory == null)
        {
            undoHistory = BoardUndoHistory.Resolve();
        }
    }

    private void BindEvents()
    {
        if (undoBooster != null)
        {
            undoBooster.OnChargesChanged -= OnChargesChanged;
            undoBooster.OnPhaseChanged -= OnPhaseChanged;
            undoBooster.OnChargesChanged += OnChargesChanged;
            undoBooster.OnPhaseChanged += OnPhaseChanged;
        }

        if (undoHistory != null)
        {
            undoHistory.OnHistoryChanged -= OnHistoryChanged;
            undoHistory.OnHistoryChanged += OnHistoryChanged;
        }
    }

    private void UnbindEvents()
    {
        if (undoBooster != null)
        {
            undoBooster.OnChargesChanged -= OnChargesChanged;
            undoBooster.OnPhaseChanged -= OnPhaseChanged;
        }

        if (undoHistory != null)
        {
            undoHistory.OnHistoryChanged -= OnHistoryChanged;
        }
    }

    private void ApplyThemeDefaults()
    {
        if (theme == null)
        {
            return;
        }

        normalTint = theme.buttonNormal;
        activeTint = Color.Lerp(theme.buttonNormal, Color.white, 0.28f);
        disabledTint = theme.buttonDisabled;

        TMP_FontAsset font = theme.buttonFont != null ? theme.buttonFont : theme.mainFont;
        if (font != null)
        {
            if (labelText != null)
            {
                labelText.font = font;
                labelText.color = theme.primaryText;
            }

            if (chargeText != null)
            {
                chargeText.font = font;
                chargeText.color = theme.primaryText;
            }

            if (iconText != null)
            {
                iconText.font = font;
            }
        }

        if (backgroundImage != null && theme.buttonSprite != null)
        {
            backgroundImage.sprite = theme.buttonSprite;
            backgroundImage.type = theme.buttonSprite.border.sqrMagnitude > 0.01f
                ? Image.Type.Sliced
                : Image.Type.Simple;
        }
    }

    private void EnsurePressRect()
    {
        if (pressRect == null)
        {
            pressRect = transform as RectTransform;
        }

        if (pressRect != null && !pressRestCaptured)
        {
            pressRestScale = pressRect.localScale;
            if (pressRestScale.sqrMagnitude < 0.0001f)
            {
                pressRestScale = Vector3.one;
            }

            pressRestCaptured = true;
        }
    }

    private void PlayActivatePulse()
    {
        EnsurePressRect();
        if (pressRect == null)
        {
            return;
        }

        KillPressTween(false);
        Sequence sequence = DOTween.Sequence()
            .SetId(TweenAnimationUtility.UiPressId)
            .SetLink(gameObject);
        sequence.Append(AnimatePressScale(pressRestScale * ActivatePressScale, ActivatePressDownSeconds));
        sequence.Append(AnimatePressScale(pressRestScale, ActivatePressUpSeconds));
        pressTween = sequence;
    }

    private void PlayInvalidPress()
    {
        EnsurePressRect();
        if (pressRect == null)
        {
            return;
        }

        KillPressTween(false);
        float half = InvalidPressSeconds * 0.5f;
        Sequence sequence = DOTween.Sequence()
            .SetId(TweenAnimationUtility.UiPressId)
            .SetLink(gameObject);
        sequence.Append(AnimatePressScale(pressRestScale * InvalidPressScale, half));
        sequence.Append(AnimatePressScale(pressRestScale, half));
        pressTween = sequence;
    }

    private Tween AnimatePressScale(Vector3 target, float duration)
    {
        if (pressRect == null)
        {
            return null;
        }

        if (duration <= 0f)
        {
            pressRect.localScale = target;
            return null;
        }

        Vector3 from = pressRect.localScale;
        return TweenAnimationUtility.Progress(duration, t =>
            {
                float eased = TweenAnimationUtility.EvaluateEaseOutQuad(t);
                pressRect.localScale = Vector3.LerpUnclamped(from, target, eased);
            }, unscaled: true)
            .SetLink(gameObject);
    }

    private void KillPressTween(bool complete)
    {
        if (pressTween != null && pressTween.IsActive())
        {
            pressTween.Kill(complete);
        }

        pressTween = null;
        TweenAnimationUtility.KillById(gameObject, TweenAnimationUtility.UiPressId, complete);
    }

    private void RestorePressScale()
    {
        EnsurePressRect();
        if (pressRect != null)
        {
            pressRect.localScale = pressRestScale;
        }
    }
}
