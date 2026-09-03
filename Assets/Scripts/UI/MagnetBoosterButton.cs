using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation-only Magnet booster button. Activates existing MagnetBooster;
/// does not implement movement or matching.
/// </summary>
[DisallowMultipleComponent]
public class MagnetBoosterButton : MonoBehaviour
{
    [SerializeField] private MagnetBooster magnetBooster;
    [SerializeField] private Button button;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TMP_Text iconText;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private TMP_Text chargeText;
    [SerializeField] private ShapeNestTheme theme;

    [SerializeField] private Color normalTint = new Color(0.45f, 0.4f, 0.75f, 1f);
    [SerializeField] private Color activeTint = new Color(0.62f, 0.52f, 0.95f, 1f);
    [SerializeField] private Color disabledTint = new Color(0.45f, 0.4f, 0.75f, 0.45f);

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

        if (magnetBooster == null)
        {
            magnetBooster = FindFirstObjectByType<MagnetBooster>();
        }

        if (magnetBooster != null)
        {
            magnetBooster.OnChargesChanged -= OnChargesChanged;
            magnetBooster.OnPhaseChanged -= OnPhaseChanged;
            magnetBooster.OnChargesChanged += OnChargesChanged;
            magnetBooster.OnPhaseChanged += OnPhaseChanged;
        }

        Refresh();
    }

    private void OnDisable()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClicked);
        }

        if (magnetBooster != null)
        {
            magnetBooster.OnChargesChanged -= OnChargesChanged;
            magnetBooster.OnPhaseChanged -= OnPhaseChanged;
        }
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClicked);
        }
    }

    private void OnClicked()
    {
        if (magnetBooster == null)
        {
            magnetBooster = FindFirstObjectByType<MagnetBooster>();
        }

        if (magnetBooster == null)
        {
            return;
        }

        if (magnetBooster.MagnetCharges <= 0 && !magnetBooster.IsSelecting)
        {
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Magnet, BoosterFailureReason.NoCharges);
            Refresh();
            return;
        }

        BoosterFailureReason reason;
        bool activated;
        BoosterManager manager = FindFirstObjectByType<BoosterManager>();
        if (manager != null)
        {
            activated = manager.TryActivate(BoosterType.Magnet, out reason);
        }
        else
        {
            activated = magnetBooster.TryBeginActivation(out reason);
        }

        if (!activated)
        {
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Magnet, reason);
        }

        Refresh();
    }

    private void OnChargesChanged(int _)
    {
        Refresh();
    }

    private void OnPhaseChanged(MagnetBooster.MagnetPhase _)
    {
        Refresh();
    }

    public void Refresh()
    {
        CacheRefs();
        if (magnetBooster == null)
        {
            magnetBooster = FindFirstObjectByType<MagnetBooster>();
        }

        int charges = magnetBooster != null ? magnetBooster.MagnetCharges : 0;
        bool selecting = magnetBooster != null && magnetBooster.IsSelecting;
        bool executing = magnetBooster != null && magnetBooster.Phase == MagnetBooster.MagnetPhase.Executing;
        bool hasCharges = charges > 0;
        bool interactable = !executing;

        if (chargeText != null)
        {
            chargeText.text = charges.ToString();
        }

        if (labelText != null)
        {
            labelText.text = selecting ? "SELECT" : "MAGNET";
        }

        if (iconText != null)
        {
            // Optional TMP icon; scene may use an Image (magnet_0) instead.
            if (string.IsNullOrEmpty(iconText.text))
            {
                iconText.text = "M";
            }
        }

        if (button != null)
        {
            button.interactable = interactable && !executing;
        }

        if (backgroundImage != null)
        {
            if (!hasCharges && !selecting)
            {
                backgroundImage.color = disabledTint;
            }
            else if (selecting || executing)
            {
                backgroundImage.color = activeTint;
            }
            else
            {
                backgroundImage.color = normalTint;
            }
        }

        // Dim optional icon Image children (e.g. magnet_0) with the button state.
        float iconAlpha = (!hasCharges && !selecting) ? 0.45f : 1f;
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

    public void Bind(
        MagnetBooster booster,
        ShapeNestTheme themeAsset,
        Button btn,
        Image background,
        TMP_Text icon,
        TMP_Text label,
        TMP_Text charge)
    {
        magnetBooster = booster;
        theme = themeAsset;
        button = btn;
        backgroundImage = background;
        iconText = icon;
        labelText = label;
        chargeText = charge;
        ApplyThemeDefaults();
        Refresh();
    }
}
