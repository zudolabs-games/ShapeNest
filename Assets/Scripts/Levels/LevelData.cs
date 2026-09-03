using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authoring data for one playable board.
/// Empty cell lists mean a single cell at (0,0) using Shape Type (backward compatible).
///
/// Examples (local cells relative to Grid Position):
/// 1x1 Circle: Shape Type = Circle, Cells empty.
/// Horizontal [C][C]: (0,0) Circle, (1,0) Circle.
/// Vertical [C]/[C]: (0,0) Circle, (0,1) Circle.
/// Multi-shape [C][T]: (0,0) Circle, (1,0) Triangle.
/// 2x2 mixed: (0,0) Circle, (1,0) Triangle, (0,1) Square, (1,1) Circle.
/// L shape: (0,0) Circle, (0,1) Circle, (1,0) Triangle.
/// Shape-in-shape: Composition = ShapeInShape, Outer Shape = outer nest/piece, Cells = inner layout.
/// </summary>
[CreateAssetMenu(fileName = "LevelData", menuName = "Shape Nest/Level Data")]
public class LevelData : ScriptableObject
{
    [Min(1)]
    [Tooltip("Playable grid width. 0 or missing on old assets is treated as 5.")]
    public int gridWidth = 5;

    [Min(1)]
    [Tooltip("Playable grid height. 0 or missing on old assets is treated as 5.")]
    public int gridHeight = 5;

    public List<LevelBlockData> blocks = new List<LevelBlockData>();
    public List<LevelTargetData> targets = new List<LevelTargetData>();
    public List<LevelShutterData> shutters = new List<LevelShutterData>();

    [Tooltip("Permanent impassable board cells (absolute grid coordinates).")]
    public List<Vector2Int> blockedCells = new List<Vector2Int>();

    public int ResolvedGridWidth => gridWidth < 1 ? 5 : gridWidth;
    public int ResolvedGridHeight => gridHeight < 1 ? 5 : gridHeight;

    public bool IsInsideGrid(Vector2Int position)
    {
        return position.x >= 0
            && position.y >= 0
            && position.x < ResolvedGridWidth
            && position.y < ResolvedGridHeight;
    }
}
