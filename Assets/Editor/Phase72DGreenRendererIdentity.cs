using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 72D — identify the exact Renderer producing green ghost pixels.
/// Diagnostic only: dump + temporary renderer.enabled=false. No presentation fixes.
/// Menu: Shape Nest / Phase 72D …
/// Writes Captures/phase72d-green-renderer.txt
/// </summary>
public static class Phase72DGreenRendererIdentity
{
    private const string ReportPath = "Captures/phase72d-green-renderer.txt";
    private const string SessionKey = "Phase72DGreenDumpRunning";

    [MenuItem("Shape Nest/Phase 72D Dump Green Renderers NOW (Play Mode)")]
    public static void DumpNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[72D] Enter Play Mode, reproduce the green ghost, then run Dump NOW.");
            return;
        }

        string report = Phase72DGreenRendererProbe.BuildFullReport("manual-now", runDisableTest: false);
        WriteReport(report);
        Debug.Log("[72D]\n" + report);
    }

    [MenuItem("Shape Nest/Phase 72D Dump + Disable-Test Green Renderers NOW")]
    public static void DumpAndDisableNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[72D] Enter Play Mode, reproduce the green ghost, then run Dump + Disable.");
            return;
        }

        string report = Phase72DGreenRendererProbe.BuildFullReport("manual-disable", runDisableTest: true);
        WriteReport(report);
        Debug.Log("[72D]\n" + report);
    }

    [MenuItem("Shape Nest/Phase 72D Reproduce Diamond Peel + Identify Green")]
    public static void ReproduceAndIdentify()
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

    private static void OnPlayMode(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(SessionKey, false))
        {
            EditorApplication.delayCall += StartProbe;
        }

        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            SessionState.SetBool(SessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayMode;
        }
    }

    private static void StartProbe()
    {
        if (!EditorApplication.isPlaying || !SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        var host = new GameObject("Phase72D_GreenRendererProbe");
        Object.DontDestroyOnLoad(host);
        host.AddComponent<Phase72DGreenRendererProbe>().BeginReproduce();
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

/// <summary>Full-scene green renderer identity + one-by-one disable test.</summary>
public sealed class Phase72DGreenRendererProbe : MonoBehaviour
{
    private readonly Dictionary<int, GreenSighting> _seenBeforeMatch = new Dictionary<int, GreenSighting>();
    private readonly List<string> _frameLog = new List<string>(128);

    private struct GreenSighting
    {
        public int RendererId;
        public int GoId;
        public string Path;
        public string Mat;
        public Vector3 Pos;
        public bool Enabled;
        public bool Hier;
    }

    public void BeginReproduce()
    {
        StartCoroutine(RunReproduce());
    }

    private IEnumerator RunReproduce()
    {
        var sb = new StringBuilder(48000);
        sb.AppendLine("PHASE 72D — GREEN RENDERER IDENTITY");
        sb.AppendLine("====================================");
        sb.AppendLine("NO presentation fixes. Dump + disable test only.");
        sb.AppendLine();

        LevelManager lm = Object.FindFirstObjectByType<LevelManager>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (lm == null || board == null)
        {
            Finish(sb, "missing lm/board");
            yield break;
        }

        LevelData level43 = null;
        LevelData[] allLevels = Resources.FindObjectsOfTypeAll<LevelData>();
        for (int i = 0; i < allLevels.Length; i++)
        {
            if (allLevels[i] != null && allLevels[i].name.Contains("Campaign_43"))
            {
                level43 = allLevels[i];
                break;
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
            Finish(sb, "no-diamond");
            yield break;
        }

        // Clear blockers so diamond can reach green nests.
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

        sb.AppendLine(
            "diamond cells=" + diamond.CellCount +
            " grid=" + diamond.GridPosition +
            " outer0=" + diamond.GetActiveIdentity(0) +
            " inner0=" + diamond.HasInnerLayerAt(0));

        yield return null;
        yield return null;

        SnapshotGreenIds(_seenBeforeMatch);
        sb.AppendLine();
        sb.AppendLine("--- BEFORE MATCH: GREEN RENDERERS ---");
        sb.AppendLine(DumpGreenHits("before"));

        BlockMover mover = diamond.GetComponent<BlockMover>();
        if (mover == null)
        {
            Finish(sb, "no BlockMover");
            yield break;
        }

        mover.StartCoroutine(mover.PlayResolvedAutoMatch(board, new Vector2Int(0, 7)));

        int frames = 0;
        int lastGreenCount = -1;
        while (frames < 600
            && (mover.IsMoving
                || (diamond != null && !diamond.IsSettled && diamond.HasPendingLayerExtraction)))
        {
            frames++;
            List<GreenHit> greens = CollectGreenHits(activeOnly: true);
            if (greens.Count != lastGreenCount)
            {
                _frameLog.Add(
                    "frame=" + frames +
                    " greenActiveCount=" + greens.Count +
                    " moving=" + mover.IsMoving +
                    " pending=" + (diamond != null && diamond.HasPendingLayerExtraction) +
                    " " + SummarizeGreens(greens));
                lastGreenCount = greens.Count;
            }

            yield return null;
        }

        for (int i = 0; i < 90; i++)
        {
            frames++;
            List<GreenHit> greens = CollectGreenHits(activeOnly: true);
            if (greens.Count != lastGreenCount)
            {
                _frameLog.Add(
                    "frame=" + frames +
                    " SETTLE_WAIT greenActiveCount=" + greens.Count +
                    " " + SummarizeGreens(greens));
                lastGreenCount = greens.Count;
            }

            yield return null;
        }

        sb.AppendLine();
        sb.AppendLine("--- FRAME GREEN COUNT CHANGES ---");
        for (int i = 0; i < _frameLog.Count; i++)
        {
            sb.AppendLine(_frameLog[i]);
        }

        sb.AppendLine();
        sb.AppendLine("--- AFTER MATCH: SURVIVOR LOGICAL STATE ---");
        if (diamond != null && !diamond.IsSettled)
        {
            for (int c = 0; c < diamond.CellCount; c++)
            {
                sb.AppendLine(
                    "cell[" + c + "] world=" + diamond.GetCellWorld(c) +
                    " outer=" + diamond.GetActiveIdentity(c) +
                    " outerColor=" + diamond.GetOuterColor(c) +
                    " inner=" + diamond.HasInnerLayerAt(c) +
                    " pending=" + diamond.HasPendingLayerExtraction);
                PieceView3D view = diamond.GetWorldViewForCellIndex(c);
                if (view != null)
                {
                    sb.AppendLine(
                        "  view=" + view.name +
                        " id=" + view.GetInstanceID() +
                        " active=" + view.gameObject.activeInHierarchy +
                        " nested=" + view.HasNestedInner +
                        " cfgMat=" + (view.ConfiguredSolidMaterial != null
                            ? view.ConfiguredSolidMaterial.name
                            : "null"));
                }
            }
        }
        else
        {
            sb.AppendLine("diamond settled/missing");
        }

        sb.AppendLine();
        sb.AppendLine("--- AFTER MATCH: FULL REPORT ---");
        sb.AppendLine(BuildFullReport("after-match", runDisableTest: true));

        Finish(sb, "complete");
    }

    public static string BuildFullReport(string tag, bool runDisableTest)
    {
        var sb = new StringBuilder(32000);
        sb.AppendLine("[72D FULL " + tag + "] frame=" + Time.frameCount + " t=" + Time.time.ToString("F3"));

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        IGridSpace space = presenter != null ? presenter.GridSpace : null;
        var controller = Object.FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Exclude);
        var tracked = new HashSet<int>();
        CollectTrackedViewIds(controller, tracked);
        CollectTrackedTargetViewIds(controller, tracked);

        Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        sb.AppendLine("sceneRendererCount(inclInactive)=" + all.Length);

        int activeCount = 0;
        int greenActive = 0;
        int greenInactive = 0;
        var greenHits = new List<GreenHit>(32);
        var allActiveLines = new List<string>(256);

        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null)
            {
                continue;
            }

            bool active = r.enabled && r.gameObject.activeInHierarchy;
            if (active)
            {
                activeCount++;
                allActiveLines.Add(FormatRendererLine(r, space, board, tracked, detailed: false));
            }

            GreenHit hit;
            if (!TryClassifyGreen(r, out hit))
            {
                continue;
            }

            if (active)
            {
                greenActive++;
                greenHits.Add(hit);
            }
            else
            {
                greenInactive++;
            }
        }

        sb.AppendLine("activeRendererCount=" + activeCount);
        sb.AppendLine("greenActiveCount=" + greenActive);
        sb.AppendLine("greenInactiveButExistsCount=" + greenInactive);
        sb.AppendLine();

        sb.AppendLine("--- GREEN ACTIVE RENDERERS (definitive candidates) ---");
        for (int i = 0; i < greenHits.Count; i++)
        {
            AppendGreenDetail(sb, greenHits[i], space, board, tracked);
        }

        sb.AppendLine();
        sb.AppendLine("--- ALL RENDERERS AT SURVIVOR CELLS (1,0) (1,1) AND NEAR GREENS ---");
        var focusCells = new HashSet<Vector2Int>
        {
            new Vector2Int(1, 0),
            new Vector2Int(1, 1),
            new Vector2Int(0, 7),
            new Vector2Int(0, 8)
        };
        for (int i = 0; i < greenHits.Count; i++)
        {
            focusCells.Add(greenHits[i].Cell);
        }

        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || space == null)
            {
                continue;
            }

            Vector2Int cell = space.WorldToGrid(r.transform.position);
            if (!focusCells.Contains(cell))
            {
                continue;
            }

            if (r.gameObject.name.Contains("Shadow") || r.gameObject.name.Contains("ChainLink"))
            {
                continue;
            }

            sb.AppendLine(FormatRendererLine(r, space, board, tracked, detailed: true));
        }

        sb.AppendLine();
        sb.AppendLine("--- DUPLICATE VISUALS AT SAME CELL (non-shadow) ---");
        sb.AppendLine(BuildSameCellDuplicates(space, board));

        if (runDisableTest)
        {
            sb.AppendLine();
            sb.AppendLine("--- DISABLE TEST (renderer.enabled=false one-by-one) ---");
            sb.AppendLine(RunDisableTest(greenHits));
        }

        sb.AppendLine();
        sb.AppendLine("--- ALL ACTIVE RENDERERS (compact) ---");
        sb.AppendLine("count=" + allActiveLines.Count);
        // Keep compact list for forensics; green section above is the identity source.
        for (int i = 0; i < allActiveLines.Count; i++)
        {
            if (allActiveLines[i].IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0
                || allActiveLines[i].IndexOf("Diamond", System.StringComparison.OrdinalIgnoreCase) >= 0
                || allActiveLines[i].IndexOf("NestedInner", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sb.AppendLine(allActiveLines[i]);
            }
        }

        return sb.ToString();
    }

    private static string DumpGreenHits(string tag)
    {
        var sb = new StringBuilder(8000);
        List<GreenHit> hits = CollectGreenHits(activeOnly: false);
        sb.AppendLine("tag=" + tag + " greenHits(inclInactive)=" + hits.Count);
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        IGridSpace space = presenter != null ? presenter.GridSpace : null;
        var tracked = new HashSet<int>();
        var controller = Object.FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Exclude);
        CollectTrackedViewIds(controller, tracked);
        CollectTrackedTargetViewIds(controller, tracked);
        for (int i = 0; i < hits.Count; i++)
        {
            AppendGreenDetail(sb, hits[i], space, board, tracked);
        }

        return sb.ToString();
    }

    private static string RunDisableTest(List<GreenHit> greenHits)
    {
        var sb = new StringBuilder(4000);
        // Prefer Block3D green meshes (not Nest3D hollows, not shadows).
        var ordered = new List<GreenHit>(greenHits.Count);
        for (int i = 0; i < greenHits.Count; i++)
        {
            GreenHit h = greenHits[i];
            if (h.Path.Contains("Nest3D") || h.GoName.Contains("Shadow"))
            {
                continue;
            }

            ordered.Add(h);
        }

        for (int i = 0; i < greenHits.Count; i++)
        {
            GreenHit h = greenHits[i];
            if (h.Path.Contains("Nest3D") || h.GoName.Contains("Shadow"))
            {
                ordered.Add(h);
            }
        }

        if (ordered.Count == 0)
        {
            sb.AppendLine("NO green active renderers to disable.");
            return sb.ToString();
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            GreenHit h = ordered[i];
            Renderer r = FindRendererById(h.RendererId);
            if (r == null)
            {
                sb.AppendLine("MISSING rendererId=" + h.RendererId + " path=" + h.Path);
                continue;
            }

            bool wasEnabled = r.enabled;
            r.enabled = false;
            int remaining = CountActiveGreenNonNestNonShadow();
            sb.AppendLine(
                "DISABLED rendererId=" + h.RendererId +
                " go=" + h.GoName +
                " goId=" + h.GoId +
                " mat=" + h.SharedMat +
                " path=" + h.Path +
                " cell=" + h.Cell +
                " wasEnabled=" + wasEnabled +
                " remainingGreenBlockish=" + remaining);
            if (remaining == 0)
            {
                sb.AppendLine(">>> DEFINITIVE: disabling this renderer removed all non-nest green block renderers.");
                // Leave disabled for visual confirmation; do not destroy.
                break;
            }
        }

        int afterNests = CountActiveGreenIncludingNests();
        sb.AppendLine("remainingGreenIncludingNests=" + afterNests);
        return sb.ToString();
    }

    private static int CountActiveGreenNonNestNonShadow()
    {
        List<GreenHit> hits = CollectGreenHits(activeOnly: true);
        int n = 0;
        for (int i = 0; i < hits.Count; i++)
        {
            if (hits[i].Path.Contains("Nest3D") || hits[i].GoName.Contains("Shadow"))
            {
                continue;
            }

            n++;
        }

        return n;
    }

    private static int CountActiveGreenIncludingNests()
    {
        return CollectGreenHits(activeOnly: true).Count;
    }

    private static Renderer FindRendererById(int id)
    {
        Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].GetInstanceID() == id)
            {
                return all[i];
            }
        }

        return null;
    }

    private struct GreenHit
    {
        public int RendererId;
        public int GoId;
        public string GoName;
        public string Path;
        public string Parent;
        public string Type;
        public string Mesh;
        public string SharedMat;
        public string InstancedMat;
        public string Shader;
        public Vector3 Pos;
        public Vector2Int Cell;
        public bool ActiveSelf;
        public bool ActiveHier;
        public bool Enabled;
        public string Reason;
    }

    private static bool TryClassifyGreen(Renderer r, out GreenHit hit)
    {
        hit = default;
        if (r == null)
        {
            return false;
        }

        Material shared = null;
        Material instanced = null;
        try
        {
            shared = r.sharedMaterial;
        }
        catch
        {
            shared = null;
        }

        // Avoid instantiating materials in edit dumps when possible; only peek name via shared.
        string sharedName = shared != null ? shared.name : "";
        string instancedName = "";
        string shaderName = shared != null && shared.shader != null ? shared.shader.name : "";
        bool nameGreen = sharedName.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool colorGreen = IsApproxGreenColor(shared);
        bool mpbGreen = false;

        // Live color can be green via MPB / instance even when sharedMaterial name is Red.
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        if (mpb != null && !mpb.isEmpty)
        {
            int idBase = Shader.PropertyToID("_BaseColor");
            int idCol = Shader.PropertyToID("_Color");
            if (mpb.HasProperty(idBase) && IsApproxGreenColorValue(mpb.GetColor(idBase)))
            {
                mpbGreen = true;
            }
            else if (mpb.HasProperty(idCol) && IsApproxGreenColorValue(mpb.GetColor(idCol)))
            {
                mpbGreen = true;
            }
        }

        if (!nameGreen && !colorGreen)
        {
            // Also check material array slots.
            Material[] shareds = null;
            try
            {
                shareds = r.sharedMaterials;
            }
            catch
            {
                shareds = null;
            }

            if (shareds != null)
            {
                for (int i = 0; i < shareds.Length; i++)
                {
                    if (shareds[i] == null)
                    {
                        continue;
                    }

                    if (shareds[i].name.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || IsApproxGreenColor(shareds[i]))
                    {
                        nameGreen = shareds[i].name.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        colorGreen = IsApproxGreenColor(shareds[i]);
                        shared = shareds[i];
                        sharedName = shareds[i].name;
                        shaderName = shareds[i].shader != null ? shareds[i].shader.name : "";
                        break;
                    }
                }
            }
        }

        if (!nameGreen && !colorGreen && !mpbGreen)
        {
            // Last resort: instance material color (may instantiate).
            try
            {
                Material live = r.material;
                if (live != null
                    && (live.name.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || IsApproxGreenColor(live)))
                {
                    nameGreen = live.name.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    colorGreen = IsApproxGreenColor(live);
                    instancedName = live.name;
                    sharedName = live.name;
                    shaderName = live.shader != null ? live.shader.name : shaderName;
                }
            }
            catch
            {
                // ignore
            }
        }

        if (!nameGreen && !colorGreen && !mpbGreen)
        {
            return false;
        }

        MeshFilter mf = r.GetComponent<MeshFilter>();
        string meshName = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-";
        Transform t = r.transform;
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace space = presenter != null ? presenter.GridSpace : null;
        Vector3 pos = t.position;
        Vector2Int cell = space != null ? space.WorldToGrid(pos) : new Vector2Int(-999, -999);

        hit = new GreenHit
        {
            RendererId = r.GetInstanceID(),
            GoId = r.gameObject.GetInstanceID(),
            GoName = r.gameObject.name,
            Path = BuildAbsolutePath(t),
            Parent = t.parent != null ? t.parent.name : "null",
            Type = r.GetType().Name,
            Mesh = meshName,
            SharedMat = sharedName,
            InstancedMat = instancedName,
            Shader = shaderName,
            Pos = pos,
            Cell = cell,
            ActiveSelf = r.gameObject.activeSelf,
            ActiveHier = r.gameObject.activeInHierarchy,
            Enabled = r.enabled,
            Reason = (nameGreen ? "matName" : "")
                + (colorGreen ? "+color" : "")
                + (mpbGreen ? "+mpb" : "")
        };
        return true;
    }

    private static bool IsApproxGreenColor(Material mat)
    {
        if (mat == null)
        {
            return false;
        }

        Color c;
        if (mat.HasProperty("_BaseColor"))
        {
            c = mat.GetColor("_BaseColor");
        }
        else if (mat.HasProperty("_Color"))
        {
            c = mat.GetColor("_Color");
        }
        else
        {
            return false;
        }

        return IsApproxGreenColorValue(c);
    }

    private static bool IsApproxGreenColorValue(Color c)
    {
        // Bright green, not cyan/yellow-ish.
        return c.g > 0.45f && c.g > c.r + 0.15f && c.g > c.b + 0.15f;
    }

    private static List<GreenHit> CollectGreenHits(bool activeOnly)
    {
        var list = new List<GreenHit>(32);
        Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            GreenHit hit;
            if (!TryClassifyGreen(r, out hit))
            {
                continue;
            }

            if (activeOnly && !(hit.Enabled && hit.ActiveHier))
            {
                continue;
            }

            list.Add(hit);
        }

        return list;
    }

    private static void AppendGreenDetail(
        StringBuilder sb,
        GreenHit h,
        IGridSpace space,
        BoardManager board,
        HashSet<int> tracked)
    {
        Renderer r = FindRendererById(h.RendererId);
        PieceView3D view = r != null ? r.GetComponentInParent<PieceView3D>() : null;
        Block src = view != null ? FindBlockOwningView(view) : null;
        Target tgtOwner = view != null ? FindTargetOwningView(view) : null;
        Block logical = board != null ? board.GetBlockAt(h.Cell) : null;
        Target targetAt = board != null ? board.GetTargetAt(h.Cell) : null;
        bool trackedOk = view != null && tracked.Contains(view.GetInstanceID());

        string logicalOuter = "-";
        string logicalInner = "-";
        if (logical != null)
        {
            for (int c = 0; c < logical.CellCount; c++)
            {
                if (logical.GetCellWorld(c) == h.Cell)
                {
                    logicalOuter = logical.GetActiveIdentity(c).ToString();
                    logicalInner = logical.HasInnerLayerAt(c).ToString();
                    break;
                }
            }
        }

        sb.AppendLine("GREEN_RENDERER");
        sb.AppendLine("  go=" + h.GoName + " goId=" + h.GoId + " rendererId=" + h.RendererId);
        sb.AppendLine("  type=" + h.Type + " mesh=" + h.Mesh);
        sb.AppendLine("  sharedMat=" + h.SharedMat + " shader=" + h.Shader + " reason=" + h.Reason);
        sb.AppendLine("  enabled=" + h.Enabled + " activeSelf=" + h.ActiveSelf + " activeHier=" + h.ActiveHier);
        sb.AppendLine("  parent=" + h.Parent);
        sb.AppendLine("  path=" + h.Path);
        sb.AppendLine("  pos=" + Format(h.Pos) + " cell=" + h.Cell);
        sb.AppendLine(
            "  pieceView=" + (view != null ? view.name + "#" + view.GetInstanceID() : "null") +
            " tracked=" + trackedOk +
            " hasNestedInner=" + (view != null && view.HasNestedInner));
        sb.AppendLine(
            "  srcBlock=" + (src != null ? src.GetInstanceID().ToString() : "0") +
            " srcTarget=" + (tgtOwner != null ? tgtOwner.GetInstanceID().ToString() : "0") +
            " logicalBlockAtCell=" + (logical != null ? logical.GetInstanceID().ToString() : "0") +
            " targetAtCell=" + (targetAt != null));
        sb.AppendLine("  LOGICAL_OUTER=" + logicalOuter + " LOGICAL_INNER=" + logicalInner);
        if (r != null)
        {
            // Live material peek (may instantiate).
            try
            {
                Material live = r.material;
                sb.AppendLine("  liveMaterial=" + (live != null ? live.name : "null"));
            }
            catch (System.Exception ex)
            {
                sb.AppendLine("  liveMaterial=ERR " + ex.Message);
            }
        }
    }

    private static string FormatRendererLine(
        Renderer r,
        IGridSpace space,
        BoardManager board,
        HashSet<int> tracked,
        bool detailed)
    {
        GameObject go = r.gameObject;
        Vector3 pos = go.transform.position;
        Vector2Int cell = space != null ? space.WorldToGrid(pos) : new Vector2Int(-999, -999);
        MeshFilter mf = r.GetComponent<MeshFilter>();
        string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-";
        string mat = r.sharedMaterial != null ? r.sharedMaterial.name : "null";
        PieceView3D view = r.GetComponentInParent<PieceView3D>();
        Block logical = board != null ? board.GetBlockAt(cell) : null;
        string line =
            "REN go=" + go.name +
            " id=" + go.GetInstanceID() +
            " rid=" + r.GetInstanceID() +
            " type=" + r.GetType().Name +
            " mesh=" + mesh +
            " mat=" + mat +
            " cell=" + cell +
            " pos=" + Format(pos) +
            " path=" + BuildAbsolutePath(go.transform);
        if (!detailed)
        {
            return line;
        }

        return line +
            " view=" + (view != null ? view.name : "null") +
            " tracked=" + (view != null && tracked.Contains(view.GetInstanceID())) +
            " logical=" + (logical != null ? logical.GetInstanceID().ToString() : "0") +
            " parent=" + (go.transform.parent != null ? go.transform.parent.name : "null");
    }

    private static string BuildSameCellDuplicates(IGridSpace space, BoardManager board)
    {
        var sb = new StringBuilder(4000);
        if (space == null)
        {
            return "no space";
        }

        var byCell = new Dictionary<Vector2Int, List<Renderer>>();
        Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (r.gameObject.name.Contains("Shadow") || r.gameObject.name.Contains("ChainLink"))
            {
                continue;
            }

            Vector2Int cell = space.WorldToGrid(r.transform.position);
            List<Renderer> list;
            if (!byCell.TryGetValue(cell, out list))
            {
                list = new List<Renderer>(4);
                byCell[cell] = list;
            }

            list.Add(r);
        }

        foreach (KeyValuePair<Vector2Int, List<Renderer>> pair in byCell)
        {
            if (pair.Value.Count < 2)
            {
                continue;
            }

            bool hasGreen = false;
            bool hasRed = false;
            for (int i = 0; i < pair.Value.Count; i++)
            {
                Material m = pair.Value[i].sharedMaterial;
                if (m == null)
                {
                    continue;
                }

                if (m.name.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasGreen = true;
                }

                if (m.name.IndexOf("Red", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasRed = true;
                }
            }

            if (!hasGreen)
            {
                continue;
            }

            Block logical = board != null ? board.GetBlockAt(pair.Key) : null;
            sb.AppendLine(
                "CELL " + pair.Key +
                " rendererCount=" + pair.Value.Count +
                " hasGreen=" + hasGreen +
                " hasRed=" + hasRed +
                " logical=" + (logical != null
                    ? logical.GetInstanceID() + " outer=" + DescribeOuterAt(logical, pair.Key)
                    : "0"));
            for (int i = 0; i < pair.Value.Count; i++)
            {
                Renderer r = pair.Value[i];
                sb.AppendLine(
                    "  " + r.gameObject.name +
                    " rid=" + r.GetInstanceID() +
                    " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.name : "null") +
                    " path=" + BuildAbsolutePath(r.transform));
            }
        }

        return sb.ToString();
    }

    private static string DescribeOuterAt(Block b, Vector2Int cell)
    {
        for (int c = 0; c < b.CellCount; c++)
        {
            if (b.GetCellWorld(c) == cell)
            {
                return b.GetActiveIdentity(c).ToString();
            }
        }

        return "?";
    }

    private void SnapshotGreenIds(Dictionary<int, GreenSighting> dst)
    {
        dst.Clear();
        List<GreenHit> hits = CollectGreenHits(activeOnly: false);
        for (int i = 0; i < hits.Count; i++)
        {
            GreenHit h = hits[i];
            dst[h.RendererId] = new GreenSighting
            {
                RendererId = h.RendererId,
                GoId = h.GoId,
                Path = h.Path,
                Mat = h.SharedMat,
                Pos = h.Pos,
                Enabled = h.Enabled,
                Hier = h.ActiveHier
            };
        }
    }

    private static string SummarizeGreens(List<GreenHit> greens)
    {
        var sb = new StringBuilder(500);
        for (int i = 0; i < greens.Count && i < 8; i++)
        {
            if (i > 0)
            {
                sb.Append(" | ");
            }

            sb.Append(greens[i].GoName);
            sb.Append("@");
            sb.Append(greens[i].Cell);
            sb.Append("/");
            sb.Append(greens[i].SharedMat);
            sb.Append("#");
            sb.Append(greens[i].RendererId);
        }

        return sb.ToString();
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
        Phase72DGreenRendererIdentity.WriteReport(text);
        Debug.Log("[72D]\n" + text);
        EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                Destroy(gameObject);
            }
        };
    }
}
