using System.Text;
using UnityEngine;
using System.Collections.Generic;

internal struct SolverBlock
{
    public int X;
    public int Y;
    public ShapeType Shape;
    public MoveDirection MoveDirection;
    public bool Settled;

    public Vector2Int Position => new Vector2Int(X, Y);
}

internal struct SolverTarget
{
    public int X;
    public int Y;
    public ShapeType Shape;

    public Vector2Int Position => new Vector2Int(X, Y);
}

internal sealed class SolverLevel
{
    public int Width;
    public int Height;
    public SolverBlock[] InitialBlocks;
    public SolverTarget[] Targets;
        public HashSet<Vector2Int> BlockedCells;
}

internal sealed class SolverState
{
    public readonly SolverBlock[] Blocks;
    public readonly string Key;

    public SolverState(SolverBlock[] blocks)
    {
        Blocks = blocks;
        Key = BuildKey(blocks);
    }

    public bool AllSettled
    {
        get
        {
            if (Blocks == null || Blocks.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < Blocks.Length; i++)
            {
                if (!Blocks[i].Settled)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public SolverState CloneMoved(int blockIndex, int x, int y, bool settled)
    {
        var copy = new SolverBlock[Blocks.Length];
        for (int i = 0; i < Blocks.Length; i++)
        {
            copy[i] = Blocks[i];
        }

        copy[blockIndex].X = x;
        copy[blockIndex].Y = y;
        copy[blockIndex].Settled = settled;
        return new SolverState(copy);
    }

    private static string BuildKey(SolverBlock[] blocks)
    {
        var builder = new StringBuilder(blocks.Length * 8);
        for (int i = 0; i < blocks.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('|');
            }

            builder.Append(blocks[i].X);
            builder.Append(',');
            builder.Append(blocks[i].Y);
            builder.Append(',');
            builder.Append(blocks[i].Settled ? '1' : '0');
        }

        return builder.ToString();
    }
}

internal struct SolverMove
{
    public int BlockIndex;
    public Vector2Int Direction;

    public override string ToString()
    {
        return $"Block {BlockIndex} → {DirectionName(Direction)}";
    }

    public static string DirectionName(Vector2Int direction)
    {
        if (direction == Vector2Int.up)
        {
            return "Up";
        }

        if (direction == Vector2Int.down)
        {
            return "Down";
        }

        if (direction == Vector2Int.left)
        {
            return "Left";
        }

        if (direction == Vector2Int.right)
        {
            return "Right";
        }

        return direction.ToString();
    }
}

internal enum SolverStatus
{
    Solved,
    Unsolvable,
    LimitReached,
    ReplayFailed
}

internal sealed class SolverResult
{
    public SolverStatus Status;
    public SolverMove[] Solution = new SolverMove[0];
    public int MoveCount;
    public int ExploredStates;
    public int MaxDepthReached;
    public int InitialMoveCount;
    public int CollisionStops;
    public int TargetStops;
    public string Error;
    public bool ReplayVerified;

    public bool IsSolved => Status == SolverStatus.Solved && ReplayVerified;
}
