using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation-only Hammer booster button. Activates existing HammerBooster
/// through <see cref="BoosterManager"/>; does not implement removal or targeting.
/// </summary>
[DisallowMultipleComponent]
public class HammerBoosterButton : MonoBehaviour
{
    [SerializeField] private HammerBooster hammerBooster;
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

        ResolveHammer();
        BindHammerEvents();
        Refresh();
    }

    private void Start()
    {
        ResolveHammer();
        BindHammerEvents();
        Refresh();
    }

    private void OnDisable()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClicked);
        }

        UnbindHammerEvents();
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
        ResolveHammer();
        if (hammerBooster == null)
        {
            return;
        }

        if (hammerBooster.HammerCharges <= 0 && !hammerBooster.IsSelecting)
        {
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Hammer, BoosterFailureReason.NoCharges);
            Refresh();
            return;
        }

        BoosterFailureReason reason;
        bool activated;
        BoosterManager manager = FindFirstObjectByType<BoosterManager>();
        if (manager != null)
        {
            activated = manager.TryActivate(BoosterType.Hammer, out reason);
        }
        else
        {
            activated = hammerBooster.TryBeginActivation(out reason);
        }

        if (!activated)
        {
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Hammer, reason);
        }

        Refresh();
    }

    private void OnChargesChanged(int _)
    {
        Refresh();
    }

    private void OnPhaseChanged(HammerBooster.HammerPhase _)
    {
        Refresh();
    }

    public void Refresh()
    {
        CacheRefs();
        ResolveHammer();

        int charges = hammerBooster != null ? hammerBooster.HammerCharges : 0;
        bool selecting = hammerBooster != null && hammerBooster.IsSelecting;
        bool executing = hammerBooster != null && hammerBooster.Phase == HammerBooster.HammerPhase.Executing;
        bool hasCharges = charges > 0;
        bool interactable = !executing;

        if (chargeText != null)
        {
            chargeText.text = charges.ToString();
        }

        if (labelText != null)
        {
            labelText.text = selecting ? "SELECT" : "HAMMER";
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

    private void ResolveHammer()
    {
        if (hammerBooster == null)
        {
            hammerBooster = FindFirstObjectByType<HammerBooster>();
        }
    }

    private void BindHammerEvents()
    {
        if (hammerBooster == null)
        {
            return;
        }

        hammerBooster.OnChargesChanged -= OnChargesChanged;
        hammerBooster.OnPhaseChanged -= OnPhaseChanged;
        hammerBooster.OnChargesChanged += OnChargesChanged;
        hammerBooster.OnPhaseChanged += OnPhaseChanged;
    }

    private void UnbindHammerEvents()
    {
        if (hammerBooster == null)
        {
            return;
        }

        hammerBooster.OnChargesChanged -= OnChargesChanged;
        hammerBooster.OnPhaseChanged -= OnPhaseChanged;
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
}
