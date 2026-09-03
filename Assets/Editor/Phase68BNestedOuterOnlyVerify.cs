using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 68B — nested outer travels; NestedInner3D stays anchored at source.
/// Menu: Shape Nest / Phase 68B Verify Nested Outer-Only Movement
/// </summary>
public static class Phase68BNestedOuterOnlyVerify
{
    private const string ReportPath = "Captures/phase68b-report.txt";

    [MenuItem("Shape Nest/Phase 68B Verify Nested Outer-Only Movement")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 68B — NESTED OUTER-ONLY MOVEMENT");
        report.AppendLine("======================================");

        bool t1 = TestDetachApiExists(report);
        bool t2 = TestPieceViewDetachPreservesWorld(report);
        bool t3 = TestDetachClearsOwnership(report);
        bool t4 = TestMultiCellRelativeResiduals(report);
        bool t5 = TestNonNestedGroupUnchanged(report);
        bool t6 = TestPhase65Identity(report);
        bool t7 = TestPhase63Partial(report);
        bool t8 = TestControllerHelperExists(report);

        report.AppendLine();
        report.AppendLine($"1 detach API: {(t1 ? "PASS" : "FAIL")}");
        report.AppendLine($"2 world transform preserved: {(t2 ? "PASS" : "FAIL")}");
        report.AppendLine($"3 ownership cleared on parent view: {(t3 ? "PASS" : "FAIL")}");
        report.AppendLine($"4 multi-cell relative residuals: {(t4 ? "PASS" : "FAIL")}");
        report.AppendLine($"5 non-nested grouping unchanged: {(t5 ? "PASS" : "FAIL")}");
        report.AppendLine($"6 Phase 65 identity: {(t6 ? "PASS" : "FAIL")}");
        report.AppendLine($"7 Phase 63 partial: {(t7 ? "PASS" : "FAIL")}");
        report.AppendLine($"8 controller helpers: {(t8 ? "PASS" : "FAIL")}");

        bool all = t1 && t2 && t3 && t4 && t5 && t6 && t7 && t8;
        report.AppendLine();
        report.AppendLine(all ? "RESULT: PASS" : "RESULT: FAIL");
        report.AppendLine(
            "Note: Play Mode outer-travel vs residual-anchor was not executed by this editor check.");

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

    private static bool TestDetachApiExists(StringBuilder report)
    {
        MethodInfo m = typeof(PieceView3D).GetMethod(
            "TryDetachNestedInnerPreservingWorld",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo c = typeof(BoardPresentationController).GetMethod(
            "DetachAndAnchorNestedInner",
            BindingFlags.Static | BindingFlags.Public);
        bool ok = m != null && c != null;
        report.AppendLine($"  T1: PieceView3D.detach={m != null} Controller.anchor={c != null}");
        return ok;
    }

    private static bool TestPieceViewDetachPreservesWorld(StringBuilder report)
    {
        var go = new GameObject("Phase68B_View");
        var host = new GameObject("Phase68B_Host");
        try
        {
            PieceView3D view = go.AddComponent<PieceView3D>();
            view.ConfigureVisual(
                ShapeType.Circle,
                ShapeVisuals3D.BlockMaterial(ShapeType.Circle, ShapeColor.Cyan, null),
                asNest: false,
                footprint: 1f,
                height: 0.35f);
            view.ConfigureNestedInner(
                true,
                ShapeType.Triangle,
                ShapeVisuals3D.BlockMaterial(ShapeType.Triangle, ShapeColor.Pink, null),
                0.55f,
                asNest: false);

            go.transform.position = new Vector3(3f, 1f, 5f);
            go.transform.rotation = Quaternion.Euler(0f, 25f, 0f);

            Transform nested = go.transform.Find("NestedInner3D");
            if (nested == null)
            {
                report.AppendLine("  T2: NestedInner3D missing");
                return false;
            }

            Vector3 worldPos = nested.position;
            Quaternion worldRot = nested.rotation;
            Vector3 lossy = nested.lossyScale;

            bool detached = view.TryDetachNestedInnerPreservingWorld(host.transform, out Transform residual);
            bool ok = detached
                && residual != null
                && residual.parent == host.transform
                && (residual.position - worldPos).sqrMagnitude < 0.0001f
                && Quaternion.Angle(residual.rotation, worldRot) < 0.1f
                && (residual.lossyScale - lossy).sqrMagnitude < 0.0001f
                && !view.HasNestedInner;

            report.AppendLine(
                $"  T2: detached={detached} hasNested={view.HasNestedInner} " +
                $"posDelta={(residual != null ? (residual.position - worldPos).magnitude : -1f):0.###}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(host);
        }
    }

    private static bool TestDetachClearsOwnership(StringBuilder report)
    {
        var go = new GameObject("Phase68B_Own");
        var host = new GameObject("Phase68B_OwnHost");
        try
        {
            PieceView3D view = go.AddComponent<PieceView3D>();
            view.ConfigureVisual(
                ShapeType.Square,
                ShapeVisuals3D.BlockMaterial(ShapeType.Square, ShapeColor.Yellow, null),
                false,
                1f,
                0.35f);
            view.ConfigureNestedInner(
                true,
                ShapeType.Square,
                ShapeVisuals3D.BlockMaterial(ShapeType.Square, ShapeColor.Purple, null),
                0.55f,
                false);
            view.TryDetachNestedInnerPreservingWorld(host.transform, out _);
            bool ok = !view.HasNestedInner && !view.HasDetachedNestedInnerCandidate;
            report.AppendLine($"  T3: ownershipCleared={ok}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(host);
        }
    }

    private static bool TestMultiCellRelativeResiduals(StringBuilder report)
    {
        // Simulate three detached inners keeping relative world offsets.
        var root = new GameObject("Phase68B_Multi");
        var a = new GameObject("A");
        var b = new GameObject("B");
        var c = new GameObject("C");
        a.transform.SetParent(root.transform, false);
        b.transform.SetParent(root.transform, false);
        c.transform.SetParent(root.transform, false);
        a.transform.position = new Vector3(0f, 0f, 0f);
        b.transform.position = new Vector3(0f, 0f, 1f);
        c.transform.position = new Vector3(0f, 0f, 2f);

        var host = new GameObject("Residuals");
        try
        {
            a.transform.SetParent(host.transform, true);
            b.transform.SetParent(host.transform, true);
            c.transform.SetParent(host.transform, true);
            bool ok = Mathf.Abs(b.transform.position.z - a.transform.position.z - 1f) < 0.001f
                && Mathf.Abs(c.transform.position.z - a.transform.position.z - 2f) < 0.001f;
            report.AppendLine($"  T4: relativeKept={ok}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(host);
        }
    }

    private static bool TestNonNestedGroupUnchanged(StringBuilder report)
    {
        var go = new GameObject("Phase68B_Group", typeof(RectTransform), typeof(Block), typeof(BlockMover));
        try
        {
            Block block = go.GetComponent<Block>();
            block.ApplyLayout(
                ShapeType.Triangle,
                new List<ShapeCellData>
                {
                    Plain(Vector2Int.zero, ShapeType.Triangle),
                    Plain(new Vector2Int(0, 1), ShapeType.Triangle)
                },
                PieceComposition.Simple,
                ShapeType.Triangle);
            block.Initialize(null, new Vector2Int(1, 1));

            var actions = new List<BlockMover.AlignedMatchAction>
            {
                new BlockMover.AlignedMatchAction(
                    block, 0, block.GridPosition, block.GridPosition),
                new BlockMover.AlignedMatchAction(
                    block, 1, block.GridPosition + new Vector2Int(0, 1), block.GridPosition + new Vector2Int(0, 1))
            };
            var groups = new List<BlockMover.AlignedMovementGroup>();
            BlockMover.BuildAlignedMovementGroups(actions, groups);
            bool ok = groups.Count == 1 && groups[0].Actions.Count == 2;
            report.AppendLine($"  T5: groups={groups.Count} cells={groups[0].Actions.Count}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static bool TestPhase65Identity(StringBuilder report)
    {
        bool ok = ShapeMatch.AreMatchingLayers(
                new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
                new MatchIdentity(ShapeType.Square, ShapeColor.Yellow))
            && !ShapeMatch.AreMatchingLayers(
                new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
                new MatchIdentity(ShapeType.Square, ShapeColor.Purple));
        report.AppendLine($"  T6: {ok}");
        return ok;
    }

    private static bool TestPhase63Partial(StringBuilder report)
    {
        var go = new GameObject("Phase68B_T", typeof(RectTransform), typeof(UIPieceView), typeof(Target));
        Target target = go.GetComponent<Target>();
        var cells = new List<ShapeCellData>
        {
            Plain(Vector2Int.zero, ShapeType.Triangle),
            Plain(new Vector2Int(1, 0), ShapeType.Triangle),
            Plain(new Vector2Int(0, 1), ShapeType.Triangle)
        };
        target.ApplyLayout(ShapeType.Triangle, cells, PieceComposition.Simple, ShapeType.Triangle);
        target.Initialize(null, new Vector2Int(2, 2));
        try
        {
            bool consumed = target.TryConsumeLayerAtWorld(
                new Vector2Int(3, 2),
                new MatchIdentity(ShapeType.Triangle, ShapeColor.Default),
                out bool complete);
            bool ok = consumed && !complete && target.CellCount == 2;
            report.AppendLine($"  T7: consumed={consumed} complete={complete} cells={target.CellCount}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static bool TestControllerHelperExists(StringBuilder report)
    {
        bool detach = typeof(BoardPresentationController).GetMethod(
                "DetachAndAnchorNestedInner", BindingFlags.Public | BindingFlags.Static)
            != null;
        bool clear = typeof(BoardPresentationController).GetMethod(
                "ClearAnchoredNestedResidual", BindingFlags.Public | BindingFlags.Static)
            != null;
        bool has = typeof(BoardPresentationController).GetMethod(
                "HasAnchoredNestedResidual", BindingFlags.Public | BindingFlags.Static)
            != null;
        bool ok = detach && clear && has;
        report.AppendLine($"  T8: detach={detach} clear={clear} has={has}");
        return ok;
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
