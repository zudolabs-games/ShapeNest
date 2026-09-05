using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Moves a block cell-by-cell. Owns gameplay sequencing (drag, hops, nest match timing).
/// Visual hop/nest interpolation is delegated to <see cref="IPieceMotion"/>.
/// </summary>
[RequireComponent(typeof(Block))]
[RequireComponent(typeof(UIPieceMotion))]
public class BlockMover : MonoBehaviour
{
    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Duration of each normal cell-to-cell hop. Phase 61A: slightly quicker for continuous drag fluency.")]
    private float secondsPerCell = 0.105f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tiny wind-up on the first hop of a drag. Later hops skip this.")]
    private float normalHopAnticipateDuration = 0.03f;

    [SerializeField]
    [Range(0f, 0.08f)]
    [Tooltip("First-hop wind-up distance as a fraction of one cell, opposite the move.")]
    private float normalHopAnticipatePercent = 0.04f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Brief pause after arriving on a non-matching final cell.")]
    private float finalSettleDelay = 0.03f;

    [SerializeField]
    [Range(0.96f, 1f)]
    [Tooltip("Subtle squash during a hop, relative to the current visual scale. 1 means none.")]
    private float hopTravelScale = 0.985f;

    [SerializeField]
    [Range(0f, 0.1f)]
    [Tooltip("Visual hop arc height as a fraction of one board cell. Does not change hop duration or occupancy.")]
    private float hopLiftPercent = 0.045f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Hold on the pre-target cell so the stop is readable before nest-entry.")]
    private float matchingTargetPause = 0.22f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Anticipation lift duration after the pause, before the nest arc.")]
    private float matchingTargetAnticipateDuration = 0.08f;

    [SerializeField]
    [Range(0.02f, 0.12f)]
    [Tooltip("Anticipation lift as a fraction of one board cell.")]
    private float matchingTargetAnticipateLiftPercent = 0.06f;

    [SerializeField]
    [Range(1f, 1.15f)]
    [Tooltip("Anticipation scale. 1 means no scale change.")]
    private float matchingTargetAnticipateScale = 1.06f;

    [SerializeField]
    [Range(0.05f, 0.25f)]
    [Tooltip("Arc peak height as a fraction of one board cell.")]
    private float matchingTargetLiftPercent = 0.12f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Duration of the curved hop into the matching nest.")]
    private float matchingTargetArcDuration = 0.14f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tiny sit into the nest after the arc, before Settle.")]
    private float matchingTargetSitDuration = 0.05f;

    [SerializeField]
    [Range(0.9f, 1f)]
    [Tooltip("Subtle scale during the hop. 1 means no scale change.")]
    private float matchingTargetHopScale = 0.97f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Duration of the nest scale pulse after the block lands.")]
    private float matchingTargetPulseDuration = 0.12f;

    [SerializeField]
    [Range(1f, 1.2f)]
    [Tooltip("Peak scale of the nest pulse. 1 means no pulse.")]
    private float matchingTargetPulseScale = 1.08f;

    [SerializeField]
    private bool debugDrag;

    [SerializeField]
    [Tooltip("Presentation-only match/merge effect. Does not affect occupancy or completion.")]
    private MatchEffect matchEffectPrefab;

    private Block block;
    private UIPieceMotion pieceMotion;
    private BoardManager cachedBoard;
    private bool isMoving;
    private LevelManager levelManager;
    private AudioFeedback audioFeedback;
    private HapticFeedback hapticFeedback;

    private bool dragActive;
    private bool dragReleased;
    private Vector2Int dragOrigin;
    private Vector2Int dragDirection;
    private Vector2Int desiredCell;
    private Vector2Int logicalCell;
    private bool visualHopActive;
    private Vector2Int visualHopFrom;
    private bool matchEntryVisualActive;
    private bool magnetPresenting;
    private bool hopAnticipatePending;
    private bool dragWantsForward;
    private bool hopBlockedCuePlayed;
    private bool fingerVisualActive;
    private bool fingerDrivenDrag;
    private Vector3 fingerVisualTarget;
    private Vector3 fingerVisualVelocity;
    private bool fingerVisualHasTarget;
    private Coroutine dragRoutine;
    private Vector2Int dragSessionStart;
    private bool dragSessionMatchEntered;
    private MatchEffect activeMatchEffect;
    private readonly List<int> nestCellIndices = new List<int>();
    private readonly List<Target> nestTargets = new List<Target>();
    private readonly List<Vector2Int> nestTargetWorlds = new List<Vector2Int>();
    private readonly List<Vector2Int> splitWorlds = new List<Vector2Int>();
    private readonly List<ShapeCellData> splitCells = new List<ShapeCellData>();
    private readonly List<Vector2Int> splitAnchors = new List<Vector2Int>();
    private readonly List<List<ShapeCellData>> splitComponents = new List<List<ShapeCellData>>();
    private readonly List<Block> pendingExtractionRevealBlocks = new List<Block>(4);
    private bool resolvingAligned;
    private bool hasLastMatch;
    private Vector2Int lastMatchOrigin;
    private Vector2Int lastMatchTargetCell;

    public static bool LastConsumeSucceeded { get; set; }

    /// <summary>
    /// Per-mover consume result from the last <see cref="ConsumeAndRebuild"/> on this instance.
    /// Safer than <see cref="LastConsumeSucceeded"/> when multiple match-wave members run in parallel.
    /// </summary>
    public bool LastResolvedConsumeSucceeded { get; private set; }

    /// <summary>One ready auto-match action (one block cell → one target cell).</summary>
    public readonly struct AlignedMatchAction
    {
        public readonly Block Subject;
        public readonly int CellIndex;
        public readonly Vector2Int CellWorld;
        public readonly Vector2Int NestTo;

        public AlignedMatchAction(Block subject, int cellIndex, Vector2Int cellWorld, Vector2Int nestTo)
        {
            Subject = subject;
            CellIndex = cellIndex;
            CellWorld = cellWorld;
            NestTo = nestTo;
        }

        public Vector2Int Translation => NestTo - CellWorld;
    }

    /// <summary>
    /// Phase 67: one connected-block movement unit. Multiple match actions may share a group
    /// when they belong to the same block and require the same rigid translation.
    /// </summary>
    public sealed class AlignedMovementGroup
    {
        public Block Subject;
        public Vector2Int Translation;
        public readonly List<AlignedMatchAction> Actions = new List<AlignedMatchAction>();

        public Vector2Int FromAnchor => Subject != null ? Subject.GridPosition : Vector2Int.zero;

        public Vector2Int ToAnchor => FromAnchor + Translation;
    }

    /// <summary>Legacy wave member (subject + nest). Prefer <see cref="AlignedMatchAction"/>.</summary>
    public readonly struct AlignedMatchWaveMember
    {
        public readonly Block Subject;
        public readonly Vector2Int NestTo;

        public AlignedMatchWaveMember(Block subject, Vector2Int nestTo)
        {
            Subject = subject;
            NestTo = nestTo;
        }
    }

    /// <summary>TEMP: auto-match visual sequence counter for MATCH SEQUENCE logs.</summary>
    private static int matchSequenceIndex;

    private RectTransform pendingCellTraveler;

    public static void ResetMatchSequenceIndex()
    {
        matchSequenceIndex = 0;
    }

    /// <summary>Existing nest pause used between sequential auto-matches.</summary>
    public float MatchingTargetPause => matchingTargetPause;

    /// <summary>Existing nest pause — used between sequential auto-matches after VFX cleanup.</summary>
    public IEnumerator WaitNaturalMatchGap()
    {
        yield return Pause(matchingTargetPause);
    }

    public bool IsMoving => isMoving;
    public bool IsDragging => dragActive;

    /// <summary>
    /// True while a successful drag is live and has not been released or handed to match.
    /// Presentation-only read of existing dragActive and dragReleased.
    /// </summary>
    public bool IsDragAiming => dragActive && !dragReleased;

    /// <summary>
    /// Presentation-only: finger is driving continuous world pose (not hop tweens).
    /// </summary>
    public bool IsFingerVisualDragging => fingerVisualActive && IsDragAiming;

    /// <summary>
    /// Presentation-only: true while nest-entry travel is still playing after match
    /// handoff set <c>dragReleased</c>. Lets the destination highlight follow
    /// <see cref="VisualGridCell"/> through the final visual stop without changing
    /// drag-release or match detection.
    /// </summary>
    public bool IsMatchEntryPresenting => matchEntryVisualActive;

    /// <summary>
    /// Presentation-only: Magnet is driving this mover's automatic journey.
    /// Independent of <see cref="IsDragAiming"/> because Magnet calls
    /// <see cref="EndDrag"/> immediately while hops continue. Not gameplay state.
    /// </summary>
    public bool IsMagnetPresenting => magnetPresenting;

    /// <summary>
    /// Presentation-only setter for <see cref="IsMagnetPresenting"/>. Does not
    /// change drag, hops, occupancy, or match detection.
    /// </summary>
    public void SetMagnetPresenting(bool presenting)
    {
        magnetPresenting = presenting;
    }

    public Vector2Int LogicalCell => logicalCell;

    /// <summary>
    /// Existing clamped drag destination written by <see cref="SetDragRequest"/>.
    /// Occupancy remains <see cref="LogicalCell"/>. Presentation must not recompute this.
    /// </summary>
    public Vector2Int DesiredCell => desiredCell;

    /// <summary>
    /// Grid cell the piece is still rendered on during a drag hop.
    /// Occupancy (<see cref="LogicalCell"/>) is assigned to hop <c>to</c> before
    /// <see cref="AnimateHop"/> moves the mesh; while that hop plays this stays
    /// on hop <c>from</c> so presentation is not one cell ahead of the visual.
    /// When no hop is playing, equals <see cref="LogicalCell"/>. Nest-entry travel
    /// uses the same hop-from rule so the highlight does not jump to the nest cell
    /// while the mesh is still on the adjacent cell.
    /// </summary>
    public Vector2Int VisualGridCell => visualHopActive ? visualHopFrom : logicalCell;

    public void SetLevelManager(LevelManager manager)
    {
        levelManager = manager;
    }

    public void SetAudioFeedback(AudioFeedback feedback)
    {
        audioFeedback = feedback;
    }

    public void SetHapticFeedback(HapticFeedback feedback)
    {
        hapticFeedback = feedback;
    }

    private void Awake()
    {
        block = GetComponent<Block>();
        pieceMotion = GetComponent<UIPieceMotion>();
        if (pieceMotion == null)
        {
            pieceMotion = gameObject.AddComponent<UIPieceMotion>();
        }
    }

    /// <summary>Presentation motion for a block. Gameplay sequencing stays here.</summary>
    private IPieceMotion MotionFor(Block subject)
    {
        if (subject == null)
        {
            return pieceMotion;
        }

        if (TryGetWorldMotion(subject, out WorldPieceMotion worldMotion))
        {
            return worldMotion;
        }

        if (subject == block && pieceMotion != null)
        {
            return pieceMotion;
        }

        UIPieceMotion motion = subject.GetComponent<UIPieceMotion>();
        if (motion == null)
        {
            motion = subject.gameObject.AddComponent<UIPieceMotion>();
        }

        return motion;
    }

    private bool TryGetWorldMotion(Block subject, out WorldPieceMotion worldMotion)
    {
        worldMotion = null;
        if (subject == null || subject.WorldView == null)
        {
            return false;
        }

        BoardPresenter3D presenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter == null)
        {
            return false;
        }

        worldMotion = subject.GetComponent<WorldPieceMotion>();
        if (worldMotion == null)
        {
            worldMotion = subject.gameObject.AddComponent<WorldPieceMotion>();
        }

        worldMotion.Bind(subject.WorldView, presenter.GridSpace);
        return true;
    }

    private IGridSpace MotionGridSpace(BoardManager board)
    {
        if (block != null && block.WorldView != null)
        {
            BoardPresenter3D presenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
            if (presenter != null)
            {
                return presenter.GridSpace;
            }
        }

        return board != null ? board.GridSpace : null;
    }

    private Vector2 MotionCellSize(BoardManager board)
    {
        IGridSpace space = MotionGridSpace(board);
        if (space is GridSpace3D grid3D)
        {
            return grid3D.CellSize;
        }

        return board != null ? board.VisualCellSize : Vector2.one;
    }

    private Vector3 MotionRestScale(Block subject)
    {
        if (subject != null && subject.WorldView != null)
        {
            return subject.WorldView.ConfiguredFootprintScale;
        }

        return subject != null ? subject.RestScale : Vector3.one;
    }

    private void SnapMotionToCell(IPieceMotion motion, BoardManager board, Vector2Int cell)
    {
        if (motion == null || board == null)
        {
            return;
        }

        IGridSpace space = MotionGridSpace(board);
        if (space != null)
        {
            motion.SnapToGrid(space, cell);
            return;
        }

        motion.SnapToLocal(board.GridToLocal(cell));
    }

    private void OnDisable()
    {
        dragActive = false;
        dragReleased = false;
        isMoving = false;
        dragRoutine = null;
        resolvingAligned = false;
        hasLastMatch = false;
        visualHopActive = false;
        matchEntryVisualActive = false;
        magnetPresenting = false;
        fingerVisualActive = false;
        fingerDrivenDrag = false;
        fingerVisualHasTarget = false;
        fingerVisualVelocity = Vector3.zero;
        if (activeMatchEffect != null)
        {
            Destroy(activeMatchEffect.gameObject);
            activeMatchEffect = null;
        }
    }

    private void OnDestroy()
    {
        if (activeMatchEffect != null)
        {
            Destroy(activeMatchEffect.gameObject);
            activeMatchEffect = null;
        }
    }

    public bool IsDirectionAllowed(Vector2Int direction)
    {
        if (block == null)
        {
            block = GetComponent<Block>();
        }

        if (block == null)
        {
            return false;
        }

        switch (block.MoveDirection)
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

    public bool TryBeginDrag(Vector2Int direction)
    {
        return TryBeginDragInternal(direction, requireAllowedDirection: true, fingerDriven: false);
    }

    /// <summary>
    /// Fingerwise press: start the existing drag sequencer immediately.
    /// Fixed-direction blocks begin already locked to that axis; Any begins with
    /// no axis until <see cref="SetDragDirection"/> (input adapter).
    /// </summary>
    public bool TryBeginFingerDrag()
    {
        if (block == null)
        {
            block = GetComponent<Block>();
        }

        Vector2Int initialDirection = Vector2Int.zero;
        if (block != null && TryGetFixedMoveDirection(out Vector2Int fixedDirection))
        {
            initialDirection = fixedDirection;
        }

        return TryBeginDragInternal(initialDirection, requireAllowedDirection: false, fingerDriven: true);
    }

    private bool TryBeginDragInternal(Vector2Int direction, bool requireAllowedDirection, bool fingerDriven)
    {
        if (block == null)
        {
            block = GetComponent<Block>();
        }

        bool directionOk = requireAllowedDirection
            ? IsDirectionAllowed(direction)
            : direction == Vector2Int.zero || IsDirectionAllowed(direction);

        if (block == null || block.IsSettled || block.IsFrozen || isMoving || dragActive || !directionOk)
        {
            LogDrag($"BeginDrag rejected: settled={block != null && block.IsSettled} moving={isMoving} dragging={dragActive} dir={direction}");
            return false;
        }

        // Pending Destroy / deactivated leftovers must not start coroutines.
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            LogDrag("BeginDrag rejected: mover inactive or disabled");
            return false;
        }

        if (levelManager != null && !levelManager.IsPieceInputAllowed)
        {
            return false;
        }

        BoardManager board = GetBoard();
        if (board == null)
        {
            return false;
        }

        if (board.IsBlockUnderImpassableCell(block))
        {
            LogDrag("BeginDrag rejected: block is behind a closed shutter");
            return false;
        }

        if (direction != Vector2Int.zero && !IsDirectionAllowed(direction))
        {
            direction = Vector2Int.zero;
        }

        cachedBoard = board;
        dragActive = true;
        dragReleased = false;
        dragOrigin = block.GridPosition;
        dragSessionStart = block.GridPosition;
        dragSessionMatchEntered = false;
        logicalCell = dragOrigin;
        dragDirection = direction;
        desiredCell = dragOrigin;
        hopAnticipatePending = true;
        dragWantsForward = false;
        hopBlockedCuePlayed = false;
        fingerDrivenDrag = fingerDriven;
        fingerVisualActive = false;
        fingerVisualHasTarget = false;
        fingerVisualVelocity = Vector3.zero;
        isMoving = true;
        dragRoutine = StartCoroutine(DragRoutine(board));
        BoardUndoHistory undoHistory = BoardUndoHistory.Resolve();
        undoHistory?.BeginPendingCapture(board);
        LogDrag($"BeginDrag {block.name} origin={dragOrigin} dir={direction}");
        PlayDragStartSound();
        return true;
    }

    /// <summary>Maps fixed <see cref="MoveDirection"/> to a cardinal; false when Any.</summary>
    public bool TryGetFixedMoveDirection(out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        if (block == null)
        {
            block = GetComponent<Block>();
        }

        if (block == null)
        {
            return false;
        }

        switch (block.MoveDirection)
        {
            case MoveDirection.Up:
                direction = Vector2Int.up;
                return true;
            case MoveDirection.Down:
                direction = Vector2Int.down;
                return true;
            case MoveDirection.Left:
                direction = Vector2Int.left;
                return true;
            case MoveDirection.Right:
                direction = Vector2Int.right;
                return true;
            default:
                return false;
        }
    }

    public void SetDragDirection(Vector2Int direction)
    {
        if (!dragActive || dragReleased || block == null || !IsDirectionAllowed(direction))
        {
            return;
        }

        if (direction == dragDirection)
        {
            return;
        }

        dragDirection = direction;
        dragOrigin = logicalCell;
        desiredCell = logicalCell;
        dragWantsForward = false;
        LogDrag($"Steer {direction} origin={dragOrigin}");
    }

    public void SetDragRequest(Vector2Int requestedCell)
    {
        if (!dragActive || dragReleased || block == null)
        {
            return;
        }

        BoardManager board = cachedBoard != null ? cachedBoard : GetBoard();
        if (board == null)
        {
            return;
        }

        Vector2Int clamped = ClampDragDestination(board, dragOrigin, dragDirection, requestedCell);
        int rawSteps = AxisSteps(requestedCell - dragOrigin, dragDirection);
        if (rawSteps > 0)
        {
            dragWantsForward = true;
            if (clamped == logicalCell && !hopBlockedCuePlayed
                && !board.HasNestMatch(block, logicalCell + dragDirection))
            {
                hopBlockedCuePlayed = true;
                NotifyBlockedAttempt();
            }
        }

        desiredCell = clamped;
        if (debugDrag)
        {
            LogDrag($"Request {requestedCell} -> clamped {clamped} dir={dragDirection}");
        }
    }

    public void EndDrag()
    {
        if (!dragActive)
        {
            return;
        }

        FinishFingerVisualAndSnapToLogical();
        dragReleased = true;
        if (debugDrag)
        {
            LogDrag($"EndDrag desired={desiredCell} grid={block.GridPosition}");
        }
    }

    /// <summary>
    /// Presentation-only continuous finger follow while aiming.
    /// Constrains board-plane world pose with existing direction / clamp rules,
    /// then applies low-latency visual smoothing. Occupancy stays in DragRoutine.
    /// </summary>
    public void SetFingerDragWorld(Vector3 desiredBoardWorld)
    {
        if (!IsDragAiming || magnetPresenting || !fingerDrivenDrag || block == null)
        {
            return;
        }

        BoardManager board = cachedBoard != null ? cachedBoard : GetBoard();
        IGridSpace space = MotionGridSpace(board);
        if (board == null || space == null)
        {
            return;
        }

        Vector3 constrained = ConstrainFingerDragWorld(board, space, desiredBoardWorld);
        fingerVisualTarget = constrained;
        fingerVisualHasTarget = true;
        fingerVisualActive = true;
        ApplySmoothedFingerVisual(forceSnap: false);
    }

    private void LateUpdate()
    {
        if (!fingerVisualActive || !fingerVisualHasTarget || !IsDragAiming || !fingerDrivenDrag)
        {
            return;
        }

        ApplySmoothedFingerVisual(forceSnap: false);
    }

    private Vector3 ConstrainFingerDragWorld(BoardManager board, IGridSpace space, Vector3 desiredBoardWorld)
    {
        Transform boardRoot = null;
        if (space is GridSpace3D)
        {
            BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
            if (presenter != null)
            {
                boardRoot = presenter.transform;
            }
        }

        Vector2Int axis = dragDirection;
        Vector2Int originCell = dragOrigin;
        if (axis == Vector2Int.zero)
        {
            originCell = dragSessionStart;
            Vector3 startSeat = SeatedCellWorld(space, originCell);
            Vector3 delta = desiredBoardWorld - startSeat;
            axis = FingerDragController.CardinalFromBoardDelta(delta, boardRoot);
            if (axis == Vector2Int.zero || !IsDirectionAllowed(axis))
            {
                // Stay on start seating until an allowed axis emerges from the finger.
                return startSeat;
            }
        }

        Vector2Int legalMax = ClampDragDestination(board, originCell, axis, originCell + (axis * 64));
        Vector3 originWorld = SeatedCellWorld(space, originCell);
        Vector3 maxWorld = SeatedCellWorld(space, legalMax);
        Vector3 axisVec = maxWorld - originWorld;
        float axisLenSq = (axisVec.x * axisVec.x) + (axisVec.z * axisVec.z);
        if (axisLenSq < 0.000001f)
        {
            // Next cell is blocked (target, obstacle, board edge, etc.).
            // Stay seated on the legal cell — do not preview into the barrier.
            // Matching still consumes into the target on release via EndDrag.
            return originWorld;
        }

        float axisLen = Mathf.Sqrt(axisLenSq);
        Vector3 dir = new Vector3(axisVec.x / axisLen, 0f, axisVec.z / axisLen);
        Vector3 toDesired = desiredBoardWorld - originWorld;
        toDesired.y = 0f;
        float t = Vector3.Dot(toDesired, dir);
        t = Mathf.Clamp(t, 0f, axisLen);
        Vector3 result = originWorld + (dir * t);
        result.y = originWorld.y;
        return result;
    }

    private Vector3 SeatedCellWorld(IGridSpace space, Vector2Int cell)
    {
        Vector3 world = space.GridToWorld(cell);
        PieceView3D view = block != null ? block.WorldView : null;
        float halfHeight = view != null ? Mathf.Abs(view.transform.lossyScale.y) * 0.5f : 0.11f;
        float lift = view != null ? view.SurfaceLift : 0.02f;
        float carry = view != null ? view.PresentationLift : 0f;
        if (!PieceMotionMath.IsFinite(halfHeight))
        {
            halfHeight = 0.11f;
        }

        if (!PieceMotionMath.IsFinite(lift))
        {
            lift = 0.02f;
        }

        if (!PieceMotionMath.IsFinite(carry))
        {
            carry = 0f;
        }

        world.y += lift + halfHeight + carry;
        return world;
    }

    /// <summary>
    /// Smooth toward the already-constrained target. Never eases past the target
    /// (no overshoot into blocked cells). Gameplay cells are untouched.
    /// </summary>
    private void ApplySmoothedFingerVisual(bool forceSnap)
    {
        if (block == null || block.WorldView == null || !fingerVisualHasTarget)
        {
            return;
        }

        if (!PieceMotionMath.IsFinite(fingerVisualTarget))
        {
            return;
        }

        if (TryGetWorldMotion(block, out WorldPieceMotion worldMotion))
        {
            worldMotion.InterruptTweensForFingerDrag();
        }

        Transform view = block.WorldView.transform;
        Vector3 current = view.position;
        if (forceSnap || !PieceMotionMath.IsFinite(current))
        {
            fingerVisualVelocity = Vector3.zero;
            view.position = fingerVisualTarget;
            return;
        }

        // Low-latency attach feel (~30ms). Constraint already applied to target.
        const float smoothTime = 0.03f;
        Vector3 next = Vector3.SmoothDamp(
            current,
            fingerVisualTarget,
            ref fingerVisualVelocity,
            smoothTime,
            Mathf.Infinity,
            Time.deltaTime);

        // Hard clamp: never travel past the constrained target on XZ.
        Vector3 toTarget = fingerVisualTarget - current;
        toTarget.y = 0f;
        Vector3 moved = next - current;
        moved.y = 0f;
        float targetLenSq = toTarget.sqrMagnitude;
        if (targetLenSq > 0.0000001f)
        {
            float along = Vector3.Dot(moved, toTarget);
            if (along > targetLenSq)
            {
                next.x = fingerVisualTarget.x;
                next.z = fingerVisualTarget.z;
                fingerVisualVelocity.x = 0f;
                fingerVisualVelocity.z = 0f;
            }
        }
        else
        {
            next.x = fingerVisualTarget.x;
            next.z = fingerVisualTarget.z;
            fingerVisualVelocity.x = 0f;
            fingerVisualVelocity.z = 0f;
        }

        // Keep seated Y from the constrained target (includes carry lift).
        next.y = fingerVisualTarget.y;
        if (PieceMotionMath.IsFinite(next))
        {
            view.position = next;
        }

        fingerVisualActive = true;
    }

    private void FinishFingerVisualAndSnapToLogical()
    {
        if (!fingerVisualActive && !fingerVisualHasTarget)
        {
            return;
        }

        fingerVisualActive = false;
        fingerVisualHasTarget = false;
        fingerVisualVelocity = Vector3.zero;
        BoardManager board = cachedBoard != null ? cachedBoard : GetBoard();
        IGridSpace space = MotionGridSpace(board);
        if (block == null || block.WorldView == null || space == null)
        {
            return;
        }

        // Seat XZ on logical cell; keep carry lift (WorldPieceMotion still owns settle).
        Vector3 seated = SeatedCellWorld(space, logicalCell);
        if (PieceMotionMath.IsFinite(seated))
        {
            block.WorldView.transform.position = seated;
        }
    }

    private IEnumerator DragRoutine(BoardManager board)
    {
        try
        {
            RectTransform rect = block.RectTransform;
            float duration = Mathf.Max(0.01f, secondsPerCell);

            while (true)
            {
                Vector2Int committed = logicalCell;
                int remainingSteps = AxisSteps(desiredCell - committed, dragDirection);
                if (dragReleased && board.HasNestMatch(block, committed))
                {
                    Vector2Int focus = dragDirection != Vector2Int.zero
                        ? committed + dragDirection
                        : committed;
                    yield return EnterMatchingTarget(board, rect, committed, focus);
                    break;
                }

                if (remainingSteps <= 0)
                {
                    if (dragReleased)
                    {
                        if (TryGetAdjacentMatchingTarget(board, committed, out Vector2Int releaseNestCell))
                        {
                            yield return EnterMatchingTarget(board, rect, committed, releaseNestCell);
                            break;
                        }

                        if (finalSettleDelay > 0f)
                        {
                            yield return Pause(finalSettleDelay);
                        }

                        break;
                    }

                    yield return null;
                    continue;
                }

                if (dragReleased
                    && TryGetAdjacentMatchingTarget(board, committed, out Vector2Int startNestCell))
                {
                    yield return EnterMatchingTarget(board, rect, committed, startNestCell);
                    break;
                }

                // Finger aiming: commit occupancy toward desiredCell without AnimateHop so
                // presentation can follow the finger continuously between cells.
                // Magnet / non-finger drags keep the existing hop animation path.
                if (fingerDrivenDrag && !dragReleased)
                {
                    bool committedAny = false;
                    while (true)
                    {
                        committed = logicalCell;
                        remainingSteps = AxisSteps(desiredCell - committed, dragDirection);
                        if (remainingSteps <= 0)
                        {
                            break;
                        }

                        Vector2Int fingerNext = committed + dragDirection;
                        if (!CanHopInto(board, fingerNext))
                        {
                            if (!hopBlockedCuePlayed)
                            {
                                hopBlockedCuePlayed = true;
                                NotifyBlockedAttempt();
                            }

                            break;
                        }

                        if (!board.TryMoveBlock(block, committed, fingerNext))
                        {
                            break;
                        }

                        logicalCell = fingerNext;
                        hopBlockedCuePlayed = false;
                        hopAnticipatePending = false;
                        committedAny = true;
                        PlayHopSound();
                        block.SetGridPosition(fingerNext, preserveWorldPresentation: true);
                    }

                    if (!committedAny && remainingSteps > 0)
                    {
                        yield return null;
                    }
                    else
                    {
                        yield return null;
                    }

                    continue;
                }

                Vector2Int next = committed + dragDirection;
                if (dragReleased && IsMatchingTargetCell(board, next))
                {
                    yield return EnterMatchingTarget(board, rect, committed, next);
                    break;
                }

                if (!CanHopInto(board, next))
                {
                    if (!hopBlockedCuePlayed)
                    {
                        hopBlockedCuePlayed = true;
                        NotifyBlockedAttempt();
                    }

                    if (dragReleased)
                    {
                        break;
                    }

                    yield return null;
                    continue;
                }

                if (!board.TryMoveBlock(block, committed, next))
                {
                    if (dragReleased)
                    {
                        break;
                    }

                    yield return null;
                    continue;
                }

                logicalCell = next;
                hopBlockedCuePlayed = false;
                PlayHopSound();
                bool anticipate = hopAnticipatePending;
                hopAnticipatePending = false;
                yield return AnimateHop(board, committed, next, duration, anticipate);
                block.SetGridPosition(next);

                if (dragReleased
                    && TryGetAdjacentMatchingTarget(board, next, out Vector2Int nestCell))
                {
                    yield return EnterMatchingTarget(board, rect, next, nestCell);
                    break;
                }
            }
        }
        finally
        {
            fingerVisualActive = false;
            fingerVisualHasTarget = false;
            fingerVisualVelocity = Vector3.zero;
            fingerDrivenDrag = false;
            FinalizeDragUndoSession(board);
            dragActive = false;
            dragReleased = false;
            isMoving = false;
            dragRoutine = null;
            cachedBoard = null;
        }
    }

    private void FinalizeDragUndoSession(BoardManager board)
    {
        BoardUndoHistory undoHistory = BoardUndoHistory.Resolve();
        if (undoHistory == null || board == null || block == null || magnetPresenting)
        {
            undoHistory?.DiscardPending();
            return;
        }

        if (dragSessionMatchEntered)
        {
            undoHistory.DiscardPending();
            return;
        }

        if (logicalCell != dragSessionStart)
        {
            undoHistory.CommitPendingAsActive();
        }
        else
        {
            undoHistory.DiscardPending();
        }
    }

    public void NotifyBlockedAttempt()
    {
        if (audioFeedback != null)
        {
            audioFeedback.PlayBlocked();
        }

        if (hapticFeedback != null)
        {
            hapticFeedback.PlayBlocked();
        }
    }

    private void PlayLogicalMatchFeedback(bool consumedInnerLayer, bool fullyConsumed)
    {
        if (audioFeedback != null)
        {
            audioFeedback.PlayLogicalMatch(consumedInnerLayer, fullyConsumed);
        }

        if (hapticFeedback != null)
        {
            hapticFeedback.PlayLogicalMatch(consumedInnerLayer, fullyConsumed);
        }
    }

    private void PlayDragStartSound()
    {
        if (audioFeedback != null)
        {
            audioFeedback.PlayDragStart();
        }

        if (hapticFeedback != null)
        {
            hapticFeedback.PlayGrab();
        }
    }

    private void PlayHopSound()
    {
        if (audioFeedback != null)
        {
            audioFeedback.PlayHop();
        }

        // Authoritative per successful grid-cell commit (after TryMoveBlock).
        if (hapticFeedback != null)
        {
            hapticFeedback.PlayGridCellMove();
        }
    }

    private void PlayNestEntrySound()
    {
        if (audioFeedback != null)
        {
            audioFeedback.PlayNestEntry();
        }

        if (hapticFeedback != null)
        {
            hapticFeedback.PlayNestEntry();
        }
    }

    private bool CanHopInto(BoardManager board, Vector2Int next)
    {
        if (!board.CanTranslateBlock(block, next))
        {
            return false;
        }

        return !board.FootprintTouchesTarget(block, next);
    }

    private static readonly Vector2Int[] AdjacentCheckOrder =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    private bool TryGetAdjacentMatchingTarget(
        BoardManager board,
        Vector2Int blockPosition,
        out Vector2Int nestCell)
    {
        for (int i = 0; i < AdjacentCheckOrder.Length; i++)
        {
            Vector2Int candidate = blockPosition + AdjacentCheckOrder[i];
            if (!IsMatchingTargetCell(board, candidate))
            {
                continue;
            }

            nestCell = candidate;
            return true;
        }

        nestCell = blockPosition;
        return false;
    }

    private IEnumerator EnterMatchingTarget(
        BoardManager board,
        RectTransform rect,
        Vector2Int from,
        Vector2Int to)
    {
        dragSessionMatchEntered = true;
        BoardUndoHistory.Resolve()?.DiscardPending();

        if (levelManager != null)
        {
            levelManager.BeginPieceMatchSequence();
        }

        // Nest-entry is not Magnet board movement. Clear presentation so the
        // destination highlight does not follow the piece into the nest.
        magnetPresenting = false;

        try
        {
            yield return EnterMatchingTargetBody(board, rect, from, to);
        }
        finally
        {
            if (levelManager != null)
            {
                levelManager.EndPieceMatchSequence();
            }
        }
    }

    private IEnumerator EnterMatchingTargetBody(
        BoardManager board,
        RectTransform rect,
        Vector2Int from,
        Vector2Int to)
    {
        // Nested pieces match their outermost layer through the focused-cell path so the
        // Block occupancy stays at SOURCE. TryMoveBlock onto the nest would promote the
        // inner at the target (Phase 71B). Multi-cell matching/grouping is unchanged.
        if (block != null && (block.CellCount > 1 || BlockHasAnyNestedInner(block)))
        {
            yield return EnterChainPartialMatch(board, block, from, to);
            yield break;
        }

        Vector2Int occupancyTo = board.HasNestMatch(block, from) ? from : to;
        bool keepHighlight = !dragReleased || matchEntryVisualActive;
        dragReleased = true;
        LogDrag($"Matching magnet {from} -> {occupancyTo}");

        Target nestTarget = null;
        board.CollectNestMatches(block, occupancyTo, nestCellIndices, nestTargets);
        SyncNestTargetWorldsFromOccupying(block, occupancyTo);
        if (nestTargets.Count > 0)
        {
            nestTarget = nestTargets[0];
            nestTarget.ShowReadyFeedback();
        }

        if (!board.TryMoveBlock(block, from, occupancyTo))
        {
            if (nestTarget != null)
            {
                nestTarget.HideReadyFeedback();
            }

            yield break;
        }

        logicalCell = occupancyTo;
        if (keepHighlight)
        {
            matchEntryVisualActive = true;
            visualHopFrom = from;
            visualHopActive = true;
        }

        try
        {
            block.CancelDragSelectionImmediate();
            PlayNestEntrySound();

            Vector2 restPosition = board.GridToLocal(from);
            Vector3 restScale = MotionRestScale(block);
            IPieceMotion motion = MotionFor(block);
            SnapMotionToCell(motion, board, from);
            ApplyMotionRestScale(block, restScale);

            yield return Pause(matchingTargetPause);
            if (nestTarget != null)
            {
                nestTarget.HideReadyFeedback();
            }

            yield return AnimateAnticipation(board, restPosition, restScale);
            yield return AnimateNestEntry(board, from, occupancyTo, restScale);

            visualHopActive = false;

            block.SetGridPosition(occupancyTo, preserveWorldPresentation: true);

            board.CollectNestMatches(block, occupancyTo, nestCellIndices, nestTargets);
            SyncNestTargetWorldsFromOccupying(block, occupancyTo);
            if (nestCellIndices.Count == 0)
            {
                yield break;
            }

            yield return ResolveCellMatches(board, from, occupancyTo, nestTarget);
        }
        finally
        {
            visualHopActive = false;
            matchEntryVisualActive = false;
        }
    }

    private IEnumerator EnterChainPartialMatch(
        BoardManager board,
        Block subject,
        Vector2Int from,
        Vector2Int focus)
    {
        bool keepHighlight = !dragReleased || matchEntryVisualActive;
        dragReleased = true;
        matchEntryVisualActive = keepHighlight;
        try
        {
            LogDrag($"Chain partial match {from} focus={focus}");
            yield return MatchFocusedChainCell(board, subject, from, focus, false);
            EnsureSubjectOccupancy(board, subject);
            yield return ResolveAlreadyAlignedMatches(board);
        }
        finally
        {
            matchEntryVisualActive = false;
        }
    }

    private IEnumerator MatchFocusedChainCell(
        BoardManager board,
        Block subject,
        Vector2Int from,
        Vector2Int focus,
        bool occupyingOnly)
    {
        if (subject == null || board == null)
        {
            yield break;
        }

        matchSequenceIndex++;
        int matchId = matchSequenceIndex;
       // Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} START");

        RectTransform rect = subject.RectTransform;
        Vector2Int occupancy = subject.GridPosition;
        IPieceMotion subjectMotion = MotionFor(subject);
        SnapMotionToCell(subjectMotion, board, occupancy);
        if (subject.View != null)
        {
            subject.View.LocalScale = subject.RestScale;
        }
        if (subject == block)
        {
            logicalCell = occupancy;
        }

        if (!CollectChainFocusedMatch(board, subject, occupancy, focus, occupyingOnly, out Vector2Int targetWorld))
        {
            // Debug.Log(
            //     $"REJECT MatchFocusedChainCell CollectChainFocusedMatch failed: " +
            //     $"Block={subject.GetInstanceID()} occupancy={occupancy} focus={focus} " +
            //     $"occupyingOnly={occupyingOnly} CellCount={subject.CellCount}");
            for (int i = 0; i < subject.CellCount; i++)
            {
                Vector2Int world = occupancy + subject.GetLocalCell(i);
                Target t = board.GetTargetAt(world);
                // Debug.Log(
                //     $"  cell[{i}] world={world} shape={subject.GetActiveShape(i)} " +
                //     $"target={(t != null ? t.RequiredShape.ToString() : "NULL")} " +
                //     $"occ={(board.GetBlockAt(world) != null ? board.GetBlockAt(world).GetInstanceID().ToString() : "NULL")}");
            }

            yield break;
        }

        // Phase 73: when 2+ cells share one rigid translation into matching targets,
        // travel them together (nested and non-nested). Falls back to one-cell travel
        // only when CanTranslateMatchingSubset rejects the group.
        if (nestCellIndices.Count >= 2)
        {
            if (TryPlayNestedSubsetMatch(board, subject, occupancy, out AlignedMovementGroup rigidGroup))
            {
                yield return PlayMatchingSubsetAlignedMatch(board, subject, rigidGroup);
                yield break;
            }

            if (BlockHasAnyNestedInner(subject))
            {
                PreferNestedCellForSingleMatch(subject);
            }
            else
            {
                KeepOnlyNearestMatch(subject, occupancy, focus);
            }
        }

        if (nestCellIndices.Count == 0)
        {
            yield break;
        }

        int cellIndex = nestCellIndices[0];
        Vector2Int cellWorld = occupancy + subject.GetLocalCell(cellIndex);
        Target focusedTarget = nestTargets.Count > 0 ? nestTargets[0] : null;
        targetWorld = nestTargetWorlds.Count > 0 ? nestTargetWorlds[0] : targetWorld;
        // Outer-first matching: nest entry always travels the active outer shell.
        // Nested children are promoted after consume and are not travelers here.
        bool travelInnerLayer = false;
        subject.CancelDragSelectionImmediate();
        PieceGameplayVisuals.ClearConnectors(rect);

        // Single-cell fallback: traveler-only; siblings stay at source occupancy.
        // (1×1 magnet still uses TryMoveBlock in EnterMatchingTargetBody.)
        // Multi-cell rigid travel uses PlayMatchingSubsetAlignedMatch above (Phase 73).
        LogChainMatchCells("BEFORE", subject);

        pendingCellTraveler = null;
        yield return PlayChainCellNestEntry(board, subject, cellIndex, travelInnerLayer, cellWorld, targetWorld);
        RectTransform landedTraveler = pendingCellTraveler;
        pendingCellTraveler = null;
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} LAND");

        // Re-resolve the focused cell after the traveler. Do not use GetTargetAt(cellWorld)
        // as the destination authority when targetWorld differs from the source cell.
        cellIndex = FindCellIndexAtWorld(subject, cellWorld);
        if (!IsFocusedChainConsumeValid(subject, cellIndex, focusedTarget, targetWorld))
        {
            // Debug.Log(
            //     $"REJECT MatchFocusedChainCell post-traveler consume invalid: " +
            //     $"cellIndex={cellIndex} cellWorld={cellWorld} " +
            //     $"target={(focusedTarget != null ? focusedTarget.GetInstanceID().ToString() : "NULL")} " +
            //     $"required={(focusedTarget != null ? focusedTarget.RequiredShape.ToString() : "n/a")} " +
            //     $"active={(cellIndex >= 0 && cellIndex < subject.CellCount ? subject.GetActiveShape(cellIndex).ToString() : "n/a")} " +
            //     $"settled={subject.IsSettled}");
            if (cellIndex >= 0)
            {
                subject.ClearTravelState(cellIndex);
                subject.SetCellVisualVisible(cellIndex, true);
            }

            subject.RefreshLayoutVisuals();
            DestroyLandedTraveler(landedTraveler);
            yield break;
        }

        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();
        nestCellIndices.Add(cellIndex);
        nestTargets.Add(focusedTarget);
        nestTargetWorlds.Add(targetWorld);

       // Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} CONSUME");
        bool fullyConsumed = ConsumeAndRebuild(
            board,
            subject,
            subject.GridPosition,
            out Target completedTarget,
            out ShapeType consumedShape,
            out Vector2Int _,
            out bool consumedInnerLayer);
        if (subject == block)
        {
            logicalCell = block != null ? block.GridPosition : logicalCell;
        }

        EnsureSubjectOccupancy(board, subject);
        // Debug.Log(
        //     "[AUTO CHAIN SEQUENCE]\n" +
        //     $"Consumed cell: {cellWorld}\n" +
        //     $"Consumed shape: {consumedShape}\n" +
        //     $"Fully consumed block: {fullyConsumed}");
        LogChainMatchCells("AFTER", subject);
        LogChainAutoMatchPostMatch(board, subject, cellWorld, consumedShape);

        yield return PlayMatchEffect(
            board,
            targetWorld,
            consumedShape,
            completedTarget,
            fullyConsumed ? subject : null,
            matchId);

        DestroyLandedTraveler(landedTraveler);
        // Phase 71B: keep the outer traveler locked at the TARGET until extraction
        // reveal hides it and seats the inner at SOURCE. Clearing travel here lets
        // LateUpdate snap the still-outer mesh onto the residual.
        bool deferTravelClear = consumedInnerLayer && subject != null && !subject.IsSettled;
        if (!deferTravelClear)
        {
            subject.ClearTravelState(cellIndex);
        }
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} CLEANUP COMPLETE");

        // Re-assert survivor occupancy after VFX; MatchEffect must not leave the
        // board unable to see an already-aligned remaining cell.
        EnsureSubjectOccupancy(board, subject);

        // TEMP DIAGNOSTIC: state the auto-match queue will see on the next scan.
        LogPostConsumeAutoMatchTrace(board, subject, cellWorld, targetWorld, fullyConsumed);
        if (!fullyConsumed && subject != null && !subject.IsSettled)
        {
            LogPostFirstMatchState(board, subject);
        }

        RememberLastMatch(cellWorld, targetWorld);

        // Phase 64: outer consumption promotes nested children to normal-sized pieces.
        // Always reveal every pending cell — a single-index reveal leaves sibling chain
        // cells stuck on pre-promote visuals (outer + nested inner ghost).
        if (consumedInnerLayer && subject != null && !subject.IsSettled)
        {
            yield return PlayAllPendingNestedExtractionReveals(subject);
            subject.ClearTravelState(cellIndex);
            SeatPromotedNestedViewsAtSource(subject);
            ForceUnlockPieceViews(subject);
            SeatPromotedNestedViewsAtSource(subject);
        }
    }

    /// <summary>
    /// After outer-layer match VFX: seat the promoted inner at its logical cell, rebuild
    /// full-size standalone presentation, then ease 0.82 → 1.04 → 1.00. No teleport.
    /// </summary>
    private IEnumerator PlayNestedExtractionReveal(Block subject, int cellIndex)
    {
        if (subject == null || subject.IsSettled)
        {
            yield break;
        }

        if (cellIndex < 0 || cellIndex >= subject.CellCount)
        {
            cellIndex = subject.AnchorCellIndex;
        }

        PieceView3D view = subject.GetWorldViewForCellIndex(cellIndex);
        if (view == null)
        {
            view = subject.WorldView;
        }

        if (view == null)
        {
            subject.ClearPendingLayerExtraction(cellIndex);
            BoardPresentationController.NotifyNestedLayerPromoted(subject, cellIndex);
            yield break;
        }

        // Keep motion-locked through seating + mesh promote so LateUpdate cannot flash/snap.
        // Phase 69B: never use ApplyGridPosition while locked — seat with SnapWorldPresentationToGrid
        // at the remembered SOURCE cell (residual authority), not the post-travel target pose.
        bool lockedBefore = view.IsMotionLocked;
        view.BeginMotionLock();
        try
        {
            BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
            IGridSpace space = presenter != null ? presenter.GridSpace : null;
            Vector2Int sourceCell = BoardPresentationController.ResolveNestedPromotionSourceCell(
                subject,
                cellIndex);
            if (sourceCell == default)
            {
                sourceCell = subject.GetCellWorld(cellIndex);
            }

            Vector3 viewWorldBeforeSeat = view.transform.position;

            // Phase 71B: kill nest-entry tweens first — a late arc/sit callback can drag the
            // view back onto the TARGET after SnapWorldPresentationToGrid.
            TweenAnimationUtility.KillById(view.gameObject, TweenAnimationUtility.TravelerId, false);
            TweenAnimationUtility.KillTransform(view.transform, complete: false);

            // Hide the outer traveler in-place, then seat at SOURCE in the same frame so the
            // inner is never shown scaling at the target. Residual covers SOURCE until
            // NotifyNestedLayerPromoted replaces it with the standalone inner.
            view.SetPresentationAnticipation(0f, 0f, 0f);
            if (space != null)
            {
                view.SnapWorldPresentationToGrid(space, sourceCell);
            }

            Phase69AForensic.LogRevealSeating(
                subject,
                cellIndex,
                view,
                sourceCell,
                lockedBefore,
                view.IsMotionLocked,
                viewWorldBeforeSeat,
                view.transform.position);

            // Promote mesh now that outer match VFX is done — full-size standalone at SOURCE.
            // Clears residual + remeshes traveler to the logical survivor (no consumed-outer ghost).
            Phase68CForensic.LogCell("REVEAL_BEFORE_PROMOTE", subject, cellIndex);
            BoardPresentationController.NotifyNestedLayerPromoted(subject, cellIndex);
            subject.ClearPendingLayerExtraction(cellIndex);
            subject.SetCellVisualVisible(cellIndex, true);
            // Promote remeshed while held inactive — release now so the reveal tween shows RED.
            BoardPresentationController.ReleasePromotedExtractionView(view);
            Phase68CForensic.LogCell("REVEAL_AFTER_PROMOTE", subject, cellIndex);
            Phase68CForensic.DumpDuplicates(subject, cellIndex);

            const float startMul = 0.82f;
            const float peakMul = 1.04f;
            const float endMul = 1f;
            const float riseDuration = 0.10f;
            const float settleDuration = 0.06f;

            TweenAnimationUtility.KillById(view.gameObject, TweenAnimationUtility.NestedExtractionId, false);
            view.SetPresentationAnticipation(0f, startMul, 0f);

            Sequence reveal = DOTween.Sequence()
                .SetId(TweenAnimationUtility.NestedExtractionId)
                .SetLink(view.gameObject);
            reveal.Append(TweenAnimationUtility.Progress(riseDuration, t =>
            {
                float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
                float mul = Mathf.LerpUnclamped(startMul, peakMul, eased);
                view.SetPresentationAnticipation(0f, mul, 0f);
            }));
            reveal.Append(TweenAnimationUtility.Progress(settleDuration, t =>
            {
                float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
                float mul = Mathf.LerpUnclamped(peakMul, endMul, eased);
                view.SetPresentationAnticipation(0f, mul, 0f);
            }));
            yield return TweenAnimationUtility.Wait(reveal);
            view.SetPresentationAnticipation(0f, endMul, 0f);
        }
        finally
        {
            subject.ClearPendingLayerExtraction(cellIndex);
            view.EndMotionLock();
        }
    }

    private static void DestroyLandedTraveler(RectTransform traveler)
    {
        if (traveler != null)
        {
            Object.Destroy(traveler.gameObject);
        }
    }

    private static void LogChainMatchCells(string phase, Block subject)
    {
        if (subject == null || subject.IsSettled)
        {
           // Debug.Log($"[CHAIN MATCH {phase}]\n(none)");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[CHAIN MATCH {phase}]");
        int count = Mathf.Max(1, subject.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = subject.GridPosition + subject.GetLocalCell(i);
            sb.AppendLine($"Cell {i} = {world} shape={subject.GetActiveShape(i)}");
        }

        //Debug.Log(sb.ToString());
    }

    /// <summary>True when any cell still has a nested inner layer (Phase 71B outer-only travel).</summary>
    private static bool BlockHasAnyNestedInner(Block subject)
    {
        if (subject == null)
        {
            return false;
        }

        int count = Mathf.Max(1, subject.CellCount);
        for (int i = 0; i < count; i++)
        {
            if (subject.HasInnerLayerAt(i))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Repairs footprint occupancy for a live block. Used after split/rebuild and by the auto-match queue.</summary>
    public static void EnsureSubjectOccupancy(BoardManager board, Block subject)
    {
        if (board == null || subject == null || subject.IsSettled)
        {
            return;
        }

        int count = Mathf.Max(1, subject.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = subject.GridPosition + subject.GetLocalCell(i);
            if (board.GetBlockAt(world) != subject)
            {
                board.TryRegisterBlock(subject, subject.GridPosition);
                return;
            }
        }
    }

    private static bool IsFocusedChainConsumeValid(
        Block subject,
        int cellIndex,
        Target focusedTarget,
        Vector2Int targetWorld)
    {
        return subject != null
            && !subject.IsSettled
            && cellIndex >= 0
            && cellIndex < subject.CellCount
            && focusedTarget != null
            && focusedTarget.isActiveAndEnabled
            && ShapeMatch.AreMatchingLayers(
                focusedTarget.GetRequiredIdentityAtWorld(targetWorld),
                subject.GetActiveIdentity(cellIndex));
    }

    private bool CollectChainFocusedMatch(
        BoardManager board,
        Block subject,
        Vector2Int occupancy,
        Vector2Int focus,
        bool occupyingOnly,
        out Vector2Int targetWorld)
    {
        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();
        targetWorld = occupancy;
        if (subject == null || board == null)
        {
            return false;
        }

        int count = subject.CellCount;
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = occupancy + subject.GetLocalCell(i);
            Target target = board.GetTargetAt(world);
            if (target == null
                || !ShapeMatch.AreMatchingLayers(
                    target.GetRequiredIdentityAtWorld(world),
                    subject.GetActiveIdentity(i)))
            {
                continue;
            }

            nestCellIndices.Add(i);
            nestTargets.Add(target);
            nestTargetWorlds.Add(world);
        }

        if (nestCellIndices.Count > 0)
        {
            if (occupyingOnly)
            {
                KeepOnlyCellAtWorld(subject, occupancy, focus);
            }
            else
            {
                // Phase 73: keep every match that shares the focus-nearest cell's
                // translation so the chain can travel as one rigid group. Nested and
                // plain multi-cell chains share this path (was nested-only in 71B).
                KeepSameTranslationMatches(subject, occupancy, focus);
            }

            if (nestCellIndices.Count == 0)
            {
                return false;
            }

            int index = nestCellIndices[0];
            targetWorld = nestTargetWorlds.Count > 0
                ? nestTargetWorlds[0]
                : occupancy + subject.GetLocalCell(index);
            return true;
        }

        if (occupyingOnly)
        {
            return false;
        }

        // Phase 73: infer ONE axis-aligned translation from focus, then collect every
        // chain cell that maps to a matching target under that same translation.
        Vector2Int inferredTranslation = Vector2Int.zero;
        bool haveTranslation = false;

        Vector2Int delta = focus - occupancy;
        bool unitCardinal = delta == Vector2Int.up
            || delta == Vector2Int.down
            || delta == Vector2Int.left
            || delta == Vector2Int.right;
        if (unitCardinal)
        {
            inferredTranslation = delta;
            haveTranslation = true;
        }
        else
        {
            int bestDist = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Vector2Int world = occupancy + subject.GetLocalCell(i);
                Target focusTarget = board.GetTargetAt(focus);
                if (focusTarget == null
                    || !ShapeMatch.AreMatchingLayers(
                        focusTarget.GetRequiredIdentityAtWorld(focus),
                        subject.GetActiveIdentity(i)))
                {
                    continue;
                }

                Vector2Int candidate = focus - world;
                if ((candidate.x != 0 && candidate.y != 0) || candidate == Vector2Int.zero)
                {
                    continue;
                }

                int dist = Mathf.Abs(candidate.x) + Mathf.Abs(candidate.y);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    inferredTranslation = candidate;
                    haveTranslation = true;
                }
            }
        }

        if (!haveTranslation)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            Vector2Int world = occupancy + subject.GetLocalCell(i);
            Vector2Int dest = world + inferredTranslation;
            Target target = board.GetTargetAt(dest);
            if (target == null
                || !ShapeMatch.AreMatchingLayers(
                    target.GetRequiredIdentityAtWorld(dest),
                    subject.GetActiveIdentity(i)))
            {
                continue;
            }

            nestCellIndices.Add(i);
            nestTargets.Add(target);
            nestTargetWorlds.Add(dest);
        }

        if (nestCellIndices.Count == 0)
        {
            return false;
        }

        KeepSameTranslationMatches(subject, occupancy, focus);

        if (nestCellIndices.Count == 0)
        {
            return false;
        }

        targetWorld = nestTargetWorlds.Count > 0
            ? nestTargetWorlds[0]
            : occupancy + subject.GetLocalCell(nestCellIndices[0]) + inferredTranslation;
        return true;
    }

    private IEnumerator PlayChainCellNestEntry(
         BoardManager board,
         Block subject,
         int cellIndex,
         bool innerLayer,
         Vector2Int cellWorld,
         Vector2Int targetWorld)
    {
        if (innerLayer)
        {
            yield return PlayChainInnerNestEntry(board, subject, cellIndex, cellWorld, targetWorld);
            yield break;
        }

        if (BoardPresentationController.SuppressGameplayPieceUiImages())
        {
            yield return PlayChainCellNestEntryWorld3D(board, subject, cellIndex, cellWorld, targetWorld);
            yield break;
        }

        Target nestTarget = nestTargets.Count > 0 ? nestTargets[0] : null;
        if (nestTarget != null)
        {
            nestTarget.ShowReadyFeedback();
        }

        PlayNestEntrySound();

        // 1. Instantiate traveler sprite FIRST while the cell visual is still visible
        RectTransform traveler = CreateTravelerCell(board, subject, cellIndex, cellWorld);
        pendingCellTraveler = traveler;

        // 2. Hide original cell visual on the block now that traveler exists
        subject.SetCellVisualVisible(cellIndex, false);

        Vector2 startPos = board.GridToLocal(cellWorld);
        Vector2 targetPos = board.GridToLocal(targetWorld);

        // 3. Pre-flight anticipation lift
        if (matchingTargetAnticipateDuration > 0f)
        {
            Vector3 baseScale = traveler != null ? traveler.localScale : subject.RestScale;
            Tween anticipate = TweenAnimationUtility.Progress(matchingTargetAnticipateDuration, t =>
            {
                if (traveler == null)
                {
                    return;
                }

                float lift = t * (board.CellSize * matchingTargetAnticipateLiftPercent);
                traveler.anchoredPosition = startPos + new Vector2(0f, lift);
                traveler.localScale = Vector3.Lerp(baseScale, baseScale * matchingTargetAnticipateScale, t);
            }).SetId(TweenAnimationUtility.TravelerId).SetLink(traveler != null ? traveler.gameObject : gameObject);
            yield return TweenAnimationUtility.Wait(anticipate);
        }

        // 4. Arc animation into the matching target cell
        float duration = Mathf.Max(0.01f, matchingTargetArcDuration);
        Vector3 arcStartScale = subject.RestScale * matchingTargetAnticipateScale;
        Vector3 arcEndScale = subject.RestScale * matchingTargetHopScale;
        Tween arc = TweenAnimationUtility.Progress(duration, t =>
        {
            if (traveler == null)
            {
                return;
            }

            Vector2 currentPos = Vector2.Lerp(startPos, targetPos, t);
            float arcLift = Mathf.Sin(t * Mathf.PI) * (board.CellSize * matchingTargetLiftPercent);
            currentPos.y += arcLift;
            traveler.anchoredPosition = currentPos;
            traveler.localScale = Vector3.Lerp(arcStartScale, arcEndScale, t);
        }).SetId(TweenAnimationUtility.TravelerId).SetLink(traveler != null ? traveler.gameObject : gameObject);
        yield return TweenAnimationUtility.Wait(arc);

        // Snap traveler to exact target coordinates upon landing
        if (traveler != null)
        {
            traveler.anchoredPosition = targetPos;
            traveler.localScale = subject.RestScale;
        }

        if (matchingTargetSitDuration > 0f)
        {
            yield return Pause(matchingTargetSitDuration);
        }

        if (nestTarget != null)
        {
            nestTarget.HideReadyFeedback();
        }
    }

    private IEnumerator PlayChainCellNestEntryWorld3D(
        BoardManager board,
        Block subject,
        int cellIndex,
        Vector2Int cellWorld,
        Vector2Int targetWorld)
    {
        Target nestTarget = nestTargets.Count > 0 ? nestTargets[0] : null;
        if (nestTarget != null)
        {
            nestTarget.ShowReadyFeedback();
        }

        PlayNestEntrySound();
        pendingCellTraveler = null;

        PieceView3D travelView = subject != null ? subject.GetWorldViewForCellIndex(cellIndex) : null;
        if (travelView == null)
        {
            if (nestTarget != null)
            {
                nestTarget.HideReadyFeedback();
            }

            yield break;
        }

        if (subject.HasInnerLayerAt(cellIndex))
        {
            Phase68CForensic.LogCell("FOCUSED_PRE_DETACH", subject, cellIndex);
            BoardPresentationController.DetachAndAnchorNestedInner(subject, cellIndex);
            Phase68CForensic.LogCell("FOCUSED_POST_DETACH", subject, cellIndex);
        }

        BoardPresentationController.BeginChainCellTravel(subject, travelView, cellIndex);
        subject.SetCellVisualVisible(cellIndex, false);
        Phase68CForensic.LogCell("FOCUSED_TRAVEL_START", subject, cellIndex, $"target={targetWorld}");

        if (!TryGetWorldMotion(subject, out WorldPieceMotion worldMotion))
        {
            BoardPresentationController.NotifyChainCellTravelCleared(subject);
            if (nestTarget != null)
            {
                nestTarget.HideReadyFeedback();
            }

            yield break;
        }

        BoardPresenter3D presenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace space = presenter != null ? presenter.GridSpace : MotionGridSpace(board);
        worldMotion.Bind(travelView, space);

        Vector3 restScale = travelView.ConfiguredFootprintScale;
        yield return worldMotion.AnimateNestAnticipate(
            MotionCellSize(board),
            Vector2.zero,
            restScale,
            matchingTargetAnticipateDuration,
            matchingTargetAnticipateLiftPercent,
            matchingTargetAnticipateScale);
        yield return worldMotion.AnimateNestEntry(
            space,
            MotionCellSize(board),
            cellWorld,
            targetWorld,
            restScale,
            matchingTargetLiftPercent,
            matchingTargetArcDuration,
            matchingTargetSitDuration,
            matchingTargetHopScale);

        if (nestTarget != null)
        {
            nestTarget.HideReadyFeedback();
        }
    }

    private IEnumerator PlayChainInnerNestEntry(
        BoardManager board,
        Block subject,
        int cellIndex,
        Vector2Int cellWorld,
        Vector2Int targetWorld)
    {
        if (BoardPresentationController.SuppressGameplayPieceUiImages())
        {
            yield return PlayChainInnerNestEntryWorld3D(board, subject, cellIndex, cellWorld, targetWorld);
            yield break;
        }

        Target nestTarget = nestTargets.Count > 0 ? nestTargets[0] : null;
        if (nestTarget != null)
        {
            nestTarget.ShowReadyFeedback();
        }

        PlayNestEntrySound();

        Image cellImage = subject.GetCellImage(cellIndex);
        if (cellImage != null)
        {
            PieceGameplayVisuals.HideInnerOverlay(cellImage.transform);
        }

        PieceGameplayVisuals.NestedInnerLook look = subject.NestedInnerLook;
        RectTransform traveler = PieceGameplayVisuals.CreateTravelingInner(
            (RectTransform)board.transform,
            subject.ContainedInnerSprite(),
            subject.VisualSizeDelta,
            (Vector2)board.GridToLocal(cellWorld) + look.offset,
            look);

        if (traveler == null)
        {
            yield return Pause(matchingTargetPause);
            if (nestTarget != null)
            {
                nestTarget.HideReadyFeedback();
            }

            yield break;
        }

        pendingCellTraveler = traveler;
        Vector3 containedScale = subject.RestScale * look.scale;
        Vector3 emergedScale = subject.RestScale;
        traveler.localScale = containedScale;

        if (look.emergeDuration > 0f)
        {
            Vector2 emergeEnd = Vector2.Lerp(
                (Vector2)board.GridToLocal(cellWorld) + look.offset,
                (Vector2)board.GridToLocal(targetWorld),
                0.12f);
            yield return AnimateTraveler(
                traveler,
                traveler.anchoredPosition,
                emergeEnd,
                look.emergeDuration,
                containedScale,
                emergedScale,
                false);
        }
        else
        {
            traveler.localScale = emergedScale;
        }

        yield return Pause(matchingTargetPause);
        if (nestTarget != null)
        {
            nestTarget.HideReadyFeedback();
        }

        float liftAmount = board.VisualCellSize.y * matchingTargetAnticipateLiftPercent;
        Vector2 lifted = traveler.anchoredPosition + new Vector2(0f, liftAmount);
        Vector3 pumped = emergedScale * matchingTargetAnticipateScale;
        yield return AnimateTraveler(
            traveler,
            traveler.anchoredPosition,
            lifted,
            matchingTargetAnticipateDuration,
            emergedScale,
            pumped,
            false);

        Vector2 start = traveler.anchoredPosition;
        Vector2 end = board.GridToLocal(targetWorld);
        Vector2 control = (((Vector2)board.GridToLocal(cellWorld) + end) * 0.5f)
            + new Vector2(0f, board.VisualCellSize.y * matchingTargetLiftPercent);
        Vector3 hopScale = emergedScale * matchingTargetHopScale;
        float duration = Mathf.Max(0.01f, matchingTargetArcDuration);
        Tween arc = TweenAnimationUtility.Progress(duration, t =>
        {
            float eased = PieceMotionMath.EaseInOutCubic(t);
            traveler.anchoredPosition = PieceMotionMath.QuadraticBezier(start, control, end, eased);
            traveler.localScale = Vector3.LerpUnclamped(emergedScale, hopScale, Mathf.Sin(t * Mathf.PI));
        }).SetId(TweenAnimationUtility.TravelerId).SetLink(traveler.gameObject);
        yield return TweenAnimationUtility.Wait(arc);

        yield return AnimateTraveler(
            traveler,
            traveler.anchoredPosition,
            end,
            matchingTargetSitDuration,
            traveler.localScale,
            emergedScale,
            true);
        traveler.anchoredPosition = end;
        traveler.localScale = emergedScale;
    }

    private IEnumerator PlayChainInnerNestEntryWorld3D(
        BoardManager board,
        Block subject,
        int cellIndex,
        Vector2Int cellWorld,
        Vector2Int targetWorld)
    {
        Target nestTarget = nestTargets.Count > 0 ? nestTargets[0] : null;
        if (nestTarget != null)
        {
            nestTarget.ShowReadyFeedback();
        }

        PlayNestEntrySound();
        pendingCellTraveler = null;

        PieceView3D travelView = BoardPresentationController.BeginNestedInnerTravel(subject, cellIndex);
        if (travelView == null)
        {
            if (nestTarget != null)
            {
                nestTarget.HideReadyFeedback();
            }

            yield break;
        }

        if (!TryGetWorldMotion(subject, out WorldPieceMotion worldMotion))
        {
            BoardPresentationController.CancelNestedInnerTravel(subject, cellIndex);
            if (nestTarget != null)
            {
                nestTarget.HideReadyFeedback();
            }

            yield break;
        }

        BoardPresenter3D presenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace space = presenter != null ? presenter.GridSpace : MotionGridSpace(board);
        worldMotion.Bind(travelView, space);

        Vector3 emergedScale = travelView.ConfiguredFootprintScale;
        Vector3 containedScale = emergedScale * subject.NestedInnerLook.scale;
        travelView.LocalScale = containedScale;
        float emergeDuration = subject.NestedInnerLook.emergeDuration;
        if (emergeDuration > 0f)
        {
            Tween emerge = TweenAnimationUtility.Progress(emergeDuration, t =>
            {
                if (travelView == null)
                {
                    return;
                }

                travelView.LocalScale = Vector3.LerpUnclamped(containedScale, emergedScale, t);
            }).SetId(TweenAnimationUtility.TravelerId).SetLink(travelView.gameObject);
            yield return TweenAnimationUtility.Wait(emerge);
        }
        else
        {
            travelView.LocalScale = emergedScale;
        }

        yield return worldMotion.AnimateNestAnticipate(
            MotionCellSize(board),
            Vector2.zero,
            emergedScale,
            matchingTargetAnticipateDuration,
            matchingTargetAnticipateLiftPercent,
            matchingTargetAnticipateScale);
        yield return worldMotion.AnimateNestEntry(
            space,
            MotionCellSize(board),
            cellWorld,
            targetWorld,
            emergedScale,
            matchingTargetLiftPercent,
            matchingTargetArcDuration,
            matchingTargetSitDuration,
            matchingTargetHopScale);

        if (nestTarget != null)
        {
            nestTarget.HideReadyFeedback();
        }
    }

    private RectTransform CreateTravelerCell(BoardManager board, Block sourceBlock, int cellIndex, Vector2Int worldPos)
    {
        if (sourceBlock == null || board == null || cellIndex < 0 || cellIndex >= sourceBlock.CellCount)
        {
            return null;
        }

        Image sourceImage = sourceBlock.GetCellImage(cellIndex);
        if (sourceImage == null)
        {
            return PieceGameplayVisuals.CreateTravelingSprite(
                (RectTransform)board.transform,
                sourceBlock.GetCellOuterSprite(cellIndex),
                sourceBlock.VisualSizeDelta,
                board.GridToLocal(worldPos));
        }

        // The anchor cell is the Block root, so clone only its Image instead of
        // cloning the Block/BlockMover hierarchy. Extra cells can retain their
        // nested overlay hierarchy when cloned from the resolved cell image.
        GameObject travelerObj;
        if (sourceImage.gameObject == sourceBlock.gameObject)
        {
            RectTransform traveler = PieceGameplayVisuals.CreateTravelingSprite(
                (RectTransform)board.transform,
                sourceBlock.GetCellOuterSprite(cellIndex),
                sourceBlock.VisualSizeDelta,
                board.GridToLocal(worldPos));
            if (traveler == null)
            {
                return null;
            }

            traveler.localScale = sourceBlock.RestScale;
            return traveler;
        }

        travelerObj = Instantiate(sourceImage.gameObject, board.transform);
        travelerObj.SetActive(true);

        // Ensure all visual graphics on the traveler are active and non-raycastable
        Graphic[] graphics = travelerObj.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            graphics[i].gameObject.SetActive(true);
            graphics[i].raycastTarget = false;
        }

        RectTransform travelerRect = travelerObj.GetComponent<RectTransform>();
        travelerRect.anchoredPosition = board.GridToLocal(worldPos);
        travelerRect.localScale = sourceBlock.RestScale;

        return travelerRect;
    }
    
    private IEnumerator EnterNestedInnerThenOuter(
        BoardManager board,
        RectTransform rect,
        Vector2Int from,
        Vector2Int to)
    {
        bool keepHighlight = !dragReleased || matchEntryVisualActive;
        dragReleased = true;
        LogDrag($"Nested inner nest {from} -> {to}");

        board.CollectNestMatches(block, to, nestCellIndices, nestTargets);
        KeepOnlyFirstMatch();
        SyncNestTargetWorldsFromOccupying(block, to);
        if (nestCellIndices.Count == 0)
        {
            yield break;
        }

        matchEntryVisualActive = keepHighlight;
        try
        {
            yield return EnterNestedInnerThenOuterBody(board, rect, from, to);
        }
        finally
        {
            matchEntryVisualActive = false;
        }
    }

    private IEnumerator EnterNestedInnerThenOuterBody(
        BoardManager board,
        RectTransform rect,
        Vector2Int from,
        Vector2Int to)
    {

        Target nestTarget = nestTargets[0];
        if (nestTarget != null)
        {
            nestTarget.ShowReadyFeedback();
        }

        block.CancelDragSelectionImmediate();
        block.HideContainedInnerVisuals();
        PlayNestEntrySound();

        Vector2 restPosition = board.GridToLocal(from);
        Vector3 restScale = MotionRestScale(block);
        SnapMotionToCell(MotionFor(block), board, from);
        ApplyMotionRestScale(block, restScale);

        yield return PresentInnerEmergenceAndEntry(board, rect, from, to, restPosition, restScale);
        RectTransform landedTraveler = pendingCellTraveler;
        pendingCellTraveler = null;

        if (nestTarget != null)
        {
            nestTarget.HideReadyFeedback();
        }

        board.CollectNestMatches(block, to, nestCellIndices, nestTargets);
        KeepOnlyFirstMatch();
        SyncNestTargetWorldsFromOccupying(block, to);

        logicalCell = from;
        block.SetGridPosition(from);
        board.TryMoveBlock(block, block.GridPosition, from);

        matchSequenceIndex++;
        int matchId = matchSequenceIndex;
       // Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} LAND");
    // Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} CONSUME");

        bool fullyConsumed = ConsumeAndRebuild(
            board,
            block,
            from,
            out Target completedTarget,
            out ShapeType consumedShape,
            out Vector2Int _,
            out bool consumedInnerLayer);

        logicalCell = block.GridPosition;
        RememberLastMatch(from, to);
        yield return PlayMatchEffect(
            board,
            to,
            consumedShape,
            completedTarget,
            fullyConsumed ? block : null,
            matchId);

        DestroyLandedTraveler(landedTraveler);
        BoardPresentationController.NotifyChainCellTravelCleared(block);
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} CLEANUP COMPLETE");

        if (fullyConsumed || block == null || block.IsSettled)
        {
            if (!IsAutoMatchRunning && levelManager != null)
            {
                levelManager.NotifyBlockSettled();
            }

            yield break;
        }

        // Phase 64: reveal promoted inner at full size; player must drag manually.
        if (consumedInnerLayer)
        {
            yield return PlayAllPendingNestedExtractionReveals(block);
        }

        if (IsAutoMatchRunning)
        {
            yield break;
        }

        if (levelManager != null)
        {
            levelManager.NotifyBlockSettled();
        }
    }

    private IEnumerator PresentInnerEmergenceAndEntry(
        BoardManager board,
        RectTransform blockRect,
        Vector2Int from,
        Vector2Int to,
        Vector2 restPosition,
        Vector3 restScale)
    {
        if (BoardPresentationController.SuppressGameplayPieceUiImages())
        {
            yield return PlayChainInnerNestEntryWorld3D(board, block, 0, from, to);
            yield break;
        }

        PieceGameplayVisuals.NestedInnerLook look = block.NestedInnerLook;
        RectTransform boardRect = blockRect.parent as RectTransform;
        Sprite innerSprite = block.ContainedInnerSprite();
        RectTransform traveler = PieceGameplayVisuals.CreateTravelingInner(
            boardRect,
            innerSprite,
            block.VisualSizeDelta,
            restPosition + look.offset,
            look);

        if (traveler == null)
        {
            yield return Pause(matchingTargetPause);
            yield break;
        }

        Vector3 containedScale = restScale * look.scale;
        Vector3 emergedScale = restScale;
        traveler.localScale = containedScale;

        if (look.emergeDuration > 0f)
        {
            Vector2 emergeEnd = Vector2.Lerp(restPosition + look.offset, board.GridToLocal(to), 0.12f);
            yield return AnimateTraveler(
                traveler,
                traveler.anchoredPosition,
                emergeEnd,
                look.emergeDuration,
                containedScale,
                emergedScale,
                false);
        }
        else
        {
            traveler.localScale = emergedScale;
        }

        yield return Pause(matchingTargetPause);

        float liftAmount = board.VisualCellSize.y * matchingTargetAnticipateLiftPercent;
        Vector2 lifted = traveler.anchoredPosition + new Vector2(0f, liftAmount);
        Vector3 pumped = emergedScale * matchingTargetAnticipateScale;
        yield return AnimateTraveler(
            traveler,
            traveler.anchoredPosition,
            lifted,
            matchingTargetAnticipateDuration,
            emergedScale,
            pumped,
            false);

        Vector2 start = traveler.anchoredPosition;
        Vector2 end = board.GridToLocal(to);
        Vector2 lift = new Vector2(0f, board.VisualCellSize.y * matchingTargetLiftPercent);
        Vector2 control = ((restPosition + end) * 0.5f) + lift;
        Vector3 hopScale = emergedScale * matchingTargetHopScale;

        float arcDuration = Mathf.Max(0.01f, matchingTargetArcDuration);
        Tween arc = TweenAnimationUtility.Progress(arcDuration, t =>
        {
            float eased = PieceMotionMath.EaseInOutCubic(t);
            traveler.anchoredPosition = PieceMotionMath.QuadraticBezier(start, control, end, eased);
            float squashT = Mathf.Sin(t * Mathf.PI);
            traveler.localScale = Vector3.LerpUnclamped(emergedScale, hopScale, squashT);
        }).SetId(TweenAnimationUtility.TravelerId).SetLink(traveler.gameObject);
        yield return TweenAnimationUtility.Wait(arc);

        yield return AnimateTraveler(
            traveler,
            traveler.anchoredPosition,
            end,
            matchingTargetSitDuration,
            traveler.localScale,
            emergedScale,
            true);
        traveler.anchoredPosition = end;
        traveler.localScale = emergedScale;

        // Keep traveler through match VFX; EnterNestedInnerThenOuter destroys after PlayMatchEffect.
        pendingCellTraveler = traveler;
    }

    private static IEnumerator AnimateTraveler(
        RectTransform traveler,
        Vector2 from,
        Vector2 to,
        float duration,
        Vector3 scaleFrom,
        Vector3 scaleTo,
        bool easeOut)
    {
        if (traveler == null)
        {
            yield break;
        }

        if (duration <= 0f)
        {
            traveler.anchoredPosition = to;
            traveler.localScale = scaleTo;
            yield break;
        }

        Tween tween = TweenAnimationUtility.Progress(duration, t =>
        {
            float eased = easeOut
                ? TweenAnimationUtility.EvaluateEaseOutQuad(t)
                : TweenAnimationUtility.EvaluateSmoothStep(t);
            traveler.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
            traveler.localScale = Vector3.LerpUnclamped(scaleFrom, scaleTo, eased);
        }).SetId(TweenAnimationUtility.TravelerId).SetLink(traveler.gameObject);
        yield return TweenAnimationUtility.Wait(tween);
        traveler.anchoredPosition = to;
        traveler.localScale = scaleTo;
    }

    private IEnumerator ResolveCellMatches(
        BoardManager board,
        Vector2Int from,
        Vector2Int to,
        Target effectTarget)
    {
        KeepOnlyFirstMatch();
        matchSequenceIndex++;
        int matchId = matchSequenceIndex;
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} CONSUME");
        bool fullyConsumed = ConsumeAndRebuild(
            board,
            block,
            to,
            out Target completedTarget,
            out ShapeType consumedShape,
            out Vector2Int effectCell,
            out bool consumedInnerLayer);
        logicalCell = block.GridPosition;
        RememberLastMatch(from, to);

        yield return PlayMatchEffect(
            board,
            effectCell,
            consumedShape,
            completedTarget,
            fullyConsumed ? block : null,
            matchId);
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} CLEANUP COMPLETE");

        // Phase 64: outer peel reveals the next layer as a normal piece — no auto nest entry.
        if (!fullyConsumed
            && consumedInnerLayer
            && block != null
            && !block.IsSettled)
        {
            yield return PlayAllPendingNestedExtractionReveals(block);
        }

        if (!IsAutoMatchRunning)
        {
            yield return ResolveAlreadyAlignedMatches(board);
        }
    }

    private IEnumerator PlayNestedOuterNestEntry(BoardManager board, Block subject)
    {
        if (subject == null || board == null)
        {
            yield break;
        }

        Vector2Int here = subject.GridPosition;
        board.CollectNestMatches(subject, here, nestCellIndices, nestTargets);
        KeepOnlyFirstMatch();
        SyncNestTargetWorldsFromOccupying(subject, here);
        if (nestCellIndices.Count == 0)
        {
            yield break;
        }

        Target nestTarget = nestTargets[0];
        if (nestTarget != null)
        {
            nestTarget.ShowReadyFeedback();
        }

        subject.CancelDragSelectionImmediate();
        PlayNestEntrySound();

        RectTransform rect = subject.RectTransform;
        Vector2 restPosition = board.GridToLocal(here);
        Vector3 restScale = MotionRestScale(subject);
        IPieceMotion subjectMotion = MotionFor(subject);
        SnapMotionToCell(subjectMotion, board, here);
        ApplyMotionRestScale(subject, restScale);

        yield return Pause(matchingTargetPause);
        if (nestTarget != null)
        {
            nestTarget.HideReadyFeedback();
        }

        yield return AnimateAnticipation(board, subject, restPosition, restScale);
        yield return AnimateNestEntry(board, subject, here, here, restScale);
        subject.SetGridPosition(here, preserveWorldPresentation: true);
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchSequenceIndex + 1} LAND");

        board.CollectNestMatches(subject, here, nestCellIndices, nestTargets);
        KeepOnlyFirstMatch();
        SyncNestTargetWorldsFromOccupying(subject, here);
        if (nestCellIndices.Count == 0)
        {
            yield break;
        }

        matchSequenceIndex++;
        int matchId = matchSequenceIndex;
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} CONSUME");
        bool fullyConsumed = ConsumeAndRebuild(
            board,
            subject,
            here,
            out Target completedTarget,
            out ShapeType consumedShape,
            out Vector2Int effectCell,
            out bool nestedPromoted);
        if (subject == block)
        {
            logicalCell = block.GridPosition;
        }

        yield return PlayMatchEffect(
            board,
            effectCell,
            consumedShape,
            completedTarget,
            fullyConsumed ? subject : null,
            matchId);
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} CLEANUP COMPLETE");
        RememberLastMatch(here, here);

        if (!fullyConsumed && nestedPromoted && subject != null && !subject.IsSettled)
        {
            yield return PlayAllPendingNestedExtractionReveals(subject);
        }
    }

    private IEnumerator ResolveAlreadyAlignedMatches(BoardManager board)
    {
        if (levelManager == null || board == null)
        {
            yield break;
        }

        yield return levelManager.WaitForAlignedMatchQueue();
    }

    public IEnumerator PlayResolvedAutoMatch(BoardManager board, Vector2Int nestTo)
    {
        // Always refresh from this GameObject — never trust a stale Block field after split/rebuild.
        block = GetComponent<Block>();
        LastResolvedConsumeSucceeded = false;

        if (block == null || board == null)
        {
            yield break;
        }

        EnsureSubjectOccupancy(board, block);

        // Nested pieces match their active outer layer through the same focused-cell path.
        // Do not auto-emerge or auto-match nested children.
        bool occupying = IsWorldCellOccupyingAlignedMatch(board, block, nestTo);
        yield return MatchFocusedChainCell(board, block, block.GridPosition, nestTo, occupying);
    }

    /// <summary>
    /// Phase 67: play one connected-block movement group (1..N matched cells, one rigid translation).
    /// </summary>
    public IEnumerator PlayResolvedMovementGroup(BoardManager board, AlignedMovementGroup group)
    {
        block = GetComponent<Block>();
        LastResolvedConsumeSucceeded = false;

        if (block == null || board == null || group == null || group.Actions.Count == 0)
        {
            yield break;
        }

        if (group.Subject != null && group.Subject != block)
        {
            block = group.Subject;
        }

        EnsureSubjectOccupancy(board, block);
        Phase69AForensic.LogResolvedGroup(group);

        if (group.Actions.Count == 1)
        {
            AlignedMatchAction only = group.Actions[0];
            bool occupying = IsWorldCellOccupyingAlignedMatch(board, block, only.NestTo);
            yield return MatchFocusedChainCell(board, block, block.GridPosition, only.NestTo, occupying);
            yield break;
        }

        yield return StartCoroutine(PlayMatchingSubsetAlignedMatch(board, block, group));
    }

    /// <summary>
    /// Phase 70B: travel only the matching cells that share one translation. Unmatched siblings
    /// stay at their source occupancy. Does not call TryMoveBlock / CanTranslateBlock on the
    /// whole footprint, and does not fall back to MatchFocusedChainCell for a valid subset.
    /// </summary>
    private IEnumerator PlayMatchingSubsetAlignedMatch(
        BoardManager board,
        Block subject,
        AlignedMovementGroup group)
    {
        if (subject == null || board == null || group == null || group.Actions.Count < 2)
        {
            yield break;
        }

        if (MatchingSubsetDiagnosticsEnabled)
        {
            Debug.Log(
                $"[70B] SUBSET_MATCH enter block={(subject != null ? subject.GetInstanceID() : 0)} " +
                $"actions={group.Actions.Count} translation={group.Translation}");
        }

        matchSequenceIndex++;
        int matchId = matchSequenceIndex;

        var cellIndices = new List<int>(group.Actions.Count);
        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();

        Vector2Int translation = group.Translation;
        for (int i = 0; i < group.Actions.Count; i++)
        {
            AlignedMatchAction action = group.Actions[i];
            if (action.Subject != null && action.Subject != subject)
            {
                continue;
            }

            if (action.Translation != translation)
            {
                if (MatchingSubsetDiagnosticsEnabled)
                {
                    Debug.LogWarning(
                        $"[70B] SUBSET_MATCH abort: mixed translations in group " +
                        $"block={subject.GetInstanceID()} expected={translation} got={action.Translation}");
                }

                yield break;
            }

            int cellIndex = action.CellIndex;
            if (cellIndex < 0 || cellIndex >= subject.CellCount)
            {
                cellIndex = FindCellIndexAtWorld(subject, action.CellWorld);
            }

            if (cellIndex < 0 || cellIndex >= subject.CellCount)
            {
                continue;
            }

            Vector2Int nestTo = action.NestTo;
            Target target = board.GetTargetAt(nestTo);
            if (target == null
                || !ShapeMatch.AreMatchingLayers(
                    target.GetRequiredIdentityAtWorld(nestTo),
                    subject.GetActiveIdentity(cellIndex)))
            {
                continue;
            }

            if (cellIndices.Contains(cellIndex))
            {
                continue;
            }

            cellIndices.Add(cellIndex);
            nestCellIndices.Add(cellIndex);
            nestTargets.Add(target);
            nestTargetWorlds.Add(nestTo);
        }

        if (nestCellIndices.Count < 2)
        {
            if (MatchingSubsetDiagnosticsEnabled)
            {
                Debug.LogWarning(
                    $"[70B] SUBSET_MATCH abort: fewer than 2 valid cells " +
                    $"block={subject.GetInstanceID()} count={nestCellIndices.Count}");
            }

            yield break;
        }

        if (!board.CanTranslateMatchingSubset(subject, cellIndices, translation))
        {
            if (MatchingSubsetDiagnosticsEnabled)
            {
                Debug.LogWarning(
                    $"[70B] SUBSET_MATCH abort: CanTranslateMatchingSubset=false " +
                    $"block={subject.GetInstanceID()} translation={translation} cells={cellIndices.Count}");
            }

            yield break;
        }

        if (MatchingSubsetDiagnosticsEnabled)
        {
            Debug.Log(
                $"[70B] SUBSET_MATCH block={subject.GetInstanceID()} " +
                $"cells=[{string.Join(",", cellIndices)}] translation={translation} " +
                $"destinations=[{string.Join(",", nestTargetWorlds)}]");
            Debug.Log($"[70B] UNMATCHED_SIBLINGS_STAY block={subject.GetInstanceID()} grid={subject.GridPosition}");
        }

        // Phase 69B: detach nested residuals only for traveling selected cells.
        for (int i = 0; i < cellIndices.Count; i++)
        {
            int cellIndex = cellIndices[i];
            if (subject.HasInnerLayerAt(cellIndex))
            {
                BoardPresentationController.DetachAndAnchorNestedInner(subject, cellIndex);
            }
        }

        for (int i = 0; i < nestTargets.Count; i++)
        {
            if (nestTargets[i] != null)
            {
                nestTargets[i].ShowReadyFeedback();
            }
        }

        subject.CancelDragSelectionImmediate();
        PieceGameplayVisuals.ClearConnectors(subject.RectTransform);
        PlayNestEntrySound();

        var travelViews = new List<PieceView3D>(cellIndices.Count);
        var sources = new List<Vector2Int>(cellIndices.Count);
        var nests = new List<Vector2Int>(cellIndices.Count);
        for (int i = 0; i < cellIndices.Count; i++)
        {
            int cellIndex = cellIndices[i];
            PieceView3D view = subject.GetWorldViewForCellIndex(cellIndex);
            if (view == null)
            {
                BoardPresentationController.EndMatchingSubsetTravel(subject);
                yield break;
            }

            travelViews.Add(view);
            sources.Add(subject.GridPosition + subject.GetLocalCell(cellIndex));
            nests.Add(nestTargetWorlds[i]);
        }

        BoardPresentationController.BeginMatchingSubsetTravel(subject, travelViews);

        // Pin unmatched siblings to logical occupancy so Follow cannot drag them.
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace space = presenter != null ? presenter.GridSpace : MotionGridSpace(board);
        if (space != null)
        {
            for (int i = 0; i < subject.CellCount; i++)
            {
                if (cellIndices.Contains(i))
                {
                    continue;
                }

                PieceView3D sibling = subject.GetWorldViewForCellIndex(i);
                if (sibling != null)
                {
                    sibling.BeginMotionLock();
                    sibling.SnapWorldPresentationToGrid(space, subject.GetCellWorld(i));
                }
            }
        }

        yield return Pause(matchingTargetPause);
        for (int i = 0; i < nestTargets.Count; i++)
        {
            if (nestTargets[i] != null)
            {
                nestTargets[i].HideReadyFeedback();
            }
        }

        if (translation != Vector2Int.zero)
        {
            yield return AnimateMatchingSubsetNestEntry(board, travelViews, sources, nests);
        }

        // Unlock unmatched siblings after travel; travelers stay locked until cleanup.
        if (space != null)
        {
            for (int i = 0; i < subject.CellCount; i++)
            {
                if (cellIndices.Contains(i))
                {
                    continue;
                }

                PieceView3D sibling = subject.GetWorldViewForCellIndex(i);
                if (sibling != null)
                {
                    sibling.EndMotionLock();
                }
            }
        }

        // Occupancy stays at SOURCE. Nest lists already point at target worlds.
        Vector2Int occupancyAnchor = subject.GridPosition;
        bool fullyConsumed = ConsumeAndRebuild(
            board,
            subject,
            occupancyAnchor,
            out Target completedTarget,
            out ShapeType consumedShape,
            out Vector2Int effectCell,
            out bool consumedInnerLayer);
        if (subject == block)
        {
            logicalCell = block != null ? block.GridPosition : logicalCell;
        }

        EnsureSubjectOccupancy(board, subject);

        yield return PlayMatchEffect(
            board,
            effectCell,
            consumedShape,
            completedTarget,
            fullyConsumed ? subject : null,
            matchId);

        for (int i = 1; i < nestTargetWorlds.Count; i++)
        {
            Vector3 vfxPos = ResolveNestMatchWorldPosition(
                board,
                nestTargetWorlds[i],
                i < nestTargets.Count ? nestTargets[i] : null,
                null);
            BoardVfx3D.PlayNestMatch(vfxPos, ShapeVisuals3D.AccentColor(consumedShape));
        }

        bool deferTravelClear = consumedInnerLayer && subject != null && !subject.IsSettled;
        if (!deferTravelClear)
        {
            BoardPresentationController.EndMatchingSubsetTravel(subject);
            BoardPresentationController.NotifyChainCellTravelCleared(subject);
        }

        EnsureSubjectOccupancy(board, subject);
        if (group.Actions.Count > 0)
        {
            RememberLastMatch(group.Actions[0].CellWorld, group.Actions[0].NestTo);
        }

        if (consumedInnerLayer && subject != null && !subject.IsSettled)
        {
            yield return PlayAllPendingNestedExtractionReveals(subject);
            SeatPromotedNestedViewsAtSource(subject);
            BoardPresentationController.EndMatchingSubsetTravel(subject);
            BoardPresentationController.NotifyChainCellTravelCleared(subject);
            // Re-seat after unlock so FollowPrimaryView cannot inherit a stale target pose.
            SeatPromotedNestedViewsAtSource(subject);
            ForceUnlockPieceViews(subject);
            SeatPromotedNestedViewsAtSource(subject);
        }
    }

    /// <summary>
    /// Rigid world-space nest entry for multiple selected cell views. Shared arc progress keeps
    /// relative spacing constant. Does not change Block GridPosition / occupancy.
    /// </summary>
    private IEnumerator AnimateMatchingSubsetNestEntry(
        BoardManager board,
        List<PieceView3D> views,
        List<Vector2Int> fromCells,
        List<Vector2Int> toCells)
    {
        if (views == null || views.Count == 0 || board == null)
        {
            yield break;
        }

        IGridSpace space = MotionGridSpace(board);
        if (space == null)
        {
            yield break;
        }

        var starts = new Vector3[views.Count];
        var ends = new Vector3[views.Count];
        var restScales = new Vector3[views.Count];
        for (int i = 0; i < views.Count; i++)
        {
            PieceView3D view = views[i];
            if (view == null)
            {
                yield break;
            }

            starts[i] = view.transform.position;
            restScales[i] = view.ConfiguredFootprintScale;
            view.SnapWorldPresentationToGrid(space, toCells[i]);
            ends[i] = view.transform.position;
            view.transform.position = starts[i];
            view.LocalScale = restScales[i];
        }

        Vector3 primaryStart = starts[0];
        Vector3 primaryEnd = ends[0];
        float pieceHeight = Mathf.Abs(views[0].transform.lossyScale.y);
        float peakAboveRest = PieceMotionMath.NestJumpPeakHeight(pieceHeight);
        Vector3 control = (primaryStart + primaryEnd) * 0.5f;
        control.y += peakAboveRest;

        float insertMul = matchingTargetHopScale;
        if (insertMul > 1.001f)
        {
            insertMul = Mathf.Min(insertMul, 1.10f);
        }
        else if (insertMul >= 0.999f)
        {
            insertMul = 0.97f;
        }
        else
        {
            insertMul = Mathf.Clamp(insertMul, 0.96f, 0.99f);
        }

        // Shared anticipation on all travelers.
        // Tag Progress tweens with TravelerId on each view — KillTransform alone does not
        // stop DOTween.To Progress callbacks, which otherwise overwrite SOURCE seats.
        float anticipate = Mathf.Max(0.01f, matchingTargetAnticipateDuration);
        Tween anticipateTween = TweenAnimationUtility.Progress(anticipate, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
            float mul = Mathf.Lerp(1f, matchingTargetAnticipateScale, eased);
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i] != null)
                {
                    views[i].SetPresentationAnticipation(
                        matchingTargetAnticipateLiftPercent * eased,
                        mul,
                        0f);
                }
            }
        });
        TagMatchingSubsetTravelTweens(views, anticipateTween);
        yield return TweenAnimationUtility.Wait(anticipateTween);

        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] != null)
            {
                views[i].SetPresentationAnticipation(0f, 1f, 0f);
                views[i].LocalScale = restScales[i];
            }
        }

        float duration = Mathf.Max(0.01f, matchingTargetArcDuration);
        Tween arcTween = TweenAnimationUtility.Progress(duration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseInCubic(Mathf.Clamp01(t));
            Vector3 primaryPos = MatchingSubsetQuadraticBezier(primaryStart, control, primaryEnd, eased);
            Vector3 delta = primaryPos - primaryStart;
            for (int i = 0; i < views.Count; i++)
            {
                PieceView3D view = views[i];
                if (view == null)
                {
                    continue;
                }

                view.transform.position = starts[i] + delta;
                view.LocalScale = Vector3.LerpUnclamped(restScales[i], restScales[i] * insertMul, eased);
            }
        });
        TagMatchingSubsetTravelTweens(views, arcTween);
        yield return TweenAnimationUtility.Wait(arcTween);
        KillMatchingSubsetTravelTweens(views);

        float sit = Mathf.Max(0.01f, matchingTargetSitDuration);
        Tween sitTween = TweenAnimationUtility.Progress(sit, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(Mathf.Clamp01(t));
            for (int i = 0; i < views.Count; i++)
            {
                PieceView3D view = views[i];
                if (view == null)
                {
                    continue;
                }

                view.transform.position = ends[i];
                view.LocalScale = Vector3.LerpUnclamped(restScales[i] * insertMul, restScales[i], eased);
            }
        });
        TagMatchingSubsetTravelTweens(views, sitTween);
        yield return TweenAnimationUtility.Wait(sitTween);
        KillMatchingSubsetTravelTweens(views);

        for (int i = 0; i < views.Count; i++)
        {
            PieceView3D view = views[i];
            if (view == null)
            {
                continue;
            }

            view.SnapWorldPresentationToGrid(space, toCells[i]);
            view.LocalScale = restScales[i];
            view.SetPresentationAnticipation(0f, 1f, 0f);
        }
    }

    private static void TagMatchingSubsetTravelTweens(List<PieceView3D> views, Tween tween)
    {
        if (tween == null)
        {
            return;
        }

        // Id-only kill in Seat/Hold — Progress tweens are not transform-targeted.
        tween.SetId(TweenAnimationUtility.TravelerId);
        if (views == null)
        {
            return;
        }

        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] != null)
            {
                tween.SetLink(views[i].gameObject);
                break;
            }
        }
    }

    private static void KillMatchingSubsetTravelTweens(List<PieceView3D> views)
    {
        DOTween.Kill(TweenAnimationUtility.TravelerId, complete: false);
        if (views == null)
        {
            return;
        }

        for (int i = 0; i < views.Count; i++)
        {
            PieceView3D view = views[i];
            if (view == null)
            {
                continue;
            }

            TweenAnimationUtility.KillById(view.gameObject, TweenAnimationUtility.TravelerId, false);
            TweenAnimationUtility.KillTransform(view.transform, complete: false);
        }
    }

    private static Vector3 MatchingSubsetQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return (u * u * p0) + (2f * u * t * p1) + (t * t * p2);
    }

    /// <summary>TEMP Phase 70B diagnostics. Disabled by default; verifier may enable.</summary>
    public static bool MatchingSubsetDiagnosticsEnabled;

    /// <summary>
    /// Moves the whole connected block as one unit, then consumes every matched cell in the group.
    /// Legacy whole-footprint path retained for reference; multi-cell playback uses
    /// <see cref="PlayMatchingSubsetAlignedMatch"/>.
    /// </summary>
    private IEnumerator PlayWholeBlockAlignedMatch(
        BoardManager board,
        Block subject,
        AlignedMovementGroup group)
    {
        if (subject == null || board == null || group == null || group.Actions.Count == 0)
        {
            yield break;
        }

        matchSequenceIndex++;
        int matchId = matchSequenceIndex;

        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();
        for (int i = 0; i < group.Actions.Count; i++)
        {
            AlignedMatchAction action = group.Actions[i];
            int cellIndex = action.CellIndex;
            if (cellIndex < 0 || cellIndex >= subject.CellCount)
            {
                cellIndex = FindCellIndexAtWorld(subject, action.CellWorld);
            }

            if (cellIndex < 0)
            {
                continue;
            }

            Vector2Int nestTo = action.NestTo;
            Target target = board.GetTargetAt(nestTo);
            if (target == null
                || !ShapeMatch.AreMatchingLayers(
                    target.GetRequiredIdentityAtWorld(nestTo),
                    subject.GetActiveIdentity(cellIndex)))
            {
                continue;
            }

            nestCellIndices.Add(cellIndex);
            nestTargets.Add(target);
            nestTargetWorlds.Add(nestTo);
        }

        if (nestCellIndices.Count == 0)
        {
            yield break;
        }

        // If validation collapsed to a single cell, use the focused traveler path.
        if (nestCellIndices.Count == 1)
        {
            Phase69AForensic.LogWholeBlockGate(
                "FALLBACK_CollapsedToOneCell",
                subject,
                subject.GridPosition,
                nestTargetWorlds[0],
                group.Translation,
                group.Actions.Count,
                false);
            Vector2Int nestTo = nestTargetWorlds[0];
            bool occupying = IsWorldCellOccupyingAlignedMatch(board, subject, nestTo);
            yield return MatchFocusedChainCell(board, subject, subject.GridPosition, nestTo, occupying);
            yield break;
        }

        Vector2Int from = subject.GridPosition;
        Vector2Int to = from + group.Translation;
        if (group.Translation != Vector2Int.zero)
        {
            bool canTranslate = board.CanTranslateBlock(subject, to);
            if (!canTranslate)
            {
                Phase69AForensic.LogWholeBlockGate(
                    "FALLBACK_CanTranslateBlock",
                    subject,
                    from,
                    to,
                    group.Translation,
                    group.Actions.Count,
                    false);
                // Invalid rigid move — fall back to best single focused match.
                Vector2Int nestTo = nestTargetWorlds[0];
                bool occupying = IsWorldCellOccupyingAlignedMatch(board, subject, nestTo);
                yield return MatchFocusedChainCell(board, subject, from, nestTo, occupying);
                yield break;
            }

            if (!board.TryMoveBlock(subject, from, to))
            {
                Phase69AForensic.LogWholeBlockGate(
                    "FALLBACK_TryMoveBlock",
                    subject,
                    from,
                    to,
                    group.Translation,
                    group.Actions.Count,
                    true);
                Vector2Int nestTo = nestTargetWorlds[0];
                bool occupying = IsWorldCellOccupyingAlignedMatch(board, subject, nestTo);
                yield return MatchFocusedChainCell(board, subject, from, nestTo, occupying);
                yield break;
            }

            if (subject == block)
            {
                logicalCell = to;
            }
        }
        else
        {
            to = from;
        }

        // Phase 68B: whole footprint may travel; leave every nested residual at its source cell.
        Phase68CForensic.Log(
            "WHOLE_BLOCK_START",
            $"block={subject.GetInstanceID()} from={from} to={to} translation={group.Translation} " +
            $"actions={group.Actions.Count} cells={subject.CellCount}");
        Phase68CForensic.LogMovementGroup(group);
        for (int i = 0; i < subject.CellCount; i++)
        {
            if (subject.HasInnerLayerAt(i))
            {
                BoardPresentationController.DetachAndAnchorNestedInner(subject, i);
            }
        }

        Phase68CForensic.LogCell("PRE_OUTER_MOTION", subject, nestCellIndices[0]);

        for (int i = 0; i < nestTargets.Count; i++)
        {
            if (nestTargets[i] != null)
            {
                nestTargets[i].ShowReadyFeedback();
            }
        }

        subject.CancelDragSelectionImmediate();
        PieceGameplayVisuals.ClearConnectors(subject.RectTransform);
        PlayNestEntrySound();

        RectTransform rect = subject.RectTransform;
        Vector2 restPosition = board.GridToLocal(from);
        Vector3 restScale = MotionRestScale(subject);
        IPieceMotion subjectMotion = MotionFor(subject);
        SnapMotionToCell(subjectMotion, board, from);
        ApplyMotionRestScale(subject, restScale);

        yield return Pause(matchingTargetPause);
        for (int i = 0; i < nestTargets.Count; i++)
        {
            if (nestTargets[i] != null)
            {
                nestTargets[i].HideReadyFeedback();
            }
        }

        yield return AnimateAnticipation(board, subject, restPosition, restScale);
        yield return AnimateNestEntry(board, subject, from, to, restScale);
        subject.SetGridPosition(to, preserveWorldPresentation: true);
        Phase69AForensic.LogWholeBlockGate(
            "OCCUPANCY_SET_TO_TARGET",
            subject,
            from,
            subject.GridPosition,
            group.Translation,
            group.Actions.Count,
            true);
        if (subject == block)
        {
            logicalCell = to;
        }

        // Re-bind nest lists from current occupancy at the seated anchor (still multi-cell).
        ApplyMovementGroupNestLists(board, subject, to, group);
        if (nestCellIndices.Count == 0)
        {
            yield break;
        }

        bool fullyConsumed = ConsumeAndRebuild(
            board,
            subject,
            to,
            out Target completedTarget,
            out ShapeType consumedShape,
            out Vector2Int effectCell,
            out bool consumedInnerLayer);
        if (subject == block)
        {
            logicalCell = block != null ? block.GridPosition : logicalCell;
        }

        EnsureSubjectOccupancy(board, subject);

        yield return PlayMatchEffect(
            board,
            effectCell,
            consumedShape,
            completedTarget,
            fullyConsumed ? subject : null,
            matchId);

        // Extra nest VFX for sibling matched cells (presentation only; consume already done).
        for (int i = 1; i < nestTargetWorlds.Count; i++)
        {
            Vector3 vfxPos = ResolveNestMatchWorldPosition(
                board,
                nestTargetWorlds[i],
                i < nestTargets.Count ? nestTargets[i] : null,
                null);
            BoardVfx3D.PlayNestMatch(vfxPos, ShapeVisuals3D.AccentColor(consumedShape));
        }

        EnsureSubjectOccupancy(board, subject);
        if (group.Actions.Count > 0)
        {
            RememberLastMatch(group.Actions[0].CellWorld, group.Actions[0].NestTo);
        }

        if (consumedInnerLayer && subject != null && !subject.IsSettled)
        {
            yield return PlayAllPendingNestedExtractionReveals(subject);
            SeatPromotedNestedViewsAtSource(subject);
        }
    }

    private void ApplyMovementGroupNestLists(
        BoardManager board,
        Block subject,
        Vector2Int occupancyAnchor,
        AlignedMovementGroup group)
    {
        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();
        if (subject == null || board == null || group == null)
        {
            return;
        }

        var claimedNests = new HashSet<Vector2Int>();
        for (int i = 0; i < group.Actions.Count; i++)
        {
            AlignedMatchAction action = group.Actions[i];
            Vector2Int expectedWorld = occupancyAnchor + subject.GetLocalCell(
                Mathf.Clamp(action.CellIndex, 0, Mathf.Max(0, subject.CellCount - 1)));
            int cellIndex = FindCellIndexAtWorld(subject, expectedWorld);
            if (cellIndex < 0)
            {
                cellIndex = action.CellIndex;
            }

            if (cellIndex < 0 || cellIndex >= subject.CellCount)
            {
                continue;
            }

            Vector2Int nestTo = occupancyAnchor + subject.GetLocalCell(cellIndex);
            // After a non-zero translation, nest seats at the new cell worlds.
            if (group.Translation != Vector2Int.zero)
            {
                nestTo = action.CellWorld + group.Translation;
            }

            if (claimedNests.Contains(nestTo))
            {
                continue;
            }

            Target target = board.GetTargetAt(nestTo);
            if (target == null
                || !ShapeMatch.AreMatchingLayers(
                    target.GetRequiredIdentityAtWorld(nestTo),
                    subject.GetActiveIdentity(cellIndex)))
            {
                continue;
            }

            claimedNests.Add(nestTo);
            nestCellIndices.Add(cellIndex);
            nestTargets.Add(target);
            nestTargetWorlds.Add(nestTo);
        }
    }

    private IEnumerator PlayAllPendingNestedExtractionReveals(Block subject)
    {
        var blocks = new List<Block>(2);
        if (subject != null && !subject.IsSettled)
        {
            blocks.Add(subject);
        }

        for (int i = 0; i < pendingExtractionRevealBlocks.Count; i++)
        {
            Block extra = pendingExtractionRevealBlocks[i];
            if (extra != null && !extra.IsSettled && !blocks.Contains(extra))
            {
                blocks.Add(extra);
            }
        }

        pendingExtractionRevealBlocks.Clear();

        for (int b = 0; b < blocks.Count; b++)
        {
            Block revealSubject = blocks[b];
            if (revealSubject == null || revealSubject.IsSettled || !revealSubject.HasPendingLayerExtraction)
            {
                continue;
            }

            var pending = new List<int>(revealSubject.CellCount);
            for (int i = 0; i < revealSubject.CellCount; i++)
            {
                if (revealSubject.IsPendingLayerExtraction(i))
                {
                    pending.Add(i);
                }
            }

            // Phase 71B: sequential reveals only. Parallel StartCoroutine races left promoted
            // meshes seated at the TARGET until a later LateUpdate teleport-back to SOURCE.
            for (int i = 0; i < pending.Count; i++)
            {
                yield return PlayNestedExtractionReveal(revealSubject, pending[i]);
            }

            SeatPromotedNestedViewsAtSource(revealSubject);
            // Safety net: any cell that still shows a pre-promote outer+residual pair must
            // reconcile to logical survivors only (presentation; gameplay already advanced).
            BoardPresentationController.ReconcileNestedSurvivorVisuals(revealSubject);
        }
    }

    /// <summary>
    /// Phase 71B: force every live cell view onto its logical SOURCE cell after nested
    /// outer consumption. Kills leftover nest-entry tweens that can drag views back to target.
    /// </summary>
    private static void SeatPromotedNestedViewsAtSource(Block subject)
    {
        if (subject == null || subject.IsSettled)
        {
            return;
        }

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace space = presenter != null ? presenter.GridSpace : null;
        if (space == null)
        {
            return;
        }

        for (int i = 0; i < subject.CellCount; i++)
        {
            PieceView3D view = subject.GetWorldViewForCellIndex(i);
            if (view == null)
            {
                continue;
            }

            TweenAnimationUtility.KillById(view.gameObject, TweenAnimationUtility.TravelerId, false);
            TweenAnimationUtility.KillById(view.gameObject, TweenAnimationUtility.NestedExtractionId, false);
            TweenAnimationUtility.KillTransform(view.transform, complete: false);
            DOTween.Kill(TweenAnimationUtility.TravelerId, complete: false);

            Vector2Int sourceCell = BoardPresentationController.ResolveNestedPromotionSourceCell(subject, i);
            if (sourceCell == default)
            {
                sourceCell = subject.GetCellWorld(i);
            }

            view.SnapWorldPresentationToGrid(space, sourceCell);
            view.SetPresentationAnticipation(0f, 1f, 0f);

            // Force survivor remesh after seating — catches any green mesh that survived promote.
            if (!subject.IsPendingLayerExtraction(i))
            {
                BoardPresentationController.NotifyNestedLayerPromoted(subject, i);
            }

            // Extraction seating owns the pose — drop leftover locks so drag works.
            int guard = 8;
            while (view.IsMotionLocked && guard-- > 0)
            {
                view.EndMotionLock();
            }
        }
    }

    private static void ForceUnlockPieceViews(Block subject)
    {
        if (subject == null || subject.IsSettled)
        {
            return;
        }

        for (int i = 0; i < subject.CellCount; i++)
        {
            PieceView3D view = subject.GetWorldViewForCellIndex(i);
            if (view == null)
            {
                continue;
            }

            int guard = 8;
            while (view.IsMotionLocked && guard-- > 0)
            {
                view.EndMotionLock();
            }
        }
    }

    public static void LogAutoChainSequenceAfterMatch(BoardManager board, Block survivorBeforeNull)
    {
        //Debug.Log("[AUTO CHAIN SEQUENCE]\nMATCH COMPLETE");

        if (board == null)
        {
            //Debug.Log("[AUTO CHAIN SEQUENCE] board null");
            return;
        }

        var unique = new List<Block>();
        board.CollectUniqueBlocks(unique);
        if (unique.Count == 0)
        {
            //Debug.Log(
                // "[AUTO CHAIN SEQUENCE]\nRemaining Block: NONE\n" +
                // "(board empty — next scan should end the queue)");
            return;
        }

        for (int i = 0; i < unique.Count; i++)
        {
            Block b = unique[i];
            if (b == null || b.IsSettled)
            {
                continue;
            }

            int count = Mathf.Max(1, b.CellCount);
            for (int c = 0; c < count; c++)
            {
                Vector2Int world = b.GridPosition + b.GetLocalCell(c);
                Target target = board.GetTargetAt(world);
                string reject = ExplainAlignedCellRejection(board, b, c, world, null);
                bool candidate = reject == null;
                //Debug.Log(
                    // "[AUTO CHAIN SEQUENCE]\n" +
                    // $"Remaining Block: {b.GetInstanceID()}\n" +
                    // $"Remaining cell: {c}\n" +
                    // $"Remaining shape: {b.GetActiveShape(c)}\n" +
                    // $"Remaining world: {world}\n" +
                    // $"Target at remaining world: {(target != null ? target.RequiredShape.ToString() : "NULL")}\n" +
                    // $"Occupying owner OK: {board.GetBlockAt(world) == b}\n" +
                    // $"Triangle candidate = {candidate}\n" +
                    // $"Reject: {(reject ?? "none")}");
            }
        }
    }

    private bool IsAutoMatchRunning =>
        resolvingAligned || (levelManager != null && levelManager.IsAlignedMatchRunning);

    public IEnumerator PlayAlignedNestedMatch(BoardManager board)
    {
        yield return PlayAlignedNestedMatch(board, block != null ? block.GridPosition : Vector2Int.zero);
    }

    public IEnumerator PlayAlignedNestedMatch(BoardManager board, Vector2Int nestTo)
    {
        if (block == null)
        {
            yield break;
        }

        bool wasResolving = resolvingAligned;
        resolvingAligned = true;
        Vector2Int here = block.GridPosition;
        yield return EnterNestedInnerThenOuter(board, block.RectTransform, here, nestTo);
        resolvingAligned = wasResolving;
    }

    public IEnumerator PlayAlignedMagnetMatch(BoardManager board, Vector2Int nestTo)
    {
        if (block == null || board == null)
        {
            yield break;
        }

        bool wasResolving = resolvingAligned;
        resolvingAligned = true;
        yield return EnterMatchingTargetBody(board, block.RectTransform, block.GridPosition, nestTo);
        resolvingAligned = wasResolving;
    }

    private IEnumerator PlaySimpleAlignedNestEntry(BoardManager board, Block subject)
    {
        yield return PlaySimpleAlignedNestEntry(board, subject, subject != null ? subject.GridPosition : Vector2Int.zero);
    }

    private IEnumerator PlaySimpleAlignedNestEntry(BoardManager board, Block subject, Vector2Int nestTo)
    {
        if (subject == null || board == null)
        {
            yield break;
        }

        EnsureSubjectOccupancy(board, subject);
        Vector2Int here = subject.GridPosition;
        matchSequenceIndex++;
        int matchId = matchSequenceIndex;
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} START");

        board.CollectNestMatches(subject, here, nestCellIndices, nestTargets);
        KeepOnlyFirstMatch();
        SyncNestTargetWorldsFromOccupying(subject, here);
        if (nestCellIndices.Count == 0)
        {
            Target target = board.GetTargetAt(here);
            Block occupant = board.GetBlockAt(here);
            //Debug.Log(
                // $"REJECT PlaySimpleAlignedNestEntry Block={subject.GetInstanceID()} here={here} nestTo={nestTo}:\n" +
                // $"- CollectNestMatches empty (shape={subject.GetActiveShape(0)} " +
                // $"target={(target != null ? target.RequiredShape.ToString() : "NULL")} " +
                // $"occupant={(occupant != null ? occupant.GetInstanceID().ToString() : "NULL")} " +
                // $"GetBlockAt(nestTo)={(board.GetBlockAt(nestTo) != null ? board.GetBlockAt(nestTo).GetInstanceID().ToString() : "NULL")})");
            yield break;
        }

        Target nestTarget = nestTargets[0];
        int lockedCellIndex = nestCellIndices[0];
        if (nestTarget != null)
        {
            nestTarget.ShowReadyFeedback();
        }

        subject.CancelDragSelectionImmediate();
        PlayNestEntrySound();

        RectTransform rect = subject.RectTransform;
        Vector2 restPosition = board.GridToLocal(here);
        Vector3 restScale = MotionRestScale(subject);
        IPieceMotion subjectMotion = MotionFor(subject);
        SnapMotionToCell(subjectMotion, board, here);
        ApplyMotionRestScale(subject, restScale);

        yield return Pause(matchingTargetPause);
        if (nestTarget != null)
        {
            nestTarget.HideReadyFeedback();
        }

        yield return AnimateAnticipation(board, subject, restPosition, restScale);
        yield return AnimateNestEntry(board, subject, here, here, restScale);
        subject.SetGridPosition(here, preserveWorldPresentation: true);
        EnsureSubjectOccupancy(board, subject);
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} LAND");

        // Lock the pre-animation match (same pattern as MatchFocusedChainCell).
        // Do not re-CollectNestMatches after the traveler — that can soft-fail in Play Mode
        // and skip a still-valid occupying survivor.
        int cellIndex = lockedCellIndex;
        Vector2Int matchedWorld = nestTargetWorlds.Count > 0 ? nestTargetWorlds[0] : here;
        if (cellIndex < 0
            || cellIndex >= subject.CellCount
            || nestTarget == null
            || !nestTarget.isActiveAndEnabled
            || !ShapeMatch.AreMatchingLayers(
                nestTarget.GetRequiredIdentityAtWorld(matchedWorld),
                subject.GetActiveIdentity(cellIndex)))
        {
            cellIndex = FindCellIndexAtWorld(subject, here);
            matchedWorld = here;
        }

        if (cellIndex < 0
            || nestTarget == null
            || !nestTarget.isActiveAndEnabled
            || !ShapeMatch.AreMatchingLayers(
                nestTarget.GetRequiredIdentityAtWorld(matchedWorld),
                subject.GetActiveIdentity(cellIndex)))
        {
            //Debug.Log(
                // $"REJECT PlaySimpleAlignedNestEntry post-animation Block={subject.GetInstanceID()} here={here}:\n" +
                // $"- locked match invalid (cellIndex={cellIndex} " +
                // $"target={(nestTarget != null ? nestTarget.RequiredShape.ToString() : "NULL")} " +
                // $"active={(cellIndex >= 0 && cellIndex < subject.CellCount ? subject.GetActiveShape(cellIndex).ToString() : "n/a")})");
            yield break;
        }

        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();
        nestCellIndices.Add(cellIndex);
        nestTargets.Add(nestTarget);
        nestTargetWorlds.Add(matchedWorld);

        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} CONSUME");
        bool fullyConsumed = ConsumeAndRebuild(
            board,
            subject,
            here,
            out Target completedTarget,
            out ShapeType consumedShape,
            out Vector2Int effectCell,
            out bool consumedInnerLayer);
        if (subject == block)
        {
            logicalCell = block.GridPosition;
        }

        yield return PlayMatchEffect(
            board,
            effectCell,
            consumedShape,
            completedTarget,
            fullyConsumed ? subject : null,
            matchId);
        //Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} CLEANUP COMPLETE");
        RememberLastMatch(here, here);

        if (!fullyConsumed && consumedInnerLayer && subject != null && !subject.IsSettled)
        {
            yield return PlayAllPendingNestedExtractionReveals(subject);
        }
    }

    private void KeepOnlyFirstMatch()
    {
        if (nestCellIndices.Count <= 1)
        {
            return;
        }

        int cellIndex = nestCellIndices[0];
        Target target = nestTargets.Count > 0 ? nestTargets[0] : null;
        Vector2Int targetWorld = nestTargetWorlds.Count > 0 ? nestTargetWorlds[0] : default;
        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();
        nestCellIndices.Add(cellIndex);
        nestTargets.Add(target);
        nestTargetWorlds.Add(targetWorld);
    }

    /// <summary>
    /// Phase 73 (from 71B nested helper): keep every collected match that shares the
    /// focus-nearest cell's translation. Used for nested and plain multi-cell chains
    /// so rigid group travel can consume the full same-geometry subset.
    /// </summary>
    private void KeepSameTranslationMatches(
        Block subject,
        Vector2Int occupancyAnchor,
        Vector2Int focusWorld)
    {
        if (subject == null || nestCellIndices.Count <= 1)
        {
            return;
        }

        int best = 0;
        int bestDist = int.MaxValue;
        for (int i = 0; i < nestCellIndices.Count; i++)
        {
            Vector2Int dest = i < nestTargetWorlds.Count
                ? nestTargetWorlds[i]
                : occupancyAnchor + subject.GetLocalCell(nestCellIndices[i]);
            int dist = Mathf.Abs(dest.x - focusWorld.x) + Mathf.Abs(dest.y - focusWorld.y);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        int bestIndex = nestCellIndices[best];
        Vector2Int bestWorld = occupancyAnchor + subject.GetLocalCell(bestIndex);
        Vector2Int bestDest = best < nestTargetWorlds.Count ? nestTargetWorlds[best] : bestWorld;
        Vector2Int translation = bestDest - bestWorld;

        var keepIndices = new List<int>(nestCellIndices.Count);
        var keepTargets = new List<Target>(nestTargets.Count);
        var keepWorlds = new List<Vector2Int>(nestTargetWorlds.Count);
        for (int i = 0; i < nestCellIndices.Count; i++)
        {
            int idx = nestCellIndices[i];
            Vector2Int world = occupancyAnchor + subject.GetLocalCell(idx);
            Vector2Int dest = i < nestTargetWorlds.Count ? nestTargetWorlds[i] : world;
            if (dest - world != translation)
            {
                continue;
            }

            keepIndices.Add(idx);
            keepTargets.Add(i < nestTargets.Count ? nestTargets[i] : null);
            keepWorlds.Add(dest);
        }

        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();
        nestCellIndices.AddRange(keepIndices);
        nestTargets.AddRange(keepTargets);
        nestTargetWorlds.AddRange(keepWorlds);
    }

    /// <summary>Compatibility alias for Phase 71B call sites / diagnostics.</summary>
    private void KeepNestedSameTranslationMatches(
        Block subject,
        Vector2Int occupancyAnchor,
        Vector2Int focusWorld)
    {
        KeepSameTranslationMatches(subject, occupancyAnchor, focusWorld);
    }

    /// <summary>
    /// Phase 71B: build a matching-subset group for nested cells that can actually translate.
    /// Drops plain (no-inner) siblings if they would abort the whole subset.
    /// </summary>
    private bool TryPlayNestedSubsetMatch(
        BoardManager board,
        Block subject,
        Vector2Int occupancy,
        out AlignedMovementGroup group)
    {
        group = null;
        if (board == null || subject == null || nestCellIndices.Count < 2)
        {
            return false;
        }

        if (TryBuildNestedSubsetGroup(board, subject, occupancy, nestedOnly: false, out group))
        {
            return true;
        }

        return TryBuildNestedSubsetGroup(board, subject, occupancy, nestedOnly: true, out group);
    }

    private bool TryBuildNestedSubsetGroup(
        BoardManager board,
        Block subject,
        Vector2Int occupancy,
        bool nestedOnly,
        out AlignedMovementGroup group)
    {
        group = null;
        var actions = new List<AlignedMatchAction>(nestCellIndices.Count);
        var cellIndices = new List<int>(nestCellIndices.Count);
        for (int i = 0; i < nestCellIndices.Count; i++)
        {
            int idx = nestCellIndices[i];
            if (nestedOnly && !subject.HasInnerLayerAt(idx))
            {
                continue;
            }

            Vector2Int world = occupancy + subject.GetLocalCell(idx);
            Vector2Int nestTo = i < nestTargetWorlds.Count ? nestTargetWorlds[i] : world;
            actions.Add(new AlignedMatchAction(subject, idx, world, nestTo));
            cellIndices.Add(idx);
        }

        if (actions.Count < 2)
        {
            return false;
        }

        Vector2Int translation = actions[0].Translation;
        if (translation != Vector2Int.zero)
        {
            // Phase 73: rigid hop must be axis-aligned and legal for fixed-direction blocks.
            Vector2Int axis;
            if (translation.x != 0 && translation.y == 0)
            {
                axis = translation.x > 0 ? Vector2Int.right : Vector2Int.left;
            }
            else if (translation.y != 0 && translation.x == 0)
            {
                axis = translation.y > 0 ? Vector2Int.up : Vector2Int.down;
            }
            else
            {
                return false;
            }

            BlockMover subjectMover = subject.GetComponent<BlockMover>();
            if (subjectMover != null && !subjectMover.IsDirectionAllowed(axis))
            {
                return false;
            }
        }

        if (!board.CanTranslateMatchingSubset(subject, cellIndices, translation))
        {
            return false;
        }

        group = new AlignedMovementGroup
        {
            Subject = subject,
            Translation = translation
        };
        group.Actions.AddRange(actions);
        return true;
    }

    /// <summary>
    /// When a nested subset cannot move together, match a nested cell rather than the
    /// nearest plain cell (orange pentagon with no inner).
    /// </summary>
    private void PreferNestedCellForSingleMatch(Block subject)
    {
        if (subject == null || nestCellIndices.Count <= 1)
        {
            return;
        }

        int nested = -1;
        for (int i = 0; i < nestCellIndices.Count; i++)
        {
            if (subject.HasInnerLayerAt(nestCellIndices[i]))
            {
                nested = i;
                break;
            }
        }

        if (nested < 0)
        {
            return;
        }

        int cellIndex = nestCellIndices[nested];
        Target target = nested < nestTargets.Count ? nestTargets[nested] : null;
        Vector2Int targetWorld = nested < nestTargetWorlds.Count ? nestTargetWorlds[nested] : default;
        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();
        nestCellIndices.Add(cellIndex);
        nestTargets.Add(target);
        nestTargetWorlds.Add(targetWorld);
    }

    private void KeepOnlyNearestMatch(Block subject, Vector2Int occupancyAnchor, Vector2Int focusWorld)
    {
        if (subject == null || nestCellIndices.Count == 0)
        {
            return;
        }

        if (nestCellIndices.Count == 1)
        {
            return;
        }

        int best = 0;
        int bestDist = int.MaxValue;
        for (int i = 0; i < nestCellIndices.Count; i++)
        {
            Vector2Int world = i < nestTargetWorlds.Count
                ? nestTargetWorlds[i]
                : occupancyAnchor + subject.GetLocalCell(nestCellIndices[i]);
            int dist = Mathf.Abs(world.x - focusWorld.x) + Mathf.Abs(world.y - focusWorld.y);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        int cellIndex = nestCellIndices[best];
        Target target = best < nestTargets.Count ? nestTargets[best] : null;
        Vector2Int targetWorld = best < nestTargetWorlds.Count ? nestTargetWorlds[best] : default;
        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();
        nestCellIndices.Add(cellIndex);
        nestTargets.Add(target);
        nestTargetWorlds.Add(targetWorld);
    }

    private void KeepOnlyCellAtWorld(Block subject, Vector2Int occupancyAnchor, Vector2Int world)
    {
        if (subject == null)
        {
            nestCellIndices.Clear();
            nestTargets.Clear();
            nestTargetWorlds.Clear();
            return;
        }

        for (int i = 0; i < nestCellIndices.Count; i++)
        {
            Vector2Int matchWorld = i < nestTargetWorlds.Count
                ? nestTargetWorlds[i]
                : occupancyAnchor + subject.GetLocalCell(nestCellIndices[i]);
            if (matchWorld != world && occupancyAnchor + subject.GetLocalCell(nestCellIndices[i]) != world)
            {
                continue;
            }

            int cellIndex = nestCellIndices[i];
            Target target = i < nestTargets.Count ? nestTargets[i] : null;
            Vector2Int targetWorld = i < nestTargetWorlds.Count ? nestTargetWorlds[i] : world;
            nestCellIndices.Clear();
            nestTargets.Clear();
            nestTargetWorlds.Clear();
            nestCellIndices.Add(cellIndex);
            nestTargets.Add(target);
            nestTargetWorlds.Add(targetWorld);
            return;
        }

        nestCellIndices.Clear();
        nestTargets.Clear();
        nestTargetWorlds.Clear();
    }

    private static int FindCellIndexAtWorld(Block subject, Vector2Int world)
    {
        if (subject == null)
        {
            return -1;
        }

        int count = subject.CellCount;
        for (int i = 0; i < count; i++)
        {
            if (subject.GetCellWorld(i) == world)
            {
                return i;
            }
        }

        return -1;
    }

    public static bool TryFindNextAlignedMatch(
        BoardManager board,
        List<Block> scratch,
        HashSet<int> skipIds,
        bool hasLastMatch,
        Vector2Int lastMatchOrigin,
        Vector2Int lastMatchTargetCell,
        out Block subject,
        out Vector2Int nestTo)
    {
        subject = null;
        nestTo = Vector2Int.zero;
        if (board == null || scratch == null)
        {
            return false;
        }

        board.CollectUniqueBlocks(scratch);
        LogAutoMatchScan(board, scratch, skipIds);
        int bestPriority = int.MaxValue;
        int bestY = int.MaxValue;
        int bestX = int.MaxValue;
        for (int i = 0; i < scratch.Count; i++)
        {
            Block candidate = scratch[i];
            if (candidate == null || candidate.IsSettled || candidate.IsFrozen || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            if (board.IsBlockUnderImpassableCell(candidate))
            {
                continue;
            }

            int count = Mathf.Max(1, candidate.CellCount);
            for (int cellIndex = 0; cellIndex < count; cellIndex++)
            {
                Vector2Int world = candidate.GridPosition + candidate.GetLocalCell(cellIndex);
                Vector2Int dest = world;
                string reject = ExplainAlignedCellRejection(
                    board,
                    candidate,
                    cellIndex,
                    world,
                    skipIds);
                if (reject != null)
                {
                    // Adjacent one-cell nest is only for follow-up after a finished match,
                    // not for the initial load scan.
                    if (!hasLastMatch
                        || !TryGetAdjacentAutoMatchDest(
                            board,
                            candidate,
                            cellIndex,
                            world,
                            skipIds,
                            out dest,
                            out _))
                    {
                        continue;
                    }
                }

                int priority = AlignedMatchPriority(
                    hasLastMatch,
                    world,
                    dest,
                    lastMatchOrigin,
                    lastMatchTargetCell);
                if (priority > bestPriority)
                {
                    continue;
                }

                if (priority == bestPriority
                    && (dest.y > bestY || (dest.y == bestY && dest.x >= bestX)))
                {
                    continue;
                }

                bestPriority = priority;
                bestY = dest.y;
                bestX = dest.x;
                subject = candidate;
                nestTo = dest;
            }
        }

        if (subject != null)
        {
            // Debug.Log(
            //     $"[AUTO MATCH SCAN] SELECTED Block={subject.GetInstanceID()} nestTo={nestTo} " +
            //     $"shape={subject.GetActiveShape(0)} priority={bestPriority}",
            //     subject);
        }
        else
        {
           // Debug.Log("[AUTO MATCH SCAN] SELECTED none");
            LogSelectedNoneDump(board, scratch, skipIds);
        }

        return subject != null;
    }

    /// <summary>
    /// Phase 66/67: collect every currently-valid auto-match action into one wave.
    /// Multiple cells of the same block may appear. Dedupes only identical cell→nest claims
    /// and nest destination cells claimed twice.
    /// Gameplay truth is decided here before presentation waits on movement.
    /// </summary>
    public static int CollectAlignedMatchActions(
        BoardManager board,
        List<Block> scratch,
        HashSet<int> skipIds,
        bool hasLastMatch,
        Vector2Int lastMatchOrigin,
        Vector2Int lastMatchTargetCell,
        List<AlignedMatchAction> actionsOut)
    {
        if (actionsOut == null)
        {
            return 0;
        }

        actionsOut.Clear();
        if (board == null || scratch == null)
        {
            return 0;
        }

        board.CollectUniqueBlocks(scratch);
        LogAutoMatchScan(board, scratch, skipIds);

        var ranked = new List<(int priority, int y, int x, Block subject, int cellIndex, Vector2Int world, Vector2Int nestTo)>(8);
        for (int i = 0; i < scratch.Count; i++)
        {
            Block candidate = scratch[i];
            if (candidate == null || candidate.IsSettled || candidate.IsFrozen || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            if (board.IsBlockUnderImpassableCell(candidate))
            {
                continue;
            }

            int count = Mathf.Max(1, candidate.CellCount);
            for (int cellIndex = 0; cellIndex < count; cellIndex++)
            {
                Vector2Int world = candidate.GridPosition + candidate.GetLocalCell(cellIndex);
                Vector2Int dest = world;
                string reject = ExplainAlignedCellRejection(
                    board,
                    candidate,
                    cellIndex,
                    world,
                    skipIds);
                if (reject != null)
                {
                    if (!hasLastMatch
                        || !TryGetAdjacentAutoMatchDest(
                            board,
                            candidate,
                            cellIndex,
                            world,
                            skipIds,
                            out dest,
                            out _))
                    {
                        continue;
                    }
                }

                int priority = AlignedMatchPriority(
                    hasLastMatch,
                    world,
                    dest,
                    lastMatchOrigin,
                    lastMatchTargetCell);
                ranked.Add((priority, dest.y, dest.x, candidate, cellIndex, world, dest));
            }
        }

        ranked.Sort((a, b) =>
        {
            int cmp = a.priority.CompareTo(b.priority);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = a.y.CompareTo(b.y);
            if (cmp != 0)
            {
                return cmp;
            }

            return a.x.CompareTo(b.x);
        });

        var usedCellKeys = new HashSet<long>();
        var usedNestCells = new HashSet<Vector2Int>();
        for (int i = 0; i < ranked.Count; i++)
        {
            Block subject = ranked[i].subject;
            int cellIndex = ranked[i].cellIndex;
            Vector2Int nestTo = ranked[i].nestTo;
            Vector2Int world = ranked[i].world;
            if (subject == null)
            {
                continue;
            }

            long cellKey = ((long)subject.GetInstanceID() << 32) ^ (uint)cellIndex;
            if (usedCellKeys.Contains(cellKey) || usedNestCells.Contains(nestTo))
            {
                continue;
            }

            if (!IsChainCellAutoMatchValid(board, subject, nestTo))
            {
                continue;
            }

            usedCellKeys.Add(cellKey);
            usedNestCells.Add(nestTo);
            actionsOut.Add(new AlignedMatchAction(subject, cellIndex, world, nestTo));
        }

        return actionsOut.Count;
    }

    /// <summary>
    /// Phase 67: fold match actions into connected-block movement groups.
    /// Same-block actions share a group only when they require the same rigid translation.
    /// Incompatible translations are not force-grouped (no visual tear); the largest
    /// consistent subset wins, remaining actions wait for a later wave.
    /// </summary>
    public static int BuildAlignedMovementGroups(
        List<AlignedMatchAction> actions,
        List<AlignedMovementGroup> groupsOut)
    {
        if (groupsOut == null)
        {
            return 0;
        }

        groupsOut.Clear();
        if (actions == null || actions.Count == 0)
        {
            return 0;
        }

        var byBlock = new Dictionary<int, List<AlignedMatchAction>>();
        for (int i = 0; i < actions.Count; i++)
        {
            AlignedMatchAction action = actions[i];
            if (action.Subject == null)
            {
                continue;
            }

            int id = action.Subject.GetInstanceID();
            if (!byBlock.TryGetValue(id, out List<AlignedMatchAction> list))
            {
                list = new List<AlignedMatchAction>();
                byBlock[id] = list;
            }

            list.Add(action);
        }

        foreach (KeyValuePair<int, List<AlignedMatchAction>> pair in byBlock)
        {
            List<AlignedMatchAction> blockActions = pair.Value;
            if (blockActions.Count == 0)
            {
                continue;
            }

            // Count translations in encounter order (already priority-sorted).
            var translationCounts = new Dictionary<Vector2Int, int>();
            var translationOrder = new List<Vector2Int>();
            for (int i = 0; i < blockActions.Count; i++)
            {
                Vector2Int translation = blockActions[i].Translation;
                if (!translationCounts.ContainsKey(translation))
                {
                    translationOrder.Add(translation);
                    translationCounts[translation] = 0;
                }

                translationCounts[translation]++;
            }

            Vector2Int bestTranslation = translationOrder[0];
            int bestCount = translationCounts[bestTranslation];
            for (int i = 1; i < translationOrder.Count; i++)
            {
                Vector2Int translation = translationOrder[i];
                int count = translationCounts[translation];
                if (count > bestCount)
                {
                    bestCount = count;
                    bestTranslation = translation;
                }
            }

            var group = new AlignedMovementGroup
            {
                Subject = blockActions[0].Subject,
                Translation = bestTranslation
            };
            for (int i = 0; i < blockActions.Count; i++)
            {
                if (blockActions[i].Translation == bestTranslation)
                {
                    group.Actions.Add(blockActions[i]);
                }
            }

            groupsOut.Add(group);
        }

        for (int g = 0; g < groupsOut.Count; g++)
        {
            Phase68CForensic.LogMovementGroup(groupsOut[g]);
        }

        return groupsOut.Count;
    }

    /// <summary>
    /// Collect match actions then fold into synchronized movement groups (Phase 66+67).
    /// </summary>
    public static int CollectAlignedMovementGroups(
        BoardManager board,
        List<Block> scratch,
        HashSet<int> skipIds,
        bool hasLastMatch,
        Vector2Int lastMatchOrigin,
        Vector2Int lastMatchTargetCell,
        List<AlignedMatchAction> actionsScratch,
        List<AlignedMovementGroup> groupsOut)
    {
        if (actionsScratch == null)
        {
            actionsScratch = new List<AlignedMatchAction>();
        }

        CollectAlignedMatchActions(
            board,
            scratch,
            skipIds,
            hasLastMatch,
            lastMatchOrigin,
            lastMatchTargetCell,
            actionsScratch);
        return BuildAlignedMovementGroups(actionsScratch, groupsOut);
    }

    /// <summary>
    /// Phase 66 compatibility: one nest destination per collected action (may be multi-cell).
    /// Prefer <see cref="CollectAlignedMovementGroups"/> for playback.
    /// </summary>
    public static int CollectAlignedMatchWave(
        BoardManager board,
        List<Block> scratch,
        HashSet<int> skipIds,
        bool hasLastMatch,
        Vector2Int lastMatchOrigin,
        Vector2Int lastMatchTargetCell,
        List<AlignedMatchWaveMember> waveOut)
    {
        if (waveOut == null)
        {
            return 0;
        }

        waveOut.Clear();
        var actions = new List<AlignedMatchAction>();
        CollectAlignedMatchActions(
            board,
            scratch,
            skipIds,
            hasLastMatch,
            lastMatchOrigin,
            lastMatchTargetCell,
            actions);
        for (int i = 0; i < actions.Count; i++)
        {
            waveOut.Add(new AlignedMatchWaveMember(actions[i].Subject, actions[i].NestTo));
        }

        return waveOut.Count;
    }

    private static void LogSelectedNoneDump(BoardManager board, List<Block> scratch, HashSet<int> skipIds)
    {
        if (board == null)
        {
            return;
        }

        // Debug.Log(
        //     $"[AUTO MATCH SCAN] SELECTED-none dump: occupancyUnique={(scratch != null ? scratch.Count : -1)} " +
        //     $"skipCount={(skipIds != null ? skipIds.Count : 0)} board={board.Width}x{board.Height}");

        Block[] children = board.GetComponentsInChildren<Block>(true);
        //Debug.Log($"[AUTO MATCH SCAN] Child Block count under board={children.Length}");
        for (int i = 0; i < children.Length; i++)
        {
            Block b = children[i];
            if (b == null)
            {
                continue;
            }

            Vector2Int world = b.GridPosition;
            Block occ = board.GetBlockAt(world);
            Target target = board.GetTargetAt(world);
            string reject = ExplainAlignedCellRejection(board, b, 0, world, skipIds);
            // Debug.Log(
            //     $"[AUTO MATCH SCAN] ORPHAN-CHECK Block={b.GetInstanceID()} Grid={world} " +
            //     $"CellCount={b.CellCount} Shape={b.GetActiveShape(0)} Settled={b.IsSettled} " +
            //     $"Active={b.isActiveAndEnabled} GetBlockAt={(occ != null ? occ.GetInstanceID().ToString() : "NULL")} " +
            //     $"same={(occ == b)} Target={(target != null ? target.RequiredShape.ToString() : "NULL")} " +
            //     $"reject={(reject ?? "none")}");
        }
    }

    /// <summary>
    /// TEMP DIAGNOSTIC: per-cell occupying auto-match scan with exact rejection reasons.
    /// </summary>
    public static void LogAutoMatchScan(BoardManager board, List<Block> scratch, HashSet<int> skipIds)
    {
        if (board == null)
        {
            //Debug.Log("[AUTO MATCH SCAN] board null");
            return;
        }

        if (scratch == null)
        {
            scratch = new List<Block>();
        }

        // Always refresh from occupancy — callers may pass an empty list.
        board.CollectUniqueBlocks(scratch);

        //Debug.Log($"[AUTO MATCH SCAN] uniqueBlocks={scratch.Count}");
        for (int i = 0; i < scratch.Count; i++)
        {
            Block candidate = scratch[i];
            if (candidate == null)
            {
                //Debug.Log("[AUTO MATCH SCAN] Block: null");
                continue;
            }

            int instanceId = candidate.GetInstanceID();
            int count = Mathf.Max(1, candidate.CellCount);
            // Debug.Log(
            //     $"[AUTO MATCH SCAN] Block: {instanceId} GridPosition={candidate.GridPosition} " +
            //     $"CellCount={candidate.CellCount} Active={candidate.isActiveAndEnabled} " +
            //     $"Settled={candidate.IsSettled} InCollectUnique=TRUE");

            for (int cellIndex = 0; cellIndex < count; cellIndex++)
            {
                Vector2Int local = candidate.GetLocalCell(cellIndex);
                Vector2Int world = candidate.GridPosition + local;
                Target target = board.GetTargetAt(world);
                Block occupant = board.GetBlockAt(world);
                ShapeType offered = candidate.GetActiveShape(cellIndex);
                string reject = ExplainAlignedCellRejection(
                    board,
                    candidate,
                    cellIndex,
                    world,
                    skipIds);
                bool isCandidate = reject == null;

                // Debug.Log(
                //     "[AUTO MATCH SCAN]\n" +
                //     $"Block: {instanceId}\n" +
                //     $"Cell: {cellIndex}\n" +
                //     $"Local: {local}\n" +
                //     $"World: {world}\n" +
                //     $"Shape: {offered}\n" +
                //     $"Target: {(target != null ? target.GetInstanceID().ToString() : "NULL")}\n" +
                //     $"RequiredShape: {(target != null ? target.RequiredShape.ToString() : "n/a")}\n" +
                //     $"OccupyingBlock: {(occupant != null ? occupant.GetInstanceID().ToString() : "NULL")}\n" +
                //     $"IsActive: {candidate.isActiveAndEnabled}\n" +
                //     $"IsSettled: {candidate.IsSettled}\n" +
                //     $"Candidate: {isCandidate}");

                if (!isCandidate)
                {
                    //Debug.Log($"REJECT cell {cellIndex} of Block {instanceId} at {world}:\n- {reject}");
                }
            }
        }

        LogRemainingTargets(board);
    }

    public static string ExplainAlignedCellRejection(
        BoardManager board,
        Block candidate,
        int cellIndex,
        Vector2Int world,
        HashSet<int> skipIds)
    {
        if (candidate == null)
        {
            return "block null";
        }

        if (candidate.IsSettled)
        {
            return "block considered moving/settled";
        }

        if (candidate.IsFrozen)
        {
            return "block is frozen";
        }

        if (board != null && board.IsBlockUnderImpassableCell(candidate))
        {
            return "block is behind a closed shutter";
        }

        if (!candidate.isActiveAndEnabled)
        {
            return "block inactive/not alive";
        }

        if (skipIds != null && skipIds.Contains(AutoMatchSkipKey(candidate.GetInstanceID(), world)))
        {
            return "candidate skipped by previous-match key";
        }

        Block occupant = board != null ? board.GetBlockAt(world) : null;
        if (occupant != candidate)
        {
            return occupant == null
                ? "occupancy missing"
                : $"occupancy mismatch (GetBlockAt={occupant.GetInstanceID()} expected={candidate.GetInstanceID()})";
        }

        Target target = board != null ? board.GetTargetAt(world) : null;
        if (target == null)
        {
            return "target not found";
        }

        if (!target.isActiveAndEnabled)
        {
            return "target inactive";
        }

        MatchIdentity offered = candidate.GetActiveIdentity(cellIndex);
        if (!ShapeMatch.AreMatchingLayers(target.GetRequiredIdentityAtWorld(world), offered))
        {
            return $"identity mismatch (block={offered} required={target.GetRequiredIdentityAtWorld(world)})";
        }

        Vector2Int expectedWorld = candidate.GridPosition + candidate.GetLocalCell(cellIndex);
        if (expectedWorld != world)
        {
            return $"world/local mismatch (expected {expectedWorld} got {world})";
        }

        return null;
    }

    private static readonly Vector2Int[] AutoMatchCardinals =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    /// <summary>
    /// One-cell nest next to an occupied chain cell. Traveler-only; dest must be empty of blocks.
    /// </summary>
    public static bool TryGetAdjacentAutoMatchDest(
        BoardManager board,
        Block candidate,
        int cellIndex,
        Vector2Int sourceWorld,
        HashSet<int> skipIds,
        out Vector2Int dest,
        out string reject)
    {
        dest = sourceWorld;
        reject = "no adjacent matching target";
        if (board == null || candidate == null || candidate.IsSettled || candidate.IsFrozen || !candidate.isActiveAndEnabled)
        {
            reject = candidate == null
                ? "block null"
                : candidate.IsSettled
                    ? "block considered moving/settled"
                    : candidate.IsFrozen
                        ? "block is frozen"
                    : "block inactive/not alive";
            return false;
        }

        if (board.GetBlockAt(sourceWorld) != candidate)
        {
            reject = "occupancy missing";
            return false;
        }

        if (board.IsCellImpassable(sourceWorld))
        {
            reject = "source is behind a closed shutter";
            return false;
        }

        MatchIdentity offered = candidate.GetActiveIdentity(cellIndex);
        for (int i = 0; i < AutoMatchCardinals.Length; i++)
        {
            Vector2Int next = sourceWorld + AutoMatchCardinals[i];
            if (skipIds != null && skipIds.Contains(AutoMatchSkipKey(candidate.GetInstanceID(), next)))
            {
                continue;
            }

            if (board.GetBlockAt(next) != null)
            {
                continue;
            }

            if (board.IsCellImpassable(next))
            {
                continue;
            }

            Target target = board.GetTargetAt(next);
            if (target == null || !target.isActiveAndEnabled)
            {
                continue;
            }

            if (!ShapeMatch.AreMatchingLayers(target.GetRequiredIdentityAtWorld(next), offered))
            {
                continue;
            }

            dest = next;
            reject = null;
            return true;
        }

        return false;
    }

    public static bool IsChainCellAutoMatchValid(BoardManager board, Block candidate, Vector2Int nestTo)
    {
        if (IsWorldCellOccupyingAlignedMatch(board, candidate, nestTo))
        {
            return true;
        }

        if (board != null && board.IsBlockUnderImpassableCell(candidate))
        {
            return false;
        }

        if (board == null || candidate == null || candidate.IsSettled || candidate.IsFrozen || !candidate.isActiveAndEnabled)
        {
            return false;
        }

        if (board.IsCellImpassable(nestTo))
        {
            return false;
        }

        Target destTarget = board.GetTargetAt(nestTo);
        if (destTarget == null || !destTarget.isActiveAndEnabled || board.GetBlockAt(nestTo) != null)
        {
            return false;
        }

        int count = Mathf.Max(1, candidate.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = candidate.GridPosition + candidate.GetLocalCell(i);
            if (board.GetBlockAt(world) != candidate)
            {
                continue;
            }

            if (!IsFourAdjacent(world, nestTo))
            {
                continue;
            }

            if (ShapeMatch.AreMatchingLayers(
                    destTarget.GetRequiredIdentityAtWorld(nestTo),
                    candidate.GetActiveIdentity(i)))
            {
                return true;
            }
        }

        return false;
    }

    public static void LogChainAutoMatchPostMatch(
        BoardManager board,
        Block survivor,
        Vector2Int consumedWorld,
        ShapeType consumedShape)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== CHAIN AUTO MATCH POST MATCH #1 ===");
        sb.AppendLine($"Consumed cell: {consumedWorld}");
        sb.AppendLine($"Consumed shape: {consumedShape}");
        sb.AppendLine();
        if (survivor == null || survivor.IsSettled)
        {
            sb.AppendLine("SURVIVING BLOCK");
            sb.AppendLine("Block ID: NONE");
            sb.AppendLine("=== END POST MATCH #1 ===");
            //Debug.Log(sb.ToString());
            return;
        }

        sb.AppendLine("SURVIVING BLOCK");
        sb.AppendLine($"Block ID: {survivor.GetInstanceID()}");
        sb.AppendLine($"GridPosition: {survivor.GridPosition}");
        sb.AppendLine($"CellCount: {survivor.CellCount}");
        sb.AppendLine();

        int count = Mathf.Max(1, survivor.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = survivor.GridPosition + survivor.GetLocalCell(i);
            Target occupying = board != null ? board.GetTargetAt(world) : null;
            Block occ = board != null ? board.GetBlockAt(world) : null;
            bool foundAdj = TryGetAdjacentAutoMatchDest(
                board,
                survivor,
                i,
                world,
                null,
                out Vector2Int adjDest,
                out string adjReject);
            sb.AppendLine($"CELL {i}");
            sb.AppendLine($"World: {world}");
            sb.AppendLine($"Local: {survivor.GetLocalCell(i)}");
            sb.AppendLine($"ActiveShape: {survivor.GetActiveShape(i)}");
            sb.AppendLine($"TargetAtWorld: {(occupying != null ? occupying.RequiredShape.ToString() : "NULL")}");
            sb.AppendLine($"TargetRequiredShape: {(occupying != null ? occupying.RequiredShape.ToString() : "n/a")}");
            sb.AppendLine($"GetBlockAtWorld == this: {occ == survivor}");
            sb.AppendLine(
                $"AdjacentDest: {(foundAdj ? adjDest.ToString() : "none")} " +
                $"reject={(adjReject ?? "none")}");
            sb.AppendLine(
                $"OccupyingCandidate: {ExplainAlignedCellRejection(board, survivor, i, world, null) == null}");
            sb.AppendLine($"AdjacentCandidate: {foundAdj}");
            sb.AppendLine();
        }

        sb.AppendLine("ALL TARGETS");
        if (board != null)
        {
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    Target target = board.GetTargetAt(cell);
                    if (target == null)
                    {
                        continue;
                    }

                    sb.AppendLine($"Target world: {cell} RequiredShape: {target.RequiredShape}");
                }
            }
        }

        sb.AppendLine("=== END POST MATCH #1 ===");
        //Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Single dump after a partial consume: survivor cell vs target vs occupancy validity.
    /// </summary>
    public static void LogPostFirstMatchState(BoardManager board, Block survivor)
    {
        if (survivor == null || survivor.IsSettled)
        {
            //Debug.Log("POST-FIRST-MATCH STATE\nBlock NONE");
            return;
        }

        Vector2Int world = survivor.GridPosition;
        Block occ = board != null ? board.GetBlockAt(world) : null;
        Target target = board != null ? board.GetTargetAt(world) : null;
        bool valid = IsWorldCellOccupyingAlignedMatch(board, survivor, world);
        string reject = ExplainAlignedCellRejection(board, survivor, 0, world, null);
        //Debug.Log(
            // "POST-FIRST-MATCH STATE\n" +
            // $"Block {survivor.GetInstanceID()}\n" +
            // $"Cell 0\n" +
            // $"World {world}\n" +
            // $"Shape {survivor.GetActiveShape(0)}\n" +
            // $"TargetAtCell {(target != null ? target.GetInstanceID().ToString() : "NULL")}\n" +
            // $"RequiredShape {(target != null ? target.RequiredShape.ToString() : "n/a")}\n" +
            // $"OccupancyOwner {(occ != null ? occ.GetInstanceID().ToString() : "NULL")}\n" +
            // $"ValidOccupyingMatch {valid}\n" +
            // $"Reject {(reject ?? "none")}");
    }

    public static void LogPostConsumeAutoMatchTrace(
        BoardManager board,
        Block subject,
        Vector2Int consumedWorld,
        Vector2Int consumedTargetWorld,
        bool fullyConsumed)
    {
        //Debug.Log(
            // $"[AUTO MATCH POST-CONSUME] consumedWorld={consumedWorld} targetWorld={consumedTargetWorld} " +
            // $"fullyConsumed={fullyConsumed} LastConsumeSucceeded={LastConsumeSucceeded}");

        if (subject == null || subject.IsSettled)
        {
            //Debug.Log("[AUTO MATCH POST-CONSUME] survivor Block: NONE (settled or null)");
            LogRemainingTargets(board);
            return;
        }

        int id = subject.GetInstanceID();
        int count = Mathf.Max(1, subject.CellCount);
        //Debug.Log(
            // $"[AUTO MATCH POST-CONSUME] survivor exists=YES id={id} GridPosition={subject.GridPosition} " +
            // $"CellCount={subject.CellCount} ShapeType={subject.ShapeType} ActiveShape0={subject.GetActiveShape(0)} " +
            // $"Settled={subject.IsSettled} Active={subject.isActiveAndEnabled}");

        bool inUnique = false;
        if (board != null)
        {
            var unique = new List<Block>();
            board.CollectUniqueBlocks(unique);
            for (int i = 0; i < unique.Count; i++)
            {
                if (unique[i] == subject)
                {
                    inUnique = true;
                    break;
                }
            }
        }

        //Debug.Log($"[AUTO MATCH POST-CONSUME] CollectUniqueBlocks contains survivor: {inUnique}");

        for (int i = 0; i < count; i++)
        {
            Vector2Int local = subject.GetLocalCell(i);
            Vector2Int world = subject.GridPosition + local;
            Block occupant = board != null ? board.GetBlockAt(world) : null;
            Target target = board != null ? board.GetTargetAt(world) : null;
            //      Debug.Log(
                //  $"[AUTO MATCH POST-CONSUME] cell[{i}] local={local} world={world} " +
                // $"activeShape={subject.GetActiveShape(i)} " +
                // $"GetBlockAt={(occupant != null ? occupant.GetInstanceID().ToString() : "NULL")} " +
                // $"sameAsSurvivor={occupant == subject} " +
                // $"Target={(target != null ? target.GetInstanceID().ToString() : "NULL")} " +
                // $"Required={(target != null ? target.RequiredShape.ToString() : "n/a")}");

            if (target != null)
            {
                //Debug.Log(
                    // $"[AUTO MATCH POST-CONSUME] coord compare cell[{i}]: " +
                    // $"blockWorld={world} targetWorld={target.GridPosition} " +
                    // $"equal={world == target.GridPosition}");
            }
        }

        LogRemainingTargets(board);

        if (board != null)
        {
            var scratch = new List<Block>();
            LogAutoMatchScan(board, scratch, null);
            bool found = TryFindNextAlignedMatch(
                board,
                scratch,
                null,
                true,
                consumedWorld,
                consumedTargetWorld,
                out Block next,
                out Vector2Int nestTo);
            // Debug.Log(
            //     $"[AUTO MATCH POST-CONSUME] immediate next candidate found={found} " +
            //     $"block={(next != null ? next.GetInstanceID().ToString() : "NULL")} nestTo={nestTo}");
        }
    }

    public static void LogRemainingTargets(BoardManager board)
    {
        if (board == null)
        {
            //Debug.Log("[AUTO MATCH TARGETS] board null");
            return;
        }

        // BoardManager does not expose a target enumerator; probe every cell.
        // Debug.Log($"[AUTO MATCH TARGETS] Remaining targets on {board.Width}x{board.Height}:");
        for (int y = 0; y < board.Height; y++)
        {
            for (int x = 0; x < board.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Target target = board.GetTargetAt(cell);
                if (target == null)
                {
                    continue;
                }

                // Debug.Log(
                    // $"[AUTO MATCH TARGETS] Target {target.GetInstanceID()} → world {cell} " +
                    // $"(GridPosition={target.GridPosition}) RequiredShape={target.RequiredShape} " +
                    // $"Active={target.isActiveAndEnabled}");
            }
        }
    }

    public static int AutoMatchSkipKey(int instanceId, Vector2Int cell)
    {
        return (instanceId * 397) ^ (cell.x * 17) ^ (cell.y * 31);
    }

    public bool TryRevalidateAlignedCandidate(BoardManager board, Vector2Int nestTo)
    {
        if (board == null)
        {
            return false;
        }

        if (block == null)
        {
            block = GetComponent<Block>();
        }

        if (block == null)
        {
            //Debug.Log("REJECT TryRevalidateAlignedCandidate:\n- BlockMover.block is null");
            return false;
        }

        EnsureSubjectOccupancy(board, block);
        bool ok = IsWorldCellOccupyingAlignedMatch(board, block, nestTo);
        if (!ok)
        {
            int cellIndex = 0;
            for (int i = 0; i < block.CellCount; i++)
            {
                if (block.GridPosition + block.GetLocalCell(i) == nestTo)
                {
                    cellIndex = i;
                    break;
                }
            }

            string reason = ExplainAlignedCellRejection(board, block, cellIndex, nestTo, null)
                ?? "revalidate failed (no footprint cell at nestTo)";
            //  Debug.Log($"REJECT TryRevalidateAlignedCandidate Block={block.GetInstanceID()} nestTo={nestTo}:\n- {reason}");
        }

        return ok;
    }

    public static bool IsWorldCellOccupyingAlignedMatch(BoardManager board, Block candidate, Vector2Int world)
    {
        if (board == null || candidate == null || candidate.IsSettled || candidate.IsFrozen || !candidate.isActiveAndEnabled)
        {
            return false;
        }

        if (board.GetBlockAt(world) != candidate)
        {
            return false;
        }

        Target target = board.GetTargetAt(world);
        if (target == null || !target.isActiveAndEnabled)
        {
            return false;
        }

        int count = Mathf.Max(1, candidate.CellCount);
        for (int i = 0; i < count; i++)
        {
            if (candidate.GridPosition + candidate.GetLocalCell(i) != world)
            {
                continue;
            }

            return ShapeMatch.AreMatchingLayers(
                target.GetRequiredIdentityAtWorld(world),
                candidate.GetActiveIdentity(i));
        }

        return false;
    }

    public static int AlignedMatchPriority(
        bool hasLastMatch,
        Vector2Int alignedCell,
        Vector2Int nestTo,
        Vector2Int lastMatchOrigin,
        Vector2Int lastMatchTargetCell)
    {
        if (hasLastMatch && (alignedCell == lastMatchOrigin || nestTo == lastMatchTargetCell))
        {
            return 0;
        }

        bool originAdjacent = hasLastMatch && IsFourAdjacent(alignedCell, lastMatchOrigin);
        bool targetAdjacent = hasLastMatch && IsFourAdjacent(nestTo, lastMatchTargetCell);
        if (originAdjacent && targetAdjacent)
        {
            return 1;
        }

        if (originAdjacent || targetAdjacent)
        {
            return 2;
        }

        return 3;
    }

    public static bool IsOccupyingAlignedMatch(
        Vector2Int blockCell,
        Vector2Int targetCell,
        MatchIdentity offered,
        MatchIdentity required)
    {
        return blockCell == targetCell && ShapeMatch.AreMatchingLayers(offered, required);
    }

    public static bool IsOccupyingAlignedMatch(
        Vector2Int blockCell,
        Vector2Int targetCell,
        ShapeType offered,
        ShapeType required)
    {
        return IsOccupyingAlignedMatch(
            blockCell,
            targetCell,
            new MatchIdentity(offered, ShapeColor.Default),
            new MatchIdentity(required, ShapeColor.Default));
    }

    private void RememberLastMatch(Vector2Int origin, Vector2Int targetCell)
    {
        hasLastMatch = true;
        lastMatchOrigin = origin;
        lastMatchTargetCell = targetCell;
        if (levelManager != null)
        {
            levelManager.RememberLastMatch(origin, targetCell);
        }
    }

    public static bool IsFourAdjacent(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;
    }

    private bool ConsumeAndRebuild(
        BoardManager board,
        Block subject,
        Vector2Int anchor,
        out Target completedTarget,
        out ShapeType consumedShape,
        out Vector2Int effectCell,
        out bool consumedInnerLayer)
    {
        LastConsumeSucceeded = false;
        LastResolvedConsumeSucceeded = false;
        completedTarget = null;
        consumedShape = subject != null ? subject.GetActiveShape(0) : ShapeType.Square;
        effectCell = anchor;
        consumedInnerLayer = false;
        pendingExtractionRevealBlocks.Clear();
        if (subject == null || nestCellIndices.Count == 0)
        {
            return false;
        }

        Phase68CForensic.Log(
            "CONSUME_BEGIN",
            $"block={subject.GetInstanceID()} nestCount={nestCellIndices.Count} " +
            $"grid={subject.GridPosition} cellCount={subject.CellCount}");
        for (int ci = 0; ci < nestCellIndices.Count; ci++)
        {
            Phase68CForensic.LogCell("CONSUME_CELL_BEFORE", subject, nestCellIndices[ci]);
        }

        var consumedIndices = new HashSet<int>();
        var promotedIndices = new List<int>();
        bool consumedAny = false;
        Vector2Int occupancyAnchor = subject.GridPosition;
        effectCell = occupancyAnchor + subject.GetLocalCell(nestCellIndices[0]);

        for (int n = 0; n < nestCellIndices.Count; n++)
        {
            int cellIndex = nestCellIndices[n];
            if (cellIndex < 0 || cellIndex >= subject.CellCount)
            {
                continue;
            }

            Vector2Int cellWorld = occupancyAnchor + subject.GetLocalCell(cellIndex);
            Target target = n < nestTargets.Count ? nestTargets[n] : null;
            ShapeType offered = subject.GetActiveShape(cellIndex);
            MatchIdentity offeredIdentity = subject.GetActiveIdentity(cellIndex);
            Vector2Int matchedTargetWorld = n < nestTargetWorlds.Count
                ? nestTargetWorlds[n]
                : ResolveMatchedTargetWorld(board, target, offeredIdentity, cellWorld);

            if (n == 0)
            {
                effectCell = matchedTargetWorld;
                consumedShape = offered;
            }

            if (target == null
                || !target.TryConsumeLayerAtWorld(matchedTargetWorld, offeredIdentity, out bool targetComplete))
            {
                continue;
            }

            consumedShape = offered;
            consumedAny = true;
            LastConsumeSucceeded = true;
            LastResolvedConsumeSucceeded = true;
            if (levelManager != null)
            {
                levelManager.NotifySuccessfulMatch();
            }

            ShapeCellData cell = subject.GetCell(cellIndex);
            bool cellGone = true;
            if (cell != null)
            {
                if (!ShapeLayout.TryConsumeLayer(cell, offeredIdentity, out bool cellRemains))
                {
                    // Target consumed but block layer rejected — should not happen when identities match.
                    return false;
                }

                cellGone = !cellRemains;
                if (cellRemains)
                {
                    consumedInnerLayer = true;
                    promotedIndices.Add(cellIndex);
                }
            }

            if (cellGone)
            {
                consumedIndices.Add(cellIndex);
            }

            if (targetComplete)
            {
                completedTarget = target;
                target.BeginMatchPresentation();
                board.UnregisterTarget(target);
            }
        }

        if (!consumedAny)
        {
            return false;
        }

        PlayLogicalMatchFeedback(
            consumedInnerLayer,
            subject.CellCount - consumedIndices.Count == 0);

        if (consumedIndices.Count == 0)
        {
            // Outer consumed, inner promoted: keep cell, occupancy, and chain topology.
            // Defer World3D mesh promote until PlayNestedExtractionReveal (avoids mid-VFX pop).
            // Hide travelers while residuals cover SOURCE — otherwise the consumed outer
            // stays rendered under/around the survivor for the whole VFX window (and forever
            // if a sibling cell never gets a reveal).
            subject.RefreshActiveLayers(syncWorldPresentation: false);
            for (int i = 0; i < promotedIndices.Count; i++)
            {
                subject.BeginPendingLayerExtraction(promotedIndices[i]);
            }

            pendingExtractionRevealBlocks.Clear();
            if (subject.HasPendingLayerExtraction)
            {
                pendingExtractionRevealBlocks.Add(subject);
                BoardPresentationController.HoldPendingExtractionViewsAtSource(subject);
            }

            Phase68CForensic.Log(
                "CONSUME_PROMOTE",
                $"block={subject.GetInstanceID()} promoted={promotedIndices.Count}");
            for (int i = 0; i < promotedIndices.Count; i++)
            {
                Phase68CForensic.LogCell("CONSUME_AFTER_PROMOTE", subject, promotedIndices[i]);
            }

            return false;
        }

        // Phase 71B: mixed peel+remove (e.g. orange chain cell0 plain + cell1/2 nested).
        // Remember promoted SOURCE worlds before rebuild so pending extraction survives
        // RebuildFromRemaining's pending clear + cell-index remap.
        var promotedWorlds = new HashSet<Vector2Int>();
        if (promotedIndices.Count > 0)
        {
            for (int i = 0; i < promotedIndices.Count; i++)
            {
                int idx = promotedIndices[i];
                if (idx >= 0 && idx < subject.CellCount)
                {
                    promotedWorlds.Add(occupancyAnchor + subject.GetLocalCell(idx));
                }
            }
        }

        splitWorlds.Clear();
        splitCells.Clear();
        int count = subject.CellCount;
        for (int i = 0; i < count; i++)
        {
            if (consumedIndices.Contains(i))
            {
                continue;
            }

            ShapeCellData source = subject.GetCell(i);
            splitWorlds.Add(occupancyAnchor + subject.GetLocalCell(i));
            splitCells.Add(new ShapeCellData
            {
                localPosition = Vector2Int.zero,
                shapeType = source != null ? source.shapeType : subject.GetActiveShape(i),
                outerColor = source != null ? source.outerColor : ShapeColor.Default,
                innerShapes = source != null
                    ? ShapeLayout.CloneInners(source.innerShapes)
                    : new List<ShapeType>(),
                innerShapeColors = source != null
                    ? ShapeLayout.CloneInnerColors(source.innerShapeColors)
                    : new List<ShapeColor>()
            });
        }

        board.UnregisterBlock(subject);
        ShapeLayout.SplitConnected(splitWorlds, splitCells, splitAnchors, splitComponents);

        if (splitComponents.Count == 0)
        {
            subject.BeginMatchPresentation();
            subject.Settle();
            BoardPresentationController.RebindAnchoredNestedResidualsToBoard(board);
            return true;
        }

        // 1. REBUILD THE PRIMARY SURVIVOR
        subject.RebuildFromRemaining(splitComponents[0], splitAnchors[0]);

        // --- FORCE RESET BOARD GRID REGISTRATION ---
        board.UnregisterBlock(subject);
        bool ok = board.TryRegisterBlock(subject, splitAnchors[0]);

        if (!ok)
        {
            // Primary survivor failed to re-register — occupancy may be stale until next rebind.
        }

        var revealSubjects = new List<Block>(splitComponents.Count) { subject };

        // 2. HANDLE ADDITIONAL SPLIT SURVIVORS
        for (int i = 1; i < splitComponents.Count; i++)
        {
            if (levelManager != null)
            {
                Block newSplitBlock = levelManager.SpawnSplitBlock(subject, splitComponents[i], splitAnchors[i]);

                if (newSplitBlock != null)
                {
                    board.UnregisterBlock(newSplitBlock);
                    board.TryRegisterBlock(newSplitBlock, splitAnchors[i]);
                    revealSubjects.Add(newSplitBlock);
                }
            }
        }

        // Rebind residuals to post-rebuild cell indices, then restore pending extraction
        // so PlayNestedExtractionReveal still seats inners at SOURCE (orange mixed chain).
        BoardPresentationController.RebindAnchoredNestedResidualsToBoard(board);
        if (promotedWorlds.Count > 0)
        {
            for (int s = 0; s < revealSubjects.Count; s++)
            {
                MarkPendingExtractionAtWorlds(revealSubjects[s], promotedWorlds);
            }

            consumedInnerLayer = true;
            pendingExtractionRevealBlocks.Clear();
            for (int s = 0; s < revealSubjects.Count; s++)
            {
                Block revealBlock = revealSubjects[s];
                if (revealBlock != null && !revealBlock.IsSettled && revealBlock.HasPendingLayerExtraction)
                {
                    pendingExtractionRevealBlocks.Add(revealBlock);
                    BoardPresentationController.HoldPendingExtractionViewsAtSource(revealBlock);
                }
            }
        }

        return false;
    }

    private static void MarkPendingExtractionAtWorlds(Block subject, HashSet<Vector2Int> worlds)
    {
        if (subject == null || subject.IsSettled || worlds == null || worlds.Count == 0)
        {
            return;
        }

        for (int i = 0; i < subject.CellCount; i++)
        {
            if (worlds.Contains(subject.GetCellWorld(i)))
            {
                subject.BeginPendingLayerExtraction(i);
            }
        }
    }

    /// <summary>
    /// Resolves which nest cell of a multi-cell Target was matched.
    /// Prefers an explicit nestTargetWorlds entry, then occupying overlap, then nearest matching cell.
    /// </summary>
    private Vector2Int ResolveMatchedTargetWorld(
        BoardManager board,
        Target target,
        MatchIdentity offered,
        Vector2Int blockCellWorld)
    {
        if (nestTargetWorlds.Count > 0)
        {
            return nestTargetWorlds[0];
        }

        if (target == null)
        {
            return blockCellWorld;
        }

        if (board != null && board.GetTargetAt(blockCellWorld) == target
            && ShapeMatch.AreMatchingLayers(target.GetRequiredIdentityAtWorld(blockCellWorld), offered))
        {
            return blockCellWorld;
        }

        int count = Mathf.Max(1, target.CellCount);
        Vector2Int best = target.GridPosition;
        int bestDist = int.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = target.GridPosition + target.GetLocalCell(i);
            if (!ShapeMatch.AreMatchingLayers(target.GetRequiredIdentityAtWorld(world), offered))
            {
                continue;
            }

            int dist = Mathf.Abs(world.x - blockCellWorld.x) + Mathf.Abs(world.y - blockCellWorld.y);
            if (dist >= bestDist)
            {
                continue;
            }

            bestDist = dist;
            best = world;
            found = true;
        }

        return found ? best : blockCellWorld;
    }

    private void SyncNestTargetWorldsFromOccupying(Block subject, Vector2Int occupancyAnchor)
    {
        nestTargetWorlds.Clear();
        if (subject == null)
        {
            return;
        }

        for (int i = 0; i < nestCellIndices.Count; i++)
        {
            int cellIndex = nestCellIndices[i];
            nestTargetWorlds.Add(occupancyAnchor + subject.GetLocalCell(cellIndex));
        }
    }

    private Vector2Int ClampDragDestination(
        BoardManager board,
        Vector2Int start,
        Vector2Int direction,
        Vector2Int requested)
    {
        int maxSteps = AxisSteps(requested - start, direction);
        if (maxSteps <= 0)
        {
            return start;
        }

        Vector2Int current = start;
        int steps = 0;
        while (steps < maxSteps)
        {
            Vector2Int next = current + direction;
            if (!board.CanTranslateBlock(block, next) || board.FootprintTouchesTarget(block, next))
            {
                return current;
            }

            current = next;
            steps++;
        }

        return current;
    }

    private static int AxisSteps(Vector2Int offset, Vector2Int direction)
    {
        return (offset.x * direction.x) + (offset.y * direction.y);
    }

    private BoardManager GetBoard()
    {
        if (block == null)
        {
            return null;
        }

        return block.Board != null ? block.Board : GetComponentInParent<BoardManager>();
    }

    private bool IsMatchingTargetCell(BoardManager board, Vector2Int cell)
    {
        return board.HasNestMatch(block, cell);
    }

    private IEnumerator AnimateHop(
        BoardManager board,
        Vector2Int from,
        Vector2Int to,
        float duration,
        bool anticipate)
    {
        IPieceMotion motion = MotionFor(block);
        if (motion == null || board == null)
        {
            yield break;
        }

        visualHopFrom = from;
        visualHopActive = true;
        try
        {
            yield return motion.AnimateHop(
                MotionGridSpace(board),
                MotionCellSize(board),
                from,
                to,
                duration,
                anticipate,
                dragDirection,
                normalHopAnticipateDuration,
                normalHopAnticipatePercent,
                hopTravelScale,
                hopLiftPercent);
        }
        finally
        {
            visualHopActive = false;
        }
    }

    private IEnumerator AnimateAnticipation(
        BoardManager board,
        Vector2 restPosition,
        Vector3 restScale)
    {
        yield return AnimateAnticipation(board, block, restPosition, restScale);
    }

    private IEnumerator AnimateAnticipation(
        BoardManager board,
        Block subject,
        Vector2 restPosition,
        Vector3 restScale)
    {
        IPieceMotion motion = MotionFor(subject);
        if (motion == null || board == null)
        {
            yield break;
        }

        yield return motion.AnimateNestAnticipate(
            MotionCellSize(board),
            restPosition,
            restScale,
            matchingTargetAnticipateDuration,
            matchingTargetAnticipateLiftPercent,
            matchingTargetAnticipateScale);
    }

    private IEnumerator AnimateNestEntry(
        BoardManager board,
        Vector2Int from,
        Vector2Int to,
        Vector3 restScale)
    {
        yield return AnimateNestEntry(board, block, from, to, restScale);
    }

    private IEnumerator AnimateNestEntry(
        BoardManager board,
        Block subject,
        Vector2Int from,
        Vector2Int to,
        Vector3 restScale)
    {
        IPieceMotion motion = MotionFor(subject);
        if (motion == null || board == null)
        {
            yield break;
        }

        yield return motion.AnimateNestEntry(
            MotionGridSpace(board),
            MotionCellSize(board),
            from,
            to,
            restScale,
            matchingTargetLiftPercent,
            matchingTargetArcDuration,
            matchingTargetSitDuration,
            matchingTargetHopScale);
    }

    private void ApplyMotionRestScale(Block subject, Vector3 restScale)
    {
        if (subject == null)
        {
            return;
        }

        if (subject.WorldView != null)
        {
            subject.WorldView.LocalScale = restScale;
            return;
        }

        if (subject.View != null)
        {
            subject.View.LocalScale = restScale;
        }
    }

    private IEnumerator PlayMatchEffect(
        BoardManager board,
        Vector2Int nestCell,
        ShapeType glowShape,
        Target nestTarget,
        Block dissolvingBlock,
        int matchId = -1)
    {
        if (matchId < 0)
        {
            matchSequenceIndex++;
            matchId = matchSequenceIndex;
        }

        // Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} VFX START");
        if (matchEffectPrefab == null)
        {
            yield return PulseTarget(board, nestCell);
            Vector3 vfxPos = ResolveNestMatchWorldPosition(board, nestCell, nestTarget, dissolvingBlock);
            BoardVfx3D.PlayNestMatch(vfxPos, ShapeVisuals3D.AccentColor(glowShape));
            if (dissolvingBlock != null)
            {
                dissolvingBlock.CompleteMatchPresentation();
            }

            if (nestTarget != null)
            {
                nestTarget.CompleteMatchPresentation();
            }

            // Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} VFX COMPLETE");
            yield break;
        }

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        Transform vfxRoot = presenter != null ? presenter.VfxRoot : null;
        Vector3 nestWorld = ResolveNestMatchWorldPosition(board, nestCell, nestTarget, dissolvingBlock);
        float cellWorld = presenter != null ? presenter.CellWorldSize : 1f;

        MatchEffect effect = Instantiate(matchEffectPrefab);
        activeMatchEffect = effect;
        effect.SetupWorldPresentation(vfxRoot, nestWorld, cellWorld);

        try
        {
            // Host on this BlockMover so LevelManager-nested auto-match enumerators
            // cannot resume until MatchEffect.Play (impact + dissolve) fully finishes.
            yield return StartCoroutine(effect.Play(glowShape, dissolvingBlock, nestTarget));
        }
        finally
        {
            if (effect != null)
            {
                // Destroy is deferred; finalize presentation now so interrupt/restart cannot
                // leave Matching/Entering without CompleteMatchPresentation.
                effect.AbortPresentation();
                Destroy(effect.gameObject);
            }

            if (activeMatchEffect == effect)
            {
                activeMatchEffect = null;
            }

            // Covers ConsumeAndRebuild BeginMatchPresentation when Play never started.
            EnsureMatchPresentationCompleted(dissolvingBlock, nestTarget);
        }

        // Debug.Log($"[MATCH SEQUENCE] MATCH {matchId} VFX COMPLETE");
    }

    private static Vector3 ResolveNestMatchWorldPosition(
        BoardManager board,
        Vector2Int nestCell,
        Target nestTarget,
        Block dissolvingBlock)
    {
        if (TryGetVisiblePieceCenter(nestTarget != null ? nestTarget.WorldView : null, out Vector3 nestCenter))
        {
            return nestCenter;
        }

        if (TryGetVisiblePieceCenter(dissolvingBlock != null ? dissolvingBlock.WorldView : null, out Vector3 blockCenter))
        {
            return blockCenter;
        }

        if (nestTarget != null && nestTarget.WorldView != null)
        {
            return nestTarget.WorldView.transform.position;
        }

        if (dissolvingBlock != null && dissolvingBlock.WorldView != null)
        {
            return dissolvingBlock.WorldView.transform.position;
        }

        if (board != null)
        {
            BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
            if (presenter != null && presenter.GridSpace != null)
            {
                Vector3 cellWorld = presenter.GridSpace.GridToWorld(nestCell);
                float halfHeight = presenter.CellWorldSize
                    * BoardAdaptivePresentation3D.NestHeightRatio
                    * 0.5f;
                cellWorld.y = presenter.CellSurfaceWorldY + halfHeight;
                return cellWorld;
            }
        }

        return Vector3.zero;
    }

    /// <summary>
    /// Visible nest/block center from MeshRenderer world AABB (shape-independent).
    /// Presentation only — does not affect gameplay grid positions.
    /// </summary>
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

        // XZ = visual AABB center (Phase 19D); Y = nest top surface (Phase 19F).
        Bounds bounds = renderer.bounds;
        center = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        return true;
    }

    /// <summary>
    /// Presentation-only: hide World3D/UI match visuals if dissolve never finalized.
    /// Does not alter occupancy, consume, or win/loss.
    /// </summary>
    private static void EnsureMatchPresentationCompleted(Block block, Target target)
    {
        if (block != null && block.IsMatchPresentationActive && !block.IsMatched)
        {
            block.CompleteMatchPresentation();
        }

        if (target != null && target.IsMatchPresentationActive && !target.IsMatched)
        {
            target.CompleteMatchPresentation();
        }
    }

    private IEnumerator PulseTarget(BoardManager board, Vector2Int nestCell)
    {
        Target target = board.GetTargetAt(nestCell);
        if (target == null)
        {
            yield break;
        }

        target.HideReadyFeedback();

        if (matchingTargetPulseDuration <= 0f || matchingTargetPulseScale <= 1f)
        {
            yield break;
        }

        RectTransform targetRect = target.RectTransform;
        Vector3 restScale = targetRect.localScale;
        Vector3 peakScale = restScale * matchingTargetPulseScale;
        float half = matchingTargetPulseDuration * 0.45f;
        yield return AnimateTransformScale(targetRect, restScale, peakScale, half, false);
        yield return AnimateTransformScale(targetRect, peakScale, restScale, matchingTargetPulseDuration - half, true);
        targetRect.localScale = restScale;
    }

    private static IEnumerator AnimateTransformScale(
        RectTransform rect,
        Vector3 from,
        Vector3 to,
        float duration,
        bool easeOut)
    {
        if (rect == null)
        {
            yield break;
        }

        if (duration <= 0f)
        {
            rect.localScale = to;
            yield break;
        }

        Tween tween = TweenAnimationUtility.Progress(duration, t =>
        {
            float eased = easeOut
                ? TweenAnimationUtility.EvaluateEaseOutQuad(t)
                : TweenAnimationUtility.EvaluateSmoothStep(t);
            rect.localScale = Vector3.LerpUnclamped(from, to, eased);
        }).SetId(TweenAnimationUtility.TravelerId).SetLink(rect.gameObject);
        yield return TweenAnimationUtility.Wait(tween);
        rect.localScale = to;
    }

    private static IEnumerator Pause(float duration)
    {
        yield return TweenAnimationUtility.WaitInterval(duration);
    }

    private void LogDrag(string message)
    {
        if (debugDrag)
        {
            // Debug.Log($"BlockMover: {message}", this);
        }
    }
}