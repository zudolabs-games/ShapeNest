using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

internal sealed class SolverRuntimeCell
{
    public Vector2Int LocalPosition;
    public readonly List<MatchIdentity> Layers = new List<MatchIdentity>();

    public MatchIdentity ActiveIdentity => Layers[0];

    public SolverRuntimeCell Clone()
    {
        var copy = new SolverRuntimeCell { LocalPosition = LocalPosition };
        copy.Layers.AddRange(Layers);
        return copy;
    }
}

internal sealed class SolverRuntimeBlock
{
    public Vector2Int Anchor;
    public MoveDirection MoveDirection;
    public bool Settled;
    public readonly List<SolverRuntimeCell> Cells = new List<SolverRuntimeCell>();

    public SolverRuntimeBlock Clone()
    {
        var copy = new SolverRuntimeBlock
        {
            Anchor = Anchor,
            MoveDirection = MoveDirection,
            Settled = Settled
        };
        for (int i = 0; i < Cells.Count; i++) copy.Cells.Add(Cells[i].Clone());
        return copy;
    }
}

internal sealed class SolverRuntimeTarget
{
    public Vector2Int Anchor;
    public readonly List<SolverRuntimeCell> Cells = new List<SolverRuntimeCell>();

    public SolverRuntimeTarget Clone()
    {
        var copy = new SolverRuntimeTarget { Anchor = Anchor };
        for (int i = 0; i < Cells.Count; i++) copy.Cells.Add(Cells[i].Clone());
        return copy;
    }
}

internal sealed class SolverRuntimeState
{
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    public int Width;
    public int Height;
    public readonly HashSet<Vector2Int> BlockedCells = new HashSet<Vector2Int>();
    public readonly List<SolverRuntimeBlock> Blocks = new List<SolverRuntimeBlock>();
    public readonly List<SolverRuntimeTarget> Targets = new List<SolverRuntimeTarget>();

    public string Key => BuildKey();
    public bool IsSolved => Targets.Count == 0;

    public SolverRuntimeState Clone()
    {
        var copy = new SolverRuntimeState { Width = Width, Height = Height };
        foreach (Vector2Int cell in BlockedCells) copy.BlockedCells.Add(cell);
        for (int i = 0; i < Blocks.Count; i++) copy.Blocks.Add(Blocks[i].Clone());
        for (int i = 0; i < Targets.Count; i++) copy.Targets.Add(Targets[i].Clone());
        return copy;
    }

    public bool TrySlideBlock(int blockIndex, Vector2Int direction, out SolverRuntimeState next)
    {
        next = null;
        if (blockIndex < 0 || blockIndex >= Blocks.Count || direction == Vector2Int.zero)
        {
            return false;
        }

        SolverRuntimeBlock block = Blocks[blockIndex];
        if (block.Settled || !IsDirectionAllowed(block.MoveDirection, direction)) return false;
        SolverRuntimeState candidate = Clone();
        SolverRuntimeBlock moving = candidate.Blocks[blockIndex];
        Vector2Int landing = moving.Anchor;
        while (CanPlace(candidate, blockIndex, landing + direction)) landing += direction;
        if (landing == moving.Anchor) return false;
        moving.Anchor = landing;
        next = candidate;
        return true;
    }

    public bool TryConsumeMatchingCells(int blockIndex, IList<int> blockCellIndices, int targetIndex, IList<int> targetCellIndices, out SolverRuntimeState next)
    {
        next = null;
        if (blockIndex < 0 || blockIndex >= Blocks.Count || targetIndex < 0 || targetIndex >= Targets.Count)
        {
            return false;
        }

        SolverRuntimeState candidate = Clone();
        SolverRuntimeBlock block = candidate.Blocks[blockIndex];
        SolverRuntimeTarget target = candidate.Targets[targetIndex];
        if (blockCellIndices == null || targetCellIndices == null || blockCellIndices.Count != targetCellIndices.Count || blockCellIndices.Count == 0)
        {
            return false;
        }

        var consumedBlockCells = new HashSet<int>();
        var consumedTargetCells = new HashSet<int>();
        for (int i = 0; i < blockCellIndices.Count; i++)
        {
            int blockCellIndex = blockCellIndices[i];
            int targetCellIndex = targetCellIndices[i];
            if (!consumedBlockCells.Add(blockCellIndex) || !consumedTargetCells.Add(targetCellIndex)
                || blockCellIndex < 0 || blockCellIndex >= block.Cells.Count
                || targetCellIndex < 0 || targetCellIndex >= target.Cells.Count)
            {
                return false;
            }

            SolverRuntimeCell blockCell = block.Cells[blockCellIndex];
            SolverRuntimeCell targetCell = target.Cells[targetCellIndex];
            if (block.Anchor + blockCell.LocalPosition != target.Anchor + targetCell.LocalPosition
                || blockCell.ActiveIdentity != targetCell.ActiveIdentity)
            {
                return false;
            }

            blockCell.Layers.RemoveAt(0);
            targetCell.Layers.RemoveAt(0);
        }

        RemoveEmptyAndRebuild(candidate, blockIndex, targetIndex);
        next = candidate;
        return true;
    }

    private static void RemoveEmptyAndRebuild(SolverRuntimeState state, int blockIndex, int targetIndex)
    {
        SolverRuntimeBlock block = state.Blocks[blockIndex];
        SolverRuntimeTarget target = state.Targets[targetIndex];
        RemoveEmptyCells(target.Cells);
        NormalizeTarget(target);
        RemoveEmptyCells(block.Cells);
        var worlds = new List<Vector2Int>();
        for (int i = 0; i < block.Cells.Count; i++) worlds.Add(block.Anchor + block.Cells[i].LocalPosition);
        var components = SplitComponents(worlds, block.Cells);
        state.Blocks.RemoveAt(blockIndex);
        for (int i = components.Count - 1; i >= 0; i--) state.Blocks.Insert(blockIndex, components[i]);
        if (target.Cells.Count == 0) state.Targets.RemoveAt(targetIndex);
    }

    private static void RemoveEmptyCells(List<SolverRuntimeCell> cells)
    {
        for (int i = cells.Count - 1; i >= 0; i--) if (cells[i].Layers.Count == 0) cells.RemoveAt(i);
    }

    private static List<SolverRuntimeBlock> SplitComponents(List<Vector2Int> worlds, List<SolverRuntimeCell> cells)
    {
        var result = new List<SolverRuntimeBlock>();
        var remaining = new HashSet<int>();
        for (int i = 0; i < worlds.Count; i++) remaining.Add(i);
        while (remaining.Count > 0)
        {
            int start = int.MaxValue;
            foreach (int index in remaining) if (start == int.MaxValue || Compare(worlds[index], worlds[start]) < 0) start = index;
            var indices = new List<int> { start };
            remaining.Remove(start);
            for (int cursor = 0; cursor < indices.Count; cursor++)
            {
                Vector2Int world = worlds[indices[cursor]];
                for (int d = 0; d < Directions.Length; d++)
                {
                    int candidate = -1;
                    foreach (int pending in remaining) { if (worlds[pending] == world + Directions[d]) { candidate = pending; break; } }
                    if (candidate >= 0) { indices.Add(candidate); remaining.Remove(candidate); }
                }
            }

            Vector2Int anchor = worlds[indices[0]];
            for (int i = 1; i < indices.Count; i++) if (Compare(worlds[indices[i]], anchor) < 0) anchor = worlds[indices[i]];
            var component = new SolverRuntimeBlock { Anchor = anchor, MoveDirection = MoveDirection.Any };
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                SolverRuntimeCell cell = cells[index].Clone();
                cell.LocalPosition = worlds[index] - anchor;
                component.Cells.Add(cell);
            }
            result.Add(component);
        }
        return result;
    }

    private static void NormalizeTarget(SolverRuntimeTarget target)
    {
        if (target.Cells.Count == 0) return;
        Vector2Int anchor = target.Anchor + target.Cells[0].LocalPosition;
        for (int i = 1; i < target.Cells.Count; i++)
        {
            Vector2Int world = target.Anchor + target.Cells[i].LocalPosition;
            if (Compare(world, anchor) < 0) anchor = world;
        }
        for (int i = 0; i < target.Cells.Count; i++) target.Cells[i].LocalPosition = target.Anchor + target.Cells[i].LocalPosition - anchor;
        target.Anchor = anchor;
    }

    private bool CanPlace(SolverRuntimeState state, int movingIndex, Vector2Int anchor)
    {
        SolverRuntimeBlock moving = state.Blocks[movingIndex];
        for (int i = 0; i < moving.Cells.Count; i++)
        {
            Vector2Int world = anchor + moving.Cells[i].LocalPosition;
            if (!IsInside(world) || BlockedCells.Contains(world)) return false;
            for (int b = 0; b < state.Blocks.Count; b++)
            {
                if (b == movingIndex || state.Blocks[b].Settled) continue;
                for (int c = 0; c < state.Blocks[b].Cells.Count; c++)
                    if (state.Blocks[b].Anchor + state.Blocks[b].Cells[c].LocalPosition == world) return false;
            }
        }
        return true;
    }

    private bool IsInside(Vector2Int cell) => cell.x >= 0 && cell.x < Width && cell.y >= 0 && cell.y < Height;
    private static bool IsDirectionAllowed(MoveDirection allowed, Vector2Int direction) => allowed == MoveDirection.Any || (allowed == MoveDirection.Up && direction == Vector2Int.up) || (allowed == MoveDirection.Down && direction == Vector2Int.down) || (allowed == MoveDirection.Left && direction == Vector2Int.left) || (allowed == MoveDirection.Right && direction == Vector2Int.right);
    private static int Compare(Vector2Int a, Vector2Int b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);

    private string BuildKey()
    {
        var blockKeys = new List<string>();
        for (int i = 0; i < Blocks.Count; i++) blockKeys.Add(ComponentKey(Blocks[i].Anchor, Blocks[i].Cells, Blocks[i].Settled));
        var targetKeys = new List<string>();
        for (int i = 0; i < Targets.Count; i++) targetKeys.Add(ComponentKey(Targets[i].Anchor, Targets[i].Cells, false));
        blockKeys.Sort(StringComparer.Ordinal); targetKeys.Sort(StringComparer.Ordinal);
        return string.Join("/", blockKeys.ToArray()) + "#" + string.Join("/", targetKeys.ToArray());
    }

    private static string ComponentKey(Vector2Int anchor, List<SolverRuntimeCell> cells, bool settled)
    {
        var cellKeys = new List<string>();
        for (int i = 0; i < cells.Count; i++)
        {
            var layers = new StringBuilder();
            for (int l = 0; l < cells[i].Layers.Count; l++) layers.Append((int)cells[i].Layers[l].Shape).Append(':').Append((int)cells[i].Layers[l].Color).Append(',');
            cellKeys.Add(cells[i].LocalPosition.x + "," + cells[i].LocalPosition.y + ":" + layers);
        }
        cellKeys.Sort(StringComparer.Ordinal);
        return anchor.x + "," + anchor.y + "," + (settled ? "1" : "0") + "[" + string.Join(";", cellKeys.ToArray()) + "]";
    }
}
