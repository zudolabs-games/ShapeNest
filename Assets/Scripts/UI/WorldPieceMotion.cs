using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// World3D <see cref="IPieceMotion"/> driven by DOTween.
/// Gameplay sequencing stays in <see cref="BlockMover"/>; this only animates presentation.
/// Phase 52E: polished hop easing, subtle mesh squash, nest entry feel — same destinations/durations.
/// Phase 52H: nest-entry compress/insert/settle mesh feel + nest socket pulse (presentation only).
/// </summary>
[DisallowMultipleComponent]
public class WorldPieceMotion : MonoBehaviour, IPieceMotion
{
    // Presentation-only carry/lift timing (does not change BlockMover hop secondsPerCell).
    private const float PickupDuration = 0.08f;
    private const float SettleDuration = 0.10f;
    private const float CarryScaleBoost = 0.05f;
    private const float NestJumpFlightScale = 1.10f;
    private const float NestAnticipateScaleMin = 1.0f;
    private const float HopAnticipatePortion = 0.16f;
    private const float HopSettlePortion = 0.18f;

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

    /// <summary>
    /// Stops hop/travel tweens so fingerwise continuous pose can own the transform.
    /// Keeps carry lift active.
    /// </summary>
    public void InterruptTweensForFingerDrag()
    {
        KillActive(false);
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
        // Phase 61B: keep continuation closer to constant-speed cruise; turns slightly softer.
        // Do not lower secondsPerCell — reduce end-of-hop settle dead-feel instead.
        float linearWeight = continuation ? (turn ? 0.88f : 0.98f) : 0.35f;
        float settlePortion = continuation ? 0.08f : HopSettlePortion;
        float microFloat = cellAxis * Mathf.Clamp(hopLiftPercent * 0.38f, 0.010f, 0.018f);
        float travelSquash = Mathf.Clamp01(1f - Mathf.Clamp(hopTravelScale, 0.96f, 1f));

        Sequence sequence = DOTween.Sequence().SetId(TweenAnimationUtility.PieceMotionId).SetLink(pieceView.gameObject);
        activeSequence = sequence;

        try
        {
            AppendHopTravel(
                sequence,
                start,
                endRest,
                fromRestY,
                toRestY,
                duration,
                microFloat,
                linearWeight,
                softenTurn: false,
                useCubicEase: !continuation,
                anticipate,
                travelSquash,
                settlePortion);
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

                ClearHopPresentationSquash();
            });

            yield return TweenAnimationUtility.Wait(sequence);
            lastHopDir = hopDir;
        }
        finally
        {
            ClearHopPresentationSquash();
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

    /// <summary>
    /// Phase 53D: one continuous Shuffle move — anticipation, quintic travel, soft arrival/settle.
    /// Does not change GridPosition or occupancy; presentation only.
    /// </summary>
    public IEnumerator AnimateShuffleMove(
        IGridSpace gridSpace,
        Vector2 visualCellSize,
        Vector2Int from,
        Vector2Int to,
        float duration)
    {
        IGridSpace space = ResolveSpace(gridSpace);
        if (pieceView == null || space == null)
        {
            yield break;
        }

        TweenAnimationUtility.KillById(pieceView.gameObject, TweenAnimationUtility.ShuffleId, false);
        lastHopDir = Vector2Int.zero;
        bool addedLock = pieceView != null && !pieceView.IsMotionLocked;
        if (addedLock)
        {
            pieceView.BeginMotionLock();
        }

        Vector3 start = CellRestWorldPosition(space, from);
        Vector3 endRest = CellRestWorldPosition(space, to);
        float fromRestY = CellRestWorldPosition(space, from).y;
        float toRestY = endRest.y;
        float cellAxis = Mathf.Max(visualCellSize.x, visualCellSize.y, 0.01f);
        float liftAmount = cellAxis * 0.028f;
        const float anticipatePortion = 0.09f;
        const float settlePortion = 0.09f;
        const float peakSquash = 0.11f;
        float travelSpan = Mathf.Max(0.0001f, 1f - anticipatePortion - settlePortion);

        Sequence sequence = DOTween.Sequence()
            .SetId(TweenAnimationUtility.ShuffleId)
            .SetLink(pieceView.gameObject);

        try
        {
            sequence.Append(TweenAnimationUtility.Progress(Mathf.Max(0.01f, duration), t =>
            {
                if (pieceView == null)
                {
                    return;
                }

                t = Mathf.Clamp01(t);
                float positionT = ResolveShufflePositionT(t, anticipatePortion, settlePortion, travelSpan);
                float travelEased = TweenAnimationUtility.EvaluateEaseInOutQuint(positionT);
                Vector3 pos = Vector3.LerpUnclamped(start, endRest, travelEased);
                float restY = Mathf.LerpUnclamped(fromRestY, toRestY, travelEased);
                pos.y = restY;
                if (liftAmount > 0.0005f && positionT > 0.0001f && positionT < 0.999f)
                {
                    float liftEnvelope = Mathf.Sin(travelEased * Mathf.PI);
                    pos.y += liftAmount * liftEnvelope;
                }

                if (IsFiniteVector(pos))
                {
                    pieceView.transform.position = pos;
                }

                float squash = EvaluateShuffleSquashEnvelope(t, anticipatePortion, settlePortion, peakSquash);
                pieceView.SetPresentationAnticipation(0f, pieceView.CarryMeshScale, squash);
            }));

            sequence.OnComplete(() =>
            {
                if (pieceView == null)
                {
                    return;
                }

                Vector3 seated = endRest;
                seated.y = toRestY;
                if (IsFiniteVector(seated))
                {
                    pieceView.transform.position = seated;
                }

                pieceView.SetPresentationAnticipation(0f, pieceView.CarryMeshScale, 0f);
            });

            yield return TweenAnimationUtility.Wait(sequence);
        }
        finally
        {
            if (pieceView != null)
            {
                pieceView.SetPresentationAnticipation(0f, pieceView.CarryMeshScale, 0f);
                if (addedLock)
                {
                    pieceView.EndMotionLock();
                }
            }
        }
    }

    private static float ResolveShufflePositionT(
        float t,
        float anticipatePortion,
        float settlePortion,
        float travelSpan)
    {
        if (t <= anticipatePortion)
        {
            return 0f;
        }

        if (t >= 1f - settlePortion)
        {
            return 1f;
        }

        return Mathf.Clamp01((t - anticipatePortion) / travelSpan);
    }

    private static float EvaluateShuffleSquashEnvelope(
        float t,
        float anticipatePortion,
        float settlePortion,
        float peak)
    {
        t = Mathf.Clamp01(t);
        float travelEnd = 1f - settlePortion;
        const float tailPeak = 0.16f;

        if (t <= anticipatePortion)
        {
            float u = anticipatePortion > 0.0001f ? t / anticipatePortion : 1f;
            return peak * TweenAnimationUtility.EvaluateSmoothStep(u);
        }

        if (t >= travelEnd)
        {
            float u = settlePortion > 0.0001f ? (t - travelEnd) / settlePortion : 1f;
            float eased = TweenAnimationUtility.EvaluateSmoothStep(u);
            return Mathf.Lerp(peak * tailPeak, 0f, eased);
        }

        float mid = (t - anticipatePortion) / Mathf.Max(0.0001f, travelEnd - anticipatePortion);
        float midEased = TweenAnimationUtility.EvaluateSmoothStep(mid);
        return Mathf.Lerp(peak, peak * tailPeak, midEased);
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
        // Phase 52H: keep BlockMover hopScale as insertion scale (~0.97). Cap pump if >1.
        float insertMul = hopScale;
        if (hopScale > 1.001f)
        {
            insertMul = Mathf.Min(hopScale, NestJumpFlightScale);
        }
        else if (hopScale >= 0.999f)
        {
            insertMul = 0.97f;
        }
        else
        {
            insertMul = Mathf.Clamp(hopScale, 0.96f, 0.99f);
        }

        Vector3 insertScale = scaleRest * insertMul;

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
                // Keep existing nest-entry trajectory; EaseInCubic biases into the socket.
                float eased = TweenAnimationUtility.EvaluateEaseInCubic(t);
                if (float.IsNaN(eased) || float.IsInfinity(eased))
                {
                    eased = t;
                }

                Vector3 pos = QuadraticBezier3(start, control, end, eased);
                if (IsFiniteVector(pos))
                {
                    pieceView.transform.position = pos;
                }

                // Root eases toward insertion scale (no mid-flight pump).
                Vector3 scale = Vector3.LerpUnclamped(scaleRest, insertScale, eased);
                if (IsFiniteVector(scale))
                {
                    pieceView.LocalScale = scale;
                }

                ApplyNestEntryMeshFeel(t, insertMul);
            }));

            sequence.AppendCallback(() =>
            {
                // Socket response as the piece seats — before MatchEffect dissolve.
                PulseNestSocketAt(space, to);
            });

            // Sit: EaseOutCubic settle — tiny press then return to rest scale (existing sitDuration).
            AppendWorldMoveScale(
                sequence,
                end,
                end,
                sitDuration,
                insertScale,
                scaleRest,
                easeOut: true,
                settleMeshSquash: true);

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

                ClearHopPresentationSquash();
            });

            yield return TweenAnimationUtility.Wait(sequence);
        }
        finally
        {
            ClearHopPresentationSquash();
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

    /// <summary>
    /// Phase 52H mesh-only nest-entry feel within the existing arc duration.
    /// Compress → insert scale → light press. Does not change destinations or timings.
    /// </summary>
    private void ApplyNestEntryMeshFeel(float t, float insertMul)
    {
        if (pieceView == null)
        {
            return;
        }

        t = Mathf.Clamp01(t);
        float squash;
        float mul;

        // A/B: pre-insertion compress (~XZ widen / Y squash), then blend into insert scale.
        const float compressPeakT = 0.14f;
        const float insertBlendT = 0.32f;
        if (t <= compressPeakT)
        {
            float u = compressPeakT > 0.0001f ? t / compressPeakT : 1f;
            float peak = TweenAnimationUtility.EvaluateEaseOutCubic(u);
            // squash≈0.33 → Y≈0.92, XZ≈1.05 (near 1.02 / 0.92 target)
            squash = 0.33f * peak;
            mul = 1f;
        }
        else if (t <= insertBlendT)
        {
            float u = (t - compressPeakT) / Mathf.Max(0.0001f, insertBlendT - compressPeakT);
            float eased = TweenAnimationUtility.EvaluateEaseInCubic(u);
            squash = Mathf.Lerp(0.33f, 0.06f, eased);
            mul = Mathf.Lerp(1f, insertMul, eased);
        }
        else
        {
            float u = (t - insertBlendT) / Mathf.Max(0.0001f, 1f - insertBlendT);
            float eased = TweenAnimationUtility.EvaluateEaseInCubic(u);
            squash = Mathf.Lerp(0.06f, 0.04f, eased);
            mul = Mathf.Lerp(insertMul, Mathf.Min(insertMul, 0.96f), eased * 0.35f);
        }

        pieceView.SetPresentationAnticipation(0f, mul, squash);
    }

    private void PulseNestSocketAt(IGridSpace space, Vector2Int cell)
    {
        MeshRenderer nestRenderer = FindNestRendererAtCell(space, cell);
        if (nestRenderer == null)
        {
            return;
        }

        PieceView3D nestView = nestRenderer.GetComponentInParent<PieceView3D>();
        if (nestView != null && nestView.ConfiguredAsNest)
        {
            nestView.PlayNestSocketPulse();
        }
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
        bool softenTurn,
        bool useCubicEase,
        bool anticipate,
        float travelSquash,
        float settlePortion = HopSettlePortion)
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

                ClearHopPresentationSquash();
            });
            return;
        }

        float settle = Mathf.Clamp(settlePortion, 0.02f, 0.35f);
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
            float eased;
            if (softenTurn)
            {
                eased = TweenAnimationUtility.EvaluateEaseInOutSine(t);
            }
            else if (useCubicEase)
            {
                eased = TweenAnimationUtility.EvaluateEaseInOutCubic(t);
            }
            else
            {
                eased = TweenAnimationUtility.EvaluateHopCruise(t, linearWeight);
            }

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

            ApplyHopPresentationSquash(t, anticipate, travelSquash, settle);
        }));
    }

    /// <summary>
    /// Phase 52E: mesh-only anticipation / travel squash / settle within the existing hop duration.
    /// Does not change transform destination or GridPosition.
    /// </summary>
    private void ApplyHopPresentationSquash(float t, bool anticipate, float travelSquash, float settlePortion = HopSettlePortion)
    {
        if (pieceView == null)
        {
            return;
        }

        float squash = 0f;
        float peak = Mathf.Clamp(travelSquash * 1.35f, 0f, 0.22f);
        if (anticipate && t < HopAnticipatePortion)
        {
            float u = Mathf.Clamp01(t / HopAnticipatePortion);
            // Wind-up: brief compress, then release into travel.
            squash = peak * 1.15f * (1f - TweenAnimationUtility.EvaluateEaseOutCubic(u));
        }
        else
        {
            squash = peak * PieceMotionMath.MicroFloatEnvelope(t);
        }

        float settle = Mathf.Clamp(settlePortion, 0.02f, 0.35f);
        if (t > 1f - settle)
        {
            float u = Mathf.Clamp01((t - (1f - settle)) / settle);
            float settlePulse = peak * 0.55f * Mathf.Sin(u * Mathf.PI) * (1f - u);
            float release = TweenAnimationUtility.EvaluateEaseOutCubic(u);
            squash = Mathf.Lerp(squash, 0f, release) + settlePulse;
        }

        float meshMul = CarryVisualScale(pieceView.PresentationLift);
        pieceView.SetPresentationAnticipation(pieceView.PresentationLift, meshMul, squash);
    }

    private void ClearHopPresentationSquash()
    {
        if (pieceView == null)
        {
            return;
        }

        float meshMul = CarryVisualScale(pieceView.PresentationLift);
        pieceView.SetPresentationAnticipation(pieceView.PresentationLift, meshMul, 0f);
    }

    private void AppendWorldMoveScale(
        Sequence sequence,
        Vector3 from,
        Vector3 to,
        float duration,
        Vector3 scaleFrom,
        Vector3 scaleTo,
        bool easeOut,
        bool settleMeshSquash = false)
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

                    if (settleMeshSquash)
                    {
                        ClearHopPresentationSquash();
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
                ? TweenAnimationUtility.EvaluateEaseOutCubic(t)
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

            if (settleMeshSquash)
            {
                // Phase 52H: subtler nest press then release — presentation only.
                float press = 0.12f * Mathf.Sin(t * Mathf.PI) * (1f - (0.35f * t));
                float mul = Mathf.Lerp(1f, 0.98f, Mathf.Sin(t * Mathf.PI) * (1f - t));
                pieceView.SetPresentationAnticipation(0f, mul, press);
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
        ClearHopPresentationSquash();
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
