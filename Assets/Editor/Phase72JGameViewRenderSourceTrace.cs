using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Phase 72J — trace Game view render sources (cameras / canvases / legacy UI).
/// Diagnostic only: temporary disables are restored before return.
/// Menu: Shape Nest / Phase 72J Trace Game View Render Sources
/// </summary>
public static class Phase72JGameViewRenderSourceTrace
{
    private const string CapDir = "Captures";
    private const string ReportPath = "Captures/72j-render-source-report.txt";

    [MenuItem("Shape Nest/Phase 72J Trace Game View Render Sources")]
    public static void TraceNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[72J] Play Mode required while green is visible.");
            return;
        }

        string report = Run();
        Debug.Log("[72J]\n" + report);
    }

    public static string Run()
    {
        var sb = new StringBuilder(48000);
        sb.AppendLine("PHASE 72J — GAME VIEW RENDER SOURCE TRACE");
        sb.AppendLine(
            "frame=" + Time.frameCount +
            " t=" + Time.time.ToString("F3") +
            " paused=" + EditorApplication.isPaused);

        Directory.CreateDirectory(CapDir);

        DumpPresentationMode(sb);
        DumpCameras(sb);
        DumpCanvases(sb);
        DumpLegacyNames(sb);
        DumpBlockTargetImages(sb);
        DumpSprites(sb);

        RunDisableBattery(sb);

        File.WriteAllText(ReportPath, sb.ToString());
        sb.AppendLine("wrote " + ReportPath);
        return sb.ToString();
    }

    private static string PathOf(Transform tr)
    {
        string path = tr.name;
        while (tr.parent != null)
        {
            tr = tr.parent;
            path = tr.name + "/" + path;
        }

        return path;
    }

    private static void DumpPresentationMode(StringBuilder sb)
    {
        BoardPresentationController bpc =
            Object.FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Include);
        if (bpc == null)
        {
            sb.AppendLine("NO BoardPresentationController");
            return;
        }

        sb.AppendLine(
            "BoardPresentationController Mode=" + bpc.Mode +
            " IsWorld3DActive=" + bpc.IsWorld3DActive +
            " goActive=" + bpc.gameObject.activeInHierarchy);

        var so = new SerializedObject(bpc);
        SerializedProperty modeProp = so.FindProperty("mode");
        SerializedProperty legacyCam = so.FindProperty("legacyBoardCamera");
        SerializedProperty bgPlate = so.FindProperty("gameplayBgImage");
        sb.AppendLine("serialized mode=" + (modeProp != null ? modeProp.enumValueIndex.ToString() : "?"));
        if (legacyCam != null && legacyCam.objectReferenceValue != null)
        {
            sb.AppendLine(
                "legacyBoardCamera=" + legacyCam.objectReferenceValue.name +
                " type=" + legacyCam.objectReferenceValue.GetType().Name);
        }
        else
        {
            sb.AppendLine("legacyBoardCamera=null");
        }

        if (bgPlate != null && bgPlate.objectReferenceValue != null)
        {
            var img = bgPlate.objectReferenceValue as Image;
            sb.AppendLine(
                "gameplayBgImage=" + bgPlate.objectReferenceValue.name +
                " en=" + (img != null && img.enabled) +
                " goActive=" + ((Component)bgPlate.objectReferenceValue).gameObject.activeInHierarchy);
        }
    }

    private static void DumpCameras(StringBuilder sb)
    {
        sb.AppendLine("--- CAMERAS ---");
        Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(cams, (a, b) => a.depth.CompareTo(b.depth));
        for (int i = 0; i < cams.Length; i++)
        {
            Camera c = cams[i];
            var uacd = c.GetComponent<UniversalAdditionalCameraData>();
            string stack = "";
            if (uacd != null)
            {
                for (int s = 0; s < uacd.cameraStack.Count; s++)
                {
                    Camera sc = uacd.cameraStack[s];
                    stack += (sc == null
                        ? "null"
                        : sc.name + ":act=" + sc.gameObject.activeInHierarchy + ":en=" + sc.enabled) + ";";
                }
            }

            sb.AppendLine(
                "CAM id=" + c.GetInstanceID() +
                " name=" + c.name +
                " path=" + PathOf(c.transform) +
                " depth=" + c.depth +
                " goActive=" + c.gameObject.activeInHierarchy +
                " en=" + c.enabled +
                " target=" + (c.targetTexture != null ? c.targetTexture.name : "Screen/GameView") +
                " mask=" + c.cullingMask +
                " clear=" + c.clearFlags +
                " type=" + (uacd != null ? uacd.renderType.ToString() : "?") +
                " post=" + (uacd != null && uacd.renderPostProcessing) +
                " stack=[" + stack + "]");
        }
    }

    private static void DumpCanvases(StringBuilder sb)
    {
        sb.AppendLine("--- CANVASES ---");
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas cv = canvases[i];
            Image[] imgs = cv.GetComponentsInChildren<Image>(true);
            int en = 0;
            for (int j = 0; j < imgs.Length; j++)
            {
                if (imgs[j] != null && imgs[j].enabled && imgs[j].gameObject.activeInHierarchy)
                {
                    en++;
                }
            }

            sb.AppendLine(
                "CANVAS " + PathOf(cv.transform) +
                " mode=" + cv.renderMode +
                " active=" + cv.gameObject.activeInHierarchy +
                " en=" + cv.enabled +
                " sort=" + cv.sortingOrder +
                " cam=" + (cv.worldCamera != null ? cv.worldCamera.name : "null") +
                " imagesTotal=" + imgs.Length +
                " imagesEnabledActive=" + en);
        }
    }

    private static void DumpLegacyNames(StringBuilder sb)
    {
        sb.AppendLine("--- InnerLayer / legacy board names ---");
        Transform[] allT = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allT.Length; i++)
        {
            Transform tr = allT[i];
            if (tr == null)
            {
                continue;
            }

            string n = tr.name;
            bool hit =
                n == "InnerLayer" ||
                n.Contains("BoardUI") ||
                n.Contains("UIBoard") ||
                n == "Board" ||
                n.Contains("BlockView") ||
                n.Contains("TargetView");
            if (!hit)
            {
                continue;
            }

            Image img = tr.GetComponent<Image>();
            SpriteRenderer sr = tr.GetComponent<SpriteRenderer>();
            sb.AppendLine(
                "HIT name=" + n +
                " active=" + tr.gameObject.activeInHierarchy +
                " imgEn=" + (img != null && img.enabled) +
                " srEn=" + (sr != null && sr.enabled) +
                " path=" + PathOf(tr));
        }
    }

    private static void DumpBlockTargetImages(StringBuilder sb)
    {
        int blockImgEn = 0;
        int targetImgEn = 0;
        int greenDiaImg = 0;
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block b = blocks[i];
            if (b == null)
            {
                continue;
            }

            Image[] imgs = b.GetComponentsInChildren<Image>(true);
            for (int j = 0; j < imgs.Length; j++)
            {
                Image img = imgs[j];
                if (img == null || !img.enabled || !img.gameObject.activeInHierarchy)
                {
                    continue;
                }

                blockImgEn++;
                Color c = img.color;
                string sn = img.sprite != null ? img.sprite.name : "null";
                bool green = c.g > c.r + 0.1f && c.g > 0.4f;
                if (green || sn.ToLower().Contains("diamond"))
                {
                    if (green)
                    {
                        greenDiaImg++;
                    }

                    sb.AppendLine(
                        "BLOCK_IMG col=" + c + " sprite=" + sn + " path=" + PathOf(img.transform));
                }
            }
        }

        Target[] targets = Object.FindObjectsByType<Target>(FindObjectsSortMode.None);
        for (int i = 0; i < targets.Length; i++)
        {
            Target tg = targets[i];
            if (tg == null)
            {
                continue;
            }

            Image[] imgs = tg.GetComponentsInChildren<Image>(true);
            for (int j = 0; j < imgs.Length; j++)
            {
                Image img = imgs[j];
                if (img == null || !img.enabled || !img.gameObject.activeInHierarchy)
                {
                    continue;
                }

                targetImgEn++;
                Color c = img.color;
                if (c.g > c.r + 0.1f && c.g > 0.4f)
                {
                    sb.AppendLine(
                        "TARGET_IMG green col=" + c +
                        " sprite=" + (img.sprite != null ? img.sprite.name : "null") +
                        " path=" + PathOf(img.transform));
                }
            }
        }

        sb.AppendLine(
            "blockImagesEnabled=" + blockImgEn +
            " targetImagesEnabled=" + targetImgEn +
            " greenDiaImgs=" + greenDiaImg);
    }

    private static void DumpSprites(StringBuilder sb)
    {
        SpriteRenderer[] srs =
            Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int srEn = 0;
        for (int i = 0; i < srs.Length; i++)
        {
            if (srs[i] != null && srs[i].enabled && srs[i].gameObject.activeInHierarchy)
            {
                srEn++;
            }
        }

        sb.AppendLine("spriteRenderersTotal=" + srs.Length + " spriteRenderersEnabled=" + srEn);
    }

    private static void RunDisableBattery(StringBuilder sb)
    {
        sb.AppendLine("--- DISABLE TESTS (restored after) ---");

        GameObject board3d = GameObject.Find("Board3D");
        GameObject mainCamGo = null;
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "Main Camera")
            {
                mainCamGo = all[i].gameObject;
                break;
            }
        }

        GameObject boardCamGo = GameObject.Find("BoardCamera3D");
        Camera boardCam = boardCamGo != null ? boardCamGo.GetComponent<Camera>() : null;
        GameObject gameplayCanvas = null;
        Canvas[] cvs = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cvs.Length; i++)
        {
            if (cvs[i] != null && cvs[i].name == "GameplayCanvas")
            {
                gameplayCanvas = cvs[i].gameObject;
            }
        }

        MeshRenderer mr18 = null;
        MeshRenderer[] allMr =
            Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allMr.Length; i++)
        {
            MeshRenderer mr = allMr[i];
            if (mr == null || mr.name != "Mesh")
            {
                continue;
            }

            Vector3 p = mr.transform.position;
            if (Mathf.Abs(p.x + 0.832f) < 0.15f && Mathf.Abs(p.z - 4.723f) < 0.2f)
            {
                mr18 = mr;
            }
        }

        bool mainCamWas = mainCamGo != null && mainCamGo.activeSelf;
        bool boardCamWas = boardCamGo != null && boardCamGo.activeSelf;
        bool boardCamEnWas = boardCam != null && boardCam.enabled;
        bool gcWas = gameplayCanvas != null && gameplayCanvas.activeSelf;
        bool b3dWas = board3d != null && board3d.activeSelf;

        var overlayCvs = new List<Canvas>();
        var overlayWas = new List<bool>();
        for (int i = 0; i < cvs.Length; i++)
        {
            if (cvs[i] != null && cvs[i].renderMode == RenderMode.ScreenSpaceOverlay)
            {
                overlayCvs.Add(cvs[i]);
                overlayWas.Add(cvs[i].gameObject.activeSelf);
            }
        }

        CaptureStep(sb, "0_baseline", boardCam, boardCamGo, mr18);

        if (mainCamGo != null)
        {
            mainCamGo.SetActive(false);
        }

        CaptureStep(sb, "A_MainCamera_off", boardCam, boardCamGo, mr18);
        if (mainCamGo != null)
        {
            mainCamGo.SetActive(mainCamWas);
        }

        CaptureStep(sb, "A2_MainCamera_restored", boardCam, boardCamGo, mr18);

        if (boardCamGo != null)
        {
            boardCamGo.SetActive(false);
        }

        CaptureStep(sb, "B_BoardCamera_off", boardCam, boardCamGo, mr18);
        if (boardCamGo != null)
        {
            boardCamGo.SetActive(boardCamWas);
            if (boardCam != null)
            {
                boardCam.enabled = boardCamEnWas;
            }
        }

        CaptureStep(sb, "B2_BoardCamera_restored", boardCam, boardCamGo, mr18);

        if (gameplayCanvas != null)
        {
            gameplayCanvas.SetActive(false);
        }

        CaptureStep(sb, "C_GameplayCanvas_off", boardCam, boardCamGo, mr18);
        if (gameplayCanvas != null)
        {
            gameplayCanvas.SetActive(gcWas);
        }

        CaptureStep(sb, "C2_GameplayCanvas_restored", boardCam, boardCamGo, mr18);

        if (board3d != null)
        {
            board3d.SetActive(false);
        }

        CaptureStep(sb, "D_Board3D_off", boardCam, boardCamGo, mr18);
        if (board3d != null)
        {
            board3d.SetActive(b3dWas);
        }

        CaptureStep(sb, "D2_Board3D_restored", boardCam, boardCamGo, mr18);

        for (int i = 0; i < overlayCvs.Count; i++)
        {
            if (overlayCvs[i] != null)
            {
                overlayCvs[i].gameObject.SetActive(false);
            }
        }

        CaptureStep(sb, "E_allOverlayCanvases_off", boardCam, boardCamGo, mr18);
        for (int i = 0; i < overlayCvs.Count; i++)
        {
            if (overlayCvs[i] != null)
            {
                overlayCvs[i].gameObject.SetActive(overlayWas[i]);
            }
        }

        CaptureStep(sb, "E2_canvases_restored", boardCam, boardCamGo, mr18);

        // Final restore belt
        if (mainCamGo != null)
        {
            mainCamGo.SetActive(mainCamWas);
        }

        if (boardCamGo != null)
        {
            boardCamGo.SetActive(boardCamWas);
            if (boardCam != null)
            {
                boardCam.enabled = boardCamEnWas;
            }
        }

        if (gameplayCanvas != null)
        {
            gameplayCanvas.SetActive(gcWas);
        }

        if (board3d != null)
        {
            board3d.SetActive(b3dWas);
        }

        for (int i = 0; i < overlayCvs.Count; i++)
        {
            if (overlayCvs[i] != null)
            {
                overlayCvs[i].gameObject.SetActive(overlayWas[i]);
            }
        }

        sb.AppendLine("RESTORED all");
    }

    private static void CaptureStep(
        StringBuilder sb,
        string label,
        Camera boardCam,
        GameObject boardCamGo,
        MeshRenderer mr18)
    {
        // Force GameView RenderView(clear) then read RT — also BoardCam.Render for truth
        try
        {
            Assembly ass = typeof(EditorWindow).Assembly;
            System.Type gvType = ass.GetType("UnityEditor.GameView");
            MethodInfo renderView = gvType.GetMethod(
                "RenderView",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Object[] gvs = Resources.FindObjectsOfTypeAll(gvType);
            if (gvs.Length > 0 && renderView != null)
            {
                renderView.Invoke(gvs[0], new object[] { Vector2.zero, true });
            }
        }
        catch
        {
            // ignore
        }

        Assembly ass2 = typeof(EditorWindow).Assembly;
        System.Type pmvType = ass2.GetType("UnityEditor.PlayModeView");
        MethodInfo getMain = pmvType.GetMethod(
            "GetMainPlayModeView",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        object pmv = getMain.Invoke(null, null);
        FieldInfo fTex = pmvType.GetField(
            "m_TargetTexture",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var gvRtt = fTex.GetValue(pmv) as RenderTexture;

        int gcount = -1;
        int lime = -1;
        string sample = "";
        if (gvRtt != null && gvRtt.IsCreated())
        {
            var tex = new Texture2D(gvRtt.width, gvRtt.height, TextureFormat.RGB24, false);
            RenderTexture.active = gvRtt;
            tex.ReadPixels(new Rect(0, 0, gvRtt.width, gvRtt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(CapDir + "/72j-gv-" + label + ".png", tex.EncodeToPNG());

            gcount = 0;
            lime = 0;
            for (int y = 0; y < tex.height; y += 3)
            {
                for (int x = 0; x < tex.width; x += 3)
                {
                    Color c = tex.GetPixel(x, y);
                    if (c.g > 0.5f && c.g > c.r + 0.15f)
                    {
                        gcount++;
                    }

                    if (c.g > 0.7f && c.g > c.r + 0.35f && c.b < 0.65f)
                    {
                        lime++;
                    }
                }
            }

            if (boardCam != null && mr18 != null && boardCamGo != null && boardCamGo.activeInHierarchy)
            {
                Vector3 vp = boardCam.WorldToViewportPoint(mr18.transform.position);
                int px = Mathf.Clamp(Mathf.RoundToInt(vp.x * (gvRtt.width - 1)), 0, gvRtt.width - 1);
                int pyf = gvRtt.height - 1 -
                          Mathf.Clamp(Mathf.RoundToInt(vp.y * (gvRtt.height - 1)), 0, gvRtt.height - 1);
                sample = " sampleFlip=" + tex.GetPixel(px, pyf);
            }

            Object.DestroyImmediate(tex);
        }

        sb.AppendLine(
            "TEST " + label +
            " greenish=" + gcount +
            " limeStrong=" + lime +
            sample);

        if (boardCam != null && boardCamGo != null && boardCamGo.activeInHierarchy && boardCam.enabled)
        {
            const int w = 540;
            const int h = 960;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            boardCam.targetTexture = rt;
            boardCam.Render();
            boardCam.targetTexture = null;
            var t2 = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            t2.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t2.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(CapDir + "/72j-bc-" + label + ".png", t2.EncodeToPNG());
            if (mr18 != null)
            {
                Vector3 vp = boardCam.WorldToViewportPoint(mr18.transform.position);
                int px = Mathf.Clamp(Mathf.RoundToInt(vp.x * (w - 1)), 0, w - 1);
                int py = Mathf.Clamp(Mathf.RoundToInt(vp.y * (h - 1)), 0, h - 1);
                sb.AppendLine("  BC " + label + " (1,8)=" + t2.GetPixel(px, py));
            }

            Object.DestroyImmediate(t2);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
        else
        {
            sb.AppendLine("  BC " + label + " SKIPPED (BoardCamera inactive)");
        }
    }
}
