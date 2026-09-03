using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One occupied cell in a block or target, relative to the piece's anchor cell.
/// </summary>
[Serializable]
public class ShapeCellData
{
    public Vector2Int localPosition;
    public ShapeType shapeType;

    [Tooltip("Optional outer color override. Default uses the shape-type palette.")]
    public ShapeColor outerColor = ShapeColor.Default;

    [Tooltip("Nested layers under Shape Type, next-to-promote first (outermost remaining child). Empty means a simple cell.")]
    public List<ShapeType> innerShapes = new List<ShapeType>();

    [Tooltip("Optional inner colors parallel to Inner Shapes. Default uses the inner shape-type palette.")]
    public List<ShapeColor> innerShapeColors = new List<ShapeColor>();
}

/// <summary>
/// Deterministic footprint helpers. Empty cell lists mean a single cell at (0,0).
/// Does not allocate in the hop/match hot path when callers pass cached buffers.
/// </summary>
public static class ShapeLayout
{
    public static int EffectiveCount(IReadOnlyList<ShapeCellData> cells)
    {
        return cells == null || cells.Count == 0 ? 1 : cells.Count;
    }

    public static Vector2Int EffectiveLocal(IReadOnlyList<ShapeCellData> cells, int index)
    {
        if (cells == null || cells.Count == 0 || index < 0 || index >= cells.Count)
        {
            return Vector2Int.zero;
        }

        return cells[index].localPosition;
    }

    public static ShapeType EffectiveShape(IReadOnlyList<ShapeCellData> cells, int index, ShapeType fallback)
    {
        if (cells == null || cells.Count == 0 || index < 0 || index >= cells.Count)
        {
            return fallback;
        }

        return cells[index].shapeType;
    }

    public static ShapeType AnchorShape(IReadOnlyList<ShapeCellData> cells, ShapeType fallback)
    {
        if (cells == null || cells.Count == 0)
        {
            return fallback;
        }

        for (int i = 0; i < cells.Count; i++)
        {
            ShapeCellData cell = cells[i];
            if (cell != null && cell.localPosition == Vector2Int.zero)
            {
                return cell.shapeType;
            }
        }

        return cells[0] != null ? cells[0].shapeType : fallback;
    }

    public static void CopyInto(IReadOnlyList<ShapeCellData> source, ShapeType fallback, List<ShapeCellData> destination)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        if (source == null || source.Count == 0)
        {
            destination.Add(new ShapeCellData
            {
                localPosition = Vector2Int.zero,
                shapeType = fallback,
                outerColor = ShapeColor.Default,
                innerShapes = new List<ShapeType>(),
                innerShapeColors = new List<ShapeColor>()
            });
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            ShapeCellData cell = source[i];
            if (cell == null)
            {
                continue;
            }

            if (ContainsLocal(destination, cell.localPosition))
            {
                continue;
            }

            destination.Add(new ShapeCellData
            {
                localPosition = cell.localPosition,
                shapeType = cell.shapeType,
                outerColor = cell.outerColor,
                innerShapes = CloneInners(cell.innerShapes),
                innerShapeColors = CloneInnerColors(cell.innerShapeColors)
            });
        }

        if (destination.Count == 0)
        {
            destination.Add(new ShapeCellData
            {
                localPosition = Vector2Int.zero,
                shapeType = fallback,
                outerColor = ShapeColor.Default,
                innerShapes = new List<ShapeType>(),
                innerShapeColors = new List<ShapeColor>()
            });
        }
    }

    public static List<ShapeCellData> Clone(IReadOnlyList<ShapeCellData> source, ShapeType fallback)
    {
        var copy = new List<ShapeCellData>();
        CopyInto(source, fallback, copy);
        return copy;
    }

    public static bool SameRelative(
        IReadOnlyList<ShapeCellData> a,
        ShapeType aFallback,
        IReadOnlyList<ShapeCellData> b,
        ShapeType bFallback)
    {
        int countA = EffectiveCount(a);
        int countB = EffectiveCount(b);
        if (countA != countB)
        {
            return false;
        }

        for (int i = 0; i < countA; i++)
        {
            Vector2Int local = EffectiveLocal(a, i);
            ShapeType shape = EffectiveShape(a, i, aFallback);
            if (!HasCell(b, bFallback, countB, local, shape))
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasCell(
        IReadOnlyList<ShapeCellData> cells,
        ShapeType fallback,
        int count,
        Vector2Int local,
        ShapeType shape)
    {
        for (int i = 0; i < count; i++)
        {
            if (EffectiveLocal(cells, i) == local && EffectiveShape(cells, i, fallback) == shape)
            {
                return true;
            }
        }

        return false;
    }

    public static ShapeType ShapeAtLocal(IReadOnlyList<ShapeCellData> cells, ShapeType fallback, Vector2Int local)
    {
        int count = EffectiveCount(cells);
        for (int i = 0; i < count; i++)
        {
            if (EffectiveLocal(cells, i) == local)
            {
                return EffectiveShape(cells, i, fallback);
            }
        }

        return fallback;
    }

    public static bool ContainsAnchorCell(IReadOnlyList<ShapeCellData> cells)
    {
        if (cells == null || cells.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i] != null && cells[i].localPosition == Vector2Int.zero)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasDuplicateLocals(IReadOnlyList<ShapeCellData> cells)
    {
        if (cells == null || cells.Count <= 1)
        {
            return false;
        }

        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i] == null)
            {
                continue;
            }

            for (int j = i + 1; j < cells.Count; j++)
            {
                if (cells[j] != null && cells[j].localPosition == cells[i].localPosition)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool HasNullCell(IReadOnlyList<ShapeCellData> cells)
    {
        if (cells == null)
        {
            return false;
        }

        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i] == null)
            {
                return true;
            }
        }

        return false;
    }

    public static bool OccupiesWorldCell(Vector2Int anchor, IReadOnlyList<ShapeCellData> cells, Vector2Int world)
    {
        int count = EffectiveCount(cells);
        for (int i = 0; i < count; i++)
        {
            if (anchor + EffectiveLocal(cells, i) == world)
            {
                return true;
            }
        }

        return false;
    }

    public static List<ShapeType> CloneInners(IReadOnlyList<ShapeType> source)
    {
        var copy = new List<ShapeType>();
        if (source == null)
        {
            return copy;
        }

        for (int i = 0; i < source.Count; i++)
        {
            copy.Add(source[i]);
        }

        return copy;
    }

    public static List<ShapeColor> CloneInnerColors(IReadOnlyList<ShapeColor> source)
    {
        var copy = new List<ShapeColor>();
        if (source == null)
        {
            return copy;
        }

        for (int i = 0; i < source.Count; i++)
        {
            copy.Add(source[i]);
        }

        return copy;
    }

    public static ShapeColor EffectiveOuterColor(ShapeCellData cell)
    {
        return cell != null ? cell.outerColor : ShapeColor.Default;
    }

    public static ShapeColor EffectiveInnerColor(ShapeCellData cell, int innerIndex)
    {
        if (cell?.innerShapeColors != null
            && innerIndex >= 0
            && innerIndex < cell.innerShapeColors.Count)
        {
            return cell.innerShapeColors[innerIndex];
        }

        return ShapeColor.Default;
    }

    public static ShapeColor ActiveInnerColor(ShapeCellData cell)
    {
        if (cell?.innerShapes != null && cell.innerShapes.Count > 0)
        {
            return EffectiveInnerColor(cell, 0);
        }

        return ShapeColor.Default;
    }

    /// <summary>
    /// Gameplay matching identity for a cell: the outermost remaining layer (<see cref="ShapeCellData.shapeType"/>).
    /// Nested children live in <see cref="ShapeCellData.innerShapes"/> and are not matchable until promoted.
    /// </summary>
    public static ShapeType ActiveShape(ShapeCellData cell, ShapeType fallback)
    {
        return cell != null ? cell.shapeType : fallback;
    }

    /// <summary>
    /// Immediate nested child shown inside the outer shell, or the outer itself when none remain.
    /// </summary>
    public static ShapeType NestedChildShape(ShapeCellData cell, ShapeType fallback)
    {
        if (cell?.innerShapes != null && cell.innerShapes.Count > 0)
        {
            return cell.innerShapes[0];
        }

        return ActiveShape(cell, fallback);
    }

    public static ShapeColor NestedChildColor(ShapeCellData cell)
    {
        return EffectiveInnerColor(cell, 0);
    }

    public static int LayerCount(ShapeCellData cell)
    {
        if (cell == null)
        {
            return 1;
        }

        int inner = cell.innerShapes != null ? cell.innerShapes.Count : 0;
        return inner + 1;
    }

    public static int TotalLayers(IReadOnlyList<ShapeCellData> cells, ShapeType fallback, PieceComposition composition, ShapeType outerShape)
    {
        int count = EffectiveCount(cells);
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            ShapeCellData cell = cells != null && i < cells.Count ? cells[i] : null;
            if (cell == null)
            {
                total += composition == PieceComposition.ShapeInShape && outerShape != fallback ? 2 : 1;
                continue;
            }

            total += LayerCount(cell);
            if (composition == PieceComposition.ShapeInShape
                && (cell.innerShapes == null || cell.innerShapes.Count == 0)
                && cell.shapeType != outerShape)
            {
                total += 1;
            }
        }

        return total;
    }

    public static void ApplyLegacyShapeInShape(
        List<ShapeCellData> cells,
        PieceComposition composition,
        ShapeType outerShape)
    {
        if (cells == null || composition != PieceComposition.ShapeInShape)
        {
            return;
        }

        for (int i = 0; i < cells.Count; i++)
        {
            ShapeCellData cell = cells[i];
            if (cell == null)
            {
                continue;
            }

            if (cell.innerShapes != null && cell.innerShapes.Count > 0)
            {
                continue;
            }

            ShapeType inner = cell.shapeType;
            cell.innerShapes = new List<ShapeType> { inner };
            cell.shapeType = outerShape;
        }
    }

    /// <summary>
    /// Consumes the outermost matching layer when offered ShapeType + Color both agree.
    /// When inner layers remain, promotes the next inner shape/color into
    /// <see cref="ShapeCellData.shapeType"/> / <see cref="ShapeCellData.outerColor"/>
    /// and returns true with <paramref name="cellRemains"/> = true.
    /// </summary>
    public static bool TryConsumeLayer(
        ShapeCellData cell,
        MatchIdentity offered,
        out bool cellRemains)
    {
        cellRemains = false;
        if (cell == null || !ShapeMatch.AreMatchingLayers(ShapeMatch.FromCell(cell), offered))
        {
            return false;
        }

        if (cell.innerShapes != null && cell.innerShapes.Count > 0)
        {
            cell.shapeType = cell.innerShapes[0];
            cell.innerShapes.RemoveAt(0);
            if (cell.innerShapeColors != null && cell.innerShapeColors.Count > 0)
            {
                cell.outerColor = cell.innerShapeColors[0];
                cell.innerShapeColors.RemoveAt(0);
            }
            else
            {
                cell.outerColor = ShapeColor.Default;
            }

            cellRemains = true;
            return true;
        }

        cellRemains = false;
        return true;
    }

    public static bool TryConsumeLayer(
        ShapeCellData cell,
        ShapeType offered,
        ShapeColor offeredColor,
        out bool cellRemains)
    {
        return TryConsumeLayer(cell, new MatchIdentity(offered, offeredColor), out cellRemains);
    }

    /// <summary>
    /// Shape-only helper for tests/legacy. Uses the cell's own configured color so
    /// peel still requires the offered shape to be the active outer layer.
    /// </summary>
    public static bool TryConsumeLayer(ShapeCellData cell, ShapeType offered, out bool cellRemains)
    {
        return TryConsumeLayer(cell, offered, EffectiveOuterColor(cell), out cellRemains);
    }

    /// <summary>Backward-compatible wrapper. Prefer the out-bool overload.</summary>
    public static bool TryConsumeLayer(ShapeCellData cell, ShapeType offered)
    {
        return TryConsumeLayer(cell, offered, out _);
    }

    public static bool TryConsumeLayer(ShapeCellData cell, MatchIdentity offered)
    {
        return TryConsumeLayer(cell, offered, out _);
    }

    public static bool IsCellFullyConsumed(ShapeCellData cell, MatchIdentity offered)
    {
        if (cell == null)
        {
            return true;
        }

        if (!ShapeMatch.AreMatchingLayers(ShapeMatch.FromCell(cell), offered))
        {
            return false;
        }

        return cell.innerShapes == null || cell.innerShapes.Count == 0;
    }

    public static bool IsCellFullyConsumed(ShapeCellData cell, ShapeType offered)
    {
        return IsCellFullyConsumed(cell, new MatchIdentity(offered, EffectiveOuterColor(cell)));
    }

    /// <summary>
    /// Splits remaining world cells into 4-connected components.
    /// Each component's locals are relative to its lowest-then-leftmost world cell.
    /// </summary>
    public static void SplitConnected(
        List<Vector2Int> worlds,
        List<ShapeCellData> remainingCells,
        List<Vector2Int> componentAnchors,
        List<List<ShapeCellData>> componentCells)
    {
        componentAnchors.Clear();
        componentCells.Clear();
        if (worlds == null || remainingCells == null || worlds.Count == 0)
        {
            return;
        }

        int count = worlds.Count;
        var visited = new bool[count];
        var queue = new Queue<int>();
        Vector2Int[] dirs =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        for (int start = 0; start < count; start++)
        {
            if (visited[start])
            {
                continue;
            }

            var indices = new List<int>();
            queue.Enqueue(start);
            visited[start] = true;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                indices.Add(i);
                for (int d = 0; d < dirs.Length; d++)
                {
                    Vector2Int next = worlds[i] + dirs[d];
                    for (int j = 0; j < count; j++)
                    {
                        if (visited[j] || worlds[j] != next)
                        {
                            continue;
                        }

                        visited[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }

            Vector2Int anchor = worlds[indices[0]];
            for (int n = 1; n < indices.Count; n++)
            {
                Vector2Int w = worlds[indices[n]];
                if (w.y < anchor.y || (w.y == anchor.y && w.x < anchor.x))
                {
                    anchor = w;
                }
            }

            var locals = new List<ShapeCellData>(indices.Count);
            for (int n = 0; n < indices.Count; n++)
            {
                int i = indices[n];
                ShapeCellData source = remainingCells[i];
                locals.Add(new ShapeCellData
                {
                    localPosition = worlds[i] - anchor,
                    shapeType = source.shapeType,
                    outerColor = source.outerColor,
                    innerShapes = CloneInners(source.innerShapes),
                    innerShapeColors = CloneInnerColors(source.innerShapeColors)
                });
            }

            componentAnchors.Add(anchor);
            componentCells.Add(locals);
        }
    }

    /// <summary>
    /// Collects matchable identities for a single cell (outer then nested children).
    /// </summary>
    public static void CollectResolvableIdentitiesForCell(
        ShapeCellData cell,
        ShapeType fallback,
        List<MatchIdentity> destination)
    {
        if (destination == null)
        {
            return;
        }

        if (cell == null)
        {
            destination.Add(new MatchIdentity(fallback, ShapeColor.Default));
            return;
        }

        destination.Add(ShapeMatch.FromCell(cell, fallback));
        if (cell.innerShapes == null)
        {
            return;
        }

        for (int j = 0; j < cell.innerShapes.Count; j++)
        {
            destination.Add(new MatchIdentity(cell.innerShapes[j], EffectiveInnerColor(cell, j)));
        }
    }

    /// <summary>
    /// Collects every matchable layer identity from a piece (outer then nested children per cell).
    /// Applies legacy ShapeInShape conversion so validation matches runtime spawn behavior.
    /// </summary>
    public static void CollectResolvableIdentities(
        IReadOnlyList<ShapeCellData> cells,
        ShapeType fallback,
        PieceComposition composition,
        ShapeType outerShape,
        List<MatchIdentity> destination)
    {
        if (destination == null)
        {
            return;
        }

        List<ShapeCellData> working = Clone(cells, fallback);
        ApplyLegacyShapeInShape(working, composition, outerShape);
        int count = EffectiveCount(working);
        for (int i = 0; i < count; i++)
        {
            ShapeCellData cell = working != null && i < working.Count ? working[i] : null;
            if (cell == null)
            {
                destination.Add(new MatchIdentity(fallback, ShapeColor.Default));
                continue;
            }

            destination.Add(ShapeMatch.FromCell(cell, fallback));
            if (cell.innerShapes == null)
            {
                continue;
            }

            for (int j = 0; j < cell.innerShapes.Count; j++)
            {
                destination.Add(new MatchIdentity(cell.innerShapes[j], EffectiveInnerColor(cell, j)));
            }
        }
    }

    /// <summary>
    /// Collects every matchable layer shape from a piece (outer then nested children per cell).
    /// Applies legacy ShapeInShape conversion so validation matches runtime spawn behavior.
    /// </summary>
    public static void CollectResolvableLayers(
        IReadOnlyList<ShapeCellData> cells,
        ShapeType fallback,
        PieceComposition composition,
        ShapeType outerShape,
        List<ShapeType> destination)
    {
        if (destination == null)
        {
            return;
        }

        var identities = new List<MatchIdentity>();
        CollectResolvableIdentities(cells, fallback, composition, outerShape, identities);
        for (int i = 0; i < identities.Count; i++)
        {
            destination.Add(identities[i].Shape);
        }
    }

    public static bool LayerSetsMatch(
        IList<ShapeType> a,
        IList<ShapeType> b)
    {
        if (a == null || b == null || a.Count != b.Count)
        {
            return false;
        }

        var counts = new Dictionary<ShapeType, int>();
        for (int i = 0; i < a.Count; i++)
        {
            ShapeType shape = a[i];
            counts.TryGetValue(shape, out int value);
            counts[shape] = value + 1;
        }

        for (int i = 0; i < b.Count; i++)
        {
            ShapeType shape = b[i];
            if (!counts.TryGetValue(shape, out int value) || value <= 0)
            {
                return false;
            }

            counts[shape] = value - 1;
        }

        return true;
    }

    public static ShapeType VisualOuter(ShapeCellData cell, ShapeType fallback)
    {
        return cell != null ? cell.shapeType : fallback;
    }

    public static bool AreCellsFourConnected(IReadOnlyList<ShapeCellData> cells)
    {
        int count = EffectiveCount(cells);
        if (count <= 1)
        {
            return true;
        }

        var worlds = new Vector2Int[count];
        for (int i = 0; i < count; i++)
        {
            worlds[i] = EffectiveLocal(cells, i);
        }

        var visited = new bool[count];
        var queue = new Queue<int>();
        queue.Enqueue(0);
        visited[0] = true;
        int seen = 0;
        Vector2Int[] dirs =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            seen++;
            for (int d = 0; d < dirs.Length; d++)
            {
                Vector2Int next = worlds[i] + dirs[d];
                for (int j = 0; j < count; j++)
                {
                    if (visited[j] || worlds[j] != next)
                    {
                        continue;
                    }

                    visited[j] = true;
                    queue.Enqueue(j);
                }
            }
        }

        return seen == count;
    }

    public static bool HasInvalidNestedLayer(IReadOnlyList<ShapeCellData> cells, ShapeType fallback)
    {
        List<ShapeCellData> working = Clone(cells, fallback);
        int count = EffectiveCount(working);
        for (int i = 0; i < count; i++)
        {
            ShapeCellData cell = working[i];
            if (cell == null || cell.innerShapes == null)
            {
                continue;
            }

            for (int j = 0; j < cell.innerShapes.Count; j++)
            {
                if (!Enum.IsDefined(typeof(ShapeType), cell.innerShapes[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool ConfigurationsMatch(
        PieceComposition aComposition,
        ShapeType aOuterShape,
        IReadOnlyList<ShapeCellData> aCells,
        ShapeType aShape,
        PieceComposition bComposition,
        ShapeType bOuterShape,
        IReadOnlyList<ShapeCellData> bCells,
        ShapeType bShape)
    {
        if (aComposition != bComposition)
        {
            return false;
        }

        if (aComposition == PieceComposition.ShapeInShape && aOuterShape != bOuterShape)
        {
            return false;
        }

        return SameRelative(aCells, aShape, bCells, bShape);
    }

    private static bool ContainsLocal(List<ShapeCellData> cells, Vector2Int local)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i] != null && cells[i].localPosition == local)
            {
                return true;
            }
        }

        return false;
    }
}
