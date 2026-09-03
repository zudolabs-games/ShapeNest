using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase 71A — forensic diagnosis only. Disabled by default.
/// Does not alter gameplay, matching, movement, or presentation.
/// </summary>
public static class Phase71AForensic
{
    public static bool Enabled;

    private static readonly StringBuilder SessionLog = new StringBuilder(8192);
    private const string ReportPath = "Captures/phase71a-forensic-report.txt";

    public static void EnableForSession()
    {
        Enabled = true;
        SessionLog.Clear();
        Log("ENABLE", "Phase 71A forensic ENABLED");
    }

    public static void Disable()
    {
        Enabled = false;
        Log("DISABLE", "Phase 71A forensic DISABLED");
        Flush();
    }

    public static void Log(string stage, string detail)
    {
        if (!Enabled && stage != "ENABLE" && stage != "DISABLE")
        {
            return;
        }

        string line = $"[71A][{Time.frameCount}] {stage} | {detail}";
        SessionLog.AppendLine(line);
        Debug.Log(line);
    }

    public static void Flush()
    {
        try
        {
            string dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(ReportPath, SessionLog.ToString());
        }
        catch
        {
            // ignore IO
        }
    }

    public static string DumpBlockSnapshot(BoardManager board, Block block, string label)
    {
        var sb = new StringBuilder(1024);
        if (block == null)
        {
            return label + ": null block";
        }

        sb.AppendLine(
            $"{label}: id={block.GetInstanceID()} cells={block.CellCount} " +
            $"grid={block.GridPosition} settled={block.IsSettled}");
        for (int i = 0; i < block.CellCount; i++)
        {
            Vector2Int logical = block.GetCellWorld(i);
            MatchIdentity active = block.GetActiveIdentity(i);
            bool hasInner = block.HasInnerLayerAt(i);
            PieceView3D view = block.GetWorldViewForCellIndex(i);
            Transform nested = null;
            if (view != null)
            {
                Transform t = view.transform.Find("NestedInner3D");
                if (t != null)
                {
                    nested = t;
                }
            }

            Transform residual = null;
            bool hasResidual = BoardPresentationController.HasAnchoredNestedResidual(block, i);
            Target atCell = board != null ? board.GetTargetAt(logical) : null;
            sb.AppendLine(
                $"  cell[{i}] logical={logical} active={active} hasInner={hasInner} " +
                $"view={(view != null ? view.GetInstanceID().ToString() : "null")} " +
                $"viewPos={(view != null ? view.transform.position.ToString("F3") : "-")} " +
                $"motionLock={(view != null && view.IsMotionLocked)} " +
                $"nestedParent={(nested != null ? nested.parent.name : "none")} " +
                $"nestedPos={(nested != null ? nested.position.ToString("F3") : "-")} " +
                $"hasResidual={hasResidual} " +
                $"targetAt={(atCell != null ? atCell.GetRequiredIdentityAtWorld(logical).ToString() : "none")}");
        }

        string text = sb.ToString();
        Log("SNAP", text);
        return text;
    }

    public static string DumpWhiteTriangleSubsets(BoardManager board, Block white)
    {
        var sb = new StringBuilder(2048);
        if (board == null || white == null || white.CellCount < 4)
        {
            return "white triangle unavailable";
        }

        sb.AppendLine($"WHITE TRIANGLE id={white.GetInstanceID()} grid={white.GridPosition}");
        char[] names = { 'A', 'B', 'C', 'D' };
        for (int i = 0; i < Mathf.Min(4, white.CellCount); i++)
        {
            sb.AppendLine(
                $"  cell {names[i]} index={i} local={white.GetLocalCell(i)} " +
                $"world={white.GetCellWorld(i)} id={white.GetActiveIdentity(i)}");
        }

        // Candidate same-translation pairs from Level 43 geometry (not hardcoded into gameplay).
        AppendSubset(sb, board, white, "A+B→TargetA", new[] { 0, 1 }, new Vector2Int(0, 5));
        AppendSubset(sb, board, white, "C+D→TargetA", new[] { 2, 3 }, new Vector2Int(0, 5));
        AppendSubset(sb, board, white, "A+C→TargetB", new[] { 0, 2 }, new Vector2Int(3, 0));
        AppendSubset(sb, board, white, "B+D→TargetB", new[] { 1, 3 }, new Vector2Int(3, 0));

        string text = sb.ToString();
        Log("SUBSETS", text);
        return text;
    }

    private static void AppendSubset(
        StringBuilder sb,
        BoardManager board,
        Block white,
        string label,
        int[] indices,
        Vector2Int translation)
    {
        var list = new List<int>(indices);
        bool subset = board.CanTranslateMatchingSubset(white, list, translation);
        bool whole = translation == Vector2Int.zero
            || board.CanTranslateBlock(white, white.GridPosition + translation);
        sb.AppendLine(
            $"  {label} indices=[{string.Join(",", indices)}] d={translation} " +
            $"CanTranslateMatchingSubset={subset} CanTranslateBlock={whole}");
        for (int i = 0; i < indices.Length; i++)
        {
            int idx = indices[i];
            Vector2Int src = white.GetCellWorld(idx);
            Vector2Int dest = src + translation;
            Target t = board.GetTargetAt(dest);
            sb.AppendLine(
                $"    src={src} dest={dest} " +
                $"target={(t != null ? t.GetRequiredIdentityAtWorld(dest).ToString() : "NONE")}");
        }
    }

    public static string DumpAutoMatchPipeline(BoardManager board, bool hasLast, Vector2Int lastOrigin, Vector2Int lastTarget)
    {
        var scratch = new List<Block>();
        var actions = new List<BlockMover.AlignedMatchAction>();
        var groups = new List<BlockMover.AlignedMovementGroup>();
        int nAct = BlockMover.CollectAlignedMatchActions(
            board, scratch, null, hasLast, lastOrigin, lastTarget, actions);
        int nGrp = BlockMover.BuildAlignedMovementGroups(actions, groups);
        var sb = new StringBuilder(1024);
        sb.AppendLine($"autoMatch actions={nAct} groups={nGrp} hasLast={hasLast} last={lastOrigin}->{lastTarget}");
        for (int a = 0; a < actions.Count; a++)
        {
            var act = actions[a];
            sb.AppendLine(
                $"  ACT block={act.Subject.GetInstanceID()} cell={act.CellIndex} " +
                $"src={act.CellWorld} nest={act.NestTo} d={act.Translation}");
        }

        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var indices = new List<int>();
            for (int a = 0; a < group.Actions.Count; a++)
            {
                indices.Add(group.Actions[a].CellIndex);
            }

            bool subset = group.Actions.Count >= 2
                && board.CanTranslateMatchingSubset(group.Subject, indices, group.Translation);
            bool whole = group.Translation == Vector2Int.zero
                || board.CanTranslateBlock(group.Subject, group.Subject.GridPosition + group.Translation);
            string resolvedPath = group.Actions.Count <= 1
                ? "PlayResolved→MatchFocusedChainCell"
                : "PlayResolved→PlayMatchingSubsetAlignedMatch (70B)";
            string dragPath = "EnterMatchingTargetBody→EnterChainPartialMatch→MatchFocusedChainCell (NOT 70B)";
            sb.AppendLine(
                $"  GROUP g={g} actions={group.Actions.Count} d={group.Translation} " +
                $"subsetOk={subset} wholeOk={whole}");
            sb.AppendLine($"    IF_AUTO_MATCH_PATH: {resolvedPath}");
            sb.AppendLine($"    IF_PLAYER_DRAG_PATH: {dragPath}");
        }

        string text = sb.ToString();
        Log("AUTO", text);
        return text;
    }

    public static string ProveDragPathFromSource()
    {
        // Static proof from BlockMover.cs structure (no gameplay mutation).
        string path =
            "PLAYER DRAG (CellCount>1):\n" +
            "  InputManager.OnPointerReleased\n" +
            "  → BlockMover.EndDrag (dragReleased=true)\n" +
            "  → DragRoutine\n" +
            "  → EnterMatchingTarget\n" +
            "  → EnterMatchingTargetBody\n" +
            "     if (CellCount > 1) → EnterChainPartialMatch  *** BRANCH ***\n" +
            "        → MatchFocusedChainCell\n" +
            "           → CollectChainFocusedMatch\n" +
            "              → KeepOnlyNearestMatch / KeepOnlyCellAtWorld  *** REDUCES TO 1 ***\n" +
            "           → PlayChainCellNestEntry (ONE cell)\n" +
            "           → ConsumeAndRebuild (ONE nestCellIndices entry)\n" +
            "\n" +
            "AUTO-MATCH (LevelManager alignedMatchRoutine):\n" +
            "  CollectAlignedMovementGroups\n" +
            "  → PlayResolvedMovementGroup\n" +
            "     Count==1 → MatchFocusedChainCell\n" +
            "     Count>=2 → PlayMatchingSubsetAlignedMatch (Phase 70B)\n" +
            "\n" +
            "CONCLUSION: Player-visible drag of the 4-cell white triangle NEVER enters Phase 70B.";
        Log("PATH", path);
        return path;
    }

    public static string SimulateFocusedReduction(
        BoardManager board,
        Block subject,
        Vector2Int focus)
    {
        // Mirrors CollectChainFocusedMatch adjacent-delta collection + KeepOnlyNearest
        // without calling private methods — proves reduction mathematically.
        var sb = new StringBuilder(1024);
        if (subject == null || board == null)
        {
            return "null";
        }

        Vector2Int occupancy = subject.GridPosition;
        Vector2Int delta = focus - occupancy;
        var candidates = new List<(int index, Vector2Int src, Vector2Int nest, float dist)>();
        for (int i = 0; i < subject.CellCount; i++)
        {
            Vector2Int world = occupancy + subject.GetLocalCell(i);
            // occupying matches
            Target here = board.GetTargetAt(world);
            if (here != null
                && ShapeMatch.AreMatchingLayers(
                    here.GetRequiredIdentityAtWorld(world),
                    subject.GetActiveIdentity(i)))
            {
                float d = Vector2Int.Distance(world, focus);
                candidates.Add((i, world, world, d));
                continue;
            }

            // adjacent / focus-delta matches
            if (delta != Vector2Int.zero)
            {
                Vector2Int dest = world + delta;
                Target t = board.GetTargetAt(dest);
                if (t != null
                    && ShapeMatch.AreMatchingLayers(
                        t.GetRequiredIdentityAtWorld(dest),
                        subject.GetActiveIdentity(i)))
                {
                    float d = Vector2Int.Distance(world, focus);
                    candidates.Add((i, world, dest, d));
                }
            }
        }

        sb.AppendLine($"focus={focus} occupancy={occupancy} delta={delta} candidates={candidates.Count}");
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            sb.AppendLine($"  cand cell={c.index} src={c.src} nest={c.nest} dist={c.dist}");
        }

        if (candidates.Count == 0)
        {
            sb.AppendLine("NO CANDIDATES — focused match would fail entirely");
        }
        else
        {
            candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
            var keep = candidates[0];
            sb.AppendLine(
                $"KeepOnlyNearest RESULT: KEEP cell={keep.index} src={keep.src} nest={keep.nest} " +
                $"DROPPED={candidates.Count - 1} other candidates");
            sb.AppendLine(
                "FIRST INCORRECT STATE (drag path): multi-cell valid set reduced to 1 cell " +
                "inside CollectChainFocusedMatch → KeepOnlyNearestMatch");
        }

        string text = sb.ToString();
        Log("REDUCE", text);
        return text;
    }

    public static string CompareLogicalVsVisual(Block block)
    {
        var sb = new StringBuilder(512);
        if (block == null)
        {
            return "null";
        }

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace space = presenter != null ? presenter.GridSpace : null;
        for (int i = 0; i < block.CellCount; i++)
        {
            Vector2Int logical = block.GetCellWorld(i);
            PieceView3D view = block.GetWorldViewForCellIndex(i);
            Vector3 expected = space != null ? space.GridToWorld(logical) : Vector3.zero;
            Vector3 actual = view != null ? view.transform.position : Vector3.positiveInfinity;
            float err = view != null ? Vector3.Distance(expected, actual) : -1f;
            sb.AppendLine(
                $"  cell[{i}] logical={logical} expectedW={expected:F3} actualW={actual:F3} " +
                $"err={err:F4} locked={(view != null && view.IsMotionLocked)}");
        }

        string text = sb.ToString();
        Log("LvsV", text);
        return text;
    }
}
