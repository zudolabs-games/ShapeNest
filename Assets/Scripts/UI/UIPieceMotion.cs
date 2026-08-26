using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// uGUI <see cref="IPieceMotion"/> driven by DOTween.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class UIPieceMotion : MonoBehaviour, IPieceMotion
{
    private UIPieceView pieceView;
    private RectTransform cachedRect;
    private Sequence activeSequence;

    private UIPieceView View
    {
        get
        {
            if (pieceView == null)
            {
                pieceView = GetComponent<UIPieceView>();
                if (pieceView == null)
                {
                    pieceView = gameObject.AddComponent<UIPieceView>();
                }
            }

            return pieceView;
        }
    }

    private RectTransform Rect
    {
        get
        {
            if (cachedRect == null)
            {
                cachedRect = View.RectTransform;
            }

            return cachedRect;
        }
    }

    private void OnDisable()
    {
        KillActive(false);
    }

    private void OnDestroy()
    {
        KillActive(false);
    }

    public void SnapToLocal(Vector2 localPosition)
    {
        Rect.anchoredPosition = localPosition;
    }

    public void SnapToGrid(IGridSpace gridSpace, Vector2Int cell)
    {
        if (gridSpace == null)
        {
            return;
        }

        Vector3 local = gridSpace.GridToLocal(cell);
        SnapToLocal(new Vector2(local.x, local.y));
    }

    public IEnumerator AnimateHop(
        IGridSpace gridSpace,
        Vector2 visualCellSize,
        Vector2Int from,
        Vector2Int to,
        float duration,
        bool anticipate,
        Vector2Int anticipateDirection,
        float anticipateDuration,
        float anticipatePercent,
        float hopTravelScale,
        float hopLiftPercent)
    {
        if (gridSpace == null)
        {
            yield break;
        }

        Vector2 startPosition = gridSpace.GridToLocal(from);
        Vector2 endPosition = gridSpace.GridToLocal(to);
        Vector3 scaleAtStart = View.LocalScale;

        KillActive(false);
        Sequence sequence = DOTween.Sequence().SetId(TweenAnimationUtility.PieceMotionId).SetLink(gameObject);
        activeSequence = sequence;

        if (anticipate && anticipateDuration > 0f && anticipatePercent > 0f)
        {
            float axisSize = anticipateDirection.x != 0 ? visualCellSize.x : visualCellSize.y;
            Vector2 windup = startPosition - ((Vector2)anticipateDirection * (axisSize * anticipatePercent));
            Vector2 windFrom = startPosition;
            sequence.Append(TweenAnimationUtility.Progress(anticipateDuration, t =>
            {
                float eased = TweenAnimationUtility.EvaluateSmoothStep(t);
                SnapToLocal(Vector2.LerpUnclamped(windFrom, windup, eased));
            }));
            startPosition = windup;
        }

        AppendHopTravel(
            sequence,
            startPosition,
            endPosition,
            duration,
            scaleAtStart,
            hopTravelScale,
            visualCellSize.y * hopLiftPercent);

        sequence.OnComplete(() =>
        {
            SnapToLocal(endPosition);
            View.LocalScale = scaleAtStart;
        });

        yield return TweenAnimationUtility.Wait(sequence);
        if (activeSequence == sequence)
        {
            activeSequence = null;
        }
    }

    public IEnumerator AnimateNestAnticipate(
        Vector2 visualCellSize,
        Vector2 restPosition,
        Vector3 restScale,
        float duration,
        float liftPercent,
        float anticipateScale)
    {
        float liftAmount = visualCellSize.y * liftPercent;
        Vector2 lifted = restPosition + new Vector2(0f, liftAmount);
        Vector3 pumped = restScale * anticipateScale;

        KillActive(false);
        Sequence sequence = DOTween.Sequence().SetId(TweenAnimationUtility.PieceMotionId).SetLink(gameObject);
        activeSequence = sequence;
        AppendAnchoredMoveScale(sequence, restPosition, lifted, duration, restScale, pumped, easeOut: false);
        yield return TweenAnimationUtility.Wait(sequence);
        if (activeSequence == sequence)
        {
            activeSequence = null;
        }
    }

    public IEnumerator AnimateNestEntry(
        IGridSpace gridSpace,
        Vector2 visualCellSize,
        Vector2Int from,
        Vector2Int to,
        Vector3 restScale,
        float liftPercent,
        float arcDuration,
        float sitDuration,
        float hopScale)
    {
        if (gridSpace == null)
        {
            yield break;
        }

        Vector2 start = Rect.anchoredPosition;
        Vector2 end = gridSpace.GridToLocal(to);
        Vector2 restPosition = gridSpace.GridToLocal(from);
        float liftAmount = visualCellSize.y * liftPercent;
        Vector2 control = ((restPosition + end) * 0.5f) + new Vector2(0f, liftAmount);
        Vector3 travelScale = restScale * hopScale;

        KillActive(false);
        Sequence sequence = DOTween.Sequence().SetId(TweenAnimationUtility.PieceMotionId).SetLink(gameObject);
        activeSequence = sequence;

        float duration = Mathf.Max(0.01f, arcDuration);
        sequence.Append(TweenAnimationUtility.Progress(duration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseInOutCubic(t);
            SnapToLocal(PieceMotionMath.QuadraticBezier(start, control, end, eased));
            float squashT = Mathf.Sin(t * Mathf.PI);
            View.LocalScale = Vector3.LerpUnclamped(restScale, travelScale, squashT);
        }));

        AppendAnchoredMoveScale(sequence, end, end, sitDuration, restScale, restScale, easeOut: true);
        sequence.OnComplete(() =>
        {
            SnapToLocal(end);
            View.LocalScale = restScale;
        });

        yield return TweenAnimationUtility.Wait(sequence);
        if (activeSequence == sequence)
        {
            activeSequence = null;
        }
    }

    private void AppendHopTravel(
        Sequence sequence,
        Vector2 from,
        Vector2 to,
        float duration,
        Vector3 scaleAtStart,
        float hopTravelScale,
        float liftAmount)
    {
        if (duration <= 0f)
        {
            sequence.AppendCallback(() => SnapToLocal(to));
            return;
        }

        Vector3 squash = scaleAtStart * hopTravelScale;
        bool squashHop = hopTravelScale < 0.999f;
        Vector2 control = ((from + to) * 0.5f) + new Vector2(0f, liftAmount);
        sequence.Append(TweenAnimationUtility.Progress(duration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseInOutCubic(t);
            SnapToLocal(
                liftAmount > 0.001f
                    ? PieceMotionMath.QuadraticBezier(from, control, to, eased)
                    : Vector2.LerpUnclamped(from, to, eased));
            if (squashHop)
            {
                float squashT = Mathf.Sin(t * Mathf.PI);
                squashT *= squashT;
                View.LocalScale = Vector3.LerpUnclamped(scaleAtStart, squash, squashT);
            }
        }));
    }

    private void AppendAnchoredMoveScale(
        Sequence sequence,
        Vector2 from,
        Vector2 to,
        float duration,
        Vector3 scaleFrom,
        Vector3 scaleTo,
        bool easeOut)
    {
        if (duration <= 0f)
        {
            sequence.AppendCallback(() =>
            {
                SnapToLocal(to);
                View.LocalScale = scaleTo;
            });
            return;
        }

        sequence.Append(TweenAnimationUtility.Progress(duration, t =>
        {
            float eased = easeOut
                ? TweenAnimationUtility.EvaluateEaseOutQuad(t)
                : TweenAnimationUtility.EvaluateSmoothStep(t);
            SnapToLocal(Vector2.LerpUnclamped(from, to, eased));
            View.LocalScale = Vector3.LerpUnclamped(scaleFrom, scaleTo, eased);
        }));
    }

    private void KillActive(bool complete)
    {
        if (activeSequence != null && activeSequence.IsActive())
        {
            activeSequence.Kill(complete);
        }

        activeSequence = null;
        TweenAnimationUtility.KillTransform(transform, complete);
    }
}
