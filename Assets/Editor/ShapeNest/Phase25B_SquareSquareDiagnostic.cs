#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TEMPORARY Play Mode diagnostic. Isolated from production gameplay.
/// Loads DIAGNOSTIC_SquareSquare and captures Square block → Square nest at T0–T4.
/// Does not change movement, matching, presentation, camera, or GridSpace3D.
/// </summary>
public static class Phase25B_SquareSquareDiagnostic
{
    const string LevelPath = "Assets/Levels/Diagnostic/DIAGNOSTIC_SquareSquare.asset";
    const string ReportPath = "Assets/Levels/Diagnostic/PHASE25B_LAST_CAPTURE.txt";
    const string SessionKey = "Phase25B_Report";
    const string StatusKey = "Phase25B_Status";

    static bool running;
    static bool sampling;
    static int phase;
    static float startRealtime;
    static float dragAtRealtime;
    static bool sawNestLock;
    static string t0;
    static string t1;
    static string t2LockedLast;
    static string t3;
    static string t4;
    static string cameraDump;
    static Vector3 t0BlockPos;
    static bool haveT0Pos;
    static int blockId;
    static int targetId;

    [MenuItem("Tools/Shape Nest/Diagnostics/Phase 25B Square Square Capture")]
    public static void RunFromMenu()
    {
        Begin();
    }

    public static string Status()
    {
        return SessionState.GetString(StatusKey, "idle") + " running=" + running + " phase=" + phase;
    }

    public static string Report()
    {
        return SessionState.GetString(SessionKey, "");
    }

    public static void Begin()
    {
        StopInternal(false);
        SessionState.SetString(StatusKey, "starting");
        SessionState.SetString(SessionKey, "");
        running = true;
        sampling = false;
        phase = 0;
        startRealtime = Time.realtimeSinceStartup;
        dragAtRealtime = 0f;
        sawNestLock = false;
        t0 = t1 = t2LockedLast = t3 = t4 = cameraDump = "";
        haveT0Pos = false;
        blockId = 0;
        targetId = 0;

        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Camera.onPreCull -= OnPreCull;

        if (!EditorApplication.isPlaying)
        {
            SessionState.SetString(StatusKey, "waiting-for-play");
            EditorApplication.isPlaying = true;
            return;
        }

        EditorApplication.isPaused = true;
        SessionState.SetString(StatusKey, "pumping-paused-steps");
    }

    static void StopInternal(bool writeIfPartial)
    {
        EditorApplication.update -= Tick;
        Camera.onPreCull -= OnPreCull;
        running = false;
        if (writeIfPartial)
        {
            Finish("stopped");
        }
    }

    static void Tick()
    {
        if (!running)
        {
            return;
        }

        if (EditorApplication.isCompiling)
        {
            return;
        }

        if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
        {
            if (Time.realtimeSinceStartup - startRealtime > 20f)
            {
                Fail("Play Mode did not stay active.");
            }

            return;
        }

        if (!EditorApplication.isPaused)
        {
            EditorApplication.isPaused = true;
        }

        EditorApplication.Step();
        Sample();
    }

    static void OnPreCull(Camera cam)
    {
        // Player loop is pumped via paused Step() in Tick. PreCull is unused.
    }

    static void Sample()
    {
        if (sampling)
        {
            return;
        }

        sampling = true;
        try
        {
            SampleBody();
        }
        finally
        {
            sampling = false;
        }
    }

    static void SampleBody()
    {
        LevelManager lm = UnityEngine.Object.FindFirstObjectByType<LevelManager>();
        if (lm == null)
        {
            if (Time.realtimeSinceStartup - startRealtime > 8f)
            {
                Fail("LevelManager not found.");
            }

            return;
        }

        if (phase == 0)
        {
            LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath);
            if (level == null)
            {
                Fail("Missing " + LevelPath);
                return;
            }

            lm.LoadLevel(level);
            phase = 1;
            SessionState.SetString(StatusKey, "level-loaded");
            return;
        }

        Block[] blocks = UnityEngine.Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        Target[] targets = UnityEngine.Object.FindObjectsByType<Target>(FindObjectsSortMode.None);
        if (blocks.Length != 1 || targets.Length != 1)
        {
            if (Time.realtimeSinceStartup - startRealtime > 10f)
            {
                Fail("Expected 1 block and 1 target, got blocks=" + blocks.Length + " targets=" + targets.Length);
            }

            return;
        }

        Block block = blocks[0];
        Target target = targets[0];
        if (block.ShapeType != ShapeType.Square || target.ShapeType != ShapeType.Square)
        {
            Fail("Not Square→Square. block=" + block.ShapeType + " target=" + target.ShapeType);
            return;
        }

        if (block.CellCount != 1 || target.CellCount != 1)
        {
            Fail("Expected 1x1 pieces. blockCells=" + block.CellCount + " targetCells=" + target.CellCount);
            return;
        }

        blockId = block.GetInstanceID();
        targetId = target.GetInstanceID();

        if (phase == 1)
        {
            if (block.WorldView == null || target.WorldView == null)
            {
                if (Time.time > 8f)
                {
                    Fail("WorldView not bound. blockWV=" + (block.WorldView != null) + " targetWV=" + (target.WorldView != null) + " frame=" + Time.frameCount);
                }

                return;
            }

            cameraDump = DumpCamera();
            t0 = Snapshot("T0_BeforeMovement", block, target);
            t0BlockPos = block.WorldView.transform.position;
            haveT0Pos = true;
            phase = 2;
            dragAtRealtime = Time.realtimeSinceStartup;
            SessionState.SetString(StatusKey, "t0-captured");
            return;
        }

        if (phase == 2)
        {
            if (Time.time < 0.15f)
            {
                return;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            if (mover == null)
            {
                Fail("BlockMover missing.");
                return;
            }

            if (!lm.IsPieceInputAllowed)
            {
                if (Time.realtimeSinceStartup - startRealtime > 15f)
                {
                    Fail("Piece input not allowed. session=" + lm.Session);
                }

                return;
            }

            Vector2Int dir = target.GridPosition - block.GridPosition;
            Vector2Int cardinal = Vector2Int.zero;
            if (Mathf.Abs(dir.y) >= Mathf.Abs(dir.x))
            {
                cardinal = dir.y < 0 ? Vector2Int.down : Vector2Int.up;
            }
            else
            {
                cardinal = dir.x < 0 ? Vector2Int.left : Vector2Int.right;
            }

            if (!mover.TryBeginDrag(cardinal))
            {
                Fail("TryBeginDrag failed dir=" + cardinal + " settled=" + block.IsSettled);
                return;
            }

            mover.SetDragRequest(target.GridPosition);
            phase = 3;
            SessionState.SetString(StatusKey, "drag-started");
            return;
        }

        if (phase >= 3)
        {
            BlockMover mover = block.GetComponent<BlockMover>();
            bool locked = block.WorldView != null && block.WorldView.IsMotionLocked;
            bool moving = mover != null && (mover.IsMoving || mover.IsDragging);
            Vector3 pos = block.WorldView != null ? block.WorldView.transform.position : Vector3.zero;
            bool movedFromStart = haveT0Pos && (pos - t0BlockPos).sqrMagnitude > 0.0001f;

            if (string.IsNullOrEmpty(t1) && moving && movedFromStart && !block.IsMatchPresentationActive)
            {
                t1 = Snapshot("T1_DuringMovement", block, target);
                SessionState.SetString(StatusKey, "t1-captured");
            }

            bool nearNest = false;
            if (block.WorldView != null && target.WorldView != null)
            {
                Vector3 bp = block.WorldView.transform.position;
                Vector3 tp = target.WorldView.transform.position;
                float dxz = new Vector2(bp.x - tp.x, bp.z - tp.z).magnitude;
                nearNest = dxz < 0.75f;
            }

            if (locked && nearNest)
            {
                sawNestLock = true;
                t2LockedLast = Snapshot("T2_NestEntryCompletion", block, target);
            }

            if (sawNestLock && !locked && string.IsNullOrEmpty(t3))
            {
                if (string.IsNullOrEmpty(t2LockedLast))
                {
                    t2LockedLast = Snapshot("T2_NestEntryCompletion", block, target);
                }

                t3 = Snapshot("T3_ImmediatelyAfterNestEntryReturns", block, target);
                SessionState.SetString(StatusKey, "t3-captured");
            }

            if (block.IsMatchPresentationActive && string.IsNullOrEmpty(t4))
            {
                if (string.IsNullOrEmpty(t2LockedLast))
                {
                    t2LockedLast = Snapshot("T2_NestEntryCompletion", block, target);
                }

                if (string.IsNullOrEmpty(t3))
                {
                    t3 = Snapshot("T3_ImmediatelyAfterNestEntryReturns", block, target);
                }

                t4 = Snapshot("T4_ImmediatelyBeforeMatchDissolve", block, target);
                Finish("complete");
            }

            if (Time.time > 20f)
            {
                Fail("Timed out waiting for nest entry / match. t1=" + !string.IsNullOrEmpty(t1)
                    + " t2=" + !string.IsNullOrEmpty(t2LockedLast)
                    + " t3=" + !string.IsNullOrEmpty(t3)
                    + " t4=" + !string.IsNullOrEmpty(t4)
                    + " moving=" + moving
                    + " locked=" + locked
                    + " match=" + block.IsMatchPresentationActive
                    + " gridB=" + block.GridPosition
                    + " gridT=" + target.GridPosition);
            }
        }
    }

    static void Fail(string reason)
    {
        SessionState.SetString(StatusKey, "failed: " + reason);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("PHASE 25B FAILED");
        sb.AppendLine(reason);
        sb.AppendLine("partial T0=" + !string.IsNullOrEmpty(t0) + " T1=" + !string.IsNullOrEmpty(t1)
            + " T2=" + !string.IsNullOrEmpty(t2LockedLast) + " T3=" + !string.IsNullOrEmpty(t3)
            + " T4=" + !string.IsNullOrEmpty(t4));
        sb.AppendLine(t0);
        sb.AppendLine(t1);
        sb.AppendLine(t2LockedLast);
        sb.AppendLine(t3);
        sb.AppendLine(t4);
        WriteReport(sb.ToString());
        running = false;
        EditorApplication.update -= Tick;
        Camera.onPreCull -= OnPreCull;
        Debug.LogError(sb.ToString());
    }

    static void Finish(string status)
    {
        running = false;
        EditorApplication.update -= Tick;
        Camera.onPreCull -= OnPreCull;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("PHASE 25B LIVE CAPTURE — Square Block → Square Target");
        sb.AppendLine("status=" + status);
        sb.AppendLine("level=" + LevelPath);
        sb.AppendLine("isolated=true (not in LevelDatabase)");
        sb.AppendLine("blockInstanceId=" + blockId);
        sb.AppendLine("targetInstanceId=" + targetId);
        sb.AppendLine("frameT3EqualsT4_check_see_positions_below");
        sb.AppendLine();
        sb.AppendLine(cameraDump);
        sb.AppendLine(t0);
        sb.AppendLine(t1);
        sb.AppendLine(t2LockedLast);
        sb.AppendLine(t3);
        sb.AppendLine(t4);
        if (!string.IsNullOrEmpty(t3) && !string.IsNullOrEmpty(t4))
        {
            sb.AppendLine("=== T3 vs T4 same-sample? ===");
            sb.AppendLine("T3_len=" + t3.Length + " T4_len=" + t4.Length);
        }

        WriteReport(sb.ToString());
        SessionState.SetString(StatusKey, status);
        Debug.Log(sb.ToString());
    }

    static void WriteReport(string text)
    {
        SessionState.SetString(SessionKey, text);
        try
        {
            File.WriteAllText(ReportPath, text);
            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Phase25B could not write report file: " + ex.Message);
        }
    }

    static string DumpCamera()
    {
        BoardCamera3D boardCam = UnityEngine.Object.FindFirstObjectByType<BoardCamera3D>();
        Camera cam = boardCam != null ? boardCam.Camera : Camera.main;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== CAMERA ===");
        if (cam == null)
        {
            sb.AppendLine("camera=null");
            return sb.ToString();
        }

        sb.AppendLine("path=" + PathOf(cam.transform));
        sb.AppendLine("position=" + F3(cam.transform.position));
        sb.AppendLine("rotationEuler=" + F3(cam.transform.rotation.eulerAngles));
        sb.AppendLine("orthographic=" + cam.orthographic);
        sb.AppendLine("orthographicSize=" + F(cam.orthographicSize));
        sb.AppendLine("near=" + F(cam.nearClipPlane) + " far=" + F(cam.farClipPlane));
        BoardPresenter3D presenter = UnityEngine.Object.FindFirstObjectByType<BoardPresenter3D>();
        if (presenter != null)
        {
            sb.AppendLine("CellWorldSize=" + F(presenter.CellWorldSize));
            sb.AppendLine("Board3D.position=" + F3(presenter.transform.position));
            sb.AppendLine("Board3D.rotationEuler=" + F3(presenter.transform.rotation.eulerAngles));
            sb.AppendLine("Board3D.lossyScale=" + F3(presenter.transform.lossyScale));
        }

        return sb.ToString();
    }

    static string Snapshot(string label, Block block, Target target)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========== " + label + " frame=" + Time.frameCount + " t=" + F(Time.time) + " ==========");
        BoardPresenter3D presenter = UnityEngine.Object.FindFirstObjectByType<BoardPresenter3D>();
        IGridSpace worldSpace = presenter != null ? presenter.GridSpace : null;
        BoardManager board = block.Board;
        Camera cam = null;
        BoardCamera3D boardCam = UnityEngine.Object.FindFirstObjectByType<BoardCamera3D>();
        if (boardCam != null)
        {
            cam = boardCam.Camera;
        }

        Vector3 gtwBlock = worldSpace != null ? worldSpace.GridToWorld(block.GridPosition) : Vector3.zero;
        Vector3 gtwTarget = worldSpace != null ? worldSpace.GridToWorld(target.GridPosition) : Vector3.zero;
        Vector3 uiLocalBlock = board != null ? board.GridToLocal(block.GridPosition) : Vector3.zero;
        Vector3 uiLocalTarget = board != null ? board.GridToLocal(target.GridPosition) : Vector3.zero;

        sb.AppendLine("--- BLOCK ---");
        DumpPiece(sb, block.gameObject, block.GetInstanceID(), block.GridPosition, block.WorldView, block.RectTransform, uiLocalBlock);
        sb.AppendLine("IsMoving=" + (block.GetComponent<BlockMover>() != null && block.GetComponent<BlockMover>().IsMoving));
        sb.AppendLine("IsDragging=" + (block.GetComponent<BlockMover>() != null && block.GetComponent<BlockMover>().IsDragging));
        sb.AppendLine("IsMatchPresentationActive=" + block.IsMatchPresentationActive);
        sb.AppendLine("IsMatched=" + block.IsMatched);
        sb.AppendLine("WorldView.IsMotionLocked=" + (block.WorldView != null && block.WorldView.IsMotionLocked));
        sb.AppendLine("GridToWorld(blockCell)=" + F3(gtwBlock));
        sb.AppendLine("UI GridToLocal(blockCell)=" + F3(uiLocalBlock));
        sb.AppendLine("UIGridSpace type=" + (board != null && board.GridSpace != null ? board.GridSpace.GetType().Name : "null"));

        sb.AppendLine("--- TARGET ---");
        DumpPiece(sb, target.gameObject, target.GetInstanceID(), target.GridPosition, target.WorldView, target.RectTransform, uiLocalTarget);
        sb.AppendLine("IsMatchPresentationActive=" + target.IsMatchPresentationActive);
        sb.AppendLine("IsMatched=" + target.IsMatched);
        sb.AppendLine("GridToWorld(targetCell)=" + F3(gtwTarget));
        sb.AppendLine("UI GridToLocal(targetCell)=" + F3(uiLocalTarget));

        sb.AppendLine("--- GRID DELTAS ---");
        sb.AppendLine("logicalCellEqual=" + (block.GridPosition == target.GridPosition));
        sb.AppendLine("GridToWorld(block)-GridToWorld(target)=" + F3(gtwBlock - gtwTarget));
        Vector2 gtwXz = new Vector2(gtwBlock.x - gtwTarget.x, gtwBlock.z - gtwTarget.z);
        sb.AppendLine("GridToWorld XZ delta=" + F2(gtwXz));

        PieceView3D bw = block.WorldView;
        PieceView3D tw = target.WorldView;
        if (bw != null && tw != null)
        {
            Vector3 bp = bw.transform.position;
            Vector3 tp = tw.transform.position;
            sb.AppendLine("1 WorldView.position delta=" + F3(bp - tp));
            sb.AppendLine("WorldView XZ delta=" + F2(new Vector2(bp.x - tp.x, bp.z - tp.z)));

            MeshRenderer bmr = bw.GetComponentInChildren<MeshRenderer>();
            MeshRenderer tmr = tw.GetComponentInChildren<MeshRenderer>();
            if (bmr != null && tmr != null)
            {
                Bounds bb = bmr.bounds;
                Bounds tb = tmr.bounds;
                sb.AppendLine("2 bounds.center delta=" + F3(bb.center - tb.center));
                sb.AppendLine("3 block.bounds.min.y - target.bounds.max.y=" + F(bb.min.y - tb.max.y));
                sb.AppendLine("4 block.bounds.max.y - target.bounds.max.y=" + F(bb.max.y - tb.max.y));
                sb.AppendLine("5 block.bounds.center.y - target.bounds.center.y=" + F(bb.center.y - tb.center.y));
                sb.AppendLine("6 bounds XZ center delta=" + F2(new Vector2(bb.center.x - tb.center.x, bb.center.z - tb.center.z)));

                if (cam != null)
                {
                    Vector3 bScreen = cam.WorldToScreenPoint(bb.center);
                    Vector3 tScreen = cam.WorldToScreenPoint(tb.center);
                    Vector3 bMinScreen = cam.WorldToScreenPoint(bb.min);
                    Vector3 tMaxScreen = cam.WorldToScreenPoint(tb.max);
                    sb.AppendLine("screen block.center=" + F3(bScreen));
                    sb.AppendLine("screen target.center=" + F3(tScreen));
                    sb.AppendLine("screen center delta (block-target)=" + F3(bScreen - tScreen));
                    sb.AppendLine("screen block.bounds.min=" + F3(bMinScreen));
                    sb.AppendLine("screen target.bounds.max=" + F3(tMaxScreen));
                    sb.AppendLine("screen (block.min - target.max)=" + F3(bMinScreen - tMaxScreen));
                    sb.AppendLine("NOTE: screen Y increases upward in WorldToScreenPoint.");
                }
            }
        }

        return sb.ToString();
    }

    static void DumpPiece(
        StringBuilder sb,
        GameObject logical,
        int id,
        Vector2Int grid,
        PieceView3D wv,
        RectTransform uiRect,
        Vector3 uiGridLocal)
    {
        sb.AppendLine("logicalPath=" + PathOf(logical.transform));
        sb.AppendLine("instanceId=" + id);
        sb.AppendLine("gridPosition=" + grid);
        if (uiRect != null)
        {
            sb.AppendLine("UI.anchoredPosition=" + F2(uiRect.anchoredPosition));
            sb.AppendLine("UI.localPosition=" + F3(uiRect.localPosition));
            sb.AppendLine("UI.position=" + F3(uiRect.position));
            sb.AppendLine("UI.active=" + uiRect.gameObject.activeSelf + " inHierarchy=" + uiRect.gameObject.activeInHierarchy);
        }

        Image[] images = logical.GetComponentsInChildren<Image>(true);
        int enabledImages = 0;
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && images[i].enabled && images[i].gameObject.activeInHierarchy)
            {
                enabledImages++;
            }
        }

        sb.AppendLine("enabled UI Images in hierarchy=" + enabledImages + " / " + images.Length);
        sb.AppendLine("UIGridToLocal vs anchored delta=" + (uiRect != null ? F2(uiRect.anchoredPosition - new Vector2(uiGridLocal.x, uiGridLocal.y)) : "n/a"));

        if (wv == null)
        {
            sb.AppendLine("WorldView=null");
            return;
        }

        Transform t = wv.transform;
        sb.AppendLine("worldPath=" + PathOf(t));
        sb.AppendLine("WorldView.position=" + F3(t.position));
        sb.AppendLine("WorldView.localPosition=" + F3(t.localPosition));
        sb.AppendLine("WorldView.localScale=" + F3(t.localScale));
        sb.AppendLine("WorldView.lossyScale=" + F3(t.lossyScale));
        sb.AppendLine("WorldView.rotationEuler=" + F3(t.rotation.eulerAngles));
        sb.AppendLine("WorldView.activeSelf=" + wv.gameObject.activeSelf + " activeInHierarchy=" + wv.gameObject.activeInHierarchy);
        sb.AppendLine("ConfiguredShape=" + wv.ConfiguredShape + " ConfiguredAsNest=" + wv.ConfiguredAsNest);
        if (t.parent != null)
        {
            sb.AppendLine("parent.path=" + PathOf(t.parent));
            sb.AppendLine("parent.position=" + F3(t.parent.position));
            sb.AppendLine("parent.rotationEuler=" + F3(t.parent.rotation.eulerAngles));
            sb.AppendLine("parent.localScale=" + F3(t.parent.localScale));
            sb.AppendLine("parent.lossyScale=" + F3(t.parent.lossyScale));
        }

        MeshRenderer mr = wv.GetComponentInChildren<MeshRenderer>();
        MeshFilter mf = wv.GetComponentInChildren<MeshFilter>();
        if (mr != null)
        {
            Bounds b = mr.bounds;
            sb.AppendLine("MeshRenderer.enabled=" + mr.enabled);
            sb.AppendLine("MeshRenderer.gameObject.activeSelf=" + mr.gameObject.activeSelf);
            sb.AppendLine("MeshRenderer.bounds.center=" + F3(b.center));
            sb.AppendLine("MeshRenderer.bounds.min=" + F3(b.min));
            sb.AppendLine("MeshRenderer.bounds.max=" + F3(b.max));
            sb.AppendLine("MeshRenderer.bounds.size=" + F3(b.size));
        }
        else
        {
            sb.AppendLine("MeshRenderer=null");
        }

        if (mf != null && mf.sharedMesh != null)
        {
            Bounds mb = mf.sharedMesh.bounds;
            sb.AppendLine("mesh.bounds.center=" + F3(mb.center));
            sb.AppendLine("mesh.bounds.size=" + F3(mb.size));
            sb.AppendLine("mesh.name=" + mf.sharedMesh.name);
        }
        else
        {
            sb.AppendLine("sharedMesh=null");
        }
    }

    static string PathOf(Transform t)
    {
        if (t == null)
        {
            return "null";
        }

        string p = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            p = t.name + "/" + p;
        }

        return p;
    }

    static string F(float v)
    {
        return v.ToString("F5", CultureInfo.InvariantCulture);
    }

    static string F2(Vector2 v)
    {
        return "(" + F(v.x) + ", " + F(v.y) + ")";
    }

    static string F3(Vector3 v)
    {
        return "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
    }
}
#endif
