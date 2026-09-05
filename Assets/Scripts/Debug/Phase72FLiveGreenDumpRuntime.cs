using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Runtime Phase 72F dump — attach or call DumpStatic while ghost is visible.
/// Avoids Editor-assembly timing issues during Play Mode.
/// </summary>
public static class Phase72FLiveGreenDumpRuntime
{
    private const string ReportPath = "Captures/phase72d-green-renderer.txt";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        // Hotkey: F8 dumps while playing.
    }

    public static string DumpStatic()
    {
        var sb = new StringBuilder(48000);
        sb.AppendLine("PHASE 72F RUNTIME — SURVIVOR CELL LIVE DUMP");
        sb.AppendLine("frame=" + Time.frameCount + " t=" + Time.time.ToString("F3"));

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace grid = presenter != null ? presenter.GridSpace : null;
        var focus = new HashSet<Vector2Int>();

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
                    " scale=" + Fmt(view.transform.localScale));

                Transform[] trs = view.GetComponentsInChildren<Transform>(true);
                for (int t = 0; t < trs.Length; t++)
                {
                    Transform tr = trs[t];
                    sb.AppendLine(
                        "    TR " + tr.name +
                        " id=" + tr.GetInstanceID() +
                        " self=" + tr.gameObject.activeSelf +
                        " hier=" + tr.gameObject.activeInHierarchy +
                        " lscale=" + Fmt(tr.localScale) +
                        " wscale=" + Fmt(tr.lossyScale) +
                        " pos=" + Fmt(tr.position));
                }

                MeshRenderer[] mrs = view.GetComponentsInChildren<MeshRenderer>(true);
                for (int m = 0; m < mrs.Length; m++)
                {
                    Append(sb, mrs[m], grid, "VIEW");
                }
            }
        }

        if (focus.Count == 0)
        {
            focus.Add(new Vector2Int(1, 7));
            focus.Add(new Vector2Int(1, 8));
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
            Append(sb, r, grid, "FOCUS");
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
                    " pos=" + Fmt(ch.position) +
                    " lscale=" + Fmt(ch.localScale));
                MeshRenderer[] mrs = ch.GetComponentsInChildren<MeshRenderer>(true);
                for (int m = 0; m < mrs.Length; m++)
                {
                    Append(sb, mrs[m], grid, "ROOTCHILD");
                }
            }
        }

        sb.AppendLine("--- GREENISH LIVE COLOR ---");
        int gh = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
            {
                continue;
            }

            Color c;
            string via;
            if (!TryColor(r, out c, out via))
            {
                continue;
            }

            if (!(c.g > 0.35f && c.g > c.r + 0.08f && c.g > c.b + 0.05f))
            {
                continue;
            }

            gh++;
            Append(sb, r, grid, "GREENISH");
            sb.AppendLine("  liveColor=" + c + " via=" + via);
        }

        sb.AppendLine("greenishHits=" + gh);

        string dir = Path.GetDirectoryName(ReportPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(ReportPath, sb.ToString());
        Debug.Log("[72F] wrote " + ReportPath + " chars=" + sb.Length);
        return sb.ToString();
    }

    private static bool TryColor(Renderer r, out Color c, out string via)
    {
        c = Color.black;
        via = "";
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        if (mpb != null && !mpb.isEmpty)
        {
            int idBase = Shader.PropertyToID("_BaseColor");
            int idCol = Shader.PropertyToID("_Color");
            if (mpb.HasProperty(idBase))
            {
                c = mpb.GetColor(idBase);
                via = "mpb._BaseColor";
                return true;
            }

            if (mpb.HasProperty(idCol))
            {
                c = mpb.GetColor(idCol);
                via = "mpb._Color";
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
            via = "shared._BaseColor:" + mat.name;
            return true;
        }

        if (mat.HasProperty("_Color"))
        {
            c = mat.GetColor("_Color");
            via = "shared._Color:" + mat.name;
            return true;
        }

        return false;
    }

    private static void Append(StringBuilder sb, Renderer r, IGridSpace grid, string tag)
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
            " en=" + r.enabled +
            " self=" + go.activeSelf +
            " hier=" + go.activeInHierarchy +
            " mesh=" + mesh +
            " cell=" + cell +
            " pos=" + Fmt(go.transform.position) +
            " lscale=" + Fmt(go.transform.localScale) +
            " wscale=" + Fmt(go.transform.lossyScale) +
            " path=" + PathOf(go.transform));

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
                    " color=" + col);
            }
        }

        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        if (mpb != null && !mpb.isEmpty)
        {
            sb.AppendLine("  mpb=YES");
            int idBase = Shader.PropertyToID("_BaseColor");
            if (mpb.HasProperty(idBase))
            {
                sb.AppendLine("  mpb._BaseColor=" + mpb.GetColor(idBase));
            }
        }
        else
        {
            sb.AppendLine("  mpb=none");
        }
    }

    private static string PathOf(Transform t)
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

    private static string Fmt(Vector3 v)
    {
        return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")";
    }
}
