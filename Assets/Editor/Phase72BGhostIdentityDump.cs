using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 72B — live identity dump of every visible renderer under PiecesRoot / NestsRoot / VfxRoot,
/// plus scene-wide Green / Diamond mesh hits (the green ghost was missing from PiecesRoot-only dumps).
/// Menu: Shape Nest / Phase 72B Dump PiecesRoot Ghost Identity
/// Does NOT change gameplay. Writes Captures/phase72b-ghost-identity.txt
/// </summary>
public static class Phase72BGhostIdentityDump
{
    private const string ReportPath = "Captures/phase72b-ghost-identity.txt";
    private const string SessionKey = "Phase72BGhostDumpRunning";

    [MenuItem("Shape Nest/Phase 72B Dump PiecesRoot Ghost Identity")]
    public static void Run()
    {
        SessionState.SetBool(SessionKey, true);
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = true;
        }
        else
        {
            EditorApplication.delayCall += StartProbe;
        }
    }

    [MenuItem("Shape Nest/Phase 72B Dump PiecesRoot NOW (Play Mode only)")]
    public static void DumpNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[72B] Enter Play Mode first, reproduce the ghost, then run Dump NOW.");
            return;
        }

        string report = Phase72BGhostIdentityProbe.BuildDump("manual");
        WriteReport(report);
        Debug.Log(report);
    }

    [MenuItem("Shape Nest/Phase 72B Reproduce Diamond Green Ghost")]
    public static void ReproduceDiamond()
    {
        SessionState.SetBool(SessionKey, true);
        SessionState.SetBool("Phase72BDiamondReproduce", true);
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = true;
        }
        else
        {
            EditorApplication.delayCall += StartProbe;
        }
    }

    private static void OnPlayMode(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(SessionKey, false))
        {
            EditorApplication.delayCall += StartProbe;
        }

        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            SessionState.SetBool(SessionKey, false);
            SessionState.SetBool("Phase72BDiamondReproduce", false);
            EditorApplication.playModeStateChanged -= OnPlayMode;
        }
    }

    private static void StartProbe()
    {
        if (!EditorApplication.isPlaying || !SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        var host = new GameObject("Phase72B_GhostIdentityProbe");
        Object.DontDestroyOnLoad(host);
        var probe = host.AddComponent<Phase72BGhostIdentityProbe>();
        if (SessionState.GetBool("Phase72BDiamondReproduce", false))
        {
            probe.BeginDiamondReproduce();
        }
        else
        {
            probe.BeginAuto();
        }
    }

    public static void WriteReport(string text)
    {
        string dir = Path.GetDirectoryName(ReportPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(ReportPath, text);
    }
}

/// <summary>Runtime dump + Level 43 diamond nested peel reproduce for green ghost identity.</summary>
public sealed class Phase72BGhostIdentityProbe : MonoBehaviour
{
    public void BeginAuto()
    {
        StartCoroutine(RunAuto());
    }

    public void BeginDiamondReproduce()
    {
        StartCoroutine(RunDiamondReproduce());
    }

    private IEnumerator RunAuto()
    {
        var sb = new StringBuilder(16000);
        sb.AppendLine("PHASE 72B — GHOST IDENTITY (legacy orange path)");
        sb.AppendLine("==============================================");

        LevelManager lm = Object.FindFirstObjectByType<LevelManager>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (lm == null || board == null)
        {
            Finish(sb, "missing lm/board");
            yield break;
        }

        lm.LoadLevel(42);
        yield return null;
        yield return null;
        yield return null;

        sb.AppendLine("level=" + (lm.CurrentLevel != null ? lm.CurrentLevel.name : "null"));
        sb.AppendLine();
        sb.AppendLine("--- BEFORE MATCH ---");
        sb.AppendLine(BuildDump("before"));

        Block orange = FindNested(ShapeType.Pentagon, ShapeColor.Orange);
        if (orange == null)
        {
            sb.AppendLine();
            sb.AppendLine("orange nested pentagon not found — dump-only mode");
            Finish(sb, "dump-only");
            yield break;
        }

        Block[] all = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Block other = all[i];
            if (other == null || other == orange || other.IsSettled)
            {
                continue;
            }

            board.UnregisterBlock(other);
        }

        Vector2Int sourceAnchor = new Vector2Int(4, 3);
        board.UnregisterBlock(orange);
        orange.SetGridPosition(sourceAnchor);
        board.TryRegisterBlock(orange, sourceAnchor);
        yield return null;

        BlockMover mover = orange.GetComponent<BlockMover>();
        if (mover == null)
        {
            Finish(sb, "no BlockMover");
            yield break;
        }

        mover.StartCoroutine(mover.PlayResolvedAutoMatch(board, new Vector2Int(5, 3)));

        int frames = 0;
        while (frames < 500 && (mover.IsMoving || orange.HasPendingLayerExtraction))
        {
            frames++;
            yield return null;
        }

        for (int i = 0; i < 45; i++)
        {
            yield return null;
        }

        sb.AppendLine();
        sb.AppendLine("--- AFTER MATCH (frames=" + frames + ") ---");
        sb.AppendLine(BuildDump("after"));
        sb.AppendLine();
        sb.AppendLine("--- MISMATCH CANDIDATES ---");
        sb.AppendLine(BuildMismatchSection());
        Finish(sb, "complete");
    }

    private IEnumerator RunDiamondReproduce()
    {
        var sb = new StringBuilder(24000);
        sb.AppendLine("PHASE 72B — DIAMOND GREEN GHOST REPRODUCE");
        sb.AppendLine("=========================================");

        LevelManager lm = Object.FindFirstObjectByType<LevelManager>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (lm == null || board == null)
        {
            Finish(sb, "missing lm/board");
            yield break;
        }

        // Prefer Campaign_43_Reference by name — index 42 drifts across databases.
        LevelData level43 = null;
        if (lm.CurrentLevel != null && lm.CurrentLevel.name.Contains("43"))
        {
            level43 = lm.CurrentLevel;
        }
        else
        {
            LevelData[] allLevels = Resources.FindObjectsOfTypeAll<LevelData>();
            for (int i = 0; i < allLevels.Length; i++)
            {
                if (allLevels[i] != null && allLevels[i].name.Contains("Campaign_43"))
                {
                    level43 = allLevels[i];
                    break;
                }
            }
        }

        if (level43 != null)
        {
            lm.LoadLevel(level43);
        }
        else
        {
            lm.LoadLevel(42);
        }

        yield return null;
        yield return null;
        yield return null;

        sb.AppendLine("level=" + (lm.CurrentLevel != null ? lm.CurrentLevel.name : "null"));

        Block diamond = FindNested(ShapeType.Diamond, ShapeColor.Green);
        if (diamond == null)
        {
            sb.AppendLine("FAIL: green/red nested diamond not found");
            Finish(sb, "no-diamond");
            yield break;
        }

        // Clear blockers so diamond at (1,0)/(1,1) can reach green nests (0,7)/(0,8).
        Block[] all = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Block other = all[i];
            if (other == null || other == diamond || other.IsSettled)
            {
                continue;
            }

            board.UnregisterBlock(other);
        }

        // Keep natural SOURCE (1,0) — this matches the player-reported ghost path.
        sb.AppendLine(
            "diamond cells=" + diamond.CellCount +
            " grid=" + diamond.GridPosition +
            " outer0=" + diamond.GetActiveIdentity(0) +
            " inner0=" + diamond.HasInnerLayerAt(0));
        yield return null;
        yield return null;

        Target greenNest = board.GetTargetAt(new Vector2Int(0, 7));
        sb.AppendLine(
            "greenNest@" + new Vector2Int(0, 7) + "=" +
            (greenNest != null
                ? greenNest.GetRequiredIdentityAtWorld(new Vector2Int(0, 7)).ToString()
                : "null"));

        sb.AppendLine();
        sb.AppendLine("--- BEFORE MATCH ---");
        sb.AppendLine(BuildDump("before"));

        BlockMover mover = diamond.GetComponent<BlockMover>();
        if (mover == null)
        {
            Finish(sb, "no BlockMover");
            yield break;
        }

        // Match left into green diamond nests from natural (1,0) source.
        mover.StartCoroutine(mover.PlayResolvedAutoMatch(board, new Vector2Int(0, 7)));

        int frames = 0;
        while (frames < 600
            && (mover.IsMoving
                || (diamond != null && !diamond.IsSettled && diamond.HasPendingLayerExtraction)))
        {
            frames++;
            yield return null;
        }

        for (int i = 0; i < 60; i++)
        {
            yield return null;
        }

        sb.AppendLine();
        sb.AppendLine("--- AFTER MATCH (frames=" + frames + ") ---");
        if (diamond != null && !diamond.IsSettled)
        {
            sb.AppendLine(
                "diamond settled=False cells=" + diamond.CellCount +
                " grid=" + diamond.GridPosition +
                " outer0=" + diamond.GetActiveIdentity(0) +
                " outerColor0=" + diamond.GetOuterColor(0) +
                " inner0=" + diamond.HasInnerLayerAt(0) +
                " pending=" + diamond.HasPendingLayerExtraction);
            PieceView3D v0 = diamond.GetWorldViewForCellIndex(0);
            PieceView3D v1 = diamond.GetWorldViewForCellIndex(1);
            sb.AppendLine(
                "view0 mat=" + (v0 != null && v0.ConfiguredSolidMaterial != null
                    ? v0.ConfiguredSolidMaterial.name
                    : "null") +
                " active=" + (v0 != null && v0.gameObject.activeInHierarchy) +
                " nested=" + (v0 != null && v0.HasNestedInner));
            sb.AppendLine(
                "view1 mat=" + (v1 != null && v1.ConfiguredSolidMaterial != null
                    ? v1.ConfiguredSolidMaterial.name
                    : "null") +
                " active=" + (v1 != null && v1.gameObject.activeInHierarchy) +
                " nested=" + (v1 != null && v1.HasNestedInner));
        }
        else
        {
            sb.AppendLine("diamond settled/missing");
        }

        sb.AppendLine(BuildDump("after"));
        sb.AppendLine();
        sb.AppendLine("--- MISMATCH CANDIDATES ---");
        sb.AppendLine(BuildMismatchSection());
        sb.AppendLine();
        sb.AppendLine("--- DISABLE TEST (green mat PieceViews) ---");
        sb.AppendLine(RunGreenDisableTest());

        Finish(sb, "diamond-complete");
    }

    private static string RunGreenDisableTest()
    {
        var sb = new StringBuilder(2000);
        Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int disabled = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || r.sharedMaterial == null || !r.enabled)
            {
                continue;
            }

            if (r.sharedMaterial.name.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (r.gameObject.name.Contains("Shadow"))
            {
                continue;
            }

            PieceView3D view = r.GetComponentInParent<PieceView3D>();
            GameObject target = view != null ? view.gameObject : r.gameObject;
            sb.AppendLine(
                "DISABLE candidate=" + target.name +
                " id=" + target.GetInstanceID() +
                " mat=" + r.sharedMaterial.name +
                " path=" + BuildAbsolutePath(target.transform));
            target.SetActive(false);
            disabled++;
        }

        sb.AppendLine("disabledCount=" + disabled);
        return sb.ToString();
    }

    public static string BuildDump(string tag)
    {
        var sb = new StringBuilder(20000);
        sb.AppendLine("[DUMP " + tag + "]");

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter == null || presenter.PiecesRoot == null)
        {
            sb.AppendLine("PiecesRoot missing");
            return sb.ToString();
        }

        IGridSpace space = presenter.GridSpace;
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var trackedViews = new HashSet<int>();
        var controller = Object.FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Exclude);
        CollectTrackedViewIds(controller, trackedViews);
        CollectTrackedTargetViewIds(controller, trackedViews);

        sb.AppendLine(DumpRoot(tag, "PiecesRoot", presenter.PiecesRoot, space, board, trackedViews));
        sb.AppendLine(DumpRoot(tag, "NestsRoot", presenter.NestsRoot, space, board, trackedViews));
        if (presenter.VfxRoot != null)
        {
            sb.AppendLine(DumpRoot(tag, "VfxRoot", presenter.VfxRoot, space, board, trackedViews));
        }

        sb.AppendLine("--- SCENE GREEN / DIAMOND RENDERERS ---");
        sb.AppendLine(DumpSceneGreenAndDiamond(space, trackedViews));
        return sb.ToString();
    }

    private static string DumpRoot(
        string tag,
        string rootLabel,
        Transform root,
        IGridSpace space,
        BoardManager board,
        HashSet<int> trackedViews)
    {
        var sb = new StringBuilder(8000);
        if (root == null)
        {
            sb.AppendLine(rootLabel + " missing");
            return sb.ToString();
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        sb.AppendLine(rootLabel + " children renderers(incl inactive)=" + renderers.Length);

        for (int i = 0; i < renderers.Length; i++)
        {
            AppendRendererLine(sb, tag, root, renderers[i], space, board, trackedViews);
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null || child.name == null)
            {
                continue;
            }

            if (child.name.StartsWith("NestedInnerResidual_")
                || child.name.StartsWith("NestedInnerTravel_")
                || child.name.StartsWith("Block3D_")
                || child.name.StartsWith("Nest3D_"))
            {
                sb.AppendLine(
                    "CHILD " + child.name +
                    " id=" + child.GetInstanceID() +
                    " active=" + child.gameObject.activeSelf +
                    " hier=" + child.gameObject.activeInHierarchy +
                    " pos=" + Format(child.position) +
                    " cell=" + (space != null ? space.WorldToGrid(child.position).ToString() : "?"));
            }
        }

        return sb.ToString();
    }

    private static string DumpSceneGreenAndDiamond(IGridSpace space, HashSet<int> trackedViews)
    {
        var sb = new StringBuilder(6000);
        Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int hits = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || r.sharedMaterial == null)
            {
                continue;
            }

            string matName = r.sharedMaterial.name;
            bool greenName = matName.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            bool diamondMesh = mf != null
                && mf.sharedMesh != null
                && mf.sharedMesh.name.IndexOf("Diamond", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool visible = r.enabled && r.gameObject.activeInHierarchy;

            if (!greenName && !(diamondMesh && visible))
            {
                continue;
            }

            hits++;
            GameObject go = r.gameObject;
            PieceView3D view = go.GetComponentInParent<PieceView3D>();
            Vector3 pos = go.transform.position;
            Vector2Int cell = space != null ? space.WorldToGrid(pos) : new Vector2Int(-999, -999);
            bool tracked = view != null && trackedViews.Contains(view.GetInstanceID());
            bool hasShadow = view != null && view.transform.Find("ContactShadow3D") != null;

            sb.AppendLine(
                (greenName ? "GREEN" : "DIAMOND") +
                " mat=" + matName +
                " go=" + go.name +
                " id=" + go.GetInstanceID() +
                " enabled=" + r.enabled +
                " hier=" + go.activeInHierarchy +
                " pos=" + Format(pos) +
                " cell=" + cell +
                " view=" + (view != null ? view.name : "null") +
                " tracked=" + tracked +
                " shadow=" + hasShadow +
                " path=" + BuildAbsolutePath(go.transform));
        }

        sb.AppendLine("sceneGreenOrDiamondHits=" + hits);
        return sb.ToString();
    }

    private static void AppendRendererLine(
        StringBuilder sb,
        string tag,
        Transform root,
        Renderer r,
        IGridSpace space,
        BoardManager board,
        HashSet<int> trackedViews)
    {
        if (r == null)
        {
            return;
        }

        GameObject go = r.gameObject;
        PieceView3D view = go.GetComponentInParent<PieceView3D>();
        Transform t = go.transform;
        Vector3 pos = t.position;
        Vector2Int cell = space != null ? space.WorldToGrid(pos) : new Vector2Int(-999, -999);
        Block logicalAtCell = board != null ? board.GetBlockAt(cell) : null;
        Target targetAtCell = board != null ? board.GetTargetAt(cell) : null;

        int viewId = view != null ? view.GetInstanceID() : 0;
        bool tracked = view != null && trackedViews.Contains(viewId);
        Block src = view != null ? FindBlockOwningView(view) : null;
        Target tgtOwner = view != null ? FindTargetOwningView(view) : null;

        string meshName = "?";
        MeshFilter mf = r.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            meshName = mf.sharedMesh.name;
        }

        string matName = r.sharedMaterial != null ? r.sharedMaterial.name : "null";
        string path = BuildPath(t, root);
        bool visible = r.enabled && r.gameObject.activeInHierarchy;
        if (!visible && tag == "after")
        {
            if (view == null || go != view.gameObject)
            {
                return;
            }
        }

        sb.AppendLine(
            "REN name=" + go.name +
            " id=" + go.GetInstanceID() +
            " type=" + r.GetType().Name +
            " enabled=" + r.enabled +
            " active=" + go.activeSelf +
            " hier=" + go.activeInHierarchy +
            " pos=" + Format(pos) +
            " cell=" + cell +
            " path=" + path);
        sb.AppendLine(
            "    view=" + (view != null ? view.name + "#" + viewId : "null") +
            " tracked=" + tracked +
            " shape=" + (view != null ? view.ConfiguredShape.ToString() : "-") +
            " nest=" + (view != null && view.HasNestedInner) +
            " srcBlock=" + (src != null ? src.GetInstanceID().ToString() : "0") +
            " srcTarget=" + (tgtOwner != null ? tgtOwner.GetInstanceID().ToString() : "0") +
            " logicalBlockAtCell=" + (logicalAtCell != null ? logicalAtCell.GetInstanceID().ToString() : "0") +
            " targetAtCell=" + (targetAtCell != null) +
            " mesh=" + meshName +
            " mat=" + matName);

        if (visible && matName.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            sb.AppendLine("    *** GREEN MAT HIT ***");
        }

        if (visible
            && view != null
            && logicalAtCell == null
            && targetAtCell == null
            && !go.name.Contains("Connector")
            && !go.name.Contains("Shadow")
            && !path.Contains("ContactShadow"))
        {
            sb.AppendLine("    *** SUSPECT: visible PieceView renderer at cell with NO logical block/target ***");
        }

        if (visible && view != null && src != null && !src.IsSettled)
        {
            bool ownsCell = false;
            int matchedCellIndex = -1;
            for (int c = 0; c < src.CellCount; c++)
            {
                if (src.GetCellWorld(c) == cell)
                {
                    ownsCell = true;
                    matchedCellIndex = c;
                    break;
                }
            }

            if (!ownsCell)
            {
                sb.AppendLine(
                    "    *** SUSPECT: view owned by block " + src.GetInstanceID() +
                    " but seated at cell " + cell + " not in that block footprint ***");
            }
            else if (matchedCellIndex >= 0)
            {
                ShapeColor logicalOuter = src.GetOuterColor(matchedCellIndex);
                if (logicalOuter != ShapeColor.Green
                    && matName.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sb.AppendLine(
                        "    *** SUSPECT: logical outer=" + logicalOuter +
                        " but renderer still uses GREEN mat ***");
                }
            }
        }

        if (visible
            && (go.name.StartsWith("NestedInnerResidual_")
                || path.Contains("NestedInnerResidual_")
                || (go.name == "NestedInner3D" && view == null)))
        {
            sb.AppendLine("    *** SUSPECT: nested residual / detached NestedInner3D ***");
        }
    }

    private static string BuildMismatchSection()
    {
        var sb = new StringBuilder(4000);
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (presenter == null || presenter.PiecesRoot == null || board == null)
        {
            return "missing presenter/board";
        }

        IGridSpace space = presenter.GridSpace;
        var controller = Object.FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Exclude);
        var tracked = new HashSet<int>();
        CollectTrackedViewIds(controller, tracked);
        CollectTrackedTargetViewIds(controller, tracked);

        var roots = new List<Transform>(2) { presenter.PiecesRoot };
        if (presenter.NestsRoot != null)
        {
            roots.Add(presenter.NestsRoot);
        }

        int suspects = 0;
        for (int r = 0; r < roots.Count; r++)
        {
            PieceView3D[] views = roots[r].GetComponentsInChildren<PieceView3D>(true);
            for (int i = 0; i < views.Length; i++)
            {
                PieceView3D view = views[i];
                if (view == null || !view.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector2Int cell = space != null
                    ? space.WorldToGrid(view.transform.position)
                    : new Vector2Int(-999, -999);
                Block at = board.GetBlockAt(cell);
                Target tgt = board.GetTargetAt(cell);
                Block owner = FindBlockOwningView(view);
                Target targetOwner = FindTargetOwningView(view);
                bool trackedOk = tracked.Contains(view.GetInstanceID());
                bool ghostEmptyCell = at == null && tgt == null;
                bool untracked = !trackedOk && owner == null && targetOwner == null;

                Renderer meshRen = null;
                MeshRenderer[] mrs = view.GetComponentsInChildren<MeshRenderer>(true);
                for (int m = 0; m < mrs.Length; m++)
                {
                    if (mrs[m] != null
                        && mrs[m].enabled
                        && mrs[m].sharedMaterial != null
                        && mrs[m].gameObject.name == "Mesh")
                    {
                        meshRen = mrs[m];
                        break;
                    }
                }

                bool greenMat = meshRen != null
                    && meshRen.sharedMaterial != null
                    && meshRen.sharedMaterial.name.IndexOf(
                        "Green",
                        System.StringComparison.OrdinalIgnoreCase) >= 0;

                if (!ghostEmptyCell && !untracked && !greenMat)
                {
                    continue;
                }

                suspects++;
                sb.AppendLine(
                    "CANDIDATE#" + suspects +
                    " name=" + view.name +
                    " id=" + view.GetInstanceID() +
                    " shape=" + view.ConfiguredShape +
                    " cell=" + cell +
                    " tracked=" + trackedOk +
                    " owner=" + (owner != null ? owner.GetInstanceID().ToString() : "0") +
                    " targetOwner=" + (targetOwner != null ? targetOwner.GetInstanceID().ToString() : "0") +
                    " greenMat=" + greenMat +
                    " mat=" + (meshRen != null && meshRen.sharedMaterial != null
                        ? meshRen.sharedMaterial.name
                        : "-") +
                    " ghostEmptyCell=" + ghostEmptyCell +
                    " untracked=" + untracked +
                    " pos=" + Format(view.transform.position));
            }
        }

        sb.AppendLine("suspectCount=" + suspects);
        return sb.ToString();
    }

    private static void CollectTrackedViewIds(BoardPresentationController controller, HashSet<int> dst)
    {
        CollectMapViews(controller, "worldViewsByBlockId", dst);
        CollectListMapViews(controller, "extraViewsByBlockId", dst);
    }

    private static void CollectTrackedTargetViewIds(BoardPresentationController controller, HashSet<int> dst)
    {
        CollectMapViews(controller, "worldViewsByTargetId", dst);
        CollectListMapViews(controller, "extraViewsByTargetId", dst);
    }

    private static void CollectMapViews(BoardPresentationController controller, string fieldName, HashSet<int> dst)
    {
        if (controller == null || dst == null)
        {
            return;
        }

        FieldInfo field = typeof(BoardPresentationController).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            return;
        }

        var map = field.GetValue(controller) as Dictionary<int, PieceView3D>;
        if (map == null)
        {
            return;
        }

        foreach (KeyValuePair<int, PieceView3D> pair in map)
        {
            if (pair.Value != null)
            {
                dst.Add(pair.Value.GetInstanceID());
            }
        }
    }

    private static void CollectListMapViews(
        BoardPresentationController controller,
        string fieldName,
        HashSet<int> dst)
    {
        if (controller == null || dst == null)
        {
            return;
        }

        FieldInfo field = typeof(BoardPresentationController).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            return;
        }

        var map = field.GetValue(controller) as Dictionary<int, List<PieceView3D>>;
        if (map == null)
        {
            return;
        }

        foreach (KeyValuePair<int, List<PieceView3D>> pair in map)
        {
            List<PieceView3D> list = pair.Value;
            if (list == null)
            {
                continue;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                {
                    dst.Add(list[i].GetInstanceID());
                }
            }
        }
    }

    private static Block FindBlockOwningView(PieceView3D view)
    {
        if (view == null)
        {
            return null;
        }

        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block b = blocks[i];
            if (b == null || b.IsSettled)
            {
                continue;
            }

            if (b.WorldView == view)
            {
                return b;
            }

            IReadOnlyList<PieceView3D> extras = b.ExtraWorldViews;
            if (extras == null)
            {
                continue;
            }

            for (int e = 0; e < extras.Count; e++)
            {
                if (extras[e] == view)
                {
                    return b;
                }
            }
        }

        return null;
    }

    private static Target FindTargetOwningView(PieceView3D view)
    {
        if (view == null)
        {
            return null;
        }

        Target[] targets = Object.FindObjectsByType<Target>(FindObjectsSortMode.None);
        for (int i = 0; i < targets.Length; i++)
        {
            Target t = targets[i];
            if (t == null || t.IsMatched)
            {
                continue;
            }

            if (t.WorldView == view)
            {
                return t;
            }

            IReadOnlyList<PieceView3D> extras = t.ExtraWorldViews;
            if (extras == null)
            {
                continue;
            }

            for (int e = 0; e < extras.Count; e++)
            {
                if (extras[e] == view)
                {
                    return t;
                }
            }
        }

        return null;
    }

    private static Block FindNested(ShapeType shape, ShapeColor color)
    {
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block b = blocks[i];
            if (b == null || b.IsSettled)
            {
                continue;
            }

            for (int c = 0; c < b.CellCount; c++)
            {
                MatchIdentity id = b.GetActiveIdentity(c);
                if (id.Shape == shape && id.Color == color && b.HasInnerLayerAt(c))
                {
                    return b;
                }
            }
        }

        return null;
    }

    private static string BuildPath(Transform t, Transform root)
    {
        var parts = new List<string>(8);
        Transform cur = t;
        int guard = 0;
        while (cur != null && cur != root && guard++ < 12)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }

        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    private static string BuildAbsolutePath(Transform t)
    {
        var parts = new List<string>(12);
        Transform cur = t;
        int guard = 0;
        while (cur != null && guard++ < 16)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }

        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    private static string Format(Vector3 v)
    {
        return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")";
    }

    private void Finish(StringBuilder sb, string tag)
    {
        sb.AppendLine();
        sb.AppendLine("DONE tag=" + tag);
        string text = sb.ToString();
        Phase72BGhostIdentityDump.WriteReport(text);
        Debug.Log("[72B]\n" + text);
        EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                Destroy(gameObject);
            }
        };
    }
}
