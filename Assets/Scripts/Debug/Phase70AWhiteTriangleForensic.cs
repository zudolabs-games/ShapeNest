using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// TEMP Phase 70A white-triangle forensic logging. Diagnostic only. Disabled by default.
/// Enable via Phase70AWhiteTriangleForensic.EnableForSession() or menu.
/// No gameplay / matching / movement / presentation changes.
/// </summary>
public static class Phase70AWhiteTriangleForensic
{
    public static bool Enabled;

    public static void EnableForSession()
    {
        Enabled = true;
        Debug.Log("[70A] white-triangle forensic ENABLED");
    }

    public static void Disable()
    {
        Enabled = false;
        Debug.Log("[70A] white-triangle forensic DISABLED");
    }

    public static void Log(string stage, string detail)
    {
        if (!Enabled)
        {
            return;
        }

        Debug.Log($"[70A][{Time.frameCount}] {stage} | {detail}");
    }

    public static string DumpMatchPipeline(
        BoardManager board,
        Block subject,
        bool hasLastMatch,
        Vector2Int lastOrigin,
        Vector2Int lastTarget)
    {
        var sb = new StringBuilder(1024);
        if (board == null || subject == null)
        {
            return "board/subject null";
        }

        sb.AppendLine($"block={subject.GetInstanceID()} cells={subject.CellCount} grid={subject.GridPosition}");
        for (int i = 0; i < subject.CellCount; i++)
        {
            Vector2Int world = subject.GridPosition + subject.GetLocalCell(i);
            Target t = board.GetTargetAt(world);
            string reject = BlockMover.ExplainAlignedCellRejection(board, subject, i, world, null);
            sb.AppendLine(
                $"  cell={i} local={subject.GetLocalCell(i)} world={world} " +
                $"id={subject.GetActiveIdentity(i)} target={(t != null ? t.GetRequiredIdentityAtWorld(world).ToString() : "none")} " +
                $"reject={(reject ?? "MATCH")}");
        }

        var scratch = new List<Block>();
        var actions = new List<BlockMover.AlignedMatchAction>();
        var groups = new List<BlockMover.AlignedMovementGroup>();
        int nAct = BlockMover.CollectAlignedMatchActions(
            board, scratch, null, hasLastMatch, lastOrigin, lastTarget, actions);
        int nGrp = BlockMover.BuildAlignedMovementGroups(actions, groups);
        sb.AppendLine($"actions={nAct} groups={nGrp}");
        for (int a = 0; a < actions.Count; a++)
        {
            var act = actions[a];
            sb.AppendLine(
                $"  ACT cell={act.CellIndex} src={act.CellWorld} nest={act.NestTo} d={act.Translation}");
        }

        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            Vector2Int from = group.Subject != null ? group.Subject.GridPosition : Vector2Int.zero;
            Vector2Int to = from + group.Translation;
            bool can = group.Translation == Vector2Int.zero
                || (board != null && group.Subject != null && board.CanTranslateBlock(group.Subject, to));
            string path = group.Actions.Count <= 1
                ? "MatchFocusedChainCell"
                : (can ? "PlayWholeBlockAlignedMatch" : "FALLBACK_CanTranslate→MatchFocusedChainCell");
            sb.AppendLine(
                $"  GROUP g={g} actions={group.Actions.Count} translation={group.Translation} " +
                $"from={from} to={to} CanTranslate={can} predictedPath={path}");
            Log("PIPELINE", sb.ToString());
        }

        return sb.ToString();
    }
}
