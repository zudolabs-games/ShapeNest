using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// Fingerwise drag input. Touch/mouse feed one drag session that maps continuous
/// finger board motion into existing <see cref="BlockMover"/> requests
/// (<c>TryBeginFingerDrag</c> / <c>SetDragDirection</c> / <c>SetDragRequest</c> / <c>EndDrag</c>).
/// BlockMover remains the only movement sequencer and match authority.
/// </summary>
public class InputManager : MonoBehaviour
{
    /// <summary>Legacy screen-pixel threshold retained for editor/test drag helpers.</summary>
    public const float DefaultDragThresholdPixels = 20f;

    private const float FirstStepCellFractionDefault = 0.38f;
    private const float DirectionChangeCellFractionDefault = 0.18f;
    private const float CellStrideFraction = 0.92f;

    [SerializeField]
    [Min(16f)]
    [Tooltip("Screen-pixel distance used by editor/test drag helpers (not gameplay gating).")]
    private float dragThresholdPixels = DefaultDragThresholdPixels;

    [SerializeField]
    [Range(0.28f, 0.55f)]
    [Tooltip("Fraction of one on-screen cell used by editor/test first-hop helpers.")]
    private float firstStepCellFraction = FirstStepCellFractionDefault;

    [SerializeField]
    [Range(0.12f, 0.5f)]
    [Tooltip("Fraction of one on-screen cell used by editor/test steer helpers.")]
    private float directionChangeCellFraction = DirectionChangeCellFractionDefault;

    [SerializeField]
    private bool debugDrag;

    [SerializeField]
    private LevelManager levelManager;

    [SerializeField]
    private BoosterManager boosterManager;

    [SerializeField]
    private MagnetBooster magnetBooster;

    [SerializeField]
    private BoardInput3D boardInput3D;

    [SerializeField]
    private BoardCamera3D boardCamera3D;

    [SerializeField]
    private BoardPresenter3D boardPresenter3D;

    private readonly FingerDragController fingerDrag = new FingerDragController();
    private Block pressedBlock;
    private BlockMover pressedMover;
    private Vector2 pressScreenPosition;
    private Vector2 latestScreenPosition;
    private Vector2Int pressGridPosition;
    private bool isPressing;
    private bool directionLocked;
    private Vector2Int lockedDirection;
    private bool fingerDragBegun;
    private int trackedTouchId = -1;
    private bool blockedCuePlayed;
    private Vector2Int lastRequestedCell;
    private bool hasRequestedCell;
    private int dragRequestCount;
    private bool simulatedPointerActive;
    private int cachedLevelIndex = int.MinValue;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>(16);
    private PointerEventData uiPointerData;

    public float DragThresholdPixels => dragThresholdPixels;
    public bool IsPointerSessionActive => isPressing;
    public Block PointerBlock => pressedBlock;
    public bool IsDragDirectionLocked => directionLocked;
    public Vector2Int PointerDragDirection => lockedDirection;
    public Vector2Int LastRequestedCell => lastRequestedCell;
    public int DragRequestCount => dragRequestCount;
    public string LastPointerRejectReason { get; private set; }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnDisable()
    {
        CancelPointerSession();
    }

    private void OnDestroy()
    {
        CancelPointerSession();
    }

    private void Update()
    {
        if (simulatedPointerActive)
        {
            if (ShouldAbortActiveSession())
            {
                EndActiveDragAndClear();
            }

            return;
        }

        if (!TryReadPointer(out Vector2 screenPosition, out bool pressedThisFrame, out bool releasedThisFrame))
        {
            if (isPressing && ShouldAbortActiveSession())
            {
                EndActiveDragAndClear();
            }

            return;
        }

        ProcessPointerFrame(screenPosition, pressedThisFrame, releasedThisFrame);
    }

    /// <summary>
    /// Editor/test entry that uses the same drag session as hardware touch/mouse.
    /// Hardware pointer is ignored until <see cref="SimulatePointerReleased"/> or cancel.
    /// </summary>
    public void SimulatePointerPressed(Vector2 screenPosition)
    {
        simulatedPointerActive = true;
        trackedTouchId = -1;
        ProcessPointerFrame(screenPosition, true, false);
    }

    public void SimulatePointerMoved(Vector2 screenPosition)
    {
        simulatedPointerActive = true;
        ProcessPointerFrame(screenPosition, false, false);
    }

    public void SimulatePointerReleased()
    {
        ProcessPointerFrame(latestScreenPosition, false, true);
        simulatedPointerActive = false;
    }

    public void CancelPointerSession()
    {
        EndActiveDragAndClear();
        simulatedPointerActive = false;
    }

    public Vector2 GetBlockScreenPosition(Block block)
    {
        Camera camera = ResolveCamera();
        if (camera == null || block == null)
        {
            return Vector2.zero;
        }

        if (block.WorldView != null)
        {
            Vector3 world = block.WorldView.PickWorldCenter;
            Vector3 viewScreen = camera.WorldToScreenPoint(world);
            if (viewScreen.z < 0f)
            {
                return Vector2.zero;
            }

            return new Vector2(viewScreen.x, viewScreen.y);
        }

        BoardPresenter3D presenter = ResolvePresenter();
        if (presenter == null)
        {
            return Vector2.zero;
        }

        Vector3 cellScreen = camera.WorldToScreenPoint(presenter.GridSpace3D.GridToWorld(block.GridPosition));
        return new Vector2(cellScreen.x, cellScreen.y);
    }

    public bool TryGetDragMetrics(Vector2Int cell, Vector2Int direction, out float firstStep, out float cellPixels)
    {
        firstStep = dragThresholdPixels;
        cellPixels = dragThresholdPixels;
        if (!TryGetCellScreenAxis(cell, direction, out _, out cellPixels))
        {
            cellPixels = dragThresholdPixels;
            firstStep = dragThresholdPixels;
            return false;
        }

        firstStep = Mathf.Max(dragThresholdPixels * 0.5f, cellPixels * firstStepCellFraction);
        return true;
    }

    public Vector2 GetScreenAxis(Vector2Int cell, Vector2Int direction)
    {
        if (TryGetCellScreenAxis(cell, direction, out Vector2 axis, out _))
        {
            return axis;
        }

        Vector2 fallback = new Vector2(direction.x, direction.y);
        return fallback.sqrMagnitude > 0.001f ? fallback.normalized : Vector2.right;
    }

    /// <summary>
    /// Screen delta that aims about <paramref name="steps"/> grid cells along
    /// <paramref name="direction"/> for editor/test pointer simulation.
    /// </summary>
    public Vector2 GetDragScreenDelta(Vector2Int cell, Vector2Int direction, int steps)
    {
        TryGetDragMetrics(cell, direction, out _, out float cellPixels);
        Vector2 axis = GetScreenAxis(cell, direction);
        if (steps <= 0)
        {
            return axis * (dragThresholdPixels * 0.35f);
        }

        // Fingerwise WorldToGrid rounds at ~0.5 cell; overshoot so N steps commit.
        float pixels = cellPixels * (steps + 0.55f);
        return axis * pixels;
    }

    /// <summary>
    /// Continues an active drag session by moving the pointer along
    /// <paramref name="direction"/> for <paramref name="steps"/> cells from the
    /// current screen position (does not re-press). Phase 61 continuous-drag helper.
    /// </summary>
    public Vector2 AppendDragScreenDelta(Vector2Int direction, int steps)
    {
        Vector2Int cell = pressedMover != null ? pressedMover.LogicalCell : pressGridPosition;
        TryGetDragMetrics(cell, direction, out _, out float cellPixels);
        Vector2 axis = GetScreenAxis(cell, direction);
        if (steps <= 0)
        {
            return axis * (dragThresholdPixels * 0.35f);
        }

        float first = Mathf.Max(dragThresholdPixels * 0.5f, cellPixels * directionChangeCellFraction);
        float stride = Mathf.Max(8f, cellPixels * CellStrideFraction);
        float pixels = first + ((steps - 1) * stride) + 14f;
        return axis * pixels;
    }

    private void ProcessPointerFrame(Vector2 screenPosition, bool pressedThisFrame, bool releasedThisFrame)
    {
        latestScreenPosition = screenPosition;

        if (ShouldAbortActiveSession())
        {
            EndActiveDragAndClear();
            if (!pressedThisFrame)
            {
                if (releasedThisFrame)
                {
                    trackedTouchId = -1;
                }

                return;
            }
        }

        if (IsBoosterSelecting())
        {
            if (isPressing)
            {
                EndActiveDragAndClear();
            }

            if (pressedThisFrame)
            {
                if (!IsPointerOverBlockingUI(screenPosition, out _))
                {
                    TryHandleBoosterSelection(FindBlockAt(screenPosition));
                }

                trackedTouchId = -1;
            }
            else if (releasedThisFrame)
            {
                trackedTouchId = -1;
            }

            return;
        }

        if (IsBoosterExecuting())
        {
            if (isPressing)
            {
                EndActiveDragAndClear();
            }

            return;
        }

        if (pressedThisFrame)
        {
            OnPointerPressed(screenPosition);
        }

        if (!isPressing)
        {
            if (releasedThisFrame)
            {
                trackedTouchId = -1;
            }

            return;
        }

        if (!releasedThisFrame)
        {
            OnPointerDragged(screenPosition);
        }

        if (releasedThisFrame)
        {
            OnPointerReleased();
        }
    }

    private bool TryReadPointer(out Vector2 screenPosition, out bool pressedThisFrame, out bool releasedThisFrame)
    {
        screenPosition = default;
        pressedThisFrame = false;
        releasedThisFrame = false;

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null && ShouldUseTouchscreen(touchscreen))
        {
            return TryReadTouch(touchscreen, out screenPosition, out pressedThisFrame, out releasedThisFrame);
        }

        Pointer pointer = Mouse.current != null ? Mouse.current : Pointer.current;
        if (pointer == null)
        {
            return false;
        }

        screenPosition = pointer.position.ReadValue();
        pressedThisFrame = pointer.press.wasPressedThisFrame;
        releasedThisFrame = pointer.press.wasReleasedThisFrame;
        return pressedThisFrame || isPressing;
    }

    private bool ShouldUseTouchscreen(Touchscreen touchscreen)
    {
        if (trackedTouchId >= 0)
        {
            return true;
        }

        var touches = touchscreen.touches;
        for (int i = 0; i < touches.Count; i++)
        {
            if (touches[i].press.wasPressedThisFrame)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryReadTouch(Touchscreen touchscreen, out Vector2 screenPosition, out bool pressedThisFrame, out bool releasedThisFrame)
    {
        screenPosition = default;
        pressedThisFrame = false;
        releasedThisFrame = false;
        var touches = touchscreen.touches;

        if (trackedTouchId >= 0)
        {
            for (int i = 0; i < touches.Count; i++)
            {
                TouchControl touch = touches[i];
                if (touch.touchId.ReadValue() != trackedTouchId)
                {
                    continue;
                }

                screenPosition = touch.position.ReadValue();
                TouchPhase phase = touch.phase.ReadValue();
                releasedThisFrame = phase == TouchPhase.Ended || phase == TouchPhase.Canceled;
                return true;
            }

            releasedThisFrame = isPressing;
            return isPressing;
        }

        for (int i = 0; i < touches.Count; i++)
        {
            TouchControl touch = touches[i];
            if (!touch.press.wasPressedThisFrame)
            {
                continue;
            }

            trackedTouchId = touch.touchId.ReadValue();
            screenPosition = touch.position.ReadValue();
            pressedThisFrame = true;
            return true;
        }

        return false;
    }

    private void OnPointerPressed(Vector2 screenPosition)
    {
        LastPointerRejectReason = null;
        EndActiveDragAndClear();
        pressScreenPosition = screenPosition;
        latestScreenPosition = screenPosition;

        if (IsPointerOverBlockingUI(screenPosition, out string uiName))
        {
            LastPointerRejectReason = "ui:" + uiName;
            if (debugDrag)
            {
                LogDrag("Ignored press over UI " + uiName);
            }

            return;
        }

        pressedBlock = FindBlockAt(screenPosition);
        isPressing = pressedBlock != null;
        if (pressedBlock == null)
        {
            LastPointerRejectReason = "no-block";
            return;
        }

        BindFoundBlock();
    }

    /// <summary>
    /// Editor/test: start a drag session on a known block using the same
    /// post-pick path as a successful raycast. Does not replace T1 pick.
    /// </summary>
    public bool BindPressOnBlock(Block block, Vector2 screenPosition)
    {
        LastPointerRejectReason = null;
        EndActiveDragAndClear();
        pressScreenPosition = screenPosition;
        latestScreenPosition = screenPosition;
        pressedBlock = block;
        isPressing = block != null;
        if (pressedBlock == null)
        {
            LastPointerRejectReason = "no-block";
            return false;
        }

        BindFoundBlock();
        return isPressing && pressedBlock == block;
    }

    private void BindFoundBlock()
    {
        if (pressedBlock.IsSettled)
        {
            if (debugDrag)
            {
                LogDrag("Ignored settled block");
            }

            pressedBlock.PlayInvalidInteractionFeedback();
            ClearPressState();
            return;
        }

        if (levelManager != null && !levelManager.IsPieceInputAllowed)
        {
            ClearPressState();
            return;
        }

        pressedMover = pressedBlock.GetComponent<BlockMover>();
        if (pressedMover == null || pressedMover.IsMoving || pressedMover.IsDragging)
        {
            if (debugDrag)
            {
                LogDrag("Ignored press: no mover or already moving");
            }

            ClearPressState();
            return;
        }

        pressGridPosition = pressedBlock.GridPosition;
        CacheLevelIndex();
        if (!TryBeginFingerwiseSession())
        {
            ClearPressState();
            return;
        }

        pressedBlock.ShowDragSelection();
        if (debugDrag)
        {
            LogDrag("Finger drag began on " + pressedBlock.name + " @ " + pressGridPosition);
        }
    }

    /// <summary>
    /// Immediate pickup: begin BlockMover drag and store finger/board offset.
    /// </summary>
    private bool TryBeginFingerwiseSession()
    {
        BoardPresenter3D presenter = ResolvePresenter();
        Camera camera = ResolveCamera();
        Vector3 blockStartWorld = ResolveBlockBoardWorld(pressedBlock, presenter);
        Vector3 pressWorld = blockStartWorld;
        if (!FingerDragController.TryScreenToBoardWorld(camera, presenter, pressScreenPosition, out pressWorld))
        {
            pressWorld = blockStartWorld;
        }

        fingerDrag.Begin(pressWorld, blockStartWorld, pressGridPosition);

        if (!pressedMover.TryBeginFingerDrag())
        {
            fingerDrag.Clear();
            LastPointerRejectReason = "begin-rejected";
            return false;
        }

        fingerDragBegun = true;
        if (pressedMover.TryGetFixedMoveDirection(out Vector2Int fixedDirection))
        {
            directionLocked = true;
            lockedDirection = fixedDirection;
        }
        else
        {
            directionLocked = false;
            lockedDirection = Vector2Int.zero;
        }

        lastRequestedCell = pressGridPosition;
        hasRequestedCell = true;
        return true;
    }

    private void OnPointerDragged(Vector2 screenPosition)
    {
        if (pressedBlock == null || pressedMover == null || !fingerDragBegun)
        {
            return;
        }

        latestScreenPosition = screenPosition;
        BoardPresenter3D presenter = ResolvePresenter();
        Camera camera = ResolveCamera();
        if (presenter == null || presenter.GridSpace3D == null)
        {
            return;
        }

        if (!FingerDragController.TryScreenToBoardWorld(camera, presenter, screenPosition, out Vector3 fingerWorld))
        {
            return;
        }

        if (!fingerDrag.TryGetRequestedCell(
                fingerWorld,
                presenter.GridSpace3D,
                out Vector2Int requestedCell,
                out Vector3 desiredBlockWorld))
        {
            return;
        }

        if (debugDrag)
        {
            Vector3 delta = fingerWorld - fingerDrag.PressBoardWorld;
            LogDrag(
                $"Finger board={fingerWorld} delta={delta} desiredWorld={desiredBlockWorld} requested={requestedCell}");
        }

        if (!TryResolveFingerDirection(fingerWorld, requestedCell, presenter.transform))
        {
            return;
        }

        if (!hasRequestedCell || requestedCell != lastRequestedCell)
        {
            lastRequestedCell = requestedCell;
            hasRequestedCell = true;
            dragRequestCount++;
            pressedMover.SetDragRequest(requestedCell);
        }
        else
        {
            pressedMover.SetDragRequest(requestedCell);
        }

        // Continuous presentation follow (constrained). Logical hops stay in BlockMover.
        pressedMover.SetFingerDragWorld(desiredBlockWorld);
    }

    /// <summary>
    /// Locks / steers the existing BlockMover axis from continuous finger board motion.
    /// Fixed-direction pieces never leave their allowed axis.
    /// </summary>
    private bool TryResolveFingerDirection(
        Vector3 fingerBoardWorld,
        Vector2Int requestedCell,
        Transform boardRoot)
    {
        if (pressedMover.TryGetFixedMoveDirection(out Vector2Int fixedDirection))
        {
            if (!directionLocked)
            {
                directionLocked = true;
                lockedDirection = fixedDirection;
                pressedMover.SetDragDirection(fixedDirection);
            }

            Vector3 travel = fingerBoardWorld - fingerDrag.PressBoardWorld;
            Vector2Int fingerCardinal = FingerDragController.CardinalFromBoardDelta(travel, boardRoot);
            if (fingerCardinal != Vector2Int.zero
                && fingerCardinal != fixedDirection
                && !blockedCuePlayed
                && FingerDragController.BoardPlaneDistance(
                    fingerDrag.PressBoardWorld,
                    fingerBoardWorld,
                    boardRoot) >= ResolveCellPitch() * FingerDragController.FirstAxisCellFraction)
            {
                blockedCuePlayed = true;
                pressedMover.NotifyBlockedAttempt();
            }

            return true;
        }

        float cellPitch = ResolveCellPitch();
        float firstAxisDistance = cellPitch * FingerDragController.FirstAxisCellFraction;

        if (!directionLocked)
        {
            float travel = FingerDragController.BoardPlaneDistance(
                fingerDrag.PressBoardWorld,
                fingerBoardWorld,
                boardRoot);
            if (travel < firstAxisDistance)
            {
                return true;
            }

            Vector2Int candidate = FingerDragController.CardinalFromBoardDelta(
                fingerBoardWorld - fingerDrag.PressBoardWorld,
                boardRoot);
            if (candidate == Vector2Int.zero)
            {
                candidate = FingerDragController.CardinalFromGridDelta(requestedCell - pressGridPosition);
            }

            if (candidate == Vector2Int.zero)
            {
                return true;
            }

            if (!pressedMover.IsDirectionAllowed(candidate))
            {
                if (!blockedCuePlayed)
                {
                    blockedCuePlayed = true;
                    pressedMover.NotifyBlockedAttempt();
                }

                return true;
            }

            pressedMover.SetDragDirection(candidate);
            directionLocked = true;
            lockedDirection = candidate;
            blockedCuePlayed = false;
            if (debugDrag)
            {
                LogDrag($"Direction lock -> {candidate}");
            }

            return true;
        }

        Vector2Int logical = pressedMover.LogicalCell;
        Vector2Int toRequest = requestedCell - logical;
        Vector2Int steer = FingerDragController.CardinalFromGridDelta(toRequest);
        if (steer == Vector2Int.zero || steer == lockedDirection)
        {
            return true;
        }

        float steerDistance = FingerDragController.BoardPlaneDistance(
            fingerDrag.BlockStartBoardWorld,
            fingerBoardWorld - fingerDrag.DragWorldOffset,
            boardRoot);
        // Prefer board distance from current logical cell center for steers.
        BoardPresenter3D presenter = ResolvePresenter();
        if (presenter != null)
        {
            Vector3 logicalWorld = presenter.GridSpace3D.GridToWorld(logical);
            Vector3 desiredWorld = fingerBoardWorld - fingerDrag.DragWorldOffset;
            steerDistance = FingerDragController.BoardPlaneDistance(logicalWorld, desiredWorld, boardRoot);
        }

        if (steerDistance < cellPitch * FingerDragController.SteerCellFraction)
        {
            return true;
        }

        if (!pressedMover.IsDirectionAllowed(steer))
        {
            if (!blockedCuePlayed)
            {
                blockedCuePlayed = true;
                pressedMover.NotifyBlockedAttempt();
            }

            return true;
        }

        pressedMover.SetDragDirection(steer);
        lockedDirection = steer;
        blockedCuePlayed = false;
        if (debugDrag)
        {
            LogDrag($"Steer -> {steer} @ {logical}");
        }

        return true;
    }

    private float ResolveCellPitch()
    {
        BoardPresenter3D presenter = ResolvePresenter();
        if (presenter != null && presenter.GridSpace3D != null)
        {
            return Mathf.Max(0.01f, presenter.GridSpace3D.CellPitch);
        }

        return 1f;
    }

    private Vector3 ResolveBlockBoardWorld(Block block, BoardPresenter3D presenter)
    {
        if (block != null && block.WorldView != null)
        {
            return block.WorldView.PickWorldCenter;
        }

        if (presenter != null && presenter.GridSpace3D != null && block != null)
        {
            return presenter.GridSpace3D.GridToWorld(block.GridPosition);
        }

        return Vector3.zero;
    }

    private void OnPointerReleased()
    {
        if (fingerDragBegun && pressedMover != null && pressedMover.IsDragging)
        {
            pressedMover.EndDrag();
        }

        ClearPressState();
        simulatedPointerActive = false;
    }

    private bool TryGetCellScreenAxis(
        Vector2Int cell,
        Vector2Int direction,
        out Vector2 axis,
        out float cellPixels)
    {
        axis = new Vector2(direction.x, direction.y);
        cellPixels = dragThresholdPixels;
        Camera camera = ResolveCamera();
        BoardPresenter3D presenter = ResolvePresenter();
        if (camera == null || presenter == null || direction == Vector2Int.zero)
        {
            return false;
        }

        Vector3 world0 = presenter.GridSpace3D.GridToWorld(cell);
        Vector3 world1 = presenter.GridSpace3D.GridToWorld(cell + direction);
        Vector3 s0 = camera.WorldToScreenPoint(world0);
        Vector3 s1 = camera.WorldToScreenPoint(world1);
        Vector2 delta = new Vector2(s1.x - s0.x, s1.y - s0.y);
        float magnitude = delta.magnitude;
        if (magnitude < 1f)
        {
            return false;
        }

        axis = delta / magnitude;
        cellPixels = magnitude;
        return true;
    }

    private Block FindBlockAt(Vector2 screenPosition)
    {
        if (boardInput3D == null)
        {
            boardInput3D = FindFirstObjectByType<BoardInput3D>(FindObjectsInactive.Include);
        }

        if (boardInput3D != null)
        {
            return boardInput3D.TryFindBlock(screenPosition);
        }

        return null;
    }

    private bool IsPointerOverBlockingUI(Vector2 screenPosition, out string hitName)
    {
        hitName = null;
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        if (uiPointerData == null)
        {
            uiPointerData = new PointerEventData(eventSystem);
        }

        uiPointerData.Reset();
        uiPointerData.position = screenPosition;
        uiHits.Clear();
        eventSystem.RaycastAll(uiPointerData, uiHits);
        for (int i = 0; i < uiHits.Count; i++)
        {
            GameObject hit = uiHits[i].gameObject;
            if (hit == null)
            {
                continue;
            }

            Selectable selectable = hit.GetComponentInParent<Selectable>();
            if (selectable != null && selectable.IsInteractable())
            {
                hitName = selectable.name;
                return true;
            }
        }

        return false;
    }

    private BoosterManager ResolveBoosterManager()
    {
        if (boosterManager == null)
        {
            boosterManager = FindFirstObjectByType<BoosterManager>();
        }

        return boosterManager;
    }

    private MagnetBooster ResolveMagnetBooster()
    {
        if (magnetBooster == null)
        {
            magnetBooster = FindFirstObjectByType<MagnetBooster>();
        }

        return magnetBooster;
    }

    private Camera ResolveCamera()
    {
        if (boardCamera3D == null)
        {
            boardCamera3D = FindFirstObjectByType<BoardCamera3D>(FindObjectsInactive.Include);
        }

        return boardCamera3D != null ? boardCamera3D.Camera : Camera.main;
    }

    private BoardPresenter3D ResolvePresenter()
    {
        if (boardPresenter3D == null)
        {
            boardPresenter3D = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        }

        return boardPresenter3D;
    }

    private void ResolveReferences()
    {
        if (boosterManager == null)
        {
            boosterManager = FindFirstObjectByType<BoosterManager>();
        }

        if (magnetBooster == null)
        {
            magnetBooster = FindFirstObjectByType<MagnetBooster>();
        }

        if (boardInput3D == null)
        {
            boardInput3D = FindFirstObjectByType<BoardInput3D>(FindObjectsInactive.Include);
        }

        if (boardCamera3D == null)
        {
            boardCamera3D = FindFirstObjectByType<BoardCamera3D>(FindObjectsInactive.Include);
        }

        if (boardPresenter3D == null)
        {
            boardPresenter3D = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Include);
        }

        if (levelManager == null)
        {
            levelManager = FindFirstObjectByType<LevelManager>();
        }
    }

    private bool IsBoosterSelecting()
    {
        BoosterManager manager = ResolveBoosterManager();
        if (manager != null)
        {
            return manager.IsAnySelecting;
        }

        MagnetBooster magnet = ResolveMagnetBooster();
        return magnet != null && magnet.IsSelecting;
    }

    private bool IsBoosterExecuting()
    {
        BoosterManager manager = ResolveBoosterManager();
        if (manager != null)
        {
            return manager.IsAnyExecuting;
        }

        MagnetBooster magnet = ResolveMagnetBooster();
        return magnet != null && magnet.IsBusy && !magnet.IsSelecting;
    }

    private void TryHandleBoosterSelection(Block tapped)
    {
        BoosterManager manager = ResolveBoosterManager();
        if (manager != null)
        {
            manager.TryHandleSelectionPress(tapped);
            return;
        }

        MagnetBooster magnet = ResolveMagnetBooster();
        if (magnet != null)
        {
            magnet.TryHandleSelectionPress(tapped);
        }
    }

    private bool ShouldAbortActiveSession()
    {
        if (!isPressing)
        {
            return false;
        }

        if (pressedBlock == null || pressedMover == null)
        {
            return true;
        }

        if (levelManager != null)
        {
            if (!levelManager.IsGameplayInputAllowed || !levelManager.IsPieceInputAllowed)
            {
                return true;
            }

            if (cachedLevelIndex != int.MinValue && levelManager.CurrentLevelIndex != cachedLevelIndex)
            {
                return true;
            }
        }

        return false;
    }

    private void EndActiveDragAndClear()
    {
        if (fingerDragBegun && pressedMover != null && pressedMover.IsDragging)
        {
            pressedMover.EndDrag();
        }

        ClearPressState();
    }

    private void CacheLevelIndex()
    {
        cachedLevelIndex = levelManager != null ? levelManager.CurrentLevelIndex : int.MinValue;
    }

    private void ClearPressState()
    {
        if (pressedBlock != null)
        {
            pressedBlock.HideDragSelection();
        }

        isPressing = false;
        trackedTouchId = -1;
        blockedCuePlayed = false;
        pressedBlock = null;
        pressedMover = null;
        directionLocked = false;
        lockedDirection = Vector2Int.zero;
        fingerDragBegun = false;
        fingerDrag.Clear();
        pressGridPosition = Vector2Int.zero;
        lastRequestedCell = Vector2Int.zero;
        hasRequestedCell = false;
        dragRequestCount = 0;
        cachedLevelIndex = int.MinValue;
    }

    private void LogDrag(string message)
    {
        Debug.Log($"InputManager: {message}", this);
    }
}
