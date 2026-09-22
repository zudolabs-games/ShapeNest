using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public static class Phase2VerificationRunner
{

    [MenuItem("Tools/Shape Nest/Run Phase 2 Verification & Captures")]
    public static void RunFromMenu()
    {
        RunVerification(captureScreenshots: true);
    }

    public static void RunFromCommandLine()
    {
        RunVerification(captureScreenshots: true);
    }

    public static void RunVerification(bool captureScreenshots)
    {
        ShapeVisuals3D.Invalidate();
        ShapeMeshFactory3D.ClearCache();
        BoardMeshFactory3D.ClearCache();

        var sb = new StringBuilder();
        sb.AppendLine("============================================================");
        sb.AppendLine("PHASE 2 VERIFICATION & AUTOMATED TEST SUITE");
        sb.AppendLine("============================================================");

        // 1. AutoMatch Tests
        string autoMatchReport = ShapeNestAutoMatchTests.RunAll();
        sb.AppendLine("\n--- 1. AUTOMATCH TESTS ---");
        sb.AppendLine(autoMatchReport);

        // 2. Solver Tests
        string solverReport = ShapeNestSolverTests.RunAll();
        sb.AppendLine("\n--- 2. SOLVER TESTS ---");
        sb.AppendLine(solverReport);

        // 3. Shutter Mechanic Verification
        sb.AppendLine("\n--- 3. SHUTTER MECHANIC VERIFICATION ---");
        try
        {
            ShutterMechanicVerification.RunFromMenu();
            sb.AppendLine("Shutter Mechanic Verification: COMPLETED (see report)");
        }
        catch (System.Exception ex)
        {
            sb.AppendLine($"Shutter Mechanic Verification: FAIL - {ex.Message}");
        }

        // 4. Campaign Levels Loading & Screenshot Captures
        sb.AppendLine("\n--- 4. CAMPAIGN LEVEL REGRESSION (1 TO 15) ---");

        if (!EditorApplication.isPlaying)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }
        var lm = Object.FindFirstObjectByType<LevelManager>();
        var cam = Object.FindFirstObjectByType<BoardCamera3D>();
        var bp = Object.FindFirstObjectByType<BoardPresenter3D>();

        var bpc = Object.FindFirstObjectByType<BoardPresentationController>();
        var env = Object.FindFirstObjectByType<BoardEnvironment3D>();

        if (lm == null)
        {
            sb.AppendLine("ERROR: LevelManager not found in SampleScene");
        }
        else
        {
            string afterDir = Path.Combine(Application.dataPath, "../Screenshots/After");
            if (!Directory.Exists(afterDir)) Directory.CreateDirectory(afterDir);

            int[] captureIndices = new int[] { 0, 3, 6, 9, 11, 12, 14 }; // 1, 4, 7, 10, 12, 13, 15
            string[] captureNames = new string[] { "Campaign_01", "Campaign_04", "Campaign_07", "Campaign_10", "Campaign_12", "Campaign_13", "Campaign_15" };

            for (int i = 0; i < 15; i++)
            {
                int levelIndex = i;
                string levelName = $"Campaign_{i + 1:D2}";
                try
                {
                    if (bpc != null)
                    {
                        bpc.ClearAllPieceViewsImmediate();
                    }
                    if (bp != null)
                    {
                        if (bp.PiecesRoot != null)
                        {
                            for (int c = bp.PiecesRoot.childCount - 1; c >= 0; c--)
                            {
                                Object.DestroyImmediate(bp.PiecesRoot.GetChild(c).gameObject);
                            }
                        }
                        if (bp.NestsRoot != null)
                        {
                            for (int c = bp.NestsRoot.childCount - 1; c >= 0; c--)
                            {
                                Object.DestroyImmediate(bp.NestsRoot.GetChild(c).gameObject);
                            }
                        }
                    }
                    bool loaded = lm.LoadLevel(levelIndex);
                    if (!loaded)
                    {
                        sb.AppendLine($"{levelName}: FAIL (LoadLevel returned false)");
                        continue;
                    }

                    if (bp != null)
                    {
                        bp.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                    }
                    if (bpc != null)
                    {
                        bpc.ForceSyncPresentationEditor();
                    }
                    Camera unityCam = cam != null ? cam.Camera : Camera.main;
                    int captureIdx = System.Array.IndexOf(captureIndices, levelIndex);
                    bool isCapture = captureScreenshots && captureIdx >= 0;
                    RenderTexture rt = null;
                    if (isCapture && unityCam != null)
                    {
                        rt = new RenderTexture(1080, 1920, 24);
                        unityCam.targetTexture = rt;
                    }

                    if (cam != null)
                    {
                        cam.FrameBoard(bp);
                    }
                    if (env != null && bp != null && cam != null)
                    {
                        env.Apply(bp, cam.Camera);
                    }


                    if (isCapture && unityCam != null && rt != null)
                    {
                        int width = 1080;
                        int height = 1920;
                        unityCam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        tex.Apply();
                        unityCam.targetTexture = null;
                        RenderTexture.active = null;
                        Object.DestroyImmediate(rt);

                        string shotPath = Path.Combine(afterDir, $"{captureNames[captureIdx]}_after.png");
                        File.WriteAllBytes(shotPath, tex.EncodeToPNG());
                        Object.DestroyImmediate(tex);

                        if (captureNames[captureIdx] == "Campaign_04")
                        {
                            Transform piecesT = bp != null ? bp.PiecesRoot : null;
                            Transform nestsT = bp != null ? bp.NestsRoot : null;
                            Transform cellsT = bp != null ? bp.transform.Find("Cells") : null;
                            Transform surfaceT = bp != null ? bp.transform.Find("BoardSurface") : null;
                            bool pActive = piecesT != null && piecesT.gameObject.activeSelf;
                            bool nActive = nestsT != null && nestsT.gameObject.activeSelf;
                            bool cActive = cellsT != null && cellsT.gameObject.activeSelf;
                            bool sActive = surfaceT != null && surfaceT.gameObject.activeSelf;
                            if (piecesT != null) piecesT.gameObject.SetActive(false);
                            if (nestsT != null) nestsT.gameObject.SetActive(false);
                            if (surfaceT != null) surfaceT.gameObject.SetActive(false);

                            var emptyRt = new RenderTexture(width, height, 24);
                            unityCam.targetTexture = emptyRt;
                            unityCam.Render();
                            RenderTexture.active = emptyRt;
                            var emptyTex = new Texture2D(width, height, TextureFormat.RGB24, false);
                            emptyTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                            emptyTex.Apply();
                            unityCam.targetTexture = null;
                            RenderTexture.active = null;
                            Object.DestroyImmediate(emptyRt);

                            string emptyShotPath = Path.Combine(afterDir, "Campaign_04_empty_tray_after.png");
                            File.WriteAllBytes(emptyShotPath, emptyTex.EncodeToPNG());
                            Object.DestroyImmediate(emptyTex);
                            sb.AppendLine($"Captured {emptyShotPath}");

                            if (piecesT != null) piecesT.gameObject.SetActive(pActive);
                            if (nestsT != null) nestsT.gameObject.SetActive(nActive);
                            if (cellsT != null) cellsT.gameObject.SetActive(cActive);
                            if (surfaceT != null) surfaceT.gameObject.SetActive(sActive);
                        }

                        Vector2 footprint = bp != null ? bp.BoardFootprint : Vector2.zero;
                        Vector3 center = bp != null ? bp.BoardCenterWorld : Vector3.zero;
                        sb.AppendLine($"{levelName}: PASS | Captured {shotPath} | Footprint={footprint}, Center={center}, FOV={unityCam.fieldOfView}, Pos={unityCam.transform.position}");
                    }
                    else
                    {
                        sb.AppendLine($"{levelName}: PASS");
                    }
                }
                catch (System.Exception ex)
                {
                    sb.AppendLine($"{levelName}: FAIL - Exception: {ex.Message}");
                }
            }

            // 5. Dense Reference Level (Campaign_43_Reference / Level 29)
            try
            {
                var refLevel = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Levels/Campaign_43_Reference.asset");
                if (refLevel != null)
                {
                    if (bpc != null) bpc.ClearAllPieceViewsImmediate();
                    if (bp != null)
                    {
                        if (bp.PiecesRoot != null)
                        {
                            for (int c = bp.PiecesRoot.childCount - 1; c >= 0; c--)
                                Object.DestroyImmediate(bp.PiecesRoot.GetChild(c).gameObject);
                        }
                        if (bp.NestsRoot != null)
                        {
                            for (int c = bp.NestsRoot.childCount - 1; c >= 0; c--)
                                Object.DestroyImmediate(bp.NestsRoot.GetChild(c).gameObject);
                        }
                    }

                    lm.LoadLevel(refLevel);
                    if (bp != null) bp.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                    if (bpc != null) bpc.ForceSyncPresentationEditor();

                    Camera unityCam = cam != null ? cam.Camera : Camera.main;
                    if (unityCam != null)
                    {
                        var rt = new RenderTexture(1080, 1920, 24);
                        unityCam.targetTexture = rt;
                        if (cam != null) cam.FrameBoard(bp);
                        if (env != null && bp != null) env.Apply(bp, unityCam);

                        int width = 1080;
                        int height = 1920;
                        unityCam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        tex.Apply();
                        unityCam.targetTexture = null;
                        RenderTexture.active = null;
                        Object.DestroyImmediate(rt);

                        string shotPath = Path.Combine(afterDir, "Campaign_43_Reference_after.png");
                        File.WriteAllBytes(shotPath, tex.EncodeToPNG());
                        Object.DestroyImmediate(tex);
                        sb.AppendLine($"Campaign_43_Reference (Dense Level 29): PASS | Captured {shotPath}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"Campaign_43_Reference: FAIL - Exception: {ex.Message}");
            }
        }

        string logPath = Path.Combine(Application.dataPath, "../Phase2_Verification_Log.txt");
        File.WriteAllText(logPath, sb.ToString());
        Debug.Log(sb.ToString());
    }
}
