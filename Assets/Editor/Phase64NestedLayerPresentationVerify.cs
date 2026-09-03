using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 64 polish — nested layer reveal presentation contracts (gameplay + deferral).
/// Menu: Shape Nest / Phase 64 Verify Nested Layer Presentation
/// </summary>
public static class Phase64NestedLayerPresentationVerify
{
    private const string ReportPath = "Captures/phase64-presentation-report.txt";
    private const string Level43Path = "Assets/Levels/Campaign_43_Reference.asset";

    [MenuItem("Shape Nest/Phase 64 Verify Nested Layer Presentation")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 64 — NESTED LAYER REVEAL PRESENTATION");
        report.AppendLine("===========================================");

        bool a = TestOuterConsumeLeavesInner(report);
        bool b = TestPartialMultiCellOnlyMatchedPromotes(report);
        bool c = TestMultiDepthSequential(report);
        bool d = TestStandaloneUnchanged(report);
        bool e = TestPhase63PartialTargetIntact(report);
        bool f = TestLevel43NestedStillPresent(report);
        bool g = TestPendingExtractionApi(report);

        report.AppendLine();
        report.AppendLine($"A outer consume leaves inner: {(a ? "PASS" : "FAIL")}");
        report.AppendLine($"B partial multi-cell promote: {(b ? "PASS" : "FAIL")}");
        report.AppendLine($"C multi-depth A→B→C: {(c ? "PASS" : "FAIL")}");
        report.AppendLine($"D standalone unchanged: {(d ? "PASS" : "FAIL")}");
        report.AppendLine($"E Phase 63 partial target: {(e ? "PASS" : "FAIL")}");
        report.AppendLine($"F Level 43 nested present: {(f ? "PASS" : "FAIL")}");
        report.AppendLine($"G pending extraction API: {(g ? "PASS" : "FAIL")}");

        bool all = a && b && c && d && e && f && g;
        report.AppendLine();
        report.AppendLine(all ? "RESULT: PASS" : "RESULT: FAIL");
        report.AppendLine("Note: Play Mode visual timing was not executed by this editor check.");

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

    private static bool TestOuterConsumeLeavesInner(StringBuilder report)
    {
        var cell = Nested(ShapeType.Circle, ShapeColor.Cyan, ShapeType.Triangle, ShapeColor.Pink);
        Vector2Int pos = new Vector2Int(3, 2);
        bool rejectInner = !ShapeLayout.TryConsumeLayer(cell, ShapeType.Triangle, out _);
        bool promote = ShapeLayout.TryConsumeLayer(cell, ShapeType.Circle, out bool remains)
            && remains
            && cell.shapeType == ShapeType.Triangle
            && cell.outerColor == ShapeColor.Pink;
        report.AppendLine($"  A: rejectInner={rejectInner} promote={promote} posKept={pos}");
        return rejectInner && promote;
    }

    private static bool TestPartialMultiCellOnlyMatchedPromotes(StringBuilder report)
    {
        var cells = new List<ShapeCellData>
        {
            Nested(ShapeType.Circle, ShapeColor.Cyan, ShapeType.Triangle, ShapeColor.Pink),
            Nested(ShapeType.Circle, ShapeColor.Cyan, ShapeType.Triangle, ShapeColor.Pink),
            Nested(ShapeType.Circle, ShapeColor.Cyan, ShapeType.Triangle, ShapeColor.Pink)
        };
        cells[0].localPosition = Vector2Int.zero;
        cells[1].localPosition = new Vector2Int(1, 0);
        cells[2].localPosition = new Vector2Int(2, 0);

        bool ok = ShapeLayout.TryConsumeLayer(cells[1], ShapeType.Circle, out bool remains)
            && remains
            && cells[1].shapeType == ShapeType.Triangle
            && cells[0].shapeType == ShapeType.Circle
            && cells[0].innerShapes.Count == 1
            && cells[2].shapeType == ShapeType.Circle
            && cells[2].innerShapes.Count == 1;
        report.AppendLine($"  B: midPromoted={cells[1].shapeType} siblingsNested={cells[0].innerShapes.Count}/{cells[2].innerShapes.Count}");
        return ok;
    }

    private static bool TestMultiDepthSequential(StringBuilder report)
    {
        var cell = new ShapeCellData
        {
            shapeType = ShapeType.Circle,
            outerColor = ShapeColor.Cyan,
            innerShapes = new List<ShapeType> { ShapeType.Triangle, ShapeType.Star },
            innerShapeColors = new List<ShapeColor> { ShapeColor.Pink, ShapeColor.Yellow }
        };
        bool a = ShapeLayout.TryConsumeLayer(cell, ShapeType.Circle, out bool r1) && r1 && cell.shapeType == ShapeType.Triangle;
        bool skip = !ShapeLayout.TryConsumeLayer(cell, ShapeType.Star, out _);
        bool b = ShapeLayout.TryConsumeLayer(cell, ShapeType.Triangle, out bool r2) && r2 && cell.shapeType == ShapeType.Star;
        bool c = ShapeLayout.TryConsumeLayer(cell, ShapeType.Star, out bool r3) && !r3;
        report.AppendLine($"  C: A={a} skip={skip} B={b} C={c}");
        return a && skip && b && c;
    }

    private static bool TestStandaloneUnchanged(StringBuilder report)
    {
        var cell = new ShapeCellData
        {
            shapeType = ShapeType.Square,
            outerColor = ShapeColor.Yellow,
            innerShapes = new List<ShapeType>(),
            innerShapeColors = new List<ShapeColor>()
        };
        bool ok = ShapeLayout.TryConsumeLayer(cell, ShapeType.Square, out bool remains) && !remains;
        report.AppendLine($"  D: consume={!remains}");
        return ok;
    }

    private static bool TestPhase63PartialTargetIntact(StringBuilder report)
    {
        var go = new GameObject("Phase64Pres_Target");
        go.AddComponent<RectTransform>();
        go.AddComponent<UIPieceView>();
        Target target = go.AddComponent<Target>();
        var cells = new List<ShapeCellData>
        {
            Plain(Vector2Int.zero, ShapeType.Diamond),
            Plain(new Vector2Int(0, 1), ShapeType.Diamond)
        };
        target.ApplyLayout(ShapeType.Diamond, cells, PieceComposition.Simple, ShapeType.Diamond);
        target.Initialize(null, new Vector2Int(0, 7));
        try
        {
            bool consumed = target.TryConsumeLayerAtWorld(new Vector2Int(0, 7), ShapeType.Diamond, out bool complete);
            bool ok = consumed && !complete && target.CellCount == 1
                && target.FindCellIndexAtWorld(new Vector2Int(0, 8)) >= 0;
            report.AppendLine($"  E: consumed={consumed} complete={complete} cells={target.CellCount}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static bool TestLevel43NestedStillPresent(StringBuilder report)
    {
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(Level43Path);
        if (level?.blocks == null)
        {
            report.AppendLine("  F: missing Level 43");
            return false;
        }

        int nestedCells = 0;
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
                if (cell?.innerShapes != null && cell.innerShapes.Count > 0)
                {
                    nestedCells++;
                }
            }
        }

        bool ok = nestedCells >= 4;
        report.AppendLine($"  F: nestedCells={nestedCells}");
        return ok;
    }

    private static bool TestPendingExtractionApi(StringBuilder report)
    {
        var go = new GameObject("Phase64Pres_Block");
        go.AddComponent<RectTransform>();
        go.AddComponent<UIPieceView>();
        go.AddComponent<UIPieceMotion>();
        Block block = go.AddComponent<Block>();
        try
        {
            var cells = new List<ShapeCellData>
            {
                Nested(ShapeType.Circle, ShapeColor.Cyan, ShapeType.Triangle, ShapeColor.Pink)
            };
            block.ApplyLayout(ShapeType.Circle, cells, PieceComposition.Simple, ShapeType.Circle);
            bool before = !block.IsPendingLayerExtraction(0) && !block.HasPendingLayerExtraction;
            block.BeginPendingLayerExtraction(0);
            bool pending = block.IsPendingLayerExtraction(0) && block.HasPendingLayerExtraction;
            block.ClearPendingLayerExtraction(0);
            bool after = !block.IsPendingLayerExtraction(0) && !block.HasPendingLayerExtraction;
            report.AppendLine($"  G: before={before} pending={pending} after={after}");
            return before && pending && after;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static ShapeCellData Nested(ShapeType outer, ShapeColor outerColor, ShapeType inner, ShapeColor innerColor)
    {
        return new ShapeCellData
        {
            localPosition = Vector2Int.zero,
            shapeType = outer,
            outerColor = outerColor,
            innerShapes = new List<ShapeType> { inner },
            innerShapeColors = new List<ShapeColor> { innerColor }
        };
    }

    private static ShapeCellData Plain(Vector2Int local, ShapeType shape)
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
