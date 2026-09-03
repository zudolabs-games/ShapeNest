using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation-only Shuffle booster button. Activates <see cref="ShuffleBooster"/>
/// through <see cref="BoosterManager"/>.
/// </summary>
[DisallowMultipleComponent]
public class ShuffleBoosterButton : MonoBehaviour
{
    [SerializeField]
    private ShuffleBooster shuffleBooster;

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

        ResolveShuffle();
        BindShuffleEvents();
        Refresh();
    }

    private void Start()
    {
        ResolveShuffle();
        BindShuffleEvents();
        Refresh();
    }

    private void OnDisable()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClicked);
        }

        UnbindShuffleEvents();
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
        ResolveShuffle();
        if (shuffleBooster == null)
        {
            return;
        }

        if (shuffleBooster.ShuffleCharges <= 0)
        {
            PlayInvalidPress();
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Shuffle, BoosterFailureReason.NoCharges);
            Refresh();
            return;
        }

        BoosterFailureReason reason;
        bool activated;
        BoosterManager manager = FindFirstObjectByType<BoosterManager>();
        if (manager != null)
        {
            activated = manager.TryActivate(BoosterType.Shuffle, out reason);
        }
        else
        {
            activated = shuffleBooster.TryBeginActivation(out reason);
        }

        if (activated)
        {
            PlayActivatePulse();
        }
        else
        {
            PlayInvalidPress();
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Shuffle, reason);
        }

        Refresh();
    }

    private void OnChargesChanged(int _)
    {
        Refresh();
    }

    private void OnPhaseChanged(ShuffleBooster.ShufflePhase _)
    {
        Refresh();
    }

    public void Refresh()
    {
        CacheRefs();
        ResolveShuffle();

        int charges = shuffleBooster != null ? shuffleBooster.ShuffleCharges : 0;
        bool executing = shuffleBooster != null && shuffleBooster.Phase == ShuffleBooster.ShufflePhase.Executing;
        bool hasCharges = charges > 0;

        if (chargeText != null)
        {
            chargeText.text = charges.ToString();
        }

        if (labelText != null)
        {
            labelText.text = "SHUFFLE";
        }

        if (iconText != null && string.IsNullOrEmpty(iconText.text))
        {
            iconText.text = "S";
        }

        if (button != null)
        {
            button.interactable = !executing;
        }

        if (backgroundImage != null)
        {
            if (!hasCharges)
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

        float iconAlpha = hasCharges ? 1f : 0.45f;
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

    private void ResolveShuffle()
    {
        if (shuffleBooster == null)
        {
            shuffleBooster = FindFirstObjectByType<ShuffleBooster>();
        }
    }

    private void BindShuffleEvents()
    {
        if (shuffleBooster == null)
        {
            return;
        }

        shuffleBooster.OnChargesChanged -= OnChargesChanged;
        shuffleBooster.OnPhaseChanged -= OnPhaseChanged;
        shuffleBooster.OnChargesChanged += OnChargesChanged;
        shuffleBooster.OnPhaseChanged += OnPhaseChanged;
    }

    private void UnbindShuffleEvents()
    {
        if (shuffleBooster == null)
        {
            return;
        }

        shuffleBooster.OnChargesChanged -= OnChargesChanged;
        shuffleBooster.OnPhaseChanged -= OnPhaseChanged;
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
