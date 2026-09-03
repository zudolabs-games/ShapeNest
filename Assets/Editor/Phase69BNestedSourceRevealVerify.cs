using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 69B — nested outer travels; inner reveals at SOURCE.
/// Menu: Shape Nest / Phase 69B Verify Nested Source Reveal
/// </summary>
public static class Phase69BNestedSourceRevealVerify
{
    private const string ReportPath = "Captures/phase69b-report.txt";

    [MenuItem("Shape Nest/Phase 69B Verify Nested Source Reveal")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 69B — NESTED SOURCE REVEAL");
        report.AppendLine("================================");

        bool t1 = TestConfigureNestedDoesNotAllocateWhenHidden(report);
        bool t2 = TestDetachThenHideSyncDoesNotRecreate(report);
        bool t3 = TestSnapWorldPresentationExists(report);
        bool t4 = TestSourceCellApisExist(report);
        bool t5 = TestRevealUsesSnapNotLockedApply(report);
        bool t6 = TestPhase65IdentityUntouched(report);
        bool t7 = TestPhase63PartialUntouched(report);
        bool t8 = TestPhase67GroupingUntouched(report);

        report.AppendLine();
        report.AppendLine($"1 ConfigureNestedInner(show:false) no allocate: {(t1 ? "PASS" : "FAIL")}");
        report.AppendLine($"2 detach + sync no duplicate NestedInner3D: {(t2 ? "PASS" : "FAIL")}");
        report.AppendLine($"3 SnapWorldPresentationToGrid exists: {(t3 ? "PASS" : "FAIL")}");
        report.AppendLine($"4 source-cell APIs exist: {(t4 ? "PASS" : "FAIL")}");
        report.AppendLine($"5 reveal uses Snap / source seating (source scan): {(t5 ? "PASS" : "FAIL")}");
        report.AppendLine($"6 Phase 65 Shape+Color untouched: {(t6 ? "PASS" : "FAIL")}");
        report.AppendLine($"7 Phase 63 partial consume present: {(t7 ? "PASS" : "FAIL")}");
        report.AppendLine($"8 Phase 67 grouping APIs present: {(t8 ? "PASS" : "FAIL")}");

        bool all = t1 && t2 && t3 && t4 && t5 && t6 && t7 && t8;
        report.AppendLine();
        report.AppendLine(all ? "RESULT: PASS" : "RESULT: FAIL");
        report.AppendLine(
            "Note: Play Mode Level 43 yellow→purple source reveal was not executed by this editor check.");

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

    private static bool TestConfigureNestedDoesNotAllocateWhenHidden(StringBuilder report)
    {
        var go = new GameObject("Phase69B_NoAlloc");
        try
        {
            PieceView3D view = go.AddComponent<PieceView3D>();
            view.ConfigureVisual(
                ShapeType.Square,
                ShapeVisuals3D.BlockMaterial(ShapeType.Square, ShapeColor.Yellow, null),
                asNest: false,
                footprint: 1f,
                height: 0.35f);

            int before = CountNestedChildren(go.transform);
            view.ConfigureNestedInner(false, ShapeType.Square, null, 0.55f, asNest: false);
            int after = CountNestedChildren(go.transform);
            bool ok = before == 0 && after == 0 && !view.HasNestedInner;
            report.AppendLine($"  T1: nestedChildren before={before} after={after} hasNested={view.HasNestedInner}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static bool TestDetachThenHideSyncDoesNotRecreate(StringBuilder report)
    {
        var go = new GameObject("Phase69B_DetachSync");
        var host = new GameObject("Phase69B_ResidualHost");
        try
        {
            PieceView3D view = go.AddComponent<PieceView3D>();
            view.ConfigureVisual(
                ShapeType.Square,
                ShapeVisuals3D.BlockMaterial(ShapeType.Square, ShapeColor.Yellow, null),
                asNest: false,
                footprint: 1f,
                height: 0.35f);
            view.ConfigureNestedInner(
                true,
                ShapeType.Square,
                ShapeVisuals3D.BlockMaterial(ShapeType.Square, ShapeColor.Purple, null),
                0.55f,
                asNest: false);

            Transform nested = go.transform.Find("NestedInner3D");
            int nestedId = nested != null ? nested.GetInstanceID() : 0;
            bool detached = view.TryDetachNestedInnerPreservingWorld(host.transform, out Transform residual);
            int underViewAfterDetach = CountNestedChildren(go.transform);

            // Simulate SyncBlockNestedInner(show:false) after residual exists.
            view.ConfigureNestedInner(false, ShapeType.Square, null, 0.55f, asNest: false);
            int underViewAfterSync = CountNestedChildren(go.transform);
            Transform recreated = go.transform.Find("NestedInner3D");

            bool ok = detached
                && residual != null
                && residual.GetInstanceID() == nestedId
                && underViewAfterDetach == 0
                && underViewAfterSync == 0
                && recreated == null
                && !view.HasNestedInner;

            report.AppendLine(
                $"  T2: detached={detached} underAfterDetach={underViewAfterDetach} " +
                $"underAfterSync={underViewAfterSync} recreated={(recreated != null)} " +
                $"sameResidual={residual != null && residual.GetInstanceID() == nestedId}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(host);
        }
    }

    private static bool TestSnapWorldPresentationExists(StringBuilder report)
    {
        MethodInfo snap = typeof(PieceView3D).GetMethod(
            "SnapWorldPresentationToGrid",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo apply = typeof(PieceView3D).GetMethod(
            "ApplyGridPosition",
            BindingFlags.Instance | BindingFlags.Public);
        bool ok = snap != null && apply != null;
        report.AppendLine($"  T3: SnapWorldPresentationToGrid={snap != null} ApplyGridPosition={apply != null}");
        return ok;
    }

    private static bool TestSourceCellApisExist(StringBuilder report)
    {
        MethodInfo resolve = typeof(BoardPresentationController).GetMethod(
            "ResolveNestedPromotionSourceCell",
            BindingFlags.Static | BindingFlags.Public);
        MethodInfo tryGet = typeof(BoardPresentationController).GetMethod(
            "TryGetAnchoredNestedSourceCell",
            BindingFlags.Static | BindingFlags.Public);
        MethodInfo notify = typeof(BoardPresentationController).GetMethod(
            "NotifyNestedLayerPromoted",
            BindingFlags.Static | BindingFlags.Public);
        bool notifyHasCellIndex = false;
        if (notify != null)
        {
            ParameterInfo[] ps = notify.GetParameters();
            notifyHasCellIndex = ps.Length >= 2;
        }

        bool ok = resolve != null && tryGet != null && notify != null && notifyHasCellIndex;
        report.AppendLine(
            $"  T4: Resolve={resolve != null} TryGet={tryGet != null} " +
            $"Notify={notify != null} Notify(cellIndex)={notifyHasCellIndex}");
        return ok;
    }

    private static bool TestRevealUsesSnapNotLockedApply(StringBuilder report)
    {
        string path = "Assets/Scripts/Blocks/BlockMover.cs";
        string text = File.ReadAllText(path);
        const string marker = "private IEnumerator PlayNestedExtractionReveal";
        int reveal = text.IndexOf(marker);
        if (reveal < 0)
        {
            report.AppendLine("  T5: PlayNestedExtractionReveal not found");
            return false;
        }

        int next = text.IndexOf("\n    private ", reveal + marker.Length);
        string body = next > reveal ? text.Substring(reveal, next - reveal) : text.Substring(reveal);
        bool usesSnap = body.Contains("SnapWorldPresentationToGrid");
        bool usesResolve = body.Contains("ResolveNestedPromotionSourceCell");
        bool notifyPerCell = body.Contains("NotifyNestedLayerPromoted(subject, cellIndex)");
        bool lockedApplySeating = body.Contains("view.ApplyGridPosition(space, logicalCell)")
            || body.Contains("view.ApplyGridPosition(space, sourceCell)");
        bool ok = usesSnap && usesResolve && notifyPerCell && !lockedApplySeating;
        report.AppendLine(
            $"  T5: Snap={usesSnap} ResolveSource={usesResolve} NotifyPerCell={notifyPerCell} " +
            $"noLockedApplySeating={!lockedApplySeating} bodyLen={body.Length}");
        return ok;
    }

    private static bool TestPhase65IdentityUntouched(StringBuilder report)
    {
        bool match = ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow));
        bool mismatchColor = !ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
            new MatchIdentity(ShapeType.Square, ShapeColor.Purple));
        bool mismatchShape = !ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
            new MatchIdentity(ShapeType.Circle, ShapeColor.Yellow));
        bool ok = match && mismatchColor && mismatchShape;
        report.AppendLine($"  T6: same={match} colorMismatch={mismatchColor} shapeMismatch={mismatchShape}");
        return ok;
    }

    private static bool TestPhase63PartialUntouched(StringBuilder report)
    {
        MethodInfo m = typeof(Target).GetMethod(
            "TryConsumeLayerAtWorld",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(Vector2Int), typeof(MatchIdentity), typeof(bool).MakeByRefType() },
            null);
        bool ok = m != null;
        report.AppendLine($"  T7: Target.TryConsumeLayerAtWorld(MatchIdentity)={ok}");
        return ok;
    }

    private static bool TestPhase67GroupingUntouched(StringBuilder report)
    {
        MethodInfo collect = typeof(BlockMover).GetMethod(
            "CollectAlignedMatchActions",
            BindingFlags.Static | BindingFlags.Public);
        MethodInfo build = typeof(BlockMover).GetMethod(
            "BuildAlignedMovementGroups",
            BindingFlags.Static | BindingFlags.Public);
        bool ok = collect != null && build != null;
        report.AppendLine($"  T8: CollectAlignedMatchActions={collect != null} BuildAlignedMovementGroups={build != null}");
        return ok;
    }

    private static int CountNestedChildren(Transform root)
    {
        int count = 0;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != null && child.name == "NestedInner3D")
            {
                count++;
            }
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
