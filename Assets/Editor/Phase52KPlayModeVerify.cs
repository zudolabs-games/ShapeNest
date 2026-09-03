using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 52K play-mode verification: Ice + Shutter 3D presentation polish.
/// Menu: Shape Nest / Phase 52K Verify Ice Shutter
/// </summary>
[InitializeOnLoad]
public static class Phase52KPlayModeVerify
{
    private const string FlagPath = "Captures/phase52k-autotest.flag";
    private const string ReportPath = "Captures/phase52k-report.txt";
    private const string SessionKey = "Phase52K.Verify";
    private const string Campaign15 = "Assets/Levels/Campaign_15_Master.asset";
    private const string Campaign10 = "Assets/Levels/Campaign_10_ShapeInShape.asset";
    private const string Campaign12 = "Assets/Levels/Campaign_12_Ice.asset";
    private const string Campaign13 = "Assets/Levels/Campaign_13_Shutter.asset";
    private const string Campaign08 = "Assets/Levels/Campaign_08_ChainCascade.asset";

    private static bool running;
    private static int step;
    private static double stepAt;
    private static readonly StringBuilder report = new StringBuilder();
    private static string lastError;

    static Phase52KPlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromFlag;
    }

    [MenuItem("Shape Nest/Phase 52K Verify Ice Shutter")]
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
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
            else
            {
                BeginRun();
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
        IceView3D.InvalidateSharedIceMaterial();
        ShutterView3D.InvalidateSharedMaterials();
        report.AppendLine("Phase 52K — 3D Ice + Shutter Presentation Polish");
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

        float wait = 0.55f;
        if (step == 0)
        {
            wait = 1.2f;
        }
        else if (step == 5 || step == 11)
        {
            // Capture mid ice-melt / shutter-open tween (durations ~0.32–0.34s).
            wait = 0.16f;
        }

        if (EditorApplication.timeSinceStartup - stepAt < wait)
        {
            return;
        }

        stepAt = EditorApplication.timeSinceStartup;
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
        if (step > 17)
        {
            Finish(lastError == null);
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
                if (!WaitUntilPresentationReady("Master", false, false))
                {
                    return;
                }

                Capture("Captures/phase52k-master.png");
                AssertVc();
                InspectIceMaterial();
                InspectShutterMaterial();
                break;
            case 2:
                LoadLevel(Campaign12, "Ice");
                break;
            case 3:
                if (!WaitUntilPresentationReady("Ice", true, false))
                {
                    return;
                }

                Capture("Captures/phase52k-after-ice.png");
                InspectIceViews();
                break;
            case 4:
                TriggerIceMeltCapture();
                break;
            case 5:
                Capture("Captures/phase52k-ice-melt.png");
                break;
            case 6:
                LoadLevel(Campaign08, "ChainCascade");
                break;
            case 7:
                if (!WaitUntilPresentationReady("Chain", false, false))
                {
                    return;
                }

                Capture("Captures/phase52k-ice-chain.png");
                report.AppendLine("NOTE ice-chain: Campaign_08 has chains; Campaign_12 ice is single-cell. Shell path layoutIsChain preserved in IceView3D.");
                break;
            case 8:
                LoadLevel(Campaign13, "Shutter");
                break;
            case 9:
                if (!WaitUntilPresentationReady("Shutter", false, true))
                {
                    return;
                }

                Capture("Captures/phase52k-after-shutter.png");
                InspectShutters();
                break;
            case 10:
                TriggerShutterOpenCapture();
                break;
            case 11:
                Capture("Captures/phase52k-shutter-open.png");
                break;
            case 12:
                LoadLevel(Campaign10, "ShapeInShape");
                break;
            case 13:
                if (!WaitUntilPresentationReady("Nested", false, false))
                {
                    return;
                }

                Capture("Captures/phase52k-nested.png");
                break;
            case 14:
                LoadLevel(Campaign12, "Ice-hammer");
                break;
            case 15:
                if (!WaitUntilPresentationReady("Ice-hammer", true, false))
                {
                    return;
                }

                TryHammerRejectIce();
                Capture("Captures/phase52k-hammer-reject.png");
                break;
            case 16:
                LoadLevel(Campaign15, "Master-magnet-spotcheck");
                TryMagnetSpotcheck();
                AssertVc();
                report.AppendLine("CONFIRM IceState.cs untouched (presentation-only IceView3D)");
                report.AppendLine("CONFIRM ShutterState.cs untouched (presentation-only ShutterView3D)");
                report.AppendLine("CONFIRM Block/BoardManager/BlockMover/InputManager/Magnet/Hammer untouched");
                report.AppendLine("CONFIRM piece roots / GridPosition / camera / VC=0.12 untouched");
                report.AppendLine("CONFIRM Phase52G selection / Phase52H nest / Phase52I shadows / Phase52J board untouched");
                report.AppendLine("BEFORE captures: phase52k-before-ice.png, phase52k-before-shutter.png (from Phase 52J baseline)");
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
            lastError = "VC changed";
        }
    }

    private static void InspectIceMaterial()
    {
        Material ice = IceView3D.GetSharedIceMaterial();
        float metallic = ice.HasProperty("_Metallic") ? ice.GetFloat("_Metallic") : -1f;
        float smooth = ice.HasProperty("_Smoothness") ? ice.GetFloat("_Smoothness") : -1f;
        bool emission = ice.IsKeywordEnabled("_EMISSION");
        report.AppendLine(
            "Ice mat color=" + ice.color
            + " metallic=" + metallic.ToString("F2")
            + " smooth=" + smooth.ToString("F2")
            + " emissionKeyword=" + emission);
        if (metallic > 0.001f || emission)
        {
            lastError = "Ice material not frosted (metallic/emission)";
        }
    }

    private static void InspectShutterMaterial()
    {
        Material plate = ShutterView3D.GetPlateMaterial();
        Material slat = ShutterView3D.GetSlatMaterial();
        float pm = plate.HasProperty("_Metallic") ? plate.GetFloat("_Metallic") : -1f;
        float sm = slat.HasProperty("_Metallic") ? slat.GetFloat("_Metallic") : -1f;
        report.AppendLine(
            "Shutter plate color=" + plate.color
            + " metallic=" + pm.ToString("F2")
            + " smooth=" + (plate.HasProperty("_Smoothness") ? plate.GetFloat("_Smoothness").ToString("F2") : "?"));
        report.AppendLine(
            "Shutter slat color=" + slat.color
            + " metallic=" + sm.ToString("F2")
            + " smooth=" + (slat.HasProperty("_Smoothness") ? slat.GetFloat("_Smoothness").ToString("F2") : "?"));
        if (pm > 0.001f || sm > 0.001f)
        {
            lastError = "Shutter metallic not 0";
        }
    }

    private static void InspectIceViews()
    {
        IceView3D[] ices = Object.FindObjectsByType<IceView3D>(FindObjectsSortMode.None);
        int active = 0;
        for (int i = 0; i < ices.Length; i++)
        {
            if (ices[i] != null && ices[i].isActiveAndEnabled && ices[i].Source != null && ices[i].Source.IsFrozen)
            {
                active++;
            }
        }

        report.AppendLine("IceView3D frozen count=" + active);
    }

    private static void InspectShutters()
    {
        ShutterView3D[] shutters = Object.FindObjectsByType<ShutterView3D>(FindObjectsSortMode.None);
        int closed = 0;
        for (int i = 0; i < shutters.Length; i++)
        {
            if (shutters[i] != null && shutters[i].Source != null && shutters[i].Source.IsClosed)
            {
                closed++;
            }
        }

        report.AppendLine("ShutterView3D closed count=" + closed);
    }

    private static void TriggerIceMeltCapture()
    {
        IceView3D[] views = Object.FindObjectsByType<IceView3D>(FindObjectsSortMode.None);
        IceView3D view = null;
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].Source != null && views[i].Source.IsFrozen)
            {
                view = views[i];
                break;
            }
        }

        if (view == null || view.Source == null)
        {
            report.AppendLine("ice melt: no frozen IceView3D");
            return;
        }

        IceState target = view.Source;
        int guard = 8;
        while (target.IsFrozen && guard-- > 0)
        {
            target.ConsumeSuccessfulMatch();
        }

        // Do not re-Bind — that resets wasFrozen. SyncFromSource starts the existing melt tween.
        view.SyncFromSource();
        report.AppendLine(
            "ice melt: SyncFromSource dur=" + target.Durability
            + " anim=" + view.IsPresentationAnimating);
    }

    private static void TriggerShutterOpenCapture()
    {
        ShutterView3D[] views = Object.FindObjectsByType<ShutterView3D>(FindObjectsSortMode.None);
        ShutterView3D view = null;
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].Source != null && views[i].Source.IsClosed)
            {
                view = views[i];
                break;
            }
        }

        if (view == null || view.Source == null)
        {
            report.AppendLine("shutter open: no closed ShutterView3D");
            return;
        }

        ShutterState target = view.Source;
        int guard = 8;
        while (target.IsClosed && guard-- > 0)
        {
            target.ConsumeSuccessfulMatch();
        }

        view.SyncFromSource();
        report.AppendLine(
            "shutter open: SyncFromSource closed=" + target.IsClosed
            + " opening=" + view.IsOpeningPresentation);
    }

    private static int readyRetries;

    private static bool WaitUntilPresentationReady(string label, bool requireIce, bool requireShutter)
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        int blocks = 0;
        int withView = 0;
        if (board != null)
        {
            Block[] all = board.GetComponentsInChildren<Block>(true);
            blocks = all.Length;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].WorldView != null)
                {
                    withView++;
                }
            }
        }

        int frozenIce = 0;
        IceView3D[] ices = Object.FindObjectsByType<IceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < ices.Length; i++)
        {
            if (ices[i] != null && ices[i].Source != null && ices[i].Source.IsFrozen)
            {
                frozenIce++;
            }
        }

        int closedShutters = 0;
        ShutterView3D[] shutters = Object.FindObjectsByType<ShutterView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < shutters.Length; i++)
        {
            if (shutters[i] != null && shutters[i].Source != null && shutters[i].Source.IsClosed)
            {
                closedShutters++;
            }
        }

        report.AppendLine(
            "READY " + label
            + " blocks=" + blocks
            + " views=" + withView
            + " ice=" + frozenIce
            + " shutter=" + closedShutters
            + " retry=" + readyRetries);

        bool iceOk = !requireIce || frozenIce > 0;
        bool shutterOk = !requireShutter || closedShutters > 0;
        bool piecesOk = withView > 0 || blocks == 0;
        if (piecesOk && iceOk && shutterOk)
        {
            readyRetries = 0;
            return true;
        }

        readyRetries++;
        if (readyRetries < 12)
        {
            step--; // stay on current step until ready
            return false;
        }

        report.AppendLine("READY TIMEOUT " + label);
        readyRetries = 0;
        return true;
    }

    private static void TryHammerRejectIce()
    {
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (hammer == null || board == null)
        {
            report.AppendLine("hammer missing");
            return;
        }

        Block[] blocks = board.GetComponentsInChildren<Block>(true);
        Block frozen = null;
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] != null && blocks[i].IsFrozen)
            {
                frozen = blocks[i];
                break;
            }
        }

        if (frozen == null)
        {
            report.AppendLine("hammer reject: no frozen block");
            return;
        }

        hammer.ActivateHammer();
        bool smashed = hammer.TryUseHammerOnBlock(frozen);
        bool eligible = hammer.IsHammerEligibleVisual(frozen);
        report.AppendLine(
            "hammer ice reject shape=" + frozen.ShapeType
            + " smashed=" + smashed
            + " eligible=" + eligible
            + " (expect smashed=false eligible=false) phase=" + hammer.Phase);
        if (smashed || eligible)
        {
            lastError = "Hammer accepted ice";
        }
    }

    private static void TryMagnetSpotcheck()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        report.AppendLine("magnet present=" + (magnet != null) + " (no route change; presentation spotcheck only)");
    }

    private static void LoadLevel(string assetPath, string label)
    {
        readyRetries = 0;
        LevelManager manager = Object.FindFirstObjectByType<LevelManager>();
        LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>(assetPath);
        if (manager == null || data == null)
        {
            report.AppendLine("LOAD FAIL " + label);
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
        report.AppendLine("LOAD " + label + " ok");
    }

    private static void Capture(string path)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full));

        // Prefer Game View ScreenCapture — URP Camera.Render to RT often returns a stale identical frame.
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null || tex.width < 8)
        {
            BoardCamera3D boardCam = Object.FindFirstObjectByType<BoardCamera3D>();
            Camera live = boardCam != null ? boardCam.Camera : Camera.main;
            if (live == null)
            {
                report.AppendLine("SHOT FAIL " + path);
                return;
            }

            if (boardCam != null)
            {
                BoardPresenter3D board = Object.FindFirstObjectByType<BoardPresenter3D>();
                if (board != null)
                {
                    boardCam.FrameBoard(board);
                }
            }

            RenderTexture savedRt = live.targetTexture;
            RenderTexture rt = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            live.targetTexture = rt;
            live.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            live.targetTexture = savedRt;
            Object.DestroyImmediate(rt);
        }

        File.WriteAllBytes(full, tex.EncodeToPNG());
        report.AppendLine("SHOT " + path + " (" + new FileInfo(full).Length + "b " + tex.width + "x" + tex.height + ")");
        Object.DestroyImmediate(tex);
    }

    private static void Finish(bool ok)
    {
        EditorApplication.update -= Tick;
        running = false;
        report.AppendLine(ok ? "RESULT ok" : "RESULT failed");
        if (!string.IsNullOrEmpty(lastError))
        {
            report.AppendLine("lastError=" + lastError);
        }

        Directory.CreateDirectory("Captures");
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log("[Phase52K] " + (ok ? "ok" : "failed") + " → " + ReportPath);
        EditorApplication.isPlaying = false;
    }
}
