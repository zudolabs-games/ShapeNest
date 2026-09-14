using System.Collections.Generic;
using UnityEngine;

internal enum GenerationOutcome
{
    Accepted,
    RejectedUnsolvable,
    RejectedLimitReached,
    RejectedTooEasy,
    RejectedTooHard,
    RejectedInvalid,
    RejectedTrivial,
    RejectedReplayFailed,
    RejectedExists,
    FailedAttempts
}

internal enum DifficultyTier
{
    Easy,
    Medium,
    Hard,
    Expert,
    Progressive
}

internal enum MechanicMode
{
    Off,
    On,
    Progressive
}

internal enum ExistingAssetPolicy
{
    Skip,
    Overwrite,
    NextAvailable
}

internal enum GenerationStyle
{
    Mixed,
    Dense,
    Corridor,
    Asymmetric,
    MultiCell,
    Chain
}

internal sealed class GeneratedLevelResult
{
    public string LevelName;
    public int LevelNumber;
    public GenerationOutcome Outcome;
    public string Message;
    public int BlockCount;
    public int MoveCount;
    public int ExploredStates;
    public int EstimatedDifficulty;
    public DifficultyTier TargetTier;
    public SolverMove[] Solution = new SolverMove[0];
    public List<LevelBlockData> Blocks = new List<LevelBlockData>();
    public List<LevelTargetData> Targets = new List<LevelTargetData>();
    public List<Vector2Int> BlockedCells = new List<Vector2Int>();
    public LevelData Asset;
    public bool ReplayVerified;
    public int QualityScore;
    public float PlayableRatio;
    public float OccupancyRatio;
    public int MultiCellCount;
    public int ShapeDiversity;
    public int ColorDiversity;
    public int GridWidth;
    public int GridHeight;
    public int IntendedSolutionLength;
    public int IntendedDependencyDepth;
    public int MeasuredDependencyDepth;
    public int DependencyDepth;
    public int InitialBranching;
    public int BranchingPreferred;
    public int BranchingHardLimit;
    public int BranchingScore;
    public int BranchingPenalty;
    public int BranchingBudget;
    public bool BranchingRejected;

    public string OutcomeLabel
    {
        get
        {
            switch (Outcome)
            {
                case GenerationOutcome.Accepted:
                    return "ACCEPTED";
                case GenerationOutcome.RejectedUnsolvable:
                    return "REJECTED (Unsolvable)";
                case GenerationOutcome.RejectedLimitReached:
                    return "REJECTED (Solver limit reached)";
                case GenerationOutcome.RejectedTooEasy:
                    return "REJECTED (Too easy)";
                case GenerationOutcome.RejectedTooHard:
                    return "REJECTED (Too hard)";
                case GenerationOutcome.RejectedInvalid:
                    return "REJECTED (Invalid)";
                case GenerationOutcome.RejectedTrivial:
                    return "REJECTED (Too trivial)";
                case GenerationOutcome.RejectedReplayFailed:
                    return "REJECTED (Replay failed)";
                case GenerationOutcome.RejectedExists:
                    return "SKIPPED (Already exists)";
                case GenerationOutcome.FailedAttempts:
                    return "FAILED (No candidate)";
                default:
                    return Outcome.ToString();
            }
        }
    }
}

internal static class LevelDifficulty
{
    public static DifficultyTier ResolveTier(DifficultyTier setting, float progress)
    {
        if (setting != DifficultyTier.Progressive)
        {
            return setting;
        }

        if (progress < 0.2f)
        {
            return DifficultyTier.Easy;
        }

        if (progress < 0.45f)
        {
            return DifficultyTier.Medium;
        }

        if (progress < 0.75f)
        {
            return DifficultyTier.Hard;
        }

        return DifficultyTier.Expert;
    }

    public static int Estimate(
        SolverResult solver,
        int blockCount,
        int restrictedDirections)
    {
        int solution = solver != null ? solver.MoveCount : 0;
        int explored = solver != null ? solver.ExploredStates : 0;
        int branching = solver != null ? solver.InitialMoveCount : 0;
        int collisions = solver != null ? solver.CollisionStops : 0;
        int targetStops = solver != null ? solver.TargetStops : 0;
        int exploredTerm = 0;
        if (explored > 1)
        {
            exploredTerm = (int)(Mathf.Log(explored) * 6f);
        }

        return solution * 10
            + exploredTerm
            + restrictedDirections * 7
            + collisions * 4
            + targetStops * 3
            + branching * 2
            + blockCount * 5;
    }

    public static bool MatchesTier(
        DifficultyTier tier,
        int score,
        int moveCount,
        int blockCount,
        int minMoves,
        int maxMoves)
    {
        if (moveCount < minMoves || moveCount > maxMoves)
        {
            return false;
        }

        switch (tier)
        {
            case DifficultyTier.Easy:
                return moveCount <= 12 && score <= 120 && blockCount <= 4;
            case DifficultyTier.Medium:
                return moveCount >= 3 && moveCount <= 14 && score >= 25 && score <= 160;
            case DifficultyTier.Hard:
                return moveCount >= 5 && moveCount <= 25 && score >= 50;
            case DifficultyTier.Expert:
                return moveCount >= 7 && score >= 70;
            default:
                return true;
        }
    }

    public static int ChooseBlockCount(System.Random rng, DifficultyTier tier, int minBlocks, int maxBlocks)
    {
        int min = Mathf.Max(1, minBlocks);
        int max = Mathf.Max(min, maxBlocks);
        switch (tier)
        {
            case DifficultyTier.Easy:
                max = Mathf.Min(max, Mathf.Max(min, 3));
                break;
            case DifficultyTier.Medium:
                min = Mathf.Max(min, 3);
                max = Mathf.Min(max, 4);
                if (min > max)
                {
                    min = max;
                }

                break;
            case DifficultyTier.Hard:
                min = Mathf.Max(min, 4);
                break;
            case DifficultyTier.Expert:
                min = Mathf.Max(min, Mathf.Min(max, 5));
                break;
        }

        if (min > max)
        {
            min = max;
        }

        return rng.Next(min, max + 1);
    }

    public static float RestrictedDirectionChance(DifficultyTier tier, MechanicMode mode, float progress)
    {
        if (mode == MechanicMode.Off)
        {
            return 0f;
        }

        if (mode == MechanicMode.On)
        {
            return 0.75f;
        }

        switch (tier)
        {
            case DifficultyTier.Easy:
                return 0.15f;
            case DifficultyTier.Medium:
                return 0.4f;
            case DifficultyTier.Hard:
                return 0.65f;
            default:
                return 0.85f;
        }
    }
}
