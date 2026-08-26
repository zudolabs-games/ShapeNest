using UnityEngine;

/// <summary>
/// World-space implementation of <see cref="IGridSpace"/>.
/// Logical cells remain <see cref="Vector2Int"/> with (0,0) at the bottom-left.
///
/// Coordinate convention (board-local, then transformed by the board root):
/// <list type="bullet">
/// <item><description>X = horizontal grid axis (matches logical x)</description></item>
/// <item><description>Y = board height / up</description></item>
/// <item><description>Z = depth grid axis across the board (matches logical y)</description></item>
/// </list>
/// </summary>
public sealed class GridSpace3D : IGridSpace
{
    private Transform boardRoot;
    private int width = 1;
    private int height = 1;
    private float cellSize = 1f;
    private float cellGap;
    private float surfaceLocalY;

    public int Width => width;
    public int Height => height;
    public float CellPitch => cellSize + cellGap;
    public float CellGap => cellGap;
    public float SurfaceLocalY => surfaceLocalY;

    public Vector2 CellSize => new Vector2(cellSize, cellSize);

    public void Bind(Transform root)
    {
        boardRoot = root;
    }

    public void Configure(int gridWidth, int gridHeight, float worldCellSize, float gap, float surfaceY)
    {
        width = Mathf.Max(1, gridWidth);
        height = Mathf.Max(1, gridHeight);
        cellSize = Mathf.Max(0.01f, worldCellSize);
        cellGap = Mathf.Max(0f, gap);
        surfaceLocalY = surfaceY;
    }

    public Vector3 GridToLocal(Vector2Int gridCoordinate)
    {
        float pitch = CellPitch;
        float originX = -((width - 1) * pitch) * 0.5f;
        float originZ = -((height - 1) * pitch) * 0.5f;
        float x = originX + gridCoordinate.x * pitch;
        float z = originZ + gridCoordinate.y * pitch;
        return new Vector3(x, surfaceLocalY, z);
    }

    public Vector2Int LocalToGrid(Vector3 localPosition)
    {
        float pitch = CellPitch;
        if (pitch <= 0.0001f)
        {
            return Vector2Int.zero;
        }

        float originX = -((width - 1) * pitch) * 0.5f;
        float originZ = -((height - 1) * pitch) * 0.5f;
        float x = (localPosition.x - originX) / pitch;
        float z = (localPosition.z - originZ) / pitch;
        return new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(z));
    }

    public Vector3 GridToWorld(Vector2Int gridCoordinate)
    {
        Vector3 local = GridToLocal(gridCoordinate);
        return boardRoot != null ? boardRoot.TransformPoint(local) : local;
    }

    public Vector2Int WorldToGrid(Vector3 worldPosition)
    {
        Vector3 local = boardRoot != null ? boardRoot.InverseTransformPoint(worldPosition) : worldPosition;
        return LocalToGrid(local);
    }

    /// <summary>Full playable footprint in board-local XZ (including outer half-gaps).</summary>
    public Vector2 GridFootprint
    {
        get
        {
            float pitch = CellPitch;
            float sizeX = width * pitch - cellGap;
            float sizeZ = height * pitch - cellGap;
            return new Vector2(Mathf.Max(cellSize, sizeX), Mathf.Max(cellSize, sizeZ));
        }
    }
}
