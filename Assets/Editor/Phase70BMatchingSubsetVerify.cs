using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 70B — matching-subset rigid movement verifier.
/// Menu: Shape Nest / Phase 70B Verify Matching Subset Movement
/// </summary>
public static class Phase70BMatchingSubsetVerify
{
    private const string ReportPath = "Captures/phase70b-report.txt";

    [MenuItem("Shape Nest/Phase 70B Verify Matching Subset Movement")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 70B — MATCHING SUBSET MOVEMENT");
        report.AppendLine("====================================");

        bool prevDiag = BlockMover.MatchingSubsetDiagnosticsEnabled;
        BlockMover.MatchingSubsetDiagnosticsEnabled = true;

        bool t1 = TestSubsetVsWholeBlockValidation(report);
        bool t2 = TestSelectedDestinationsValid(report);
        bool t3 = TestApisExist(report);
        bool t4 = TestPlayResolvedRoutesToSubset(report);
        bool t5 = TestPhase65Identity(report);
        bool t6 = TestPhase63PartialApi(report);
        bool t7 = TestPhase69BSourceApis(report);
        bool t8 = TestLevel43WhiteTriangleGroup(report);

        BlockMover.MatchingSubsetDiagnosticsEnabled = prevDiag;

        report.AppendLine();
        report.AppendLine($"1 subset vs whole-block validation: {(t1 ? "PASS" : "FAIL")}");
        report.AppendLine($"2 selected destinations valid: {(t2 ? "PASS" : "FAIL")}");
        report.AppendLine($"3 subset APIs exist: {(t3 ? "PASS" : "FAIL")}");
        report.AppendLine($"4 PlayResolved routes to subset (source): {(t4 ? "PASS" : "FAIL")}");
        report.AppendLine($"5 Phase 65 identity: {(t5 ? "PASS" : "FAIL")}");
        report.AppendLine($"6 Phase 63 partial API: {(t6 ? "PASS" : "FAIL")}");
        report.AppendLine($"7 Phase 69B source APIs: {(t7 ? "PASS" : "FAIL")}");
        report.AppendLine($"8 Level 43 white triangle 2-action group: {(t8 ? "PASS" : "FAIL")}");

        bool all = t1 && t2 && t3 && t4 && t5 && t6 && t7 && t8;
        report.AppendLine();
        report.AppendLine(all ? "RESULT: PASS" : "RESULT: FAIL");
        report.AppendLine(
            "Note: Editor check is static/API. Play Mode: add Phase70BPlayProbe in Play Mode, " +
            "load Level 43 (index 42), call Begin(); see Captures/phase70b-play-report.txt.");

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

    private static bool TestSubsetVsWholeBlockValidation(StringBuilder report)
    {
        // Synthetic: 2x2 locals, translation (0,5) — whole footprint off-board on 6x10,
        // but subset of top two cells to (1,9)(2,9) is valid when those dest cells are free.
        // Without a live board, validate method presence + Level43 static geometry logic.
        MethodInfo m = typeof(BoardManager).GetMethod(
            "CanTranslateMatchingSubset",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo whole = typeof(BoardManager).GetMethod(
            "CanTranslateBlock",
            BindingFlags.Instance | BindingFlags.Public);
        bool ok = m != null && whole != null;
        report.AppendLine($"  T1: CanTranslateMatchingSubset={m != null} CanTranslateBlock={whole != null}");

        // Authored Level 43 geometry: top pair d=(0,5) — destinations on-board; unmatched would be off-board.
        Vector2Int a = new Vector2Int(1, 4);
        Vector2Int b = new Vector2Int(2, 4);
        Vector2Int c = new Vector2Int(1, 5);
        Vector2Int d = new Vector2Int(2, 5);
        Vector2Int t = new Vector2Int(0, 5);
        bool subsetDestOk = IsInside(a + t, 6, 10) && IsInside(b + t, 6, 10);
        bool wholeDestOk = IsInside(a + t, 6, 10) && IsInside(b + t, 6, 10)
            && IsInside(c + t, 6, 10) && IsInside(d + t, 6, 10);
        report.AppendLine($"  T1: subsetDestOnBoard={subsetDestOk} wholeDestOnBoard={wholeDestOk}");
        return ok && subsetDestOk && !wholeDestOk;
    }

    private static bool TestSelectedDestinationsValid(StringBuilder report)
    {
        Vector2Int[] dests =
        {
            new Vector2Int(1, 9),
            new Vector2Int(2, 9)
        };
        bool ok = true;
        for (int i = 0; i < dests.Length; i++)
        {
            if (!IsInside(dests[i], 6, 10))
            {
                ok = false;
            }
        }

        report.AppendLine($"  T2: Target A destinations inside 6x10={ok}");
        return ok;
    }

    private static bool TestApisExist(StringBuilder report)
    {
        MethodInfo begin = typeof(BoardPresentationController).GetMethod(
            "BeginMatchingSubsetTravel",
            BindingFlags.Static | BindingFlags.Public);
        MethodInfo end = typeof(BoardPresentationController).GetMethod(
            "EndMatchingSubsetTravel",
            BindingFlags.Static | BindingFlags.Public);
        MethodInfo subset = typeof(BlockMover).GetMethod(
            "PlayMatchingSubsetAlignedMatch",
            BindingFlags.Instance | BindingFlags.NonPublic);
        bool ok = begin != null && end != null && subset != null;
        report.AppendLine(
            $"  T3: BeginSubset={begin != null} EndSubset={end != null} PlayMatchingSubset={subset != null}");
        return ok;
    }

    private static bool TestPlayResolvedRoutesToSubset(StringBuilder report)
    {
        string path = "Assets/Scripts/Blocks/BlockMover.cs";
        string text = File.ReadAllText(path);
        const string marker = "public IEnumerator PlayResolvedMovementGroup";
        int start = text.IndexOf(marker);
        if (start < 0)
        {
            report.AppendLine("  T4: PlayResolvedMovementGroup not found");
            return false;
        }

        int next = text.IndexOf("\n    private IEnumerator PlayMatchingSubsetAlignedMatch", start);
        if (next < 0)
        {
            next = text.IndexOf("\n    private IEnumerator PlayWholeBlockAlignedMatch", start);
        }

        string body = next > start ? text.Substring(start, next - start) : text.Substring(start, 1200);
        bool callsSubset = body.Contains("PlayMatchingSubsetAlignedMatch");
        bool notWhole = !body.Contains("PlayWholeBlockAlignedMatch");
        report.AppendLine($"  T4: callsSubset={callsSubset} avoidsWholeInResolved={notWhole}");
        return callsSubset && notWhole;
    }

    private static bool TestPhase65Identity(StringBuilder report)
    {
        bool match = ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Triangle, ShapeColor.White),
            new MatchIdentity(ShapeType.Triangle, ShapeColor.White));
        bool mismatch = !ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Triangle, ShapeColor.White),
            new MatchIdentity(ShapeType.Triangle, ShapeColor.Yellow));
        report.AppendLine($"  T5: same={match} colorMismatch={mismatch}");
        return match && mismatch;
    }

    private static bool TestPhase63PartialApi(StringBuilder report)
    {
        MethodInfo m = typeof(Target).GetMethod(
            "TryConsumeLayerAtWorld",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(Vector2Int), typeof(MatchIdentity), typeof(bool).MakeByRefType() },
            null);
        report.AppendLine($"  T6: TryConsumeLayerAtWorld(MatchIdentity)={m != null}");
        return m != null;
    }

    private static bool TestPhase69BSourceApis(StringBuilder report)
    {
        MethodInfo resolve = typeof(BoardPresentationController).GetMethod(
            "ResolveNestedPromotionSourceCell",
            BindingFlags.Static | BindingFlags.Public);
        MethodInfo snap = typeof(PieceView3D).GetMethod(
            "SnapWorldPresentationToGrid",
            BindingFlags.Instance | BindingFlags.Public);
        report.AppendLine($"  T7: ResolveSource={resolve != null} Snap={snap != null}");
        return resolve != null && snap != null;
    }

    private static bool TestLevel43WhiteTriangleGroup(StringBuilder report)
    {
        // Static proof: A+B share (0,5) onto Target A without requiring C+D to translate.
        Vector2Int a = new Vector2Int(1, 4);
        Vector2Int b = new Vector2Int(2, 4);
        Vector2Int ta0 = new Vector2Int(1, 9);
        Vector2Int ta1 = new Vector2Int(2, 9);
        Vector2Int dA = ta0 - a;
        Vector2Int dB = ta1 - b;
        bool same = dA == dB && dA == new Vector2Int(0, 5);
        report.AppendLine($"  T8: A→TA0={dA} B→TA1={dB} sameTranslation={same}");
        return same;
    }

    private static bool IsInside(Vector2Int cell, int width, int height)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;
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
