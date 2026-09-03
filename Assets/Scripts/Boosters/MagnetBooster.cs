using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Additive Magnet booster. 1-cell blocks: plan a soft-obstacle route to a matching
/// nest and drive existing BlockMover hops until match. Multi-cell chains: split into
/// independent 1-cell Blocks with existing SpawnSplitBlock, then start every remnant's
/// Magnet BlockMover journey in the same execution window so pieces travel together.
/// Other movable blocks are soft obstacles (temporarily cleared from occupancy for hops
/// only). Hard gates remain: fixed direction, Ice, closed shutters, board bounds.
/// One charge per successful Magnet completion (not per chain cell).
/// </summary>
public class MagnetBooster : MonoBehaviour, IBooster
{
    public enum MagnetPhase
    {
        Idle,
        Selecting,
        Executing
    }

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    [SerializeField]
    private LevelManager levelManager;

    [SerializeField]
    private BoardManager boardManager;

    [SerializeField]
    [Min(0)]
    [Tooltip("Test inventory. Consumed only after a successful Magnet match.")]
    private int magnetCharges = 3;

    [SerializeField]
    private bool enableKeyboardActivate = true;

    [SerializeField]
    private bool debugLog = true;

    private MagnetPhase phase = MagnetPhase.Idle;
    private Coroutine pullRoutine;
    private readonly List<Coroutine> chainPullRoutines = new List<Coroutine>();
    private readonly List<Block> activeMagnetCohort = new List<Block>();
    private Block highlightedBlock;
    private readonly List<PieceView3D> selectionViews = new List<PieceView3D>();
    private Sequence selectionPulse;
    private bool overlayHideImmediate;

    private const float SelectionPulsePeak = 1.03f;
    private const float SelectionPulseCycle = 0.65f;

    public MagnetPhase Phase => phase;
    public bool IsSelecting => phase == MagnetPhase.Selecting;
    public bool IsBusy => phase != MagnetPhase.Idle;
    public int MagnetCharges => magnetCharges;

    public BoosterType Type => BoosterType.Magnet;

    public BoosterState State
    {
        get
        {
            switch (phase)
            {
                case MagnetPhase.Selecting:
                    return BoosterState.Selecting;
                case MagnetPhase.Executing:
                    return BoosterState.Executing;
                default:
                    return BoosterState.Idle;
            }
        }
    }

    int IBooster.Charges => magnetCharges;

    public bool CanActivate
    {
        get
        {
            if (phase == MagnetPhase.Executing)
            {
                return false;
            }

            if (phase == MagnetPhase.Selecting)
            {
                return true;
            }

            if (levelManager != null && !levelManager.IsGameplayInputAllowed)
            {
                return false;
            }

            return magnetCharges > 0;
        }
    }

    /// <summary>Fired when charge count changes (UI sync).</summary>
    public event Action<int> OnChargesChanged;

    /// <summary>Fired when Magnet phase changes (Idle/Selecting/Executing).</summary>
    public event Action<MagnetPhase> OnPhaseChanged;

    public event Action OnStateChanged;

    void IBooster.Activate() => ActivateMagnet();

    void IBooster.Cancel() => CancelMagnet();

    void IBooster.ResetState(string reason) => ResetMagnetState(reason);

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
        ResetMagnetState("disabled");
    }

    /// <summary>
    /// Clears selection/execution after level load or restart.
    /// Does not change charge inventory.
    /// </summary>
    public void ResetMagnetState(string reason = null)
    {
        if (pullRoutine != null)
        {
            StopCoroutine(pullRoutine);
            pullRoutine = null;
        }

        StopChainPullRoutines();
        activeMagnetCohort.Clear();
        ClearMagnetPresentation();
        ClearAllMagnetSelectionPresentation();
        ClearHighlight();
        overlayHideImmediate = true;
        if (phase != MagnetPhase.Idle)
        {
            SetPhase(MagnetPhase.Idle);
        }
        else
        {
            BoosterSelectionOverlay.HideExisting(true);
        }

        overlayHideImmediate = false;

        if (!string.IsNullOrEmpty(reason))
        {
            Log($"Magnet reset: {reason}");
        }
    }

    private void Update()
    {
        if (!enableKeyboardActivate)
        {
            return;
        }

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
        {
            ToggleMagnet();
        }
    }

    /// <summary>Test/UI entry point. Toggles selection mode when idle.</summary>
    [ContextMenu("Activate Magnet")]
    public void ActivateMagnet()
    {
        TryBeginActivation(out _);
    }

    /// <summary>
    /// Begins Magnet selection using the same gates as <see cref="ActivateMagnet"/>.
    /// Returns false with a presentation reason when selection does not start.
    /// Cancel-while-selecting counts as success (no failure reason).
    /// </summary>
    public bool TryBeginActivation(out BoosterFailureReason failure)
    {
        failure = BoosterFailureReason.None;

        if (phase == MagnetPhase.Executing)
        {
            failure = BoosterFailureReason.Busy;
            return false;
        }

        if (phase == MagnetPhase.Selecting)
        {
            CancelMagnet("Cancelled");
            return true;
        }

        if (levelManager != null && !levelManager.IsGameplayInputAllowed)
        {
            Log("Magnet ignored: gameplay input not allowed");
            failure = BoosterFailureReason.Unavailable;
            return false;
        }

        if (magnetCharges <= 0)
        {
            Log("Magnet ignored: no charges");
            failure = BoosterFailureReason.NoCharges;
            return false;
        }

        if (!HasAnyMagnetEligibleBlock())
        {
            Log("Magnet ignored: no eligible blocks");
            failure = BoosterFailureReason.NoValidTarget;
            return false;
        }

        SetPhase(MagnetPhase.Selecting);
        Log($"Magnet selecting (charges={magnetCharges}). Tap a block.");
        return true;
    }

    public void ToggleMagnet()
    {
        ActivateMagnet();
    }

    public void CancelMagnet(string reason = null)
    {
        if (phase == MagnetPhase.Executing)
        {
            return;
        }

        ClearHighlight();
        StopSelectionPresentation();
        SetPhase(MagnetPhase.Idle);
        if (!string.IsNullOrEmpty(reason))
        {
            Log($"Magnet cancelled: {reason}");
        }
    }

    /// <summary>
    /// Called by InputManager while selecting. Returns true if the press was consumed.
    /// </summary>
    public bool TryHandleSelectionPress(Block block)
    {
        if (phase != MagnetPhase.Selecting)
        {
            return false;
        }

        if (block == null)
        {
            Log("Magnet: tap a block to pull");
            return true;
        }

        TryUseMagnetOnBlock(block);
        return true;
    }

    public bool TryUseMagnetOnBlock(Block block)
    {
        if (phase != MagnetPhase.Selecting || pullRoutine != null)
        {
            return false;
        }

        if (!TryBuildMagnetPlan(block, out _, out string failReason))
        {
            Log($"Magnet failed: {failReason}");
            block.PlayInvalidInteractionFeedback();
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Magnet, BoosterFailureReason.InvalidTarget);
            // Stay in selecting mode so the player can try another block.
            return false;
        }

        bool resolveChain = block.CellCount > 1;
        if (resolveChain && block.IsFrozen)
        {
            Log("Magnet failed: block frozen by Ice");
            block.PlayInvalidInteractionFeedback();
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Magnet, BoosterFailureReason.InvalidTarget);
            return false;
        }

        if (resolveChain && !CanFullyResolveChain(block, out string chainFail))
        {
            Log($"Magnet failed: {chainFail}");
            block.PlayInvalidInteractionFeedback();
            BoosterFeedbackMessage.NotifyFailure(BoosterType.Magnet, BoosterFailureReason.InvalidTarget);
            return false;
        }

        ClearHighlight();
        highlightedBlock = block;
        block.ShowDragSelection();
        SetPhase(MagnetPhase.Executing);
        PlaySelectionConfirm(block);
        pullRoutine = resolveChain
            ? StartCoroutine(ExecuteMagnetChainResolution(block))
            : StartCoroutine(ExecuteMagnetJourney(block));
        return true;
    }

    /// <summary>True if Magnet could legally pull this block right now.</summary>
    public bool CanMagnetPull(Block block) => IsMagnetEligibleVisual(block);

    /// <summary>
    /// Presentation-only eligibility. Mirrors TryUseMagnetOnBlock validation
    /// (route plan + chain resolution) without changing gameplay.
    /// </summary>
    public bool IsMagnetEligibleVisual(PieceView3D view)
    {
        if (view == null || view.ConfiguredAsNest)
        {
            return false;
        }

        return IsMagnetEligibleVisual(view.SourceBlock);
    }

    /// <summary>
    /// True when the existing Magnet activation path would accept this block.
    /// </summary>
    public bool IsMagnetEligibleVisual(Block block)
    {
        if (block == null || !block.isActiveAndEnabled || block.IsSettled)
        {
            return false;
        }

        if (!TryBuildMagnetPlan(block, out _, out _))
        {
            return false;
        }

        if (block.CellCount > 1)
        {
            if (block.IsFrozen)
            {
                return false;
            }

            if (!CanFullyResolveChain(block, out _))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasAnyMagnetEligibleBlock()
    {
        Block[] blocks = FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            if (IsMagnetEligibleVisual(blocks[i]))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryBuildMagnetPlan(Block block, out MagnetPlan plan, out string failReason)
    {
        plan = default;
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
        if (mover == null || mover.IsMoving || mover.IsDragging)
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

        if (!TryFindPathTowardMatchingNest(board, block, mover, out List<Vector2Int> path, out _))
        {
            failReason = "no legal route toward a matching nest";
            return false;
        }

        if (!TryBuildPlanFromPath(board, block, mover, path, out plan))
        {
            failReason = "route found but no executable first move";
            return false;
        }

        return true;
    }

    /// <summary>
    /// BFS toward a matching nest. Other movable blocks are SOFT obstacles (ignored for
    /// routing). Hard gates: fixed direction, board bounds, closed shutters, non-matching nests.
    /// </summary>
    private static bool TryFindPathTowardMatchingNest(
        BoardManager board,
        Block block,
        BlockMover mover,
        out List<Vector2Int> path,
        out bool pathEndsInNestEntry)
    {
        path = null;
        pathEndsInNestEntry = false;

        Vector2Int origin = block.GridPosition;
        if (IsMagnetGoalCell(board, block, origin))
        {
            path = new List<Vector2Int> { origin };
            pathEndsInNestEntry = false;
            return true;
        }

        int capacity = Mathf.Max(16, board.Width * board.Height);
        var visited = new HashSet<Vector2Int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>(capacity);
        var queue = new Queue<Vector2Int>(capacity);
        visited.Add(origin);
        queue.Enqueue(origin);

        Vector2Int goal = origin;
        bool found = false;

        while (queue.Count > 0)
        {
            Vector2Int pos = queue.Dequeue();

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int direction = CardinalDirections[i];
                if (!mover.IsDirectionAllowed(direction))
                {
                    continue;
                }

                Vector2Int next = pos + direction;

                // Matching nest under next anchor (soft: ignore other blocks on the nest).
                if (HasMagnetNestMatchSoft(board, block, next))
                {
                    cameFrom[next] = pos;
                    goal = next;
                    pathEndsInNestEntry = true;
                    found = true;
                    queue.Clear();
                    break;
                }

                if (!CanMagnetSoftHopInto(board, block, next) || visited.Contains(next))
                {
                    continue;
                }

                visited.Add(next);
                cameFrom[next] = pos;

                if (IsMagnetGoalCell(board, block, next))
                {
                    goal = next;
                    pathEndsInNestEntry = false;
                    found = true;
                    queue.Clear();
                    break;
                }

                queue.Enqueue(next);
            }
        }

        if (!found)
        {
            return false;
        }

        path = ReconstructPath(origin, goal, cameFrom);
        return path != null && path.Count > 0;
    }

    private static bool TryBuildPlanFromPath(
        BoardManager board,
        Block block,
        BlockMover mover,
        List<Vector2Int> path,
        out MagnetPlan plan)
    {
        plan = default;
        Vector2Int origin = block.GridPosition;
        if (path == null || path.Count == 0 || path[0] != origin)
        {
            return false;
        }

        // Already at an adjacent/occupying goal: nudge one allowed step so DragRoutine
        // sees remainingSteps > 0 and can trigger the existing adjacent-match path.
        if (path.Count == 1)
        {
            if (!TryFindMatchNudgeDirection(board, block, mover, origin, out Vector2Int nudge))
            {
                return false;
            }

            plan = new MagnetPlan
            {
                direction = nudge,
                requestCell = origin + nudge,
                hopsBeforeMatch = 0
            };
            return true;
        }

        Vector2Int firstDir = path[1] - path[0];
        if (firstDir == Vector2Int.zero || !mover.IsDirectionAllowed(firstDir))
        {
            return false;
        }

        // Collapse the first straight segment; BlockMover may multi-hop along it.
        int endIndex = 1;
        while (endIndex + 1 < path.Count && path[endIndex + 1] - path[endIndex] == firstDir)
        {
            endIndex++;
        }

        Vector2Int requestCell = path[endIndex];
        plan = new MagnetPlan
        {
            direction = firstDir,
            requestCell = requestCell,
            hopsBeforeMatch = endIndex
        };
        return true;
    }

    private static bool TryFindMatchNudgeDirection(
        BoardManager board,
        Block block,
        BlockMover mover,
        Vector2Int origin,
        out Vector2Int direction)
    {
        direction = Vector2Int.zero;

        // Prefer the cardinal that enters the matching nest cell when possible.
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            Vector2Int dir = CardinalDirections[i];
            if (!mover.IsDirectionAllowed(dir))
            {
                continue;
            }

            if (board.HasNestMatch(block, origin + dir)
                || HasMagnetNestMatchSoft(board, block, origin + dir))
            {
                direction = dir;
                return true;
            }
        }

        // Otherwise any allowed direction works: DragRoutine checks adjacent match
        // when remainingSteps > 0 after BeginDrag.
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            Vector2Int dir = CardinalDirections[i];
            if (!mover.IsDirectionAllowed(dir))
            {
                continue;
            }

            direction = dir;
            return true;
        }

        return false;
    }

    private static List<Vector2Int> ReconstructPath(
        Vector2Int origin,
        Vector2Int goal,
        Dictionary<Vector2Int, Vector2Int> cameFrom)
    {
        var path = new List<Vector2Int>();
        Vector2Int current = goal;
        path.Add(current);
        while (current != origin)
        {
            if (!cameFrom.TryGetValue(current, out Vector2Int parent))
            {
                return null;
            }

            current = parent;
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private static bool IsMagnetGoalCell(BoardManager board, Block block, Vector2Int anchor)
    {
        if (HasMagnetNestMatchSoft(board, block, anchor))
        {
            return true;
        }

        return HasAdjacentMatchingNestSoft(board, block, anchor);
    }

    /// <summary>
    /// Soft hop: board bounds + closed shutters are hard. Other blocks are ignored.
    /// Non-matching target cells remain blocked (matching nests are handled as entry edges).
    /// </summary>
    private static bool CanMagnetSoftHopInto(BoardManager board, Block block, Vector2Int nextAnchor)
    {
        if (!IsMagnetSoftFootprintValid(board, block, nextAnchor))
        {
            return false;
        }

        if (HasMagnetNestMatchSoft(board, block, nextAnchor))
        {
            return false;
        }

        return !board.FootprintTouchesTarget(block, nextAnchor);
    }

    private static bool IsMagnetSoftFootprintValid(BoardManager board, Block block, Vector2Int toAnchor)
    {
        if (block == null || board == null)
        {
            return false;
        }

        if (board.DoesFootprintTouchImpassableCell(block, toAnchor))
        {
            return false;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            if (!board.IsInsideBoard(toAnchor + block.GetLocalCell(i)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Like BoardManager.HasNestMatch, but ignores other movable blocks (soft obstacles).
    /// </summary>
    private static bool HasMagnetNestMatchSoft(BoardManager board, Block block, Vector2Int proposedAnchor)
    {
        if (!IsMagnetSoftFootprintValid(board, block, proposedAnchor))
        {
            return false;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            Target target = board.GetTargetAt(proposedAnchor + block.GetLocalCell(i));
            if (target == null)
            {
                continue;
            }

            if (ShapeMatch.AreMatchingLayers(
                    target.GetRequiredIdentityAtWorld(proposedAnchor + block.GetLocalCell(i)),
                    block.GetActiveIdentity(i)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAdjacentMatchingNestSoft(BoardManager board, Block block, Vector2Int anchor)
    {
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            Vector2Int candidate = anchor + CardinalDirections[i];
            if (HasMagnetNestMatchSoft(board, block, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static int MagnetAxisSteps(Vector2Int delta, Vector2Int direction)
    {
        if (direction.x != 0)
        {
            return direction.x > 0 ? delta.x : -delta.x;
        }

        if (direction.y != 0)
        {
            return direction.y > 0 ? delta.y : -delta.y;
        }

        return 0;
    }

    /// <summary>
    /// Temporarily unregisters other blocks along a Magnet segment so BlockMover can hop.
    /// Soft blocks are not moved; occupancy is restored after the segment.
    /// </summary>
    private List<Block> SuspendSoftBlocksForSegment(
        BoardManager board,
        Block magnetBlock,
        Vector2Int origin,
        Vector2Int direction,
        Vector2Int requestCell)
    {
        var suspended = new List<Block>();
        if (board == null || magnetBlock == null || direction == Vector2Int.zero)
        {
            return suspended;
        }

        var seen = new HashSet<Block>();
        int targetSteps = Mathf.Max(1, MagnetAxisSteps(requestCell - origin, direction));
        Vector2Int cursor = origin;

        for (int step = 1; step <= targetSteps; step++)
        {
            cursor = origin + direction * step;
            CollectSoftOccupantsAtAnchor(board, magnetBlock, cursor, seen, suspended);
        }

        for (int i = 0; i < suspended.Count; i++)
        {
            board.UnregisterBlock(suspended[i]);
        }

        return suspended;
    }

    private void CollectSoftOccupantsAtAnchor(
        BoardManager board,
        Block magnetBlock,
        Vector2Int anchor,
        HashSet<Block> seen,
        List<Block> destination)
    {
        int count = Mathf.Max(1, magnetBlock.CellCount);
        for (int i = 0; i < count; i++)
        {
            Block occupant = board.GetBlockAt(anchor + magnetBlock.GetLocalCell(i));
            if (occupant == null || occupant == magnetBlock || !seen.Add(occupant))
            {
                continue;
            }

            if (IsMagnetCohortMember(occupant))
            {
                continue;
            }

            BlockMover occupantMover = occupant.GetComponent<BlockMover>();
            if (occupantMover != null && (occupantMover.IsDragging || occupantMover.IsMoving))
            {
                continue;
            }

            destination.Add(occupant);
        }
    }

    private static void RestoreSoftBlocks(BoardManager board, Block magnetBlock, List<Block> suspended)
    {
        if (board == null || suspended == null)
        {
            return;
        }

        for (int i = 0; i < suspended.Count; i++)
        {
            Block soft = suspended[i];
            if (soft == null || !soft || soft.IsSettled || !soft.isActiveAndEnabled)
            {
                continue;
            }

            // Skip if the magnet block still occupies any of those cells.
            if (magnetBlock != null && magnetBlock && FootprintsOverlap(magnetBlock, soft))
            {
                continue;
            }

            board.TryRegisterBlock(soft, soft.GridPosition);
        }
    }

    private static bool FootprintsOverlap(Block a, Block b)
    {
        int countA = Mathf.Max(1, a.CellCount);
        int countB = Mathf.Max(1, b.CellCount);
        for (int i = 0; i < countA; i++)
        {
            Vector2Int cellA = a.GridPosition + a.GetLocalCell(i);
            for (int j = 0; j < countB; j++)
            {
                if (cellA == b.GridPosition + b.GetLocalCell(j))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private IEnumerator ExecuteMagnetJourney(Block block)
    {
        bool returnToSelecting = false;
        try
        {
            bool[] matched = new bool[1];
            bool[] aborted = new bool[1];
            yield return ExecuteMagnetPullCore(block, matched, aborted, false);
            if (matched[0])
            {
                SetCharges(magnetCharges - 1);
                Log($"Magnet journey complete (match). Charges left={magnetCharges}");
            }
            else
            {
                Log(aborted[0]
                    ? "Magnet journey failed (not consumed)"
                    : "Magnet journey ended without match (not consumed)");
                returnToSelecting = magnetCharges > 0;
            }
        }
        finally
        {
            FinishMagnetExecution(block, returnToSelecting);
        }
    }

    /// <summary>
    /// Approach A (Magnet only; normal drag is unchanged):
    /// 1. Precheck CanFullyResolveChain (unchanged from Phase 49B).
    /// 2. Explode the selected chain into independent 1-cell gameplay Blocks at the
    ///    current world cells using RebuildFromRemaining + SpawnSplitBlock.
    ///    Occupancy stays on the same cells; connectors drop because CellCount becomes 1.
    /// 3. Start every remnant's existing Magnet BlockMover journey in the same
    ///    execution window (sibling coroutines). Pieces do not wait for another
    ///    piece's match before their first hop.
    /// 4. Nested leftovers (inner consumed, outer remains) start together in the
    ///    next wave after gameplay is idle.
    /// 5. Consume exactly one Magnet charge when no descendants remain.
    /// </summary>
    private IEnumerator ExecuteMagnetChainResolution(Block root)
    {
        bool returnToSelecting = false;
        try
        {
            HashSet<int> foreignIds = SnapshotForeignBlockIds(root);
            ClearHighlight();
            if (!TryExplodeMagnetChain(root))
            {
                Log("Magnet chain stopped: could not split into independent pieces");
                returnToSelecting = magnetCharges > 0;
                yield break;
            }

            // One presentation frame so extras/connectors rebuild for 1-cell Blocks
            // before hops start. All journeys still begin in the following window.
            yield return null;
            if (boardManager != null)
            {
                boardManager.RebindChildBlockOccupancy();
            }

            const int maxWaves = 12;
            for (int wave = 0; wave < maxWaves; wave++)
            {
                if (wave > 0)
                {
                    yield return WaitForMagnetGameplayIdle();
                    if (boardManager != null)
                    {
                        boardManager.RebindChildBlockOccupancy();
                    }
                }

                List<Block> pieces = CollectChainDescendants(foreignIds);
                if (pieces.Count == 0)
                {
                    SetCharges(magnetCharges - 1);
                    Log($"Magnet chain resolved. Charges left={magnetCharges}");
                    yield break;
                }

                yield return ExecuteMagnetPullsTogether(pieces);
            }

            Log("Magnet chain stopped: resolution loop limit");
            returnToSelecting = magnetCharges > 0;
        }
        finally
        {
            StopChainPullRoutines();
            activeMagnetCohort.Clear();
            FinishMagnetExecution(root, returnToSelecting);
        }
    }

    private IEnumerator ExecuteMagnetPullCore(Block block, bool[] matched, bool[] aborted, bool requireOwnPieceConsume)
    {
        matched[0] = false;
        aborted[0] = false;
        BlockMover mover = block != null ? block.GetComponent<BlockMover>() : null;
        int matchesBefore = levelManager != null ? levelManager.SuccessfulMatchCount : 0;
        int cellsBefore = block != null ? Mathf.Max(1, block.CellCount) : 0;
        int layersBefore = block != null
            ? ShapeLayout.TotalLayers(
                CollectBlockCells(block),
                block.ShapeType,
                block.Composition,
                block.OuterShape)
            : 0;
        bool anySegmentMoved = false;
        int stallRetries = 0;

        if (block == null || mover == null)
        {
            aborted[0] = true;
            yield break;
        }

        mover.SetMagnetPresenting(true);
        try
        {
            BoardManager board = boardManager != null ? boardManager : block.Board;
            int maxSegments = board != null
                ? Mathf.Max(8, board.Width * board.Height + 4)
                : 32;

        for (int segment = 0; segment < maxSegments; segment++)
        {
            if (block == null || !block || !block.isActiveAndEnabled || block.IsSettled)
            {
                break;
            }

            if (HasMagnetPullSucceeded(
                block,
                matchesBefore,
                cellsBefore,
                layersBefore,
                requireOwnPieceConsume))
            {
                break;
            }

            MagnetPlan plan = default;
            string failReason = null;
            float planDeadline = Time.realtimeSinceStartup + 4f;
            bool planned = false;
            while (Time.realtimeSinceStartup < planDeadline)
            {
                if (HasMagnetPullSucceeded(
                    block,
                    matchesBefore,
                    cellsBefore,
                    layersBefore,
                    requireOwnPieceConsume))
                {
                    planned = false;
                    break;
                }

                if (TryBuildMagnetPlan(block, out plan, out failReason))
                {
                    planned = true;
                    break;
                }

                if (!ShouldRetryMagnetPlan(failReason))
                {
                    break;
                }

                yield return null;
            }

            if (HasMagnetPullSucceeded(
                block,
                matchesBefore,
                cellsBefore,
                layersBefore,
                requireOwnPieceConsume))
            {
                break;
            }

            if (!planned)
            {
                Log(anySegmentMoved
                    ? $"Magnet stopped mid-journey: {failReason}"
                    : $"Magnet failed before first move: {failReason}");
                aborted[0] = true;
                yield break;
            }

            Vector2Int posBefore = block.GridPosition;
            yield return ExecuteMagnetSegment(block, mover, plan);

            if (block == null || !block || !block.isActiveAndEnabled || block.IsSettled)
            {
                break;
            }

            if (block.GridPosition != posBefore)
            {
                anySegmentMoved = true;
                stallRetries = 0;
            }
            else if (!HasMagnetPullSucceeded(
                block,
                matchesBefore,
                cellsBefore,
                layersBefore,
                requireOwnPieceConsume))
            {
                if (IsMagnetCohortBusy(block) && stallRetries < 120)
                {
                    stallRetries++;
                    yield return null;
                    segment--;
                    continue;
                }

                Log("Magnet stopped: segment produced no movement");
                aborted[0] = true;
                yield break;
            }

            if (HasMagnetPullSucceeded(
                block,
                matchesBefore,
                cellsBefore,
                layersBefore,
                requireOwnPieceConsume))
            {
                break;
            }
        }

        matched[0] = HasMagnetPullSucceeded(
            block,
            matchesBefore,
            cellsBefore,
            layersBefore,
            requireOwnPieceConsume);
        if (!matched[0])
        {
            aborted[0] = !anySegmentMoved;
        }
        }
        finally
        {
            if (mover != null)
            {
                mover.SetMagnetPresenting(false);
            }
        }
    }

    private void FinishMagnetExecution(Block block, bool returnToSelecting)
    {
        BoardManager board = boardManager != null
            ? boardManager
            : (block != null ? block.Board : null);
        if (board != null)
        {
            board.RebindChildBlockOccupancy();
        }

        ClearHighlight();
        pullRoutine = null;
        bool canReselect = returnToSelecting && magnetCharges > 0 && HasAnyMagnetEligibleBlock();
        SetPhase(canReselect ? MagnetPhase.Selecting : MagnetPhase.Idle);
    }

    private IEnumerator WaitForMagnetGameplayIdle()
    {
        float deadline = Time.realtimeSinceStartup + 8f;
        while (levelManager != null && Time.realtimeSinceStartup < deadline)
        {
            bool alignedBusy = levelManager.IsAlignedMatchRunning;
            bool pieceBusy = !levelManager.IsPieceInputAllowed;
            if (!alignedBusy && !pieceBusy)
            {
                yield break;
            }

            yield return null;
        }
    }

    private HashSet<int> SnapshotForeignBlockIds(Block root)
    {
        var foreign = new HashSet<int>();
        Block[] all = FindObjectsByType<Block>(FindObjectsSortMode.None);
        int rootId = root != null ? root.GetInstanceID() : 0;
        for (int i = 0; i < all.Length; i++)
        {
            Block b = all[i];
            if (b == null)
            {
                continue;
            }

            int id = b.GetInstanceID();
            if (id != rootId)
            {
                foreign.Add(id);
            }
        }

        return foreign;
    }

    private List<Block> CollectChainDescendants(HashSet<int> foreignIds)
    {
        var pieces = new List<Block>();
        Block[] scratch = FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < scratch.Length; i++)
        {
            Block b = scratch[i];
            if (b == null || b.IsSettled || !b.isActiveAndEnabled)
            {
                continue;
            }

            if (foreignIds != null && foreignIds.Contains(b.GetInstanceID()))
            {
                continue;
            }

            pieces.Add(b);
        }

        return pieces;
    }

    /// <summary>
    /// Magnet-only: turn one multi-cell Block into independent 1-cell Blocks occupying
    /// the same world cells. Uses existing RebuildFromRemaining + SpawnSplitBlock.
    /// Normal player drag never calls this.
    /// </summary>
    private bool TryExplodeMagnetChain(Block root)
    {
        if (root == null || root.CellCount <= 1)
        {
            return true;
        }

        if (levelManager == null)
        {
            return false;
        }

        BoardManager board = boardManager != null ? boardManager : root.Board;
        if (board == null)
        {
            return false;
        }

        int count = root.CellCount;
        var worlds = new Vector2Int[count];
        var cells = new ShapeCellData[count];
        int anchorIndex = root.AnchorCellIndex;
        if (anchorIndex < 0 || anchorIndex >= count)
        {
            anchorIndex = 0;
        }

        for (int i = 0; i < count; i++)
        {
            worlds[i] = root.GridPosition + root.GetLocalCell(i);
            ShapeCellData source = root.GetCell(i);
            cells[i] = new ShapeCellData
            {
                localPosition = Vector2Int.zero,
                shapeType = source != null ? source.shapeType : root.GetActiveShape(i),
                innerShapes = source != null
                    ? ShapeLayout.CloneInners(source.innerShapes)
                    : new List<ShapeType>()
            };
        }

        board.UnregisterBlock(root);
        var primary = new List<ShapeCellData> { cells[anchorIndex] };
        root.RebuildFromRemaining(primary, worlds[anchorIndex]);
        board.UnregisterBlock(root);
        if (!board.TryRegisterBlock(root, worlds[anchorIndex]))
        {
            Log("Magnet explode failed: could not re-register primary cell");
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (i == anchorIndex)
            {
                continue;
            }

            var one = new List<ShapeCellData> { cells[i] };
            Block spawned = levelManager.SpawnSplitBlock(root, one, worlds[i]);
            if (spawned == null)
            {
                Log("Magnet explode failed: SpawnSplitBlock returned null");
                return false;
            }

            board.UnregisterBlock(spawned);
            board.TryRegisterBlock(spawned, worlds[i]);
        }

        Log($"Magnet split chain into {count} independent pieces");
        return true;
    }

    private IEnumerator ExecuteMagnetPullsTogether(List<Block> pieces)
    {
        StopChainPullRoutines();
        activeMagnetCohort.Clear();
        if (pieces == null || pieces.Count == 0)
        {
            yield break;
        }

        int n = pieces.Count;
        bool[] done = new bool[n];
        bool[] matched = new bool[n];
        bool[] aborted = new bool[n];
        for (int i = 0; i < n; i++)
        {
            activeMagnetCohort.Add(pieces[i]);
        }

        for (int i = 0; i < n; i++)
        {
            Coroutine routine = StartCoroutine(
                ExecuteMagnetPullAndMark(pieces[i], matched, aborted, done, i));
            chainPullRoutines.Add(routine);
        }

        float deadline = Time.realtimeSinceStartup + 30f;
        bool allDone = false;
        while (!allDone && Time.realtimeSinceStartup < deadline)
        {
            allDone = true;
            for (int i = 0; i < n; i++)
            {
                if (!done[i])
                {
                    allDone = false;
                    break;
                }
            }

            if (allDone)
            {
                break;
            }

            yield return null;
        }

        StopChainPullRoutines();
        activeMagnetCohort.Clear();
    }

    private IEnumerator ExecuteMagnetPullAndMark(
        Block block,
        bool[] matched,
        bool[] aborted,
        bool[] done,
        int index)
    {
        bool[] oneMatched = new bool[1];
        bool[] oneAborted = new bool[1];
        yield return ExecuteMagnetPullCore(block, oneMatched, oneAborted, true);
        matched[index] = oneMatched[0];
        aborted[index] = oneAborted[0];
        done[index] = true;
    }

    private void StopChainPullRoutines()
    {
        for (int i = 0; i < chainPullRoutines.Count; i++)
        {
            if (chainPullRoutines[i] != null)
            {
                StopCoroutine(chainPullRoutines[i]);
            }
        }

        chainPullRoutines.Clear();
    }

    private bool IsMagnetCohortMember(Block block)
    {
        if (block == null)
        {
            return false;
        }

        for (int i = 0; i < activeMagnetCohort.Count; i++)
        {
            if (activeMagnetCohort[i] == block)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsMagnetCohortBusy(Block except)
    {
        for (int i = 0; i < activeMagnetCohort.Count; i++)
        {
            Block b = activeMagnetCohort[i];
            if (b == null || b == except || !b || b.IsSettled)
            {
                continue;
            }

            BlockMover mover = b.GetComponent<BlockMover>();
            if (mover != null && (mover.IsDragging || mover.IsMoving))
            {
                return true;
            }
        }

        return levelManager != null
            && (levelManager.IsAlignedMatchRunning || !levelManager.IsPieceInputAllowed);
    }

    private static bool ShouldRetryMagnetPlan(string failReason)
    {
        if (string.IsNullOrEmpty(failReason))
        {
            return false;
        }

        return failReason.IndexOf("input", StringComparison.OrdinalIgnoreCase) >= 0
            || failReason.IndexOf("busy", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool CanFullyResolveChain(Block block, out string failReason)
    {
        failReason = null;
        if (block == null)
        {
            failReason = "invalid block";
            return false;
        }

        BoardManager board = boardManager != null ? boardManager : block.Board;
        if (board == null)
        {
            failReason = "no board";
            return false;
        }

        Dictionary<Vector2Int, List<MatchIdentity>> simLayers = SnapshotSimNestLayers(board);
        var needed = new List<MatchIdentity>();
        ShapeLayout.CollectResolvableIdentities(
            CollectBlockCells(block),
            block.ShapeType,
            block.Composition,
            block.OuterShape,
            needed);
        if (!SimNestCoverageAllows(needed, simLayers))
        {
            failReason = "not every chain cell has a matching nest";
            return false;
        }

        var pieces = new List<MagnetSimPiece>
        {
            MagnetSimPiece.FromBlock(block)
        };

        const int maxSteps = 32;
        for (int step = 0; step < maxSteps; step++)
        {
            DrainSimAlignedMatches(board, pieces, simLayers);
            if (pieces.Count == 0)
            {
                return true;
            }

            SortSimPieces(pieces);
            MagnetSimPiece piece = pieces[0];
            if (!TrySimFindPath(board, piece, simLayers, out Vector2Int lastLegal, out Vector2Int nestAnchor))
            {
                failReason = "no legal route for a remaining chain cell";
                return false;
            }

            piece.anchor = lastLegal;
            if (!TrySimConsumeOneMatch(piece, nestAnchor, simLayers))
            {
                failReason = "remaining chain cell cannot match its nest";
                return false;
            }

            ReplaceSimPieceWithSplits(pieces, 0, piece);
        }

        failReason = "chain resolution exceeds plan limit";
        return false;
    }

    private static List<ShapeCellData> CollectBlockCells(Block block)
    {
        if (block == null)
        {
            return new List<ShapeCellData>();
        }

        return ShapeLayout.Clone(block.Cells, block.ShapeType);
    }

    private static Dictionary<Vector2Int, List<MatchIdentity>> SnapshotSimNestLayers(BoardManager board)
    {
        var simLayers = new Dictionary<Vector2Int, List<MatchIdentity>>();
        if (board == null)
        {
            return simLayers;
        }

        var seen = new HashSet<int>();
        int width = board.Width;
        int height = board.Height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Target target = board.GetTargetAt(cell);
                if (target == null || !seen.Add(target.GetInstanceID()))
                {
                    continue;
                }

                int nestCount = Mathf.Max(1, target.CellCount);
                IReadOnlyList<ShapeCellData> targetCells = target.Cells;
                for (int i = 0; i < nestCount; i++)
                {
                    var layers = new List<MatchIdentity>();
                    ShapeCellData nestCell = targetCells != null && i < targetCells.Count
                        ? targetCells[i]
                        : null;
                    ShapeLayout.CollectResolvableIdentitiesForCell(
                        nestCell,
                        target.ShapeType,
                        layers);
                    simLayers[target.GridPosition + target.GetLocalCell(i)] = layers;
                }
            }
        }

        return simLayers;
    }

    private static bool SimNestCoverageAllows(
        List<MatchIdentity> needed,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers)
    {
        if (needed == null || needed.Count == 0)
        {
            return true;
        }

        var available = new Dictionary<MatchIdentity, int>();
        if (simLayers != null)
        {
            var seenLists = new HashSet<List<MatchIdentity>>();
            foreach (KeyValuePair<Vector2Int, List<MatchIdentity>> pair in simLayers)
            {
                List<MatchIdentity> layers = pair.Value;
                if (layers == null || !seenLists.Add(layers))
                {
                    continue;
                }

                for (int i = 0; i < layers.Count; i++)
                {
                    MatchIdentity identity = layers[i];
                    if (available.ContainsKey(identity))
                    {
                        available[identity] = available[identity] + 1;
                    }
                    else
                    {
                        available[identity] = 1;
                    }
                }
            }
        }

        var required = new Dictionary<MatchIdentity, int>();
        for (int i = 0; i < needed.Count; i++)
        {
            MatchIdentity identity = needed[i];
            if (required.ContainsKey(identity))
            {
                required[identity] = required[identity] + 1;
            }
            else
            {
                required[identity] = 1;
            }
        }

        foreach (KeyValuePair<MatchIdentity, int> pair in required)
        {
            int have = 0;
            if (available != null)
            {
                available.TryGetValue(pair.Key, out have);
            }

            if (have < pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static void DrainSimAlignedMatches(
        BoardManager board,
        List<MagnetSimPiece> pieces,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers)
    {
        if (pieces == null)
        {
            return;
        }

        bool progressed = true;
        int guard = 0;
        while (progressed && pieces.Count > 0 && guard < 32)
        {
            guard++;
            progressed = false;
            for (int i = 0; i < pieces.Count; i++)
            {
                MagnetSimPiece piece = pieces[i];
                if (piece == null || !TrySimFindAdjacentOrOccupyingNest(board, piece, simLayers, out Vector2Int nestAnchor))
                {
                    continue;
                }

                if (!TrySimConsumeOneMatch(piece, nestAnchor, simLayers))
                {
                    continue;
                }

                ReplaceSimPieceWithSplits(pieces, i, piece);
                progressed = true;
                break;
            }
        }
    }

    private static void SortSimPieces(List<MagnetSimPiece> pieces)
    {
        if (pieces == null || pieces.Count < 2)
        {
            return;
        }

        pieces.Sort(CompareSimPieces);
    }

    private static int CompareSimPieces(MagnetSimPiece a, MagnetSimPiece b)
    {
        if (a == null && b == null)
        {
            return 0;
        }

        if (a == null)
        {
            return 1;
        }

        if (b == null)
        {
            return -1;
        }

        int countCompare = a.CellCount.CompareTo(b.CellCount);
        if (countCompare != 0)
        {
            return countCompare;
        }

        int yCompare = a.anchor.y.CompareTo(b.anchor.y);
        if (yCompare != 0)
        {
            return yCompare;
        }

        return a.anchor.x.CompareTo(b.anchor.x);
    }

    private static bool TrySimFindPath(
        BoardManager board,
        MagnetSimPiece piece,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers,
        out Vector2Int lastLegal,
        out Vector2Int nestAnchor)
    {
        lastLegal = Vector2Int.zero;
        nestAnchor = Vector2Int.zero;
        if (piece == null || board == null)
        {
            return false;
        }

        Vector2Int origin = piece.anchor;
        if (TrySimFindAdjacentOrOccupyingNest(board, piece, simLayers, out nestAnchor))
        {
            lastLegal = origin;
            return true;
        }

        int capacity = Mathf.Max(16, board.Width * board.Height);
        var visited = new HashSet<Vector2Int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>(capacity);
        var queue = new Queue<Vector2Int>(capacity);
        visited.Add(origin);
        queue.Enqueue(origin);

        Vector2Int goal = origin;
        bool found = false;
        bool pathEndsInNestEntry = false;

        while (queue.Count > 0)
        {
            Vector2Int pos = queue.Dequeue();
            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int direction = CardinalDirections[i];
                if (!MagnetDirectionAllowed(piece.moveDirection, direction))
                {
                    continue;
                }

                Vector2Int next = pos + direction;
                if (HasSimNestMatchSoft(board, piece, next, simLayers))
                {
                    cameFrom[next] = pos;
                    goal = next;
                    pathEndsInNestEntry = true;
                    found = true;
                    queue.Clear();
                    break;
                }

                if (!CanSimSoftHopInto(board, piece, next, simLayers) || visited.Contains(next))
                {
                    continue;
                }

                visited.Add(next);
                cameFrom[next] = pos;
                if (IsSimGoalCell(board, piece, next, simLayers))
                {
                    goal = next;
                    pathEndsInNestEntry = false;
                    found = true;
                    queue.Clear();
                    break;
                }

                queue.Enqueue(next);
            }
        }

        if (!found)
        {
            return false;
        }

        List<Vector2Int> path = ReconstructPath(origin, goal, cameFrom);
        if (path == null || path.Count == 0)
        {
            return false;
        }

        if (pathEndsInNestEntry)
        {
            lastLegal = path.Count >= 2 ? path[path.Count - 2] : origin;
            return TryGetSimMatchingNestWorld(board, piece, goal, simLayers, out nestAnchor);
        }

        lastLegal = goal;
        return TrySimFindAdjacentOrOccupyingNestAt(board, piece, lastLegal, simLayers, out nestAnchor);
    }

    private static bool TrySimFindAdjacentOrOccupyingNest(
        BoardManager board,
        MagnetSimPiece piece,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers,
        out Vector2Int nestAnchor)
    {
        nestAnchor = Vector2Int.zero;
        if (piece == null)
        {
            return false;
        }

        return TrySimFindAdjacentOrOccupyingNestAt(board, piece, piece.anchor, simLayers, out nestAnchor);
    }

    private static bool TrySimFindAdjacentOrOccupyingNestAt(
        BoardManager board,
        MagnetSimPiece piece,
        Vector2Int anchor,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers,
        out Vector2Int nestWorld)
    {
        nestWorld = Vector2Int.zero;
        if (TryGetSimMatchingNestWorld(board, piece, anchor, simLayers, out nestWorld))
        {
            return true;
        }

        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            Vector2Int direction = CardinalDirections[i];
            if (!MagnetDirectionAllowed(piece.moveDirection, direction))
            {
                continue;
            }

            Vector2Int candidate = anchor + direction;
            if (!TryGetSimMatchingNestWorld(board, piece, candidate, simLayers, out nestWorld))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryGetSimMatchingNestWorld(
        BoardManager board,
        MagnetSimPiece piece,
        Vector2Int proposedAnchor,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers,
        out Vector2Int nestWorld)
    {
        nestWorld = Vector2Int.zero;
        if (!IsSimSoftFootprintValid(board, piece, proposedAnchor))
        {
            return false;
        }

        int count = Mathf.Max(1, piece.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = proposedAnchor + piece.GetLocalCell(i);
            if (!TryGetSimRequiredIdentity(simLayers, world, out MatchIdentity required))
            {
                continue;
            }

            if (!ShapeMatch.AreMatchingLayers(required, piece.GetActiveIdentity(i)))
            {
                continue;
            }

            nestWorld = world;
            return true;
        }

        return false;
    }

    private static bool IsSimGoalCell(
        BoardManager board,
        MagnetSimPiece piece,
        Vector2Int anchor,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers)
    {
        return TrySimFindAdjacentOrOccupyingNestAt(board, piece, anchor, simLayers, out _);
    }

    private static bool CanSimSoftHopInto(
        BoardManager board,
        MagnetSimPiece piece,
        Vector2Int nextAnchor,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers)
    {
        if (!IsSimSoftFootprintValid(board, piece, nextAnchor))
        {
            return false;
        }

        if (HasSimNestMatchSoft(board, piece, nextAnchor, simLayers))
        {
            return false;
        }

        return !SimFootprintTouchesNest(piece, nextAnchor, simLayers);
    }

    private static bool IsSimSoftFootprintValid(BoardManager board, MagnetSimPiece piece, Vector2Int toAnchor)
    {
        if (piece == null || board == null)
        {
            return false;
        }

        int count = Mathf.Max(1, piece.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = toAnchor + piece.GetLocalCell(i);
            if (!board.IsInsideBoard(world) || board.IsCellImpassable(world))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasSimNestMatchSoft(
        BoardManager board,
        MagnetSimPiece piece,
        Vector2Int proposedAnchor,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers)
    {
        if (!IsSimSoftFootprintValid(board, piece, proposedAnchor))
        {
            return false;
        }

        int count = Mathf.Max(1, piece.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = proposedAnchor + piece.GetLocalCell(i);
            if (!TryGetSimRequiredIdentity(simLayers, world, out MatchIdentity required))
            {
                continue;
            }

            if (ShapeMatch.AreMatchingLayers(required, piece.GetActiveIdentity(i)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SimFootprintTouchesNest(
        MagnetSimPiece piece,
        Vector2Int toAnchor,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers)
    {
        if (piece == null || simLayers == null)
        {
            return false;
        }

        int count = Mathf.Max(1, piece.CellCount);
        for (int i = 0; i < count; i++)
        {
            if (TryGetSimRequiredIdentity(simLayers, toAnchor + piece.GetLocalCell(i), out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSimRequiredIdentity(
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers,
        Vector2Int world,
        out MatchIdentity required)
    {
        required = default;
        if (simLayers == null)
        {
            return false;
        }

        if (!simLayers.TryGetValue(world, out List<MatchIdentity> layers) || layers == null || layers.Count == 0)
        {
            return false;
        }

        required = layers[0];
        return true;
    }

    private static bool TrySimConsumeOneMatch(
        MagnetSimPiece piece,
        Vector2Int nestFocus,
        Dictionary<Vector2Int, List<MatchIdentity>> simLayers)
    {
        if (piece == null || simLayers == null)
        {
            return false;
        }

        int count = Mathf.Max(1, piece.CellCount);
        int bestIndex = -1;
        Vector2Int bestWorld = nestFocus;
        int bestDist = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = piece.anchor + piece.GetLocalCell(i);
            Vector2Int targetWorld = world;
            MatchIdentity pieceIdentity = piece.GetActiveIdentity(i);
            bool occupying = TryGetSimRequiredIdentity(simLayers, world, out MatchIdentity occupyingRequired)
                && ShapeMatch.AreMatchingLayers(occupyingRequired, pieceIdentity);
            bool adjacent = !occupying
                && TryGetSimRequiredIdentity(simLayers, nestFocus, out MatchIdentity adjacentRequired)
                && ShapeMatch.AreMatchingLayers(adjacentRequired, pieceIdentity)
                && IsFourAdjacent(world, nestFocus);
            if (!occupying && !adjacent)
            {
                continue;
            }

            if (occupying)
            {
                targetWorld = world;
            }
            else
            {
                targetWorld = nestFocus;
            }

            int dist = Mathf.Abs(world.x - nestFocus.x) + Mathf.Abs(world.y - nestFocus.y);
            if (dist >= bestDist)
            {
                continue;
            }

            bestDist = dist;
            bestIndex = i;
            bestWorld = targetWorld;
        }

        if (bestIndex < 0)
        {
            return false;
        }

        MatchIdentity offered = piece.GetActiveIdentity(bestIndex);
        if (!TryGetSimRequiredIdentity(simLayers, bestWorld, out MatchIdentity required)
            || !ShapeMatch.AreMatchingLayers(required, offered))
        {
            return false;
        }

        if (!simLayers.TryGetValue(bestWorld, out List<MatchIdentity> layers) || layers == null || layers.Count == 0)
        {
            return false;
        }

        layers.RemoveAt(0);

        ShapeCellData cell = piece.GetCell(bestIndex);
        bool hadInner = cell != null && cell.innerShapes != null && cell.innerShapes.Count > 0;
        if (hadInner)
        {
            ShapeLayout.TryConsumeLayer(cell, offered);
            piece.RefreshFallbackShape();
            return true;
        }

        piece.RemoveCellAt(bestIndex);
        return true;
    }

    private static void ReplaceSimPieceWithSplits(
        List<MagnetSimPiece> pieces,
        int index,
        MagnetSimPiece piece)
    {
        if (pieces == null || index < 0 || index >= pieces.Count)
        {
            return;
        }

        pieces.RemoveAt(index);
        if (piece == null || piece.CellCount <= 0 || piece.cells == null || piece.cells.Count == 0)
        {
            return;
        }

        var worlds = new List<Vector2Int>();
        var remaining = new List<ShapeCellData>();
        int count = piece.cells.Count;
        for (int i = 0; i < count; i++)
        {
            ShapeCellData source = piece.cells[i];
            worlds.Add(piece.anchor + (source != null ? source.localPosition : Vector2Int.zero));
            remaining.Add(new ShapeCellData
            {
                localPosition = Vector2Int.zero,
                shapeType = source != null ? source.shapeType : piece.shapeType,
                innerShapes = source != null
                    ? ShapeLayout.CloneInners(source.innerShapes)
                    : new List<ShapeType>()
            });
        }

        var anchors = new List<Vector2Int>();
        var components = new List<List<ShapeCellData>>();
        ShapeLayout.SplitConnected(worlds, remaining, anchors, components);
        for (int i = 0; i < components.Count; i++)
        {
            pieces.Insert(index + i, MagnetSimPiece.FromCells(
                components[i],
                anchors[i],
                piece.moveDirection));
        }
    }

    private static bool MagnetDirectionAllowed(MoveDirection moveDirection, Vector2Int direction)
    {
        switch (moveDirection)
        {
            case MoveDirection.Any:
                return direction == Vector2Int.up
                    || direction == Vector2Int.down
                    || direction == Vector2Int.left
                    || direction == Vector2Int.right;
            case MoveDirection.Up:
                return direction == Vector2Int.up;
            case MoveDirection.Down:
                return direction == Vector2Int.down;
            case MoveDirection.Left:
                return direction == Vector2Int.left;
            case MoveDirection.Right:
                return direction == Vector2Int.right;
            default:
                return false;
        }
    }

    private static bool IsFourAdjacent(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;
    }

    private bool HasMagnetSucceeded(Block block, int matchesBefore)
    {
        if (levelManager != null && levelManager.SuccessfulMatchCount > matchesBefore)
        {
            return true;
        }

        if (block == null || !block || !block.isActiveAndEnabled)
        {
            return true;
        }

        return block.IsSettled;
    }

    private bool HasMagnetPullSucceeded(
        Block block,
        int matchesBefore,
        int cellsBefore,
        int layersBefore,
        bool requireOwnPieceConsume)
    {
        if (!requireOwnPieceConsume)
        {
            return HasMagnetSucceeded(block, matchesBefore);
        }

        if (block == null || !block || !block.isActiveAndEnabled || block.IsSettled)
        {
            return true;
        }

        int cellsNow = Mathf.Max(1, block.CellCount);
        if (cellsNow < cellsBefore)
        {
            return true;
        }

        int layersNow = ShapeLayout.TotalLayers(
            CollectBlockCells(block),
            block.ShapeType,
            block.Composition,
            block.OuterShape);
        return layersNow < layersBefore;
    }

    private IEnumerator ExecuteMagnetSegment(Block block, BlockMover mover, MagnetPlan plan)
    {
        if (block == null || mover == null)
        {
            yield break;
        }

        if (!mover.IsDirectionAllowed(plan.direction))
        {
            Log("Magnet segment skipped: direction not allowed");
            yield break;
        }

        BoardManager board = boardManager != null ? boardManager : block.Board;
        List<Block> suspended = SuspendSoftBlocksForSegment(
            board,
            block,
            block.GridPosition,
            plan.direction,
            plan.requestCell);

        try
        {
            bool began = false;
            float beginDeadline = Time.realtimeSinceStartup + 4f;
            while (!began && Time.realtimeSinceStartup < beginDeadline)
            {
                if (block == null || !block || block.IsSettled)
                {
                    yield break;
                }

                began = mover.TryBeginDrag(plan.direction);
                if (began)
                {
                    break;
                }

                if (levelManager != null && !levelManager.IsPieceInputAllowed)
                {
                    yield return null;
                    continue;
                }

                Log("Magnet segment skipped: TryBeginDrag rejected");
                yield break;
            }

            if (!began)
            {
                Log("Magnet segment skipped: TryBeginDrag rejected");
                yield break;
            }

            Log($"Magnet hop start shape={block.GetActiveShape(0)} cell={block.GridPosition} frame={Time.frameCount}");

            mover.SetDragRequest(plan.requestCell);
            yield return null;
            if (mover != null)
            {
                mover.EndDrag();
            }

            float timeout = 8f;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (mover != null && (mover.IsMoving || mover.IsDragging) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (mover != null && (mover.IsMoving || mover.IsDragging))
            {
                Log("Magnet segment timeout: forcing EndDrag");
                mover.EndDrag();
                float releaseDeadline = Time.realtimeSinceStartup + 1.5f;
                while (mover != null && (mover.IsMoving || mover.IsDragging) && Time.realtimeSinceStartup < releaseDeadline)
                {
                    yield return null;
                }
            }

            deadline = Time.realtimeSinceStartup + timeout;
            while (levelManager != null && Time.realtimeSinceStartup < deadline)
            {
                bool alignedBusy = levelManager.IsAlignedMatchRunning;
                bool moverBusy = mover != null && (mover.IsMoving || mover.IsDragging);
                if (!alignedBusy && !moverBusy)
                {
                    break;
                }

                yield return null;
            }

            deadline = Time.realtimeSinceStartup + timeout;
            while (levelManager != null
                   && !levelManager.IsPieceInputAllowed
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }
        finally
        {
            RestoreSoftBlocks(board, block, suspended);
        }
    }

    /// <summary>Test helper to grant charges without economy UI.</summary>
    public void SetMagnetCharges(int count)
    {
        SetCharges(count);
    }

    private void SetPhase(MagnetPhase next)
    {
        if (phase == next)
        {
            return;
        }

        bool wasSelecting = phase == MagnetPhase.Selecting;
        phase = next;
        if (phase == MagnetPhase.Selecting)
        {
            StartSelectionPresentation();
        }
        else if (wasSelecting)
        {
            StopSelectionPresentation();
        }

        SyncSelectionOverlay(overlayHideImmediate);
        overlayHideImmediate = false;
        OnPhaseChanged?.Invoke(phase);
        OnStateChanged?.Invoke();
    }

    private void SyncSelectionOverlay(bool immediate = false)
    {
        if (phase == MagnetPhase.Selecting)
        {
            BoosterSelectionOverlay overlay = BoosterSelectionOverlay.Ensure();
            if (overlay != null)
            {
                overlay.SetVisible(true, immediate);
            }

            return;
        }

        BoosterSelectionOverlay.HideExisting(immediate);
    }

    /// <summary>
    /// Presentation-only breath pulse on eligible Magnet blocks while Selecting.
    /// One shared timing so chain cells stay synchronized.
    /// </summary>
    private void StartSelectionPresentation()
    {
        StopSelectionPresentation();
        CollectEligibleSelectionViews(selectionViews);
        if (selectionViews.Count == 0)
        {
            return;
        }

        selectionPulse = DOTween.Sequence()
            .SetId(TweenAnimationUtility.MagnetSelectionId)
            .SetLink(gameObject)
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true);
        selectionPulse.Append(TweenAnimationUtility.Progress(SelectionPulseCycle, t =>
        {
            float wave = Mathf.Sin(t * Mathf.PI);
            float mul = Mathf.LerpUnclamped(1f, SelectionPulsePeak, wave);
            for (int i = 0; i < selectionViews.Count; i++)
            {
                PieceView3D view = selectionViews[i];
                if (view != null)
                {
                    view.SetMagnetSelectionEmphasis(mul);
                }
            }
        }));
        selectionPulse.OnKill(() => { selectionPulse = null; });
    }

    private void StopSelectionPresentation()
    {
        if (selectionPulse != null && selectionPulse.IsActive())
        {
            selectionPulse.Kill(false);
        }

        selectionPulse = null;
        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.MagnetSelectionId, false);

        for (int i = 0; i < selectionViews.Count; i++)
        {
            PieceView3D view = selectionViews[i];
            if (view != null)
            {
                view.SetMagnetSelectionEmphasis(1f);
            }
        }

        selectionViews.Clear();
    }

    private void ClearAllMagnetSelectionPresentation()
    {
        StopSelectionPresentation();
        PieceView3D[] views = FindObjectsByType<PieceView3D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null)
            {
                views[i].ClearMagnetSelectionPresentation();
            }
        }
    }

    private void PlaySelectionConfirm(Block block)
    {
        if (block == null)
        {
            return;
        }

        PieceView3D[] views = FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view != null && view.SourceBlock == block && view.gameObject.activeInHierarchy)
            {
                view.PlayMagnetSelectionConfirm();
            }
        }
    }

    private void CollectEligibleSelectionViews(List<PieceView3D> destination)
    {
        destination.Clear();
        PieceView3D[] views = FindObjectsByType<PieceView3D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null || !IsMagnetEligibleVisual(view))
            {
                continue;
            }

            if (!destination.Contains(view))
            {
                destination.Add(view);
            }
        }
    }

    /// <summary>True while the Selecting breath pulse is running (presentation only).</summary>
    public bool IsSelectionPresentationActive =>
        selectionPulse != null && selectionPulse.IsActive();

    private void SetCharges(int count)
    {
        int clamped = Mathf.Max(0, count);
        if (magnetCharges == clamped)
        {
            return;
        }

        magnetCharges = clamped;
        OnChargesChanged?.Invoke(magnetCharges);
    }

    private void ClearMagnetPresentation()
    {
        BlockMover[] movers = FindObjectsByType<BlockMover>(FindObjectsSortMode.None);
        for (int i = 0; i < movers.Length; i++)
        {
            BlockMover mover = movers[i];
            if (mover != null && mover.IsMagnetPresenting)
            {
                mover.SetMagnetPresenting(false);
            }
        }
    }

    private void ClearHighlight()
    {
        if (highlightedBlock != null)
        {
            highlightedBlock.HideDragSelection();
            highlightedBlock = null;
        }
    }

    private void Log(string message)
    {
        if (debugLog)
        {
            Debug.Log($"[Magnet] {message}", this);
        }
    }

    private struct MagnetPlan
    {
        public Vector2Int direction;
        public Vector2Int requestCell;
        public int hopsBeforeMatch;
    }

    /// <summary>
    /// Virtual chain remnant for Magnet precheck only. Not a gameplay Block.
    /// </summary>
    private sealed class MagnetSimPiece
    {
        public Vector2Int anchor;
        public List<ShapeCellData> cells;
        public ShapeType shapeType;
        public MoveDirection moveDirection;

        public int CellCount
        {
            get { return ShapeLayout.EffectiveCount(cells); }
        }

        public static MagnetSimPiece FromBlock(Block block)
        {
            if (block == null)
            {
                return null;
            }

            return new MagnetSimPiece
            {
                anchor = block.GridPosition,
                cells = ShapeLayout.Clone(block.Cells, block.ShapeType),
                shapeType = block.ShapeType,
                moveDirection = block.MoveDirection
            };
        }

        public static MagnetSimPiece FromCells(
            List<ShapeCellData> remaining,
            Vector2Int worldAnchor,
            MoveDirection moveDirection)
        {
            ShapeType fallback = remaining != null && remaining.Count > 0 && remaining[0] != null
                ? remaining[0].shapeType
                : ShapeType.Square;
            return new MagnetSimPiece
            {
                anchor = worldAnchor,
                cells = remaining != null ? remaining : new List<ShapeCellData>(),
                shapeType = fallback,
                moveDirection = moveDirection
            };
        }

        public Vector2Int GetLocalCell(int index)
        {
            return ShapeLayout.EffectiveLocal(cells, index);
        }

        public ShapeType GetActiveShape(int index)
        {
            ShapeCellData cell = GetCell(index);
            return ShapeLayout.ActiveShape(cell, shapeType);
        }

        public MatchIdentity GetActiveIdentity(int index)
        {
            ShapeCellData cell = GetCell(index);
            return ShapeMatch.FromCell(cell, shapeType);
        }

        public ShapeCellData GetCell(int index)
        {
            if (cells == null || index < 0 || index >= cells.Count)
            {
                return null;
            }

            return cells[index];
        }

        public void RefreshFallbackShape()
        {
            ShapeCellData first = cells != null && cells.Count > 0 ? cells[0] : null;
            shapeType = ShapeLayout.ActiveShape(first, shapeType);
        }

        public void RemoveCellAt(int index)
        {
            if (cells == null || index < 0 || index >= cells.Count)
            {
                return;
            }

            cells.RemoveAt(index);
            RefreshFallbackShape();
        }
    }
}
