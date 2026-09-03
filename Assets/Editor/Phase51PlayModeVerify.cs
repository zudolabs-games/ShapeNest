using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 51 Editor play-mode verification. Triggered by Captures/phase51-autotest.flag
/// or menu Shape Nest / Phase 51 Verify 3D Pieces.
/// </summary>
[InitializeOnLoad]
public static class Phase51PlayModeVerify
{
    private const string FlagPath = "Captures/phase51-autotest.flag";
    private const string ReportPath = "Captures/phase51-report.txt";
    private const string SessionKey = "Phase51.Verify";
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

    static Phase51PlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromFlag;
    }

    [MenuItem("Shape Nest/Phase 51 Verify 3D Pieces")]
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
        if (File.Exists("Captures/phase51a-autotest.flag")
            || SessionState.GetBool("Phase51A.Verify", false)
            || File.Exists("Captures/phase51b-autotest.flag")
            || SessionState.GetBool("Phase51B.Verify", false))
        {
            return;
        }

        if (File.Exists(FlagPath))
        {
            File.Delete(FlagPath);
            SessionState.SetBool(SessionKey, true);
            if (!EditorApplication.isPlaying)
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
        if (change == PlayModeStateChange.EnteredPlayMode
            && SessionState.GetBool(SessionKey, false)
            && !SessionState.GetBool("Phase51A.Verify", false)
            && !File.Exists("Captures/phase51a-autotest.flag"))
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
        report.AppendLine("Phase 51 Play Mode verification");
        report.AppendLine("Unity " + Application.unityVersion);
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

        try
        {
            Advance();
        }
        catch (System.Exception ex)
        {
            lastError = ex.GetType().Name + ": " + ex.Message;
            report.AppendLine("EXCEPTION step " + step + " " + lastError);
            Finish(false);
        }
    }

    private static float WaitForStep(int current)
    {
        switch (current)
        {
            case 0:
                return 2.2f;
            case 4:
                return 0.7f;
            case 6:
                return 0.5f;
            case 16:
                return 0.42f;
            case 17:
                return 0.55f;
            default:
                return 0.85f;
        }
    }

    private static void Advance()
    {
        switch (step)
        {
            case 0:
                LoadLevel(Campaign15, "Campaign_15_Master (all shapes)");
                break;
            case 1:
                Capture("Captures/phase51-t1-all-shapes.png");
                InspectMeshes("T1 all-shapes");
                break;
            case 2:
                CaptureAngled("Captures/phase51-t2-angled.png");
                break;
            case 3:
                bool moved = TryMoveAnyBlock();
                report.AppendLine("T3 move: " + (moved ? "TryMoveBlock succeeded" : "no legal move found"));
                break;
            case 4:
                Capture("Captures/phase51-t3-after-move.png");
                break;
            case 5:
                LoadLevel(Campaign07, "Campaign_07_ChainIntro");
                break;
            case 6:
                Capture("Captures/phase51-t4-chain.png");
                InspectChains("T4 chain");
                break;
            case 7:
                LoadLevel(Campaign10, "Campaign_10_ShapeInShape");
                break;
            case 8:
                Capture("Captures/phase51-t5-nested.png");
                InspectNested("T5 nested");
                break;
            case 9:
                LoadLevel(Campaign12, "Campaign_12_Ice");
                break;
            case 10:
                Capture("Captures/phase51-t9-ice.png");
                InspectIce("T9 ice");
                break;
            case 11:
                LoadLevel(Campaign13, "Campaign_13_Shutter");
                break;
            case 12:
                Capture("Captures/phase51-t10-shutter.png");
                InspectShutters("T10 shutter");
                break;
            case 13:
                LoadLevel(Campaign08, "Campaign_08_ChainCascade");
                break;
            case 14:
                Capture("Captures/phase51-t7-magnet-board.png");
                TryMagnetSelect();
                break;
            case 15:
                Capture("Captures/phase51-t7-magnet-selecting.png");
                CancelMagnet();
                break;
            case 16:
                TryHammerSmash();
                break;
            case 17:
                Capture("Captures/phase51-t8-hammer.png");
                break;
            default:
                Finish(lastError == null);
                return;
        }

        step++;
        stepAt = EditorApplication.timeSinceStartup;
    }

    private static void LoadLevel(string assetPath, string label)
    {
        LevelManager manager = Object.FindFirstObjectByType<LevelManager>();
        LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>(assetPath);
        if (manager == null || data == null)
        {
            report.AppendLine("LOAD FAIL " + label);
            return;
        }

        manager.LoadLevel(data);
        report.AppendLine("LOAD " + label + " ok");
    }

    private static void Capture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        ScreenCapture.CaptureScreenshot(path);
        report.AppendLine("SHOT " + path);
    }

    private static void CaptureAngled(string path)
    {
        BoardCamera3D boardCam = Object.FindFirstObjectByType<BoardCamera3D>();
        BoardPresenter3D board = Object.FindFirstObjectByType<BoardPresenter3D>();
        Camera live = boardCam != null ? boardCam.Camera : null;
        if (board == null || live == null)
        {
            report.AppendLine("T2 angled camera missing board=" + (board != null) + " cam=" + (live != null));
            Capture(path);
            return;
        }

        Transform pose = live.transform;
        Vector3 savedPos = pose.position;
        Quaternion savedRot = pose.rotation;
        bool savedEnabled = boardCam.enabled;
        RenderTexture savedRt = live.targetTexture;
        boardCam.enabled = false;

        Quaternion inspectRot = Quaternion.Euler(50f, 34f, 0f);
        pose.rotation = inspectRot;
        pose.position = board.BoardCenterWorld + (inspectRot * new Vector3(0f, 0f, -6.5f));
        live.orthographic = true;

        RenderTexture rt = new RenderTexture(1080, 1920, 24);
        live.targetTexture = rt;
        live.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        live.targetTexture = savedRt;
        pose.position = savedPos;
        pose.rotation = savedRot;
        boardCam.enabled = savedEnabled;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        report.AppendLine("SHOT angled-live " + path);
    }

    private static void InspectMeshes(string tag)
    {
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        var seen = new HashSet<ShapeType>();
        int spriteOnMesh = 0;
        int thin = 0;
        int ok = 0;
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null || !view.gameObject.activeInHierarchy)
            {
                continue;
            }

            seen.Add(view.ConfiguredShape);
            if (!view.HasRenderableMesh)
            {
                report.AppendLine(tag + " missing mesh " + view.name);
                continue;
            }

            MeshRenderer[] renderers = view.GetComponentsInChildren<MeshRenderer>(false);
            SpriteRenderer[] sprites = view.GetComponentsInChildren<SpriteRenderer>(false);
            spriteOnMesh += sprites.Length;
            Bounds b = renderers.Length > 0 ? renderers[0].bounds : default;
            float height = b.size.y;
            if (height < 0.08f)
            {
                thin++;
            }
            else
            {
                ok++;
            }
        }

        report.AppendLine(
            tag + " views=" + views.Length
            + " meshOk=" + ok
            + " thin=" + thin
            + " spritesOnPiece=" + spriteOnMesh
            + " shapes=" + string.Join(",", seen));
    }

    private static void InspectChains(string tag)
    {
        ChainConnectorView3D[] links = Object.FindObjectsByType<ChainConnectorView3D>(FindObjectsSortMode.None);
        int live = 0;
        for (int i = 0; i < links.Length; i++)
        {
            if (links[i] != null && links[i].gameObject.activeInHierarchy)
            {
                live++;
            }
        }

        report.AppendLine(tag + " connectors=" + live);
        InspectMeshes(tag);
    }

    private static void InspectNested(string tag)
    {
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        int nested = 0;
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].HasNestedInner)
            {
                nested++;
            }
        }

        report.AppendLine(tag + " nestedInners=" + nested);
        InspectMeshes(tag);
    }

    private static void InspectIce(string tag)
    {
        IceView3D[] ices = Object.FindObjectsByType<IceView3D>(FindObjectsSortMode.None);
        int live = 0;
        for (int i = 0; i < ices.Length; i++)
        {
            if (ices[i] != null && ices[i].gameObject.activeInHierarchy && ices[i].IsBound)
            {
                live++;
            }
        }

        report.AppendLine(tag + " iceViews=" + live);
    }

    private static void InspectShutters(string tag)
    {
        ShutterView3D[] shutters = Object.FindObjectsByType<ShutterView3D>(FindObjectsSortMode.None);
        int live = 0;
        for (int i = 0; i < shutters.Length; i++)
        {
            if (shutters[i] != null && shutters[i].gameObject.activeInHierarchy)
            {
                live++;
            }
        }

        report.AppendLine(tag + " shutterViews=" + live);
    }

    private static bool TryMoveAnyBlock()
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        if (board == null)
        {
            return false;
        }

        Vector2Int[] dirs =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || block.CellCount != 1 || block.IsFrozen)
            {
                continue;
            }

            Vector2Int from = block.GridPosition;
            for (int d = 0; d < dirs.Length; d++)
            {
                Vector2Int to = from + dirs[d];
                if (board.TryMoveBlock(block, from, to))
                {
                    report.AppendLine("moved " + block.ShapeType + " " + from + " -> " + to);
                    return true;
                }
            }
        }

        return false;
    }

    private static void TryMagnetSelect()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            report.AppendLine("T7 magnet missing");
            return;
        }

        magnet.ActivateMagnet();
        report.AppendLine("T7 magnet selecting=" + magnet.IsSelecting);
    }

    private static void CancelMagnet()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet != null && magnet.IsSelecting)
        {
            magnet.CancelMagnet("phase51");
            report.AppendLine("T7 magnet cancelled");
        }
    }

    private static void TryHammerSmash()
    {
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (hammer == null || board == null)
        {
            report.AppendLine("T8 hammer missing");
            return;
        }

        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        Block target = null;
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsFrozen || board.IsBlockUnderClosedShutter(block))
            {
                continue;
            }

            if (block.CellCount == 1)
            {
                target = block;
                break;
            }

            if (target == null)
            {
                target = block;
            }
        }

        if (target == null)
        {
            report.AppendLine("T8 no hammer target");
            return;
        }

        hammer.ActivateHammer();
        bool accepted = hammer.TryHandleSelectionPress(target);
        report.AppendLine("T8 hammer " + target.ShapeType + " accepted=" + accepted + " phase=" + hammer.Phase);
    }

    private static void Finish(bool ok)
    {
        EditorApplication.update -= Tick;
        running = false;
        report.AppendLine(ok ? "RESULT ok" : "RESULT failed");
        if (!string.IsNullOrEmpty(lastError))
        {
            report.AppendLine(lastError);
        }

        Directory.CreateDirectory("Captures");
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log(report.ToString());
        EditorApplication.isPlaying = false;
    }
}
