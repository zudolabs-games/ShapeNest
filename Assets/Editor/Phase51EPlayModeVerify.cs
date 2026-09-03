using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 51E Editor play-mode verification: visual-only mesh centering, chain safety.
/// Triggered by Captures/phase51e-autotest.flag or menu Shape Nest / Phase 51E Verify Visual Centering.
/// </summary>
[InitializeOnLoad]
public static class Phase51EPlayModeVerify
{
    private const string FlagPath = "Captures/phase51e-autotest.flag";
    private const string ReportPath = "Captures/phase51e-report.txt";
    private const string SessionKey = "Phase51E.Verify";
    private const string BeforeReference = "Captures/phase51e-before-t1-all-shapes.png";
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

    static Phase51EPlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromFlag;
    }

    [MenuItem("Shape Nest/Phase 51E Verify Visual Centering")]
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
            SessionState.SetBool("Phase51.Verify", false);
            SessionState.SetBool("Phase51D.Verify", false);
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
        SessionState.SetBool("Phase51.Verify", false);
        SessionState.SetBool("Phase51D.Verify", false);
        SessionState.SetBool(SessionKey, false);
        running = true;
        step = 0;
        stepAt = EditorApplication.timeSinceStartup;
        report.Length = 0;
        lastError = null;
        report.AppendLine("Phase 51E Play Mode verification");
        report.AppendLine(
            "legacy verifier; Phase 51F uses VisualCenterBoardPlaneOffsetLocal="
            + BoardAdaptivePresentation3D.VisualCenterBoardPlaneOffsetLocal.ToString("F3"));
        if (File.Exists(BeforeReference))
        {
            report.AppendLine("before ref " + BeforeReference);
        }
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
                return 3.5f;
            case 5:
            case 7:
            case 9:
            case 11:
            case 13:
                return 1.15f;
            case 4:
                return 0.7f;
            case 6:
                return 0.5f;
            case 16:
                return 0.55f;
            case 17:
                return 0.42f;
            case 18:
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
                Capture("Captures/phase51e-t1-all-shapes.png");
                InspectMeshes("T1 all-shapes");
                InspectVisualCenterOffset("T1");
                InspectRootSeating("T1");
                InspectCentering("T1");
                InspectFactoryMeshes();
                break;
            case 2:
                CaptureAngled("Captures/phase51e-t2-angled.png");
                break;
            case 3:
                bool moved = TryMoveAnyBlock();
                report.AppendLine("T3 move: " + (moved ? "TryMoveBlock succeeded" : "no legal move found"));
                break;
            case 4:
                Capture("Captures/phase51e-t3-after-move.png");
                break;
            case 5:
                LoadLevel(Campaign07, "Campaign_07_ChainIntro");
                break;
            case 6:
                Capture("Captures/phase51e-t-chain-2.png");
                InspectChains("T4 chain");
                InspectChainRelativePositions("T4 chain-2");
                InspectCentering("T4");
                break;
            case 7:
                LoadLevel(Campaign10, "Campaign_10_ShapeInShape");
                break;
            case 8:
                Capture("Captures/phase51e-t5-nested.png");
                InspectNested("T5 nested");
                InspectCentering("T5");
                break;
            case 9:
                LoadLevel(Campaign12, "Campaign_12_Ice");
                break;
            case 10:
                Capture("Captures/phase51e-t9-ice.png");
                InspectIce("T9 ice");
                InspectCentering("T9");
                break;
            case 11:
                LoadLevel(Campaign13, "Campaign_13_Shutter");
                break;
            case 12:
                Capture("Captures/phase51e-t10-shutter.png");
                InspectShutters("T10 shutter");
                break;
            case 13:
                LoadLevel(Campaign08, "Campaign_08_ChainCascade");
                break;
            case 14:
                Capture("Captures/phase51e-t-chain-3.png");
                InspectChainRelativePositions("T chain-3");
                break;
            case 15:
                Capture("Captures/phase51e-t7-magnet-board.png");
                TryMagnetSelect();
                break;
            case 16:
                Capture("Captures/phase51e-t7-magnet-selecting.png");
                CancelMagnet();
                break;
            case 17:
                TryHammerSmash();
                break;
            case 18:
                Capture("Captures/phase51e-t8-hammer.png");
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
        System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(LevelManager).GetField("timerRunning", flags)?.SetValue(manager, false);
        typeof(LevelManager).GetField("remainingSeconds", flags)?.SetValue(manager, 90f);
        typeof(LevelManager).GetField("session", flags)?.SetValue(manager, LevelManager.SessionState.Playing);
        Time.timeScale = 1f;
        report.AppendLine("LOAD " + label + " ok");
        BoardPresenter3D board = Object.FindFirstObjectByType<BoardPresenter3D>();
        if (board != null)
        {
            report.AppendLine("  cell=" + board.CellWorldSize.ToString("F3"));
        }
    }

    private static void Capture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        BoardCamera3D boardCam = Object.FindFirstObjectByType<BoardCamera3D>();
        Camera live = boardCam != null ? boardCam.Camera : Camera.main;
        if (live == null)
        {
            ScreenCapture.CaptureScreenshot(path);
            report.AppendLine("SHOT " + path + " (fallback)");
            return;
        }

        RenderTexture savedRt = live.targetTexture;
        RenderTexture rt = new RenderTexture(1080, 1920, 24);
        live.targetTexture = rt;
        live.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        live.targetTexture = savedRt;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
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

    private static void InspectFactoryMeshes()
    {
        ShapeMeshFactory3D.ClearCache();
        foreach (ShapeType shape in System.Enum.GetValues(typeof(ShapeType)))
        {
            Mesh solid = ShapeMeshFactory3D.GetSolidMesh(shape);
            Mesh nest = ShapeMeshFactory3D.GetNestMesh(shape);
            LogMeshBounds("factory solid " + shape, solid);
            LogMeshBounds("factory nest " + shape, nest);
        }
    }

    private static void LogMeshBounds(string tag, Mesh mesh)
    {
        if (mesh == null)
        {
            report.AppendLine(tag + " missing");
            return;
        }

        Bounds b = mesh.bounds;
        report.AppendLine(
            tag
            + " verts=" + mesh.vertexCount
            + " minY=" + b.min.y.ToString("F4")
            + " center=" + b.center.ToString("F4")
            + " extents=" + b.extents.ToString("F4"));
    }

    private static void InspectVisualCenterOffset(string tag)
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>();
        float cell = presenter != null ? presenter.CellWorldSize : 0.646f;
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        int counted = 0;
        float minOffset = 999f;
        float maxOffset = -999f;
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null || !view.gameObject.activeInHierarchy || !view.HasRenderableMesh)
            {
                continue;
            }

            counted++;
            Transform meshChild = view.transform.Find("Mesh");
            float visualLocalY = meshChild != null ? meshChild.localPosition.y : 0f;
            minOffset = Mathf.Min(minOffset, visualLocalY);
            maxOffset = Mathf.Max(maxOffset, visualLocalY);
            float expectedY = 0f;
            float expectedZ = view.ConfiguredAsNest
                ? 0f
                : -BoardAdaptivePresentation3D.VisualCenterBoardPlaneOffsetLocal;
            Vector3 expected = new Vector3(0f, expectedY, expectedZ);
            if ((meshChild != null ? meshChild.localPosition - expected : Vector3.one).magnitude > 0.05f)
            {
                report.AppendLine(
                    "  " + view.ConfiguredShape
                    + (view.ConfiguredAsNest ? " nest" : " block")
                    + " visualLocalY=" + visualLocalY.ToString("F4")
                    + " expected=" + expected.ToString("F4"));
            }
        }

        report.AppendLine(
            tag + " visualOffset views=" + counted
            + " localY range=[" + minOffset.ToString("F4") + "," + maxOffset.ToString("F4") + "]");
    }

    private static void InspectRootSeating(string tag)
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>();
        if (presenter == null)
        {
            return;
        }

        float surfaceY = presenter.CellSurfaceWorldY;
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        int counted = 0;
        float maxErr = 0f;
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null || !view.gameObject.activeInHierarchy || !view.HasRenderableMesh || view.ConfiguredAsNest)
            {
                continue;
            }

            counted++;
            float halfH = Mathf.Abs(view.transform.lossyScale.y) * 0.5f;
            float expectedY = surfaceY + view.SurfaceLift + halfH;
            float err = Mathf.Abs(view.transform.position.y - expectedY);
            maxErr = Mathf.Max(maxErr, err);
            if (err > 0.004f)
            {
                report.AppendLine(
                    "  " + view.ConfiguredShape
                    + " rootY=" + view.transform.position.y.ToString("F4")
                    + " expected=" + expectedY.ToString("F4")
                    + " lift=" + view.SurfaceLift.ToString("F4"));
            }
        }

        report.AppendLine(tag + " rootSeating blocks=" + counted + " maxRootYErr=" + maxErr.ToString("F4"));
    }

    private static void InspectChainRelativePositions(string tag)
    {
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>();
        IGridSpace space = presenter != null ? presenter.GridSpace : null;
        float surfaceY = presenter != null ? presenter.CellSurfaceWorldY : 0f;
        float maxYSpread = 0f;
        float maxPlanarErr = 0f;
        int chains = 0;
        PieceView3D[] allViews = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);

        for (int b = 0; b < blocks.Length; b++)
        {
            Block block = blocks[b];
            if (block == null || block.CellCount < 2 || block.WorldView == null)
            {
                continue;
            }

            chains++;
            var blockViews = new List<PieceView3D>();
            for (int i = 0; i < allViews.Length; i++)
            {
                PieceView3D view = allViews[i];
                if (view != null && view.SourceBlock == block && view.gameObject.activeInHierarchy)
                {
                    blockViews.Add(view);
                }
            }

            if (blockViews.Count < 2)
            {
                report.AppendLine(tag + " missing views for " + block.ShapeType + " cells=" + block.CellCount);
                continue;
            }

            float minY = blockViews[0].transform.position.y;
            float maxY = minY;
            for (int i = 1; i < blockViews.Count; i++)
            {
                minY = Mathf.Min(minY, blockViews[i].transform.position.y);
                maxY = Mathf.Max(maxY, blockViews[i].transform.position.y);
            }

            float spread = maxY - minY;
            maxYSpread = Mathf.Max(maxYSpread, spread);
            if (spread > 0.001f)
            {
                report.AppendLine(tag + " Y spread " + block.ShapeType + " dy=" + spread.ToString("F4"));
            }

            if (space == null)
            {
                continue;
            }

            Vector3 anchorPos = block.WorldView.transform.position;
            Vector2Int anchorCell = block.GetCellWorld(block.AnchorCellIndex);
            for (int c = 0; c < block.CellCount; c++)
            {
                if (c == block.AnchorCellIndex)
                {
                    continue;
                }

                Vector2Int cell = block.GetCellWorld(c);
                Vector3 expected = space.GridToWorld(cell);
                float halfH = Mathf.Abs(block.WorldView.transform.lossyScale.y) * 0.5f;
                expected.y = surfaceY + block.WorldView.SurfaceLift + halfH;
                Vector3 expectedRel = expected - anchorPos;
                expectedRel.y = 0f;

                PieceView3D matched = null;
                float best = float.MaxValue;
                for (int v = 0; v < blockViews.Count; v++)
                {
                    if (blockViews[v] == block.WorldView)
                    {
                        continue;
                    }

                    float d = Vector3.SqrMagnitude(blockViews[v].transform.position - expected);
                    if (d < best)
                    {
                        best = d;
                        matched = blockViews[v];
                    }
                }

                if (matched == null)
                {
                    continue;
                }

                Vector3 actualRel = matched.transform.position - anchorPos;
                actualRel.y = 0f;
                float err = (actualRel - expectedRel).magnitude;
                maxPlanarErr = Mathf.Max(maxPlanarErr, err);
                if (err > 0.02f)
                {
                    report.AppendLine(
                        tag + " planarErr " + block.ShapeType
                        + " cell=" + cell
                        + " err=" + err.ToString("F4"));
                }
            }
        }

        report.AppendLine(
            tag + " chains=" + chains
            + " maxYSpread=" + maxYSpread.ToString("F4")
            + " maxPlanarRelErr=" + maxPlanarErr.ToString("F4"));
    }

    private static void InspectFootprint(string tag)
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>();
        float cell = presenter != null ? presenter.CellWorldSize : 1f;
        float tileFace = cell * BoardAdaptivePresentation3D.CellTileFaceRatio;
        float targetBlock = cell * BoardAdaptivePresentation3D.BlockFootprintRatio;
        float targetNest = cell * BoardAdaptivePresentation3D.NestFootprintRatio;
        report.AppendLine(
            tag + " cellPitch=" + cell.ToString("F3")
            + " tileFace=" + tileFace.ToString("F3")
            + " targetBlock=" + targetBlock.ToString("F3")
            + " (" + (targetBlock / cell).ToString("P0") + " pitch, "
            + (targetBlock / tileFace).ToString("P0") + " tile)"
            + " targetNest=" + targetNest.ToString("F3"));

        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null || !view.gameObject.activeInHierarchy || !view.HasRenderableMesh)
            {
                continue;
            }

            if (view.SourceBlock == null && !view.ConfiguredAsNest)
            {
                continue;
            }

            float configured = view.ConfiguredFootprintScale.x;
            MeshRenderer renderer = view.OuterMeshRenderer;
            float renderedX = renderer != null ? renderer.bounds.size.x : configured;
            float renderedZ = renderer != null ? renderer.bounds.size.z : configured;
            float meshUnit = 1f;
            MeshFilter filter = view.GetComponentInChildren<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                meshUnit = filter.sharedMesh.bounds.size.x;
            }

            report.AppendLine(
                "  " + view.ConfiguredShape
                + (view.ConfiguredAsNest ? " nest" : " block")
                + " cfg=" + configured.ToString("F3")
                + " rendXZ=" + renderedX.ToString("F3") + "x" + renderedZ.ToString("F3")
                + " meshUnitX=" + meshUnit.ToString("F3")
                + " fillPitch=" + (renderedX / cell).ToString("P0")
                + " fillTile=" + (renderedX / tileFace).ToString("P0"));
        }
    }

    private static void InspectCentering(string tag)
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>();
        IGridSpace space = presenter != null ? presenter.GridSpace : null;
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        float cell = presenter != null ? presenter.CellWorldSize : 1f;
        float maxMesh = 0f;
        float maxWorld = 0f;
        float maxMeshChild = 0f;
        int counted = 0;
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null || !view.gameObject.activeInHierarchy || !view.HasRenderableMesh)
            {
                continue;
            }

            counted++;
            MeshFilter filter = view.GetComponentInChildren<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null)
            {
                Vector3 mc = mesh.bounds.center;
                maxMesh = Mathf.Max(maxMesh, new Vector2(mc.x, mc.z).magnitude);
            }

            Transform meshChild = view.transform.Find("Mesh");
            if (meshChild != null)
            {
                Vector3 lp = meshChild.localPosition;
                maxMeshChild = Mathf.Max(maxMeshChild, new Vector2(lp.x, lp.z).magnitude);
            }

            MeshRenderer renderer = view.OuterMeshRenderer;
            Vector3 worldCenter = renderer != null ? renderer.bounds.center : view.transform.position;
            Vector3 expected = view.transform.position;
            if (space != null && view.SourceBlock != null)
            {
                expected = view.transform.position;
            }

            Vector2 planar = new Vector2(worldCenter.x - expected.x, worldCenter.z - expected.z);
            maxWorld = Mathf.Max(maxWorld, planar.magnitude);
            if (planar.magnitude > cell * 0.08f)
            {
                report.AppendLine(
                    tag + " offset " + view.name
                    + " shape=" + view.ConfiguredShape
                    + " nest=" + view.ConfiguredAsNest
                    + " dxz=" + planar.ToString("F3")
                    + " meshC=" + (mesh != null ? mesh.bounds.center.ToString("F3") : "n/a")
                    + " meshLocal=" + (meshChild != null ? meshChild.localPosition.ToString("F3") : "n/a"));
            }
        }

        report.AppendLine(
            tag + " centering views=" + counted
            + " maxMeshBoundsXZ=" + maxMesh.ToString("F4")
            + " maxMeshChildXZ=" + maxMeshChild.ToString("F4")
            + " maxWorldDxz=" + maxWorld.ToString("F4")
            + " cell=" + cell.ToString("F3")
            + " frac=" + (maxWorld / Mathf.Max(0.001f, cell)).ToString("F3"));
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
            magnet.CancelMagnet("phase51e");
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
