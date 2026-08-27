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
    private const float PickupDuration = 0.10f;
    private const float SettleDuration = 0.12f;
    private const float CarryScaleBoost = 0.07f;
    private const float NestJumpFlightScale = 1.22f;
    private const float NestAnticipateScaleMin = 1.12f;

    private PieceView3D pieceView;
    private IGridSpace worldGridSpace;
    private BoardPresenter3D presenter;
    private Sequence activeSequence;
    private Tween carryTween;
    private BlockMover cachedMover;
    private Vector2Int lastHopDir;
    private bool carryActive;
    private bool carryHasSettled;
    private float carryLiftTarget;

    public void Bind(PieceView3D view, IGridSpace space)
    {
        KillActive(false);
        if (pieceView != view)
        {
            lastHopDir = Vector2Int.zero;
            TransferCarryToView(view);
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
        ReleaseCarryImmediate();
        KillActive(false);
    }

    private void OnDestroy()
    {
        lastHopDir = Vector2Int.zero;
        ReleaseCarryImmediate();
        KillActive(false);
    }

    /// <summary>
    /// Pickup starts when BlockMover reports a real drag (after TryBeginDrag).
    /// Settle starts when that drag/move is no longer active. Not a per-frame lerp.
    /// </summary>
    private void LateUpdate()
    {
        if (pieceView == null || !pieceView.isActiveAndEnabled)
        {
            if (carryActive)
            {
                KillCarry(false);
                carryActive = false;
            }

            return;
        }

        if (IsGameplayDragging())
        {
            if (!carryHasSettled && !pieceView.IsMotionLocked)
            {
                EnsureCarry(ResolveCellAxis());
            }

            return;
        }

        carryHasSettled = false;
        if (pieceView.IsMotionLocked)
        {
            return;
        }

        if (carryActive || pieceView.PresentationLift > 0.0005f)
        {
            BeginSettle(writeTransform: true);
        }
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
        if (carryActive || pieceView.PresentationLift > 0.0005f)
        {
            BeginSettle(writeTransform: true);
        }
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
        float cellAxis = Mathf.Max(visualCellSize.x, visualCellSize.y, 0.01f);
        yield return PlayPickupBeatIfNeeded(cellAxis);
        if (pieceView == null)
        {
            yield break;
        }

        pieceView.BeginMotionLock();
        EnsureCarry(cellAxis);
        Vector3 endRest = CellRestWorldPosition(space, to);
        Vector3 start = ResolveHopStart(space, from, visualCellSize);
        float fromRestY = CellRestWorldPosition(space, from).y;
        float toRestY = endRest.y;
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
            // Carry height is a live offset so pickup is not restarted per hop.
            AppendHopTravel(sequence, start, endRest, fromRestY, toRestY, duration, microFloat, linearWeight, turn);
            sequence.OnComplete(() =>
            {
                if (pieceView == null)
                {
                    return;
                }

                Vector3 seated = endRest;
                seated.y = toRestY + pieceView.PresentationLift;
                if (IsFiniteVector(seated))
                {
                    pieceView.transform.position = seated;
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
        yield return SettleCarryRoutine(writeTransform: true);
        if (pieceView == null)
        {
            yield break;
        }

        Vector3 start = pieceView.transform.position;
        float pieceHeight = ResolvePieceHeight();
        float anticipateLift = PieceMotionMath.NestAnticipateLiftAmount(pieceHeight);
        Vector3 lifted = start + (Vector3.up * anticipateLift);
        Vector3 scaleFrom = ResolveScale(restScale);
        float pumpMul = Mathf.Max(anticipateScale, NestAnticipateScaleMin);
        Vector3 pumped = scaleFrom * pumpMul;
        if (!IsFiniteVector(lifted))
        {
            lifted = start;
        }

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
        yield return SettleCarryRoutine(writeTransform: true);
        if (pieceView == null || space == null)
        {
            yield break;
        }

        Vector3 scaleRest = ResolveScale(restScale);
        Vector3 start = pieceView.transform.position;
        Vector3 end = CellWorldPositionForNestEntry(space, to);
        float pieceHeight = ResolvePieceHeight();
        float peakAboveRest = PieceMotionMath.NestJumpPeakHeight(pieceHeight);
        Vector3 control = NestJumpControl(start, end, peakAboveRest);
        float flightMul = hopScale > 1f ? hopScale : NestJumpFlightScale;
        Vector3 travelScale = scaleRest * flightMul;

        KillActive(false);
        Sequence sequence = DOTween.Sequence().SetId(TweenAnimationUtility.PieceMotionId).SetLink(pieceView.gameObject);
        activeSequence = sequence;

        try
        {
            float duration = Mathf.Max(0.01f, arcDuration);
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
                float eased = TweenAnimationUtility.EvaluateEaseInOutCubic(t);
                if (float.IsNaN(eased) || float.IsInfinity(eased))
                {
                    eased = t;
                }

                Vector3 pos = QuadraticBezier3(start, control, end, eased);
                if (IsFiniteVector(pos))
                {
                    pieceView.transform.position = pos;
                }

                float squashT = Mathf.Sin(t * Mathf.PI);
                Vector3 scale = Vector3.LerpUnclamped(scaleRest, travelScale, squashT);
                if (IsFiniteVector(scale))
                {
                    pieceView.LocalScale = scale;
                }
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
                if (pieceView == null)
                {
                    return;
                }

                if (IsFiniteVector(end))
                {
                    pieceView.transform.position = end;
                }

                if (IsFiniteVector(scaleRest))
                {
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
        float fromRestY,
        float toRestY,
        float duration,
        float liftAmount,
        float linearWeight,
        bool softenTurn)
    {
        if (duration <= 0f)
        {
            sequence.AppendCallback(() =>
            {
                if (pieceView == null)
                {
                    return;
                }

                Vector3 seated = to;
                seated.y = toRestY + pieceView.PresentationLift;
                if (IsFiniteVector(seated))
                {
                    pieceView.transform.position = seated;
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
            float restY = Mathf.LerpUnclamped(fromRestY, toRestY, eased);
            pos.y = restY + pieceView.PresentationLift;
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
                    if (IsFiniteVector(to))
                    {
                        pieceView.transform.position = to;
                    }

                    if (IsFiniteVector(scaleTo))
                    {
                        pieceView.LocalScale = scaleTo;
                    }
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
            float eased = easeOut
                ? TweenAnimationUtility.EvaluateEaseOutQuad(t)
                : TweenAnimationUtility.EvaluateSmoothStep(t);
            if (float.IsNaN(eased) || float.IsInfinity(eased))
            {
                eased = t;
            }

            Vector3 pos = Vector3.LerpUnclamped(from, to, eased);
            Vector3 scale = Vector3.LerpUnclamped(scaleFrom, scaleTo, eased);
            if (IsFiniteVector(pos))
            {
                pieceView.transform.position = pos;
            }

            if (IsFiniteVector(scale))
            {
                pieceView.LocalScale = scale;
            }
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
            // Do not DOKill the whole transform: carry tweens must survive hop sequence swaps.
            TweenAnimationUtility.KillById(pieceView.transform, TweenAnimationUtility.PieceMotionId, complete);
        }
    }

    private void TransferCarryToView(PieceView3D nextView)
    {
        float oldLift = 0f;
        if (pieceView != null)
        {
            oldLift = pieceView.PresentationLift;
            pieceView.ClearCarryPresentation(applyToTransform: true);
        }

        KillCarry(false);
        if (nextView == null)
        {
            carryActive = false;
            carryHasSettled = false;
            carryLiftTarget = 0f;
            return;
        }

        // Travel/rebind views already sit at the followed world pose; keep the
        // scalar so nest settle can lower them without a second lift.
        if (oldLift > 0.0005f)
        {
            nextView.SetPresentationLift(oldLift, CarryVisualScale(oldLift));
            carryActive = true;
        }
        else
        {
            carryActive = false;
            carryHasSettled = false;
            carryLiftTarget = 0f;
        }
    }

    private void EnsureCarry(float cellAxis)
    {
        if (pieceView == null || carryActive || carryHasSettled)
        {
            return;
        }

        if (!IsGameplayDragging())
        {
            return;
        }

        BeginPickup(cellAxis, !pieceView.IsMotionLocked);
    }

    private IEnumerator PlayPickupBeatIfNeeded(float cellAxis)
    {
        if (pieceView == null || carryHasSettled)
        {
            yield break;
        }

        if (!carryActive && IsGameplayDragging())
        {
            BeginPickup(cellAxis, writeTransform: true);
        }

        if (carryTween != null && carryTween.IsActive())
        {
            yield return TweenAnimationUtility.Wait(carryTween);
        }
    }

    private void BeginPickup(float cellAxis, bool writeTransform)
    {
        if (pieceView == null)
        {
            return;
        }

        carryActive = true;
        carryHasSettled = false;
        float pieceHeight = Mathf.Max(pieceView.PieceHeight, pieceView.ConfiguredFootprintScale.y);
        carryLiftTarget = PieceMotionMath.CarryLiftAmount(cellAxis, pieceHeight);
        AnimateCarryLift(pieceView.PresentationLift, carryLiftTarget, PickupDuration, writeTransform);
    }

    private void BeginSettle(bool writeTransform)
    {
        if (pieceView == null)
        {
            return;
        }

        if (!carryActive && pieceView.PresentationLift <= 0.0005f)
        {
            return;
        }

        if (carryTween != null && carryTween.IsActive() && !carryActive)
        {
            return;
        }

        carryActive = false;
        carryHasSettled = true;
        AnimateCarryLift(pieceView.PresentationLift, 0f, SettleDuration, writeTransform);
    }

    private IEnumerator SettleCarryRoutine(bool writeTransform)
    {
        if (pieceView == null)
        {
            yield break;
        }

        if (carryActive || pieceView.PresentationLift > 0.0005f)
        {
            BeginSettle(writeTransform);
        }

        if (carryTween != null && carryTween.IsActive())
        {
            yield return TweenAnimationUtility.Wait(carryTween);
        }
    }

    private void AnimateCarryLift(float fromLift, float toLift, float duration, bool writeTransform)
    {
        if (pieceView == null)
        {
            return;
        }

        if (!PieceMotionMath.IsFinite(fromLift))
        {
            fromLift = 0f;
        }

        if (!PieceMotionMath.IsFinite(toLift))
        {
            toLift = 0f;
        }

        fromLift = Mathf.Max(0f, fromLift);
        toLift = Mathf.Max(0f, toLift);
        KillCarry(false);

        if (duration <= 0.0001f || Mathf.Abs(toLift - fromLift) < 0.0002f)
        {
            ApplyCarryLift(fromLift, toLift, writeTransform);
            return;
        }

        float appliedLift = fromLift;
        Tween tween = TweenAnimationUtility.Progress(duration, t =>
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
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
            if (float.IsNaN(eased) || float.IsInfinity(eased))
            {
                eased = t;
            }

            float lift = Mathf.LerpUnclamped(fromLift, toLift, eased);
            ApplyCarryLift(appliedLift, lift, writeTransform);
            appliedLift = lift;
        })
            .SetId(TweenAnimationUtility.CarryId)
            .SetLink(pieceView.gameObject)
            .OnComplete(() =>
            {
                if (pieceView != null)
                {
                    ApplyCarryLift(appliedLift, toLift, writeTransform);
                }
            });

        carryTween = tween;
    }

    private void ApplyCarryLift(float previousLift, float lift, bool writeTransform)
    {
        if (pieceView == null)
        {
            return;
        }

        if (!PieceMotionMath.IsFinite(lift))
        {
            lift = 0f;
        }

        lift = Mathf.Max(0f, lift);
        pieceView.SetPresentationLift(lift, CarryVisualScale(lift));
        if (!writeTransform)
        {
            return;
        }

        float delta = lift - previousLift;
        if (!PieceMotionMath.IsFinite(delta) || Mathf.Abs(delta) < 0.00001f)
        {
            return;
        }

        Vector3 world = pieceView.transform.position;
        world.y += delta;
        if (IsFiniteVector(world))
        {
            pieceView.transform.position = world;
        }
    }

    private float CarryVisualScale(float lift)
    {
        float target = carryLiftTarget > 0.0001f ? carryLiftTarget : Mathf.Max(lift, 0.0001f);
        float blend = Mathf.Clamp01(lift / target);
        if (!PieceMotionMath.IsFinite(blend))
        {
            blend = 0f;
        }

        return 1f + (CarryScaleBoost * blend);
    }

    private float ResolvePieceHeight()
    {
        if (pieceView == null)
        {
            return 0.22f;
        }

        float height = Mathf.Max(pieceView.PieceHeight, pieceView.ConfiguredFootprintScale.y);
        if (!PieceMotionMath.IsFinite(height) || height < 0.01f)
        {
            return 0.22f;
        }

        return height;
    }

    /// <summary>
    /// Quadratic control point: same XZ midpoint as before, Y set so the arc peak
    /// is <paramref name="peakAboveRest"/> above the lower of start/end seating.
    /// controlY = 2*peak - 0.5*startY - 0.5*endY.
    /// </summary>
    private static Vector3 NestJumpControl(Vector3 start, Vector3 end, float peakAboveRest)
    {
        Vector3 control = (start + end) * 0.5f;
        if (!PieceMotionMath.IsFinite(peakAboveRest) || peakAboveRest < 0.01f)
        {
            peakAboveRest = 0.22f;
        }

        float restY = Mathf.Min(start.y, end.y);
        float desiredPeakY = restY + peakAboveRest;
        float controlY = (2f * desiredPeakY) - (0.5f * start.y) - (0.5f * end.y);
        if (!PieceMotionMath.IsFinite(controlY))
        {
            controlY = desiredPeakY;
        }

        control.y = Mathf.Max(controlY, desiredPeakY);
        if (!IsFiniteVector(control))
        {
            control = (start + end) * 0.5f;
            control.y = desiredPeakY;
        }

        return control;
    }

    private void ReleaseCarryImmediate()
    {
        KillCarry(false);
        carryActive = false;
        carryHasSettled = false;
        carryLiftTarget = 0f;
        if (pieceView != null)
        {
            pieceView.ClearCarryPresentation(applyToTransform: true);
        }
    }

    private void KillCarry(bool complete)
    {
        if (carryTween != null && carryTween.IsActive())
        {
            carryTween.Kill(complete);
        }

        carryTween = null;
        if (pieceView != null)
        {
            TweenAnimationUtility.KillById(pieceView.transform, TweenAnimationUtility.CarryId, complete);
        }
    }

    private bool IsGameplayDragging()
    {
        if (cachedMover == null)
        {
            cachedMover = GetComponent<BlockMover>();
        }

        return cachedMover != null && cachedMover.IsDragging;
    }

    private float ResolveCellAxis()
    {
        if (worldGridSpace is GridSpace3D grid3D)
        {
            float axis = Mathf.Max(grid3D.CellSize.x, grid3D.CellSize.y);
            return PieceMotionMath.IsFinite(axis) ? Mathf.Max(axis, 0.01f) : 1f;
        }

        if (presenter == null)
        {
            presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        }

        if (presenter != null)
        {
            float size = presenter.CellWorldSize;
            return PieceMotionMath.IsFinite(size) ? Mathf.Max(size, 0.01f) : 1f;
        }

        return 1f;
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
        if (!PieceMotionMath.IsFinite(halfHeight))
        {
            halfHeight = 0.11f;
        }

        if (!PieceMotionMath.IsFinite(lift))
        {
            lift = 0.02f;
        }

        world.y += lift + halfHeight;
        if (!IsFiniteVector(world))
        {
            world = space.GridToWorld(cell);
            world.y += 0.13f;
        }

        return world;
    }

    private Vector3 CellRestWorldPosition(IGridSpace space, Vector2Int cell)
    {
        return CellWorldPosition(space, cell);
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
