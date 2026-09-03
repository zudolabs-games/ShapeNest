using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 71B — nested outer travels; inner stays at SOURCE then becomes standalone.
/// Menu: Shape Nest / Phase 71B Verify Nested Extraction
/// </summary>
public static class Phase71BNestedExtractionVerify
{
    private const string ReportPath = "Captures/phase71b-report.txt";
    private const string MoverPath = "Assets/Scripts/Blocks/BlockMover.cs";

    [MenuItem("Shape Nest/Phase 71B Verify Nested Extraction")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 71B — NESTED OUTER → INNER EXTRACTION");
        report.AppendLine("===========================================");

        bool t1 = TestOneCellNestedUsesChainPath(report);
        bool t2 = TestConfigureNestedFalseDoesNotAllocate(report);
        bool t3 = TestDetachLeavesIndependentInner(report);
        bool t4 = TestRevealHidesThenSeatsAtSource(report);
        bool t5 = TestPhase65Identities(report);
        bool t6 = TestPhase70BSubsetRoutingUntouched(report);
        bool t7 = TestPhase63ExactCellApi(report);
        bool t8 = TestResidualPinApi(report);
        bool t9 = TestSequentialRevealAndForcedSeat(report);

        report.AppendLine();
        report.AppendLine($"1 1-cell nested uses chain path (occupancy stays source): {(t1 ? "PASS" : "FAIL")}");
        report.AppendLine($"2 ConfigureNestedInner(false) does not allocate: {(t2 ? "PASS" : "FAIL")}");
        report.AppendLine($"3 Detach reparents inner off traveler: {(t3 ? "PASS" : "FAIL")}");
        report.AppendLine($"4 Reveal hides then SnapWorldPresentationToGrid: {(t4 ? "PASS" : "FAIL")}");
        report.AppendLine($"5 Phase 65 identities (O→C Y→P G→R C→P): {(t5 ? "PASS" : "FAIL")}");
        report.AppendLine($"6 Phase 70B subset routing untouched: {(t6 ? "PASS" : "FAIL")}");
        report.AppendLine($"7 Phase 63 exact-cell consume API: {(t7 ? "PASS" : "FAIL")}");
        report.AppendLine($"8 Residual pin-to-source helper: {(t8 ? "PASS" : "FAIL")}");
        report.AppendLine($"9 Sequential reveal + forced SOURCE seat: {(t9 ? "PASS" : "FAIL")}");

        bool all = t1 && t2 && t3 && t4 && t5 && t6 && t7 && t8 && t9;
        report.AppendLine();
        report.AppendLine(all ? "RESULT: PASS" : "RESULT: FAIL");
        report.AppendLine(
            "Note: Full Level 43 Orange→Cyan Play Mode travel is reported separately by Phase71BPlayProbe.");

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

    private static bool TestOneCellNestedUsesChainPath(StringBuilder report)
    {
        if (!File.Exists(MoverPath))
        {
            report.AppendLine("  T1: BlockMover.cs missing");
            return false;
        }

        string text = File.ReadAllText(MoverPath);
        int body = text.IndexOf("private IEnumerator EnterMatchingTargetBody");
        if (body < 0)
        {
            report.AppendLine("  T1: EnterMatchingTargetBody missing");
            return false;
        }

        int next = text.IndexOf("\n    private IEnumerator", body + 10);
        string bodyText = next > body ? text.Substring(body, next - body) : text.Substring(body, 1200);
        bool usesNestedGate = bodyText.Contains("BlockHasAnyNestedInner");
        bool stillChain = bodyText.Contains("EnterChainPartialMatch");
        bool noSubset = !bodyText.Contains("PlayMatchingSubsetAlignedMatch");
        report.AppendLine(
            $"  T1: nestedGate={usesNestedGate} chain={stillChain} no70BInDragBody={noSubset}");
        return usesNestedGate && stillChain && noSubset;
    }

    private static bool TestConfigureNestedFalseDoesNotAllocate(StringBuilder report)
    {
        var go = new GameObject("Phase71B_NoAlloc");
        try
        {
            PieceView3D view = go.AddComponent<PieceView3D>();
            view.ConfigureVisual(
                ShapeType.Pentagon,
                ShapeVisuals3D.BlockMaterial(ShapeType.Pentagon, ShapeColor.Orange, null),
                asNest: false,
                footprint: 1f,
                height: 0.35f);
            int before = CountNestedChildren(go.transform);
            view.ConfigureNestedInner(false, ShapeType.Pentagon, null, 0.55f, asNest: false);
            int after = CountNestedChildren(go.transform);
            bool ok = before == 0 && after == 0 && !view.HasNestedInner;
            report.AppendLine($"  T2: nested before={before} after={after} hasNested={view.HasNestedInner}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static bool TestDetachLeavesIndependentInner(StringBuilder report)
    {
        var outer = new GameObject("Phase71B_Outer");
        var host = new GameObject("Phase71B_Host");
        try
        {
            PieceView3D view = outer.AddComponent<PieceView3D>();
            view.ConfigureVisual(
                ShapeType.Pentagon,
                ShapeVisuals3D.BlockMaterial(ShapeType.Pentagon, ShapeColor.Orange, null),
                asNest: false,
                footprint: 1f,
                height: 0.35f);
            view.ConfigureNestedInner(
                true,
                ShapeType.Pentagon,
                ShapeVisuals3D.BlockMaterial(ShapeType.Pentagon, ShapeColor.Cyan, null),
                0.55f,
                asNest: false);

            Transform nested = outer.transform.Find("NestedInner3D");
            Vector3 innerStart = nested != null ? nested.position : Vector3.zero;
            bool detached = view.TryDetachNestedInnerPreservingWorld(host.transform, out Transform residual);
            Vector3 outerMoved = outer.transform.position + new Vector3(2f, 0f, 3f);
            outer.transform.position = outerMoved;
            Vector3 innerAfter = residual != null ? residual.position : Vector3.positiveInfinity;
            float drift = Vector3.Distance(
                new Vector3(innerAfter.x, 0f, innerAfter.z),
                new Vector3(innerStart.x, 0f, innerStart.z));
            bool independent = detached
                && residual != null
                && residual.parent == host.transform
                && outer.transform.Find("NestedInner3D") == null
                && drift < 0.001f;
            report.AppendLine(
                $"  T3: detached={detached} parentIsHost={(residual != null && residual.parent == host.transform)} " +
                $"noChildOnOuter={outer.transform.Find("NestedInner3D") == null} xzDrift={drift:F4}");
            return independent;
        }
        finally
        {
            Object.DestroyImmediate(outer);
            Object.DestroyImmediate(host);
        }
    }

    private static bool TestRevealHidesThenSeatsAtSource(StringBuilder report)
    {
        if (!File.Exists(MoverPath))
        {
            report.AppendLine("  T4: BlockMover.cs missing");
            return false;
        }

        string text = File.ReadAllText(MoverPath);
        int reveal = text.IndexOf("private IEnumerator PlayNestedExtractionReveal");
        if (reveal < 0)
        {
            report.AppendLine("  T4: PlayNestedExtractionReveal missing");
            return false;
        }

        int next = text.IndexOf("\n    private ", reveal + 10);
        string body = next > reveal ? text.Substring(reveal, next - reveal) : text.Substring(reveal, 2000);
        int hide = body.IndexOf("view.SetPresentationAnticipation(0f, 0f, 0f)");
        if (hide < 0)
        {
            hide = body.IndexOf("view.SetPresentationAnticipation(0f, 0.01f, 0f)");
        }

        int snap = body.IndexOf("view.SnapWorldPresentationToGrid(space, sourceCell)");
        int promote = snap >= 0
            ? body.IndexOf("BoardPresentationController.NotifyNestedLayerPromoted", snap)
            : -1;
        bool order = hide >= 0 && snap > hide && promote > snap;
        report.AppendLine($"  T4: hideThenSnapThenPromote={order} hide={hide >= 0} snap={snap >= 0} promote={promote >= 0}");
        return order;
    }

    private static bool TestSequentialRevealAndForcedSeat(StringBuilder report)
    {
        if (!File.Exists(MoverPath))
        {
            report.AppendLine("  T9: BlockMover.cs missing");
            return false;
        }

        string text = File.ReadAllText(MoverPath);
        int allReveal = text.IndexOf("private IEnumerator PlayAllPendingNestedExtractionReveals");
        if (allReveal < 0)
        {
            report.AppendLine("  T9: PlayAllPendingNestedExtractionReveals missing");
            return false;
        }

        int next = text.IndexOf("\n    private ", allReveal + 10);
        string body = next > allReveal ? text.Substring(allReveal, next - allReveal) : text.Substring(allReveal, 1500);
        bool sequential = body.Contains("yield return PlayNestedExtractionReveal")
            && !body.Contains("StartCoroutine(RunRevealThenSignal");
        bool forcedSeat = text.Contains("SeatPromotedNestedViewsAtSource");
        bool killsTweens = text.Contains("KillTransform(view.transform");
        bool mixedRebuild = text.Contains("MarkPendingExtractionAtWorlds")
            && text.Contains("RebindAnchoredNestedResidualsToBoard")
            && text.Contains("HoldPendingExtractionViewsAtSource");
        report.AppendLine(
            $"  T9: sequential={sequential} forcedSeat={forcedSeat} killTweens={killsTweens} mixedRebuild={mixedRebuild}");
        return sequential && forcedSeat && killsTweens && mixedRebuild;
    }

    private static bool TestPhase65Identities(StringBuilder report)
    {
        bool oc = ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Pentagon, ShapeColor.Orange),
            new MatchIdentity(ShapeType.Pentagon, ShapeColor.Orange));
        bool ocMismatch = !ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Pentagon, ShapeColor.Orange),
            new MatchIdentity(ShapeType.Pentagon, ShapeColor.Cyan));
        bool yp = ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow));
        bool ypMismatch = !ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
            new MatchIdentity(ShapeType.Square, ShapeColor.Purple));
        bool gr = ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Diamond, ShapeColor.Green),
            new MatchIdentity(ShapeType.Diamond, ShapeColor.Green));
        bool grMismatch = !ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Diamond, ShapeColor.Green),
            new MatchIdentity(ShapeType.Diamond, ShapeColor.Red));
        bool cp = ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Circle, ShapeColor.Cyan),
            new MatchIdentity(ShapeType.Circle, ShapeColor.Cyan));
        bool cpMismatch = !ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Circle, ShapeColor.Cyan),
            new MatchIdentity(ShapeType.Circle, ShapeColor.Pink));
        bool ok = oc && ocMismatch && yp && ypMismatch && gr && grMismatch && cp && cpMismatch;
        report.AppendLine($"  T5: orange/cyan/yellow/purple/green/red/cyan/pink identity ok={ok}");
        return ok;
    }

    private static bool TestPhase70BSubsetRoutingUntouched(StringBuilder report)
    {
        if (!File.Exists(MoverPath))
        {
            report.AppendLine("  T6: BlockMover.cs missing");
            return false;
        }

        string text = File.ReadAllText(MoverPath);
        int resolved = text.IndexOf("public IEnumerator PlayResolvedMovementGroup");
        int subset = text.IndexOf("PlayMatchingSubsetAlignedMatch", resolved);
        int next = text.IndexOf("\n    private IEnumerator PlayMatchingSubsetAlignedMatch", resolved);
        string body = next > resolved ? text.Substring(resolved, next - resolved) : string.Empty;
        bool callsSubset = body.Contains("PlayMatchingSubsetAlignedMatch");
        bool notWhole = !body.Contains("PlayWholeBlockAlignedMatch");
        report.AppendLine($"  T6: PlayResolved→subset={callsSubset} avoidsWhole={notWhole} subsetMethod={subset > 0}");
        return callsSubset && notWhole && subset > 0;
    }

    private static bool TestPhase63ExactCellApi(StringBuilder report)
    {
        MethodInfo m = typeof(Target).GetMethod(
            "TryConsumeLayerAtWorld",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(Vector2Int), typeof(MatchIdentity), typeof(bool).MakeByRefType() },
            null);
        report.AppendLine($"  T7: TryConsumeLayerAtWorld={m != null}");
        return m != null;
    }

    private static bool TestResidualPinApi(StringBuilder report)
    {
        MethodInfo pin = typeof(BoardPresentationController).GetMethod(
            "PinAnchoredNestedResidualsToSource",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo detach = typeof(BoardPresentationController).GetMethod(
            "DetachAndAnchorNestedInner",
            BindingFlags.Static | BindingFlags.Public);
        report.AppendLine($"  T8: PinResiduals={pin != null} DetachAndAnchor={detach != null}");
        return pin != null && detach != null;
    }

    private static int CountNestedChildren(Transform root)
    {
        int n = 0;
        for (int i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i).name == "NestedInner3D")
            {
                n++;
            }
        }

        return n;
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
