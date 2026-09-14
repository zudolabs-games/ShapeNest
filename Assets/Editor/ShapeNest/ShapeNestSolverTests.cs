using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class ShapeNestSolverTests
{
    [MenuItem("Tools/Shape Nest/Run Solver Tests")]
    public static void RunFromMenu()
    {
        string report = RunAll();
        if (report.Contains("FAIL"))
        {
            Debug.LogError(report);
        }
        else
        {
            Debug.Log(report);
        }
    }

    [MenuItem("Tools/Shape Nest/Run Generator Batch Validation")]
    public static void RunGeneratorBatchValidationFromMenu()
    {
        StartGeneratorBatchValidation(20);
    }

    [MenuItem("Tools/Shape Nest/Run Generator Batch Validation (1 per difficulty)")]
    public static void RunGeneratorSmokeValidationFromMenu()
    {
        StartGeneratorBatchValidation(1);
    }

    [MenuItem("Tools/Shape Nest/Run Generator Batch Validation (5 per difficulty)")]
    public static void RunGeneratorSmallValidationFromMenu()
    {
        StartGeneratorBatchValidation(5);
    }

    private static GeneratorBatchRunner activeBatch;

    private static void StartGeneratorBatchValidation(int levelsPerDifficulty)
    {
        if (activeBatch != null)
        {
            Debug.LogWarning("Generator batch validation is already running.");
            return;
        }

        activeBatch = new GeneratorBatchRunner(Mathf.Clamp(levelsPerDifficulty, 1, 20));
        activeBatch.Start();
    }

    private static string BuildGeneratedSignature(GeneratedLevelResult result)
    {
        var builder = new StringBuilder();
        builder.Append(result.GridWidth).Append('x').Append(result.GridHeight).Append('|');
        for (int i = 0; i < result.BlockedCells.Count; i++)
        {
            builder.Append(result.BlockedCells[i].x).Append(',').Append(result.BlockedCells[i].y).Append(';');
        }
        builder.Append('|');
        for (int i = 0; i < result.Blocks.Count; i++)
        {
            LevelBlockData block = result.Blocks[i];
            builder.Append((int)block.shapeType).Append(':').Append(block.gridPosition.x).Append(',').Append(block.gridPosition.y).Append(':');
            for (int c = 0; c < block.cells.Count; c++)
            {
                builder.Append(block.cells[c].localPosition.x).Append(',').Append(block.cells[c].localPosition.y).Append(';');
            }
        }
        return builder.ToString();
    }

    private enum BatchOutcome
    {
        Accepted,
        Unsolvable,
        QualityRejected,
        Duplicate,
        StructuralRejected,
        BudgetExceeded
    }

    private sealed class GeneratorBatchRunner
    {
        private const int MaxAttemptsPerLevel = 1;
        private const int SolverStateBudget = 100000;
        private const double CandidateTimeBudgetSeconds = 0.75d;

        private readonly DifficultyTier[] tiers = { DifficultyTier.Easy, DifficultyTier.Medium, DifficultyTier.Hard, DifficultyTier.Expert };
        private readonly GenerationStyle[] styles = { GenerationStyle.Mixed, GenerationStyle.Dense, GenerationStyle.Corridor, GenerationStyle.Asymmetric };
        private readonly int levelsPerDifficulty;
        private readonly List<BatchRecord> records = new List<BatchRecord>();
        private readonly HashSet<string>[] signatures =
        {
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>()
        };

        private System.Random rng;
        private LevelGeneratorSettings settings;
        private int tierIndex;
        private int levelIndex;
        private int attempts;
        private bool sawUnsolvable;
        private bool sawQualityReject;
        private bool sawStructuralReject;
        private double candidateStartedAt;
        private int lastReportedCompleted = -1;

        public GeneratorBatchRunner(int levelsPerDifficulty)
        {
            this.levelsPerDifficulty = levelsPerDifficulty;
        }

        public void Start()
        {
            tierIndex = 0;
            levelIndex = 0;
            BeginTier();
            EditorApplication.update += Tick;
            Debug.Log($"Generator batch validation started: {levelsPerDifficulty} per difficulty, 4 difficulties.");
        }

        private void BeginTier()
        {
            DifficultyTier tier = tiers[tierIndex];
            settings = new LevelGeneratorSettings
            {
                Seed = 928374 + tierIndex * 1000,
                LevelCount = levelsPerDifficulty,
                BoardWidth = 5,
                BoardHeight = 7,
                MinBlocks = 5,
                MaxBlocks = 9,
                MaxAttemptsPerLevel = MaxAttemptsPerLevel,
                MinSolutionMoves = tier == DifficultyTier.Easy ? 1 : tier == DifficultyTier.Medium ? 3 : tier == DifficultyTier.Hard ? 5 : 7,
                MaxSolutionMoves = 100,
                MaxSolverDepth = 100,
                MaxSolverStates = SolverStateBudget,
                Difficulty = tier,
                FixedDirections = MechanicMode.Progressive,
                Collisions = MechanicMode.Progressive,
                TargetStopping = MechanicMode.Progressive,
                Style = styles[tierIndex],
                VaryBoardSize = true
            };
            rng = new System.Random(settings.Seed);
            attempts = 0;
            sawUnsolvable = false;
            sawQualityReject = false;
            sawStructuralReject = false;
        }

        private void Tick()
        {
            if (tierIndex >= tiers.Length)
            {
                Finish();
                return;
            }

            candidateStartedAt = EditorApplication.timeSinceStartup;
            float progress = levelsPerDifficulty <= 1 ? 1f : levelIndex / (float)(levelsPerDifficulty - 1);
            string levelName = "Batch" + tiers[tierIndex] + levelIndex;
            GeneratedLevelResult result = LevelGenerator.TryCandidate(rng, settings, tiers[tierIndex], progress, levelName);
            double elapsed = EditorApplication.timeSinceStartup - candidateStartedAt;
            attempts++;

            if (elapsed > CandidateTimeBudgetSeconds || result.Outcome == GenerationOutcome.RejectedLimitReached)
            {
                FinishLevel(BatchOutcome.BudgetExceeded, result, elapsed);
            }
            else if (result.Outcome == GenerationOutcome.Accepted)
            {
                ValidateAccepted(result, elapsed);
            }
            else
            {
                TrackRejection(result.Outcome);
                if (attempts >= MaxAttemptsPerLevel)
                {
                    FinishLevel(ClassifyExhaustedAttempts(), result, elapsed);
                }
            }

            UpdateProgress();
        }

        private void ValidateAccepted(GeneratedLevelResult result, double elapsed)
        {
            LevelEditorValidationResult validation = LevelEditorValidation.Validate(
                result.LevelName, result.Blocks, result.Targets, result.GridWidth, result.GridHeight);
            if (!validation.IsValid)
            {
                FinishLevel(BatchOutcome.StructuralRejected, result, elapsed);
                return;
            }

            string signature = BuildGeneratedSignature(result);
            if (!signatures[tierIndex].Add(signature))
            {
                FinishLevel(BatchOutcome.Duplicate, result, elapsed);
                return;
            }

            FinishLevel(BatchOutcome.Accepted, result, elapsed);
        }

        private void TrackRejection(GenerationOutcome outcome)
        {
            if (outcome == GenerationOutcome.RejectedUnsolvable || outcome == GenerationOutcome.RejectedReplayFailed)
            {
                sawUnsolvable = true;
            }
            else if (outcome == GenerationOutcome.RejectedInvalid)
            {
                sawStructuralReject = true;
            }
            else if (outcome == GenerationOutcome.RejectedTooEasy || outcome == GenerationOutcome.RejectedTooHard || outcome == GenerationOutcome.RejectedTrivial)
            {
                sawQualityReject = true;
            }
        }

        private BatchOutcome ClassifyExhaustedAttempts()
        {
            if (sawStructuralReject) return BatchOutcome.StructuralRejected;
            if (sawQualityReject) return BatchOutcome.QualityRejected;
            if (sawUnsolvable) return BatchOutcome.Unsolvable;
            return BatchOutcome.BudgetExceeded;
        }

        private void FinishLevel(BatchOutcome outcome, GeneratedLevelResult result, double elapsed)
        {
            records.Add(new BatchRecord
            {
                Tier = tiers[tierIndex],
                LevelIndex = levelIndex + 1,
                Seed = settings.Seed,
                Style = settings.Style,
                Outcome = outcome,
                Attempts = attempts,
                ElapsedMilliseconds = elapsed * 1000d,
                Result = result
            });
            levelIndex++;
            if (levelIndex >= levelsPerDifficulty)
            {
                tierIndex++;
                levelIndex = 0;
                if (tierIndex < tiers.Length) BeginTier();
            }
            else
            {
                attempts = 0;
                sawUnsolvable = false;
                sawQualityReject = false;
                sawStructuralReject = false;
            }
        }

        private void UpdateProgress()
        {
            int completed = tierIndex * levelsPerDifficulty + levelIndex;
            int total = tiers.Length * levelsPerDifficulty;
            string message = $"{tiers[Mathf.Min(tierIndex, tiers.Length - 1)]} {Mathf.Min(levelIndex, levelsPerDifficulty)}/{levelsPerDifficulty} | " +
                $"total {completed}/{total}";
            EditorUtility.DisplayProgressBar("Shape Nest Generator Validation", message, completed / (float)total);
            if (completed != lastReportedCompleted || completed == total)
            {
                lastReportedCompleted = completed;
                Debug.Log(message);
            }
        }

        private void Finish()
        {
            EditorApplication.update -= Tick;
            EditorUtility.ClearProgressBar();
            Debug.Log(BuildReport());
            activeBatch = null;
        }

        private string BuildReport()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Generator batch validation complete: {records.Count} levels");
            for (int i = 0; i < tiers.Length; i++)
            {
                DifficultyTier tier = tiers[i];
                int accepted = Count(tier, BatchOutcome.Accepted);
                builder.AppendLine($"{tier}: {accepted}/{levelsPerDifficulty} accepted, rejected={levelsPerDifficulty - accepted}");
                builder.AppendLine($"  Accepted={accepted} Unsolvable={Count(tier, BatchOutcome.Unsolvable)} QualityRejected={Count(tier, BatchOutcome.QualityRejected)} Duplicate={Count(tier, BatchOutcome.Duplicate)} StructuralRejected={Count(tier, BatchOutcome.StructuralRejected)} Timeout/BudgetExceeded={Count(tier, BatchOutcome.BudgetExceeded)}");
                AppendAverages(builder, tier);
            }

            builder.AppendLine("Per-level statistics:");
            for (int i = 0; i < records.Count; i++) builder.AppendLine(records[i].ToString());
            return builder.ToString();
        }

        private void AppendAverages(StringBuilder builder, DifficultyTier tier)
        {
            var accepted = new List<BatchRecord>();
            for (int i = 0; i < records.Count; i++) if (records[i].Tier == tier && records[i].Outcome == BatchOutcome.Accepted) accepted.Add(records[i]);
            if (accepted.Count == 0)
            {
                builder.AppendLine("  Averages: no accepted levels");
                return;
            }

            float area = 0f, blocked = 0f, pieces = 0f, targets = 0f, multiPieces = 0f, multiTargets = 0f, density = 0f, shapes = 0f, colors = 0f, fixedDirections = 0f, ice = 0f, moves = 0f, states = 0f, quality = 0f;
            for (int i = 0; i < accepted.Count; i++)
            {
                GeneratedLevelResult result = accepted[i].Result;
                area += result.GridWidth * result.GridHeight;
                blocked += result.BlockedCells.Count;
                pieces += result.BlockCount;
                targets += result.Targets.Count;
                multiPieces += CountMultiCell(result.Blocks);
                multiTargets += CountMultiCellTargets(result.Targets);
                density += result.OccupancyRatio;
                shapes += result.ShapeDiversity;
                colors += result.ColorDiversity;
                fixedDirections += CountFixedDirections(result.Blocks);
                ice += CountIce(result.Blocks);
                moves += result.MoveCount;
                states += result.ExploredStates;
                quality += result.QualityScore;
            }

            float count = accepted.Count;
            builder.AppendLine($"  Averages: area={area / count:0.0} blocked={blocked / count:0.0} pieces={pieces / count:0.0} targets={targets / count:0.0} multiPieces={multiPieces / count:0.0} multiTargets={multiTargets / count:0.0} density={density / count:P0} shapes={shapes / count:0.0} colors={colors / count:0.0} fixed={fixedDirections / count:0.0} ice={ice / count:0.0} moves={moves / count:0.0} states={states / count:0.0} quality={quality / count:0.0}");
        }

        private int Count(DifficultyTier tier, BatchOutcome outcome)
        {
            int count = 0;
            for (int i = 0; i < records.Count; i++) if (records[i].Tier == tier && records[i].Outcome == outcome) count++;
            return count;
        }

        private static int CountMultiCell(List<LevelBlockData> blocks)
        {
            int count = 0;
            for (int i = 0; i < blocks.Count; i++) if (ShapeLayout.EffectiveCount(blocks[i].cells) > 1) count++;
            return count;
        }

        private static int CountMultiCellTargets(List<LevelTargetData> targets)
        {
            int count = 0;
            for (int i = 0; i < targets.Count; i++) if (ShapeLayout.EffectiveCount(targets[i].cells) > 1) count++;
            return count;
        }

        private static int CountFixedDirections(List<LevelBlockData> blocks)
        {
            int count = 0;
            for (int i = 0; i < blocks.Count; i++) if (blocks[i].moveDirection != MoveDirection.Any) count++;
            return count;
        }

        private static int CountIce(List<LevelBlockData> blocks)
        {
            int count = 0;
            for (int i = 0; i < blocks.Count; i++) if (blocks[i].hasIce) count++;
            return count;
        }
    }

    private sealed class BatchRecord
    {
        public DifficultyTier Tier;
        public int LevelIndex;
        public int Seed;
        public GenerationStyle Style;
        public BatchOutcome Outcome;
        public int Attempts;
        public double ElapsedMilliseconds;
        public GeneratedLevelResult Result;

        public override string ToString()
        {
            int multiPieces = Result != null ? CountMultiCell(Result.Blocks) : 0;
            int multiTargets = Result != null ? CountMultiCellTargets(Result.Targets) : 0;
            int fixedDirections = Result != null ? CountFixedDirections(Result.Blocks) : 0;
            int ice = Result != null ? CountIce(Result.Blocks) : 0;
            return $"{Tier} {LevelIndex:00} seed={Seed} style={Style} outcome={Outcome} board={Result?.GridWidth}x{Result?.GridHeight} playable={(Result == null ? 0 : Result.BlockedCells.Count == 0 ? Result.GridWidth * Result.GridHeight : Result.GridWidth * Result.GridHeight - Result.BlockedCells.Count)} blocked={Result?.BlockedCells.Count ?? 0} pieces={Result?.BlockCount ?? 0} targets={Result?.Targets.Count ?? 0} multiPieces={multiPieces} multiTargets={multiTargets} density={Result?.OccupancyRatio ?? 0f:P0} shapes={Result?.ShapeDiversity ?? 0} colors={Result?.ColorDiversity ?? 0} fixed={fixedDirections} ice={ice} intended={Result?.IntendedSolutionLength ?? 0} intendedDepth={Result?.IntendedDependencyDepth ?? 0} measuredDepth={Result?.MeasuredDependencyDepth ?? 0} initialBranching={Result?.InitialBranching ?? 0} branchingPreferred={Result?.BranchingPreferred ?? 0} branchingHardLimit={Result?.BranchingHardLimit ?? 0} branchingScore={Result?.BranchingScore ?? 0} branchingPenalty={Result?.BranchingPenalty ?? 0} branchingRejected={(Result != null && Result.BranchingRejected ? "YES" : "NO")} moves={Result?.MoveCount ?? 0} solverStates={Result?.ExploredStates ?? 0} quality={Result?.QualityScore ?? 0} replay={(Result != null && Result.ReplayVerified ? "PASS" : "FAIL")} attempts={Attempts} elapsedMs={ElapsedMilliseconds:0}";
        }
    }

    private static int CountMultiCell(List<LevelBlockData> blocks)
    {
        int count = 0;
        for (int i = 0; i < blocks.Count; i++) if (ShapeLayout.EffectiveCount(blocks[i].cells) > 1) count++;
        return count;
    }

    private static int CountMultiCellTargets(List<LevelTargetData> targets)
    {
        int count = 0;
        for (int i = 0; i < targets.Count; i++) if (ShapeLayout.EffectiveCount(targets[i].cells) > 1) count++;
        return count;
    }

    private static int CountFixedDirections(List<LevelBlockData> blocks)
    {
        int count = 0;
        for (int i = 0; i < blocks.Count; i++) if (blocks[i].moveDirection != MoveDirection.Any) count++;
        return count;
    }

    private static int CountIce(List<LevelBlockData> blocks)
    {
        int count = 0;
        for (int i = 0; i < blocks.Count; i++) if (blocks[i].hasIce) count++;
        return count;
    }

    public static string RunAll()
    {
        var builder = new StringBuilder();
        int passed = 0;
        int failed = 0;

        Check(builder, ref passed, ref failed, "TestLevel solvable", TestLevelSolvable());
        Check(builder, ref passed, ref failed, "Replay matches BFS", TestLevelReplay());
        Check(builder, ref passed, ref failed, "Fixed direction respected", FixedDirectionRespected());
        Check(builder, ref passed, ref failed, "Collision stops before other block", CollisionStopsBeforeBlock());
        Check(builder, ref passed, ref failed, "Matching target enter and stop", MatchingTargetStopsOnCell());
        Check(builder, ref passed, ref failed, "Wrong-shape target stops before", WrongShapeTargetStopsBefore());
        Check(builder, ref passed, ref failed, "Settled blocks cannot move", SettledCannotMove());
        Check(builder, ref passed, ref failed, "All-settled recognized", AllSettledRecognized());
        Check(builder, ref passed, ref failed, "Illegal zero-length move rejected", ZeroLengthMoveRejected());
        Check(builder, ref passed, ref failed, "Reference mask is connected and irregular", ReferenceMaskIsConnectedAndIrregular());


        string autoMatch = ShapeNestAutoMatchTests.RunAll();
        builder.AppendLine();
        builder.Append(autoMatch);
        if (autoMatch.Contains("FAIL"))
        {
            failed++;
        }

        builder.Insert(0, $"Solver tests: {passed} passed, {failed} failed\n");
        return builder.ToString();
    }

    private static void Check(StringBuilder builder, ref int passed, ref int failed, string name, bool ok)
    {
        if (ok)
        {
            passed++;
            builder.AppendLine("PASS  " + name);
        }
        else
        {
            failed++;
            builder.AppendLine("FAIL  " + name);
        }
    }



    private static SolverLevel TestLevel()
    {
        return new SolverLevel
        {
            Width = 5,
            Height = 5,
            InitialBlocks = new[]
            {
                new SolverBlock { X = 1, Y = 2, Shape = ShapeType.Square, MoveDirection = MoveDirection.Any },
                new SolverBlock { X = 3, Y = 2, Shape = ShapeType.Circle, MoveDirection = MoveDirection.Left },
                new SolverBlock { X = 2, Y = 4, Shape = ShapeType.Triangle, MoveDirection = MoveDirection.Down }
            },
            Targets = new[]
            {
                new SolverTarget { X = 4, Y = 2, Shape = ShapeType.Square },
                new SolverTarget { X = 0, Y = 2, Shape = ShapeType.Circle },
                new SolverTarget { X = 2, Y = 0, Shape = ShapeType.Triangle }
            }
        };
    }

    private static bool TestLevelSolvable()
    {
        SolverResult result = ShapeNestSolver.Solve(TestLevel(), SolverLimits.Default);
        return result.IsSolved && result.MoveCount > 0;
    }

    private static bool TestLevelReplay()
    {
        SolverLevel level = TestLevel();
        SolverResult result = ShapeNestSolver.Solve(level, SolverLimits.Default);
        if (!result.IsSolved)
        {
            return false;
        }

        return ShapeNestSolver.Replay(level, ShapeNestSolver.CreateInitialState(level), result.Solution);
    }

    private static bool FixedDirectionRespected()
    {
        var level = new SolverLevel
        {
            Width = 5,
            Height = 5,
            InitialBlocks = new[]
            {
                new SolverBlock { X = 2, Y = 2, Shape = ShapeType.Circle, MoveDirection = MoveDirection.Left }
            },
            Targets = new[]
            {
                new SolverTarget { X = 4, Y = 2, Shape = ShapeType.Circle }
            }
        };

        SolverState start = ShapeNestSolver.CreateInitialState(level);
        SolverState right = ShapeNestSolver.TryMove(level, start, 0, Vector2Int.right, out _, out _);
        SolverState left = ShapeNestSolver.TryMove(level, start, 0, Vector2Int.left, out _, out _);
        return right == null && left != null && left.Blocks[0].X == 0;
    }

    private static bool CollisionStopsBeforeBlock()
    {
        var level = new SolverLevel
        {
            Width = 5,
            Height = 5,
            InitialBlocks = new[]
            {
                new SolverBlock { X = 0, Y = 2, Shape = ShapeType.Square, MoveDirection = MoveDirection.Any },
                new SolverBlock { X = 3, Y = 2, Shape = ShapeType.Circle, MoveDirection = MoveDirection.Any }
            },
            Targets = new SolverTarget[0]
        };

        SolverState start = ShapeNestSolver.CreateInitialState(level);
        SolverState moved = ShapeNestSolver.TryMove(level, start, 0, Vector2Int.right, out bool collision, out _);
        return moved != null && moved.Blocks[0].X == 2 && collision;
    }

    private static bool MatchingTargetStopsOnCell()
    {
        var level = new SolverLevel
        {
            Width = 5,
            Height = 5,
            InitialBlocks = new[]
            {
                new SolverBlock { X = 0, Y = 2, Shape = ShapeType.Square, MoveDirection = MoveDirection.Any }
            },
            Targets = new[]
            {
                new SolverTarget { X = 3, Y = 2, Shape = ShapeType.Square }
            }
        };

        SolverState start = ShapeNestSolver.CreateInitialState(level);
        SolverState moved = ShapeNestSolver.TryMove(level, start, 0, Vector2Int.right, out _, out bool targetStop);
        return moved != null && moved.Blocks[0].X == 3 && moved.Blocks[0].Settled && targetStop;
    }

    private static bool WrongShapeTargetStopsBefore()
    {
        var level = new SolverLevel
        {
            Width = 5,
            Height = 5,
            InitialBlocks = new[]
            {
                new SolverBlock { X = 0, Y = 2, Shape = ShapeType.Square, MoveDirection = MoveDirection.Any }
            },
            Targets = new[]
            {
                new SolverTarget { X = 3, Y = 2, Shape = ShapeType.Circle }
            }
        };

        SolverState start = ShapeNestSolver.CreateInitialState(level);
        SolverState moved = ShapeNestSolver.TryMove(level, start, 0, Vector2Int.right, out _, out bool targetStop);
        return moved != null && moved.Blocks[0].X == 2 && !moved.Blocks[0].Settled && targetStop;
    }

    private static bool SettledCannotMove()
    {
        var level = new SolverLevel
        {
            Width = 5,
            Height = 5,
            InitialBlocks = new[]
            {
                new SolverBlock { X = 2, Y = 2, Shape = ShapeType.Square, MoveDirection = MoveDirection.Any }
            },
            Targets = new[]
            {
                new SolverTarget { X = 2, Y = 2, Shape = ShapeType.Square }
            }
        };

        SolverState start = ShapeNestSolver.CreateInitialState(level);
        if (!start.Blocks[0].Settled)
        {
            return false;
        }

        return ShapeNestSolver.TryMove(level, start, 0, Vector2Int.right, out _, out _) == null;
    }

    private static bool AllSettledRecognized()
    {
        var level = new SolverLevel
        {
            Width = 5,
            Height = 5,
            InitialBlocks = new[]
            {
                new SolverBlock { X = 1, Y = 1, Shape = ShapeType.Square, MoveDirection = MoveDirection.Any },
                new SolverBlock { X = 3, Y = 3, Shape = ShapeType.Circle, MoveDirection = MoveDirection.Any }
            },
            Targets = new[]
            {
                new SolverTarget { X = 1, Y = 1, Shape = ShapeType.Square },
                new SolverTarget { X = 3, Y = 3, Shape = ShapeType.Circle }
            }
        };

        SolverResult result = ShapeNestSolver.Solve(level, SolverLimits.Default);
        return result.IsSolved && result.MoveCount == 0 && ShapeNestSolver.CreateInitialState(level).AllSettled;
    }

    private static bool ZeroLengthMoveRejected()
    {
        var level = new SolverLevel
        {
            Width = 5,
            Height = 5,
            InitialBlocks = new[]
            {
                new SolverBlock { X = 0, Y = 0, Shape = ShapeType.Square, MoveDirection = MoveDirection.Any }
            },
            Targets = new[]
            {
                new SolverTarget { X = 4, Y = 4, Shape = ShapeType.Square }
            }
        };

        SolverState start = ShapeNestSolver.CreateInitialState(level);
        return ShapeNestSolver.TryMove(level, start, 0, Vector2Int.left, out _, out _) == null;
    }

    private static bool ReferenceMaskIsConnectedAndIrregular()
    {
        var mask = ReferenceBoardMaskGenerator.Generate(8, 8, DifficultyTier.Medium, new System.Random(1234), 0, GenerationStyle.Mixed);
        var cells = new HashSet<Vector2Int>(mask);
        if (cells.Count < 20 || cells.Count > 60)
        {
            return false;
        }

        var queue = new Queue<Vector2Int>();
        var visited = new HashSet<Vector2Int>();
        Vector2Int seed = default;
        foreach (Vector2Int cell in cells)
        {
            seed = cell;
            break;
        }

        queue.Enqueue(seed);
        visited.Add(seed);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            Vector2Int[] neighbors =
            {
                current + Vector2Int.up,
                current + Vector2Int.down,
                current + Vector2Int.left,
                current + Vector2Int.right
            };

            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2Int neighbor = neighbors[i];
                if (cells.Contains(neighbor) && visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return visited.Count == cells.Count;
    }
}
