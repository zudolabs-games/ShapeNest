using UnityEngine;

/// <summary>
/// Thin fingerwise adapter: continuous screen pointer → board world → requested grid cell.
/// Does not move pieces or decide legality; <see cref="InputManager"/> feeds results into
/// existing <see cref="BlockMover"/> (<c>SetDragDirection</c> / <c>SetDragRequest</c> / <c>EndDrag</c>).
/// </summary>
public sealed class FingerDragController
{
    /// <summary>Board-local travel (as a fraction of cell pitch) before the first axis locks.</summary>
    public const float FirstAxisCellFraction = 0.08f;

    /// <summary>Board-local travel before a mid-drag steer to another allowed axis.</summary>
    public const float SteerCellFraction = 0.35f;

    private Vector3 pressBoardWorld;
    private Vector3 blockStartBoardWorld;
    private Vector3 dragWorldOffset;
    private Vector2Int blockStartCell;
    private bool hasSession;

    public bool HasSession => hasSession;
    public Vector2Int BlockStartCell => blockStartCell;
    public Vector3 PressBoardWorld => pressBoardWorld;
    public Vector3 BlockStartBoardWorld => blockStartBoardWorld;
    public Vector3 DragWorldOffset => dragWorldOffset;

    public void Begin(Vector3 pressBoardWorld, Vector3 blockStartBoardWorld, Vector2Int blockStartCell)
    {
        this.pressBoardWorld = pressBoardWorld;
        this.blockStartBoardWorld = blockStartBoardWorld;
        this.blockStartCell = blockStartCell;
        dragWorldOffset = pressBoardWorld - blockStartBoardWorld;
        hasSession = true;
    }

    public void Clear()
    {
        hasSession = false;
        pressBoardWorld = Vector3.zero;
        blockStartBoardWorld = Vector3.zero;
        dragWorldOffset = Vector3.zero;
        blockStartCell = Vector2Int.zero;
    }

    /// <summary>
    /// Finger board hit minus the press offset → desired block world → rounded grid cell.
    /// Preserves the touch point on the piece so the block does not jump under the finger.
    /// </summary>
    public bool TryGetRequestedCell(
        Vector3 fingerBoardWorld,
        GridSpace3D space,
        out Vector2Int requestedCell,
        out Vector3 desiredBlockWorld)
    {
        requestedCell = blockStartCell;
        desiredBlockWorld = blockStartBoardWorld;
        if (!hasSession || space == null)
        {
            return false;
        }

        desiredBlockWorld = fingerBoardWorld - dragWorldOffset;
        requestedCell = space.WorldToGrid(desiredBlockWorld);
        return true;
    }

    public static bool TryScreenToBoardWorld(
        Camera camera,
        BoardPresenter3D presenter,
        Vector2 screenPosition,
        out Vector3 boardWorld)
    {
        boardWorld = Vector3.zero;
        if (camera == null || presenter == null)
        {
            return false;
        }

        GridSpace3D space = presenter.GridSpace3D;
        if (space == null)
        {
            return false;
        }

        Ray ray = camera.ScreenPointToRay(screenPosition);
        Vector3 planePoint = space.GridToWorld(Vector2Int.zero);
        Plane boardPlane = new Plane(presenter.transform.up, planePoint);
        if (!boardPlane.Raycast(ray, out float enter) || enter < 0f)
        {
            return false;
        }

        boardWorld = ray.GetPoint(enter);
        return true;
    }

    /// <summary>
    /// Dominant cardinal on the board plane. Grid Y maps to board local Z.
    /// </summary>
    public static Vector2Int CardinalFromBoardDelta(Vector3 boardWorldDelta, Transform boardRoot)
    {
        Vector3 local = boardRoot != null
            ? boardRoot.InverseTransformDirection(boardWorldDelta)
            : boardWorldDelta;
        float absX = Mathf.Abs(local.x);
        float absZ = Mathf.Abs(local.z);
        if (absX >= absZ)
        {
            return local.x >= 0f ? Vector2Int.right : Vector2Int.left;
        }

        return local.z >= 0f ? Vector2Int.up : Vector2Int.down;
    }

    public static Vector2Int CardinalFromGridDelta(Vector2Int delta)
    {
        int absX = Mathf.Abs(delta.x);
        int absY = Mathf.Abs(delta.y);
        if (absX == 0 && absY == 0)
        {
            return Vector2Int.zero;
        }

        if (absX >= absY)
        {
            return delta.x > 0 ? Vector2Int.right : Vector2Int.left;
        }

        return delta.y > 0 ? Vector2Int.up : Vector2Int.down;
    }

    public static float BoardPlaneDistance(Vector3 a, Vector3 b, Transform boardRoot)
    {
        Vector3 delta = b - a;
        if (boardRoot != null)
        {
            Vector3 local = boardRoot.InverseTransformDirection(delta);
            local.y = 0f;
            return local.magnitude;
        }

        delta.y = 0f;
        return delta.magnitude;
    }
}
