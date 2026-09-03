using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 62C — static validation for rebuilt Campaign_43_Reference.
/// Menu: Shape Nest / Phase 62C Verify Level 43
/// Coordinates use runtime convention: (0,0) = bottom-left. Screenshot R1 is y=9.
/// </summary>
public static class Phase62Level43Verify
{
    private const string LevelPath = "Assets/Levels/Campaign_43_Reference.asset";
    private const string DatabasePath = "Assets/Levels/LevelDatabase.asset";
    private const string ReportPath = "Captures/phase62c-report.txt";

    private static readonly (string id, Vector2Int[] cells, ShapeType shape, ShapeColor color, bool nested, ShapeType inner, ShapeColor innerColor)[] ExpectedPieces =
    {
        ("B-orange-pentagon", new[] { new Vector2Int(1, 6), new Vector2Int(1, 7), new Vector2Int(1, 8) },
            ShapeType.Pentagon, ShapeColor.Orange, true, ShapeType.Pentagon, ShapeColor.Cyan),
        ("B-purple-star", new[] { new Vector2Int(2, 7), new Vector2Int(2, 8) },
            ShapeType.Star, ShapeColor.Purple, false, ShapeType.Square, ShapeColor.Default),
        ("B-yellow-purple-square", new[] { new Vector2Int(4, 7), new Vector2Int(4, 8), new Vector2Int(4, 9) },
            ShapeType.Square, ShapeColor.Yellow, true, ShapeType.Square, ShapeColor.Purple),
        ("B-white-triangle-2x2", new[] { new Vector2Int(1, 4), new Vector2Int(2, 4), new Vector2Int(1, 5), new Vector2Int(2, 5) },
            ShapeType.Triangle, ShapeColor.White, false, ShapeType.Square, ShapeColor.Default),
        ("B-blue-pink-circle", new[] { new Vector2Int(3, 0), new Vector2Int(3, 1), new Vector2Int(3, 2) },
            ShapeType.Circle, ShapeColor.Cyan, true, ShapeType.Circle, ShapeColor.Pink),
        ("B-green-red-diamond", new[] { new Vector2Int(1, 0), new Vector2Int(1, 1) },
            ShapeType.Diamond, ShapeColor.Green, true, ShapeType.Diamond, ShapeColor.Red),
    };

    private static readonly (string id, Vector2Int[] cells, ShapeType shape, ShapeColor color)[] ExpectedTargets =
    {
        ("T-white-tri-top", new[] { new Vector2Int(1, 9), new Vector2Int(2, 9) }, ShapeType.Triangle, ShapeColor.White),
        ("T-green-diamond", new[] { new Vector2Int(0, 7), new Vector2Int(0, 8) }, ShapeType.Diamond, ShapeColor.Green),
        ("T-purple-square", new[] { new Vector2Int(5, 7), new Vector2Int(5, 8), new Vector2Int(5, 9) }, ShapeType.Square, ShapeColor.Purple),
        ("T-cyan-circle", new[] { new Vector2Int(0, 4), new Vector2Int(0, 5), new Vector2Int(0, 6) }, ShapeType.Circle, ShapeColor.Cyan),
        ("T-white-tri-mid", new[] { new Vector2Int(4, 4), new Vector2Int(4, 5) }, ShapeType.Triangle, ShapeColor.White),
        ("T-orange-pentagon", new[] { new Vector2Int(5, 3), new Vector2Int(5, 4), new Vector2Int(5, 5) }, ShapeType.Pentagon, ShapeColor.Orange),
        ("T-purple-star", new[] { new Vector2Int(1, 2), new Vector2Int(1, 3) }, ShapeType.Star, ShapeColor.Purple),
        ("T-yellow-square", new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2) }, ShapeType.Square, ShapeColor.Yellow),
        ("T-cyan-pentagon", new[] { new Vector2Int(2, 0), new Vector2Int(2, 1) }, ShapeType.Pentagon, ShapeColor.Cyan),
        ("T-pink-circle", new[] { new Vector2Int(5, 0), new Vector2Int(5, 1), new Vector2Int(5, 2) }, ShapeType.Circle, ShapeColor.Pink),
        ("T-red-diamond", new[] { new Vector2Int(3, 7), new Vector2Int(3, 8) }, ShapeType.Diamond, ShapeColor.Red),
    };

    private static readonly Vector2Int[] ExpectedObstacle =
    {
        new Vector2Int(3, 3),
        new Vector2Int(4, 3),
    };

    [MenuItem("Shape Nest/Phase 62C Verify Level 43")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 62C — LEVEL 43 STATIC VALIDATION");
        report.AppendLine("======================================");

        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath);
        if (level == null)
        {
            report.AppendLine("FAIL: Could not load " + LevelPath);
            WriteReport(report.ToString());
            Debug.LogError(report.ToString());
            return;
        }

        report.AppendLine($"Level asset: {level.name}");
        report.AppendLine($"Grid: {level.ResolvedGridWidth}x{level.ResolvedGridHeight}");
        report.AppendLine($"Blocks: {level.blocks?.Count ?? 0} (expected 6)");
        report.AppendLine($"Targets: {level.targets?.Count ?? 0} (expected 11)");
        report.AppendLine($"Shutters: {level.shutters?.Count ?? 0} (expected 0)");
        report.AppendLine($"Blocked cells: {level.blockedCells?.Count ?? 0} (expected 2)");

        bool gridOk = level.ResolvedGridWidth == 6 && level.ResolvedGridHeight == 10;
        report.AppendLine($"Grid 6x10: {(gridOk ? "PASS" : "FAIL")}");

        bool countsOk = (level.blocks?.Count ?? 0) == 6
            && (level.targets?.Count ?? 0) == 11
            && (level.shutters == null || level.shutters.Count == 0)
            && (level.blockedCells?.Count ?? 0) == 2;
        report.AppendLine($"Counts: {(countsOk ? "PASS" : "FAIL")}");

        report.AppendLine(ValidateDatabaseIndex(level));
        report.AppendLine(ValidateNoDuplicates(level));
        report.AppendLine(ValidateObstacleCells(level));
        report.AppendLine(ValidatePieces(level));
        report.AppendLine(ValidateTargets(level));
        report.AppendLine(ValidateTargetNesting(level));
        report.AppendLine(ValidateOrangePentagonPartialNest(level));

        var go = new GameObject("Phase62C_TempValidator");
        var validator = go.AddComponent<LevelValidator>();
        bool valid = validator.ValidateLevel(level);
        Object.DestroyImmediate(go);
        report.AppendLine($"LevelValidator layer balance: {(valid ? "PASS" : "FAIL")}");

        WriteReport(report.ToString());
        Debug.Log(report.ToString());
    }

    [MenuItem("Shape Nest/Phase 62B Verify Level 43")]
    public static void RunLegacy62B()
    {
        Run();
    }

    [MenuItem("Shape Nest/Phase 62A Verify Level 43")]
    public static void RunLegacy62A()
    {
        Run();
    }

    private static string ValidateDatabaseIndex(LevelData level)
    {
        LevelDatabase database = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DatabasePath);
        if (database == null || database.Count <= 42)
        {
            return "Database index 42: FAIL";
        }

        bool ok = database.GetLevel(42) == level;
        return $"Database index 42 = Level 43: {(ok ? "PASS" : "FAIL")}";
    }

    private static string ValidateNoDuplicates(LevelData level)
    {
        var blockCells = CollectBlockCells(level);
        var targetCells = CollectTargetCells(level);
        bool blockDup = blockCells.Count != CountBlockOccupancy(level);
        bool targetDup = targetCells.Count != CountTargetOccupancy(level);
        bool overlap = false;
        foreach (Vector2Int cell in blockCells)
        {
            if (targetCells.Contains(cell))
            {
                overlap = true;
                break;
            }
        }

        bool obstacleOverlap = false;
        if (level.blockedCells != null)
        {
            for (int i = 0; i < level.blockedCells.Count; i++)
            {
                Vector2Int cell = level.blockedCells[i];
                if (blockCells.Contains(cell) || targetCells.Contains(cell))
                {
                    obstacleOverlap = true;
                    break;
                }
            }
        }

        bool ok = !blockDup && !targetDup && !overlap && !obstacleOverlap;
        return $"No duplicate/overlapping occupancy: {(ok ? "PASS" : "FAIL")}";
    }

    private static string ValidateObstacleCells(LevelData level)
    {
        if (level.blockedCells == null || level.blockedCells.Count != 2)
        {
            return "Obstacle C4-R7/C5-R7 (3,3)/(4,3): FAIL";
        }

        var set = new HashSet<Vector2Int>(level.blockedCells);
        bool ok = set.Contains(ExpectedObstacle[0]) && set.Contains(ExpectedObstacle[1]) && set.Count == 2;
        return $"Obstacle C4-R7/C5-R7 (3,3)/(4,3): {(ok ? "PASS" : "FAIL")}";
    }

    private static string ValidatePieces(LevelData level)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Movable pieces:");
        var remaining = new List<LevelBlockData>(level.blocks ?? new List<LevelBlockData>());
        for (int i = 0; i < ExpectedPieces.Length; i++)
        {
            var expected = ExpectedPieces[i];
            int match = FindBlock(remaining, expected.cells, expected.shape, expected.color, expected.nested, expected.inner, expected.innerColor);
            sb.AppendLine($"  {expected.id}: {(match >= 0 ? "PASS" : "FAIL")}");
            if (match >= 0)
            {
                remaining.RemoveAt(match);
            }
        }

        sb.AppendLine($"  Extra blocks: {(remaining.Count == 0 ? "PASS (0)" : "FAIL (" + remaining.Count + ")")}");
        return sb.ToString();
    }

    private static string ValidateTargets(LevelData level)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Targets:");
        var remaining = new List<LevelTargetData>(level.targets ?? new List<LevelTargetData>());
        for (int i = 0; i < ExpectedTargets.Length; i++)
        {
            var expected = ExpectedTargets[i];
            int match = FindTarget(remaining, expected.cells, expected.shape, expected.color);
            sb.AppendLine($"  {expected.id}: {(match >= 0 ? "PASS" : "FAIL")}");
            if (match >= 0)
            {
                remaining.RemoveAt(match);
            }
        }

        sb.AppendLine($"  Extra targets: {(remaining.Count == 0 ? "PASS (0)" : "FAIL (" + remaining.Count + ")")}");
        return sb.ToString();
    }

    private static string ValidateTargetNesting(LevelData level)
    {
        int nested = 0;
        if (level.targets != null)
        {
            for (int i = 0; i < level.targets.Count; i++)
            {
                nested += CountNestedCells(level.targets[i]?.cells);
            }
        }

        return $"All targets plain: {(nested == 0 ? "PASS" : "FAIL (" + nested + " nested cells)")}";
    }

    private static string ValidateOrangePentagonPartialNest(LevelData level)
    {
        if (level.blocks == null)
        {
            return "Orange pentagon R4 plain / R2-R3 nested: FAIL";
        }

        for (int i = 0; i < level.blocks.Count; i++)
        {
            LevelBlockData block = level.blocks[i];
            if (block == null)
            {
                continue;
            }

            var footprint = new HashSet<Vector2Int>();
            CollectPieceWorlds(block.gridPosition, block.cells, footprint);
            if (!footprint.Contains(new Vector2Int(1, 6)))
            {
                continue;
            }

            ShapeCellData plain = CellAtWorld(block, new Vector2Int(1, 6));
            ShapeCellData mid = CellAtWorld(block, new Vector2Int(1, 7));
            ShapeCellData top = CellAtWorld(block, new Vector2Int(1, 8));
            bool ok = IsPlain(plain)
                && HasInner(mid, ShapeType.Pentagon, ShapeColor.Cyan)
                && HasInner(top, ShapeType.Pentagon, ShapeColor.Cyan);
            return $"Orange pentagon R4 plain / R2-R3 nested: {(ok ? "PASS" : "FAIL")}";
        }

        return "Orange pentagon R4 plain / R2-R3 nested: FAIL";
    }

    private static int FindBlock(
        List<LevelBlockData> remaining,
        Vector2Int[] cells,
        ShapeType shape,
        ShapeColor color,
        bool nested,
        ShapeType inner,
        ShapeColor innerColor)
    {
        for (int i = 0; i < remaining.Count; i++)
        {
            LevelBlockData block = remaining[i];
            if (!FootprintEquals(block?.gridPosition ?? default, block?.cells, cells))
            {
                continue;
            }

            if (!BlockMatchesVisual(block, shape, color, nested, inner, innerColor))
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private static int FindTarget(
        List<LevelTargetData> remaining,
        Vector2Int[] cells,
        ShapeType shape,
        ShapeColor color)
    {
        for (int i = 0; i < remaining.Count; i++)
        {
            LevelTargetData target = remaining[i];
            if (!FootprintEquals(target?.gridPosition ?? default, target?.cells, cells))
            {
                continue;
            }

            if (!TargetMatchesVisual(target, shape, color))
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private static bool BlockMatchesVisual(
        LevelBlockData block,
        ShapeType shape,
        ShapeColor color,
        bool nested,
        ShapeType inner,
        ShapeColor innerColor)
    {
        if (block?.cells == null || block.cells.Count == 0)
        {
            return false;
        }

        bool sawNested = false;
        for (int i = 0; i < block.cells.Count; i++)
        {
            ShapeCellData cell = block.cells[i];
            if (cell == null || cell.shapeType != shape || cell.outerColor != color)
            {
                return false;
            }

            bool cellNested = cell.innerShapes != null && cell.innerShapes.Count > 0;
            if (cellNested)
            {
                sawNested = true;
                if (!nested
                    || cell.innerShapes[0] != inner
                    || cell.innerShapeColors == null
                    || cell.innerShapeColors.Count == 0
                    || cell.innerShapeColors[0] != innerColor)
                {
                    return false;
                }
            }
        }

        return sawNested == nested || (nested && sawNested);
    }

    private static bool TargetMatchesVisual(LevelTargetData target, ShapeType shape, ShapeColor color)
    {
        if (target?.cells == null || target.cells.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < target.cells.Count; i++)
        {
            ShapeCellData cell = target.cells[i];
            if (cell == null
                || cell.shapeType != shape
                || cell.outerColor != color
                || (cell.innerShapes != null && cell.innerShapes.Count > 0))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FootprintEquals(Vector2Int anchor, List<ShapeCellData> cells, Vector2Int[] expected)
    {
        if (expected == null || expected.Length == 0)
        {
            return false;
        }

        var actual = new HashSet<Vector2Int>();
        CollectPieceWorlds(anchor, cells, actual);
        if (actual.Count != expected.Length)
        {
            return false;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (!actual.Contains(expected[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void CollectPieceWorlds(Vector2Int anchor, List<ShapeCellData> cells, HashSet<Vector2Int> destination)
    {
        int count = ShapeLayout.EffectiveCount(cells);
        for (int i = 0; i < count; i++)
        {
            destination.Add(anchor + ShapeLayout.EffectiveLocal(cells, i));
        }
    }

    private static ShapeCellData CellAtWorld(LevelBlockData block, Vector2Int world)
    {
        if (block?.cells == null)
        {
            return null;
        }

        int count = ShapeLayout.EffectiveCount(block.cells);
        for (int i = 0; i < count; i++)
        {
            if (block.gridPosition + ShapeLayout.EffectiveLocal(block.cells, i) == world)
            {
                return block.cells[i];
            }
        }

        return null;
    }

    private static bool IsPlain(ShapeCellData cell)
    {
        return cell != null && (cell.innerShapes == null || cell.innerShapes.Count == 0);
    }

    private static bool HasInner(ShapeCellData cell, ShapeType inner, ShapeColor innerColor)
    {
        return cell != null
            && cell.innerShapes != null
            && cell.innerShapes.Count > 0
            && cell.innerShapes[0] == inner
            && cell.innerShapeColors != null
            && cell.innerShapeColors.Count > 0
            && cell.innerShapeColors[0] == innerColor;
    }

    private static int CountNestedCells(List<ShapeCellData> cells)
    {
        if (cells == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            ShapeCellData cell = cells[i];
            if (cell?.innerShapes != null && cell.innerShapes.Count > 0)
            {
                count++;
            }
        }

        return count;
    }

    private static HashSet<Vector2Int> CollectBlockCells(LevelData level)
    {
        var set = new HashSet<Vector2Int>();
        if (level.blocks == null)
        {
            return set;
        }

        for (int i = 0; i < level.blocks.Count; i++)
        {
            LevelBlockData piece = level.blocks[i];
            if (piece != null)
            {
                CollectPieceWorlds(piece.gridPosition, piece.cells, set);
            }
        }

        return set;
    }

    private static HashSet<Vector2Int> CollectTargetCells(LevelData level)
    {
        var set = new HashSet<Vector2Int>();
        if (level.targets == null)
        {
            return set;
        }

        for (int i = 0; i < level.targets.Count; i++)
        {
            LevelTargetData piece = level.targets[i];
            if (piece != null)
            {
                CollectPieceWorlds(piece.gridPosition, piece.cells, set);
            }
        }

        return set;
    }

    private static int CountBlockOccupancy(LevelData level)
    {
        int count = 0;
        if (level.blocks == null)
        {
            return 0;
        }

        for (int i = 0; i < level.blocks.Count; i++)
        {
            count += ShapeLayout.EffectiveCount(level.blocks[i]?.cells);
        }

        return count;
    }

    private static int CountTargetOccupancy(LevelData level)
    {
        int count = 0;
        if (level.targets == null)
        {
            return 0;
        }

        for (int i = 0; i < level.targets.Count; i++)
        {
            count += ShapeLayout.EffectiveCount(level.targets[i]?.cells);
        }

        return count;
    }

    private static void WriteReport(string text)
    {
        string dir = Path.GetDirectoryName(ReportPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(ReportPath, text);
    }
}
