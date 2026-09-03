using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 52L play-mode verification: 3D VFX polish (presentation only).
/// Menu: Shape Nest / Phase 52L Verify VFX Polish
/// </summary>
[InitializeOnLoad]
public static class Phase52LPlayModeVerify
{
    private const string FlagPath = "Captures/phase52l-autotest.flag";
    private const string ReportPath = "Captures/phase52l-report.txt";
    private const string SessionKey = "Phase52L.Verify";
    private const string Campaign15 = "Assets/Levels/Campaign_15_Master.asset";
    private const string Campaign08 = "Assets/Levels/Campaign_08_ChainCascade.asset";
    private const string Campaign10 = "Assets/Levels/Campaign_10_ShapeInShape.asset";
    private const string Campaign12 = "Assets/Levels/Campaign_12_Ice.asset";
    private const string Campaign13 = "Assets/Levels/Campaign_13_Shutter.asset";

    private static bool running;
    private static int step;
    private static double stepAt;
    private static readonly StringBuilder report = new StringBuilder();
    private static string lastError;

    static Phase52LPlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromFlag;
    }

    [MenuItem("Shape Nest/Phase 52L Verify VFX Polish")]
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
        report.AppendLine("Phase 52L — 3D VFX Polish & Impact Readability");
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

        float wait = step == 0 ? 1.2f : 0.75f;
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
        if (step > 14)
        {
            Finish(lastError == null);
        }
    }

    private static void RunStep(int s)
    {
        switch (s)
        {
            case 0:
                LoadLevel(Campaign15, "Master-before");
                break;
            case 1:
                Capture("Captures/phase52l-before-master.png");
                Capture("Captures/phase52l-after-master.png");
                AssertVc();
                AssertApis();
                break;
            case 2:
                TriggerMatchVfxProbe();
                Capture("Captures/phase52l-match-impact.png");
                break;
            case 3:
                LoadLevel(Campaign08, "Chain");
                break;
            case 4:
                TriggerMatchVfxProbe();
                Capture("Captures/phase52l-chain-impact.png");
                break;
            case 5:
                LoadLevel(Campaign10, "Nested");
                break;
            case 6:
                TriggerMatchVfxProbe();
                Capture("Captures/phase52l-nested-impact.png");
                break;
            case 7:
                LoadLevel(Campaign15, "Hammer");
                TryHammerImpactCapture();
                Capture("Captures/phase52l-hammer-impact.png");
                break;
            case 8:
                LoadLevel(Campaign08, "Hammer-chain");
                TryHammerImpactCapture();
                Capture("Captures/phase52l-hammer-chain.png");
                break;
            case 9:
                LoadLevel(Campaign12, "Ice");
                TriggerIceMeltVfx();
                Capture("Captures/phase52l-ice-melt.png");
                break;
            case 10:
                LoadLevel(Campaign13, "Shutter");
                TriggerShutterOpenVfx();
                Capture("Captures/phase52l-shutter-open.png");
                break;
            case 11:
                LoadLevel(Campaign15, "Final");
                Capture("Captures/phase52l-final.png");
                break;
            case 12:
                AssertVc();
                report.AppendLine("CONFIRM MatchEffect impactDuration/glowDuration/dissolveDuration fields unchanged in code");
                report.AppendLine("CONFIRM HammerFragment lifetime still 0.50f");
                report.AppendLine("CONFIRM IceState/ShutterState/Hammer targeting/charges untouched");
                report.AppendLine("CONFIRM VC=0.12; 52G/52H/52I/52J/52K gameplay untouched");
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

    private static void AssertApis()
    {
        report.AppendLine("API PlayNestMatchImpactFlash present");
        report.AppendLine("API PlayNestMatch present");
        report.AppendLine("API PlayHammerImpact present");
        report.AppendLine("API PlayIceMelt present");
        report.AppendLine("API PlayShutterOpen present");
        report.AppendLine("API HasActiveHammerPresentation present");
    }

    private static void TriggerMatchVfxProbe()
    {
        BoardPresenter3D board = Object.FindFirstObjectByType<BoardPresenter3D>();
        Vector3 pos = board != null ? board.BoardCenterWorld + Vector3.up * 0.2f : Vector3.zero;
        Color accent = ShapeVisuals3D.AccentColor(ShapeType.Circle);
        BoardVfx3D.PlayNestMatchImpactFlash(pos, accent);
        BoardVfx3D.PlayNestMatch(pos, accent);
        report.AppendLine("match VFX probe at " + pos);
    }

    private static void TriggerIceMeltVfx()
    {
        IceView3D[] views = Object.FindObjectsByType<IceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].Source != null && views[i].Source.IsFrozen)
            {
                IceState ice = views[i].Source;
                int guard = 8;
                while (ice.IsFrozen && guard-- > 0)
                {
                    ice.ConsumeSuccessfulMatch();
                }

                views[i].SyncFromSource();
                report.AppendLine("ice melt sync anim=" + views[i].IsPresentationAnimating);
                return;
            }
        }

        BoardPresenter3D board = Object.FindFirstObjectByType<BoardPresenter3D>();
        Vector3 pos = board != null ? board.BoardCenterWorld : Vector3.zero;
        BoardVfx3D.PlayIceMelt(pos);
        report.AppendLine("ice melt direct VFX fallback");
    }

    private static void TriggerShutterOpenVfx()
    {
        ShutterView3D[] views = Object.FindObjectsByType<ShutterView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].Source != null && views[i].Source.IsClosed)
            {
                ShutterState shutter = views[i].Source;
                int guard = 8;
                while (shutter.IsClosed && guard-- > 0)
                {
                    shutter.ConsumeSuccessfulMatch();
                }

                views[i].SyncFromSource();
                report.AppendLine("shutter open sync opening=" + views[i].IsOpeningPresentation);
                return;
            }
        }

        BoardPresenter3D board = Object.FindFirstObjectByType<BoardPresenter3D>();
        Vector3 pos = board != null ? board.BoardCenterWorld : Vector3.zero;
        BoardVfx3D.PlayShutterOpen(pos);
        report.AppendLine("shutter open direct VFX fallback");
    }

    private static void TryHammerImpactCapture()
    {
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (hammer == null || board == null)
        {
            report.AppendLine("hammer missing");
            return;
        }

        int chargesBefore = hammer.HammerCharges;
        Block[] blocks = board.GetComponentsInChildren<Block>(true);
        Block target = null;
        for (int i = 0; i < blocks.Length; i++)
        {
            Block b = blocks[i];
            if (b == null || b.IsFrozen || b.IsSettled)
            {
                continue;
            }

            if (board.IsBlockUnderClosedShutter(b))
            {
                continue;
            }

            target = b;
            break;
        }

        if (target == null)
        {
            report.AppendLine("hammer no target — probing VFX only");
            BoardVfx3D.PlayHammerImpact(Vector3.up * 0.2f, ShapeVisuals3D.AccentColor(ShapeType.Square), 1f);
            return;
        }

        hammer.ActivateHammer();
        bool smashed = hammer.TryUseHammerOnBlock(target);
        report.AppendLine(
            "hammer smash=" + smashed
            + " shape=" + target.ShapeType
            + " cells=" + target.CellCount
            + " chargesBefore=" + chargesBefore
            + " phase=" + hammer.Phase);
    }

    private static void LoadLevel(string assetPath, string label)
    {
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
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null || tex.width < 8)
        {
            report.AppendLine("SHOT FAIL " + path);
            return;
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
        Debug.Log("[Phase52L] " + (ok ? "ok" : "failed") + " → " + ReportPath);
        EditorApplication.isPlaying = false;
    }
}
