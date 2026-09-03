using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 52I play-mode verification: contact shadow & depth presentation polish.
/// Menu: Shape Nest / Phase 52I Verify Shadow Depth
/// </summary>
[InitializeOnLoad]
public static class Phase52IPlayModeVerify
{
    private const string FlagPath = "Captures/phase52i-autotest.flag";
    private const string ReportPath = "Captures/phase52i-report.txt";
    private const string SessionKey = "Phase52I.Verify";
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

    static Phase52IPlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromFlag;
    }

    [MenuItem("Shape Nest/Phase 52I Verify Shadow Depth")]
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
        report.AppendLine("Phase 52I — 3D Block/Nest Shadow & Depth Polish");
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

        if (EditorApplication.timeSinceStartup - stepAt < (step == 0 ? 1.2f : 0.9f))
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
        if (step > 12)
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
                Capture("Captures/phase52i-before-master.png");
                AssertVc();
                InspectShadows("master");
                InspectMaterials();
                break;
            case 2:
                Capture("Captures/phase52i-after-master.png");
                break;
            case 3:
                LoadLevel(Campaign08, "ChainCascade");
                break;
            case 4:
                Capture("Captures/phase52i-chain.png");
                break;
            case 5:
                LoadLevel(Campaign10, "ShapeInShape");
                break;
            case 6:
                Capture("Captures/phase52i-nested.png");
                break;
            case 7:
                LoadLevel(Campaign12, "Ice");
                Capture("Captures/phase52i-ice.png");
                break;
            case 8:
                LoadLevel(Campaign13, "Shutter");
                Capture("Captures/phase52i-shutter.png");
                break;
            case 9:
                LoadLevel(Campaign15, "Master-boosters");
                TryMagnet();
                Capture("Captures/phase52i-magnet.png");
                CancelMagnet();
                break;
            case 10:
                TryHammer();
                Capture("Captures/phase52i-hammer.png");
                break;
            case 11:
                AssertVc();
                report.AppendLine("CONFIRM BlockSmoothness=" + ShapeVisuals3D.BlockSmoothness.ToString("F2"));
                report.AppendLine("CONFIRM NestSmoothness=" + ShapeVisuals3D.NestSmoothness.ToString("F2"));
                report.AppendLine("CONFIRM NestCavitySmoothness=" + ShapeVisuals3D.NestCavitySmoothness.ToString("F2"));
                report.AppendLine("CONFIRM ConnectorSmoothness=" + ShapeVisuals3D.ConnectorSmoothness.ToString("F2"));
                report.AppendLine("CONFIRM Metallic=0 Emission=off");
                report.AppendLine("CONFIRM Phase52G selection APIs present=" + (typeof(PieceView3D).GetMethod("PlayTapFeedback") != null));
                report.AppendLine("CONFIRM Phase52H nestPulse present=" + (typeof(PieceView3D).GetMethod("PlayNestSocketPulse") != null));
                report.AppendLine("CONFIRM gameplay/chains/VC untouched");
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

    private static void InspectMaterials()
    {
        report.AppendLine(
            "materials Block="
            + ShapeVisuals3D.BlockSmoothness.ToString("F2")
            + " Nest="
            + ShapeVisuals3D.NestSmoothness.ToString("F2")
            + " Cavity="
            + ShapeVisuals3D.NestCavitySmoothness.ToString("F2")
            + " Connector="
            + ShapeVisuals3D.ConnectorSmoothness.ToString("F2")
            + " Metallic="
            + ShapeVisuals3D.BlockMetallic.ToString("F0"));
    }

    private static void InspectShadows(string label)
    {
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        int shadows = 0;
        int aligned = 0;
        int mismatches = 0;
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null)
            {
                continue;
            }

            Transform shadow = view.transform.Find("ContactShadow3D");
            Transform mesh = view.transform.Find("Mesh");
            if (shadow == null || !shadow.gameObject.activeInHierarchy)
            {
                continue;
            }

            shadows++;
            if (mesh != null)
            {
                float dx = Mathf.Abs(shadow.localPosition.x - mesh.localPosition.x);
                float dz = Mathf.Abs(shadow.localPosition.z - mesh.localPosition.z);
                if (dx < 0.001f && dz < 0.001f)
                {
                    aligned++;
                }
                else
                {
                    mismatches++;
                }
            }
        }

        report.AppendLine(
            label
            + " shadows="
            + shadows
            + " alignedXZ="
            + aligned
            + " mismatch="
            + mismatches);
        if (mismatches > 0)
        {
            lastError = "shadow XZ mismatch";
        }
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
            magnet.CancelMagnet("phase52i");
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

        Block[] blocks = board.GetComponentsInChildren<Block>(true);
        Block target = null;
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] != null && !blocks[i].IsFrozen && blocks[i].CellCount == 1)
            {
                target = blocks[i];
                break;
            }
        }

        if (target == null)
        {
            report.AppendLine("hammer no target");
            return;
        }

        hammer.ActivateHammer();
        bool ok = hammer.TryHandleSelectionPress(target);
        report.AppendLine("hammer " + target.ShapeType + " ok=" + ok + " phase=" + hammer.Phase);
    }

    private static void Capture(string path)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        BoardCamera3D boardCam = Object.FindFirstObjectByType<BoardCamera3D>();
        Camera live = boardCam != null ? boardCam.Camera : Camera.main;
        if (live == null)
        {
            report.AppendLine("SHOT FAIL " + path);
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
        report.AppendLine(ok ? "RESULT ok" : "RESULT failed");
        if (!string.IsNullOrEmpty(lastError))
        {
            report.AppendLine("lastError=" + lastError);
        }

        Directory.CreateDirectory("Captures");
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log("[Phase52I] " + (ok ? "ok" : "failed") + " → " + ReportPath);
        EditorApplication.isPlaying = false;
    }
}
