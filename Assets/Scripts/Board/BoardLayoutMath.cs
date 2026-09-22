using UnityEngine;

/// <summary>
/// Shared layout math for runtime board sizing and the level editor preview grid.
/// </summary>
public static class BoardLayoutMath
{
    /// <summary>
    /// Largest square cell size that fits gridWidth x gridHeight inside the padded gameplay area.
    /// </summary>
    public static float ComputeSquareCellSize(
        int gridWidth,
        int gridHeight,
        float gameplayAreaWidth,
        float gameplayAreaHeight,
        float horizontalPadding,
        float verticalPadding)
    {
        gridWidth = Mathf.Max(1, gridWidth);
        gridHeight = Mathf.Max(1, gridHeight);

        float usableWidth = Mathf.Max(0f, gameplayAreaWidth - horizontalPadding * 2f);
        float usableHeight = Mathf.Max(0f, gameplayAreaHeight - verticalPadding * 2f);
        if (usableWidth <= 0f || usableHeight <= 0f)
        {
            return 1f;
        }

        return Mathf.Min(usableWidth / gridWidth, usableHeight / gridHeight);
    }

    /// <summary>
    /// Board RectTransform size that wraps the playable grid plus inner board padding.
    /// </summary>
    public static Vector2 ComputeBoardSize(
        int gridWidth,
        int gridHeight,
        float cellSize,
        float boardPadding)
    {
        gridWidth = Mathf.Max(1, gridWidth);
        gridHeight = Mathf.Max(1, gridHeight);
        boardPadding = Mathf.Max(0f, boardPadding);

        float width = cellSize * gridWidth + boardPadding * 2f;
        float height = cellSize * gridHeight + boardPadding * 2f;
        return new Vector2(width, height);
    }
}
