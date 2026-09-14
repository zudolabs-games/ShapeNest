using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public class LevelGeneratorWindow : EditorWindow
{
    private int seed = 12345;
    private int levelCount = 20;
    private int boardWidth = 5;
    private int boardHeight = 5;
    private int minBlocks = 3;
    private int maxBlocks = 5;
    private int startingLevelNumber = 1;
    private int maxAttemptsPerLevel = 1000;
    private int minSolutionMoves = 1;
    private int maxSolutionMoves = 100;
    private int maxSolverDepth = 100;
    private int maxSolverStates = 100000;
    private DifficultyTier difficulty = DifficultyTier.Progressive;
    private MechanicMode fixedDirections = MechanicMode.Progressive;
    private MechanicMode collisions = MechanicMode.Progressive;
    private MechanicMode targetStopping = MechanicMode.Progressive;
    private ExistingAssetPolicy existingPolicy = ExistingAssetPolicy.Skip;
    private GenerationStyle style = GenerationStyle.Mixed;
    private bool varyBoardSize = true;

    private bool isGenerating;
    private bool cancelRequested;
    private int currentIndex;
    private int currentNumber;
    private int attemptsThisLevel;
    private System.Random rng;
    private LevelGeneratorSettings settings;
    private readonly List<GeneratedLevelResult> results = new List<GeneratedLevelResult>();
    private readonly StringBuilder log = new StringBuilder();
    private Vector2 logScroll;
    private Vector2 resultScroll;
    private int selectedResult = -1;
    private string lastSeedMessage = string.Empty;

    [MenuItem("Tools/Shape Nest/Level Generator")]
    public static void Open()
    {
        LevelGeneratorWindow window = GetWindow<LevelGeneratorWindow>("Level Generator");
        window.minSize = new Vector2(460f, 720f);
        window.Show();
    }

    [MenuItem("Tools/Shape Nest/Generate 3 Test Levels")]
    public static void GenerateThreeTestLevels()
    {
        var settings = new LevelGeneratorSettings
        {
            Seed = 12345,
            LevelCount = 3,
            BoardWidth = 5,
            BoardHeight = 5,
            MinBlocks = 3,
            MaxBlocks = 3,
            StartingLevelNumber = 2,
            MaxAttemptsPerLevel = 400,
            MinSolutionMoves = 1,
            MaxSolutionMoves = 100,
            MaxSolverDepth = 100,
            MaxSolverStates = 100000,
            Difficulty = DifficultyTier.Easy,
            FixedDirections = MechanicMode.Progressive,
            Collisions = MechanicMode.Off,
            TargetStopping = MechanicMode.Progressive,
            ExistingPolicy = ExistingAssetPolicy.NextAvailable,
            VaryBoardSize = true
        };

        var rng = new System.Random(settings.Seed);
        int accepted = 0;
        for (int i = 0; i < 3; i++)
        {
            float progress = i / 2f;
            DifficultyTier tier = LevelDifficulty.ResolveTier(settings.Difficulty, progress);
            string name = LevelAssetUtility.NextAvailableLevelName(settings.StartingLevelNumber);
            GeneratedLevelResult result = LevelGenerator.TryGenerateOne(rng, settings, tier, progress, name);
            if (result.Outcome == GenerationOutcome.Accepted)
            {
                LevelEditorValidationResult safety = LevelEditorValidation.Validate(
                    name, result.Blocks, result.Targets, result.GridWidth, result.GridHeight);
                if (safety.IsValid)
                {
                    result.Asset = LevelAssetUtility.SaveLevelData(
                        name,
                        result.Blocks,
                        result.Targets,
                        false,
                        result.GridWidth,
                        result.GridHeight,
                        result.BlockedCells);
                    accepted++;
                    Debug.Log($"Generate 3 Test Levels: ACCEPTED {result.Asset.name} moves={result.MoveCount} difficulty={result.EstimatedDifficulty} replay={result.ReplayVerified}");
                }
                else
                {
                    Debug.LogError("Generate 3 Test Levels: validator rejected " + name);
                }
            }
            else
            {
                Debug.LogWarning($"Generate 3 Test Levels: {name} {result.OutcomeLabel} {result.Message}");
            }
        }

        Debug.Log($"Generate 3 Test Levels completed. Seed: {settings.Seed}. Accepted: {accepted}/3");
    }

    private void OnEnable()
    {
        EditorApplication.update += TickGeneration;
    }

    private void OnDisable()
    {
        EditorApplication.update -= TickGeneration;
        isGenerating = false;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("SHAPE NEST LEVEL GENERATOR", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(isGenerating);

        LevelDatabase database = LevelAssetUtility.FindLevelDatabase();
        if (database == null)
        {
            EditorGUILayout.HelpBox("No LevelDatabase found.", MessageType.Warning);
            if (GUILayout.Button("Create LevelDatabase"))
            {
                LevelAssetUtility.CreateLevelDatabase();
            }
        }
        else
        {
            EditorGUILayout.LabelField("Level Database", AssetDatabase.GetAssetPath(database));
        }

        EditorGUILayout.Space(6f);
        if (GUILayout.Button("Recommended Progressive"))
        {
            ApplyRecommendedPreset();
        }

        seed = EditorGUILayout.IntField("Seed", seed);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Randomize Seed"))
        {
            seed = UnityEngine.Random.Range(1, int.MaxValue);
        }

        EditorGUILayout.EndHorizontal();

        levelCount = Mathf.Max(1, EditorGUILayout.IntField("Number of Levels", levelCount));
        boardWidth = Mathf.Max(1, EditorGUILayout.IntField("Board Width", boardWidth));
        boardHeight = Mathf.Max(1, EditorGUILayout.IntField("Board Height", boardHeight));
        minBlocks = Mathf.Max(1, EditorGUILayout.IntField("Minimum Blocks", minBlocks));
        maxBlocks = Mathf.Max(minBlocks, EditorGUILayout.IntField("Maximum Blocks", maxBlocks));
        difficulty = (DifficultyTier)EditorGUILayout.EnumPopup("Difficulty", difficulty);
        style = (GenerationStyle)EditorGUILayout.EnumPopup("Generation Style", style);
        varyBoardSize = EditorGUILayout.Toggle("Vary Board Size", varyBoardSize);
        startingLevelNumber = Mathf.Max(1, EditorGUILayout.IntField("Starting Level Number", startingLevelNumber));
        existingPolicy = (ExistingAssetPolicy)EditorGUILayout.EnumPopup("If Asset Exists", existingPolicy);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("MECHANICS", EditorStyles.boldLabel);
        fixedDirections = (MechanicMode)EditorGUILayout.EnumPopup("Fixed Directions", fixedDirections);
        collisions = (MechanicMode)EditorGUILayout.EnumPopup("Collisions", collisions);
        targetStopping = (MechanicMode)EditorGUILayout.EnumPopup("Target Stopping", targetStopping);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("SOLVER", EditorStyles.boldLabel);
        maxSolverDepth = Mathf.Max(1, EditorGUILayout.IntField("Maximum Solver Depth", maxSolverDepth));
        maxSolverStates = Mathf.Max(100, EditorGUILayout.IntField("Maximum States", maxSolverStates));
        minSolutionMoves = Mathf.Max(1, EditorGUILayout.IntField("Minimum Solution Moves", minSolutionMoves));
        maxSolutionMoves = Mathf.Max(minSolutionMoves, EditorGUILayout.IntField("Maximum Solution Moves", maxSolutionMoves));
        maxAttemptsPerLevel = Mathf.Max(1, EditorGUILayout.IntField("Maximum Attempts per Level", maxAttemptsPerLevel));

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.Toggle("Require Unique Solution", false);
        EditorGUILayout.HelpBox("Unique-solution checking is not implemented in v1.", MessageType.None);
        EditorGUI.EndDisabledGroup();

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(isGenerating);
        if (GUILayout.Button("Generate Levels", GUILayout.Height(28f)))
        {
            StartGeneration();
        }

        EditorGUI.EndDisabledGroup();
        EditorGUI.BeginDisabledGroup(!isGenerating);
        if (GUILayout.Button("Cancel", GUILayout.Height(28f)))
        {
            cancelRequested = true;
        }

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Run Solver Tests"))
        {
            log.AppendLine(ShapeNestSolverTests.RunAll());
        }

        DrawResults();
        DrawSelectedSolution();
        DrawLog();
    }

    private void ApplyRecommendedPreset()
    {
        levelCount = 20;
        boardWidth = 5;
        boardHeight = 5;
        minBlocks = 3;
        maxBlocks = 9;
        minBlocks = 5;
        boardHeight = 7;
        difficulty = DifficultyTier.Progressive;
        style = GenerationStyle.Mixed;
        startingLevelNumber = 1;
        fixedDirections = MechanicMode.Progressive;
        collisions = MechanicMode.Progressive;
        targetStopping = MechanicMode.Progressive;
        maxSolverDepth = 100;
        maxSolverStates = 100000;
        maxAttemptsPerLevel = 1000;
        minSolutionMoves = 1;
        maxSolutionMoves = 100;
        existingPolicy = ExistingAssetPolicy.Skip;
        if (seed == 0)
        {
            seed = 12345;
        }
    }

    private void StartGeneration()
    {
        results.Clear();
        selectedResult = -1;
        log.Length = 0;
        currentIndex = 0;
        currentNumber = startingLevelNumber;
        attemptsThisLevel = 0;
        cancelRequested = false;
        rng = new System.Random(seed);
        settings = BuildSettings();
        isGenerating = true;
        lastSeedMessage = string.Empty;
        log.AppendLine($"Generation started. Seed: {seed}");
        log.AppendLine($"Generating {levelCount} levels starting at Level{startingLevelNumber}.");
    }

    private LevelGeneratorSettings BuildSettings()
    {
        return new LevelGeneratorSettings
        {
            Seed = seed,
            LevelCount = levelCount,
            BoardWidth = boardWidth,
            BoardHeight = boardHeight,
            MinBlocks = minBlocks,
            MaxBlocks = maxBlocks,
            StartingLevelNumber = startingLevelNumber,
            LevelNumber = startingLevelNumber,
            MaxAttemptsPerLevel = maxAttemptsPerLevel,
            MinSolutionMoves = minSolutionMoves,
            MaxSolutionMoves = maxSolutionMoves,
            MaxSolverDepth = maxSolverDepth,
            MaxSolverStates = maxSolverStates,
            Difficulty = difficulty,
            FixedDirections = fixedDirections,
            Collisions = collisions,
            TargetStopping = targetStopping,
            ExistingPolicy = existingPolicy,
            Style = style,
            VaryBoardSize = varyBoardSize
        };
    }

    private void TickGeneration()
    {
        if (!isGenerating)
        {
            return;
        }

        if (cancelRequested)
        {
            log.AppendLine("Generation cancelled.");
            FinishGeneration();
            return;
        }

        if (currentIndex >= levelCount)
        {
            FinishGeneration();
            return;
        }

        float progress = levelCount <= 1 ? 1f : currentIndex / (float)(levelCount - 1);
        DifficultyTier tier = LevelDifficulty.ResolveTier(difficulty, progress);
        string desiredName = "Level" + currentNumber;
        string levelName = desiredName;

        if (attemptsThisLevel == 0 && LevelAssetUtility.AssetExists(desiredName))
        {
            if (existingPolicy == ExistingAssetPolicy.Skip)
            {
                var skipped = new GeneratedLevelResult
                {
                    LevelName = desiredName,
                    Outcome = GenerationOutcome.RejectedExists,
                    Message = desiredName + " already exists.",
                    TargetTier = tier
                };
                results.Add(skipped);
                log.AppendLine($"{desiredName}: SKIPPED (already exists)");
                AdvanceLevelSlot();
                Repaint();
                return;
            }

            if (existingPolicy == ExistingAssetPolicy.NextAvailable)
            {
                levelName = LevelAssetUtility.NextAvailableLevelName(currentNumber);
            }
        }

        const int attemptsPerTick = 12;
        GeneratedLevelResult accepted = null;
        GeneratedLevelResult lastReject = null;
        for (int i = 0; i < attemptsPerTick && attemptsThisLevel < maxAttemptsPerLevel; i++)
        {
            attemptsThisLevel++;
            GeneratedLevelResult candidate = LevelGenerator.TryCandidate(rng, settings, tier, progress, levelName, currentNumber);
            lastReject = candidate;
            if (candidate.Outcome == GenerationOutcome.Accepted)
            {
                if (ValidateCandidate(candidate, levelName, currentNumber))
                {
                    accepted = candidate;
                    break;
                }
                else
                {
                    lastReject = candidate;
                }
            }
        }

        if (accepted != null)
        {
            CompleteLevel(accepted, levelName);
            AdvanceLevelSlot();
            Repaint();
            return;
        }

        if (attemptsThisLevel >= maxAttemptsPerLevel)
        {
            if (lastReject == null)
            {
                lastReject = new GeneratedLevelResult
                {
                    LevelName = levelName,
                    Outcome = GenerationOutcome.FailedAttempts,
                    TargetTier = tier,
                    LevelNumber = currentNumber,
                    Message = "No candidates were produced."
                };
            }
            else
            {
                lastReject.Outcome = GenerationOutcome.FailedAttempts;
                lastReject.Message = $"Could not find an accepted candidate in {maxAttemptsPerLevel} attempts.";
            }

            results.Add(lastReject);
            log.AppendLine($"{levelName}: {lastReject.OutcomeLabel}  {lastReject.Message}");
            AdvanceLevelSlot();
        }

        Repaint();
    }

    private bool ValidateCandidate(GeneratedLevelResult candidate, string levelName, int levelNumber)
    {
        LevelEditorValidationResult safety = LevelEditorValidation.Validate(
            levelName,
            candidate.Blocks,
            candidate.Targets,
            candidate.GridWidth,
            candidate.GridHeight);
        if (!safety.IsValid)
        {
            candidate.Outcome = GenerationOutcome.RejectedInvalid;
            candidate.Message = "Post-generation validator rejected the candidate: " +
                (safety.Errors.Count > 0 ? safety.Errors[0] : "invalid");
            return false;
        }

        bool isTutorial = levelNumber <= 5;
        if (isTutorial)
        {
            // Enforce single-cell blocks and targets, no ice, MoveDirection.Any, no overlap
            foreach (var block in candidate.Blocks)
            {
                if (ShapeLayout.EffectiveCount(block.cells) != 1)
                {
                    candidate.Outcome = GenerationOutcome.RejectedInvalid;
                    candidate.Message = "Tutorial level constraint: block must be single-cell.";
                    return false;
                }
                if (block.hasIce)
                {
                    candidate.Outcome = GenerationOutcome.RejectedInvalid;
                    candidate.Message = "Tutorial level constraint: block must not have ice.";
                    return false;
                }
                if (block.moveDirection != MoveDirection.Any)
                {
                    candidate.Outcome = GenerationOutcome.RejectedInvalid;
                    candidate.Message = "Tutorial level constraint: block must have MoveDirection.Any.";
                    return false;
                }
            }
            foreach (var target in candidate.Targets)
            {
                if (ShapeLayout.EffectiveCount(target.cells) != 1)
                {
                    candidate.Outcome = GenerationOutcome.RejectedInvalid;
                    candidate.Message = "Tutorial level constraint: target must be single-cell.";
                    return false;
                }
            }
            // Check for block-target footprint overlap (all footprint cells)
            var blockPositions = new HashSet<Vector2Int>();
            foreach (var block in candidate.Blocks)
            {
                int count = block.cells == null ? 0 : block.cells.Count;
                if (count == 0)
                {
                    blockPositions.Add(block.gridPosition);
                }
                else
                {
                    for (int c = 0; c < count; c++)
                    {
                        blockPositions.Add(block.gridPosition + ShapeLayout.EffectiveLocal(block.cells, c));
                    }
                }
            }
            foreach (var target in candidate.Targets)
            {
                int count = target.cells == null ? 0 : target.cells.Count;
                if (count == 0)
                {
                    if (blockPositions.Contains(target.gridPosition))
                    {
                        candidate.Outcome = GenerationOutcome.RejectedInvalid;
                        candidate.Message = "Tutorial level constraint: block and target cannot occupy same cell.";
                        return false;
                    }
                }
                else
                {
                    for (int c = 0; c < count; c++)
                    {
                        var targetPos = target.gridPosition + ShapeLayout.EffectiveLocal(target.cells, c);
                        if (blockPositions.Contains(targetPos))
                        {
                            candidate.Outcome = GenerationOutcome.RejectedInvalid;
                            candidate.Message = "Tutorial level constraint: block and target cannot occupy same cell.";
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    private void CompleteLevel(GeneratedLevelResult result, string levelName)
    {
        bool overwrite = existingPolicy == ExistingAssetPolicy.Overwrite;
        result.Asset = LevelAssetUtility.SaveLevelData(
            levelName,
            result.Blocks,
            result.Targets,
            overwrite,
            result.GridWidth,
            result.GridHeight,
            result.BlockedCells);
        if (result.Asset == null)
        {
            result.Outcome = GenerationOutcome.RejectedExists;
            result.Message = "Did not overwrite existing asset.";
        }
        else
        {
            result.LevelName = result.Asset.name;
        }

        results.Add(result);
        log.AppendLine($"{result.LevelName}: {result.OutcomeLabel}  {result.Message}");
    }

    private void AdvanceLevelSlot()
    {
        currentIndex++;
        currentNumber++;
        attemptsThisLevel = 0;
    }

    private void FinishGeneration()
    {
        isGenerating = false;
        cancelRequested = false;
        lastSeedMessage = $"Generation completed. Seed: {seed}";
        log.AppendLine(lastSeedMessage);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Repaint();
    }

    private void DrawResults()
    {
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("RESULTS", EditorStyles.boldLabel);
        int accepted = Count(GenerationOutcome.Accepted);
        int unsolvable = Count(GenerationOutcome.RejectedUnsolvable);
        int tooEasy = Count(GenerationOutcome.RejectedTooEasy);
        int tooHard = Count(GenerationOutcome.RejectedTooHard);
        int qualityRejected = Count(GenerationOutcome.RejectedTooEasy) + Count(GenerationOutcome.RejectedTooHard);
        int rejected = results.Count - accepted;

        EditorGUILayout.LabelField("Generated", results.Count.ToString());
        EditorGUILayout.LabelField("Accepted", accepted.ToString());
        EditorGUILayout.LabelField("Rejected", rejected.ToString());
        EditorGUILayout.LabelField("Unsolvable", unsolvable.ToString());
        EditorGUILayout.LabelField("Too Easy", tooEasy.ToString());
        EditorGUILayout.LabelField("Too Hard", tooHard.ToString());
        EditorGUILayout.LabelField("Quality Rejected", qualityRejected.ToString());
        if (accepted > 0)
        {
            int totalMoves = 0;
            int totalPieces = 0;
            float totalDensity = 0f;
            int totalAttempts = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Outcome != GenerationOutcome.Accepted) continue;
                totalMoves += results[i].MoveCount;
                totalPieces += results[i].BlockCount;
                totalDensity += results[i].OccupancyRatio;
            }
            EditorGUILayout.LabelField("Average Solution Length", (totalMoves / (float)accepted).ToString("0.0"));
            EditorGUILayout.LabelField("Average Pieces", (totalPieces / (float)accepted).ToString("0.0"));
            EditorGUILayout.LabelField("Average Density", (totalDensity / accepted).ToString("0%"));
        }
        if (!string.IsNullOrEmpty(lastSeedMessage))
        {
            EditorGUILayout.HelpBox(lastSeedMessage, MessageType.Info);
        }

        EditorGUILayout.LabelField("Level | Result | Solution | Difficulty | Quality | States | Blocks");
        resultScroll = EditorGUILayout.BeginScrollView(resultScroll, GUILayout.Height(160f));
        for (int i = 0; i < results.Count; i++)
        {
            GeneratedLevelResult result = results[i];
            string solution = result.Outcome == GenerationOutcome.Accepted ? result.MoveCount + " moves" : "-";
            string line =
                $"{result.LevelName} | {result.OutcomeLabel} | {solution} | {result.EstimatedDifficulty} | {result.QualityScore} | {result.ExploredStates} | {result.BlockCount}";
            if (GUILayout.Toggle(selectedResult == i, line, EditorStyles.label))
            {
                selectedResult = i;
                if (result.Asset != null && Event.current.clickCount == 2)
                {
                    Selection.activeObject = result.Asset;
                    EditorGUIUtility.PingObject(result.Asset);
                }
            }
        }

        EditorGUILayout.EndScrollView();

        if (selectedResult >= 0 && selectedResult < results.Count)
        {
            GeneratedLevelResult selected = results[selectedResult];
            EditorGUI.BeginDisabledGroup(selected.Asset == null);
            if (GUILayout.Button("Select Level Asset"))
            {
                Selection.activeObject = selected.Asset;
                EditorGUIUtility.PingObject(selected.Asset);
            }

            EditorGUI.EndDisabledGroup();
        }
    }

    private void DrawSelectedSolution()
    {
        if (selectedResult < 0 || selectedResult >= results.Count)
        {
            return;
        }

        GeneratedLevelResult selected = results[selectedResult];
        if (selected.Outcome != GenerationOutcome.Accepted)
        {
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("SHOW SOLUTION", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Shortest solution", selected.MoveCount + " moves");
        EditorGUILayout.LabelField("Explored states", selected.ExploredStates.ToString());
        EditorGUILayout.LabelField("Estimated difficulty", selected.EstimatedDifficulty.ToString());
        EditorGUILayout.LabelField("Quality score", selected.QualityScore.ToString());
        EditorGUILayout.LabelField("Playable ratio", selected.PlayableRatio.ToString("P0"));
        EditorGUILayout.LabelField("Board size", selected.GridWidth + " x " + selected.GridHeight);
        EditorGUILayout.LabelField("Piece density", selected.OccupancyRatio.ToString("P0"));
        EditorGUILayout.LabelField("Multi-cell pieces", selected.MultiCellCount.ToString());
        EditorGUILayout.LabelField("Shape / color diversity", selected.ShapeDiversity + " / " + selected.ColorDiversity);
        EditorGUILayout.LabelField("Replay verified", selected.ReplayVerified ? "Yes" : "No");
        if (selected.Solution != null)
        {
            for (int i = 0; i < selected.Solution.Length; i++)
            {
                EditorGUILayout.LabelField($"{i + 1}. {selected.Solution[i]}");
            }
        }
    }

    private void DrawLog()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("LOG", EditorStyles.boldLabel);
        logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(120f));
        EditorGUILayout.TextArea(log.ToString(), GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private int Count(GenerationOutcome outcome)
    {
        int count = 0;
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].Outcome == outcome)
            {
                count++;
            }
        }

        return count;
    }
}
