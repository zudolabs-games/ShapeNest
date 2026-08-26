using UnityEngine;

/// <summary>
/// UI/canvas implementation of <see cref="IGridSpace"/>.
/// Positions are Board <see cref="RectTransform"/> local units (same math as the pre-abstraction BoardManager).
/// </summary>
public sealed class UIGridSpace : IGridSpace
{
    private readonly BoardManager board;

    public UIGridSpace(BoardManager board)
    {
        this.board = board;
    }

    public Vector2 CellSize => VisualCellSize;

    public Vector3 GridToLocal(Vector2Int gridCoordinate)
    {
        Rect rect = CellGridRect;
        Vector2 cell = VisualCellSize;
        float x = rect.xMin + (gridCoordinate.x + 0.5f) * cell.x;
        float y = rect.yMin + (gridCoordinate.y + 0.5f) * cell.y;
        return new Vector3(x, y, 0f);
    }

    public Vector2Int LocalToGrid(Vector3 localPosition)
    {
        Rect rect = CellGridRect;
        Vector2 cell = VisualCellSize;

        if (cell.x <= 0f || cell.y <= 0f)
        {
            return Vector2Int.zero;
        }

        float x = (localPosition.x - rect.xMin) / cell.x - 0.5f;
        float y = (localPosition.y - rect.yMin) / cell.y - 0.5f;
        return new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
    }

    public Vector3 GridToWorld(Vector2Int gridCoordinate)
    {
        return GridToLocal(gridCoordinate);
    }

    public Vector2Int WorldToGrid(Vector3 localPosition)
    {
        return LocalToGrid(localPosition);
    }

    /// <summary>
    /// Playable square grid rect in Board local UI units (for UI grid-line layout).
    /// </summary>
    public Rect CellGridRect
    {
        get
        {
            Rect playable = PlayableRect;
            Vector2 cell = VisualCellSize;
            float gridWidthPx = cell.x * board.Width;
            float gridHeightPx = cell.y * board.Height;
            float originX = playable.xMin + (playable.width - gridWidthPx) * 0.5f;
            float originY = playable.yMin + (playable.height - gridHeightPx) * 0.5f;
            return new Rect(originX, originY, gridWidthPx, gridHeightPx);
        }
    }

    /// <summary>
    /// Square cell size in Board local UI units, derived from the Board RectTransform.
    /// Matches previous BoardManager.VisualCellSize behavior.
    /// </summary>
    private Vector2 VisualCellSize
    {
        get
        {
            Rect rect = PlayableRect;
            int width = board.Width;
            int height = board.Height;
            if (width <= 0 || height <= 0)
            {
                return Vector2.one;
            }

            float cell = Mathf.Min(rect.width / width, rect.height / height);
            cell = Mathf.Max(0.01f, cell);
            return new Vector2(cell, cell);
        }
    }

    private Rect PlayableRect
    {
        get
        {
            RectTransform boardRect = board.BoardRectTransform;
            Rect rect = boardRect.rect;
            float pad = Mathf.Min(board.GridPadding, rect.width * 0.12f, rect.height * 0.12f);
            pad = Mathf.Max(0f, pad);
            if (rect.width <= pad * 2f || rect.height <= pad * 2f)
            {
                return rect;
            }

            return new Rect(rect.xMin + pad, rect.yMin + pad, rect.width - pad * 2f, rect.height - pad * 2f);
        }
    }
}
