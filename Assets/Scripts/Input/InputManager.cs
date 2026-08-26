using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// Grid-cell drag input. After the first cardinal threshold, Any-direction
/// blocks can steer without lifting. Fixed MoveDirection stays restricted.
/// Distance is cell-by-cell from the current drag segment.
/// </summary>
public class InputManager : MonoBehaviour
{
    [SerializeField]
    [Min(1f)]
    [Tooltip("Screen-pixel distance before the first drag direction is accepted.")]
    private float dragThresholdPixels = 30f;

    [SerializeField]
    [Range(0.15f, 0.5f)]
    [Tooltip("Finger travel, as a fraction of one cell, required to accept a mid-drag direction change.")]
    private float directionChangeCellFraction = 0.28f;

    [SerializeField]
    private bool debugDrag;

    [SerializeField]
    private LevelManager levelManager;

    [SerializeField]
    private MagnetBooster magnetBooster;

    [SerializeField]
    private BoardInput3D boardInput3D;

    private Block pressedBlock;
    private BlockMover pressedMover;
    private BoardManager cachedBoard;
    private RectTransform cachedBoardRect;
    private Camera cachedEventCamera;
    private Vector2 cachedPressLocal;
    private float cachedAxisSize;
    private Vector2 pressScreenPosition;
    private Vector2Int pressGridPosition;
    private bool isPressing;
    private bool directionLocked;
    private Vector2Int lockedDirection;
    private Vector2 segmentLocal;
    private Vector2Int segmentCell;
    private Vector2 steerAnchorLocal;
    private int trackedTouchId = -1;
    private bool blockedCuePlayed;

    private void Awake()
    {
        if (magnetBooster == null)
        {
            magnetBooster = FindFirstObjectByType<MagnetBooster>();
        }

        if (boardInput3D == null)
        {
            boardInput3D = FindFirstObjectByType<BoardInput3D>(FindObjectsInactive.Include);
        }
    }

    private void Update()
    {
        if (!TryReadPointer(out Vector2 screenPosition, out bool pressedThisFrame, out bool releasedThisFrame))
        {
            return;
        }

        if (levelManager != null && !levelManager.IsGameplayInputAllowed)
        {
            if (isPressing)
            {
                if (directionLocked && pressedMover != null)
                {
                    pressedMover.EndDrag();
                }

                ClearPress();
            }
            else
            {
                trackedTouchId = -1;
            }

            return;
        }

        // Magnet selection consumes the press; normal drag must not start.
        if (magnetBooster != null && magnetBooster.IsSelecting && pressedThisFrame)
        {
            Block tapped = FindBlockAt(screenPosition);
            magnetBooster.TryHandleSelectionPress(tapped);
            trackedTouchId = -1;
            return;
        }

        if (magnetBooster != null && magnetBooster.IsBusy && !magnetBooster.IsSelecting)
        {
            if (isPressing)
            {
                if (directionLocked && pressedMover != null)
                {
                    pressedMover.EndDrag();
                }

                ClearPress();
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
        pressedBlock = FindBlockAt(screenPosition);
        pressScreenPosition = screenPosition;
        isPressing = pressedBlock != null;
        directionLocked = false;
        lockedDirection = Vector2Int.zero;
        blockedCuePlayed = false;
        pressedMover = null;
        cachedBoard = null;
        cachedBoardRect = null;
        cachedEventCamera = null;
        cachedAxisSize = 0f;

        if (pressedBlock == null)
        {
            return;
        }

        if (pressedBlock.IsSettled)
        {
            if (debugDrag)
            {
                LogDrag("Ignored settled block");
            }

            ClearPress();
            return;
        }

        if (levelManager != null && !levelManager.IsPieceInputAllowed)
        {
            ClearPress();
            return;
        }

        pressedMover = pressedBlock.GetComponent<BlockMover>();
        if (pressedMover == null || pressedMover.IsMoving || pressedMover.IsDragging)
        {
            if (debugDrag)
            {
                LogDrag("Ignored press: no mover or already moving");
            }

            ClearPress();
            return;
        }

        pressGridPosition = pressedBlock.GridPosition;
        CacheBoardForPress();
        pressedBlock.ShowDragSelection();
        if (debugDrag)
        {
            LogDrag("Selected block");
        }
    }

    private void OnPointerDragged(Vector2 screenPosition)
    {
        if (pressedBlock == null || pressedMover == null)
        {
            return;
        }

        if (!TryGetBoardLocal(screenPosition, out Vector2 currentLocal))
        {
            return;
        }

        if (!directionLocked)
        {
            Vector2 delta = screenPosition - pressScreenPosition;
            if (delta.sqrMagnitude < dragThresholdPixels * dragThresholdPixels)
            {
                return;
            }

            Vector2Int direction = GetCardinalDirection(delta);
            if (!pressedMover.IsDirectionAllowed(direction))
            {
                if (!blockedCuePlayed)
                {
                    blockedCuePlayed = true;
                    pressedMover.NotifyBlockedAttempt();
                }

                return;
            }

            if (!pressedMover.TryBeginDrag(direction))
            {
                ClearPress();
                return;
            }

            directionLocked = true;
            lockedDirection = direction;
            CacheAxisSize();
            BeginDragSegment(cachedPressLocal, pressGridPosition);
        }
        else
        {
            TryChangeDirection(currentLocal);
        }

        pressedMover.SetDragRequest(ComputeRequestedCell(currentLocal));
    }

    private void OnPointerReleased()
    {
        if (directionLocked && pressedMover != null)
        {
            pressedMover.EndDrag();
        }

        ClearPress();
    }

    private Vector2Int ComputeRequestedCell(Vector2 currentLocal)
    {
        if (cachedAxisSize <= 0.01f)
        {
            return segmentCell;
        }

        Vector2 localDelta = currentLocal - segmentLocal;
        float along = (localDelta.x * lockedDirection.x) + (localDelta.y * lockedDirection.y);
        int steps = Mathf.RoundToInt(along / cachedAxisSize);
        if (steps < 0)
        {
            steps = 0;
        }

        return segmentCell + (lockedDirection * steps);
    }

    private void TryChangeDirection(Vector2 currentLocal)
    {
        if (pressedMover == null || lockedDirection == Vector2Int.zero)
        {
            return;
        }

        Vector2 fromAnchor = currentLocal - steerAnchorLocal;
        float along = (fromAnchor.x * lockedDirection.x) + (fromAnchor.y * lockedDirection.y);
        if (along > 0f)
        {
            steerAnchorLocal += new Vector2(lockedDirection.x, lockedDirection.y) * along;
            fromAnchor = currentLocal - steerAnchorLocal;
        }

        float changeThreshold = GetDirectionChangeThreshold();
        if (fromAnchor.sqrMagnitude < changeThreshold * changeThreshold)
        {
            return;
        }

        Vector2Int candidate = GetCardinalDirection(fromAnchor);
        if (candidate == lockedDirection || !pressedMover.IsDirectionAllowed(candidate))
        {
            return;
        }

        float absX = Mathf.Abs(fromAnchor.x);
        float absY = Mathf.Abs(fromAnchor.y);
        float dominant = Mathf.Max(absX, absY);
        float secondary = Mathf.Min(absX, absY);
        if (dominant < secondary * 1.2f)
        {
            return;
        }

        lockedDirection = candidate;
        CacheAxisSize();
        BeginDragSegment(currentLocal, pressedMover.LogicalCell);
        pressedMover.SetDragDirection(candidate);
        if (debugDrag)
        {
            LogDrag($"Direction -> {candidate}");
        }
    }

    private void BeginDragSegment(Vector2 localOrigin, Vector2Int cellOrigin)
    {
        segmentLocal = localOrigin;
        segmentCell = cellOrigin;
        steerAnchorLocal = localOrigin;
    }

    private float GetDirectionChangeThreshold()
    {
        if (cachedBoard == null)
        {
            return 24f;
        }

        Vector2 cell = cachedBoard.VisualCellSize;
        float axis = lockedDirection.x != 0 ? cell.x : cell.y;
        return Mathf.Max(8f, axis * directionChangeCellFraction);
    }

    private bool TryGetBoardLocal(Vector2 screenPosition, out Vector2 local)
    {
        local = Vector2.zero;
        if (cachedBoardRect == null)
        {
            return false;
        }

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            cachedBoardRect,
            screenPosition,
            cachedEventCamera,
            out local);
    }

    private void CacheBoardForPress()
    {
        cachedBoard = pressedBlock.Board;
        if (cachedBoard == null)
        {
            return;
        }

        cachedBoardRect = (RectTransform)cachedBoard.transform;
        Canvas canvas = cachedBoard.GetComponentInParent<Canvas>();
        cachedEventCamera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            cachedEventCamera = canvas.worldCamera;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            cachedBoardRect,
            pressScreenPosition,
            cachedEventCamera,
            out cachedPressLocal);
    }

    private void CacheAxisSize()
    {
        if (cachedBoard == null)
        {
            cachedAxisSize = 0f;
            return;
        }

        Vector2 cellSize = cachedBoard.VisualCellSize;
        cachedAxisSize = lockedDirection.x != 0 ? cellSize.x : cellSize.y;
    }

    private static Vector2Int GetCardinalDirection(Vector2 delta)
    {
        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
        {
            return delta.x > 0f ? Vector2Int.right : Vector2Int.left;
        }

        return delta.y > 0f ? Vector2Int.up : Vector2Int.down;
    }

    private Block FindBlockAt(Vector2 screenPosition)
    {
        // Phase 11: board presentation is World3D only — resolve via physics pick.
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

    private void ClearPress()
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
        cachedBoard = null;
        cachedBoardRect = null;
        cachedEventCamera = null;
        cachedAxisSize = 0f;
        directionLocked = false;
        lockedDirection = Vector2Int.zero;
        segmentCell = Vector2Int.zero;
        segmentLocal = Vector2.zero;
        steerAnchorLocal = Vector2.zero;
    }

    private void LogDrag(string message)
    {
        Debug.Log($"InputManager: {message}", this);
    }
}
