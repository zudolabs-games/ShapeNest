using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 65 — ShapeType + ShapeColor matching identity.
/// Menu: Shape Nest / Phase 65 Verify Shape + Color Matching
/// </summary>
public static class Phase65ShapeColorMatchVerify
{
    private const string ReportPath = "Captures/phase65-report.txt";
    private const string Level43Path = "Assets/Levels/Campaign_43_Reference.asset";

    [MenuItem("Shape Nest/Phase 65 Verify Shape + Color Matching")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 65 — SHAPE + COLOR MATCHING IDENTITY");
        report.AppendLine("==========================================");

        bool t1 = TestSameShapeSameColor(report);
        bool t2 = TestSameShapeDifferentColor(report);
        bool t3 = TestDifferentShapeSameColor(report);
        bool t4 = TestNestedOuterAndInnerColors(report);
        bool t5 = TestMultiCellTargetColor(report);
        bool t6 = TestPartialTargetAndUnrelatedColor(report);
        bool t7 = TestOccupyingAlignedRespectsColor(report);
        bool t8 = TestLevel43Identities(report);

        report.AppendLine();
        report.AppendLine($"1 same shape+color: {(t1 ? "PASS" : "FAIL")}");
        report.AppendLine($"2 same shape different color: {(t2 ? "PASS" : "FAIL")}");
        report.AppendLine($"3 different shape same color: {(t3 ? "PASS" : "FAIL")}");
        report.AppendLine($"4 nested outer/inner colors: {(t4 ? "PASS" : "FAIL")}");
        report.AppendLine($"5 multi-cell target color: {(t5 ? "PASS" : "FAIL")}");
        report.AppendLine($"6 partial + unrelated color target: {(t6 ? "PASS" : "FAIL")}");
        report.AppendLine($"7 occupying aligned color: {(t7 ? "PASS" : "FAIL")}");
        report.AppendLine($"8 Level 43 identities: {(t8 ? "PASS" : "FAIL")}");

        bool all = t1 && t2 && t3 && t4 && t5 && t6 && t7 && t8;
        report.AppendLine();
        report.AppendLine(all ? "RESULT: PASS" : "RESULT: FAIL");
        report.AppendLine("Note: Play Mode was not executed by this editor check.");

        WriteReport(report.ToString());
        if (all)
        {
            Debug.Log(report.ToString());
        }
        else
        {
            Debug.LogError(report.ToString());
        }
    }

    private static bool TestSameShapeSameColor(StringBuilder report)
    {
        var yellow = new MatchIdentity(ShapeType.Square, ShapeColor.Yellow);
        bool ok = ShapeMatch.AreMatchingLayers(yellow, yellow)
            && ShapeMatch.AreMatchingLayers(
                ShapeType.Square,
                ShapeColor.Yellow,
                ShapeType.Square,
                ShapeColor.Yellow);
        report.AppendLine($"  T1: {ok}");
        return ok;
    }

    private static bool TestSameShapeDifferentColor(StringBuilder report)
    {
        bool ok = !ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
            new MatchIdentity(ShapeType.Square, ShapeColor.Purple));
        report.AppendLine($"  T2: reject yellow vs purple square={ok}");
        return ok;
    }

    private static bool TestDifferentShapeSameColor(StringBuilder report)
    {
        bool ok = !ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Circle, ShapeColor.Yellow),
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow));
        report.AppendLine($"  T3: reject circle vs square yellow={ok}");
        return ok;
    }

    private static bool TestNestedOuterAndInnerColors(StringBuilder report)
    {
        var cell = new ShapeCellData
        {
            shapeType = ShapeType.Square,
            outerColor = ShapeColor.Yellow,
            innerShapes = new List<ShapeType> { ShapeType.Square },
            innerShapeColors = new List<ShapeColor> { ShapeColor.Purple }
        };

        MatchIdentity outer = ShapeMatch.FromCell(cell);
        bool outerOk = outer.Shape == ShapeType.Square && outer.Color == ShapeColor.Yellow;
        bool wrongInnerOffer = !ShapeLayout.TryConsumeLayer(
            cell,
            new MatchIdentity(ShapeType.Square, ShapeColor.Purple),
            out _);
        bool outerConsume = ShapeLayout.TryConsumeLayer(
            cell,
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
            out bool remains);
        bool innerOk = outerConsume
            && remains
            && cell.shapeType == ShapeType.Square
            && cell.outerColor == ShapeColor.Purple;
        bool purpleMatchesPurple = ShapeMatch.AreMatchingLayers(
            ShapeMatch.FromCell(cell),
            new MatchIdentity(ShapeType.Square, ShapeColor.Purple));
        bool purpleRejectsYellow = !ShapeMatch.AreMatchingLayers(
            ShapeMatch.FromCell(cell),
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow));

        bool ok = outerOk && wrongInnerOffer && innerOk && purpleMatchesPurple && purpleRejectsYellow;
        report.AppendLine(
            $"  T4: outer={outerOk} rejectPurpleOffer={wrongInnerOffer} promote={innerOk} " +
            $"purpleOk={purpleMatchesPurple} rejectYellow={purpleRejectsYellow}");
        return ok;
    }

    private static bool TestMultiCellTargetColor(StringBuilder report)
    {
        Target target = CreateTarget(
            new Vector2Int(5, 7),
            new[]
            {
                Colored(Vector2Int.zero, ShapeType.Square, ShapeColor.Yellow),
                Colored(new Vector2Int(0, 1), ShapeType.Square, ShapeColor.Yellow),
                Colored(new Vector2Int(0, 2), ShapeType.Square, ShapeColor.Yellow)
            });
        try
        {
            bool rejectPurple = !target.TryConsumeLayerAtWorld(
                new Vector2Int(5, 7),
                new MatchIdentity(ShapeType.Square, ShapeColor.Purple),
                out _);
            bool acceptYellow = target.TryConsumeLayerAtWorld(
                new Vector2Int(5, 7),
                new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
                out bool complete);
            bool ok = rejectPurple
                && acceptYellow
                && !complete
                && target.CellCount == 2
                && target.FindCellIndexAtWorld(new Vector2Int(5, 7)) < 0;
            report.AppendLine(
                $"  T5: rejectPurple={rejectPurple} acceptYellow={acceptYellow} " +
                $"complete={complete} cells={target.CellCount}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(target.gameObject);
        }
    }

    private static bool TestPartialTargetAndUnrelatedColor(StringBuilder report)
    {
        var root = new GameObject("Phase65_Board");
        var board = root.AddComponent<BoardManager>();
        board.ApplyGridSize(6, 10);

        Target yellow = CreateTarget(
            new Vector2Int(4, 7),
            new[]
            {
                Colored(Vector2Int.zero, ShapeType.Square, ShapeColor.Yellow),
                Colored(new Vector2Int(0, 1), ShapeType.Square, ShapeColor.Yellow)
            });
        Target purple = CreateTarget(
            new Vector2Int(5, 7),
            new[]
            {
                Colored(Vector2Int.zero, ShapeType.Square, ShapeColor.Purple),
                Colored(new Vector2Int(0, 1), ShapeType.Square, ShapeColor.Purple)
            });
        board.TryRegisterTarget(yellow);
        board.TryRegisterTarget(purple);
        try
        {
            bool consumed = yellow.TryConsumeLayerAtWorld(
                new Vector2Int(4, 7),
                new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
                out bool complete);
            bool purpleSurvives = purple.CellCount == 2
                && purple.FindCellIndexAtWorld(new Vector2Int(5, 7)) >= 0
                && board.GetTargetAt(new Vector2Int(5, 7)) == purple;
            bool ok = consumed && !complete && yellow.CellCount == 1 && purpleSurvives;
            report.AppendLine(
                $"  T6: consumed={consumed} yellowCells={yellow.CellCount} purpleSurvives={purpleSurvives}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(yellow.gameObject);
            Object.DestroyImmediate(purple.gameObject);
        }
    }

    private static bool TestOccupyingAlignedRespectsColor(StringBuilder report)
    {
        Vector2Int cell = new Vector2Int(1, 1);
        bool same = BlockMover.IsOccupyingAlignedMatch(
            cell,
            cell,
            new MatchIdentity(ShapeType.Diamond, ShapeColor.Green),
            new MatchIdentity(ShapeType.Diamond, ShapeColor.Green));
        bool diff = !BlockMover.IsOccupyingAlignedMatch(
            cell,
            cell,
            new MatchIdentity(ShapeType.Diamond, ShapeColor.Green),
            new MatchIdentity(ShapeType.Diamond, ShapeColor.Red));
        report.AppendLine($"  T7: same={same} diffReject={diff}");
        return same && diff;
    }

    private static bool TestLevel43Identities(StringBuilder report)
    {
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(Level43Path);
        if (level?.blocks == null || level.targets == null)
        {
            report.AppendLine("  T8: missing Level 43");
            return false;
        }

        bool yellowSquare = HasBlock(level, ShapeType.Square, ShapeColor.Yellow);
        bool purpleInner = HasNestedInner(level, ShapeType.Square, ShapeColor.Yellow, ShapeType.Square, ShapeColor.Purple);
        bool greenDiamond = HasBlock(level, ShapeType.Diamond, ShapeColor.Green);
        bool redInner = HasNestedInner(level, ShapeType.Diamond, ShapeColor.Green, ShapeType.Diamond, ShapeColor.Red);
        bool cyanCircle = HasBlock(level, ShapeType.Circle, ShapeColor.Cyan);
        bool pinkInner = HasNestedInner(level, ShapeType.Circle, ShapeColor.Cyan, ShapeType.Circle, ShapeColor.Pink);
        bool orangePent = HasBlock(level, ShapeType.Pentagon, ShapeColor.Orange);
        bool cyanPentInner = HasNestedInner(
            level,
            ShapeType.Pentagon,
            ShapeColor.Orange,
            ShapeType.Pentagon,
            ShapeColor.Cyan);

        bool yellowTarget = HasTarget(level, ShapeType.Square, ShapeColor.Yellow);
        bool purpleTarget = HasTarget(level, ShapeType.Square, ShapeColor.Purple);
        bool greenTarget = HasTarget(level, ShapeType.Diamond, ShapeColor.Green);
        bool redTarget = HasTarget(level, ShapeType.Diamond, ShapeColor.Red);

        bool ok = yellowSquare && purpleInner && greenDiamond && redInner
            && cyanCircle && pinkInner && orangePent && cyanPentInner
            && yellowTarget && purpleTarget && greenTarget && redTarget;
        report.AppendLine(
            $"  T8: YSq={yellowSquare} PInner={purpleInner} GDia={greenDiamond} RInner={redInner} " +
            $"CCirc={cyanCircle} PInnerC={pinkInner} OPent={orangePent} CPent={cyanPentInner} " +
            $"YT={yellowTarget} PT={purpleTarget} GT={greenTarget} RT={redTarget}");
        return ok;
    }

    private static bool HasBlock(LevelData level, ShapeType shape, ShapeColor color)
    {
        for (int i = 0; i < level.blocks.Count; i++)
        {
            LevelBlockData block = level.blocks[i];
            if (block?.cells == null)
            {
                continue;
            }

            for (int c = 0; c < block.cells.Count; c++)
            {
                ShapeCellData cell = block.cells[c];
                if (cell != null && cell.shapeType == shape && cell.outerColor == color)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasNestedInner(
        LevelData level,
        ShapeType outer,
        ShapeColor outerColor,
        ShapeType inner,
        ShapeColor innerColor)
    {
        for (int i = 0; i < level.blocks.Count; i++)
        {
            LevelBlockData block = level.blocks[i];
            if (block?.cells == null)
            {
                continue;
            }

            for (int c = 0; c < block.cells.Count; c++)
            {
                ShapeCellData cell = block.cells[c];
                if (cell == null
                    || cell.shapeType != outer
                    || cell.outerColor != outerColor
                    || cell.innerShapes == null
                    || cell.innerShapes.Count == 0
                    || cell.innerShapes[0] != inner
                    || cell.innerShapeColors == null
                    || cell.innerShapeColors.Count == 0
                    || cell.innerShapeColors[0] != innerColor)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool HasTarget(LevelData level, ShapeType shape, ShapeColor color)
    {
        for (int i = 0; i < level.targets.Count; i++)
        {
            LevelTargetData target = level.targets[i];
            if (target?.cells == null)
            {
                continue;
            }

            for (int c = 0; c < target.cells.Count; c++)
            {
                ShapeCellData cell = target.cells[c];
                if (cell != null && cell.shapeType == shape && cell.outerColor == color)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ShapeCellData Colored(Vector2Int local, ShapeType shape, ShapeColor color)
    {
        return new ShapeCellData
        {
            localPosition = local,
            shapeType = shape,
            outerColor = color,
            innerShapes = new List<ShapeType>(),
            innerShapeColors = new List<ShapeColor>()
        };
    }

    private static Target CreateTarget(Vector2Int anchor, ShapeCellData[] cells)
    {
        var go = new GameObject("Phase65_Target");
        go.AddComponent<RectTransform>();
        go.AddComponent<UIPieceView>();
        Target target = go.AddComponent<Target>();
        var list = new List<ShapeCellData>(cells);
        target.ApplyLayout(cells[0].shapeType, list, PieceComposition.Simple, cells[0].shapeType);
        target.Initialize(null, anchor);
        return target;
    }

    private static void WriteReport(string text)
    {
        string dir = Path.GetDirectoryName(ReportPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(ReportPath, text);
        AssetDatabase.Refresh();
    }
}
