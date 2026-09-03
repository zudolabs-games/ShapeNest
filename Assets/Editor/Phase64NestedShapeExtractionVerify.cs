using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 64 — nested shape extraction: outer matches first; promote inner to full-size active layer.
/// Menu: Shape Nest / Phase 64 Verify Nested Shape Extraction
/// </summary>
public static class Phase64NestedShapeExtractionVerify
{
    private const string ReportPath = "Captures/phase64-report.txt";
    private const string Level43Path = "Assets/Levels/Campaign_43_Reference.asset";

    [MenuItem("Shape Nest/Phase 64 Verify Nested Shape Extraction")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 64 — NESTED SHAPE EXTRACTION");
        report.AppendLine("==================================");

        bool t1 = Test1_CircleTriangleOuterFirst(report);
        bool t2 = Test2_MultiCellPartialPromote(report);
        bool t3 = Test3_MultiLayerSequential(report);
        bool t4 = Test4_NormalBlockRegression(report);
        bool t5 = Test5_Level43NestedPieces(report);

        report.AppendLine();
        report.AppendLine($"Test 1 Circle→Triangle outer-first: {(t1 ? "PASS" : "FAIL")}");
        report.AppendLine($"Test 2 multi-cell partial promote: {(t2 ? "PASS" : "FAIL")}");
        report.AppendLine($"Test 3 multi-layer A→B→C: {(t3 ? "PASS" : "FAIL")}");
        report.AppendLine($"Test 4 normal block regression: {(t4 ? "PASS" : "FAIL")}");
        report.AppendLine($"Test 5 Level 43 nested pieces: {(t5 ? "PASS" : "FAIL")}");

        bool all = t1 && t2 && t3 && t4 && t5;
        report.AppendLine();
        report.AppendLine(all ? "RESULT: PASS" : "RESULT: FAIL");
        report.AppendLine("Note: Play Mode drag/visual verification was not executed by this editor check.");

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

    private static bool Test1_CircleTriangleOuterFirst(StringBuilder report)
    {
        var cell = new ShapeCellData
        {
            localPosition = Vector2Int.zero,
            shapeType = ShapeType.Circle,
            outerColor = ShapeColor.Cyan,
            innerShapes = new List<ShapeType> { ShapeType.Triangle },
            innerShapeColors = new List<ShapeColor> { ShapeColor.Pink }
        };

        bool activeIsOuter = ShapeLayout.ActiveShape(cell, ShapeType.Square) == ShapeType.Circle;
        bool childIsTriangle = ShapeLayout.NestedChildShape(cell, ShapeType.Square) == ShapeType.Triangle;
        bool innerCannotMatch = !ShapeLayout.TryConsumeLayer(cell, ShapeType.Triangle, out _);
        bool outerMatches = ShapeLayout.TryConsumeLayer(cell, ShapeType.Circle, out bool remains);
        bool promoted =
            outerMatches
            && remains
            && cell.shapeType == ShapeType.Triangle
            && cell.outerColor == ShapeColor.Pink
            && (cell.innerShapes == null || cell.innerShapes.Count == 0);

        bool ok = activeIsOuter && childIsTriangle && innerCannotMatch && promoted;
        report.AppendLine(
            $"  T1: activeOuter={activeIsOuter} nestedChild={childIsTriangle} " +
            $"innerReject={innerCannotMatch} promote={promoted} " +
            $"shape={cell.shapeType} color={cell.outerColor}");
        return ok;
    }

    private static bool Test2_MultiCellPartialPromote(StringBuilder report)
    {
        var cells = new List<ShapeCellData>
        {
            Nested(Vector2Int.zero, ShapeType.Circle, ShapeColor.Cyan, ShapeType.Triangle, ShapeColor.Pink),
            Nested(new Vector2Int(1, 0), ShapeType.Circle, ShapeColor.Cyan, ShapeType.Triangle, ShapeColor.Pink),
            Nested(new Vector2Int(2, 0), ShapeType.Circle, ShapeColor.Cyan, ShapeType.Triangle, ShapeColor.Pink)
        };

        bool consumed = ShapeLayout.TryConsumeLayer(cells[0], ShapeType.Circle, out bool remains);
        bool ok = consumed
            && remains
            && cells[0].shapeType == ShapeType.Triangle
            && cells[0].outerColor == ShapeColor.Pink
            && cells[1].shapeType == ShapeType.Circle
            && cells[1].innerShapes != null
            && cells[1].innerShapes.Count == 1
            && cells[1].innerShapes[0] == ShapeType.Triangle
            && cells[2].shapeType == ShapeType.Circle
            && cells[2].innerShapes != null
            && cells[2].innerShapes.Count == 1;

        report.AppendLine(
            $"  T2: consumed={consumed} remains={remains} " +
            $"c0={cells[0].shapeType} c1={cells[1].shapeType}/{cells[1].innerShapes?.Count} " +
            $"c2={cells[2].shapeType}/{cells[2].innerShapes?.Count}");
        return ok;
    }

    private static bool Test3_MultiLayerSequential(StringBuilder report)
    {
        var cell = new ShapeCellData
        {
            localPosition = Vector2Int.zero,
            shapeType = ShapeType.Circle,
            outerColor = ShapeColor.Cyan,
            innerShapes = new List<ShapeType> { ShapeType.Triangle, ShapeType.Star },
            innerShapeColors = new List<ShapeColor> { ShapeColor.Pink, ShapeColor.Yellow }
        };

        bool a = ShapeLayout.TryConsumeLayer(cell, ShapeType.Circle, out bool r1)
            && r1
            && cell.shapeType == ShapeType.Triangle
            && cell.outerColor == ShapeColor.Pink
            && cell.innerShapes != null
            && cell.innerShapes.Count == 1
            && cell.innerShapes[0] == ShapeType.Star;

        bool skipStar = !ShapeLayout.TryConsumeLayer(cell, ShapeType.Star, out _);

        bool b = ShapeLayout.TryConsumeLayer(cell, ShapeType.Triangle, out bool r2)
            && r2
            && cell.shapeType == ShapeType.Star
            && cell.outerColor == ShapeColor.Yellow
            && (cell.innerShapes == null || cell.innerShapes.Count == 0);

        bool c = ShapeLayout.TryConsumeLayer(cell, ShapeType.Star, out bool r3)
            && !r3
            && cell.shapeType == ShapeType.Star;

        bool ok = a && skipStar && b && c;
        report.AppendLine($"  T3: A→B={a} skipStar={skipStar} B→C={b} CConsume={c}");
        return ok;
    }

    private static bool Test4_NormalBlockRegression(StringBuilder report)
    {
        var cell = new ShapeCellData
        {
            localPosition = Vector2Int.zero,
            shapeType = ShapeType.Square,
            outerColor = ShapeColor.Yellow,
            innerShapes = new List<ShapeType>(),
            innerShapeColors = new List<ShapeColor>()
        };

        bool wrong = !ShapeLayout.TryConsumeLayer(cell, ShapeType.Circle, out _);
        bool consume = ShapeLayout.TryConsumeLayer(cell, ShapeType.Square, out bool remains);
        bool ok = wrong && consume && !remains;

        var target = CreateTarget(
            new Vector2Int(0, 0),
            new[]
            {
                Cell(Vector2Int.zero, ShapeType.Square),
                Cell(new Vector2Int(1, 0), ShapeType.Square)
            });
        try
        {
            bool partial = target.TryConsumeLayerAtWorld(
                new Vector2Int(0, 0),
                ShapeType.Square,
                out bool complete);
            ok = ok
                && partial
                && !complete
                && target.CellCount == 1
                && target.FindCellIndexAtWorld(new Vector2Int(1, 0)) >= 0;
            report.AppendLine(
                $"  T4: wrongReject={wrong} simpleConsume={consume} remains={remains} " +
                $"phase63Partial={partial} complete={complete} cells={target.CellCount}");
        }
        finally
        {
            Object.DestroyImmediate(target.gameObject);
        }

        return ok;
    }

    private static bool Test5_Level43NestedPieces(StringBuilder report)
    {
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(Level43Path);
        if (level == null || level.blocks == null)
        {
            report.AppendLine("  T5: FAILED — Campaign_43_Reference missing");
            return false;
        }

        bool greenRed = HasNestedBlock(
            level,
            ShapeType.Diamond,
            ShapeColor.Green,
            ShapeType.Diamond,
            ShapeColor.Red);
        bool orangeCyan = HasNestedBlock(
            level,
            ShapeType.Pentagon,
            ShapeColor.Orange,
            ShapeType.Pentagon,
            ShapeColor.Cyan);
        bool yellowPurple = HasNestedBlock(
            level,
            ShapeType.Square,
            ShapeColor.Yellow,
            ShapeType.Square,
            ShapeColor.Purple);
        bool bluePink = HasNestedBlock(
            level,
            ShapeType.Circle,
            ShapeColor.Cyan,
            ShapeType.Circle,
            ShapeColor.Pink);

        // Simulate extraction color preservation for green→red diamond cell.
        var sample = Nested(Vector2Int.zero, ShapeType.Diamond, ShapeColor.Green, ShapeType.Diamond, ShapeColor.Red);
        bool extract = ShapeLayout.TryConsumeLayer(sample, ShapeType.Diamond, out bool remains)
            && remains
            && sample.shapeType == ShapeType.Diamond
            && sample.outerColor == ShapeColor.Red;

        bool ok = greenRed && orangeCyan && yellowPurple && bluePink && extract;
        report.AppendLine(
            $"  T5: green→red={greenRed} orange→cyan={orangeCyan} " +
            $"yellow→purple={yellowPurple} cyan→pink={bluePink} extractSim={extract}");
        return ok;
    }

    private static bool HasNestedBlock(
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
                    || cell.innerShapes[0] != inner)
                {
                    continue;
                }

                if (cell.innerShapeColors != null
                    && cell.innerShapeColors.Count > 0
                    && cell.innerShapeColors[0] == innerColor)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ShapeCellData Nested(
        Vector2Int local,
        ShapeType outer,
        ShapeColor outerColor,
        ShapeType inner,
        ShapeColor innerColor)
    {
        return new ShapeCellData
        {
            localPosition = local,
            shapeType = outer,
            outerColor = outerColor,
            innerShapes = new List<ShapeType> { inner },
            innerShapeColors = new List<ShapeColor> { innerColor }
        };
    }

    private static ShapeCellData Cell(Vector2Int local, ShapeType shape)
    {
        return new ShapeCellData
        {
            localPosition = local,
            shapeType = shape,
            outerColor = ShapeColor.Default,
            innerShapes = new List<ShapeType>(),
            innerShapeColors = new List<ShapeColor>()
        };
    }

    private static Target CreateTarget(Vector2Int anchor, ShapeCellData[] cells)
    {
        var go = new GameObject("Phase64_Target");
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
        Debug.Log($"Phase 64 report written to {ReportPath}");
    }
}
