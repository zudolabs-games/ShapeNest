using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TEMP Phase 69A forensic logging. Diagnostic only. Disabled by default.
/// Reuses Phase 68C cell/transform dumps. No gameplay, timing, or movement changes.
/// </summary>
public static class Phase69AForensic
{
    public static bool Enabled;

    private struct SourceMemory
    {
        public Vector2Int Logical;
        public Vector3 ViewWorld;
        public Vector3 NestedWorld;
        public int ViewId;
        public int NestedId;
    }

    private static readonly Dictionary<long, SourceMemory> sources = new Dictionary<long, SourceMemory>();

    public static void EnableForSession()
    {
        Enabled = true;
        Phase68CForensic.EnableForSession();
        Debug.Log("[69A] forensic ENABLED (also enables 68C)");
    }

    public static void Disable()
    {
        Enabled = false;
        sources.Clear();
        Phase68CForensic.Disable();
        Debug.Log("[69A] forensic DISABLED");
    }

    public static void RememberSource(
        Block block,
        int cellIndex,
        PieceView3D view,
        Transform nested)
    {
        if (!Enabled || block == null)
        {
            return;
        }

        sources[CellKey(block.GetInstanceID(), cellIndex)] = new SourceMemory
        {
            Logical = block.GetCellWorld(cellIndex),
            ViewWorld = view != null ? view.transform.position : Vector3.zero,
            NestedWorld = nested != null ? nested.position : Vector3.zero,
            ViewId = view != null ? view.GetInstanceID() : 0,
            NestedId = nested != null ? nested.GetInstanceID() : 0
        };
    }

    public static void LogNestedCreated(PieceView3D view, GameObject created)
    {
        if (!Enabled)
        {
            return;
        }

        Debug.Log(
            $"[69A][{Time.frameCount}] EnsureNestedInner_CREATE " +
            $"view={(view != null ? view.GetInstanceID().ToString() : "null")} " +
            $"viewName={(view != null ? view.name : "null")} " +
            $"created={(created != null ? created.GetInstanceID().ToString() : "null")} " +
            $"parent={(created != null && created.transform.parent != null ? created.transform.parent.name : "null")} " +
            $"viewWorld={(view != null ? Fmt(view.transform.position) : "n/a")} " +
            $"motionLock={(view != null && view.IsMotionLocked)}");
    }

    public static void LogResolvedGroup(BlockMover.AlignedMovementGroup group)
    {
        if (!Enabled || group == null)
        {
            return;
        }

        string path = group.Actions.Count <= 1 ? "MatchFocusedChainCell" : "PlayWholeBlockAlignedMatch";
        Debug.Log(
            $"[69A][{Time.frameCount}] PlayResolvedMovementGroup path={path} " +
            $"block={(group.Subject != null ? group.Subject.GetInstanceID() : 0)} " +
            $"cellCount={(group.Subject != null ? group.Subject.CellCount : 0)} " +
            $"actions={group.Actions.Count} translation={group.Translation} grid={(group.Subject != null ? group.Subject.GridPosition.ToString() : "n/a")}");
        Phase68CForensic.LogMovementGroup(group);
    }

    public static void LogWholeBlockGate(
        string reason,
        Block subject,
        Vector2Int from,
        Vector2Int to,
        Vector2Int translation,
        int actionCount,
        bool canTranslate)
    {
        if (!Enabled)
        {
            return;
        }

        Debug.Log(
            $"[69A][{Time.frameCount}] WHOLE_BLOCK_GATE reason={reason} " +
            $"block={subject?.GetInstanceID()} from={from} to={to} translation={translation} " +
            $"actions={actionCount} cells={subject?.CellCount} canTranslate={canTranslate} " +
            $"FALLBACK={(reason.StartsWith("FALLBACK") ? "MatchFocusedChainCell" : "none")}");
    }

    public static void LogRevealSeating(
        Block subject,
        int cellIndex,
        PieceView3D view,
        Vector2Int logicalCell,
        bool motionLockedBeforeApply,
        bool motionLockedAfterBegin,
        Vector3 viewWorldBeforeApply,
        Vector3 viewWorldAfterApply)
    {
        if (!Enabled || subject == null)
        {
            return;
        }

        long key = CellKey(subject.GetInstanceID(), cellIndex);
        sources.TryGetValue(key, out SourceMemory memory);
        Transform nested = view != null ? view.transform.Find("NestedInner3D") : null;
        Transform residual = FindResidualHost(subject.GetInstanceID(), cellIndex);

        Debug.Log(
            $"[69A][{Time.frameCount}] REVEAL_SEAT " +
            $"block={subject.GetInstanceID()} cell={cellIndex} " +
            $"gridNow={subject.GridPosition} logicalCell={logicalCell} " +
            $"rememberedSource={memory.Logical} " +
            $"viewId={(view != null ? view.GetInstanceID() : 0)} " +
            $"sameViewAsDetach={view != null && memory.ViewId != 0 && view.GetInstanceID() == memory.ViewId} " +
            $"lockBeforeApply={motionLockedBeforeApply} lockAfterBegin={motionLockedAfterBegin} " +
            $"ApplyGridPositionNoOp={motionLockedAfterBegin} " +
            $"viewBefore={Fmt(viewWorldBeforeApply)} viewAfter={Fmt(viewWorldAfterApply)} " +
            $"detachViewWorld={Fmt(memory.ViewWorld)} detachNestedWorld={Fmt(memory.NestedWorld)} " +
            $"nestedUnderView={(nested != null ? nested.GetInstanceID().ToString() : "null")} " +
            $"nestedActive={(nested != null && nested.gameObject.activeInHierarchy)} " +
            $"residualHost={(residual != null ? residual.name : "null")} " +
            $"residualWorld={(residual != null ? Fmt(residual.position) : "n/a")} " +
            $"promotionAt={(ApproximatelyCell(viewWorldAfterApply, memory.ViewWorld) ? "SOURCE" : "NOT_SOURCE")}");
        Phase68CForensic.LogCell("69A_REVEAL_SEAT", subject, cellIndex, $"logical={logicalCell}");
    }

    private static long CellKey(int blockId, int cellIndex) =>
        ((long)blockId << 32) ^ (uint)cellIndex;

    private static string Fmt(Vector3 v) =>
        $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";

    private static bool ApproximatelyCell(Vector3 a, Vector3 b) =>
        (a - b).sqrMagnitude < 0.05f;

    private static Transform FindResidualHost(int blockId, int cellIndex)
    {
        string hostName = $"NestedInnerResidual_{blockId}_c{cellIndex}";
        GameObject host = GameObject.Find(hostName);
        return host != null ? host.transform : null;
    }
}
