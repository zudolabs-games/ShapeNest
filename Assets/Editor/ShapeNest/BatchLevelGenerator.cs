using System;
using UnityEditor;
using UnityEngine;

public static class BatchLevelGenerator
{
    // Entry point for Unity batch mode: -executeMethod BatchLevelGenerator.GenerateAllLevels
    public static void GenerateAllLevels()
    {
        // Configuration for each difficulty
        var config = new[]
        {
            new { Tier = DifficultyTier.Easy,    Seed = 1000, Count = 5 },
            new { Tier = DifficultyTier.Medium,  Seed = 2000, Count = 5 },
            new { Tier = DifficultyTier.Hard,    Seed = 3000, Count = 5 },
            new { Tier = DifficultyTier.Expert,  Seed = 4000, Count = 5 }
        };

        foreach (var entry in config)
        {
            // Build settings – match the UI defaults but enforce deterministic seed
            var settings = new LevelGeneratorSettings
            {
                Seed = entry.Seed,
                LevelCount = entry.Count,
                BoardWidth = 5,
                BoardHeight = 5,
                MinBlocks = 3,
                MaxBlocks = 5,
                StartingLevelNumber = 2,
                MaxAttemptsPerLevel = 1000,
                MinSolutionMoves = 1,
                MaxSolutionMoves = 100,
                MaxSolverDepth = 100,
                MaxSolverStates = 100000,
                Difficulty = entry.Tier,
                FixedDirections = MechanicMode.Progressive,
                Collisions = MechanicMode.Progressive,
                TargetStopping = MechanicMode.Progressive,
                ExistingPolicy = ExistingAssetPolicy.Overwrite,
                Style = GenerationStyle.Mixed,
                VaryBoardSize = true
            };

            var rng = new System.Random(settings.Seed);
            for (int i = 0; i < entry.Count; i++)
            {
                string levelName = $"Level_{entry.Tier}_{i + 1}";
                var result = LevelGenerator.TryGenerateOne(rng, settings, entry.Tier, i / (float)(entry.Count - 1), levelName);

                // Validate
                var validation = LevelEditorValidation.Validate(levelName, result.Blocks, result.Targets, result.GridWidth, result.GridHeight);
                if (!validation.IsValid)
                {
                    Debug.LogError($"Validation failed for {levelName}: {validation.Errors[0]}");
                    continue;
                }

                // Save asset
                var asset = LevelAssetUtility.SaveLevelData(
                    levelName,
                    result.Blocks,
                    result.Targets,
                    true, // overwrite
                    result.GridWidth,
                    result.GridHeight,
                    result.BlockedCells);

                if (asset != null)
                {
                    Debug.Log($"Saved {asset.name} (Tier={entry.Tier}, Moves={result.MoveCount})");
                }
                else
                {
                    Debug.LogWarning($"Asset for {levelName} not saved (already exists and overwrite disabled).");
                }
            }
        }

        // Refresh the AssetDatabase so the generated assets appear in the editor
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Batch level generation completed.");
    }

    [MenuItem("Tools/Shape Nest/Generate Tutorial Levels (1-5)")]
    public static string GenerateTutorialLevels()
    {
        var settings = new LevelGeneratorSettings
        {
            Seed = 12345,
            LevelCount = 5,
            BoardWidth = 5,
            BoardHeight = 5,
            MinBlocks = 2,
            MaxBlocks = 5,
            StartingLevelNumber = 1,
            LevelNumber = 1,
            MaxAttemptsPerLevel = 1000,
            MinSolutionMoves = 1,
            MaxSolutionMoves = 100,
            MaxSolverDepth = 100,
            MaxSolverStates = 100000,
            Difficulty = DifficultyTier.Easy,
            FixedDirections = MechanicMode.Off,
            Collisions = MechanicMode.Off,
            TargetStopping = MechanicMode.Progressive,
            ExistingPolicy = ExistingAssetPolicy.Overwrite,
            Style = GenerationStyle.Mixed,
            VaryBoardSize = true
        };

        var rng = new System.Random(settings.Seed);
        var report = new System.Text.StringBuilder();
        int acceptedCount = 0;

        for (int lvl = 1; lvl <= 5; lvl++)
        {
            string levelName = "Level" + lvl;
            float progress = (lvl - 1) / 4f;
            DifficultyTier tier = DifficultyTier.Easy;

            GeneratedLevelResult accepted = null;
            int attempts = 0;

            for (int attempt = 0; attempt < settings.MaxAttemptsPerLevel; attempt++)
            {
                attempts++;
                GeneratedLevelResult candidate = LevelGenerator.TryCandidate(rng, settings, tier, progress, levelName, lvl);
                if (candidate.Outcome == GenerationOutcome.Accepted)
                {
                    var safety = LevelEditorValidation.Validate(levelName, candidate.Blocks, candidate.Targets, candidate.GridWidth, candidate.GridHeight);
                    if (!safety.IsValid) continue;

                    bool valid = true;
                    foreach (var b in candidate.Blocks)
                    {
                        if (ShapeLayout.EffectiveCount(b.cells) != 1) { valid = false; break; }
                        if (b.hasIce) { valid = false; break; }
                        if (b.moveDirection != MoveDirection.Any) { valid = false; break; }
                    }
                    if (!valid) continue;

                    foreach (var t in candidate.Targets)
                    {
                        if (ShapeLayout.EffectiveCount(t.cells) != 1) { valid = false; break; }
                    }
                    if (!valid) continue;

                    var blockCells = new System.Collections.Generic.HashSet<Vector2Int>();
                    foreach (var b in candidate.Blocks)
                    {
                        int count = b.cells == null ? 0 : b.cells.Count;
                        if (count == 0) blockCells.Add(b.gridPosition);
                        else { for (int c = 0; c < count; c++) blockCells.Add(b.gridPosition + ShapeLayout.EffectiveLocal(b.cells, c)); }
                    }
                    foreach (var t in candidate.Targets)
                    {
                        int count = t.cells == null ? 0 : t.cells.Count;
                        Vector2Int pos = count == 0 ? t.gridPosition : t.gridPosition + ShapeLayout.EffectiveLocal(t.cells, 0);
                        if (blockCells.Contains(pos)) { valid = false; break; }
                    }
                    if (!valid) continue;

                    accepted = candidate;
                    break;
                }
            }

            if (accepted != null)
            {
                var asset = LevelAssetUtility.SaveLevelData(
                    levelName,
                    accepted.Blocks,
                    accepted.Targets,
                    true,
                    accepted.GridWidth,
                    accepted.GridHeight,
                    accepted.BlockedCells);

                acceptedCount++;
                report.AppendLine($"ACCEPTED {levelName}: attempts={attempts}, size={accepted.GridWidth}x{accepted.GridHeight}, playable={accepted.GridWidth * accepted.GridHeight - accepted.BlockedCells.Count}, blocked={accepted.BlockedCells.Count}, blocks={accepted.Blocks.Count}, targets={accepted.Targets.Count}, moves={accepted.MoveCount}, states={accepted.ExploredStates}, replay={accepted.ReplayVerified}");
                Debug.Log($"ACCEPTED {levelName}: moves={accepted.MoveCount}, blocks={accepted.Blocks.Count}, size={accepted.GridWidth}x{accepted.GridHeight}");
            }
            else
            {
                report.AppendLine($"FAILED {levelName} in {attempts} attempts");
                Debug.LogError($"FAILED {levelName} in {attempts} attempts");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = $"Accepted {acceptedCount}/5 levels.\n" + report.ToString();
        Debug.Log(summary);
        return summary;
    }
}
