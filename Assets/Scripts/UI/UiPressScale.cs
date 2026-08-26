using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Subtle press scale for existing UI buttons. Unscaled time. No new visuals.
/// </summary>
public class UiPressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [SerializeField] private float pressedScale = 0.96f;
    [SerializeField] private float duration = 0.06f;

    private RectTransform rect;
    private Vector3 restScale = Vector3.one;
    private Tween scaleTween;
    private bool captured;

    private void Awake()
    {
        rect = transform as RectTransform;
        CaptureRest();
    }

    private void OnDisable()
    {
        KillTween(false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        AnimateTo(restScale * pressedScale);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        AnimateTo(restScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        AnimateTo(restScale);
    }

    private void CaptureRest()
    {
        if (captured || rect == null)
        {
            return;
        }

        restScale = rect.localScale;
        if (restScale.sqrMagnitude < 0.0001f)
        {
            restScale = Vector3.one;
        }

        captured = true;
    }

    private void AnimateTo(Vector3 target)
    {
        CaptureRest();
        if (rect == null)
        {
            return;
        }

        KillTween(false);
        if (duration <= 0f)
        {
            rect.localScale = target;
            return;
        }

        Vector3 from = rect.localScale;
        scaleTween = TweenAnimationUtility.Progress(duration, t =>
            {
                float eased = TweenAnimationUtility.EvaluateEaseOutQuad(t);
                rect.localScale = Vector3.LerpUnclamped(from, target, eased);
            }, unscaled: true)
            .SetId(TweenAnimationUtility.UiPressId)
            .SetLink(gameObject)
            .OnComplete(() =>
            {
                rect.localScale = target;
                scaleTween = null;
            });
    }

    private void KillTween(bool complete)
    {
        if (scaleTween != null && scaleTween.IsActive())
        {
            scaleTween.Kill(complete);
        }

        scaleTween = null;
    }
}
