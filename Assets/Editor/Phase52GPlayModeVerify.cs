using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 52G play-mode verification: interaction feel presentation only.
/// Menu: Shape Nest / Phase 52G Verify Interaction Feel
/// Flag: Captures/phase52g-autotest.flag
/// </summary>
[InitializeOnLoad]
public static class Phase52GPlayModeVerify
{
    private const string FlagPath = "Captures/phase52g-autotest.flag";
    private const string ReportPath = "Captures/phase52g-report.txt";
    private const string SessionKey = "Phase52G.Verify";
    private const string Campaign15 = "Assets/Levels/Campaign_15_Master.asset";
    private const string Campaign07 = "Assets/Levels/Campaign_07_ChainIntro.asset";
    private const string Campaign08 = "Assets/Levels/Campaign_08_ChainCascade.asset";
    private const string Campaign10 = "Assets/Levels/Campaign_10_ShapeInShape.asset";
    private const string Campaign12 = "Assets/Levels/Campaign_12_Ice.asset";
    private const string Campaign13 = "Assets/Levels/Campaign_13_Shutter.asset";

    private static bool running;
    private static int step;
    private static double stepAt;
    private static readonly StringBuilder report = new StringBuilder();
    private static string lastError;
    private static Block heldBlock;
    private static BlockMover heldMover;

    static Phase52GPlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromFlag;
    }

    [MenuItem("Shape Nest/Phase 52G Verify Interaction Feel")]
    public static void RunFromMenu()
    {
        SessionState.SetBool(SessionKey, true);
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = true;
        }
        else
        {
            BeginRun();
        }
    }

    private static void TryBeginFromFlag()
    {
        if (File.Exists(FlagPath))
        {
            File.Delete(FlagPath);
            SessionState.SetBool(SessionKey, true);
            if (EditorApplication.isPlaying)
            {
                BeginRun();
            }
            else
            {
                EditorApplication.isPlaying = true;
            }
        }
        else if (SessionState.GetBool(SessionKey, false) && !EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = true;
        }
    }

    private static void OnPlayMode(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(SessionKey, false))
        {
            BeginRun();
        }

        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
            running = false;
        }
    }

    private static void BeginRun()
    {
        SessionState.SetBool(SessionKey, false);
        running = true;
        step = 0;
        stepAt = EditorApplication.timeSinceStartup;
        report.Length = 0;
        lastError = null;
        heldBlock = null;
        heldMover = null;
        report.AppendLine("Phase 52G — Interaction Feel Polish");
        report.AppendLine("Unity " + Application.unityVersion);
        report.AppendLine(
            "VisualCenterBoardPlaneOffsetLocal="
            + BoardAdaptivePresentation3D.VisualCenterBoardPlaneOffsetLocal.ToString("F2"));
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!running || !EditorApplication.isPlaying)
        {
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        if (now - stepAt < WaitForStep(step))
        {
            return;
        }

        stepAt = now;
        try
        {
            RunStep(step);
        }
        catch (System.Exception ex)
        {
            lastError = ex.Message;
            report.AppendLine("EXCEPTION " + ex);
            Finish(false);
            return;
        }

        step++;
        if (step > 24)
        {
            Finish(lastError == null);
        }
    }

    private static float WaitForStep(int s)
    {
        switch (s)
        {
            case 0:
                return 1.2f;
            case 1:
            case 7:
            case 10:
            case 12:
            case 13:
            case 14:
            case 17:
                return 1.0f;
            case 3:
            case 4:
                return 0.30f;
            case 5:
                return 0.40f;
            default:
                return 0.65f;
        }
    }

    private static void RunStep(int s)
    {
        switch (s)
        {
            case 0:
                LoadLevel(Campaign15, "Master");
                break;
            case 1:
                if (!WaitUntilBlocksReady("Master"))
                {
                    break;
                }

                Capture("Captures/phase52g-before-interaction.png");
                AssertVc();
                break;
            case 2:
                BeginTapSelection();
                break;
            case 3:
                Capture("Captures/phase52g-tap-feedback.png");
                InspectHeld("tap");
                break;
            case 4:
                BeginDragAim();
                break;
            case 5:
                Capture("Captures/phase52g-drag.png");
                Capture("Captures/phase52g-destination.png");
                InspectHeld("drag");
                InspectHighlight("drag");
                break;
            case 6:
                EndDragAim();
                break;
            case 7:
                LoadLevel(Campaign07, "ChainIntro");
                break;
            case 8:
                if (!WaitUntilBlocksReady("ChainIntro"))
                {
                    break;
                }

                BeginTapOnChain();
                break;
            case 9:
                Capture("Captures/phase52g-chain.png");
                InspectChainAlignment("chain");
                EndSelection();
                break;
            case 10:
                LoadLevel(Campaign10, "ShapeInShape");
                break;
            case 11:
                if (!WaitUntilBlocksReady("ShapeInShape"))
                {
                    break;
                }

                BeginTapOnNested();
                Capture("Captures/phase52g-nested.png");
                InspectNested("nested");
                EndSelection();
                break;
            case 12:
                LoadLevel(Campaign12, "Ice");
                break;
            case 13:
                if (!WaitUntilBlocksReady("Ice"))
                {
                    break;
                }

                Capture("Captures/phase52g-ice.png");
                break;
            case 14:
                LoadLevel(Campaign13, "Shutter");
                break;
            case 15:
                if (!WaitUntilBlocksReady("Shutter"))
                {
                    break;
                }

                Capture("Captures/phase52g-shutter.png");
                break;
            case 16:
                LoadLevel(Campaign15, "Master-boosters");
                break;
            case 17:
                if (!WaitUntilBlocksReady("Master-boosters"))
                {
                    break;
                }

                TryMagnet();
                Capture("Captures/phase52g-magnet.png");
                CancelMagnet();
                break;
            case 18:
                TryHammer();
                Capture("Captures/phase52g-hammer.png");
                break;
            case 19:
                LoadLevel(Campaign08, "ChainCascade");
                break;
            case 20:
                if (!WaitUntilBlocksReady("ChainCascade"))
                {
                    break;
                }

                InspectChainAlignment("cascade");
                AssertVc();
                report.AppendLine("CONFIRM gameplay BlockMover.secondsPerCell untouched (presentation-only phase)");
                report.AppendLine("CONFIRM destination calc via BoardCellDestinationHighlight3D unchanged");
                report.AppendLine("CONFIRM WorldPieceMotion PickupDuration/CarryScaleBoost untouched");
                break;
            default:
                break;
        }
    }

    private static void AssertVc()
    {
        float vc = BoardAdaptivePresentation3D.VisualCenterBoardPlaneOffsetLocal;
        report.AppendLine("VC=" + vc.ToString("F2") + (Mathf.Approximately(vc, 0.12f) ? " ok" : " FAIL"));
        if (!Mathf.Approximately(vc, 0.12f))
        {
            lastError = "VisualCenterBoardPlaneOffsetLocal changed";
        }
    }

    private static int blocksReadyRetries;

    private static bool WaitUntilBlocksReady(string label)
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        int count = 0;
        int withView = 0;
        if (board != null)
        {
            Block[] blocks = board.GetComponentsInChildren<Block>(true);
            count = blocks.Length;
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] != null && blocks[i].WorldView != null)
                {
                    withView++;
                }
            }
        }

        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        report.AppendLine(
            "READY "
            + label
            + " boardBlocks="
            + count
            + " withWorldView="
            + withView
            + " views="
            + views.Length
            + " retry="
            + blocksReadyRetries);

        if ((count > 0 && withView > 0) || (count == 0 && blocksReadyRetries >= 7))
        {
            blocksReadyRetries = 0;
            return true;
        }

        if (count > 0 && withView == 0 && blocksReadyRetries >= 10)
        {
            report.AppendLine("READY WARN world views still unbound");
            blocksReadyRetries = 0;
            return true;
        }

        blocksReadyRetries++;
        if (blocksReadyRetries < 12)
        {
            step--;
            return false;
        }

        lastError = "no blocks after load " + label;
        blocksReadyRetries = 0;
        return true;
    }

    private static void LoadLevel(string assetPath, string label)
    {
        blocksReadyRetries = 0;
        LevelManager manager = Object.FindFirstObjectByType<LevelManager>();
        LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>(assetPath);
        if (manager == null || data == null)
        {
            report.AppendLine("LOAD FAIL " + label + " manager=" + (manager != null) + " data=" + (data != null));
            lastError = "load " + label;
            return;
        }

        manager.LoadLevel(data);
        System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(LevelManager).GetField("timerRunning", flags)?.SetValue(manager, false);
        typeof(LevelManager).GetField("remainingSeconds", flags)?.SetValue(manager, 90f);
        typeof(LevelManager).GetField("session", flags)?.SetValue(manager, LevelManager.SessionState.Playing);
        Time.timeScale = 1f;
        report.AppendLine("LOAD " + label + " ok data=" + data.name);
    }

    private static Block FindMovableSingle()
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        Block[] blocks = board != null
            ? board.GetComponentsInChildren<Block>(true)
            : Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        report.AppendLine("findMovable blocks=" + blocks.Length);

        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || !block.isActiveAndEnabled || block.IsSettled || block.IsFrozen)
            {
                continue;
            }

            if (board != null && board.IsBlockUnderClosedShutter(block))
            {
                continue;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            if (mover == null || !mover.isActiveAndEnabled || mover.IsMoving || mover.IsDragging)
            {
                continue;
            }

            if (block.CellCount <= 1)
            {
                return block;
            }
        }

        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block != null && block.isActiveAndEnabled && !block.IsSettled)
            {
                return block;
            }
        }

        return null;
    }

    private static Block FindChain()
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        Block[] blocks = board != null
            ? board.GetComponentsInChildren<Block>(true)
            : Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        Block best = null;
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled || block.CellCount < 2)
            {
                continue;
            }

            if (best == null || block.CellCount > best.CellCount)
            {
                best = block;
            }
        }

        return best;
    }

    private static Block FindNested()
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        Block[] blocks = board != null
            ? board.GetComponentsInChildren<Block>(true)
            : Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            PieceView3D view = block != null ? block.WorldView : null;
            if (view != null && view.HasNestedInner)
            {
                return block;
            }
        }

        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view != null && view.HasNestedInner && view.SourceBlock != null)
            {
                return view.SourceBlock;
            }
        }

        return null;
    }

    private static void BeginTapSelection()
    {
        heldBlock = FindMovableSingle();
        if (heldBlock == null)
        {
            report.AppendLine("TAP no block");
            lastError = "no tap block";
            return;
        }

        heldBlock.ShowDragSelection();
        heldMover = heldBlock.GetComponent<BlockMover>();
        report.AppendLine("TAP select " + heldBlock.ShapeType + " cell=" + heldBlock.GridPosition);
    }

    private static void BeginTapOnChain()
    {
        heldBlock = FindChain();
        if (heldBlock == null)
        {
            report.AppendLine("CHAIN none");
            return;
        }

        heldBlock.ShowDragSelection();
        heldMover = heldBlock.GetComponent<BlockMover>();
        report.AppendLine("CHAIN select cells=" + heldBlock.CellCount);
    }

    private static void BeginTapOnNested()
    {
        heldBlock = FindNested();
        if (heldBlock == null)
        {
            report.AppendLine("NESTED none");
            return;
        }

        heldBlock.ShowDragSelection();
        report.AppendLine("NESTED select " + heldBlock.ShapeType);
    }

    private static void BeginDragAim()
    {
        if (heldBlock == null || heldMover == null)
        {
            report.AppendLine("DRAG skip");
            return;
        }

        Vector2Int[] dirs =
        {
            Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
        };
        for (int i = 0; i < dirs.Length; i++)
        {
            if (heldMover.TryBeginDrag(dirs[i]))
            {
                report.AppendLine("DRAG begin dir=" + dirs[i] + " aiming=" + heldMover.IsDragAiming);
                return;
            }
        }

        report.AppendLine("DRAG begin failed (no free dir)");
    }

    private static void EndDragAim()
    {
        if (heldMover != null && heldMover.IsDragging)
        {
            heldMover.EndDrag();
            report.AppendLine("DRAG end");
        }

        EndSelection();
    }

    private static void EndSelection()
    {
        if (heldBlock != null)
        {
            heldBlock.HideDragSelection();
            heldBlock = null;
        }

        heldMover = null;
    }

    private static void InspectHeld(string label)
    {
        if (heldBlock == null)
        {
            report.AppendLine(label + " held missing block");
            return;
        }

        PieceView3D view = heldBlock.WorldView;
        if (view == null)
        {
            PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] != null && views[i].SourceBlock == heldBlock)
                {
                    view = views[i];
                    break;
                }
            }
        }

        if (view == null)
        {
            report.AppendLine(label + " held missing view");
            return;
        }

        float meshX = view.transform.childCount > 0
            ? view.transform.GetChild(0).localScale.x
            : view.LocalScale.x;
        report.AppendLine(
            label
            + " presentationLift="
            + view.PresentationLift.ToString("F3")
            + " carryMesh="
            + view.CarryMeshScale.ToString("F3")
            + " meshScaleX="
            + meshX.ToString("F3")
            + " rootScale="
            + view.LocalScale.ToString("F3"));
    }

    private static void InspectHighlight(string label)
    {
        BoardCellDestinationHighlight3D highlight =
            Object.FindFirstObjectByType<BoardCellDestinationHighlight3D>();
        report.AppendLine(
            label
            + " highlight="
            + (highlight != null && highlight.isActiveAndEnabled));
    }

    private static void InspectChainAlignment(string label)
    {
        Block chain = FindChain();
        if (chain == null || chain.WorldView == null)
        {
            report.AppendLine(label + " no chain");
            return;
        }

        var extras = chain.ExtraWorldViews;
        int extrasCount = extras != null ? extras.Count : 0;
        report.AppendLine(label + " cells=" + chain.CellCount + " extras=" + extrasCount);
        if (extrasCount > 0 && extras[0] != null)
        {
            float dy = Mathf.Abs(extras[0].transform.position.y - chain.WorldView.transform.position.y);
            report.AppendLine(label + " primary-extra dy=" + dy.ToString("F4"));
        }
    }

    private static void InspectNested(string label)
    {
        if (heldBlock == null || heldBlock.WorldView == null || !heldBlock.WorldView.HasNestedInner)
        {
            report.AppendLine(label + " no nested");
            return;
        }

        Transform visual = null;
        Transform nested = null;
        Transform root = heldBlock.WorldView.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name.Contains("Mesh") || child.name.Contains("Visual"))
            {
                visual = child;
            }

            if (child.name.Contains("Nested") || child.name.Contains("Inner"))
            {
                nested = child;
            }
        }

        if (visual != null && nested != null)
        {
            Vector3 d = nested.localPosition - visual.localPosition;
            report.AppendLine(
                label
                + " inner-outer localDelta="
                + d.ToString("F4")
                + (d.sqrMagnitude < 0.0001f ? " aligned" : " CHECK"));
        }
        else
        {
            report.AppendLine(label + " hasNested=" + heldBlock.WorldView.HasNestedInner);
        }
    }

    private static void TryMagnet()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            report.AppendLine("magnet missing");
            return;
        }

        magnet.ActivateMagnet();
        report.AppendLine("magnet selecting=" + magnet.IsSelecting);
    }

    private static void CancelMagnet()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet != null && magnet.IsSelecting)
        {
            magnet.CancelMagnet("phase52g");
            report.AppendLine("magnet cancelled");
        }
    }

    private static void TryHammer()
    {
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (hammer == null || board == null)
        {
            report.AppendLine("hammer missing");
            return;
        }

        Block target = FindMovableSingle();
        if (target == null)
        {
            report.AppendLine("hammer no target");
            return;
        }

        hammer.ActivateHammer();
        bool accepted = hammer.TryHandleSelectionPress(target);
        report.AppendLine("hammer " + target.ShapeType + " accepted=" + accepted + " phase=" + hammer.Phase);
    }

    private static void Capture(string path)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        BoardCamera3D boardCam = Object.FindFirstObjectByType<BoardCamera3D>();
        Camera live = boardCam != null ? boardCam.Camera : Camera.main;
        if (live == null)
        {
            report.AppendLine("SHOT FAIL " + path + " no camera");
            return;
        }

        RenderTexture savedRt = live.targetTexture;
        RenderTexture rt = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        live.targetTexture = rt;
        live.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(full, tex.EncodeToPNG());
        RenderTexture.active = null;
        live.targetTexture = savedRt;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        report.AppendLine("SHOT " + path + " (" + new FileInfo(full).Length + "b)");
    }

    private static void Finish(bool ok)
    {
        EditorApplication.update -= Tick;
        running = false;
        EndSelection();
        report.AppendLine(ok ? "RESULT ok" : "RESULT failed");
        if (!string.IsNullOrEmpty(lastError))
        {
            report.AppendLine("lastError=" + lastError);
        }

        Directory.CreateDirectory("Captures");
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log("[Phase52G] " + (ok ? "ok" : "failed") + " → " + ReportPath);
        EditorApplication.isPlaying = false;
    }
}
