using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

internal sealed class LevelGeneratorSettings
{
    public int Seed = 12345;
    public int LevelCount = 20;
    public int BoardWidth = 5;
    public int BoardHeight = 5;
    public int MinBlocks = 3;
    public int MaxBlocks = 5;
    public int StartingLevelNumber = 1;
    public int LevelNumber;
    public int MaxAttemptsPerLevel = 1000;
    public int MinSolutionMoves = 1;
    public int MaxSolutionMoves = 100;
    public int MaxSolverDepth = 100;
    public int MaxSolverStates = 100000;
    public DifficultyTier Difficulty = DifficultyTier.Progressive;
    public MechanicMode FixedDirections = MechanicMode.Progressive;
    public MechanicMode Collisions = MechanicMode.Progressive;
    public MechanicMode TargetStopping = MechanicMode.Progressive;
    public ExistingAssetPolicy ExistingPolicy = ExistingAssetPolicy.Skip;
    public GenerationStyle Style = GenerationStyle.Mixed;
    public bool VaryBoardSize = true;
    public bool AllowImmediateSettlement;
}

internal static class LevelGenerator
{
    private static readonly ShapeType[] Shapes = (ShapeType[])Enum.GetValues(typeof(ShapeType));
    private static readonly MoveDirection[] FixedDirections =
    {
        MoveDirection.Up,
        MoveDirection.Down,
        MoveDirection.Left,
        MoveDirection.Right
    };
    private static readonly ShapeColor[] Colors =
    {
        ShapeColor.Yellow,
        ShapeColor.Cyan,
        ShapeColor.Pink,
        ShapeColor.Purple,
        ShapeColor.Green,
        ShapeColor.Red,
        ShapeColor.Orange,
        ShapeColor.White,
        ShapeColor.Blue
    };

    public static GeneratedLevelResult TryCandidate(
        Random rng,
        LevelGeneratorSettings settings,
        DifficultyTier tier,
        float progress,
        string levelName,
        int levelNumber = 0)
    {
        return BuildCandidate(rng, settings, tier, progress, levelName, levelNumber);
    }

    public static GeneratedLevelResult TryGenerateOne(
        Random rng,
        LevelGeneratorSettings settings,
        DifficultyTier tier,
        float progress,
        string levelName,
        int levelNumber = 0)
    {
        int attempts = Mathf.Max(1, settings.MaxAttemptsPerLevel);
        GeneratedLevelResult lastReject = null;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            GeneratedLevelResult candidate = BuildCandidate(rng, settings, tier, progress, levelName, levelNumber);
            if (candidate.Outcome == GenerationOutcome.Accepted)
            {
                return candidate;
            }

            lastReject = candidate;
        }

        if (lastReject == null)
        {
            lastReject = new GeneratedLevelResult
            {
                LevelName = levelName,
                Outcome = GenerationOutcome.FailedAttempts,
                TargetTier = tier,
                LevelNumber = levelNumber,
                Message = "No candidates were produced."
            };
        }
        else
        {
            lastReject.Outcome = GenerationOutcome.FailedAttempts;
            lastReject.Message = $"Could not find an accepted candidate in {attempts} attempts. Last: {lastReject.OutcomeLabel}";
        }

        return lastReject;
    }

    private static GeneratedLevelResult BuildCandidate(
        Random rng,
        LevelGeneratorSettings settings,
        DifficultyTier tier,
        float progress,
        string levelName,
        int levelNumber = 0)
    {
        if (levelNumber <= 0)
        {
            if (settings != null && settings.LevelNumber > 0)
            {
                levelNumber = settings.LevelNumber;
            }
            else if (!string.IsNullOrEmpty(levelName))
            {
                string digits = System.Text.RegularExpressions.Regex.Match(levelName, @"\d+").Value;
                if (int.TryParse(digits, out int parsed))
                {
                    levelNumber = parsed;
                }
            }

            if (levelNumber <= 0 && settings != null)
            {
                levelNumber = settings.StartingLevelNumber;
            }
        }

        var result = new GeneratedLevelResult
        {
            LevelName = levelName,
            TargetTier = tier,
            LevelNumber = levelNumber
        };

        int width = Mathf.Max(1, settings.BoardWidth);
        int height = Mathf.Max(1, settings.BoardHeight);
        if (levelNumber > 0 && levelNumber <= 5)
        {
            switch (levelNumber)
            {
                case 1: width = 4; height = 4; break;
                case 2: width = 4; height = 4; break;
                case 3: width = 4; height = 5; break;
                case 4: width = 5; height = 5; break;
                case 5: width = 5; height = 5; break;
            }
        }
        else if (settings.VaryBoardSize)
        {
            int dimensionVariation = settings.Style == GenerationStyle.Corridor || settings.Style == GenerationStyle.Asymmetric ? 2 : 1;
            width = Mathf.Max(3, width + rng.Next(-dimensionVariation, dimensionVariation + 1));
            height = Mathf.Max(3, height + rng.Next(-dimensionVariation, dimensionVariation + 1));
        }
        result.GridWidth = width;
        result.GridHeight = height;
        // Phase 85B: level-number drives generation style for the board mask.
        // Style controls board *shape*; piece composition is controlled separately below.
        GenerationStyle effectiveStyle = settings.Style;
        if (levelNumber >= 6 && levelNumber <= 7)
        {
            effectiveStyle = GenerationStyle.Mixed;        // readable rectangular/L layouts
        }
        else if (levelNumber >= 8 && levelNumber <= 10)
        {
            effectiveStyle = GenerationStyle.Asymmetric;   // L/U/T/stepped
        }
        else if (levelNumber >= 11 && levelNumber <= 13)
        {
            effectiveStyle = GenerationStyle.Dense;        // chamber + corridor
        }
        else if (levelNumber >= 14 && levelNumber <= 15)
        {
            effectiveStyle = GenerationStyle.Chain;        // asymmetric + narrow
        }
        var playableMask = ReferenceBoardMaskGenerator.Generate(width, height, tier, rng, levelNumber, effectiveStyle);
        int playableCount = playableMask.Count;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!playableMask.Contains(cell))
                {
                    result.BlockedCells.Add(cell);
                }
            }
        }
        if (playableCount < 4)
        {
            result.Outcome = GenerationOutcome.RejectedInvalid;
            result.Message = "Playable mask was too small.";
            return result;
        }

        int blockCount = ChooseBlockCount(rng, settings, tier, playableCount, levelNumber);
// Use the effective style for subsequent layout generation

        if (blockCount > playableCount)
        {
            result.Outcome = GenerationOutcome.RejectedInvalid;
            result.Message = "Block count exceeds playable board size.";
            return result;
        }

        var usedBlockCells = new HashSet<Vector2Int>();
        var usedTargetCells = new HashSet<Vector2Int>();
        var blocks = new List<LevelBlockData>(blockCount);
        var targets = new List<LevelTargetData>(blockCount);

        bool preferSharedRow = ShouldUseCollisions(tier, settings.Collisions);

        ShapeType[] shapes = AssignShapes(rng, blockCount, tier);
        ShapeColor[] colors = AssignColors(rng, blockCount);
        var targetLayouts = new List<List<ShapeCellData>>(blockCount);
        var targetAnchors = new Vector2Int[blockCount];

        for (int i = 0; i < blockCount; i++)
        {
            List<ShapeCellData> layout = CreateLayout(rng, effectiveStyle, tier, shapes[i], levelNumber);
            Vector2Int anchor = PickFootprintAnchor(rng, playableMask, layout, usedTargetCells, settings.Style);
            if (anchor == InvalidCell)
            {
                result.Outcome = GenerationOutcome.RejectedInvalid;
                result.Message = "Could not fit target footprints inside the playable mask.";
                return result;
            }

            targetLayouts.Add(layout);
            targetAnchors[i] = anchor;
            AddFootprint(usedTargetCells, anchor, layout);
        }

        Vector2Int[] blockCells = new Vector2Int[blockCount];
        int[] intendedOrder = CreateIntendedOrder(rng, blockCount);
        for (int order = blockCount - 1; order >= 0; order--)
        {
            int i = intendedOrder[order];
            blockCells[i] = PickSolutionAwareBlockAnchor(
                rng,
                playableMask,
                targetAnchors[i],
                targetLayouts[i],
                usedBlockCells,
                usedTargetCells,
                preferSharedRow,
                effectiveStyle,
                tier,
                true);
            if (blockCells[i] == InvalidCell)
            {
                result.Outcome = GenerationOutcome.RejectedInvalid;
                result.Message = "Could not fit block footprints inside the playable mask.";
                return result;
            }
            AddFootprint(usedBlockCells, blockCells[i], targetLayouts[i]);
        }

        // Decide which piece indices will be nested (ShapeInShape).
        // Nested pieces must be single-cell (footprint 1); multi-cell nested is not supported.
        bool[] isNested = DecideNested(rng, blockCount, targetLayouts, levelNumber);

        if (levelNumber >= 6 && levelNumber <= 15)
        {
            int nestedPiecesCount = 0;
            int multiCellPiecesCount = 0;
            for (int i = 0; i < blockCount; i++) {
                if (isNested[i]) nestedPiecesCount++;
                if (targetLayouts[i].Count > 1) multiCellPiecesCount++;
            }

            int idx = levelNumber - 6;

            // Nested limits
            int[] minNested = { 0, 1, 1, 1, 2, 2, 2, 2, 2, 2 };
            int[] maxNested = { 1, 1, 2, 2, 4, 4, 5, 5, 5, 6 };

            if (nestedPiecesCount < minNested[idx] || nestedPiecesCount > maxNested[idx]) {
                result.Outcome = GenerationOutcome.RejectedInvalid;
                result.Message = "Nested count out of range.";
                return result;
            }

            // Multi-cell limits
            int[] minMulti = { 1, 1, 2, 2, 3, 3, 3, 4, 4, 4 };
            int[] maxMulti = { 1, 2, 3, 4, 4, 5, 5, 6, 6, 7 };

            if (multiCellPiecesCount < minMulti[idx] || multiCellPiecesCount > maxMulti[idx]) {
                result.Outcome = GenerationOutcome.RejectedInvalid;
                result.Message = "Multi-cell piece count out of required range.";
                return result;
            }
        }

        // Pre-compute outer shapes for nested pieces so block and target use the same outer.
        ShapeType[] outerShapes = new ShapeType[blockCount];
        for (int i = 0; i < blockCount; i++)
            outerShapes[i] = isNested[i] ? PickOuterShape(rng, shapes[i]) : shapes[i];

        for (int i = 0; i < blockCount; i++)
        {
            List<ShapeCellData> blockCells2 = BuildBlockCells(
                targetLayouts[i], colors[i], shapes[i], isNested[i], outerShapes[i]);
            blocks.Add(new LevelBlockData
            {
                shapeType = shapes[i],
                moveDirection = MoveDirection.Any,
                gridPosition = blockCells[i],
                cells = blockCells2,
                composition = isNested[i] ? PieceComposition.ShapeInShape : PieceComposition.Simple,
                outerShape = outerShapes[i],
                hasIce = ShouldUseIce(rng, settings.Style, tier, levelNumber),
                iceDurability = 1
            });
        }

        for (int i = 0; i < blockCount; i++)
        {
            List<ShapeCellData> targetCells2 = BuildTargetCells(
                targetLayouts[i], colors[i], shapes[i], isNested[i], outerShapes[i]);
            targets.Add(new LevelTargetData
            {
                shapeType = shapes[i],
                gridPosition = targetAnchors[i],
                cells = targetCells2,
                composition = isNested[i] ? PieceComposition.ShapeInShape : PieceComposition.Simple,
                outerShape = outerShapes[i]
            });
        }

        result.Blocks = blocks;
        result.Targets = targets;
        result.BlockCount = blockCount;
        result.IntendedSolutionLength = blockCount;
        result.IntendedDependencyDepth = 1;
        result.MeasuredDependencyDepth = EstimateDependencyDepth(blockCells, targetAnchors);
        result.DependencyDepth = result.MeasuredDependencyDepth;

        LevelEditorValidationResult validation = LevelEditorValidation.Validate(levelName, blocks, targets, width, height);
        if (!validation.IsValid)
        {
            result.Outcome = GenerationOutcome.RejectedInvalid;
            result.Message = validation.Errors.Count > 0 ? validation.Errors[0] : "Structural validation failed.";
            return result;
        }

        if (!settings.AllowImmediateSettlement && CountImmediateSettlements(blocks, targets) > 0)
        {
            result.Outcome = GenerationOutcome.RejectedTrivial;
            result.Message = "A block starts on its matching target.";
            return result;
        }

        SolverLevel solverLevel = ShapeNestSolver.FromLists(width, height, blocks, targets, result.BlockedCells);
        result.InitialBranching = CountInitialLegalMoves(solverLevel);
        result.BranchingPreferred = BranchingPreferred(tier);
        result.BranchingHardLimit = BranchingHardLimit(tier);
        result.BranchingBudget = result.BranchingHardLimit;
        result.BranchingPenalty = BranchingPenalty(result.InitialBranching, result.BranchingPreferred);
        result.BranchingScore = Mathf.Max(0, 20 - result.BranchingPenalty);
        if (result.InitialBranching > result.BranchingHardLimit)
        {
            result.BranchingRejected = true;
            result.Outcome = GenerationOutcome.RejectedTooEasy;
            result.Message = $"Initial branching {result.InitialBranching} exceeds hard limit {result.BranchingHardLimit}.";
            return result;
        }

        var limits = new SolverLimits
        {
            MaxDepth = settings.MaxSolverDepth,
            MaxStates = settings.MaxSolverStates
        };
        SolverResult solver = ShapeNestSolver.Solve(solverLevel, limits);
        result.MoveCount = solver.MoveCount;
        result.ExploredStates = solver.ExploredStates;
        result.Solution = solver.Solution;
        result.ReplayVerified = solver.ReplayVerified;
        result.InitialBranching = solver.InitialMoveCount;
        result.EstimatedDifficulty = LevelDifficulty.Estimate(solver, blockCount, 0);
        result.MultiCellCount = CountMultiCell(blocks);
        result.ShapeDiversity = Diversity(blocks, false);
        result.ColorDiversity = Diversity(blocks, true);
        result.PlayableRatio = playableCount / (float)(width * height);
        result.OccupancyRatio = OccupiedCellCount(blocks) / (float)Mathf.Max(1, playableCount);
        result.QualityScore = CalculateQuality(result, tier, settings.Style);

        if (solver.Status == SolverStatus.LimitReached)
        {
            result.Outcome = GenerationOutcome.RejectedLimitReached;
            result.Message = "Solver limit reached.";
            return result;
        }

        if (solver.Status == SolverStatus.ReplayFailed || !solver.ReplayVerified)
        {
            result.Outcome = GenerationOutcome.RejectedReplayFailed;
            result.Message = solver.Error ?? "Solution replay failed.";
            return result;
        }

        if (solver.Status != SolverStatus.Solved)
        {
            result.Outcome = GenerationOutcome.RejectedUnsolvable;
            result.Message = "Unsolvable.";
            return result;
        }

        if (solver.MoveCount == 0)
        {
            result.Outcome = GenerationOutcome.RejectedTrivial;
            result.Message = "Already solved at start.";
            return result;
        }

        if (result.QualityScore < MinimumQuality(tier))
        {
            result.Outcome = GenerationOutcome.RejectedTooEasy;
            result.Message = "Reference-style quality score was below the requested tier.";
            return result;
        }

        if (tier != DifficultyTier.Easy && solver.MoveCount <= 1)
        {
            result.Outcome = GenerationOutcome.RejectedTooEasy;
            result.Message = "Solution is only 1 move.";
            return result;
        }

        if (tier == DifficultyTier.Hard || tier == DifficultyTier.Expert)
        {
            if (solver.CollisionStops == 0 && solver.TargetStops <= 1)
            {
                result.Outcome = GenerationOutcome.RejectedTooEasy;
                result.Message = "Not enough interaction for this difficulty.";
                return result;
            }
        }

        if (levelNumber > 0 && levelNumber <= 5)
        {
            int minMoves = 2;
            int maxMoves = 3;
            switch (levelNumber)
            {
                case 1: minMoves = 2; maxMoves = 3; break;
                case 2: minMoves = 3; maxMoves = 4; break;
                case 3: minMoves = 4; maxMoves = 5; break;
                case 4: minMoves = 4; maxMoves = 6; break;
                case 5: minMoves = 5; maxMoves = 7; break;
            }

            if (solver.MoveCount < minMoves || solver.MoveCount > maxMoves)
            {
                bool tooEasy = solver.MoveCount < minMoves;
                result.Outcome = tooEasy ? GenerationOutcome.RejectedTooEasy : GenerationOutcome.RejectedTooHard;
                result.Message = tooEasy
                    ? $"Tutorial Level {levelNumber}: Solution of {solver.MoveCount} moves is below target ({minMoves}–{maxMoves})."
                    : $"Tutorial Level {levelNumber}: Solution of {solver.MoveCount} moves is above target ({minMoves}–{maxMoves}).";
                return result;
            }
        }
        else if (!LevelDifficulty.MatchesTier(
                tier,
                result.EstimatedDifficulty,
                solver.MoveCount,
                blockCount,
                settings.MinSolutionMoves,
                settings.MaxSolutionMoves))
        {
            bool tooEasy = solver.MoveCount < 3 || result.EstimatedDifficulty < 20;
            result.Outcome = tooEasy ? GenerationOutcome.RejectedTooEasy : GenerationOutcome.RejectedTooHard;
            result.Message = tooEasy ? "Below requested difficulty." : "Above requested difficulty.";
            return result;
        }

        result.Outcome = GenerationOutcome.Accepted;
        result.Message = "Solvable and verified.";
        return result;
    }

    private static readonly Vector2Int InvalidCell = new Vector2Int(int.MinValue, int.MinValue);

    private static int ChooseBlockCount(Random rng, LevelGeneratorSettings settings, DifficultyTier tier, int playableCount, int levelNumber = 0)
    {
        // Tutorial levels 1-5
        if (levelNumber > 0 && levelNumber <= 5)
        {
            switch (levelNumber)
            {
                case 1: return rng.Next(2, 4); // 2–3
                case 2: return 3;               // 3
                case 3: return rng.Next(3, 5); // 3–4
                case 4: return 4;               // 4
                case 5: return rng.Next(4, 6); // 4–5
            }
        }

        // Phase 85B: explicit block count per level 6-15
        if (levelNumber >= 6 && levelNumber <= 15)
        {
            int[] minCounts = { 3, 4, 4, 5, 5, 5, 5, 6, 6, 6 }; // L6-L15
            int[] maxCounts = { 4, 5, 5, 6, 6, 6, 7, 7, 7, 8 };
            int idx = Mathf.Clamp(levelNumber - 6, 0, 9);
            int lo = Mathf.Min(minCounts[idx], playableCount / 2);
            int hi = Mathf.Min(maxCounts[idx], playableCount / 2);
            lo = Mathf.Max(1, lo);
            hi = Mathf.Max(lo, hi);
            return rng.Next(lo, hi + 1);
        }

        int minimum = Mathf.Max(1, settings.MinBlocks);
        int maximum = Mathf.Max(minimum, settings.MaxBlocks);
        int densityMinimum = tier == DifficultyTier.Easy ? 40 : tier == DifficultyTier.Medium ? 50 : tier == DifficultyTier.Hard ? 60 : 65;
        int densityMaximum = tier == DifficultyTier.Easy ? 60 : tier == DifficultyTier.Medium ? 70 : tier == DifficultyTier.Hard ? 80 : 85;
        int densityCount = Mathf.CeilToInt(playableCount * Mathf.Lerp(densityMinimum, densityMaximum, (float)rng.NextDouble()) / 100f);
        maximum = Mathf.Min(maximum, Mathf.Max(minimum, densityCount));
        minimum = Mathf.Min(minimum, maximum);
        return rng.Next(minimum, maximum + 1);
    }

    // Per-level multi-cell piece probability (index 0 = L6, index 9 = L15)
    private static readonly double[] LevelMultiChance  = { 0.30, 0.40, 0.50, 0.55, 0.55, 0.65, 0.65, 0.70, 0.72, 0.75 };
    // Per-level 2x2 probability given that a piece is already multi-cell
    private static readonly double[] Level2x2Chance    = { 0.00, 0.00, 0.05, 0.08, 0.10, 0.15, 0.20, 0.22, 0.25, 0.28 };

    private static List<ShapeCellData> CreateLayout(Random rng, GenerationStyle style, DifficultyTier tier, ShapeType shape, int levelNumber = 0)
    {
        bool isTutorial = levelNumber > 0 && levelNumber <= 5;
        var locals = new List<Vector2Int> { Vector2Int.zero };

        if (!isTutorial)
        {
            double multiChance;
            double chance2x2;
            if (levelNumber >= 6 && levelNumber <= 15)
            {
                int idx = Mathf.Clamp(levelNumber - 6, 0, 9);
                multiChance = LevelMultiChance[idx];
                chance2x2   = Level2x2Chance[idx];
            }
            else
            {
                // Fallback for levels > 15
                multiChance = style == GenerationStyle.MultiCell ? 0.80 : style == GenerationStyle.Dense ? 0.50 : tier == DifficultyTier.Easy ? 0.15 : 0.35;
                chance2x2   = tier >= DifficultyTier.Hard ? 0.25 : 0.10;
            }

            if (rng.NextDouble() < multiChance)
            {
                if (rng.NextDouble() < chance2x2)
                {
                    // 2x2 square footprint
                    locals.Add(Vector2Int.right);
                    locals.Add(Vector2Int.up);
                    locals.Add(new Vector2Int(1, 1));
                }
                else
                {
                    // Domino: horizontal or vertical
                    locals.Add(rng.Next(2) == 0 ? Vector2Int.right : Vector2Int.up);
                }
            }
        }

        var layout = new List<ShapeCellData>(locals.Count);
        for (int i = 0; i < locals.Count; i++)
        {
            layout.Add(new ShapeCellData { localPosition = locals[i], shapeType = shape, outerColor = ShapeColor.Default });
        }
        return layout;
    }

    // -----------------------------------------------------------------------
    // Phase 85B: Nested piece helpers
    // -----------------------------------------------------------------------

    // Probability that a *single-cell* piece is nested (ShapeInShape), by level.
    // index 0 = L6 … index 9 = L15
    private static readonly double[] LevelNestedChance = { 0.10, 0.25, 0.35, 0.40, 0.50, 0.55, 0.55, 0.60, 0.65, 0.70 };

    /// <summary>
    /// Decides which piece indices are nested. Only single-cell pieces may be nested.
    /// </summary>
    private static bool[] DecideNested(Random rng, int blockCount, List<List<ShapeCellData>> layouts, int levelNumber)
    {
        bool[] nested = new bool[blockCount];
        if (levelNumber < 6 || levelNumber > 15) return nested;
        int idx = Mathf.Clamp(levelNumber - 6, 0, 9);
        double chance = LevelNestedChance[idx];
        for (int i = 0; i < blockCount; i++)
        {
            // Only single-cell pieces can be nested
            if (layouts[i].Count == 1 && rng.NextDouble() < chance)
                nested[i] = true;
        }
        return nested;
    }

    /// <summary>
    /// Picks an outer shape different from the inner shape when possible.
    /// Uses a seeded deterministic value — note: we call this twice (block + target)
    /// so we need a stable shape, not rng-derived. We derive it from the inner shape index.
    /// </summary>
    private static ShapeType PickOuterShape(Random rng, ShapeType innerShape)
    {
        // Pick a different shape from the available types
        int inner = (int)innerShape;
        int total = Shapes.Length;
        if (total <= 1) return innerShape;
        int offset = 1 + rng.Next(total - 1);
        return (ShapeType)((inner + offset) % total);
    }

    /// <summary>
    /// Builds the ShapeCellData list for a block, including nested inner shape data.
    /// For nested pieces: cell.shapeType = outerShape, cell.innerShapes[0] = innerShape (original).
    /// </summary>
    private static List<ShapeCellData> BuildBlockCells(
        List<ShapeCellData> layout,
        ShapeColor color,
        ShapeType innerShape,
        bool isNested,
        ShapeType outerShape)
    {
        var copy = new List<ShapeCellData>(layout.Count);
        for (int i = 0; i < layout.Count; i++)
        {
            var cell = new ShapeCellData
            {
                localPosition = layout[i].localPosition,
                outerColor = color,
                innerShapes = new List<ShapeType>(),
                innerShapeColors = new List<ShapeColor>()
            };
            if (isNested && i == 0)
            {
                // Outer layer = outer shape; inner layer = original inner shape
                cell.shapeType = outerShape;
                cell.innerShapes.Add(innerShape);
                cell.innerShapeColors.Add(color);
            }
            else
            {
                cell.shapeType = innerShape;
            }
            copy.Add(cell);
        }
        return copy;
    }

    /// <summary>
    /// Builds the ShapeCellData list for a target matching a nested block.
    /// Target must expose the outer shape first (matching the block's current outer layer).
    /// </summary>
    private static List<ShapeCellData> BuildTargetCells(
        List<ShapeCellData> layout,
        ShapeColor color,
        ShapeType innerShape,
        bool isNested,
        ShapeType outerShape)
    {
        var copy = new List<ShapeCellData>(layout.Count);
        for (int i = 0; i < layout.Count; i++)
        {
            var cell = new ShapeCellData
            {
                localPosition = layout[i].localPosition,
                outerColor = color,
                innerShapes = new List<ShapeType>(),
                innerShapeColors = new List<ShapeColor>()
            };
            if (isNested && i == 0)
            {
                // Target's outer must match block's current outer layer
                cell.shapeType = outerShape;
                cell.innerShapes.Add(innerShape);
                cell.innerShapeColors.Add(color);
            }
            else
            {
                cell.shapeType = innerShape;
            }
            copy.Add(cell);
        }
        return copy;
    }

    private static ShapeColor[] AssignColors(Random rng, int count)
    {
        var colors = new ShapeColor[count];
        for (int i = 0; i < count; i++) colors[i] = Colors[i % Colors.Length];
        Shuffle(rng, colors);
        return colors;
    }

    private static List<ShapeCellData> CloneLayoutWithColor(List<ShapeCellData> source, ShapeColor color)
    {
        var copy = new List<ShapeCellData>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            copy.Add(new ShapeCellData
            {
                localPosition = source[i].localPosition,
                shapeType = source[i].shapeType,
                outerColor = color,
                innerShapes = source[i].innerShapes != null
                    ? new List<ShapeType>(source[i].innerShapes)
                    : new List<ShapeType>(),
                innerShapeColors = source[i].innerShapeColors != null
                    ? new List<ShapeColor>(source[i].innerShapeColors)
                    : new List<ShapeColor>()
            });
        }
        return copy;
    }

    private static Vector2Int PickFootprintAnchor(Random rng, HashSet<Vector2Int> mask, List<ShapeCellData> layout, HashSet<Vector2Int> used, GenerationStyle style)
    {
        var candidates = new List<Vector2Int>(mask);
        Shuffle(rng, candidates);
        candidates.Sort((a, b) => TargetScore(b, mask, used, style).CompareTo(TargetScore(a, mask, used, style)));
        for (int i = 0; i < candidates.Count; i++)
        {
            Vector2Int anchor = candidates[i];
            bool valid = true;
            for (int c = 0; c < layout.Count; c++)
            {
                Vector2Int cell = anchor + layout[c].localPosition;
                if (!mask.Contains(cell) || used.Contains(cell)) { valid = false; break; }
            }
            if (valid) return anchor;
        }
        return InvalidCell;
    }

    private static int TargetScore(Vector2Int cell, HashSet<Vector2Int> mask, HashSet<Vector2Int> used, GenerationStyle style)
    {
        int neighbors = 0;
        if (mask.Contains(cell + Vector2Int.up)) neighbors++;
        if (mask.Contains(cell + Vector2Int.down)) neighbors++;
        if (mask.Contains(cell + Vector2Int.left)) neighbors++;
        if (mask.Contains(cell + Vector2Int.right)) neighbors++;
        int score = (4 - neighbors) * 10;
        if (style == GenerationStyle.Corridor && neighbors <= 2) score += 18;
        if (style == GenerationStyle.Chain && used.Count > 0)
        {
            foreach (Vector2Int prior in used)
            {
                int distance = Mathf.Abs(cell.x - prior.x) + Mathf.Abs(cell.y - prior.y);
                if (distance <= 2) score += 12;
            }
        }
        return score;
    }

    private static Vector2Int PickBlockAnchor(
        Random rng,
        HashSet<Vector2Int> mask,
        Vector2Int target,
        List<ShapeCellData> layout,
        HashSet<Vector2Int> used,
        HashSet<Vector2Int> usedTargetCells,
        bool sharedRow)
    {
        var candidates = new List<Vector2Int>(mask);
        Shuffle(rng, candidates);
        candidates.Sort((a, b) => DistanceScore(b, target, sharedRow).CompareTo(DistanceScore(a, target, sharedRow)));
        for (int i = 0; i < candidates.Count; i++)
        {
            Vector2Int anchor = candidates[i];
            if (anchor == target) continue;
            bool valid = true;
            for (int c = 0; c < layout.Count; c++)
            {
                Vector2Int cell = anchor + layout[c].localPosition;
                if (!mask.Contains(cell) || used.Contains(cell) || (usedTargetCells != null && usedTargetCells.Contains(cell))) { valid = false; break; }
            }
            if (valid) return anchor;
        }
        return InvalidCell;
    }

    private static Vector2Int PickSolutionAwareBlockAnchor(
        Random rng,
        HashSet<Vector2Int> mask,
        Vector2Int target,
        List<ShapeCellData> layout,
        HashSet<Vector2Int> used,
        HashSet<Vector2Int> usedTargetCells,
        bool sharedRow,
        GenerationStyle style,
        DifficultyTier tier,
        bool protectIntendedLane)
    {
        var candidates = new List<Vector2Int>(mask);
        Shuffle(rng, candidates);
        candidates.Sort((a, b) => SolutionAnchorScore(b, target, sharedRow, style, tier).CompareTo(
            SolutionAnchorScore(a, target, sharedRow, style, tier)));
        for (int i = 0; i < candidates.Count; i++)
        {
            Vector2Int anchor = candidates[i];
            bool aligned = anchor.x == target.x || anchor.y == target.y;
            if (!aligned || anchor == target)
            {
                continue;
            }

            if (protectIntendedLane && !IsLaneClear(anchor, target, used))
            {
                continue;
            }

            bool valid = true;
            for (int c = 0; c < layout.Count; c++)
            {
                Vector2Int cell = anchor + layout[c].localPosition;
                if (!mask.Contains(cell) || used.Contains(cell) || (usedTargetCells != null && usedTargetCells.Contains(cell)))
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
            {
                return anchor;
            }
        }

        return PickBlockAnchor(rng, mask, target, layout, used, usedTargetCells, sharedRow);
    }

    private static int SolutionAnchorScore(Vector2Int anchor, Vector2Int target, bool sharedRow, GenerationStyle style, DifficultyTier tier)
    {
        int distance = Mathf.Abs(anchor.x - target.x) + Mathf.Abs(anchor.y - target.y);
        int score = distance * 12;
        if (anchor.y == target.y) score += sharedRow ? 20 : 8;
        if (style == GenerationStyle.Chain && distance >= 3) score += 12;
        if (tier == DifficultyTier.Easy && distance <= 4) score += 8;
        return score;
    }

    private static int BranchingBudget(DifficultyTier tier)
    {
        switch (tier)
        {
            case DifficultyTier.Easy: return 12;
            case DifficultyTier.Medium: return 14;
            case DifficultyTier.Hard: return 16;
            case DifficultyTier.Expert: return 18;
            default: return 8;
        }
    }

    private static int BranchingPreferred(DifficultyTier tier)
    {
        switch (tier)
        {
            case DifficultyTier.Easy: return 4;
            case DifficultyTier.Medium: return 5;
            case DifficultyTier.Hard: return 6;
            case DifficultyTier.Expert: return 7;
            default: return 6;
        }
    }

    private static int BranchingHardLimit(DifficultyTier tier)
    {
        switch (tier)
        {
            case DifficultyTier.Easy: return 12;
            case DifficultyTier.Medium: return 14;
            case DifficultyTier.Hard: return 16;
            case DifficultyTier.Expert: return 18;
            default: return 16;
        }
    }

    private static int BranchingPenalty(int initialBranching, int preferred)
    {
        return Mathf.Max(0, initialBranching - preferred) * 2;
    }

    private static int CountInitialLegalMoves(SolverLevel level)
    {
        SolverState initial = ShapeNestSolver.CreateInitialState(level);
        int legalMoves = 0;
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int blockIndex = 0; blockIndex < initial.Blocks.Length; blockIndex++)
        {
            for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
            {
                if (ShapeNestSolver.TryMove(level, initial, blockIndex, directions[directionIndex], out _, out _) != null)
                {
                    legalMoves++;
                }
            }
        }
        return legalMoves;
    }

    private static int[] CreateIntendedOrder(Random rng, int count)
    {
        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        Shuffle(rng, order);
        return order;
    }

    private static bool IsLaneClear(Vector2Int block, Vector2Int target, HashSet<Vector2Int> occupied)
    {
        Vector2Int step;
        if (block.x == target.x)
        {
            step = target.y > block.y ? Vector2Int.up : Vector2Int.down;
        }
        else if (block.y == target.y)
        {
            step = target.x > block.x ? Vector2Int.right : Vector2Int.left;
        }
        else
        {
            return false;
        }

        Vector2Int current = block + step;
        while (current != target)
        {
            if (occupied.Contains(current)) return false;
            current += step;
        }
        return true;
    }

    private static int EstimateDependencyDepth(Vector2Int[] blocks, Vector2Int[] targets)
    {
        int maximumDepth = 1;
        for (int i = 0; i < blocks.Length; i++)
        {
            int depth = 1;
            for (int j = 0; j < blocks.Length; j++)
            {
                if (i == j) continue;
                if (IsBetween(blocks[j], blocks[i], targets[i])) depth++;
            }
            maximumDepth = Mathf.Max(maximumDepth, depth);
        }
        return maximumDepth;
    }

    private static bool IsBetween(Vector2Int candidate, Vector2Int block, Vector2Int target)
    {
        if (block.x == target.x && candidate.x == block.x)
        {
            return candidate.y > Mathf.Min(block.y, target.y) && candidate.y < Mathf.Max(block.y, target.y);
        }

        if (block.y == target.y && candidate.y == block.y)
        {
            return candidate.x > Mathf.Min(block.x, target.x) && candidate.x < Mathf.Max(block.x, target.x);
        }

        return false;
    }

    private static void AddFootprint(HashSet<Vector2Int> used, Vector2Int anchor, List<ShapeCellData> layout)
    {
        for (int i = 0; i < layout.Count; i++) used.Add(anchor + layout[i].localPosition);
    }

    private static int DistanceScore(Vector2Int cell, Vector2Int target, bool sharedRow)
    {
        int distance = Mathf.Abs(cell.x - target.x) + Mathf.Abs(cell.y - target.y);
        return distance * 10 + (sharedRow && cell.y == target.y ? 8 : 0);
    }

    private static bool ShouldUseIce(Random rng, GenerationStyle style, DifficultyTier tier, int levelNumber = 0)
    {
        // Phase 85B: Ice is off for ALL levels 1-15.
        if (levelNumber > 0 && levelNumber <= 15)
        {
            return false;
        }

        return tier >= DifficultyTier.Hard && style != GenerationStyle.Corridor && rng.NextDouble() < 0.12;
    }

    private static int CountMultiCell(List<LevelBlockData> blocks) { int count = 0; for (int i = 0; i < blocks.Count; i++) if (ShapeLayout.EffectiveCount(blocks[i].cells) > 1) count++; return count; }
    private static int Diversity(List<LevelBlockData> blocks, bool color)
    {
        var values = new HashSet<int>();
        for (int i = 0; i < blocks.Count; i++)
        {
            ShapeColor cellColor = blocks[i].cells != null && blocks[i].cells.Count > 0
                ? ShapeLayout.EffectiveOuterColor(blocks[i].cells[0])
                : ShapeColor.Default;
            values.Add(color ? (int)cellColor : (int)blocks[i].shapeType);
        }
        return values.Count;
    }
    private static int OccupiedCellCount(List<LevelBlockData> blocks) { int count = 0; for (int i = 0; i < blocks.Count; i++) count += ShapeLayout.EffectiveCount(blocks[i].cells); return count; }
    private static int CalculateQuality(GeneratedLevelResult result, DifficultyTier tier, GenerationStyle style)
    {
        int score = result.MoveCount * 4 + result.ExploredStates / 20 + result.MultiCellCount * 8 + result.ShapeDiversity * 6 + result.ColorDiversity * 4;
        score += result.BlockCount * 2 + result.MeasuredDependencyDepth * 3 + result.BranchingScore - result.BranchingPenalty;
        score += Mathf.RoundToInt(result.PlayableRatio * 30f) + Mathf.RoundToInt(result.OccupancyRatio * 30f);
        if (style == GenerationStyle.Dense && result.OccupancyRatio > 0.55f) score += 12;
        if (style == GenerationStyle.MultiCell && result.MultiCellCount > 0) score += 12;
        return score;
    }
    private static int MinimumQuality(DifficultyTier tier) { return tier == DifficultyTier.Easy ? 20 : tier == DifficultyTier.Medium ? 35 : tier == DifficultyTier.Hard ? 50 : 65; }

    private static bool ShouldUseCollisions(DifficultyTier tier, MechanicMode mode)
    {
        if (mode == MechanicMode.Off)
        {
            return false;
        }

        if (mode == MechanicMode.On)
        {
            return true;
        }

        return tier != DifficultyTier.Easy;
    }

    private static ShapeType[] AssignShapes(Random rng, int count, DifficultyTier tier)
    {
        var shapes = new ShapeType[count];
        var bag = new List<ShapeType>(Shapes);
        Shuffle(rng, bag);
        for (int i = 0; i < count; i++)
        {
            if (i < bag.Count && (tier == DifficultyTier.Easy || i < 3))
            {
                shapes[i] = bag[i];
            }
            else
            {
                shapes[i] = Shapes[rng.Next(Shapes.Length)];
            }
        }

        return shapes;
    }

    private static Vector2Int[] PickUniqueCellsFromMask(
        Random rng,
        HashSet<Vector2Int> playableMask,
        int count,
        HashSet<Vector2Int> used,
        bool preferSharedRow)
    {
        var cells = new Vector2Int[count];
        var playableList = new List<Vector2Int>(playableMask);
        if (playableList.Count == 0)
        {
            return new Vector2Int[count];
        }

        int sharedAxis = rng.Next(playableList.Count);
        bool shareRow = rng.Next(2) == 0;
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell;
            int guard = 0;
            do
            {
                if (preferSharedRow && guard < 8)
                {
                    Vector2Int anchor = playableList[sharedAxis % playableList.Count];
                    cell = shareRow
                        ? new Vector2Int(anchor.x + (rng.Next(0, 2) == 0 ? -1 : 1), anchor.y)
                        : new Vector2Int(anchor.x, anchor.y + (rng.Next(0, 2) == 0 ? -1 : 1));
                    if (!playableMask.Contains(cell))
                    {
                        cell = playableList[rng.Next(playableList.Count)];
                    }
                }
                else
                {
                    cell = playableList[rng.Next(playableList.Count)];
                }

                guard++;
            }
            while (used.Contains(cell) && guard < 200);

            if (used.Contains(cell))
            {
                cell = FirstFreeInMask(playableMask, used);
            }

            used.Add(cell);
            cells[i] = cell;
        }

        return cells;
    }

    private static Vector2Int PickTargetCellFromMask(
        Random rng,
        HashSet<Vector2Int> playableMask,
        HashSet<Vector2Int> usedTargets,
        Vector2Int blockCell,
        bool allowImmediate,
        int width,
        int height)
    {
        var candidates = new List<Vector2Int>(playableMask);
        if (candidates.Count == 0)
        {
            return FirstFree(width, height, usedTargets);
        }

        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            Vector2Int candidate = candidates[i];
            if (usedTargets.Contains(candidate) || (!allowImmediate && candidate == blockCell))
            {
                candidates.RemoveAt(i);
            }
        }

        if (candidates.Count == 0)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var candidate = new Vector2Int(x, y);
                    if (!usedTargets.Contains(candidate) && (allowImmediate || candidate != blockCell))
                    {
                        return candidate;
                    }
                }
            }

            return FirstFree(width, height, usedTargets);
        }

        return candidates[rng.Next(candidates.Count)];
    }

    private static Vector2Int FirstFree(int width, int height, HashSet<Vector2Int> used)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!used.Contains(cell))
                {
                    return cell;
                }
            }
        }

        return Vector2Int.zero;
    }

    private static Vector2Int FirstFreeInMask(HashSet<Vector2Int> playableMask, HashSet<Vector2Int> used)
    {
        foreach (Vector2Int cell in playableMask)
        {
            if (!used.Contains(cell))
            {
                return cell;
            }
        }

        return Vector2Int.zero;
    }

    private static int CountImmediateSettlements(List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        int count = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            LevelBlockData block = blocks[i];
            for (int t = 0; t < targets.Count; t++)
            {
                LevelTargetData target = targets[t];
                if (target.gridPosition == block.gridPosition && target.shapeType == block.shapeType)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private static void Shuffle<T>(Random rng, IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }
}
