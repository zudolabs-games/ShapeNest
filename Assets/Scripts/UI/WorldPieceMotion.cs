using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// World3D <see cref="IPieceMotion"/> driven by DOTween.
/// Gameplay sequencing stays in <see cref="BlockMover"/>; this only animates presentation.
/// </summary>
[DisallowMultipleComponent]
public class WorldPieceMotion : MonoBehaviour, IPieceMotion
{
    private PieceView3D pieceView;
    private IGridSpace worldGridSpace;
    private BoardPresenter3D presenter;
    private Sequence activeSequence;
    private Vector2Int lastHopDir;

    public void Bind(PieceView3D view, IGridSpace space)
    {
        KillActive(false);
        if (pieceView != view)
        {
            lastHopDir = Vector2Int.zero;
        }

        pieceView = view;
        worldGridSpace = space;
        if (space is GridSpace3D)
        {
            presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        }
    }

    public bool IsBound => pieceView != null && worldGridSpace != null;

    private void OnDisable()
    {
        lastHopDir = Vector2Int.zero;
        KillActive(false);
    }

    private void OnDestroy()
    {
        lastHopDir = Vector2Int.zero;
        KillActive(false);
    }

    public void SnapToLocal(Vector2 localPosition)
    {
        // UI-local pixels are not valid world positions. Prefer SnapToGrid.
    }

    public void SnapToGrid(IGridSpace gridSpace, Vector2Int cell)
    {
        IGridSpace space = ResolveSpace(gridSpace);
        if (pieceView == null || space == null)
        {
            return;
        }

        pieceView.ApplyGridPosition(space, cell);
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
        IGridSpace space = ResolveSpace(gridSpace);
        if (pieceView == null || space == null)
        {
            yield break;
        }

        KillActive(false);
        pieceView.BeginMotionLock();
        Vector3 end = CellWorldPosition(space, to);
        Vector3 start = ResolveHopStart(space, from, visualCellSize);
        float cellAxis = Mathf.Max(visualCellSize.x, visualCellSize.y, 0.01f);
        Vector2Int hopDir = to - from;
        bool continuation = !anticipate && lastHopDir != Vector2Int.zero;
        bool turn = continuation && lastHopDir != hopDir;
        float linearWeight = continuation ? 0.9f : 0.62f;
        float microFloat = cellAxis * Mathf.Clamp(hopLiftPercent * 0.32f, 0.008f, 0.016f);

        Sequence sequence = DOTween.Sequence().SetId(TweenAnimationUtility.PieceMotionId).SetLink(pieceView.gameObject);
        activeSequence = sequence;

        try
        {
            // Wind-up and hop squash stay unused so chained hops keep speed and
            // drag-selection scale is not overwritten at hop end.
            AppendHopTravel(sequence, start, end, duration, microFloat, linearWeight, turn);
            sequence.OnComplete(() =>
            {
                if (pieceView != null && IsFiniteVector(end))
                {
                    pieceView.transform.position = end;
                }
            });

            yield return TweenAnimationUtility.Wait(sequence);
            lastHopDir = hopDir;
        }
        finally
        {
            if (pieceView != null)
            {
                pieceView.EndMotionLock();
            }

            if (activeSequence == sequence)
            {
                activeSequence = null;
            }
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
        if (pieceView == null)
        {
            yield break;
        }

        lastHopDir = Vector2Int.zero;
        pieceView.BeginMotionLock();
        Vector3 start = pieceView.transform.position;
        float cellAxis = Mathf.Max(visualCellSize.x, visualCellSize.y, 0.01f);
        Vector3 lifted = start + (Vector3.up * (cellAxis * liftPercent));
        Vector3 scaleFrom = ResolveScale(restScale);
        Vector3 pumped = scaleFrom * anticipateScale;

        KillActive(false);
        Sequence sequence = DOTween.Sequence().SetId(TweenAnimationUtility.PieceMotionId).SetLink(pieceView.gameObject);
        activeSequence = sequence;

        try
        {
            AppendWorldMoveScale(sequence, start, lifted, duration, scaleFrom, pumped, easeOut: false);
            yield return TweenAnimationUtility.Wait(sequence);
        }
        finally
        {
            if (pieceView != null)
            {
                pieceView.EndMotionLock();
            }

            if (activeSequence == sequence)
            {
                activeSequence = null;
            }
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
        IGridSpace space = ResolveSpace(gridSpace);
        if (pieceView == null || space == null)
        {
            yield break;
        }

        lastHopDir = Vector2Int.zero;
        pieceView.BeginMotionLock();
        Vector3 scaleRest = ResolveScale(restScale);
        Vector3 start = pieceView.transform.position;
        Vector3 end = CellWorldPositionForNestEntry(space, to);
        float cellAxis = Mathf.Max(visualCellSize.x, visualCellSize.y, 0.01f);
        float liftAmount = cellAxis * liftPercent;
        Vector3 control = ((CellWorldPosition(space, from) + end) * 0.5f) + (Vector3.up * liftAmount);
        Vector3 travelScale = scaleRest * hopScale;

        KillActive(false);
        Sequence sequence = DOTween.Sequence().SetId(TweenAnimationUtility.PieceMotionId).SetLink(pieceView.gameObject);
        activeSequence = sequence;

        try
        {
            float duration = Mathf.Max(0.01f, arcDuration);
            sequence.Append(TweenAnimationUtility.Progress(duration, t =>
            {
                float eased = TweenAnimationUtility.EvaluateEaseInOutCubic(t);
                pieceView.transform.position = QuadraticBezier3(start, control, end, eased);
                float squashT = Mathf.Sin(t * Mathf.PI);
                pieceView.LocalScale = Vector3.LerpUnclamped(scaleRest, travelScale, squashT);
            }));

            // Sit must start from the arc's actual end scale (Sin(π) → scaleRest), not travelScale.
            AppendWorldMoveScale(
                sequence,
                end,
                end,
                sitDuration,
                scaleRest,
                scaleRest,
                easeOut: true);

            sequence.OnComplete(() =>
            {
                if (pieceView != null)
                {
                    pieceView.transform.position = end;
                    pieceView.LocalScale = scaleRest;
                }
            });

            yield return TweenAnimationUtility.Wait(sequence);
        }
        finally
        {
            if (pieceView != null)
            {
                pieceView.EndMotionLock();
            }

            if (activeSequence == sequence)
            {
                activeSequence = null;
            }
        }

        // Nest match VFX is owned by MatchEffect (one burst/ring per successful match presentation).
    }

    private void AppendHopTravel(
        Sequence sequence,
        Vector3 from,
        Vector3 to,
        float duration,
        float liftAmount,
        float linearWeight,
        bool softenTurn)
    {
        if (duration <= 0f)
        {
            sequence.AppendCallback(() =>
            {
                if (pieceView != null && IsFiniteVector(to))
                {
                    pieceView.transform.position = to;
                }
            });
            return;
        }

        sequence.Append(TweenAnimationUtility.Progress(duration, t =>
        {
            if (pieceView == null)
            {
                return;
            }

            if (float.IsNaN(t) || float.IsInfinity(t))
            {
                t = 1f;
            }

            t = Mathf.Clamp01(t);
            float eased = softenTurn
                ? TweenAnimationUtility.EvaluateEaseInOutSine(t)
                : TweenAnimationUtility.EvaluateHopCruise(t, linearWeight);
            if (float.IsNaN(eased) || float.IsInfinity(eased))
            {
                eased = t;
            }

            eased = Mathf.Clamp01(eased);
            Vector3 pos = Vector3.LerpUnclamped(from, to, eased);
            if (liftAmount > 0.0005f && !float.IsNaN(liftAmount) && !float.IsInfinity(liftAmount))
            {
                pos.y += PieceMotionMath.MicroFloatEnvelope(t) * liftAmount;
            }

            if (!IsFiniteVector(pos))
            {
                pos = IsFiniteVector(to) ? to : (IsFiniteVector(from) ? from : pieceView.transform.position);
            }

            if (IsFiniteVector(pos))
            {
                pieceView.transform.position = pos;
            }
        }));
    }

    private void AppendWorldMoveScale(
        Sequence sequence,
        Vector3 from,
        Vector3 to,
        float duration,
        Vector3 scaleFrom,
        Vector3 scaleTo,
        bool easeOut)
    {
        if (duration <= 0f)
        {
            sequence.AppendCallback(() =>
            {
                if (pieceView != null)
                {
                    pieceView.transform.position = to;
                    pieceView.LocalScale = scaleTo;
                }
            });
            return;
        }

        sequence.Append(TweenAnimationUtility.Progress(duration, t =>
        {
            float eased = easeOut
                ? TweenAnimationUtility.EvaluateEaseOutQuad(t)
                : TweenAnimationUtility.EvaluateSmoothStep(t);
            pieceView.transform.position = Vector3.LerpUnclamped(from, to, eased);
            pieceView.LocalScale = Vector3.LerpUnclamped(scaleFrom, scaleTo, eased);
        }));
    }

    private void KillActive(bool complete)
    {
        if (activeSequence != null && activeSequence.IsActive())
        {
            activeSequence.Kill(complete);
        }

        activeSequence = null;
        if (pieceView != null)
        {
            TweenAnimationUtility.KillById(pieceView.transform, TweenAnimationUtility.PieceMotionId, complete);
            TweenAnimationUtility.KillTransform(pieceView.transform, complete);
        }
    }

    private IGridSpace ResolveSpace(IGridSpace preferred)
    {
        if (worldGridSpace != null)
        {
            return worldGridSpace;
        }

        if (preferred is GridSpace3D)
        {
            return preferred;
        }

        if (presenter == null)
        {
            presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        }

        return presenter != null ? presenter.GridSpace : preferred;
    }

    private Vector3 ResolveHopStart(IGridSpace space, Vector2Int from, Vector2 visualCellSize)
    {
        Vector3 gridFrom = CellWorldPosition(space, from);
        if (pieceView == null)
        {
            return gridFrom;
        }

        Vector3 current = pieceView.transform.position;
        float cellAxis = Mathf.Max(visualCellSize.x, visualCellSize.y, 0.01f);
        float dx = current.x - gridFrom.x;
        float dz = current.z - gridFrom.z;
        float maxDist = cellAxis * 0.5f;
        if ((dx * dx) + (dz * dz) > maxDist * maxDist)
        {
            return gridFrom;
        }

        return current;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private Vector3 CellWorldPosition(IGridSpace space, Vector2Int cell)
    {
        Vector3 world = space.GridToWorld(cell);
        float halfHeight = pieceView != null ? Mathf.Abs(pieceView.transform.lossyScale.y) * 0.5f : 0.11f;
        float lift = pieceView != null ? pieceView.SurfaceLift : 0.02f;
        world.y += lift + halfHeight;
        return world;
    }

    /// <summary>
    /// Nest-entry destination: same GridToWorld XZ as <see cref="CellWorldPosition"/>,
    /// Y seated on the visible nest top (block bottom = nest bounds.max.y).
    /// Empty-cell hops keep <see cref="CellWorldPosition"/>.
    /// </summary>
    private Vector3 CellWorldPositionForNestEntry(IGridSpace space, Vector2Int cell)
    {
        Vector3 world = CellWorldPosition(space, cell);
        MeshRenderer nestRenderer = FindNestRendererAtCell(space, cell);
        if (nestRenderer == null)
        {
            return world;
        }

        float halfHeight = pieceView != null
            ? Mathf.Abs(pieceView.transform.lossyScale.y) * 0.5f
            : 0.11f;
        world.y = nestRenderer.bounds.max.y + halfHeight;
        return world;
    }

    private MeshRenderer FindNestRendererAtCell(IGridSpace space, Vector2Int cell)
    {
        if (presenter == null)
        {
            presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        }

        if (presenter == null || presenter.NestsRoot == null || space == null)
        {
            return null;
        }

        Vector3 cellWorld = space.GridToWorld(cell);
        float cellSize = presenter.CellWorldSize;
        float maxDistSq = cellSize * cellSize * 0.36f;
        Transform nestsRoot = presenter.NestsRoot;
        MeshRenderer best = null;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < nestsRoot.childCount; i++)
        {
            Transform child = nestsRoot.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy)
            {
                continue;
            }

            PieceView3D view = child.GetComponent<PieceView3D>();
            if (view == null || !view.ConfiguredAsNest)
            {
                continue;
            }

            MeshRenderer renderer = view.OuterMeshRenderer;
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Vector3 center = renderer.bounds.center;
            float dx = center.x - cellWorld.x;
            float dz = center.z - cellWorld.z;
            float distSq = (dx * dx) + (dz * dz);
            if (distSq <= maxDistSq && distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = renderer;
            }
        }

        return best;
    }

    private Vector3 ResolveScale(Vector3 requested)
    {
        if (pieceView == null)
        {
            return requested;
        }

        if (requested.x > 0.5f && requested.x < 1.5f && pieceView.ConfiguredFootprintScale.x > 1.5f)
        {
            return pieceView.ConfiguredFootprintScale;
        }

        if (requested.sqrMagnitude < 0.0001f)
        {
            return pieceView.ConfiguredFootprintScale;
        }

        return requested;
    }

    private static Vector3 QuadraticBezier3(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return (u * u * p0) + (2f * u * t * p1) + (t * t * p2);
    }
}
