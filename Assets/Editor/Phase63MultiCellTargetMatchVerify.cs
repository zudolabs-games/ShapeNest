using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 63 — regression: multi-cell target consumption must be per-cell / per-target-group.
/// Menu: Shape Nest / Phase 63 Verify Multi-Cell Target Match
/// </summary>
public static class Phase63MultiCellTargetMatchVerify
{
    private const string ReportPath = "Captures/phase63-report.txt";

    [MenuItem("Shape Nest/Phase 63 Verify Multi-Cell Target Match")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 63 — MULTI-CELL TARGET MATCH REGRESSION");
        report.AppendLine("==============================================");

        bool a = TestPartialConsumeKeepsSiblingCell(report);
        bool b = TestUnrelatedSameShapeTargetSurvives(report);
        bool c = TestWrongShapeDoesNotConsume(report);
        bool d = TestFullFootprintRequiresAllCells(report);
        bool e = TestOccupyingShapeUsesMatchedCellNotAnchorOnly(report);

        report.AppendLine();
        report.AppendLine($"A partial consume keeps sibling: {(a ? "PASS" : "FAIL")}");
        report.AppendLine($"B unrelated same-shape target survives: {(b ? "PASS" : "FAIL")}");
        report.AppendLine($"C wrong shape does not consume: {(c ? "PASS" : "FAIL")}");
        report.AppendLine($"D full complete only after all cells: {(d ? "PASS" : "FAIL")}");
        report.AppendLine($"E matched-cell shape resolution: {(e ? "PASS" : "FAIL")}");

        bool all = a && b && c && d && e;
        report.AppendLine();
        report.AppendLine(all ? "RESULT: PASS" : "RESULT: FAIL");

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

    private static bool TestPartialConsumeKeepsSiblingCell(StringBuilder report)
    {
        Target target = CreateTarget(
            new Vector2Int(1, 4),
            new[]
            {
                Cell(Vector2Int.zero, ShapeType.Triangle),
                Cell(new Vector2Int(1, 0), ShapeType.Triangle),
                Cell(new Vector2Int(0, 1), ShapeType.Triangle),
                Cell(new Vector2Int(1, 1), ShapeType.Triangle)
            });

        try
        {
            bool consumed = target.TryConsumeLayerAtWorld(
                new Vector2Int(1, 4),
                ShapeType.Triangle,
                out bool complete);
            bool ok = consumed
                && !complete
                && target.CellCount == 3
                && target.FindCellIndexAtWorld(new Vector2Int(1, 4)) < 0
                && target.FindCellIndexAtWorld(new Vector2Int(2, 4)) >= 0
                && target.FindCellIndexAtWorld(new Vector2Int(1, 5)) >= 0
                && target.FindCellIndexAtWorld(new Vector2Int(2, 5)) >= 0;
            report.AppendLine($"  A details: consumed={consumed} complete={complete} cells={target.CellCount}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(target.gameObject);
        }
    }

    private static bool TestUnrelatedSameShapeTargetSurvives(StringBuilder report)
    {
        var root = new GameObject("Phase63_Board");
        var board = root.AddComponent<BoardManager>();
        board.ApplyGridSize(6, 10);

        Target matched = CreateTarget(
            new Vector2Int(4, 4),
            new[]
            {
                Cell(Vector2Int.zero, ShapeType.Triangle),
                Cell(new Vector2Int(0, 1), ShapeType.Triangle)
            });
        Target other = CreateTarget(
            new Vector2Int(1, 9),
            new[]
            {
                Cell(Vector2Int.zero, ShapeType.Triangle),
                Cell(new Vector2Int(1, 0), ShapeType.Triangle)
            });

        matched.Initialize(board, matched.GridPosition);
        other.Initialize(board, other.GridPosition);
        board.TryRegisterTarget(matched);
        board.TryRegisterTarget(other);

        try
        {
            bool consumed = matched.TryConsumeLayerAtWorld(
                new Vector2Int(4, 4),
                ShapeType.Triangle,
                out bool complete);
            bool matchedRemaining = matched.CellCount == 1
                && board.GetTargetAt(new Vector2Int(4, 5)) == matched
                && board.GetTargetAt(new Vector2Int(4, 4)) == null;
            bool otherIntact = other.CellCount == 2
                && board.GetTargetAt(new Vector2Int(1, 9)) == other
                && board.GetTargetAt(new Vector2Int(2, 9)) == other
                && !other.IsMatched;
            bool ok = consumed && !complete && matchedRemaining && otherIntact;
            report.AppendLine(
                $"  B details: consumed={consumed} complete={complete} " +
                $"matchedCells={matched.CellCount} otherCells={other.CellCount}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(matched.gameObject);
            Object.DestroyImmediate(other.gameObject);
            Object.DestroyImmediate(root);
        }
    }

    private static bool TestWrongShapeDoesNotConsume(StringBuilder report)
    {
        Target target = CreateTarget(
            Vector2Int.zero,
            new[]
            {
                Cell(Vector2Int.zero, ShapeType.Triangle),
                Cell(new Vector2Int(0, 1), ShapeType.Triangle)
            });

        try
        {
            bool consumed = target.TryConsumeLayerAtWorld(
                Vector2Int.zero,
                ShapeType.Circle,
                out bool complete);
            bool ok = !consumed && !complete && target.CellCount == 2;
            report.AppendLine($"  C details: consumed={consumed} complete={complete} cells={target.CellCount}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(target.gameObject);
        }
    }

    private static bool TestFullFootprintRequiresAllCells(StringBuilder report)
    {
        Target target = CreateTarget(
            new Vector2Int(0, 0),
            new[]
            {
                Cell(Vector2Int.zero, ShapeType.Circle),
                Cell(new Vector2Int(0, 1), ShapeType.Circle),
                Cell(new Vector2Int(0, 2), ShapeType.Circle)
            });

        try
        {
            bool c1 = target.TryConsumeLayerAtWorld(new Vector2Int(0, 0), ShapeType.Circle, out bool complete1);
            bool c2 = target.TryConsumeLayerAtWorld(new Vector2Int(0, 1), ShapeType.Circle, out bool complete2);
            bool c3 = target.TryConsumeLayerAtWorld(new Vector2Int(0, 2), ShapeType.Circle, out bool complete3);
            // After first two consumes, anchor may shift — resolve remaining cell from CellCount.
            bool okPartial = c1 && !complete1 && c2 && !complete2 && target.CellCount == 1;
            bool okFinal = false;
            if (okPartial)
            {
                Vector2Int last = target.GridPosition + target.GetLocalCell(0);
                okFinal = target.TryConsumeLayerAtWorld(last, ShapeType.Circle, out bool completeFinal)
                    && completeFinal
                    && target.CellCount == 0;
                report.AppendLine(
                    $"  D details: c1={c1}/{complete1} c2={c2}/{complete2} final={okFinal} last={last}");
            }
            else
            {
                report.AppendLine($"  D details: partial failed c1={c1}/{complete1} c2={c2}/{complete2} c3={c3}/{complete3}");
            }

            return okPartial && okFinal;
        }
        finally
        {
            Object.DestroyImmediate(target.gameObject);
        }
    }

    private static bool TestOccupyingShapeUsesMatchedCellNotAnchorOnly(StringBuilder report)
    {
        Target target = CreateTarget(
            new Vector2Int(3, 0),
            new[]
            {
                Cell(Vector2Int.zero, ShapeType.Square),
                Cell(new Vector2Int(0, 1), ShapeType.Circle),
                Cell(new Vector2Int(0, 2), ShapeType.Triangle)
            });

        try
        {
            ShapeType atMid = target.GetRequiredShapeAtWorld(new Vector2Int(3, 1));
            ShapeType atTop = target.GetRequiredShapeAtWorld(new Vector2Int(3, 2));
            bool consumedMid = target.TryConsumeLayerAtWorld(
                new Vector2Int(3, 1),
                ShapeType.Circle,
                out bool complete);
            bool ok = atMid == ShapeType.Circle
                && atTop == ShapeType.Triangle
                && consumedMid
                && !complete
                && target.CellCount == 2
                && target.FindCellIndexAtWorld(new Vector2Int(3, 1)) < 0
                && target.FindCellIndexAtWorld(new Vector2Int(3, 0)) >= 0
                && target.FindCellIndexAtWorld(new Vector2Int(3, 2)) >= 0;
            report.AppendLine($"  E details: mid={atMid} top={atTop} consumed={consumedMid} cells={target.CellCount}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(target.gameObject);
        }
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
        var go = new GameObject("Phase63_Target");
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
    }
}
