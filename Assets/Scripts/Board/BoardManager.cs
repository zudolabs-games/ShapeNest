using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Grid layout and occupancy for ShapeNest.
/// Lives on the Board RectTransform. Cell (0, 0) is the bottom-left cell.
/// Presentation coordinates are provided by <see cref="IGridSpace"/> (currently <see cref="UIGridSpace"/>).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class BoardManager : MonoBehaviour
{
    private const string RuntimeGridName = "RuntimeGrid";
    private const float LineThickness = 2f;

    [SerializeField]
    [Min(1)]
    private int width = 5;

    [SerializeField]
    [Min(1)]
    private int height = 5;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Legacy field. Visual cell size is driven by the Board RectTransform.")]
    private float cellSize = 1f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Inset inside the Board rect before cells are laid out. Does not change logical grid coordinates.")]
    private float gridPadding = 14f;

    [SerializeField]
    private bool showGrid = true;

    [SerializeField]
    private bool debugOccupancy;

    private UIGridSpace uiGridSpace;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;
    public float GridPadding => gridPadding;

    /// <summary>Active presentation grid-space (UI implementation in Phase 1).</summary>
    public IGridSpace GridSpace => EnsureUIGridSpace();

    /// <summary>Board RectTransform used by <see cref="UIGridSpace"/>.</summary>
    public RectTransform BoardRectTransform => BoardRect;

    /// <summary>
    /// Sets the playable grid from LevelData. Rebuilds the runtime grid lines.
    /// Does not change occupancy dictionaries; callers must clear/rebuild pieces.
    /// </summary>
    public void ApplyGridSize(int gridWidth, int gridHeight)
    {
        width = Mathf.Max(1, gridWidth);
        height = Mathf.Max(1, gridHeight);

        BoardLayout layout = GetComponent<BoardLayout>();
        if (layout != null)
        {
            layout.ApplyLayout(width, height);
            return;
        }

        if (isActiveAndEnabled)
        {
            RefreshRuntimeGrid();
        }
    }

    /// <summary>
    /// Rebuilds runtime grid lines after BoardLayout resizes the board rect.
    /// </summary>
    public void RefreshRuntimeGridAfterLayout()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        builtWidth = 0;
        builtHeight = 0;
        RefreshRuntimeGrid();
    }

    private RectTransform boardRectTransform;
    private RectTransform runtimeGridRoot;
    private Sprite lineSprite;
    private int builtWidth;
    private int builtHeight;
    private readonly Dictionary<Vector2Int, Block> occupancy = new Dictionary<Vector2Int, Block>();
    private readonly Dictionary<Vector2Int, Target> targets = new Dictionary<Vector2Int, Target>();
    private readonly List<ShutterState> closedShutters = new List<ShutterState>();
    private readonly HashSet<Vector2Int> staticBlockedCells = new HashSet<Vector2Int>();

    private RectTransform BoardRect
    {
        get
        {
            if (boardRectTransform == null)
            {
                boardRectTransform = (RectTransform)transform;
            }

            return boardRectTransform;
        }
    }

    private UIGridSpace EnsureUIGridSpace()
    {
        return uiGridSpace ??= new UIGridSpace(this);
    }

    /// <summary>
    /// Square cell size in Board local UI units, derived from the RectTransform.
    /// </summary>
    public Vector2 VisualCellSize => EnsureUIGridSpace().CellSize;

    /// <summary>
    /// Local / anchored position of the cell center, relative to the Board RectTransform.
    /// Uses Rect.xMin / yMin so (0, 0) stays the bottom-left cell regardless of pivot.
    /// </summary>
    public Vector3 GridToLocal(Vector2Int gridCoordinate)
    {
        return EnsureUIGridSpace().GridToLocal(gridCoordinate);
    }

    /// <summary>
    /// UI local position of the cell center (same as GridToLocal).
    /// Kept so existing call sites keep a familiar name.
    /// </summary>
    public Vector3 GridToWorld(Vector2Int gridCoordinate)
    {
        return EnsureUIGridSpace().GridToWorld(gridCoordinate);
    }

    /// <summary>
    /// Converts a Board-local UI position to the nearest cell.
    /// Input is RectTransform local space, not world or screen space.
    /// </summary>
    public Vector2Int LocalToGrid(Vector3 localPosition)
    {
        return EnsureUIGridSpace().LocalToGrid(localPosition);
    }

    /// <summary>
    /// Same as LocalToGrid. The argument is Board-local UI coordinates, not world space.
    /// </summary>
    public Vector2Int WorldToGrid(Vector3 localPosition)
    {
        return EnsureUIGridSpace().WorldToGrid(localPosition);
    }

    /// <summary>
    /// Applies the presentation position for a piece RectTransform at a grid cell.
    /// Coordinate conversion lives in <see cref="IGridSpace"/>, not on Block/Target.
    /// </summary>
    public void ApplyPieceAnchoredPosition(RectTransform piece, Vector2Int gridPosition)
    {
        if (piece == null)
        {
            return;
        }

        piece.anchoredPosition = GridToLocal(gridPosition);
    }

    public bool IsInsideBoard(Vector2Int gridCoordinate)
    {
        return gridCoordinate.x >= 0
            && gridCoordinate.x < width
            && gridCoordinate.y >= 0
            && gridCoordinate.y < height;
    }

    public bool IsCellOccupied(Vector2Int gridPosition)
    {
        return occupancy.TryGetValue(gridPosition, out Block occupant) && occupant != null;
    }

    public bool IsCellBlockedByClosedShutter(Vector2Int gridPosition)
    {
        for (int i = closedShutters.Count - 1; i >= 0; i--)
        {
            ShutterState shutter = closedShutters[i];
            if (shutter == null || !shutter.IsClosed)
            {
                if (shutter == null)
                {
                    closedShutters.RemoveAt(i);
                }
                continue;
            }

            if (shutter.CoversCell(gridPosition))
            {
                return true;
            }
        }

        return false;
    }

    public IReadOnlyCollection<Vector2Int> StaticBlockedCells => staticBlockedCells;

    public void SetStaticBlockedCells(IEnumerable<Vector2Int> cells)
    {
        staticBlockedCells.Clear();
        if (cells == null)
        {
            return;
        }

        foreach (Vector2Int cell in cells)
        {
            staticBlockedCells.Add(cell);
        }
    }

    public void ClearStaticBlockedCells()
    {
        staticBlockedCells.Clear();
    }

    public bool IsCellBlockedByStaticObstacle(Vector2Int gridPosition)
    {
        return staticBlockedCells.Contains(gridPosition);
    }

    public bool IsCellImpassable(Vector2Int gridPosition)
    {
        return IsCellBlockedByClosedShutter(gridPosition)
            || IsCellBlockedByStaticObstacle(gridPosition);
    }

    public bool DoesFootprintTouchImpassableCell(Block block, Vector2Int toAnchor)
    {
        if (block == null)
        {
            return false;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            if (IsCellImpassable(toAnchor + block.GetLocalCell(i)))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsBlockUnderImpassableCell(Block block)
    {
        return DoesFootprintTouchImpassableCell(block, block != null ? block.GridPosition : Vector2Int.zero);
    }

    public bool IsBlockUnderClosedShutter(Block block)
    {
        return DoesFootprintTouchClosedShutter(block, block != null ? block.GridPosition : Vector2Int.zero);
    }

    public bool DoesFootprintTouchClosedShutter(Block block, Vector2Int toAnchor)
    {
        if (block == null)
        {
            return false;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            if (IsCellBlockedByClosedShutter(toAnchor + block.GetLocalCell(i)))
            {
                return true;
            }
        }

        return false;
    }

    public void RegisterShutter(ShutterState shutter)
    {
        if (shutter != null && !closedShutters.Contains(shutter) && shutter.IsClosed)
        {
            closedShutters.Add(shutter);
        }
    }

    public void UnregisterShutter(ShutterState shutter)
    {
        if (shutter != null)
        {
            closedShutters.Remove(shutter);
        }
    }

    public Block GetBlockAt(Vector2Int gridPosition)
    {
        occupancy.TryGetValue(gridPosition, out Block occupant);
        return occupant;
    }

    public void CollectUniqueBlocks(List<Block> destination)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        foreach (Block occupant in occupancy.Values)
        {
            if (occupant == null || destination.Contains(occupant))
            {
                continue;
            }

            destination.Add(occupant);
        }
    }

    public bool TryRegisterBlock(Block block, Vector2Int gridPosition)
    {
        if (block == null)
        {
            return false;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = gridPosition + block.GetLocalCell(i);
            if (!IsInsideBoard(cell))
            {
                Debug.LogWarning(
                    $"[Board Occupancy] Register REJECTED outside board: block={block.GetInstanceID()} " +
                    $"anchor={gridPosition} cell={cell} board={width}x{height}",
                    this);
                return false;
            }

            Block occupant = GetBlockAt(cell);
            if (occupant != null && occupant != block)
            {
                Debug.LogWarning(
                    $"[Board Occupancy] Register REJECTED conflict: block={block.GetInstanceID()} " +
                    $"cell={cell} occupiedBy={occupant.GetInstanceID()}",
                    this);
                return false;
            }
        }

        UnregisterBlock(block);
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = gridPosition + block.GetLocalCell(i);
            occupancy[cell] = block;
            LogOccupancy($"Registered {block.name} at {cell}");
        }

        return true;
    }

    public void UnregisterBlock(Block block)
    {
        if (block == null)
        {
            return;
        }

        List<Vector2Int> keysToRemove = null;
        foreach (KeyValuePair<Vector2Int, Block> entry in occupancy)
        {
            if (entry.Value != block)
            {
                continue;
            }

            keysToRemove ??= new List<Vector2Int>();
            keysToRemove.Add(entry.Key);
        }

        if (keysToRemove == null)
        {
            return;
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            occupancy.Remove(keysToRemove[i]);
            LogOccupancy($"Unregistered {block.name} from {keysToRemove[i]}");
        }
    }

    /// <summary>
    /// Re-registers every live Block under this board into occupancy.
    /// Used when a survivor is visible but missing from CollectUniqueBlocks.
    /// </summary>
    public int RebindChildBlockOccupancy()
    {
        Block[] blocks = GetComponentsInChildren<Block>(true);
        int rebound = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled || !block.isActiveAndEnabled)
            {
                continue;
            }

            bool missing = false;
            int count = Mathf.Max(1, block.CellCount);
            for (int c = 0; c < count; c++)
            {
                if (GetBlockAt(block.GridPosition + block.GetLocalCell(c)) != block)
                {
                    missing = true;
                    break;
                }
            }

            if (!missing)
            {
                continue;
            }

            if (TryRegisterBlock(block, block.GridPosition))
            {
                rebound++;
                Debug.Log(
                    $"[Board Occupancy] Rebound orphan Block={block.GetInstanceID()} " +
                    $"at {block.GridPosition} shape={block.GetActiveShape(0)}",
                    block);
            }
        }

        return rebound;
    }

    public bool TryMoveBlock(Block block, Vector2Int from, Vector2Int to)
    {
        if (block == null)
        {
            return false;
        }

        if (!CanTranslateBlock(block, to))
        {
            LogOccupancy($"Move rejected: {block.name} {from} -> {to} blocked");
            return false;
        }

        UnregisterBlock(block);
        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            occupancy[to + block.GetLocalCell(i)] = block;
        }

        LogOccupancy($"Moved {block.name} {from} -> {to}");
        return true;
    }

    public bool CanTranslateBlock(Block block, Vector2Int toAnchor)
    {
        if (block == null)
        {
            return false;
        }

        if (DoesFootprintTouchImpassableCell(block, toAnchor))
        {
            return false;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = toAnchor + block.GetLocalCell(i);
            if (!IsInsideBoard(cell))
            {
                return false;
            }

            Block occupant = GetBlockAt(cell);
            if (occupant != null && occupant != block)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Phase 70B: validate translating only the selected matching cells by
    /// <paramref name="translation"/>. Unmatched sibling cells are not required to move
    /// and are treated as stationary occupancy that destinations must not collide with.
    /// </summary>
    public bool CanTranslateMatchingSubset(
        Block block,
        IReadOnlyList<int> cellIndices,
        Vector2Int translation)
    {
        if (block == null || cellIndices == null || cellIndices.Count == 0)
        {
            return false;
        }

        var selected = new HashSet<int>();
        var destinations = new HashSet<Vector2Int>();
        for (int n = 0; n < cellIndices.Count; n++)
        {
            int cellIndex = cellIndices[n];
            if (cellIndex < 0 || cellIndex >= block.CellCount)
            {
                return false;
            }

            if (!selected.Add(cellIndex))
            {
                continue;
            }

            Vector2Int source = block.GridPosition + block.GetLocalCell(cellIndex);
            Vector2Int dest = source + translation;
            if (!IsInsideBoard(dest) || IsCellImpassable(dest))
            {
                return false;
            }

            if (!destinations.Add(dest))
            {
                // Two selected cells map onto the same destination.
                return false;
            }
        }

        foreach (Vector2Int dest in destinations)
        {
            Block occupant = GetBlockAt(dest);
            if (occupant == null)
            {
                continue;
            }

            if (occupant != block)
            {
                return false;
            }

            // Occupied by this block: allowed only if that occupant cell is also selected
            // (and therefore leaving), or translation is zero (already seated).
            if (translation == Vector2Int.zero)
            {
                continue;
            }

            int occupantIndex = -1;
            for (int i = 0; i < block.CellCount; i++)
            {
                if (block.GridPosition + block.GetLocalCell(i) == dest)
                {
                    occupantIndex = i;
                    break;
                }
            }

            if (occupantIndex < 0 || !selected.Contains(occupantIndex))
            {
                return false;
            }
        }

        return true;
    }

    public bool FootprintTouchesTarget(Block block, Vector2Int toAnchor)
    {
        if (block == null)
        {
            return false;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            if (GetTargetAt(toAnchor + block.GetLocalCell(i)) != null)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryRegisterTarget(Target target)
    {
        if (target == null)
        {
            return false;
        }

        int count = Mathf.Max(1, target.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = target.GridPosition + target.GetLocalCell(i);
            if (!IsInsideBoard(cell))
            {
                return false;
            }

            Target existing = GetTargetAt(cell);
            if (existing != null && existing != target)
            {
                return false;
            }
        }

        UnregisterTarget(target);
        for (int i = 0; i < count; i++)
        {
            targets[target.GridPosition + target.GetLocalCell(i)] = target;
        }

        // UnregisterTarget cleared the flag — restore after successful occupancy write.
        target.NotifyBoardRegistered();
        return true;
    }

    /// <summary>
    /// Clears block occupancy and target registration after a finished match.
    /// The cell is then empty for movement. Does not destroy objects.
    /// </summary>
    public void ReleaseMatchedCell(Block block, Target target)
    {
        UnregisterBlock(block);
        UnregisterTarget(target);
    }

    public void UnregisterTarget(Target target)
    {
        if (target == null)
        {
            return;
        }

        List<Vector2Int> keysToRemove = null;
        foreach (KeyValuePair<Vector2Int, Target> entry in targets)
        {
            if (entry.Value != target)
            {
                continue;
            }

            keysToRemove ??= new List<Vector2Int>();
            keysToRemove.Add(entry.Key);
        }

        if (keysToRemove != null)
        {
            for (int i = 0; i < keysToRemove.Count; i++)
            {
                targets.Remove(keysToRemove[i]);
            }
        }

        target.NotifyBoardUnregistered();
        Phase72CNestLifecycle.LogTargetState(target, "UnregisterTarget");
    }

    /// <summary>
    /// Collects registered nests whose remaining layer stacks match the block's cells.
    /// Each target is claimed at most once. Does not consume, match, or notify.
    /// </summary>
    public void CollectCorrespondingTargets(Block block, List<Target> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        if (block == null)
        {
            return;
        }

        int cellCount = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < cellCount; i++)
        {
            Target match = FindUnclaimedCorrespondingTarget(block, i, results);
            if (match != null)
            {
                results.Add(match);
            }
        }
    }

    /// <summary>
    /// Unregisters a nest and finishes its removal presentation without a successful match.
    /// Does not notify LevelManager, Ice, or Shutters.
    /// </summary>
    public void RemoveTargetWithoutMatch(Target target)
    {
        if (target == null)
        {
            return;
        }

        UnregisterTarget(target);
        target.BeginMatchPresentation();
        target.CompleteMatchPresentation();
    }

    private Target FindUnclaimedCorrespondingTarget(Block block, int cellIndex, List<Target> claimed)
    {
        foreach (KeyValuePair<Vector2Int, Target> entry in targets)
        {
            Target candidate = entry.Value;
            if (candidate == null || candidate.IsMatched)
            {
                continue;
            }

            if (claimed != null && claimed.Contains(candidate))
            {
                continue;
            }

            if (RemainingStacksMatch(block, cellIndex, candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool RemainingStacksMatch(Block block, int cellIndex, Target target)
    {
        if (block == null || target == null)
        {
            return false;
        }

        ShapeCellData blockCell = block.GetCell(cellIndex);
        MatchIdentity blockActive = block.GetActiveIdentity(cellIndex);
        ShapeType blockOuter = blockCell != null ? blockCell.shapeType : blockActive.Shape;
        ShapeColor blockOuterColor = blockCell != null
            ? ShapeLayout.EffectiveOuterColor(blockCell)
            : blockActive.Color;
        int blockLayers = ShapeLayout.LayerCount(blockCell);
        IReadOnlyList<ShapeType> blockInners = blockCell != null ? blockCell.innerShapes : null;
        IReadOnlyList<ShapeColor> blockInnerColors = blockCell != null ? blockCell.innerShapeColors : null;

        ShapeCellData targetCell = null;
        IReadOnlyList<ShapeCellData> targetCells = target.Cells;
        if (targetCells != null && targetCells.Count > 0)
        {
            targetCell = targetCells[0];
        }

        MatchIdentity targetActive = new MatchIdentity(
            target.RequiredShape,
            target.GetOuterColorAtIndex(0));
        ShapeType targetOuter = targetCell != null ? targetCell.shapeType : targetActive.Shape;
        ShapeColor targetOuterColor = targetCell != null
            ? ShapeLayout.EffectiveOuterColor(targetCell)
            : targetActive.Color;
        int targetLayers = ShapeLayout.LayerCount(targetCell);
        IReadOnlyList<ShapeType> targetInners = targetCell != null ? targetCell.innerShapes : null;
        IReadOnlyList<ShapeColor> targetInnerColors = targetCell != null ? targetCell.innerShapeColors : null;

        if (!ShapeMatch.AreMatchingLayers(blockActive, targetActive)
            || blockOuter != targetOuter
            || blockOuterColor != targetOuterColor
            || blockLayers != targetLayers)
        {
            return false;
        }

        return SameShapeList(blockInners, targetInners)
            && SameColorList(blockInnerColors, targetInnerColors);
    }

    private static bool SameColorList(IReadOnlyList<ShapeColor> a, IReadOnlyList<ShapeColor> b)
    {
        int ac = a != null ? a.Count : 0;
        int bc = b != null ? b.Count : 0;
        if (ac != bc)
        {
            return false;
        }

        for (int i = 0; i < ac; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameShapeList(IReadOnlyList<ShapeType> a, IReadOnlyList<ShapeType> b)
    {
        int ac = a != null ? a.Count : 0;
        int bc = b != null ? b.Count : 0;
        if (ac != bc)
        {
            return false;
        }

        for (int i = 0; i < ac; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Drops occupancy and target maps. Does not destroy objects or change grid size.
    /// Used when a level is rebuilt at runtime.
    /// </summary>
    public void ClearRuntimeRegistrations()
    {
        occupancy.Clear();
        targets.Clear();
        closedShutters.Clear();
    }

    public Target GetTargetAt(Vector2Int position)
    {
        targets.TryGetValue(position, out Target target);
        return target;
    }

    public bool IsTargetCell(Vector2Int position)
    {
        return GetTargetAt(position) != null;
    }

    public bool IsMatchingTarget(Block block)
    {
        if (block == null)
        {
            return false;
        }

        return IsMatchingTargetAt(block, block.GridPosition);
    }

    public bool IsMatchingTargetAt(Block block, Vector2Int proposedAnchor)
    {
        return HasNestMatch(block, proposedAnchor);
    }

    public bool HasNestMatch(Block block, Vector2Int proposedAnchor)
    {
        if (block == null || !CanTranslateBlock(block, proposedAnchor))
        {
            return false;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = proposedAnchor + block.GetLocalCell(i);
            Target target = GetTargetAt(world);
            if (target == null)
            {
                continue;
            }

            if (ShapeMatch.AreMatchingLayers(
                    target.GetRequiredIdentityAtWorld(world),
                    block.GetActiveIdentity(i)))
            {
                return true;
            }
        }

        return false;
    }

    public void CollectNestMatches(
        Block block,
        Vector2Int proposedAnchor,
        List<int> cellIndices,
        List<Target> matchedTargets)
    {
        cellIndices.Clear();
        matchedTargets.Clear();
        if (block == null)
        {
            return;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int world = proposedAnchor + block.GetLocalCell(i);
            Target target = GetTargetAt(world);
            if (target == null
                || !ShapeMatch.AreMatchingLayers(
                    target.GetRequiredIdentityAtWorld(world),
                    block.GetActiveIdentity(i)))
            {
                continue;
            }

            cellIndices.Add(i);
            matchedTargets.Add(target);
        }
    }

    public bool AreAllMatchesComplete()
    {
        return occupancy.Count == 0 && targets.Count == 0;
    }

    public bool AreAllBlocksSettled()
    {
        if (occupancy.Count == 0)
        {
            return false;
        }

        foreach (Block registeredBlock in occupancy.Values)
        {
            if (registeredBlock == null || !registeredBlock.IsSettled)
            {
                return false;
            }
        }

        return true;
    }

    private void LogOccupancy(string message)
    {
        if (debugOccupancy)
        {
            Debug.Log($"[Board Occupancy] {message}", this);
        }
    }

    private void OnEnable()
    {
        RefreshRuntimeGrid();
    }

    private void OnDisable()
    {
        SetRuntimeGridVisible(false);
    }

    private void OnDestroy()
    {
        DestroyLineSprite();
    }

    private void OnValidate()
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        cellSize = Mathf.Max(0.01f, cellSize);
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        StretchRuntimeGrid();
    }

    private void Update()
    {
        RefreshRuntimeGrid();
    }

    private void OnDrawGizmos()
    {
        if (!showGrid)
        {
            return;
        }

        Gizmos.color = new Color(1f, 1f, 1f, 0.7f);

        for (int x = 0; x <= width; x++)
        {
            Gizmos.DrawLine(GetCornerWorld(x, 0), GetCornerWorld(x, height));
        }

        for (int y = 0; y <= height; y++)
        {
            Gizmos.DrawLine(GetCornerWorld(0, y), GetCornerWorld(width, y));
        }
    }

    private Vector3 GetCornerWorld(int cornerX, int cornerY)
    {
        return BoardRect.TransformPoint(GetCornerLocal(cornerX, cornerY));
    }

    private Vector2 GetCornerLocal(int cornerX, int cornerY)
    {
        Rect rect = EnsureUIGridSpace().CellGridRect;
        Vector2 cell = VisualCellSize;
        return new Vector2(rect.xMin + cornerX * cell.x, rect.yMin + cornerY * cell.y);
    }

    private void RefreshRuntimeGrid()
    {
        if (!showGrid)
        {
            SetRuntimeGridVisible(false);
            return;
        }

        if (runtimeGridRoot == null || builtWidth != width || builtHeight != height)
        {
            BuildRuntimeGrid();
        }

        StretchRuntimeGrid();
        // Phase 11: World3D is the board presentation — keep UI grid chrome hidden.
        SetRuntimeGridVisible(false);
    }

    private void BuildRuntimeGrid()
    {
        EnsureRuntimeGridRoot();
        ClearRuntimeGridChildren();
        EnsureLineSprite();

        builtWidth = width;
        builtHeight = height;

        for (int x = 0; x <= width; x++)
        {
            float t = width == 0 ? 0f : (float)x / width;
            CreateGridLine($"Vertical_{x}", new Vector2(t, 0f), new Vector2(t, 1f), new Vector2(LineThickness, 0f));
        }

        for (int y = 0; y <= height; y++)
        {
            float t = height == 0 ? 0f : (float)y / height;
            CreateGridLine($"Horizontal_{y}", new Vector2(0f, t), new Vector2(1f, t), new Vector2(0f, LineThickness));
        }
    }

    private void EnsureRuntimeGridRoot()
    {
        if (runtimeGridRoot != null)
        {
            return;
        }

        Transform existing = transform.Find(RuntimeGridName);
        if (existing != null)
        {
            runtimeGridRoot = existing as RectTransform;
            if (runtimeGridRoot == null)
            {
                DestroyImmediate(existing.gameObject);
            }
            else
            {
                return;
            }
        }

        var rootObject = new GameObject(RuntimeGridName, typeof(RectTransform));
        runtimeGridRoot = rootObject.GetComponent<RectTransform>();
        runtimeGridRoot.SetParent(BoardRect, false);
        runtimeGridRoot.gameObject.layer = gameObject.layer;
        runtimeGridRoot.hideFlags = HideFlags.DontSave;
    }

    private void StretchRuntimeGrid()
    {
        if (runtimeGridRoot == null)
        {
            return;
        }

        runtimeGridRoot.anchorMin = Vector2.zero;
        runtimeGridRoot.anchorMax = Vector2.one;
        Rect board = BoardRect.rect;
        Rect cellGrid = EnsureUIGridSpace().CellGridRect;
        float left = cellGrid.xMin - board.xMin;
        float bottom = cellGrid.yMin - board.yMin;
        float right = board.xMax - cellGrid.xMax;
        float top = board.yMax - cellGrid.yMax;
        runtimeGridRoot.offsetMin = new Vector2(left, bottom);
        runtimeGridRoot.offsetMax = new Vector2(-right, -top);
        runtimeGridRoot.pivot = BoardRect.pivot;
        runtimeGridRoot.localScale = Vector3.one;
        runtimeGridRoot.localRotation = Quaternion.identity;
    }

    private void CreateGridLine(string lineName, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta)
    {
        var lineObject = new GameObject(lineName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var lineRect = lineObject.GetComponent<RectTransform>();
        lineRect.SetParent(runtimeGridRoot, false);
        lineObject.layer = gameObject.layer;
        lineObject.hideFlags = HideFlags.DontSave;

        lineRect.anchorMin = anchorMin;
        lineRect.anchorMax = anchorMax;
        lineRect.pivot = new Vector2(0.5f, 0.5f);
        lineRect.anchoredPosition = Vector2.zero;
        lineRect.sizeDelta = sizeDelta;

        var image = lineObject.GetComponent<Image>();
        image.sprite = lineSprite;
        image.color = new Color(1f, 1f, 1f, 0.7f);
        image.raycastTarget = false;
    }

    private void ClearRuntimeGridChildren()
    {
        if (runtimeGridRoot == null)
        {
            return;
        }

        for (int i = runtimeGridRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = runtimeGridRoot.GetChild(i);
            DestroyImmediate(child.gameObject);
        }
    }

    private void SetRuntimeGridVisible(bool visible)
    {
        if (runtimeGridRoot != null)
        {
            runtimeGridRoot.gameObject.SetActive(visible);
        }
    }

    private void EnsureLineSprite()
    {
        if (lineSprite != null)
        {
            return;
        }

        Texture2D texture = Texture2D.whiteTexture;
        lineSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            texture.width);
        lineSprite.hideFlags = HideFlags.HideAndDontSave;
    }

    private void DestroyLineSprite()
    {
        if (lineSprite == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(lineSprite);
        }
        else
        {
            DestroyImmediate(lineSprite);
        }

        lineSprite = null;
    }
}