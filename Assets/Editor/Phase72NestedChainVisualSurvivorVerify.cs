using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 72 — nested-chain visual survivor sync (presentation only).
/// Menu: Shape Nest / Phase 72 Verify Nested Chain Visual Survivor
/// </summary>
public static class Phase72NestedChainVisualSurvivorVerify
{
    private const string ReportPath = "Captures/phase72-visual-survivor-report.txt";
    private const string SessionKey = "Phase72VisualSurvivorRunning";

    [MenuItem("Shape Nest/Phase 72 Verify Nested Chain Visual Survivor")]
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

        var host = new GameObject("Phase72_VisualSurvivorProbe");
        Object.DontDestroyOnLoad(host);
        host.AddComponent<Phase72VisualSurvivorProbe>().Begin();
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

/// <summary>Play Mode probe for consumed-outer visual ghosts on nested chains.</summary>
public sealed class Phase72VisualSurvivorProbe : MonoBehaviour
{
    public void Begin()
    {
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("PHASE 72 — NESTED CHAIN VISUAL SURVIVOR");
        sb.AppendLine("======================================");

        LevelManager lm = Object.FindFirstObjectByType<LevelManager>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (lm == null || board == null)
        {
            Finish(sb, false, "missing LevelManager/BoardManager");
            yield break;
        }

        lm.LoadLevel(42);
        yield return null;
        yield return null;

        // Prefer a multi-cell nested chain (matches screenshot: 2 linked nested diamonds).
        Block chain = FindMultiCellNested();
        if (chain == null)
        {
            Finish(sb, false, "no multi-cell nested block found on Level 43");
            yield break;
        }

        sb.AppendLine(
            $"subject={chain.GetInstanceID()} cells={chain.CellCount} grid={chain.GridPosition}");

        // Presentation-only peel simulation for every nested cell (no TryMoveBlock / matching).
        var peeled = new System.Collections.Generic.List<int>(chain.CellCount);
        for (int i = 0; i < chain.CellCount; i++)
        {
            if (!chain.HasInnerLayerAt(i))
            {
                continue;
            }

            MatchIdentity offered = chain.GetActiveIdentity(i);
            ShapeCellData cell = chain.GetCell(i);
            BoardPresentationController.DetachAndAnchorNestedInner(chain, i);
            if (cell == null || !ShapeLayout.TryConsumeLayer(cell, offered, out bool remains) || !remains)
            {
                Finish(sb, false, $"logical consume failed cell={i} offered={offered}");
                yield break;
            }

            peeled.Add(i);
        }

        chain.RefreshActiveLayers(syncWorldPresentation: false);
        for (int i = 0; i < peeled.Count; i++)
        {
            chain.BeginPendingLayerExtraction(peeled[i]);
        }

        BoardPresentationController.HoldPendingExtractionViewsAtSource(chain);
        yield return null;

        // Promote every pending cell (same path PlayAllPending ends on).
        for (int i = 0; i < chain.CellCount; i++)
        {
            if (!chain.IsPendingLayerExtraction(i))
            {
                continue;
            }

            BoardPresentationController.NotifyNestedLayerPromoted(chain, i);
            chain.ClearPendingLayerExtraction(i);
            chain.SetCellVisualVisible(i, true);
        }

        BoardPresentationController.ReconcileNestedSurvivorVisuals(chain);
        yield return null;
        yield return null;

        bool pendingGone = !chain.HasPendingLayerExtraction;
        bool noResiduals = CountNestedResiduals() == 0;
        bool shapesSynced = true;
        bool noNestedOverlay = true;
        for (int i = 0; i < chain.CellCount; i++)
        {
            PieceView3D view = chain.GetWorldViewForCellIndex(i);
            ShapeType logical = chain.GetOuterShape(i);
            if (view == null)
            {
                shapesSynced = false;
                sb.AppendLine($"  cell {i}: view=null");
                continue;
            }

            bool shapeOk = view.ConfiguredShape == logical;
            Transform nestedChild = view.transform.Find("NestedInner3D");
            bool nestedOk = !view.HasNestedInner
                && (nestedChild == null || !nestedChild.gameObject.activeSelf);
            shapesSynced &= shapeOk;
            noNestedOverlay &= nestedOk;
            sb.AppendLine(
                $"  cell {i}: logical={logical} visual={view.ConfiguredShape} " +
                $"shapeOk={shapeOk} nestedOverlayGone={nestedOk} active={view.gameObject.activeSelf}");
        }

        bool pass = pendingGone && noResiduals && shapesSynced && noNestedOverlay;
        sb.AppendLine();
        sb.AppendLine($"pendingGone={pendingGone}");
        sb.AppendLine($"residualsDestroyed={noResiduals} residualCount={CountNestedResiduals()}");
        sb.AppendLine($"shapesSynced={shapesSynced}");
        sb.AppendLine($"noNestedOverlay={noNestedOverlay}");
        sb.AppendLine();
        sb.AppendLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
        sb.AppendLine(
            "Acceptance: consumed outer mesh gone; survivor outer matches logical; no residual ghost.");

        Finish(sb, pass, pass ? "PASS" : "FAIL");
    }

    private static Block FindMultiCellNested()
    {
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        Block fallback = null;
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled || block.CellCount < 2)
            {
                continue;
            }

            int nestedCells = 0;
            for (int c = 0; c < block.CellCount; c++)
            {
                if (block.HasInnerLayerAt(c))
                {
                    nestedCells++;
                }
            }

            if (nestedCells >= 2)
            {
                return block;
            }

            if (nestedCells >= 1 && fallback == null)
            {
                fallback = block;
            }
        }

        return fallback;
    }

    private static int CountNestedResiduals()
    {
        int count = 0;
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.name == null)
            {
                continue;
            }

            if (t.name.StartsWith("NestedInnerResidual_", System.StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private void Finish(StringBuilder sb, bool pass, string summary)
    {
        sb.AppendLine($"summary={summary}");
        string text = sb.ToString();
        Phase72NestedChainVisualSurvivorVerify.WriteReport(text);
        if (pass)
        {
            Debug.Log(text);
        }
        else
        {
            Debug.LogError(text);
        }

        EditorApplication.delayCall += () =>
        {
            EditorApplication.isPlaying = false;
            if (this != null)
            {
                Destroy(gameObject);
            }
        };
    }
}
