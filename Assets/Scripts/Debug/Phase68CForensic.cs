using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// TEMP Phase 68C forensic logging. No gameplay changes.
/// Enable with Phase68CForensic.Enabled = true (or menu).
/// </summary>
public static class Phase68CForensic
{
    public static bool Enabled;

    /// <summary>When true, logs outer/inner/residual world positions each LateUpdate for tracked cells.</summary>
    public static bool TrackTransforms;

    private static readonly HashSet<long> trackedCells = new HashSet<long>();
    private static readonly StringBuilder batch = new StringBuilder(512);

    public static void EnableForSession()
    {
        Enabled = true;
        TrackTransforms = true;
        Debug.Log("[68C] forensic ENABLED");
    }

    public static void Disable()
    {
        Enabled = false;
        TrackTransforms = false;
        trackedCells.Clear();
        Debug.Log("[68C] forensic DISABLED");
    }

    public static void TrackCell(Block block, int cellIndex)
    {
        if (block == null)
        {
            return;
        }

        trackedCells.Add(CellKey(block.GetInstanceID(), cellIndex));
    }

    public static void Log(string stage, string detail)
    {
        if (!Enabled)
        {
            return;
        }

        Debug.Log($"[68C][{Time.frameCount}] {stage} | {detail}");
    }

    public static void LogCell(
        string stage,
        Block block,
        int cellIndex,
        string extra = null)
    {
        if (!Enabled || block == null)
        {
            return;
        }

        TrackCell(block, cellIndex);
        PieceView3D view = block.GetWorldViewForCellIndex(cellIndex);
        Transform nested = FindChildNamed(view != null ? view.transform : null, "NestedInner3D");
        Transform residual = FindResidual(block.GetInstanceID(), cellIndex);
        Vector2Int logical = block.GridPosition + block.GetLocalCell(cellIndex);
        MatchIdentity id = block.GetActiveIdentity(cellIndex);

        batch.Clear();
        batch.Append("block=").Append(block.GetInstanceID());
        batch.Append(" cell=").Append(cellIndex);
        batch.Append(" logical=").Append(logical);
        batch.Append(" shape=").Append(id.Shape).Append('/').Append(id.Color);
        batch.Append(" pending=").Append(block.IsPendingLayerExtraction(cellIndex));
        batch.Append(" hasInner=").Append(block.HasInnerLayerAt(cellIndex));
        batch.Append(" view=").Append(view != null ? view.GetInstanceID().ToString() : "null");
        if (view != null)
        {
            batch.Append(" viewParent=").Append(view.transform.parent != null ? view.transform.parent.name : "null");
            batch.Append(" viewWorld=").Append(Fmt(view.transform.position));
            batch.Append(" viewScale=").Append(Fmt(view.LocalScale));
            batch.Append(" motionLock=").Append(view.IsMotionLocked);
            batch.Append(" hasNestedFlag=").Append(view.HasNestedInner);
        }

        batch.Append(" nested3d=").Append(nested != null ? nested.GetInstanceID().ToString() : "null");
        if (nested != null)
        {
            batch.Append(" nestedParent=").Append(nested.parent != null ? nested.parent.name : "null");
            batch.Append(" nestedWorld=").Append(Fmt(nested.position));
            batch.Append(" nestedActive=").Append(nested.gameObject.activeInHierarchy);
        }

        batch.Append(" residual=").Append(residual != null ? residual.GetInstanceID().ToString() : "null");
        if (residual != null)
        {
            batch.Append(" residualParent=").Append(residual.parent != null ? residual.parent.name : "null");
            batch.Append(" residualWorld=").Append(Fmt(residual.position));
            batch.Append(" residualActive=").Append(residual.gameObject.activeInHierarchy);
        }

        if (!string.IsNullOrEmpty(extra))
        {
            batch.Append(" | ").Append(extra);
        }

        Log(stage, batch.ToString());
    }

    public static void LogDetach(
        string when,
        Block block,
        int cellIndex,
        PieceView3D view,
        Transform nestedBefore,
        Transform residualAfter)
    {
        if (!Enabled)
        {
            return;
        }

        Log(
            "DETACH_" + when,
            $"block={block?.GetInstanceID()} cell={cellIndex} " +
            $"view={(view != null ? view.GetInstanceID().ToString() : "null")} " +
            $"nestedBefore={(nestedBefore != null ? nestedBefore.GetInstanceID().ToString() : "null")} " +
            $"nestedBeforeParent={(nestedBefore != null && nestedBefore.parent != null ? nestedBefore.parent.name : "null")} " +
            $"nestedBeforeWorld={(nestedBefore != null ? Fmt(nestedBefore.position) : "n/a")} " +
            $"residualAfter={(residualAfter != null ? residualAfter.GetInstanceID().ToString() : "null")} " +
            $"residualParent={(residualAfter != null && residualAfter.parent != null ? residualAfter.parent.name : "null")} " +
            $"residualWorld={(residualAfter != null ? Fmt(residualAfter.position) : "n/a")} " +
            $"sameObject={(nestedBefore != null && residualAfter != null && nestedBefore.GetInstanceID() == residualAfter.GetInstanceID())}");
    }

    public static void LogConfigureNested(
        string caller,
        PieceView3D view,
        Block block,
        int cellIndex,
        bool show)
    {
        if (!Enabled)
        {
            return;
        }

        Transform nested = FindChildNamed(view != null ? view.transform : null, "NestedInner3D");
        Log(
            "ConfigureNestedInner",
            $"caller={caller} show={show} block={block?.GetInstanceID()} cell={cellIndex} " +
            $"view={(view != null ? view.GetInstanceID().ToString() : "null")} " +
            $"nested={(nested != null ? nested.GetInstanceID().ToString() : "null")} " +
            $"nestedParent={(nested != null && nested.parent != null ? nested.parent.name : "null")} " +
            $"nestedWorld={(nested != null ? Fmt(nested.position) : "n/a")} " +
            $"nestedActive={(nested != null && nested.gameObject.activeInHierarchy)} " +
            $"hasAnchored={BoardPresentationController.HasAnchoredNestedResidual(block, cellIndex)}");
    }

    public static void LogMovementGroup(BlockMover.AlignedMovementGroup group)
    {
        if (!Enabled || group == null)
        {
            return;
        }

        batch.Clear();
        batch.Append("block=").Append(group.Subject != null ? group.Subject.GetInstanceID() : 0);
        batch.Append(" translation=").Append(group.Translation);
        batch.Append(" actions=").Append(group.Actions.Count);
        for (int i = 0; i < group.Actions.Count; i++)
        {
            BlockMover.AlignedMatchAction a = group.Actions[i];
            batch.Append(" [").Append(i).Append(" cell=").Append(a.CellIndex);
            batch.Append(" src=").Append(a.CellWorld);
            batch.Append(" nest=").Append(a.NestTo);
            batch.Append(" d=").Append(a.Translation).Append(']');
            TrackCell(a.Subject, a.CellIndex);
        }

        Log("MOVEMENT_GROUP", batch.ToString());
    }

    public static void DumpDuplicates(Block block, int cellIndex)
    {
        if (!Enabled || block == null)
        {
            return;
        }

        PieceView3D[] allViews = Object.FindObjectsByType<PieceView3D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < allViews.Length; i++)
        {
            PieceView3D v = allViews[i];
            if (v == null)
            {
                continue;
            }

            Transform nested = FindChildNamed(v.transform, "NestedInner3D");
            bool nameHit = v.name != null
                && (v.name.Contains(block.GetInstanceID().ToString())
                    || (nested != null));
            if (!nameHit && nested == null)
            {
                continue;
            }

            count++;
            Log(
                "DUP_SCAN",
                $"viewId={v.GetInstanceID()} name={v.name} active={v.gameObject.activeInHierarchy} " +
                $"parent={(v.transform.parent != null ? v.transform.parent.name : "null")} " +
                $"world={Fmt(v.transform.position)} " +
                $"nested={(nested != null ? nested.GetInstanceID().ToString() : "null")} " +
                $"nestedActive={(nested != null && nested.gameObject.activeInHierarchy)} " +
                $"nestedParent={(nested != null && nested.parent != null ? nested.parent.name : "null")} " +
                $"nestedWorld={(nested != null ? Fmt(nested.position) : "n/a")}");
        }

        Transform residual = FindResidual(block.GetInstanceID(), cellIndex);
        Log(
            "DUP_SCAN_SUMMARY",
            $"block={block.GetInstanceID()} cell={cellIndex} pieceViewsTouched={count} " +
            $"residual={(residual != null ? residual.GetInstanceID().ToString() : "null")}");
    }

    public static void LateUpdateTick()
    {
        if (!Enabled || !TrackTransforms || trackedCells.Count == 0)
        {
            return;
        }

        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (long key in trackedCells)
        {
            int blockId = (int)(key >> 32);
            int cellIndex = (int)(key & 0xffffffff);
            for (int b = 0; b < blocks.Length; b++)
            {
                Block block = blocks[b];
                if (block == null || block.GetInstanceID() != blockId)
                {
                    continue;
                }

                if (cellIndex < 0 || cellIndex >= block.CellCount)
                {
                    continue;
                }

                PieceView3D view = block.GetWorldViewForCellIndex(cellIndex);
                Transform nested = FindChildNamed(view != null ? view.transform : null, "NestedInner3D");
                Transform residual = FindResidual(blockId, cellIndex);
                Vector2Int logical = block.GridPosition + block.GetLocalCell(cellIndex);
                Log(
                    "FRAME",
                    $"block={blockId} cell={cellIndex} logical={logical} " +
                    $"outer={(view != null ? Fmt(view.transform.position) : "null")} " +
                    $"nestedUnderView={(nested != null && nested.gameObject.activeInHierarchy ? Fmt(nested.position) : "inactive/null")} " +
                    $"nestedId={(nested != null ? nested.GetInstanceID().ToString() : "null")} " +
                    $"residual={(residual != null && residual.gameObject.activeInHierarchy ? Fmt(residual.position) : "inactive/null")} " +
                    $"residualId={(residual != null ? residual.GetInstanceID().ToString() : "null")}");
                break;
            }
        }
    }

    private static long CellKey(int blockId, int cellIndex) =>
        ((long)blockId << 32) ^ (uint)cellIndex;

    private static string Fmt(Vector3 v) =>
        $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";

    private static Transform FindChildNamed(Transform root, string name)
    {
        if (root == null)
        {
            return null;
        }

        Transform direct = root.Find(name);
        if (direct != null)
        {
            return direct;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != null && child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    private static Transform FindResidual(int blockId, int cellIndex)
    {
        string hostName = $"NestedInnerResidual_{blockId}_c{cellIndex}";
        GameObject host = GameObject.Find(hostName);
        if (host == null)
        {
            // Residual may be the NestedInner3D itself under Pieces3D
            PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            // Fall through: search transforms by name pattern
        }

        if (host != null)
        {
            Transform nested = host.transform.Find("NestedInner3D");
            return nested != null ? nested : host.transform;
        }

        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t != null && t.name == hostName)
            {
                Transform nested = t.Find("NestedInner3D");
                return nested != null ? nested : t;
            }
        }

        return null;
    }
}
