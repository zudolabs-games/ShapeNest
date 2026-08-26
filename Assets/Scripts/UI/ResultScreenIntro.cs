using DG.Tweening;
using UnityEngine;

/// <summary>
/// Short unscaled entrance for result screens. Designer assigns existing
/// hierarchy refs; this component does not create visuals.
/// </summary>
public class ResultScreenIntro : MonoBehaviour
{
    [SerializeField] private CanvasGroup background;
    [SerializeField] private RectTransform panel;
    [SerializeField] private CanvasGroup titleGroup;
    [SerializeField] private CanvasGroup infoGroup;
    [SerializeField] private CanvasGroup buttonGroup;

    [SerializeField] private float backgroundDuration = 0.12f;
    [SerializeField] private float panelDuration = 0.2f;
    [SerializeField] private float panelStartScale = 0.92f;
    [SerializeField] private float elementDuration = 0.12f;
    [SerializeField] private float elementStartScale = 0.96f;
    [SerializeField] private float titleDelay = 0.08f;
    [SerializeField] private float infoDelay = 0.14f;
    [SerializeField] private float buttonDelay = 0.22f;

    private Sequence sequence;
    private Vector3 panelRest = Vector3.one;
    private Vector3 titleRest = Vector3.one;
    private Vector3 infoRest = Vector3.one;
    private Vector3 buttonRest = Vector3.one;
    private bool capturedRests;

    public void Play()
    {
        CaptureRests();
        StopAndHold();
        ApplyHidden();

        float total = Mathf.Max(
            backgroundDuration,
            panelDuration,
            titleDelay + elementDuration,
            infoDelay + elementDuration,
            buttonDelay + elementDuration);

        sequence = DOTween.Sequence().SetId(TweenAnimationUtility.HudId).SetUpdate(true).SetLink(gameObject);
        sequence.Append(TweenAnimationUtility.Progress(total, t =>
        {
            Evaluate(t * total);
        }, unscaled: true));
        sequence.OnComplete(() =>
        {
            Evaluate(total);
            if (buttonGroup != null)
            {
                buttonGroup.interactable = true;
                buttonGroup.blocksRaycasts = true;
            }

            sequence = null;
        });
    }

    public void StopAndHold()
    {
        if (sequence != null && sequence.IsActive())
        {
            sequence.Kill(false);
        }

        sequence = null;
    }

    private void OnDisable()
    {
        StopAndHold();
    }

    private void CaptureRests()
    {
        if (capturedRests)
        {
            return;
        }

        if (panel != null)
        {
            panelRest = panel.localScale;
        }

        if (titleGroup != null)
        {
            titleRest = titleGroup.transform.localScale;
        }

        if (infoGroup != null)
        {
            infoRest = infoGroup.transform.localScale;
        }

        if (buttonGroup != null)
        {
            buttonRest = buttonGroup.transform.localScale;
        }

        capturedRests = true;
    }

    private void ApplyHidden()
    {
        SetGroup(background, 0f, true);
        if (panel != null)
        {
            panel.localScale = panelRest * panelStartScale;
        }

        PrepareElement(titleGroup, titleRest);
        PrepareElement(infoGroup, infoRest);
        PrepareElement(buttonGroup, buttonRest);
        if (buttonGroup != null)
        {
            buttonGroup.interactable = false;
            buttonGroup.blocksRaycasts = false;
        }
    }

    private void Evaluate(float elapsed)
    {
        if (background != null && backgroundDuration > 0f)
        {
            background.alpha = EaseOutQuad(Mathf.Clamp01(elapsed / backgroundDuration));
        }

        if (panel != null && panelDuration > 0f)
        {
            float t = EaseOutCubic(Mathf.Clamp01(elapsed / panelDuration));
            panel.localScale = Vector3.LerpUnclamped(panelRest * panelStartScale, panelRest, t);
        }

        AnimateElement(titleGroup, titleRest, elapsed - titleDelay);
        AnimateElement(infoGroup, infoRest, elapsed - infoDelay);
        AnimateElement(buttonGroup, buttonRest, elapsed - buttonDelay);
    }

    private void PrepareElement(CanvasGroup group, Vector3 rest)
    {
        if (group == null)
        {
            return;
        }

        group.alpha = 0f;
        group.transform.localScale = rest * elementStartScale;
    }

    private void AnimateElement(CanvasGroup group, Vector3 rest, float localTime)
    {
        if (group == null)
        {
            return;
        }

        if (localTime <= 0f)
        {
            group.alpha = 0f;
            group.transform.localScale = rest * elementStartScale;
            return;
        }

        float t = EaseOutCubic(Mathf.Clamp01(localTime / Mathf.Max(0.01f, elementDuration)));
        group.alpha = t;
        group.transform.localScale = Vector3.LerpUnclamped(rest * elementStartScale, rest, t);
    }

    private static void SetGroup(CanvasGroup group, float alpha, bool blockRaycasts)
    {
        if (group == null)
        {
            return;
        }

        group.alpha = alpha;
        group.blocksRaycasts = blockRaycasts;
        group.interactable = blockRaycasts;
    }

    private static float EaseOutQuad(float t)
    {
        return 1f - ((1f - t) * (1f - t));
    }

    private static float EaseOutCubic(float t)
    {
        float inv = 1f - t;
        return 1f - (inv * inv * inv);
    }
}
