using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Phase 72I — same-frame Game view RTT vs BoardCamera3D.Render() capture.
/// Does not change gameplay / cleanup. Menu while green is visible in Game view.
/// </summary>
public static class Phase72ISameFrameCapture
{
    private const string CapDir = "Captures";
    private const string ReportPath = "Captures/72i-live-report.txt";

    [MenuItem("Shape Nest/Phase 72I Capture Game vs BoardCam NOW")]
    public static void CaptureNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[72I] Play Mode required (leave Play running; do not stop).");
            return;
        }

        string report = CaptureInternal(stepRefresh: false);
        Debug.Log("[72I]\n" + report);
    }

    [MenuItem("Shape Nest/Phase 72I Capture + Step Refresh")]
    public static void CaptureThenStep()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[72I] Play Mode required.");
            return;
        }

        string before = CaptureInternal(stepRefresh: false);
        bool wasPaused = EditorApplication.isPaused;
        EditorApplication.isPaused = true;
        EditorApplication.Step();
        EditorApplication.Step();
        string after = CaptureInternal(stepRefresh: true);
        EditorApplication.isPaused = wasPaused;

        string combined = before + "\n--- AFTER TWO STEPS ---\n" + after;
        File.WriteAllText(ReportPath, combined);
        Debug.Log("[72I] before+after steps wrote " + ReportPath);
    }

    private static string CaptureInternal(bool stepRefresh)
    {
        var sb = new StringBuilder(16000);
        string tag = stepRefresh ? "after_steps" : "now";
        sb.AppendLine("PHASE 72I GAME vs BOARDCAM (" + tag + ")");
        sb.AppendLine(
            "frame=" + Time.frameCount +
            " t=" + Time.time.ToString("F3") +
            " paused=" + EditorApplication.isPaused +
            " timeScale=" + Time.timeScale);

        Directory.CreateDirectory(CapDir);
        Camera cam = null;
        var camGo = GameObject.Find("BoardCamera3D");
        if (camGo != null)
        {
            cam = camGo.GetComponent<Camera>();
        }

        if (cam == null)
        {
            sb.AppendLine("NO_BOARDCAM");
            File.WriteAllText(ReportPath, sb.ToString());
            return sb.ToString();
        }

        MeshRenderer mr17 = null;
        MeshRenderer mr18 = null;
        FindSurvivorMeshes(out mr17, out mr18);
        sb.AppendLine("mr17=" + DescribeMr(mr17));
        sb.AppendLine("mr18=" + DescribeMr(mr18));

        DumpCameras(sb);

        // BoardCamera.Render
        CaptureBoardCam(cam, mr17, mr18, CapDir + "/72i-bc-" + tag + ".png", sb);

        // Game view RTT
        CaptureGameView(cam, mr17, mr18, CapDir + "/72i-gv-" + tag + ".png", sb);

        File.WriteAllText(ReportPath, sb.ToString());
        sb.AppendLine("wrote " + ReportPath);
        return sb.ToString();
    }

    private static void FindSurvivorMeshes(out MeshRenderer mr17, out MeshRenderer mr18)
    {
        mr17 = null;
        mr18 = null;
        MeshRenderer[] all = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            MeshRenderer mr = all[i];
            if (mr == null || mr.name != "Mesh")
            {
                continue;
            }

            Vector3 p = mr.transform.position;
            if (Mathf.Abs(p.x + 0.832f) < 0.12f && Mathf.Abs(p.z - 4.169f) < 0.15f)
            {
                mr17 = mr;
            }

            if (Mathf.Abs(p.x + 0.832f) < 0.12f && Mathf.Abs(p.z - 4.723f) < 0.15f)
            {
                mr18 = mr;
            }
        }
    }

    private static string DescribeMr(MeshRenderer mr)
    {
        if (mr == null)
        {
            return "null";
        }

        Color baseCol = Color.clear;
        if (mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_BaseColor"))
        {
            baseCol = mr.sharedMaterial.GetColor("_BaseColor");
        }

        return "rid=" + mr.GetInstanceID() +
               " en=" + mr.enabled +
               " mat=" + (mr.sharedMaterial != null ? mr.sharedMaterial.name : "null") +
               " base=" + baseCol;
    }

    private static void DumpCameras(StringBuilder sb)
    {
        sb.AppendLine("--- CAMERAS ---");
        Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cams.Length; i++)
        {
            Camera c = cams[i];
            var uacd = c.GetComponent<UniversalAdditionalCameraData>();
            int stack = uacd != null ? uacd.cameraStack.Count : -1;
            sb.AppendLine(
                "cam=" + c.name +
                " active=" + c.gameObject.activeInHierarchy +
                " en=" + c.enabled +
                " post=" + (uacd != null && uacd.renderPostProcessing) +
                " type=" + (uacd != null ? uacd.renderType.ToString() : "?") +
                " stack=" + stack);
        }
    }

    private static void CaptureBoardCam(
        Camera cam,
        MeshRenderer mr17,
        MeshRenderer mr18,
        string path,
        StringBuilder sb)
    {
        const int w = 540;
        const int h = 960;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        RenderTexture prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;

        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        File.WriteAllBytes(path, tex.EncodeToPNG());
        sb.AppendLine("wrote " + path);
        SampleAt(cam, tex, mr17, "(1,7)", sb, flipY: false);
        SampleAt(cam, tex, mr18, "(1,8)", sb, flipY: false);

        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    private static void CaptureGameView(
        Camera cam,
        MeshRenderer mr17,
        MeshRenderer mr18,
        string path,
        StringBuilder sb)
    {
        Assembly ass = typeof(EditorWindow).Assembly;
        System.Type pmvType = ass.GetType("UnityEditor.PlayModeView");
        if (pmvType == null)
        {
            sb.AppendLine("NO_PlayModeView_type");
            return;
        }

        MethodInfo getMain = pmvType.GetMethod(
            "GetMainPlayModeView",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        object pmv = getMain != null ? getMain.Invoke(null, null) : null;
        if (pmv == null)
        {
            sb.AppendLine("NO_PlayModeView");
            return;
        }

        FieldInfo fTex = pmvType.GetField(
            "m_TargetTexture",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (fTex == null)
        {
            sb.AppendLine("NO_m_TargetTexture");
            return;
        }

        var rtt = fTex.GetValue(pmv) as RenderTexture;
        if (rtt == null || !rtt.IsCreated())
        {
            sb.AppendLine("GameRTT missing/created=" + (rtt != null && rtt.IsCreated()));
            return;
        }

        var tex = new Texture2D(rtt.width, rtt.height, TextureFormat.RGB24, false);
        RenderTexture.active = rtt;
        tex.ReadPixels(new Rect(0, 0, rtt.width, rtt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        File.WriteAllBytes(path, tex.EncodeToPNG());
        sb.AppendLine("wrote " + path + " " + rtt.width + "x" + rtt.height);

        // Game view RT is typically top-left origin vs Camera bottom-left — sample both.
        SampleAtViewport(tex, cam, mr17, "GV(1,7)", sb, flipY: true);
        SampleAtViewport(tex, cam, mr18, "GV(1,8)", sb, flipY: true);

        int gcount = 0;
        for (int y = 0; y < tex.height; y += 3)
        {
            for (int x = 0; x < tex.width; x += 3)
            {
                Color c = tex.GetPixel(x, y);
                if (c.g > 0.5f && c.g > c.r + 0.15f)
                {
                    gcount++;
                }
            }
        }

        sb.AppendLine("GV greenishPixelSamples=" + gcount);
        Object.DestroyImmediate(tex);
    }

    private static void SampleAt(
        Camera cam,
        Texture2D tex,
        MeshRenderer mr,
        string label,
        StringBuilder sb,
        bool flipY)
    {
        if (mr == null)
        {
            sb.AppendLine(label + "=null");
            return;
        }

        Vector3 vp = cam.WorldToViewportPoint(mr.transform.position);
        int px = Mathf.Clamp(Mathf.RoundToInt(vp.x * (tex.width - 1)), 0, tex.width - 1);
        int py = Mathf.Clamp(Mathf.RoundToInt(vp.y * (tex.height - 1)), 0, tex.height - 1);
        if (flipY)
        {
            py = tex.height - 1 - py;
        }

        sb.AppendLine("BC " + label + " rgb=" + tex.GetPixel(px, py));
    }

    private static void SampleAtViewport(
        Texture2D tex,
        Camera cam,
        MeshRenderer mr,
        string label,
        StringBuilder sb,
        bool flipY)
    {
        if (mr == null)
        {
            sb.AppendLine(label + "=null");
            return;
        }

        Vector3 vp = cam.WorldToViewportPoint(mr.transform.position);
        int px = Mathf.Clamp(Mathf.RoundToInt(vp.x * (tex.width - 1)), 0, tex.width - 1);
        int py = Mathf.Clamp(Mathf.RoundToInt(vp.y * (tex.height - 1)), 0, tex.height - 1);
        int pyf = tex.height - 1 - py;
        sb.AppendLine(
            label +
            " rgb=" + tex.GetPixel(px, py) +
            " flip=" + tex.GetPixel(px, pyf));
    }
}
