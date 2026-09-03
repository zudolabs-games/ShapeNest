using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Shared DOTween helpers for presentation animation. Gameplay sequencing stays outside.
/// </summary>
public static class TweenAnimationUtility
{
    public const string PieceMotionId = "ShapeNest.PieceMotion";
    public const string CarryId = "ShapeNest.Carry";
    public const string SelectionId = "ShapeNest.Selection";
    public const string InteractionId = "ShapeNest.Interaction";
    public const string NestSocketId = "ShapeNest.NestSocket";
    public const string ReadyPulseId = "ShapeNest.ReadyPulse";
    public const string UiPressId = "ShapeNest.UiPress";
    public const string HudId = "ShapeNest.Hud";
    public const string MatchEffectId = "ShapeNest.MatchEffect";
    public const string VfxId = "ShapeNest.Vfx";
    public const string TravelerId = "ShapeNest.Traveler";
    public const string ShuffleId = "ShapeNest.Shuffle";
    public const string MagnetSelectionId = "ShapeNest.MagnetSelection";
    public const string BoosterFeedbackId = "ShapeNest.BoosterFeedback";
    public const string NestedExtractionId = "ShapeNest.NestedExtraction";

    public static Ease Linear => Ease.Linear;

    public static float EvaluateSmoothStep(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

    public static float EvaluateEaseOutQuad(float t) => PieceMotionMath.EaseOutQuad(Mathf.Clamp01(t));

    public static float EvaluateEaseOutCubic(float t) => PieceMotionMath.EaseOutCubic(t);

    public static float EvaluateEaseOutSine(float t) => PieceMotionMath.EaseOutSine(t);

    public static float EvaluateEaseInOutSine(float t) => PieceMotionMath.EaseInOutSine(t);

    public static float EvaluateHopCruise(float t, float linearWeight) =>
        PieceMotionMath.EaseHopCruise(t, linearWeight);

    public static float EvaluateEaseInOutCubic(float t) => PieceMotionMath.EaseInOutCubic(t);

    public static float EvaluateEaseInOutQuint(float t) => PieceMotionMath.EaseInOutQuint(t);

    public static float EvaluateEaseInQuad(float t) => PieceMotionMath.EaseInQuad(Mathf.Clamp01(t));

    /// <summary>True when any active tween uses the given string id.</summary>
    public static bool IsTweeningId(string id) =>
        !string.IsNullOrEmpty(id) && DOTween.IsTweening(id);

    public static float EvaluateEaseInCubic(float t) => PieceMotionMath.EaseInCubic(t);

    /// <summary>Yield until a tween finishes. Safe if tween is null or already complete.</summary>
    public static IEnumerator Wait(Tween tween)
    {
        if (tween == null || !tween.IsActive())
        {
            yield break;
        }

        yield return tween.WaitForCompletion();
    }

    public static IEnumerator WaitInterval(float seconds, bool unscaled = false)
    {
        if (seconds <= 0f)
        {
            yield break;
        }

        Tween delay = DOVirtual.DelayedCall(seconds, null, ignoreTimeScale: unscaled);
        yield return Wait(delay);
    }

    public static void KillById(object target, string id, bool complete = false)
    {
        if (target == null || string.IsNullOrEmpty(id))
        {
            return;
        }

        DOTween.Kill(target, id, complete);
    }

    public static void KillTransform(Transform target, bool complete = false)
    {
        if (target == null)
        {
            return;
        }

        target.DOKill(complete);
    }

    public static Tween TweenFloat(
        float from,
        float to,
        float duration,
        TweenCallback<float> onUpdate,
        Ease ease = Ease.Linear,
        bool unscaled = false)
    {
        return DOTween.To(() => from, v =>
            {
                from = v;
                onUpdate?.Invoke(v);
            }, to, Mathf.Max(0f, duration))
            .SetEase(ease)
            .SetUpdate(unscaled);
    }

    /// <summary>
    /// 0→1 progress tween with Linear ease; caller applies custom easing inside onUpdate.
    /// </summary>
    public static Tween Progress(
        float duration,
        TweenCallback<float> onUpdate,
        bool unscaled = false)
    {
        float t = 0f;
        return DOTween.To(() => t, v =>
            {
                t = v;
                onUpdate?.Invoke(v);
            }, 1f, Mathf.Max(0.0001f, duration))
            .SetEase(Ease.Linear)
            .SetUpdate(unscaled);
    }
}
