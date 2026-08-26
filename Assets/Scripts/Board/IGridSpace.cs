using UnityEngine;

/// <summary>
/// Coordinate-space contract between logical grid cells and presentation positions.
/// Implementations may use UI-local space, world space, etc. Logical cells stay Vector2Int.
/// </summary>
public interface IGridSpace
{
    /// <summary>Square cell size in this space's units.</summary>
    Vector2 CellSize { get; }

    /// <summary>Presentation position of the cell center for <paramref name="gridCoordinate"/>.</summary>
    Vector3 GridToLocal(Vector2Int gridCoordinate);

    /// <summary>Nearest grid cell for a presentation-space position.</summary>
    Vector2Int LocalToGrid(Vector3 localPosition);

    /// <summary>
    /// Alias for <see cref="GridToLocal"/>. Existing call sites use "World" historically
    /// even when the space is UI-local.
    /// </summary>
    Vector3 GridToWorld(Vector2Int gridCoordinate);

    /// <summary>Alias for <see cref="LocalToGrid"/>.</summary>
    Vector2Int WorldToGrid(Vector3 localPosition);
}
