using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 72F — dump EVERY renderer at survivor diamond cells with live colors/MPB/scale.
/// Use when green is visible but Phase72D greenActiveCount=0.
/// Menu: Shape Nest / Phase 72F Dump Survivor Cell Renderers NOW
/// </summary>
public static class Phase72FLiveGreenDump
{
    private const string ReportPath = "Captures/phase72d-green-renderer.txt";

    [MenuItem("Shape Nest/Phase 72F Dump Survivor Cell Renderers NOW")]
    public static void DumpNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[72F] Play Mode + visible ghost required.");
            return;
        }

        string text = Phase72FLiveGreenDumpRuntime.DumpStatic();
        Debug.Log("[72F]\n" + text);
    }

    public static string Build()
    {
        var sb = new StringBuilder(48000);
        sb.AppendLine("PHASE 72F — SURVIVOR CELL LIVE DUMP");
        sb.AppendLine("frame=" + Time.frameCount + " t=" + Time.time.ToString("F3"));

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace grid = presenter != null ? presenter.GridSpace : null;
        var focus = new HashSet<Vector2Int>
        {
            new Vector2Int(1, 7),
            new Vector2Int(1, 8),
            new Vector2Int(1, 0),
            new Vector2Int(1, 1)
        };

        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        sb.AppendLine("--- LOGICAL DIAMONDS ---");
        for (int bi = 0; bi < blocks.Length; bi++)
        {
            Block b = blocks[bi];
            if (b == null || b.IsSettled)
            {
                continue;
            }

            bool anyDia = false;
            for (int c = 0; c < b.CellCount; c++)
            {
                if (b.GetActiveIdentity(c).Shape == ShapeType.Diamond)
                {
                    anyDia = true;
                    break;
                }
            }

            if (!anyDia)
            {
                continue;
            }

            sb.AppendLine(
                "BLOCK id=" + b.GetInstanceID() +
                " grid=" + b.GridPosition +
                " pending=" + b.HasPendingLayerExtraction +
                " matchPres=" + b.IsMatchPresentationActive);
            for (int c = 0; c < b.CellCount; c++)
            {
                Vector2Int cell = b.GetCellWorld(c);
                focus.Add(cell);
                sb.AppendLine(
                    "  c" + c + " cell=" + cell +
                    " outer=" + b.GetActiveIdentity(c) +
                    " color=" + b.GetOuterColor(c) +
                    " inner=" + b.HasInnerLayerAt(c));
                PieceView3D view = b.GetWorldViewForCellIndex(c);
                if (view == null)
                {
                    sb.AppendLine("  view=null");
                    continue;
                }

                sb.AppendLine(
                    "  view=" + view.name +
                    " id=" + view.GetInstanceID() +
                    " hier=" + view.gameObject.activeInHierarchy +
                    " nested=" + view.HasNestedInner +
                    " cfgMat=" + (view.ConfiguredSolidMaterial != null
                        ? view.ConfiguredSolidMaterial.name
                        : "null") +
                    " lift=" + view.PresentationLift +
                    " scale=" + view.transform.localScale);
                Transform[] trs = view.GetComponentsInChildren<Transform>(true);
                for (int t = 0; t < trs.Length; t++)
                {
                    Transform tr = trs[t];
                    sb.AppendLine(
                        "    TR " + tr.name +
                        " id=" + tr.GetInstanceID() +
                        " self=" + tr.gameObject.activeSelf +
                        " hier=" + tr.gameObject.activeInHierarchy +
                        " lscale=" + Format(tr.localScale) +
                        " wscale=" + Format(tr.lossyScale) +
                        " pos=" + Format(tr.position));
                }

                MeshRenderer[] mrs = view.GetComponentsInChildren<MeshRenderer>(true);
                for (int m = 0; m < mrs.Length; m++)
                {
                    AppendRenderer(sb, mrs[m], grid, "VIEW");
                }
            }
        }

        sb.AppendLine("--- ALL RENDERERS AT FOCUS CELLS ---");
        Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int near = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || grid == null)
            {
                continue;
            }

            Vector2Int cell = grid.WorldToGrid(r.transform.position);
            if (!focus.Contains(cell))
            {
                continue;
            }

            if (r.gameObject.name.StartsWith("Cell_"))
            {
                continue;
            }

            near++;
            AppendRenderer(sb, r, grid, "FOCUS");
        }

        sb.AppendLine("focusRendererCount=" + near);

        sb.AppendLine("--- PiecesRoot diamond/residual/travel/nested ---");
        if (presenter != null && presenter.PiecesRoot != null)
        {
            Transform root = presenter.PiecesRoot;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform ch = root.GetChild(i);
                string n = ch.name;
                if (n.IndexOf("Diamond", System.StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Residual", System.StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Travel", System.StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Nested", System.StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Extract", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                sb.AppendLine(
                    "CHILD " + n +
                    " id=" + ch.GetInstanceID() +
                    " self=" + ch.gameObject.activeSelf +
                    " hier=" + ch.gameObject.activeInHierarchy +
                    " cell=" + (grid != null ? grid.WorldToGrid(ch.position).ToString() : "?") +
                    " pos=" + Format(ch.position) +
                    " lscale=" + Format(ch.localScale));
                MeshRenderer[] mrs = ch.GetComponentsInChildren<MeshRenderer>(true);
                for (int m = 0; m < mrs.Length; m++)
                {
                    AppendRenderer(sb, mrs[m], grid, "ROOTCHILD");
                }
            }
        }

        sb.AppendLine("--- GREENISH LIVE COLOR (any mat name) ---");
        int gh = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
            {
                continue;
            }

            Color c;
            string source;
            if (!TryGetLiveColor(r, out c, out source))
            {
                continue;
            }

            if (!(c.g > 0.35f && c.g > c.r + 0.08f && c.g > c.b + 0.05f))
            {
                continue;
            }

            gh++;
            AppendRenderer(sb, r, grid, "GREENISH");
            sb.AppendLine("  liveColor=" + c + " via=" + source);
        }

        sb.AppendLine("greenishHits=" + gh);
        return sb.ToString();
    }

    private static bool TryGetLiveColor(Renderer r, out Color c, out string source)
    {
        c = Color.black;
        source = "";
        if (r == null)
        {
            return false;
        }

        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        if (mpb != null && !mpb.isEmpty)
        {
            int idBase = Shader.PropertyToID("_BaseColor");
            int idCol = Shader.PropertyToID("_Color");
            if (mpb.HasProperty(idBase))
            {
                c = mpb.GetColor(idBase);
                source = "mpb._BaseColor";
                return true;
            }

            if (mpb.HasProperty(idCol))
            {
                c = mpb.GetColor(idCol);
                source = "mpb._Color";
                return true;
            }
        }

        Material mat = r.sharedMaterial;
        if (mat == null)
        {
            return false;
        }

        if (mat.HasProperty("_BaseColor"))
        {
            c = mat.GetColor("_BaseColor");
            source = "shared._BaseColor:" + mat.name;
            return true;
        }

        if (mat.HasProperty("_Color"))
        {
            c = mat.GetColor("_Color");
            source = "shared._Color:" + mat.name;
            return true;
        }

        return false;
    }

    private static void AppendRenderer(StringBuilder sb, Renderer r, IGridSpace grid, string tag)
    {
        if (r == null)
        {
            return;
        }

        GameObject go = r.gameObject;
        MeshFilter mf = r.GetComponent<MeshFilter>();
        string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-";
        string cell = grid != null ? grid.WorldToGrid(go.transform.position).ToString() : "?";
        sb.AppendLine(
            tag +
            " go=" + go.name +
            " goId=" + go.GetInstanceID() +
            " rid=" + r.GetInstanceID() +
            " type=" + r.GetType().Name +
            " en=" + r.enabled +
            " self=" + go.activeSelf +
            " hier=" + go.activeInHierarchy +
            " mesh=" + mesh +
            " cell=" + cell +
            " pos=" + Format(go.transform.position) +
            " lscale=" + Format(go.transform.localScale) +
            " wscale=" + Format(go.transform.lossyScale) +
            " path=" + BuildPath(go.transform));

        Material[] mats = r.sharedMaterials;
        if (mats != null)
        {
            for (int s = 0; s < mats.Length; s++)
            {
                Material mat = mats[s];
                string col = "-";
                if (mat != null)
                {
                    if (mat.HasProperty("_BaseColor"))
                    {
                        col = mat.GetColor("_BaseColor").ToString();
                    }
                    else if (mat.HasProperty("_Color"))
                    {
                        col = mat.GetColor("_Color").ToString();
                    }
                }

                sb.AppendLine(
                    "  shared[" + s + "]=" + (mat != null ? mat.name : "null") +
                    " shader=" + (mat != null && mat.shader != null ? mat.shader.name : "-") +
                    " color=" + col);
            }
        }

        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        if (mpb != null && !mpb.isEmpty)
        {
            sb.AppendLine("  mpb=YES");
            int idBase = Shader.PropertyToID("_BaseColor");
            int idCol = Shader.PropertyToID("_Color");
            int idEm = Shader.PropertyToID("_EmissionColor");
            if (mpb.HasProperty(idBase))
            {
                sb.AppendLine("  mpb._BaseColor=" + mpb.GetColor(idBase));
            }

            if (mpb.HasProperty(idCol))
            {
                sb.AppendLine("  mpb._Color=" + mpb.GetColor(idCol));
            }

            if (mpb.HasProperty(idEm))
            {
                sb.AppendLine("  mpb._EmissionColor=" + mpb.GetColor(idEm));
            }
        }
        else
        {
            sb.AppendLine("  mpb=none");
        }
    }

    private static string BuildPath(Transform t)
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
        return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")";
    }
}
