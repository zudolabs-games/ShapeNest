using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Undo booster: restores the board to the state immediately before the last
/// completed valid player move or successful Shuffle. Magnet and Hammer are not undoable.
/// </summary>
public class UndoBooster : MonoBehaviour, IBooster
{
    public enum UndoPhase
    {
        Idle,
        Executing
    }

    private const float UndoSecondsPerCell = 0.12f;
    private const float UndoMinDuration = 0.18f;
    private const float UndoMaxDuration = 0.55f;
    private const float UndoStaggerSeconds = 0.018f;

    [SerializeField]
    private LevelManager levelManager;

    [SerializeField]
    private BoardManager boardManager;

    [SerializeField]
    private BoardUndoHistory undoHistory;

    [SerializeField]
    [Min(0)]
    [Tooltip("Test inventory. Consumed only after a successful Undo restoration.")]
    private int undoCharges = 3;

    [SerializeField]
    private bool enableKeyboardActivate = true;

    [SerializeField]
    private bool debugLog = true;

    private UndoPhase phase = UndoPhase.Idle;
    private Coroutine undoRoutine;
    private readonly List<PieceView3D> undoHoldViewsScratch = new List<PieceView3D>();

    public UndoPhase Phase => phase;
    public bool IsSelecting => false;
    public bool IsBusy => phase != UndoPhase.Idle;
    public int UndoCharges => undoCharges;

    public BoosterType Type => BoosterType.Undo;

    public BoosterState State =>
        phase == UndoPhase.Executing ? BoosterState.Executing : BoosterState.Idle;

    int IBooster.Charges => undoCharges;

    public bool CanActivate
    {
        get
        {
            if (phase == UndoPhase.Executing)
            {
                return false;
            }

            if (levelManager != null && !levelManager.IsGameplayInputAllowed)
            {
                return false;
            }

            if (undoCharges <= 0)
            {
                return false;
            }

            BoardUndoHistory history = ResolveHistory();
            return history != null && history.HasUndoableSnapshot;
        }
    }

    public event Action<int> OnChargesChanged;
    public event Action<UndoPhase> OnPhaseChanged;
    public event Action OnStateChanged;

    void IBooster.Activate() => ActivateUndo();

    void IBooster.Cancel() { }

    void IBooster.ResetState(string reason) => ResetUndoState(reason);

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

        if (undoHistory == null)
        {
            undoHistory = GetComponent<BoardUndoHistory>();
        }

        if (undoHistory == null)
        {
            undoHistory = FindFirstObjectByType<BoardUndoHistory>();
        }
    }

    private void OnDisable()
    {
        ResetUndoState("disabled");
    }

    public void ResetUndoState(string reason = null)
    {
        if (undoRoutine != null)
        {
            StopCoroutine(undoRoutine);
            undoRoutine = null;
        }

        EndUndoPresentationHold();
        ClearUndoPresentation();

        if (phase != UndoPhase.Idle)
        {
            SetPhase(UndoPhase.Idle);
        }

        if (!string.IsNullOrEmpty(reason))
        {
            Log($"Undo reset: {reason}");
        }
    }

    private void Update()
    {
        if (!enableKeyboardActivate)
        {
            return;
        }

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.uKey.wasPressedThisFrame)
        {
            BoosterManager manager = FindFirstObjectByType<BoosterManager>();
            if (manager != null)
            {
                manager.TryActivate(BoosterType.Undo);
            }
            else
            {
                ActivateUndo();
            }
        }
    }

    [ContextMenu("Activate Undo")]
    public void ActivateUndo()
    {
        TryBeginActivation(out _);
    }

    /// <summary>
    /// Starts Undo using the same gates as <see cref="ActivateUndo"/>.
    /// Returns false with a presentation reason when Undo does not begin.
    /// </summary>
    public bool TryBeginActivation(out BoosterFailureReason failure)
    {
        failure = BoosterFailureReason.None;

        if (phase == UndoPhase.Executing)
        {
            failure = BoosterFailureReason.Busy;
            return false;
        }

        if (levelManager != null && !levelManager.IsGameplayInputAllowed)
        {
            Log("Undo ignored: gameplay input not allowed");
            failure = BoosterFailureReason.Unavailable;
            return false;
        }

        if (undoCharges <= 0)
        {
            Log("Undo ignored: no charges");
            failure = BoosterFailureReason.NoCharges;
            return false;
        }

        BoardUndoHistory history = ResolveHistory();
        if (history == null || !history.HasUndoableSnapshot)
        {
            Log("Undo ignored: no undo history");
            failure = BoosterFailureReason.NoUndoAvailable;
            return false;
        }

        if (!IsBoardStableForUndo(out string busyReason))
        {
            Log($"Undo ignored: {busyReason}");
            failure = BoosterFailureReason.Busy;
            return false;
        }

        if (!history.TryGetActiveSnapshot(out Dictionary<Block, Vector2Int> restoreAnchors, out Dictionary<Block, Vector2Int> fromAnchors))
        {
            Log("Undo ignored: snapshot invalid");
            failure = BoosterFailureReason.NoUndoAvailable;
            return false;
        }

        BoardManager board = ResolveBoard();
        if (board == null)
        {
            Log("Undo ignored: no board");
            failure = BoosterFailureReason.Unavailable;
            return false;
        }

        if (!HasRestorableChange(fromAnchors, restoreAnchors))
        {
            Log("Undo ignored: snapshot matches current layout");
            history.ConsumeActiveSnapshot();
            failure = BoosterFailureReason.NoUndoAvailable;
            return false;
        }

        BeginUndoPresentationHold(fromAnchors, restoreAnchors);
        SetPhase(UndoPhase.Executing);
        HideDestinationHighlight();
        undoRoutine = StartCoroutine(ExecuteUndo(board, history, fromAnchors, restoreAnchors));
        return true;
    }

    public void SetUndoCharges(int count)
    {
        SetCharges(count);
    }

    private IEnumerator ExecuteUndo(
        BoardManager board,
        BoardUndoHistory history,
        Dictionary<Block, Vector2Int> fromAnchors,
        Dictionary<Block, Vector2Int> restoreAnchors)
    {
        bool succeeded = false;
        try
        {
            if (!ApplyLogicalRestore(board, restoreAnchors))
            {
                Log("Undo failed during occupancy apply");
                yield break;
            }

            BoardPresenter3D presenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
            IGridSpace space = presenter != null ? presenter.GridSpace3D : null;
            RestoreUndoViewsAtFrom(space, fromAnchors, restoreAnchors);

            SetCharges(undoCharges - 1);
            history.ConsumeActiveSnapshot();
            succeeded = true;
            Log($"Undo applied ({restoreAnchors.Count} units). Charges left={undoCharges}");

            yield return AnimateUndoMoves(board, fromAnchors, restoreAnchors);
            board.RebindChildBlockOccupancy();
        }
        finally
        {
            EndUndoPresentationHold();
            undoRoutine = null;
            SetPhase(UndoPhase.Idle);
            if (!succeeded)
            {
                Log("Undo did not consume charge");
            }
        }
    }

    private static bool HasRestorableChange(
        Dictionary<Block, Vector2Int> fromAnchors,
        Dictionary<Block, Vector2Int> restoreAnchors)
    {
        foreach (KeyValuePair<Block, Vector2Int> entry in restoreAnchors)
        {
            Block block = entry.Key;
            if (block == null)
            {
                continue;
            }

            if (fromAnchors.TryGetValue(block, out Vector2Int from) && from != entry.Value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ApplyLogicalRestore(BoardManager board, Dictionary<Block, Vector2Int> restoreAnchors)
    {
        if (board == null || restoreAnchors == null || restoreAnchors.Count == 0)
        {
            return false;
        }

        var blocks = new List<Block>(restoreAnchors.Keys);
        for (int i = 0; i < blocks.Count; i++)
        {
            board.UnregisterBlock(blocks[i]);
        }

        foreach (KeyValuePair<Block, Vector2Int> entry in restoreAnchors)
        {
            Block block = entry.Key;
            Vector2Int anchor = entry.Value;
            if (block == null || block.IsSettled)
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

    private IEnumerator AnimateUndoMoves(
        BoardManager board,
        Dictionary<Block, Vector2Int> fromAnchors,
        Dictionary<Block, Vector2Int> restoreAnchors)
    {
        BoardPresenter3D presenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter == null || presenter.GridSpace3D == null)
        {
            FinalizeUndoPresentation(board, restoreAnchors);
            yield break;
        }

        IGridSpace space = presenter.GridSpace3D;
        Vector2 cellSize = Vector2.one * Mathf.Max(0.01f, presenter.CellWorldSize);
        int running = 0;
        int staggerIndex = 0;
        foreach (KeyValuePair<Block, Vector2Int> entry in restoreAnchors)
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
            float stagger = staggerIndex * UndoStaggerSeconds;
            staggerIndex++;
            StartCoroutine(AnimateBlockUndo(
                block,
                space,
                cellSize,
                fromAnchor,
                toAnchor,
                stagger,
                () => running--));
        }

        float deadline = Time.realtimeSinceStartup + UndoMaxDuration + 0.35f + (staggerIndex * UndoStaggerSeconds);
        while (running > 0 && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        FinalizeUndoPresentation(board, restoreAnchors);
    }

    private IEnumerator AnimateBlockUndo(
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
            float duration = ResolveUndoDuration(fromAnchor, toAnchor);
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
                StartCoroutine(AnimateCellUndo(
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

    private static IEnumerator AnimateCellUndo(
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

    private static void FinalizeUndoPresentation(BoardManager board, Dictionary<Block, Vector2Int> restoreAnchors)
    {
        if (restoreAnchors == null)
        {
            return;
        }

        foreach (KeyValuePair<Block, Vector2Int> entry in restoreAnchors)
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

    private static void ClearUndoPresentation()
    {
        Block[] blocks = FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            ClearBlockUndoPresentation(blocks[i]);
        }
    }

    private static void ClearBlockUndoPresentation(Block block)
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

    private void BeginUndoPresentationHold(
        Dictionary<Block, Vector2Int> fromAnchors,
        Dictionary<Block, Vector2Int> restoreAnchors)
    {
        undoHoldViewsScratch.Clear();
        if (restoreAnchors == null || fromAnchors == null)
        {
            return;
        }

        foreach (KeyValuePair<Block, Vector2Int> entry in restoreAnchors)
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
                if (view == null || undoHoldViewsScratch.Contains(view))
                {
                    continue;
                }

                view.BeginMotionLock();
                undoHoldViewsScratch.Add(view);
            }
        }
    }

    private void EndUndoPresentationHold()
    {
        for (int i = 0; i < undoHoldViewsScratch.Count; i++)
        {
            PieceView3D view = undoHoldViewsScratch[i];
            if (view != null && view.IsMotionLocked)
            {
                view.EndMotionLock();
            }
        }

        undoHoldViewsScratch.Clear();
    }

    private static void RestoreUndoViewsAtFrom(
        IGridSpace space,
        Dictionary<Block, Vector2Int> fromAnchors,
        Dictionary<Block, Vector2Int> restoreAnchors)
    {
        if (space == null || restoreAnchors == null || fromAnchors == null)
        {
            return;
        }

        foreach (KeyValuePair<Block, Vector2Int> entry in restoreAnchors)
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

    private static float ResolveUndoDuration(Vector2Int fromAnchor, Vector2Int toAnchor)
    {
        int manhattan = Mathf.Abs(fromAnchor.x - toAnchor.x) + Mathf.Abs(fromAnchor.y - toAnchor.y);
        float duration = UndoMinDuration + manhattan * UndoSecondsPerCell;
        return Mathf.Clamp(duration, UndoMinDuration, UndoMaxDuration);
    }

    private bool IsBoardStableForUndo(out string reason)
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
        if (manager != null && manager.IsAnySelecting)
        {
            reason = "booster selecting";
            return false;
        }

        HammerBooster hammer = FindFirstObjectByType<HammerBooster>();
        if (hammer != null && hammer.IsBusy)
        {
            reason = "hammer busy";
            return false;
        }

        MagnetBooster magnet = FindFirstObjectByType<MagnetBooster>();
        if (magnet != null && magnet.IsBusy)
        {
            reason = "magnet busy";
            return false;
        }

        ShuffleBooster shuffle = FindFirstObjectByType<ShuffleBooster>();
        if (shuffle != null && shuffle.IsBusy)
        {
            reason = "shuffle busy";
            return false;
        }

        if (hammer != null && hammer.IsPresentationActive)
        {
            reason = "hammer presentation active";
            return false;
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

    private BoardUndoHistory ResolveHistory()
    {
        if (undoHistory == null)
        {
            undoHistory = BoardUndoHistory.Resolve();
        }

        return undoHistory;
    }

    private BoardManager ResolveBoard()
    {
        return boardManager != null ? boardManager : FindFirstObjectByType<BoardManager>();
    }

    private static void HideDestinationHighlight()
    {
        BoardPresenter3D presenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter != null)
        {
            BoardCellDestinationHighlight3D.HideImmediate(presenter);
        }
    }

    private void SetPhase(UndoPhase next)
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
        if (undoCharges == clamped)
        {
            return;
        }

        undoCharges = clamped;
        OnChargesChanged?.Invoke(undoCharges);
    }

    private void Log(string message)
    {
        if (debugLog)
        {
            Debug.Log($"[Undo] {message}", this);
        }
    }
}
