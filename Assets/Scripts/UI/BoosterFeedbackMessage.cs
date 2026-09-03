using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation-only toast for booster failure feedback.
/// Does not activate boosters, consume charges, or alter board state.
/// </summary>
[DisallowMultipleComponent]
public class BoosterFeedbackMessage : MonoBehaviour
{
    public const string RootObjectName = "BoosterFeedbackMessage";
    public const string TweenId = "ShapeNest.BoosterFeedback";

    private const float DefaultDuration = 1.75f;
    private const float ShowSeconds = 0.18f;
    private const float HideSeconds = 0.14f;
    private const float ShowFromScale = 0.92f;
    private const float HideToScale = 0.97f;

    private static Sprite whiteSprite;

    [SerializeField]
    private CanvasGroup canvasGroup;

    [SerializeField]
    private RectTransform panelRect;

    [SerializeField]
    private TMP_Text messageText;

    [SerializeField]
    private Image panelImage;

    private Tween sequenceTween;
    private string currentMessage;
    private bool visible;

    public bool IsVisible => visible && gameObject.activeInHierarchy;
    public string CurrentMessage => currentMessage;
    public static int InstanceCount =>
        Object.FindObjectsByType<BoosterFeedbackMessage>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

    public static BoosterFeedbackMessage Ensure()
    {
        BoosterFeedbackMessage existing = FindExisting();
        if (existing != null)
        {
            existing.EnsureBuilt();
            return existing;
        }

        Transform parent = FindGameplayParent();
        if (parent == null)
        {
            Debug.LogWarning("BoosterFeedbackMessage: GameplayCanvas/Parent not found.");
            return null;
        }

        GameObject root = new GameObject(RootObjectName, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        BoosterFeedbackMessage message = root.AddComponent<BoosterFeedbackMessage>();
        message.EnsureBuilt();
        root.SetActive(false);
        return message;
    }

    public static BoosterFeedbackMessage FindExisting()
    {
        Transform parent = FindGameplayParent();
        if (parent != null)
        {
            Transform child = parent.Find(RootObjectName);
            if (child != null)
            {
                return child.GetComponent<BoosterFeedbackMessage>();
            }
        }

        return Object.FindFirstObjectByType<BoosterFeedbackMessage>(FindObjectsInactive.Include);
    }

    public static void HideExisting(bool immediate = true)
    {
        BoosterFeedbackMessage existing = FindExisting();
        if (existing != null)
        {
            existing.Hide(immediate);
        }
    }

    public static void NotifyFailure(BoosterType type, BoosterFailureReason reason)
    {
        if (reason == BoosterFailureReason.None)
        {
            return;
        }

        string text = ResolveMessage(type, reason);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        BoosterFeedbackMessage presenter = Ensure();
        if (presenter != null)
        {
            presenter.Show(text);
        }
    }

    public static string ResolveMessage(BoosterType type, BoosterFailureReason reason)
    {
        switch (reason)
        {
            case BoosterFailureReason.NoCharges:
                switch (type)
                {
                    case BoosterType.Magnet:
                        return "Magnet has no charges";
                    case BoosterType.Hammer:
                        return "Hammer has no charges";
                    case BoosterType.Shuffle:
                        return "Shuffle has no charges";
                    case BoosterType.Undo:
                        return "Undo has no charges";
                    default:
                        return "No charges remaining";
                }

            case BoosterFailureReason.Busy:
                switch (type)
                {
                    case BoosterType.Magnet:
                        return "Magnet is already active";
                    case BoosterType.Hammer:
                        return "Hammer is already active";
                    case BoosterType.Shuffle:
                        return "Shuffle is already active";
                    case BoosterType.Undo:
                        return "Undo is already active";
                    default:
                        return "Booster is already active";
                }

            case BoosterFailureReason.NoValidTarget:
                switch (type)
                {
                    case BoosterType.Magnet:
                        return "No block can be magnetized";
                    case BoosterType.Hammer:
                        return "No block can be smashed";
                    default:
                        return "No valid target";
                }

            case BoosterFailureReason.InvalidTarget:
                switch (type)
                {
                    case BoosterType.Magnet:
                        return "Magnet can't be used on this block";
                    case BoosterType.Hammer:
                        return "Hammer can't be used on this block";
                    default:
                        return "Can't be used on this block";
                }

            case BoosterFailureReason.NoUndoAvailable:
                return "Nothing to undo";

            case BoosterFailureReason.NoShufflePlan:
                return "Shuffle can't find a valid arrangement";

            case BoosterFailureReason.Unavailable:
                switch (type)
                {
                    case BoosterType.Undo:
                        return "Undo isn't available right now";
                    case BoosterType.Magnet:
                        return "Magnet isn't available right now";
                    case BoosterType.Hammer:
                        return "Hammer isn't available right now";
                    case BoosterType.Shuffle:
                        return "Shuffle isn't available right now";
                    default:
                        return "Booster isn't available right now";
                }

            default:
                return null;
        }
    }

    public void Show(string message)
    {
        Show(message, DefaultDuration);
    }

    public void Show(string message, float duration)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        EnsureBuilt();
        currentMessage = message;
        if (messageText != null)
        {
            messageText.text = message;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        KillTween(false);
        visible = true;

        float hold = Mathf.Max(0.35f, duration);
        Vector3 restScale = Vector3.one;
        if (panelRect != null)
        {
            panelRect.localScale = Vector3.one * ShowFromScale;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        Sequence sequence = DOTween.Sequence()
            .SetId(TweenAnimationUtility.BoosterFeedbackId)
            .SetUpdate(true)
            .SetLink(gameObject);

        sequence.Append(TweenAnimationUtility.Progress(ShowSeconds, t =>
            {
                float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.LerpUnclamped(0f, 1f, eased);
                }

                if (panelRect != null)
                {
                    float scale = Mathf.LerpUnclamped(ShowFromScale, 1f, eased);
                    panelRect.localScale = restScale * scale;
                }
            }, unscaled: true));

        sequence.AppendInterval(hold);

        sequence.Append(TweenAnimationUtility.Progress(HideSeconds, t =>
            {
                float eased = TweenAnimationUtility.EvaluateEaseInCubic(t);
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.LerpUnclamped(1f, 0f, eased);
                }

                if (panelRect != null)
                {
                    float scale = Mathf.LerpUnclamped(1f, HideToScale, eased);
                    panelRect.localScale = restScale * scale;
                }
            }, unscaled: true));

        sequence.OnComplete(() =>
        {
            visible = false;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            if (panelRect != null)
            {
                panelRect.localScale = restScale;
            }

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            sequenceTween = null;
        });

        sequenceTween = sequence;
    }

    public void Hide(bool immediate = false)
    {
        KillTween(false);
        visible = false;
        currentMessage = null;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }

        if (panelRect != null)
        {
            panelRect.localScale = Vector3.one;
        }

        if (immediate || !gameObject.activeSelf)
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            return;
        }

        gameObject.SetActive(false);
    }

    public void Clear()
    {
        Hide(true);
    }

    private void OnDisable()
    {
        KillTween(false);
        visible = false;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }

    private void OnDestroy()
    {
        KillTween(false);
        TweenAnimationUtility.KillById(gameObject, TweenAnimationUtility.BoosterFeedbackId, false);
        DOTween.Kill(TweenAnimationUtility.BoosterFeedbackId, false);
    }

    private void EnsureBuilt()
    {
        if (panelRect != null && canvasGroup != null && messageText != null)
        {
            ApplyLayout();
            return;
        }

        RectTransform rootRect = transform as RectTransform;
        if (rootRect == null)
        {
            rootRect = gameObject.AddComponent<RectTransform>();
        }

        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = new Vector2(560f, 72f);
        rootRect.anchoredPosition = new Vector2(0f, 220f);
        rootRect.localScale = Vector3.one;
        rootRect.SetAsLastSibling();

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        Transform panelTransform = transform.Find("Panel");
        if (panelTransform == null)
        {
            GameObject panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelTransform = panelGo.transform;
            panelTransform.SetParent(transform, false);
        }

        panelRect = panelTransform as RectTransform;
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panelRect.localScale = Vector3.one;

        panelImage = panelTransform.GetComponent<Image>();
        if (panelImage == null)
        {
            panelImage = panelTransform.gameObject.AddComponent<Image>();
        }

        Transform textTransform = panelTransform.Find("MessageText");
        if (textTransform == null)
        {
            GameObject textGo = new GameObject("MessageText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textTransform = textGo.transform;
            textTransform.SetParent(panelTransform, false);
        }

        messageText = textTransform.GetComponent<TMP_Text>();
        if (messageText == null)
        {
            messageText = textTransform.gameObject.AddComponent<TextMeshProUGUI>();
        }

        RectTransform textRect = textTransform as RectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(28f, 12f);
        textRect.offsetMax = new Vector2(-28f, -12f);

        ApplyTheme();
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        RectTransform rootRect = transform as RectTransform;
        if (rootRect == null)
        {
            return;
        }

        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = new Vector2(560f, 72f);
        rootRect.anchoredPosition = new Vector2(0f, 220f);
    }

    private void ApplyTheme()
    {
        ShapeNestTheme theme = null;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            theme = UnityEditor.AssetDatabase.LoadAssetAtPath<ShapeNestTheme>("Assets/Scripts/UI/ShapeNestTheme.asset");
        }
#endif
        if (theme == null)
        {
            ShapeNestTheme[] themes = Resources.FindObjectsOfTypeAll<ShapeNestTheme>();
            if (themes != null && themes.Length > 0)
            {
                theme = themes[0];
            }
        }

        if (panelImage != null)
        {
            Color panel = theme != null
                ? theme.panelBackground
                : new Color(0.22f, 0.18f, 0.34f, 0.96f);
            panel.a = Mathf.Max(0.92f, panel.a);
            panelImage.color = panel;
            panelImage.raycastTarget = false;
            if (theme != null && theme.panelSprite != null)
            {
                panelImage.sprite = theme.panelSprite;
                panelImage.type = theme.panelSprite.border.sqrMagnitude > 0.01f
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
            }
            else
            {
                panelImage.sprite = ResolveWhiteSprite();
                panelImage.type = Image.Type.Simple;
            }
        }

        if (messageText != null)
        {
            messageText.raycastTarget = false;
            messageText.alignment = TextAlignmentOptions.Center;
            messageText.enableWordWrapping = true;
            messageText.fontSize = 28f;
            messageText.color = theme != null
                ? theme.primaryText
                : new Color(0.93f, 0.91f, 0.98f, 1f);
            TMP_FontAsset font = theme != null
                ? (theme.buttonFont != null ? theme.buttonFont : theme.mainFont)
                : null;
            if (font != null)
            {
                messageText.font = font;
            }
        }
    }

    private void KillTween(bool complete)
    {
        if (sequenceTween != null && sequenceTween.IsActive())
        {
            sequenceTween.Kill(complete);
        }

        sequenceTween = null;
        TweenAnimationUtility.KillById(gameObject, TweenAnimationUtility.BoosterFeedbackId, complete);
        DOTween.Kill(TweenAnimationUtility.BoosterFeedbackId, complete);
    }

    private static Transform FindGameplayParent()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas.name != "GameplayCanvas")
            {
                continue;
            }

            Transform parent = canvas.transform.Find("Parent");
            if (parent != null)
            {
                return parent;
            }
        }

        return null;
    }

    private static Sprite ResolveWhiteSprite()
    {
        if (whiteSprite != null)
        {
            return whiteSprite;
        }

        Texture2D tex = Texture2D.whiteTexture;
        whiteSprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            4f,
            0,
            SpriteMeshType.FullRect);
        whiteSprite.name = "BoosterFeedbackWhite";
        return whiteSprite;
    }
}
