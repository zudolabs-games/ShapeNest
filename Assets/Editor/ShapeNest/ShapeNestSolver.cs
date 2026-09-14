using System.Collections.Generic;
using UnityEngine;

internal struct SolverLimits
{
    public int MaxDepth;
    public int MaxStates;

    public static SolverLimits Default => new SolverLimits
    {
        MaxDepth = 100,
        MaxStates = 100000
    };
}

internal static class ShapeNestSolver
{
    private static readonly Vector2Int[] Cardinals =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    public static SolverState CreateInitialState(SolverLevel level)
    {
        var blocks = new SolverBlock[level.InitialBlocks.Length];
        for (int i = 0; i < level.InitialBlocks.Length; i++)
        {
            blocks[i] = level.InitialBlocks[i];
            if (HasMatchingTarget(level.Targets, blocks[i].X, blocks[i].Y, blocks[i].Shape))
            {
                blocks[i].Settled = true;
            }
        }

        return new SolverState(blocks);
    }

    public static SolverResult Solve(SolverLevel level, SolverLimits limits)
    {
        var result = new SolverResult();
        if (level == null || level.InitialBlocks == null || level.InitialBlocks.Length == 0)
        {
            result.Status = SolverStatus.Unsolvable;
            return result;
        }

        // BuildRuntimeState and related runtime-faithful start removed; using legacy SolverState for BFScompatibility
        
        // Use legacy SolverState for BFS
        SolverState start = CreateInitialState(level);
        result.InitialMoveCount = CountLegalMoves(level, start);
        var queue = new Queue<SolverState>();
        var visited = new Dictionary<string, int>();
        var parent = new Dictionary<string, string>();
        var moveFromParent = new Dictionary<string, SolverMove>();
        queue.Enqueue(start);
        visited[start.Key] = 0;
        int explored = 0;
        int maxDepth = 0;
        SolverState solved = null;

        while (queue.Count > 0)
        {
            SolverState current = queue.Dequeue();
            explored++;
            int depth = visited[current.Key];
            if (depth > maxDepth)
            {
                maxDepth = depth;
            }

            if (explored > limits.MaxStates)
            {
                result.Status = SolverStatus.LimitReached;
                result.ExploredStates = explored;
                result.MaxDepthReached = maxDepth;
                return result;
            }

            if (depth >= limits.MaxDepth)
            {
                continue;
            }

            for (int blockIndex = 0; blockIndex < current.Blocks.Length; blockIndex++)
            {
                if (current.Blocks[blockIndex].Settled)
                {
                    continue;
                }

                IList<Vector2Int> directions = GetAllowedDirections(current.Blocks[blockIndex].MoveDirection);
                for (int d = 0; d < directions.Count; d++)
                {
                    Vector2Int direction = directions[d];
                    SolverState next = TryMove(level, current, blockIndex, direction, out bool collisionStop, out bool targetStop);
                    if (next == null)
                    {
                        continue;
                    }

                    if (visited.ContainsKey(next.Key))
                    {
                        continue;
                    }

                    visited[next.Key] = depth + 1;
                    parent[next.Key] = current.Key;
                    moveFromParent[next.Key] = new SolverMove
                    {
                        BlockIndex = blockIndex,
                        Direction = direction
                    };

                    if (next.AllSettled)
                    {
                        solved = next;
                        queue.Clear();
                        break;
                    }

                    queue.Enqueue(next);
                }

                if (solved != null)
                {
                    break;
                }
            }
        }

        result.ExploredStates = explored;
        result.MaxDepthReached = maxDepth;

        if (solved == null)
        {
            result.Status = visited.Count >= limits.MaxStates || maxDepth >= limits.MaxDepth
                ? SolverStatus.LimitReached
                : SolverStatus.Unsolvable;
            return result;
        }

        SolverMove[] solution = Reconstruct(solved.Key, parent, moveFromParent);
        result.Solution = solution;
        result.MoveCount = solution.Length;
        result.Status = SolverStatus.Solved;
        CountSolutionInteractions(level, start, solution, result);

        if (!Replay(level, start, solution))
        {
            result.Status = SolverStatus.ReplayFailed;
            result.ReplayVerified = false;
            result.Error = "Solver replay failed. The generated move sequence does not reach a solved state.";
            return result;
        }

        result.ReplayVerified = true;
        return result;
    }

    public static bool Replay(SolverLevel level, SolverState start, SolverMove[] solution)
    {
        SolverState current = start;
        if (solution == null)
        {
            return current.AllSettled;
        }

        for (int i = 0; i < solution.Length; i++)
        {
            SolverMove move = solution[i];
            SolverState next = TryMove(level, current, move.BlockIndex, move.Direction, out _, out _);
            if (next == null)
            {
                return false;
            }

            current = next;
        }

        return current.AllSettled;
    }

    public static SolverState TryMove(
        SolverLevel level,
        SolverState state,
        int blockIndex,
        Vector2Int direction,
        out bool collisionStop,
        out bool targetStop)
    {
        collisionStop = false;
        targetStop = false;

        if (state == null || blockIndex < 0 || blockIndex >= state.Blocks.Length)
        {
            return null;
        }

        SolverBlock block = state.Blocks[blockIndex];
        if (block.Settled || !IsDirectionAllowed(block.MoveDirection, direction))
        {
            return null;
        }

        Vector2Int landing = FindLandingCell(
            level,
            state,
            blockIndex,
            new Vector2Int(block.X, block.Y),
            direction,
            out collisionStop,
            out targetStop);

        if (landing.x == block.X && landing.y == block.Y)
        {
            return null;
        }

        bool settled = HasMatchingTarget(level.Targets, landing.x, landing.y, block.Shape);
        return state.CloneMoved(blockIndex, landing.x, landing.y, settled);
    }

    /// <summary>
    /// Mirrors BlockMover.FindLandingCell:
    /// edge → stop in last in-bounds cell;
    /// other block → stop before it;
    /// matching target → enter and stop;
    /// wrong-shape target → stop before it (runtime treats it as an obstacle).
    /// </summary>
    public static Vector2Int FindLandingCell(
        SolverLevel level,
        SolverState state,
        int movingIndex,
        Vector2Int start,
        Vector2Int direction,
        out bool collisionStop,
        out bool targetStop)
    {
        collisionStop = false;
        targetStop = false;
        Vector2Int current = start;

        while (true)
        {
            Vector2Int next = current + direction;
            if (!IsInsideBoard(level, next))
            {
                return current;
            }

            if (level.BlockedCells != null && level.BlockedCells.Contains(next))
            {
                collisionStop = true;
                return current;
            }

            int occupant = GetBlockIndexAt(state, next.x, next.y);
            if (occupant >= 0 && occupant != movingIndex)
            {
                collisionStop = true;
                return current;
            }

            if (TryGetTarget(level.Targets, next.x, next.y, out ShapeType targetShape))
            {
                if (targetShape == state.Blocks[movingIndex].Shape)
                {
                    targetStop = true;
                    return next;
                }

                targetStop = true;
                return current;
            }

            current = next;
        }
    }

    public static bool IsDirectionAllowed(MoveDirection moveDirection, Vector2Int direction)
    {
        switch (moveDirection)
        {
            case MoveDirection.Any:
                return direction == Vector2Int.up
                    || direction == Vector2Int.down
                    || direction == Vector2Int.left
                    || direction == Vector2Int.right;
            case MoveDirection.Up:
                return direction == Vector2Int.up;
            case MoveDirection.Down:
                return direction == Vector2Int.down;
            case MoveDirection.Left:
                return direction == Vector2Int.left;
            case MoveDirection.Right:
                return direction == Vector2Int.right;
            default:
                return false;
        }
    }

    public static SolverLevel FromLevelData(LevelData data, int width, int height)
    {
        var blocks = new List<SolverBlock>();
        if (data != null && data.blocks != null)
        {
            for (int i = 0; i < data.blocks.Count; i++)
            {
                LevelBlockData block = data.blocks[i];
                if (block == null)
                {
                    continue;
                }

                blocks.Add(new SolverBlock
                {
                    X = block.gridPosition.x,
                    Y = block.gridPosition.y,
                    Shape = block.shapeType,
                    MoveDirection = block.moveDirection
                });
            }
        }

        var targets = new List<SolverTarget>();
        if (data != null && data.targets != null)
        {
            for (int i = 0; i < data.targets.Count; i++)
            {
                LevelTargetData target = data.targets[i];
                if (target == null)
                {
                    continue;
                }

                targets.Add(new SolverTarget
                {
                    X = target.gridPosition.x,
                    Y = target.gridPosition.y,
                    Shape = target.shapeType
                });
            }
        }

        return new SolverLevel
        {
            Width = width,
            Height = height,
            InitialBlocks = blocks.ToArray(),
            Targets = targets.ToArray(),
            BlockedCells = data != null && data.blockedCells != null
                ? new HashSet<Vector2Int>(data.blockedCells)
                : new HashSet<Vector2Int>()
        };
    }

    public static SolverLevel FromLists(
        int width,
        int height,
        IList<LevelBlockData> blocks,
        IList<LevelTargetData> targets,
        IList<Vector2Int> blockedCells = null)
    {
        var solverBlocks = new List<SolverBlock>();
        if (blocks != null)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                LevelBlockData block = blocks[i];
                if (block == null)
                {
                    continue;
                }

                solverBlocks.Add(new SolverBlock
                {
                    X = block.gridPosition.x,
                    Y = block.gridPosition.y,
                    Shape = block.shapeType,
                    MoveDirection = block.moveDirection
                });
            }
        }

        var solverTargets = new List<SolverTarget>();
        if (targets != null)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                LevelTargetData target = targets[i];
                if (target == null)
                {
                    continue;
                }

                solverTargets.Add(new SolverTarget
                {
                    X = target.gridPosition.x,
                    Y = target.gridPosition.y,
                    Shape = target.shapeType
                });
            }
        }

        return new SolverLevel
        {
            Width = width,
            Height = height,
            InitialBlocks = solverBlocks.ToArray(),
            Targets = solverTargets.ToArray(),
            BlockedCells = blockedCells != null
                ? new HashSet<Vector2Int>(blockedCells)
                : new HashSet<Vector2Int>()
        };
    }

    private static IList<Vector2Int> GetAllowedDirections(MoveDirection moveDirection)
    {
        switch (moveDirection)
        {
            case MoveDirection.Any:
                return Cardinals;
            case MoveDirection.Up:
                return new[] { Vector2Int.up };
            case MoveDirection.Down:
                return new[] { Vector2Int.down };
            case MoveDirection.Left:
                return new[] { Vector2Int.left };
            case MoveDirection.Right:
                return new[] { Vector2Int.right };
            default:
                return new Vector2Int[0];
        }
    }

    private static int CountLegalMoves(SolverLevel level, SolverState state)
    {
        int count = 0;
        for (int i = 0; i < state.Blocks.Length; i++)
        {
            if (state.Blocks[i].Settled)
            {
                continue;
            }

            IList<Vector2Int> directions = GetAllowedDirections(state.Blocks[i].MoveDirection);
            for (int d = 0; d < directions.Count; d++)
            {
                if (TryMove(level, state, i, directions[d], out _, out _) != null)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void CountSolutionInteractions(
        SolverLevel level,
        SolverState start,
        SolverMove[] solution,
        SolverResult result)
    {
        SolverState current = start;
        for (int i = 0; i < solution.Length; i++)
        {
            SolverState next = TryMove(
                level,
                current,
                solution[i].BlockIndex,
                solution[i].Direction,
                out bool collisionStop,
                out bool targetStop);
            if (next == null)
            {
                return;
            }

            if (collisionStop)
            {
                result.CollisionStops++;
            }

            if (targetStop)
            {
                result.TargetStops++;
            }

            current = next;
        }
    }

    private static SolverMove[] Reconstruct(
        string solvedKey,
        Dictionary<string, string> parent,
        Dictionary<string, SolverMove> moveFromParent)
    {
        var moves = new List<SolverMove>();
        string key = solvedKey;
        while (parent.ContainsKey(key))
        {
            moves.Add(moveFromParent[key]);
            key = parent[key];
        }

        moves.Reverse();
        return moves.ToArray();
    }

    private static bool IsInsideBoard(SolverLevel level, Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < level.Width && cell.y >= 0 && cell.y < level.Height;
    }

    private static int GetBlockIndexAt(SolverState state, int x, int y)
    {
        for (int i = 0; i < state.Blocks.Length; i++)
        {
            if (state.Blocks[i].X == x && state.Blocks[i].Y == y)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryGetTarget(SolverTarget[] targets, int x, int y, out ShapeType shape)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i].X == x && targets[i].Y == y)
            {
                shape = targets[i].Shape;
                return true;
            }
        }

        shape = default;
        return false;
    }

    private static bool HasMatchingTarget(SolverTarget[] targets, int x, int y, ShapeType shape)
    {
        return TryGetTarget(targets, x, y, out ShapeType targetShape) && targetShape == shape;
    }
}
