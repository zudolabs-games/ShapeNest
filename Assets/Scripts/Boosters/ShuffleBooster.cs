using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shuffle booster: reassigns eligible playable <see cref="Block"/> anchors on the board
/// while preserving block identity, shape, nesting, chains, Ice, and Shutters.
/// Does not match, settle, or alter level data.
/// </summary>
public class ShuffleBooster : MonoBehaviour, IBooster
{
    public enum ShufflePhase
    {
        Idle,
        Executing
    }

    private const int MaxPlanAttempts = 48;
    private const float ShuffleSecondsPerCell = 0.12f;
    private const float ShuffleMinDuration = 0.18f;
    private const float ShuffleMaxDuration = 0.55f;
    private const float ShuffleStaggerSeconds = 0.018f;

    [SerializeField]
    private LevelManager levelManager;

    [SerializeField]
    private BoardManager boardManager;

    [SerializeField]
    [Min(0)]
    [Tooltip("Test inventory. Consumed only after a successful Shuffle rearrangement.")]
    private int shuffleCharges = 3;

    [SerializeField]
    private bool enableKeyboardActivate = true;

    [SerializeField]
    private bool debugLog = true;

    private ShufflePhase phase = ShufflePhase.Idle;
    private Coroutine shuffleRoutine;
    private readonly List<PieceView3D> shuffleHoldViewsScratch = new List<PieceView3D>();

    public ShufflePhase Phase => phase;
    public bool IsSelecting => false;
    public bool IsBusy => phase != ShufflePhase.Idle;
    public int ShuffleCharges => shuffleCharges;

    public BoosterType Type => BoosterType.Shuffle;

    public BoosterState State =>
        phase == ShufflePhase.Executing ? BoosterState.Executing : BoosterState.Idle;

    int IBooster.Charges => shuffleCharges;

    public bool CanActivate
    {
        get
        {
            if (phase == ShufflePhase.Executing)
            {
                return false;
            }

            if (levelManager != null && !levelManager.IsGameplayInputAllowed)
            {
                return false;
            }

            return shuffleCharges > 0;
        }
    }

    public event Action<int> OnChargesChanged;
    public event Action<ShufflePhase> OnPhaseChanged;
    public event Action OnStateChanged;

    void IBooster.Activate() => ActivateShuffle();

    void IBooster.Cancel() { }

    void IBooster.ResetState(string reason) => ResetShuffleState(reason);

    bool IBooster.TryHandleBlockSelection(Block block) => false;

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
        ResetShuffleState("disabled");
    }

    public void ResetShuffleState(string reason = null)
    {
        if (shuffleRoutine != null)
        {
            StopCoroutine(shuffleRoutine);
            shuffleRoutine = null;
        }

        ClearShufflePresentation();
        EndShufflePresentationHold();

        if (phase != ShufflePhase.Idle)
        {
            SetPhase(ShufflePhase.Idle);
        }

        if (!string.IsNullOrEmpty(reason))
        {
            Log($"Shuffle reset: {reason}");
        }
    }

    private void Update()
    {
        if (!enableKeyboardActivate)
        {
            return;
        }

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.sKey.wasPressedThisFrame)
        {
            BoosterManager manager = FindFirstObjectByType<BoosterManager>();
            if (manager != null)
            {
                manager.TryActivate(BoosterType.Shuffle);
            }
            else
            {
                ActivateShuffle();
            }
        }
    }

    [ContextMenu("Activate Shuffle")]
    public void ActivateShuffle()
    {
        TryBeginActivation(out _);
    }

    /// <summary>
    /// Starts Shuffle using the same gates and plan build as <see cref="ActivateShuffle"/>.
    /// Returns false with a presentation reason when Shuffle does not begin.
    /// </summary>
    public bool TryBeginActivation(out BoosterFailureReason failure)
    {
        failure = BoosterFailureReason.None;

        if (phase == ShufflePhase.Executing)
        {
            failure = BoosterFailureReason.Busy;
            return false;
        }

        if (levelManager != null && !levelManager.IsGameplayInputAllowed)
        {
            Log("Shuffle ignored: gameplay input not allowed");
            failure = BoosterFailureReason.Unavailable;
            return false;
        }

        if (shuffleCharges <= 0)
        {
            Log("Shuffle ignored: no charges");
            failure = BoosterFailureReason.NoCharges;
            return false;
        }

        if (!IsBoardStableForShuffle(out string busyReason))
        {
            Log($"Shuffle ignored: {busyReason}");
            failure = BoosterFailureReason.Busy;
            return false;
        }

        BoardManager board = ResolveBoard();
        if (board == null)
        {
            Log("Shuffle ignored: no board");
            failure = BoosterFailureReason.Unavailable;
            return false;
        }

        CollectShuffleUnits(board, shuffleUnitsScratch);
        if (shuffleUnitsScratch.Count == 0)
        {
            Log("Shuffle ignored: no eligible blocks");
            failure = BoosterFailureReason.NoShufflePlan;
            return false;
        }

        if (!TryBuildShufflePlan(board, shuffleUnitsScratch, out Dictionary<Block, Vector2Int> plan))
        {
            Log("Shuffle ignored: no valid different arrangement");
            failure = BoosterFailureReason.NoShufflePlan;
            return false;
        }

        var fromAnchors = new Dictionary<Block, Vector2Int>();
        foreach (KeyValuePair<Block, Vector2Int> entry in plan)
        {
            Block block = entry.Key;
            if (block != null)
            {
                fromAnchors[block] = block.GridPosition;
            }
        }

        BeginShufflePresentationHold(fromAnchors, plan);

        BoardUndoHistory undoHistory = BoardUndoHistory.Resolve();
        undoHistory?.CaptureActiveSnapshot(board);

        SetPhase(ShufflePhase.Executing);
        HideDestinationHighlight();
        shuffleRoutine = StartCoroutine(ExecuteShuffle(board, plan, fromAnchors));
        return true;
    }

    public void SetShuffleCharges(int count)
    {
        SetCharges(count);
    }

    private IEnumerator ExecuteShuffle(
        BoardManager board,
        Dictionary<Block, Vector2Int> plan,
        Dictionary<Block, Vector2Int> fromAnchors)
    {
        try
        {
            if (!ApplyLogicalShuffle(board, plan))
            {
                Log("Shuffle failed during occupancy apply");
                yield break;
            }

            BoardPresenter3D presenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
            IGridSpace space = presenter != null ? presenter.GridSpace3D : null;
            RestoreShuffleViewsAtFrom(space, fromAnchors, plan);

            SetCharges(shuffleCharges - 1);
            Log($"Shuffle applied ({plan.Count} units). Charges left={shuffleCharges}");

            yield return AnimateShuffleMoves(board, fromAnchors, plan);
            board.RebindChildBlockOccupancy();
        }
        finally
        {
            EndShufflePresentationHold();
            shuffleRoutine = null;
            SetPhase(ShufflePhase.Idle);
        }
    }

    private static bool ApplyLogicalShuffle(BoardManager board, Dictionary<Block, Vector2Int> plan)
    {
        if (board == null || plan == null || plan.Count == 0)
        {
            return false;
        }

        var blocks = new List<Block>(plan.Keys);
        for (int i = 0; i < blocks.Count; i++)
        {
            board.UnregisterBlock(blocks[i]);
        }

        foreach (KeyValuePair<Block, Vector2Int> entry in plan)
        {
            Block block = entry.Key;
            Vector2Int anchor = entry.Value;
            if (block == null)
            {
                continue;
            }

            if (!board.TryRegisterBlock(block, anchor))
            {
                return false;
            }

            block.SetGridPosition(anchor, preserveWorldPresentation: true);
        }

        return true;
    }

    private IEnumerator AnimateShuffleMoves(
        BoardManager board,
        Dictionary<Block, Vector2Int> fromAnchors,
        Dictionary<Block, Vector2Int> toAnchors)
    {
        BoardPresenter3D presenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter == null || presenter.GridSpace3D == null)
        {
            FinalizeShufflePresentation(board, toAnchors);
            yield break;
        }

        IGridSpace space = presenter.GridSpace3D;
        Vector2 cellSize = Vector2.one * Mathf.Max(0.01f, presenter.CellWorldSize);
        int running = 0;
        int staggerIndex = 0;
        foreach (KeyValuePair<Block, Vector2Int> entry in toAnchors)
        {
            Block block = entry.Key;
            if (block == null || !fromAnchors.TryGetValue(block, out Vector2Int fromAnchor))
            {
                continue;
            }

            Vector2Int toAnchor = entry.Value;
            if (fromAnchor == toAnchor)
            {
                continue;
            }

            running++;
            float stagger = staggerIndex * ShuffleStaggerSeconds;
            staggerIndex++;
            StartCoroutine(AnimateBlockShuffle(
                block,
                space,
                cellSize,
                fromAnchor,
                toAnchor,
                stagger,
                () => running--));
        }

        float deadline = Time.realtimeSinceStartup + ShuffleMaxDuration + 0.35f + (staggerIndex * ShuffleStaggerSeconds);
        while (running > 0 && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        FinalizeShufflePresentation(board, toAnchors);
    }

    private IEnumerator AnimateBlockShuffle(
        Block block,
        IGridSpace space,
        Vector2 cellSize,
        Vector2Int fromAnchor,
        Vector2Int toAnchor,
        float staggerDelay,
        Action onComplete)
    {
        try
        {
            if (staggerDelay > 0.0001f)
            {
                yield return new WaitForSeconds(staggerDelay);
            }

            int cellCount = Mathf.Max(1, block.CellCount);
            float duration = ResolveShuffleDuration(fromAnchor, toAnchor);
            int pending = 0;

            for (int i = 0; i < cellCount; i++)
            {
                PieceView3D view = block.GetWorldViewForCellIndex(i);
                if (view == null || !view.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector2Int fromCell = fromAnchor + block.GetLocalCell(i);
                Vector2Int toCell = toAnchor + block.GetLocalCell(i);
                pending++;
                StartCoroutine(AnimateCellShuffle(
                    view,
                    space,
                    cellSize,
                    fromCell,
                    toCell,
                    duration,
                    () => pending--));
            }

            float deadline = Time.realtimeSinceStartup + duration + 0.2f;
            while (pending > 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }
        finally
        {
            onComplete?.Invoke();
        }
    }

    private static IEnumerator AnimateCellShuffle(
        PieceView3D view,
        IGridSpace space,
        Vector2 cellSize,
        Vector2Int fromCell,
        Vector2Int toCell,
        float duration,
        Action onComplete)
    {
        WorldPieceMotion motion = view.GetComponent<WorldPieceMotion>();
        bool added = false;
        if (motion == null)
        {
            motion = view.gameObject.AddComponent<WorldPieceMotion>();
            added = true;
        }

        motion.Bind(view, space);
        try
        {
            yield return motion.AnimateShuffleMove(
                space,
                cellSize,
                fromCell,
                toCell,
                duration);
        }
        finally
        {
            if (added && motion != null)
            {
                UnityEngine.Object.Destroy(motion);
            }

            onComplete?.Invoke();
        }
    }

    private static void FinalizeShufflePresentation(BoardManager board, Dictionary<Block, Vector2Int> toAnchors)
    {
        if (toAnchors == null)
        {
            return;
        }

        foreach (KeyValuePair<Block, Vector2Int> entry in toAnchors)
        {
            Block block = entry.Key;
            if (block == null)
            {
                continue;
            }

            block.SetGridPosition(entry.Value, preserveWorldPresentation: true);
        }

        if (board != null)
        {
            board.RebindChildBlockOccupancy();
        }
    }

    private static void ClearShufflePresentation()
    {
        Block[] blocks = FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            ClearBlockShufflePresentation(blocks[i]);
        }
    }

    private static void ClearBlockShufflePresentation(Block block)
    {
        if (block == null)
        {
            return;
        }

        int cellCount = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < cellCount; i++)
        {
            PieceView3D view = block.GetWorldViewForCellIndex(i);
            if (view == null)
            {
                continue;
            }

            view.ClearShufflePresentation();
            if (view.IsMotionLocked)
            {
                view.EndMotionLock();
            }
        }
    }

    private void BeginShufflePresentationHold(
        Dictionary<Block, Vector2Int> fromAnchors,
        Dictionary<Block, Vector2Int> plan)
    {
        shuffleHoldViewsScratch.Clear();
        if (plan == null || fromAnchors == null)
        {
            return;
        }

        foreach (KeyValuePair<Block, Vector2Int> entry in plan)
        {
            Block block = entry.Key;
            if (block == null || !fromAnchors.TryGetValue(block, out Vector2Int fromAnchor))
            {
                continue;
            }

            if (fromAnchor == entry.Value)
            {
                continue;
            }

            int cellCount = Mathf.Max(1, block.CellCount);
            for (int i = 0; i < cellCount; i++)
            {
                PieceView3D view = block.GetWorldViewForCellIndex(i);
                if (view == null || shuffleHoldViewsScratch.Contains(view))
                {
                    continue;
                }

                view.BeginMotionLock();
                shuffleHoldViewsScratch.Add(view);
            }
        }
    }

    private void EndShufflePresentationHold()
    {
        for (int i = 0; i < shuffleHoldViewsScratch.Count; i++)
        {
            PieceView3D view = shuffleHoldViewsScratch[i];
            if (view != null && view.IsMotionLocked)
            {
                view.EndMotionLock();
            }
        }

        shuffleHoldViewsScratch.Clear();
    }

    private static void RestoreShuffleViewsAtFrom(
        IGridSpace space,
        Dictionary<Block, Vector2Int> fromAnchors,
        Dictionary<Block, Vector2Int> plan)
    {
        if (space == null || plan == null || fromAnchors == null)
        {
            return;
        }

        foreach (KeyValuePair<Block, Vector2Int> entry in plan)
        {
            Block block = entry.Key;
            if (block == null || !fromAnchors.TryGetValue(block, out Vector2Int fromAnchor))
            {
                continue;
            }

            if (fromAnchor == entry.Value)
            {
                continue;
            }

            int cellCount = Mathf.Max(1, block.CellCount);
            for (int i = 0; i < cellCount; i++)
            {
                PieceView3D view = block.GetWorldViewForCellIndex(i);
                if (view == null)
                {
                    continue;
                }

                Vector2Int fromCell = fromAnchor + block.GetLocalCell(i);
                view.SnapWorldPresentationToGrid(space, fromCell);
            }
        }
    }

    private static float ResolveShuffleDuration(Vector2Int fromAnchor, Vector2Int toAnchor)
    {
        int manhattan = Mathf.Abs(fromAnchor.x - toAnchor.x) + Mathf.Abs(fromAnchor.y - toAnchor.y);
        float duration = ShuffleMinDuration + manhattan * ShuffleSecondsPerCell;
        return Mathf.Clamp(duration, ShuffleMinDuration, ShuffleMaxDuration);
    }

    private bool IsBoardStableForShuffle(out string reason)
    {
        reason = null;
        if (levelManager != null)
        {
            if (!levelManager.IsGameplayInputAllowed)
            {
                reason = "gameplay input not allowed";
                return false;
            }

            if (levelManager.IsAlignedMatchRunning)
            {
                reason = "aligned match running";
                return false;
            }

            if (!levelManager.IsPieceInputAllowed)
            {
                reason = "piece match sequence running";
                return false;
            }
        }

        BoosterManager manager = FindFirstObjectByType<BoosterManager>();
        if (manager != null && manager.IsAnyBusy)
        {
            HammerBooster hammer = FindFirstObjectByType<HammerBooster>();
            if (hammer != null && hammer.IsBusy)
            {
                reason = "another booster busy";
                return false;
            }

            MagnetBooster magnet = FindFirstObjectByType<MagnetBooster>();
            if (magnet != null && magnet.IsBusy)
            {
                reason = "another booster busy";
                return false;
            }

            if (hammer != null && hammer.IsPresentationActive)
            {
                reason = "hammer presentation active";
                return false;
            }
        }

        Block[] blocks = FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || !block.isActiveAndEnabled || block.IsSettled)
            {
                continue;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            if (mover != null && (mover.IsMoving || mover.IsDragging))
            {
                reason = "block moving";
                return false;
            }
        }

        return true;
    }

    private readonly List<Block> shuffleUnitsScratch = new List<Block>();

    private static void CollectShuffleUnits(BoardManager board, List<Block> results)
    {
        results.Clear();
        if (board == null)
        {
            return;
        }

        board.CollectUniqueBlocks(results);
        for (int i = results.Count - 1; i >= 0; i--)
        {
            if (!IsShuffleEligible(results[i], board))
            {
                results.RemoveAt(i);
            }
        }
    }

    private static bool IsShuffleEligible(Block block, BoardManager board)
    {
        if (block == null || !block.isActiveAndEnabled || block.IsSettled)
        {
            return false;
        }

        BlockMover mover = block.GetComponent<BlockMover>();
        if (mover != null && (mover.IsMoving || mover.IsDragging))
        {
            return false;
        }

        if (board != null && board.IsBlockUnderImpassableCell(block))
        {
            return false;
        }

        return true;
    }

    private static bool TryBuildShufflePlan(
        BoardManager board,
        List<Block> units,
        out Dictionary<Block, Vector2Int> plan)
    {
        plan = null;
        if (board == null || units == null || units.Count == 0)
        {
            return false;
        }

        var blockedCells = new HashSet<Vector2Int>();
        CollectStaticBlockedCells(board, blockedCells);
        CollectOccupiedCellsOutsideUnits(board, units, blockedCells);

        var rng = new System.Random();
        for (int attempt = 0; attempt < MaxPlanAttempts; attempt++)
        {
            var occupied = new HashSet<Vector2Int>(blockedCells);
            var candidate = new Dictionary<Block, Vector2Int>();
            var order = new List<Block>(units);
            ShuffleList(rng, order);

            bool failed = false;
            for (int i = 0; i < order.Count; i++)
            {
                Block block = order[i];
                if (!TryPickAnchor(board, block, occupied, rng, out Vector2Int anchor))
                {
                    failed = true;
                    break;
                }

                AddFootprintCells(block, anchor, occupied);
                candidate[block] = anchor;
            }

            if (failed || IsIdenticalArrangement(candidate))
            {
                continue;
            }

            plan = candidate;
            return true;
        }

        return false;
    }

    private static void CollectStaticBlockedCells(BoardManager board, HashSet<Vector2Int> blocked)
    {
        blocked.Clear();
        int width = board.Width;
        int height = board.Height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (board.IsTargetCell(cell) || board.IsCellImpassable(cell))
                {
                    blocked.Add(cell);
                }
            }
        }
    }

    private static void CollectOccupiedCellsOutsideUnits(
        BoardManager board,
        List<Block> units,
        HashSet<Vector2Int> occupied)
    {
        var unitSet = new HashSet<Block>(units);
        var all = new List<Block>();
        board.CollectUniqueBlocks(all);
        for (int i = 0; i < all.Count; i++)
        {
            Block block = all[i];
            if (block == null || unitSet.Contains(block))
            {
                continue;
            }

            AddFootprintCells(block, block.GridPosition, occupied);
        }
    }

    private static bool TryPickAnchor(
        BoardManager board,
        Block block,
        HashSet<Vector2Int> occupied,
        System.Random rng,
        out Vector2Int anchor)
    {
        anchor = default;
        var candidates = new List<Vector2Int>();
        int width = board.Width;
        int height = board.Height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int candidate = new Vector2Int(x, y);
                if (CanPlaceShuffleUnit(board, block, candidate, occupied))
                {
                    candidates.Add(candidate);
                }
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        anchor = candidates[rng.Next(candidates.Count)];
        return true;
    }

    private static bool CanPlaceShuffleUnit(
        BoardManager board,
        Block block,
        Vector2Int anchor,
        HashSet<Vector2Int> occupied)
    {
        if (block == null || board == null)
        {
            return false;
        }

        if (board.DoesFootprintTouchImpassableCell(block, anchor))
        {
            return false;
        }

        if (board.FootprintTouchesTarget(block, anchor))
        {
            return false;
        }

        if (board.HasNestMatch(block, anchor))
        {
            return false;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = anchor + block.GetLocalCell(i);
            if (!board.IsInsideBoard(cell) || occupied.Contains(cell))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddFootprintCells(Block block, Vector2Int anchor, HashSet<Vector2Int> occupied)
    {
        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            occupied.Add(anchor + block.GetLocalCell(i));
        }
    }

    private static bool IsIdenticalArrangement(Dictionary<Block, Vector2Int> plan)
    {
        foreach (KeyValuePair<Block, Vector2Int> entry in plan)
        {
            Block block = entry.Key;
            if (block == null)
            {
                continue;
            }

            if (block.GridPosition != entry.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static void ShuffleList<T>(System.Random rng, IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static void HideDestinationHighlight()
    {
        BoardPresenter3D presenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter != null)
        {
            BoardCellDestinationHighlight3D.HideImmediate(presenter);
        }
    }

    private BoardManager ResolveBoard()
    {
        return boardManager != null ? boardManager : FindFirstObjectByType<BoardManager>();
    }

    private void SetPhase(ShufflePhase next)
    {
        if (phase == next)
        {
            return;
        }

        phase = next;
        OnPhaseChanged?.Invoke(phase);
        OnStateChanged?.Invoke();
    }

    private void SetCharges(int count)
    {
        int clamped = Mathf.Max(0, count);
        if (shuffleCharges == clamped)
        {
            return;
        }

        shuffleCharges = clamped;
        OnChargesChanged?.Invoke(shuffleCharges);
    }

    private void Log(string message)
    {
        if (debugLog)
        {
            Debug.Log($"[Shuffle] {message}", this);
        }
    }
}
