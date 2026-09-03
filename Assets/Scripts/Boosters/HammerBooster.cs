using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Additive Hammer booster. Player selects one gameplay <see cref="Block"/>
/// (a chain is still one Block). Hammer then removes that Block and its
/// corresponding nests through occupancy + settle/match-presentation teardown.
/// Does not drag, Magnet, or match into a nest.
/// </summary>
public class HammerBooster : MonoBehaviour, IBooster
{
    public enum HammerPhase
    {
        Idle,
        Selecting,
        Executing
    }

    [SerializeField]
    private LevelManager levelManager;

    [SerializeField]
    private BoardManager boardManager;

    [SerializeField]
    [Min(0)]
    [Tooltip("Test inventory. Consumed only after a successful Hammer removal.")]
    private int hammerCharges = 3;

    [SerializeField]
    private bool enableKeyboardActivate = true;

    [SerializeField]
    private bool debugLog = true;

    private HammerPhase phase = HammerPhase.Idle;
    private Block highlightedBlock;
    private HammerSmashView3D smashView;
    private Coroutine smashRoutine;
    private Block smashBlock;
    private readonly List<PieceView3D> smashViews = new List<PieceView3D>();

    public HammerPhase Phase => phase;
    public bool IsSelecting => phase == HammerPhase.Selecting;
    public bool IsBusy => phase != HammerPhase.Idle;
    public int HammerCharges => hammerCharges;

    /// <summary>
    /// True for the full smash visual sequence, including leftover fragments/CFX
    /// after gameplay removal. Used only to gate Level Complete presentation.
    /// </summary>
    public bool IsPresentationActive
    {
        get
        {
            if (phase == HammerPhase.Executing || smashRoutine != null)
            {
                return true;
            }

            if (smashView != null && smashView.IsVisible)
            {
                return true;
            }

            return BoardVfx3D.HasActiveHammerPresentation();
        }
    }

    public BoosterType Type => BoosterType.Hammer;

    public BoosterState State
    {
        get
        {
            switch (phase)
            {
                case HammerPhase.Selecting:
                    return BoosterState.Selecting;
                case HammerPhase.Executing:
                    return BoosterState.Executing;
                default:
                    return BoosterState.Idle;
            }
        }
    }

    int IBooster.Charges => hammerCharges;

    public bool CanActivate
    {
        get
        {
            if (phase == HammerPhase.Executing)
            {
                return false;
            }

            if (phase == HammerPhase.Selecting)
            {
                return true;
            }

            if (levelManager != null && !levelManager.IsGameplayInputAllowed)
            {
                return false;
            }

            return hammerCharges > 0;
        }
    }

    public event Action<int> OnChargesChanged;
    public event Action<HammerPhase> OnPhaseChanged;
    public event Action OnStateChanged;

    void IBooster.Activate() => ActivateHammer();

    void IBooster.Cancel() => CancelHammer();

    void IBooster.ResetState(string reason) => ResetHammerState(reason);

    bool IBooster.TryHandleBlockSelection(Block block) => TryHandleSelectionPress(block);

    private void Awake()
    {
        if (levelManager == null)
        {
            levelManager = FindFirstObjectByType<LevelManager>();
        }

        if (boardManager == null)
        {
            boardManager = FindFirstObjectByType<BoardManager>();
        }
    }

    private void OnDisable()
    {
        ResetHammerState("disabled");
    }

    /// <summary>
    /// Clears selection/execution after level load or restart.
    /// Does not change charge inventory.
    /// </summary>
    public void ResetHammerState(string reason = null)
    {
        AbortSmashPresentation();
        ClearHighlight();
        HideSmashView();
        BoardVfx3D.ClearHammerEffects();
        if (phase != HammerPhase.Idle)
        {
            SetPhase(HammerPhase.Idle);
        }

        BoosterSelectionOverlay.HideExisting(true);

        if (!string.IsNullOrEmpty(reason))
        {
            Log($"Hammer reset: {reason}");
        }
    }

    private void Update()
    {
        if (!enableKeyboardActivate)
        {
            return;
        }

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
        {
            ToggleHammer();
        }
    }

    [ContextMenu("Activate Hammer")]
    public void ActivateHammer()
    {
        TryBeginActivation(out _);
    }

    /// <summary>
    /// Begins Hammer selection using the same gates as <see cref="ActivateHammer"/>.
    /// Cancel-while-selecting counts as success (no failure reason).
    /// </summary>
    public bool TryBeginActivation(out BoosterFailureReason failure)
    {
        failure = BoosterFailureReason.None;

        if (phase == HammerPhase.Executing)
        {
            failure = BoosterFailureReason.Busy;
            return false;
        }

        if (phase == HammerPhase.Selecting)
        {
            CancelHammer("Cancelled");
            return true;
        }

        if (levelManager != null && !levelManager.IsGameplayInputAllowed)
        {
            Log("Hammer ignored: gameplay input not allowed");
            failure = BoosterFailureReason.Unavailable;
            return false;
        }

        if (hammerCharges <= 0)
        {
            Log("Hammer ignored: no charges");
            failure = BoosterFailureReason.NoCharges;
            return false;
        }

        SetPhase(HammerPhase.Selecting);
        HideSmashView();
        Log($"Hammer selecting (charges={hammerCharges}). Tap a block.");
        return true;
    }

    public void ToggleHammer()
    {
        ActivateHammer();
    }

    public void CancelHammer(string reason = null)
    {
        if (phase == HammerPhase.Executing)
        {
            return;
        }

        ClearHighlight();
        HideSmashView();
        SetPhase(HammerPhase.Idle);
        if (!string.IsNullOrEmpty(reason))
        {
            Log($"Hammer cancelled: {reason}");
        }
    }

    /// <summary>
    /// Called by InputManager while selecting. Returns true if the press was consumed.
    /// </summary>
    public bool TryHandleSelectionPress(Block block)
    {
        if (phase != HammerPhase.Selecting)
        {
            return false;
        }

        if (block == null)
        {
            Log("Hammer: tap a block to smash");
            return true;
        }

        TryUseHammerOnBlock(block);
        return true;
    }

    public bool TryUseHammerOnBlock(Block block)
    {
        if (phase != HammerPhase.Selecting)
        {
            return false;
        }

        if (!CanHammerBlock(block, out string failReason))
        {
            Log($"Hammer failed: {failReason}");
            if (block != null)
            {
                block.PlayInvalidInteractionFeedback();
            }

            BoosterFeedbackMessage.NotifyFailure(BoosterType.Hammer, BoosterFailureReason.InvalidTarget);
            return false;
        }

        ClearHighlight();
        highlightedBlock = block;
        block.ShowDragSelection();
        SetPhase(HammerPhase.Executing);
        smashBlock = block;
        smashRoutine = StartCoroutine(SmashThenRemove(block));
        return true;
    }

    /// <summary>Test helper to grant charges without economy UI.</summary>
    public void SetHammerCharges(int count)
    {
        SetCharges(count);
    }

    /// <summary>
    /// Presentation-only eligibility. Mirrors Ice / Shutter / settled Hammer rules
    /// without changing targeting, charges, or input gates.
    /// </summary>
    public bool IsHammerEligibleVisual(PieceView3D view)
    {
        if (view == null || view.ConfiguredAsNest)
        {
            return false;
        }

        return IsHammerEligibleVisual(view.SourceBlock);
    }

    public bool IsHammerEligibleVisual(Block block)
    {
        if (block == null || !block.isActiveAndEnabled || block.IsSettled || block.IsFrozen)
        {
            return false;
        }

        BoardManager board = boardManager != null ? boardManager : block.Board;
        if (board != null && board.IsBlockUnderImpassableCell(block))
        {
            return false;
        }

        return true;
    }

    private bool CanHammerBlock(Block block, out string failReason)
    {
        failReason = null;
        if (block == null || !block.isActiveAndEnabled)
        {
            failReason = "invalid block";
            return false;
        }

        if (block.IsSettled)
        {
            failReason = "block settled";
            return false;
        }

        if (block.IsFrozen)
        {
            failReason = "block frozen by Ice";
            return false;
        }

        BlockMover mover = block.GetComponent<BlockMover>();
        if (mover != null && (mover.IsMoving || mover.IsDragging))
        {
            failReason = "block busy";
            return false;
        }

        BoardManager board = boardManager != null ? boardManager : block.Board;
        if (board == null)
        {
            failReason = "no board";
            return false;
        }

        if (board.IsBlockUnderImpassableCell(block))
        {
            failReason = "block under closed shutter";
            return false;
        }

        if (levelManager != null && !levelManager.IsPieceInputAllowed)
        {
            failReason = "piece input not allowed";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Lift / grow / squash / slam presentation, then the existing occupancy + nest teardown.
    /// Gameplay removal runs at impact so fragments can continue afterward.
    /// </summary>
    private IEnumerator SmashThenRemove(Block block)
    {
        smashBlock = block;
        yield return null;
        CollectSmashViews(block, smashViews);
        if (smashViews.Count == 0)
        {
            AddSmashView(smashViews, block.WorldView);
            IReadOnlyList<PieceView3D> extras = block.ExtraWorldViews;
            if (extras != null)
            {
                for (int i = 0; i < extras.Count; i++)
                {
                    AddSmashView(smashViews, extras[i]);
                }
            }
        }
        var corresponding = new List<Target>();
        BoardManager board = boardManager != null ? boardManager : block.Board;
        if (board != null)
        {
            board.CollectCorrespondingTargets(block, corresponding);
        }

        Vector3 center;
        int visualCount;
        CollectBlockVisualCenters(block, out center, out visualCount);
        float cellSize = 1f;
        BoardPresenter3D presenter = UnityEngine.Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter != null)
        {
            cellSize = presenter.CellWorldSize;
        }

        const float scalePeak = 1.10f;
        float liftHeight = Mathf.Max(0.08f, cellSize * 0.16f);
        Vector3 hammerPos = center + Vector3.up * (liftHeight + cellSize * 0.55f);
        HammerSmashView3D view = EnsureSmashView();
        view.ShowAt(hammerPos);

        yield return TweenAnticipation(
            smashViews, 0f, liftHeight, 1f, 1f, 0f, 0f, 0.16f, SmashEase.OutCubic);
        yield return TweenAnticipation(
            smashViews, liftHeight, liftHeight, 1f, scalePeak, 0f, 0f, 0.14f, SmashEase.OutSine);
        yield return HoldAnticipation(smashViews, liftHeight, scalePeak, 0f, 0.08f);
        yield return view.PlayWindUp(0.12f);
        yield return TweenAnticipation(
            smashViews, liftHeight, liftHeight, scalePeak, scalePeak, 0f, 1f, 0.07f, SmashEase.InQuad);
        yield return view.PlaySlam(hammerPos, 0.16f);

        Color accent = ShapeVisuals3D.AccentColor(block.GetActiveShape(0));
        float footprint = visualCount > 1
            ? Mathf.Min(1.85f, 1f + 0.28f * (visualCount - 1))
            : 1f;
        PlayHammerImpactPresentation(corresponding, center, visualCount, accent, footprint);

        int cellCount = block.CellCount;
        ShapeType shape = block.GetActiveShape(0);
        ApplyHammerRemoval(block, corresponding);
        SetCharges(hammerCharges - 1);
        Log($"Hammer removed block shape={shape} cells={cellCount} charges={hammerCharges}");

        yield return view.PlayImpactSettle(0.05f);
        ClearHighlight();
        HideSmashView();
        smashRoutine = null;
        smashBlock = null;
        smashViews.Clear();
        SetPhase(HammerPhase.Idle);
    }

    private void PlayHammerImpactPresentation(
        List<Target> nests,
        Vector3 center,
        int visualCount,
        Color accent,
        float footprint)
    {
        BoardVfx3D.PlayHammerImpact(center, accent, footprint);
        for (int i = 0; i < smashViews.Count; i++)
        {
            PieceView3D pieceView = smashViews[i];
            if (pieceView == null || !pieceView.gameObject.activeInHierarchy)
            {
                continue;
            }

            Color cellAccent = ShapeVisuals3D.AccentColor(pieceView.ConfiguredShape);
            BoardVfx3D.PlayHammerBreakFragments(pieceView, cellAccent);
            if (visualCount > 1)
            {
                BoardVfx3D.PlayHammerCellBurst(ResolveViewWorld(pieceView), cellAccent);
            }
        }

        if (nests == null)
        {
            return;
        }

        for (int i = 0; i < nests.Count; i++)
        {
            Target nest = nests[i];
            if (nest == null)
            {
                continue;
            }

            BoardVfx3D.PlayHammerNest(
                ResolveTargetWorld(nest),
                ShapeVisuals3D.AccentColor(nest.RequiredShape));
        }
    }

    /// <summary>
    /// Whole-Block removal using the existing occupancy + settle teardown, then
    /// corresponding nest removal via <see cref="BoardManager.RemoveTargetWithoutMatch"/>.
    /// Does not call nest matching, <see cref="LevelManager.NotifySuccessfulMatch"/>,
    /// or <see cref="BlockMover"/> drag APIs.
    /// </summary>
    private void ApplyHammerRemoval(Block block, List<Target> corresponding)
    {
        BoardManager board = boardManager != null ? boardManager : block.Board;
        block.HideDragSelection();

        if (board != null)
        {
            board.UnregisterBlock(block);
        }

        block.BeginMatchPresentation();
        block.Settle();
        block.CompleteMatchPresentation();

        if (board != null)
        {
            for (int i = 0; i < corresponding.Count; i++)
            {
                board.RemoveTargetWithoutMatch(corresponding[i]);
            }
        }

        if (levelManager != null)
        {
            levelManager.NotifyBlockSettled();
        }
    }

    private static void CollectBlockVisualCenters(Block block, out Vector3 center, out int visualCount)
    {
        center = Vector3.zero;
        visualCount = 0;
        Vector3 sum = Vector3.zero;

        PieceView3D[] views = UnityEngine.Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null || view.SourceBlock != block || !view.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 pos = ResolveViewWorld(view);
            sum += pos;
            visualCount++;
        }

        if (visualCount > 0)
        {
            center = sum / visualCount;
            return;
        }

        BoardPresenter3D presenter = UnityEngine.Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        int cellCount = Mathf.Max(1, block.CellCount);
        sum = Vector3.zero;
        for (int i = 0; i < cellCount; i++)
        {
            Vector3 pos = ResolveGridWorld(presenter, block.GetCellWorld(i));
            sum += pos;
            visualCount++;
        }

        center = visualCount > 0 ? sum / visualCount : Vector3.zero;
    }

    private static Vector3 ResolveTargetWorld(Target target)
    {
        if (TryGetVisiblePieceCenter(target != null ? target.WorldView : null, out Vector3 nestCenter))
        {
            return nestCenter;
        }

        BoardPresenter3D presenter = UnityEngine.Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (target != null)
        {
            return ResolveGridWorld(presenter, target.GridPosition);
        }

        return Vector3.zero;
    }

    private static Vector3 ResolveViewWorld(PieceView3D view)
    {
        if (TryGetVisiblePieceCenter(view, out Vector3 center))
        {
            return center;
        }

        return view != null ? view.transform.position : Vector3.zero;
    }

    private static Vector3 ResolveGridWorld(BoardPresenter3D presenter, Vector2Int cell)
    {
        if (presenter == null || presenter.GridSpace3D == null)
        {
            return Vector3.zero;
        }

        Vector3 world = presenter.GridSpace3D.GridToWorld(cell);
        world.y = presenter.CellSurfaceWorldY;
        return world;
    }

    private static bool TryGetVisiblePieceCenter(PieceView3D pieceView, out Vector3 center)
    {
        center = Vector3.zero;
        if (pieceView == null || !pieceView.gameObject.activeInHierarchy)
        {
            return false;
        }

        MeshRenderer renderer = pieceView.GetComponentInChildren<MeshRenderer>();
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
        {
            return false;
        }

        Bounds bounds = renderer.bounds;
        center = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        return true;
    }

    private void SetPhase(HammerPhase next)
    {
        if (phase == next)
        {
            return;
        }

        phase = next;
        SyncSelectionOverlay();
        OnPhaseChanged?.Invoke(phase);
        OnStateChanged?.Invoke();
    }

    private void SyncSelectionOverlay()
    {
        if (phase == HammerPhase.Idle)
        {
            BoosterSelectionOverlay.HideExisting();
            return;
        }

        BoosterSelectionOverlay overlay = BoosterSelectionOverlay.Ensure();
        if (overlay != null)
        {
            overlay.SetVisible(true);
        }
    }

    private void SetCharges(int count)
    {
        int clamped = Mathf.Max(0, count);
        if (hammerCharges == clamped)
        {
            return;
        }

        hammerCharges = clamped;
        OnChargesChanged?.Invoke(hammerCharges);
    }

    private void ClearHighlight()
    {
        if (highlightedBlock != null)
        {
            highlightedBlock.HideDragSelection();
            highlightedBlock = null;
        }
    }

    private void AbortSmashPresentation()
    {
        if (smashRoutine != null)
        {
            StopCoroutine(smashRoutine);
            smashRoutine = null;
        }

        RestoreSmashLifts();
        smashBlock = null;
        smashViews.Clear();
    }

    private void RestoreSmashLifts()
    {
        for (int i = 0; i < smashViews.Count; i++)
        {
            PieceView3D view = smashViews[i];
            if (view == null)
            {
                continue;
            }

            view.SetPresentationLift(0f, 1f);
        }

        if (smashBlock != null && smashBlock.WorldView != null)
        {
            smashBlock.WorldView.SetPresentationLift(0f, 1f);
            BoardPresenter3D presenter = UnityEngine.Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
            if (presenter != null && presenter.GridSpace != null)
            {
                smashBlock.WorldView.ApplyGridPosition(presenter.GridSpace, smashBlock.GridPosition);
            }
        }
    }

    private HammerSmashView3D EnsureSmashView()
    {
        if (smashView == null)
        {
            smashView = HammerSmashView3D.Ensure();
        }

        return smashView;
    }

    private void HideSmashView()
    {
        if (smashView != null)
        {
            smashView.Hide();
        }
    }

    private static void CollectSmashViews(Block block, List<PieceView3D> results)
    {
        results.Clear();
        if (block == null)
        {
            return;
        }

        PieceView3D[] views = UnityEngine.Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null || view.SourceBlock != block || !view.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!results.Contains(view))
            {
                results.Add(view);
            }
        }

        AddSmashView(results, block.WorldView);
        IReadOnlyList<PieceView3D> extras = block.ExtraWorldViews;
        if (extras == null)
        {
            return;
        }

        for (int i = 0; i < extras.Count; i++)
        {
            AddSmashView(results, extras[i]);
        }
    }

    private static void AddSmashView(List<PieceView3D> results, PieceView3D view)
    {
        if (view == null || !view.gameObject.activeInHierarchy || results.Contains(view))
        {
            return;
        }

        results.Add(view);
    }

    private enum SmashEase
    {
        OutCubic,
        OutSine,
        InQuad,
        InCubic
    }

    private IEnumerator HoldAnticipation(
        List<PieceView3D> views,
        float lift,
        float scale,
        float squash,
        float duration)
    {
        if (views == null || views.Count == 0)
        {
            yield break;
        }

        BoardPresenter3D presenter = UnityEngine.Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace space = presenter != null ? presenter.GridSpace : null;
        PieceView3D primary = smashBlock != null ? smashBlock.WorldView : null;
        Vector2Int primaryCell = smashBlock != null ? smashBlock.GridPosition : default;
        float held = 0f;
        while (held < duration)
        {
            for (int i = 0; i < views.Count; i++)
            {
                PieceView3D view = views[i];
                if (view == null)
                {
                    continue;
                }

                view.SetPresentationAnticipation(lift, scale, squash);
                if (space != null && view == primary)
                {
                    view.ApplyGridPosition(space, primaryCell);
                }
            }

            held += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator TweenAnticipation(
        List<PieceView3D> views,
        float fromLift,
        float toLift,
        float fromScale,
        float toScale,
        float fromSquash,
        float toSquash,
        float duration,
        SmashEase ease)
    {
        if (views == null || views.Count == 0)
        {
            yield break;
        }

        BoardPresenter3D presenter = UnityEngine.Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace space = presenter != null ? presenter.GridSpace : null;
        PieceView3D primary = smashBlock != null ? smashBlock.WorldView : null;
        Vector2Int primaryCell = smashBlock != null ? smashBlock.GridPosition : default;
        float life = Mathf.Max(0.01f, duration);
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Clamp01(t + Time.deltaTime / life);
            float eased = EvaluateSmashEase(ease, t);
            float lift = Mathf.LerpUnclamped(fromLift, toLift, eased);
            float scale = Mathf.LerpUnclamped(fromScale, toScale, eased);
            float squash = Mathf.LerpUnclamped(fromSquash, toSquash, eased);
            for (int i = 0; i < views.Count; i++)
            {
                PieceView3D view = views[i];
                if (view == null)
                {
                    continue;
                }

                view.SetPresentationAnticipation(lift, scale, squash);
                if (space != null && view == primary)
                {
                    view.ApplyGridPosition(space, primaryCell);
                }
            }

            yield return null;
        }
    }

    private static float EvaluateSmashEase(SmashEase ease, float t)
    {
        switch (ease)
        {
            case SmashEase.OutSine:
                return TweenAnimationUtility.EvaluateEaseOutSine(t);
            case SmashEase.InQuad:
                return TweenAnimationUtility.EvaluateEaseInQuad(t);
            case SmashEase.InCubic:
                return TweenAnimationUtility.EvaluateEaseInCubic(t);
            default:
                return TweenAnimationUtility.EvaluateEaseOutCubic(t);
        }
    }

    private void Log(string message)
    {
        if (debugLog)
        {
            Debug.Log($"[Hammer] {message}", this);
        }
    }
}
