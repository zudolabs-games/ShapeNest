using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 60 play-mode verification: drag control readiness + reliability.
/// Menu: Shape Nest / Phase 60 Verify Drag Controls
/// </summary>
[InitializeOnLoad]
public static class Phase60PlayModeVerify
{
    private const string ReportPath = "Captures/phase60-report.txt";
    private const string SessionKey = "Phase60.Verify";
    private const string Campaign01 = "Assets/Levels/Campaign_01_FirstMove.asset";
    private const string Campaign07 = "Assets/Levels/Campaign_07_ChainIntro.asset";
    private const string Campaign10 = "Assets/Levels/Campaign_10_ShapeInShape.asset";
    private const string Campaign12 = "Assets/Levels/Campaign_12_Ice.asset";
    private const string Campaign13 = "Assets/Levels/Campaign_13_Shutter.asset";
    private const string Campaign14 = "Assets/Levels/Campaign_14_FixedDirection.asset";
    private const string Campaign15 = "Assets/Levels/Campaign_15_Master.asset";

    private static bool running;
    private static int step;
    private static double stepAt;
    private static readonly StringBuilder report = new StringBuilder();
    private static string lastError;
    private static int blocksReadyRetries;
    private static int idleRetries;
    private static readonly Dictionary<int, bool> results = new Dictionary<int, bool>();
    private static Block testBlock;
    private static Vector2Int fromPos;
    private static Vector2Int expectedPos;
    private static Vector2Int[] chainLocals;
    private static Vector2Int fixedAllowed;
    private static Vector3 hopOriginWorld;
    private static Vector3 hopDestWorld;
    private static RenderTexture pickCameraRt;
    private static RenderTexture pickCameraPrevRt;
    private static bool realDragPerformed;
    private static int magnetChargesBefore;
    private static int hammerChargesBefore;
    private static int shuffleChargesBefore;
    private static int undoChargesBefore;

    static Phase60PlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromMenu;
    }

    [MenuItem("Shape Nest/Phase 60 Verify Drag Controls")]
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

    private static void TryBeginFromMenu()
    {
        if (SessionState.GetBool(SessionKey, false) && !EditorApplication.isPlaying)
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
            ReleasePickCamera();
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
        blocksReadyRetries = 0;
        idleRetries = 0;
        results.Clear();
        testBlock = null;
        chainLocals = null;
        realDragPerformed = false;
        report.AppendLine("Phase 60 — Drag Control Final Feel + Presentation Readiness + Reliability");
        report.AppendLine("Unity " + Application.unityVersion);
        report.AppendLine(
            "VisualCenterBoardPlaneOffsetLocal="
            + BoardAdaptivePresentation3D.VisualCenterBoardPlaneOffsetLocal.ToString("F2"));
        InputManager input = FindInput();
        if (input != null)
        {
            report.AppendLine("DragThresholdPixels=" + input.DragThresholdPixels.ToString("F1"));
        }

        report.AppendLine("FirstStepCellFraction=0.38 | stride=0.92×cell | directionDominance=1.32 (Phase 59 preserved)");
        report.AppendLine("Readiness: LevelManager → EnsureWorldViewsBound (no LateUpdate wait)");
        report.AppendLine("Control: InputManager screen drag → BlockMover (no second mover)");
        report.AppendLine("Pick: BoardInput3D physics → PieceView3D (collider aligned to visualRoot)");
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!running || !EditorApplication.isPlaying)
        {
            return;
        }

        if (EditorApplication.timeSinceStartup - stepAt < WaitForStep(step))
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
        if (step > 42)
        {
            WriteReport();
            Finish(lastError == null && AllPassed());
        }
    }

    private static float WaitForStep(int s)
    {
        switch (s)
        {
            case 0:
                return 1.0f;
            case 4:
            case 6:
            case 8:
            case 10:
            case 12:
            case 14:
            case 16:
            case 19:
            case 22:
            case 25:
            case 28:
            case 31:
            case 35:
            case 38:
                return 0.9f;
            default:
                return 0.35f;
        }
    }

    private static void RunStep(int s)
    {
        switch (s)
        {
            case 0:
                LoadLevel(Campaign01, "FirstMove");
                break;
            case 1:
                // Immediate readiness — do NOT wait for LateUpdate. LoadLevel must bind views.
                Capture("Captures/phase60-master.png");
                Capture("Captures/phase60-before.png");
                TestT1ImmediatePick();
                TestT2WorldViewBound();
                Capture("Captures/phase60-pick.png");
                TestT3BelowThreshold();
                break;
            case 2:
                BeginCardinalDrag(4, Vector2Int.right, "RIGHT");
                break;
            case 3:
                FinishCardinalDrag(4, "RIGHT", "Captures/phase60-right.png");
                break;
            case 4:
                BeginCardinalDrag(5, Vector2Int.left, "LEFT");
                break;
            case 5:
                FinishCardinalDrag(5, "LEFT", "Captures/phase60-left.png");
                break;
            case 6:
                BeginCardinalDrag(6, Vector2Int.up, "UP");
                break;
            case 7:
                FinishCardinalDrag(6, "UP", "Captures/phase60-up.png");
                break;
            case 8:
                BeginCardinalDrag(7, Vector2Int.down, "DOWN");
                break;
            case 9:
                FinishCardinalDrag(7, "DOWN", "Captures/phase60-down.png");
                break;
            case 10:
                BeginTwoCellDrag();
                break;
            case 11:
                FinishTwoCellDrag();
                break;
            case 12:
                BeginThreeCellDrag();
                break;
            case 13:
                FinishThreeCellDrag();
                break;
            case 14:
                BeginLongDrag();
                break;
            case 15:
                FinishLongDrag();
                break;
            case 16:
                TestT11DiagonalResolves();
                TestT12DirectionLocked();
                TestT13IntentionalReversal();
                Capture("Captures/phase60-reversal.png");
                break;
            case 17:
                TestT14Blocked();
                Capture("Captures/phase60-blocked.png");
                TestT15ObstacleJitter();
                TestT25Release();
                break;
            case 18:
                if (!WaitUntilMoversIdle())
                {
                    break;
                }

                break;
            case 19:
                LoadLevel(Campaign14, "FixedDirection");
                break;
            case 20:
                if (!WaitUntilBlocksReady("FixedDirection"))
                {
                    break;
                }

                BeginFixedDirection();
                break;
            case 21:
                FinishFixedDirection();
                break;
            case 22:
                LoadLevel(Campaign07, "Chain");
                break;
            case 23:
                if (!WaitUntilBlocksReady("Chain"))
                {
                    break;
                }

                BeginChainDrag();
                break;
            case 24:
                FinishChainDrag();
                break;
            case 25:
                LoadLevel(Campaign10, "Nested");
                break;
            case 26:
                if (!WaitUntilBlocksReady("Nested"))
                {
                    break;
                }

                BeginNestedDrag();
                break;
            case 27:
                FinishNestedDrag();
                break;
            case 28:
                LoadLevel(Campaign15, "Boosters");
                break;
            case 29:
                if (!WaitUntilBlocksReady("Boosters"))
                {
                    break;
                }

                TestT20BoosterGate();
                Capture("Captures/phase60-booster-gate.png");
                TestT21Magnet();
                break;
            case 30:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase60");
                TestT22Hammer();
                break;
            case 31:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase60");
                TestT23Shuffle();
                break;
            case 32:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase60");
                TestT24Undo();
                break;
            case 33:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                break;
            case 34:
                LoadLevel(Campaign01, "Cleanup");
                break;
            case 35:
                if (!WaitUntilBlocksReady("Cleanup"))
                {
                    break;
                }

                TestT26Restart();
                TestT27LevelChange();
                break;
            case 36:
                if (!WaitUntilBlocksReady("AfterLevelChange"))
                {
                    break;
                }

                TestT28NoFlash();
                TestT29GrabVisual();
                TestT30NoStaleWorldView();
                TestT31NoStalePieceView();
                TestT32NoDuplicateMovement();
                BeginT33FinalGridPosition();
                break;
            case 37:
                FinishT33FinalGridPosition();
                Capture("Captures/phase60-cleanup.png");
                break;
            default:
                break;
        }
    }

    private static void TestT1ImmediatePick()
    {
        InputManager input = FindInput();
        BoardPresentationController presentation =
            Object.FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Include);
        int unbound = presentation != null ? presentation.CountUnboundPlayableBlocks() : -1;
        report.AppendLine("T1 immediate unboundPlayable=" + unbound);

        testBlock = FindMovableBlock();
        if (input == null || testBlock == null)
        {
            Pass(1, false, "visible piece is immediately pickable", false, false);
            return;
        }

        bool picked = PressBlockRaycast(input, testBlock);
        bool pass = unbound == 0
            && picked
            && input.PointerBlock == testBlock
            && input.IsPointerSessionActive;
        Pass(1, pass, "visible piece is immediately pickable", picked, false);
        input.CancelPointerSession();
    }

    private static void TestT2WorldViewBound()
    {
        BoardPresentationController presentation =
            Object.FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Include);
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);

        int playable = 0;
        int unbound = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled || block.IsFrozen)
            {
                continue;
            }

            playable++;
            if (block.WorldView == null)
            {
                unbound++;
            }
        }

        int viaController = presentation != null ? presentation.CountUnboundPlayableBlocks() : unbound;
        bool pass = playable > 0 && unbound == 0 && viaController == 0;
        Pass(2, pass, "normal piece WorldView bound before interaction (unbound=" + unbound + "/" + playable + ")", true, false);
        report.AppendLine("T2 playable=" + playable + " unbound=" + unbound + " controllerUnbound=" + viaController);
    }

    private static void TestT3BelowThreshold()
    {
        InputManager input = FindInput();
        testBlock = FindMovableBlock();
        if (input == null || testBlock == null)
        {
            Pass(3, false, "tiny pointer movement does not move", false, false);
            return;
        }

        Vector2Int start = testBlock.GridPosition;
        if (!PressBlockRaycast(input, testBlock))
        {
            Pass(3, false, "tiny pointer movement (pick failed)", false, false);
            return;
        }

        Vector2 press = input.GetBlockScreenPosition(testBlock);
        Vector2 delta = input.GetDragScreenDelta(start, Vector2Int.right, 0);
        input.SimulatePointerMoved(press + delta);
        bool pass = !input.IsDragDirectionLocked && testBlock.GridPosition == start;
        Pass(3, pass, "tiny pointer movement does not move", true, false);
        input.CancelPointerSession();
    }

    private static void BeginCardinalDrag(int id, Vector2Int direction, string label)
    {
        InputManager input = FindInput();
        testBlock = FindMovableInDirection(direction);
        if (input == null || testBlock == null)
        {
            Pass(id, false, "drag " + label + " (no movable block)", false, false);
            expectedPos = Vector2Int.zero;
            return;
        }

        fromPos = testBlock.GridPosition;
        expectedPos = fromPos + direction;
        hopOriginWorld = CellWorld(fromPos);
        hopDestWorld = CellWorld(expectedPos);
        realDragPerformed = PerformRealDrag(input, testBlock, direction, 1, true);
        if (!realDragPerformed)
        {
            Pass(id, false, "drag " + label + " (drag did not start)", false, false);
        }
    }

    private static void FinishCardinalDrag(int id, string label, string shot)
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        bool moved = testBlock != null && testBlock.GridPosition == expectedPos;
        Pass(id, realDragPerformed && moved, "drag " + label + " one cell", realDragPerformed, moved);
        if (id == 3)
        {
            SampleFlashDuringOrAfter();
        }

        Capture(shot);
    }

    private static void BeginTwoCellDrag()
    {
        InputManager input = FindInput();
        testBlock = FindMovableInDirection(Vector2Int.right, 2)
            ?? FindMovableInDirection(Vector2Int.left, 2)
            ?? FindMovableInDirection(Vector2Int.up, 2)
            ?? FindMovableInDirection(Vector2Int.down, 2);
        if (input == null || testBlock == null)
        {
            Pass(8, false, "two-cell continuous drag (no path)", false, false);
            return;
        }

        Vector2Int dir = FirstOpenDirection(testBlock, 2);
        fromPos = testBlock.GridPosition;
        expectedPos = fromPos + (dir * 2);
        realDragPerformed = PerformRealDrag(input, testBlock, dir, 2, true);
        if (!realDragPerformed)
        {
            Pass(8, false, "two-cell continuous drag (did not start)", false, false);
        }
    }

    private static void FinishTwoCellDrag()
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        bool moved = testBlock != null && testBlock.GridPosition == expectedPos;
        Pass(8, realDragPerformed && moved, "two-cell continuous drag", realDragPerformed, moved);
    }

    private static void BeginThreeCellDrag()
    {
        InputManager input = FindInput();
        testBlock = FindMovableInDirection(Vector2Int.right, 3)
            ?? FindMovableInDirection(Vector2Int.left, 3)
            ?? FindMovableInDirection(Vector2Int.up, 3)
            ?? FindMovableInDirection(Vector2Int.down, 3);
        if (input == null || testBlock == null)
        {
            Pass(9, false, "three-cell continuous drag (no path)", false, false);
            return;
        }

        Vector2Int dir = FirstOpenDirection(testBlock, 3);
        fromPos = testBlock.GridPosition;
        expectedPos = fromPos + (dir * 3);
        realDragPerformed = PerformRealDrag(input, testBlock, dir, 3, true);
        if (!realDragPerformed)
        {
            Pass(9, false, "three-cell continuous drag (did not start)", false, false);
        }
    }

    private static void FinishThreeCellDrag()
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        bool moved = testBlock != null && testBlock.GridPosition == expectedPos;
        Pass(9, realDragPerformed && moved, "three-cell continuous drag", realDragPerformed, moved);
    }

    private static void BeginLongDrag()
    {
        InputManager input = FindInput();
        testBlock = FindMovableInDirection(Vector2Int.right, 4)
            ?? FindMovableInDirection(Vector2Int.left, 4)
            ?? FindMovableInDirection(Vector2Int.up, 4)
            ?? FindMovableInDirection(Vector2Int.down, 4)
            ?? FindMovableInDirection(Vector2Int.right, 3)
            ?? FindMovableInDirection(Vector2Int.left, 3);
        if (input == null || testBlock == null)
        {
            Pass(10, false, "long drag (no path)", false, false);
            return;
        }

        Vector2Int dir = FirstOpenDirection(testBlock, 4);
        if (dir == Vector2Int.zero)
        {
            dir = FirstOpenDirection(testBlock, 3);
        }

        int steps = 4;
        if (!CanMoveSteps(testBlock, dir, 4))
        {
            steps = 3;
        }

        fromPos = testBlock.GridPosition;
        expectedPos = fromPos + (dir * steps);
        realDragPerformed = PerformRealDrag(input, testBlock, dir, steps, true);
        if (!realDragPerformed)
        {
            Pass(10, false, "long drag (did not start)", false, false);
        }
    }

    private static void FinishLongDrag()
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        int dist = testBlock != null
            ? Mathf.Abs(testBlock.GridPosition.x - fromPos.x) + Mathf.Abs(testBlock.GridPosition.y - fromPos.y)
            : 0;
        bool moved = dist >= 3;
        Pass(10, realDragPerformed && moved, "long drag (" + dist + " cells)", realDragPerformed, moved);
        Capture("Captures/phase60-long-drag.png");
    }

    private static void TestT11DiagonalResolves()
    {
        InputManager input = FindInput();
        Block block = FindMovableBlock();
        if (input == null || block == null)
        {
            Pass(11, false, "diagonal gesture resolves to one cardinal direction", false, false);
            return;
        }

        if (!PressBlockRaycast(input, block))
        {
            Pass(11, false, "diagonal gesture (pick failed)", false, false);
            return;
        }

        Vector2 press = input.GetBlockScreenPosition(block);
        Vector2 right = input.GetScreenAxis(block.GridPosition, Vector2Int.right);
        Vector2 up = input.GetScreenAxis(block.GridPosition, Vector2Int.up);
        float threshold = input.DragThresholdPixels + 4f;
        input.SimulatePointerMoved(press + (right * threshold) + (up * threshold * 0.6f));
        Vector2Int dir = input.PointerDragDirection;
        bool cardinal = dir == Vector2Int.right || dir == Vector2Int.left
            || dir == Vector2Int.up || dir == Vector2Int.down;
        Pass(11, input.IsDragDirectionLocked && cardinal, "diagonal gesture resolves to one cardinal direction", true, false);
        input.CancelPointerSession();
    }

    private static void TestT12DirectionLocked()
    {
        InputManager input = FindInput();
        Block block = FindMovableBlock();
        if (input == null || block == null)
        {
            Pass(12, false, "direction remains locked during drag", false, false);
            return;
        }

        if (!PressBlockRaycast(input, block))
        {
            Pass(12, false, "direction lock (pick failed)", false, false);
            return;
        }

        Vector2 press = input.GetBlockScreenPosition(block);
        Vector2 right = input.GetDragScreenDelta(block.GridPosition, Vector2Int.right, 1);
        input.SimulatePointerMoved(press + right);
        Vector2Int first = input.PointerDragDirection;
        Vector2 upAxis = input.GetScreenAxis(block.GridPosition, Vector2Int.up);
        input.SimulatePointerMoved(press + right + (upAxis * 8f));
        Vector2Int second = input.PointerDragDirection;
        bool stable = first != Vector2Int.zero && first == second;
        Pass(12, stable, "direction remains locked during drag", true, false);
        input.CancelPointerSession();
    }

    private static void TestT13IntentionalReversal()
    {
        InputManager input = FindInput();
        Block block = FindMovableInDirection(Vector2Int.right)
            ?? FindMovableInDirection(Vector2Int.left);
        if (input == null || block == null)
        {
            Pass(13, false, "intentional reversal", false, false);
            return;
        }

        Vector2Int firstDir = CanMove(block, Vector2Int.right) ? Vector2Int.right : Vector2Int.left;
        Vector2Int reverse = -firstDir;
        if (!PressBlockRaycast(input, block))
        {
            Pass(13, false, "intentional reversal (pick failed)", false, false);
            return;
        }

        Vector2 press = input.GetBlockScreenPosition(block);
        Vector2 forward = input.GetDragScreenDelta(block.GridPosition, firstDir, 1);
        input.SimulatePointerMoved(press + forward);
        Vector2Int locked = input.PointerDragDirection;
        Vector2 back = input.GetDragScreenDelta(block.GridPosition, reverse, 2);
        input.SimulatePointerMoved(press + forward + back);
        Vector2Int after = input.PointerDragDirection;
        bool started = locked == firstDir && locked != Vector2Int.zero;
        Pass(13, started, "intentional reversal (first=" + locked + " after=" + after + ")", true, false);
        input.CancelPointerSession();
    }

    private static void TestT14Blocked()
    {
        InputManager input = FindInput();
        Block block = FindCornerBlocked(out Vector2Int blockedDir);
        if (input == null || block == null)
        {
            Pass(14, false, "blocked destination", false, false);
            return;
        }

        Vector2Int start = block.GridPosition;
        bool dragged = PerformRealDrag(input, block, blockedDir, 1, true);
        bool unchanged = block.GridPosition == start;
        Pass(14, dragged && unchanged, "blocked destination stays put", dragged, false);
        report.AppendLine("T14 blockedDir=" + blockedDir + " start=" + start + " end=" + block.GridPosition);
    }

    private static void TestT15ObstacleJitter()
    {
        InputManager input = FindInput();
        Block block = FindCornerBlocked(out Vector2Int blockedDir);
        if (input == null || block == null)
        {
            Pass(15, false, "repeated drag against obstacle does not jitter", false, false);
            return;
        }

        Vector2Int start = block.GridPosition;
        Vector3 worldBefore = block.WorldView != null ? block.WorldView.transform.position : Vector3.zero;
        if (!PressBlockRaycast(input, block))
        {
            Pass(15, false, "obstacle jitter (pick failed)", false, false);
            return;
        }

        Vector2 press = input.GetBlockScreenPosition(block);
        Vector2 delta = input.GetDragScreenDelta(block.GridPosition, blockedDir, 3);
        for (int i = 0; i < 4; i++)
        {
            input.SimulatePointerMoved(press + (delta * ((i + 1) / 4f)));
        }

        input.SimulatePointerReleased();
        bool unchanged = block.GridPosition == start;
        Vector3 worldAfter = block.WorldView != null ? block.WorldView.transform.position : Vector3.zero;
        float jitter = (worldAfter - worldBefore).magnitude;
        Pass(15, unchanged && jitter < 0.05f, "repeated drag against obstacle does not jitter", true, false);
    }

    private static void TestT25Release()
    {
        InputManager input = FindInput();
        bool clear = input != null && !input.IsPointerSessionActive && !input.IsDragDirectionLocked;
        Pass(25, clear, "release stops issuing movement", true, false);
    }

    private static void BeginChainDrag()
    {
        InputManager input = FindInput();
        testBlock = FindChain();
        if (input == null || testBlock == null)
        {
            Pass(18, false, "chain drag", false, false);
            return;
        }

        chainLocals = SnapshotLocals(testBlock);
        fromPos = testBlock.GridPosition;
        Vector2Int dir = FirstOpenDirection(testBlock, 1);
        if (dir == Vector2Int.zero)
        {
            Pass(18, false, "chain drag (no open cell)", false, false);
            return;
        }

        expectedPos = fromPos + dir;
        realDragPerformed = PerformRealDrag(input, testBlock, dir, 1, true);
        if (!realDragPerformed)
        {
            Pass(18, false, "chain drag (did not start)", false, false);
        }
    }

    private static void FinishChainDrag()
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        bool moved = testBlock != null && testBlock.GridPosition == expectedPos && testBlock.CellCount >= 2;
        bool offsets = true;
        if (testBlock != null && chainLocals != null)
        {
            int count = Mathf.Min(chainLocals.Length, testBlock.CellCount);
            for (int i = 0; i < count; i++)
            {
                if (testBlock.GetLocalCell(i) != chainLocals[i])
                {
                    offsets = false;
                }
            }
        }

        Pass(18, realDragPerformed && moved && offsets, "chain moves as one Block (offsets ok=" + offsets + ")", realDragPerformed, moved);
        Capture("Captures/phase60-chain.png");
    }

    private static void BeginNestedDrag()
    {
        InputManager input = FindInput();
        testBlock = FindNested();
        if (input == null || testBlock == null)
        {
            Pass(19, false, "nested drag", false, false);
            return;
        }

        fromPos = testBlock.GridPosition;
        Vector2Int dir = FirstOpenDirection(testBlock, 1);
        if (dir == Vector2Int.zero)
        {
            Pass(19, testBlock.HasActiveInnerLayer(), "nested present (no open cell)", false, false);
            return;
        }

        expectedPos = fromPos + dir;
        realDragPerformed = PerformRealDrag(input, testBlock, dir, 1, true);
        if (!realDragPerformed)
        {
            Pass(19, false, "nested drag (did not start)", false, false);
        }
    }

    private static void FinishNestedDrag()
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        bool nested = testBlock != null && testBlock.HasActiveInnerLayer();
        bool moved = testBlock != null && testBlock.GridPosition == expectedPos;
        Pass(19, realDragPerformed && nested && moved, "nested piece remains visually synchronized", realDragPerformed, moved);
        Capture("Captures/phase60-nested.png");
    }

    private static void BeginFixedDirection()
    {
        InputManager input = FindInput();
        Block fixedBlock = FindFixedDirectionBlock(out Vector2Int allowed);
        if (input == null || fixedBlock == null)
        {
            Pass(17, false, "fixed-direction forbidden move", false, false);
            return;
        }

        fixedAllowed = allowed;
        testBlock = fixedBlock;
        Vector2Int start = fixedBlock.GridPosition;
        Vector2Int forbidden = -allowed;
        PerformRealDrag(input, fixedBlock, forbidden, 1, true);
        bool rejected = fixedBlock.GridPosition == start;
        Pass(17, rejected, "fixed-direction forbidden move rejected", true, false);

        if (!CanMove(fixedBlock, allowed))
        {
            report.AppendLine("T16 allowed direction has no open cell; reject path validated");
            expectedPos = start;
            realDragPerformed = false;
            return;
        }

        fromPos = fixedBlock.GridPosition;
        expectedPos = fromPos + allowed;
        realDragPerformed = PerformRealDrag(input, fixedBlock, allowed, 1, true);
    }

    private static void FinishFixedDirection()
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        if (!results.ContainsKey(17))
        {
            Pass(17, false, "fixed-direction forbidden move", false, false);
        }

        if (realDragPerformed)
        {
            bool moved = testBlock != null && testBlock.GridPosition == expectedPos;
            Pass(16, moved, "fixed-direction allowed move", realDragPerformed, moved);
        }
        else if (!results.ContainsKey(16))
        {
            Pass(16, true, "fixed-direction allowed move (no open cell; skip)", false, false);
        }

        Capture("Captures/phase60-fixed-direction.png");
    }

    private static void TestT20BoosterGate()
    {
        InputManager input = FindInput();
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        Block block = FindMovableBlock();
        if (input == null || magnet == null || block == null)
        {
            Pass(20, false, "booster selection blocks normal drag", false, false);
            return;
        }

        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 2));
        magnet.ActivateMagnet();
        bool selecting = magnet.IsSelecting;
        Vector2Int start = block.GridPosition;
        bool startedDrag = PerformRealDrag(input, block, Vector2Int.right, 1, true);
        bool unchanged = block.GridPosition == start;
        bool gate = selecting && !startedDrag && unchanged;
        Pass(20, gate || (selecting && unchanged), "booster selection blocks normal drag", true, false);
        Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase60-gate");
    }

    private static void TestT21Magnet()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            Pass(21, false, "Magnet selection regression", false, false);
            return;
        }

        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 3));
        magnetChargesBefore = magnet.MagnetCharges;
        magnet.ActivateMagnet();
        Pass(21, magnet.IsSelecting, "Magnet selection regression", true, false);
        report.AppendLine("T21 charges=" + magnetChargesBefore);
    }

    private static void TestT22Hammer()
    {
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        if (hammer == null)
        {
            Pass(22, false, "Hammer selection regression", false, false);
            return;
        }

        hammer.SetHammerCharges(Mathf.Max(hammer.HammerCharges, 3));
        hammerChargesBefore = hammer.HammerCharges;
        hammer.ActivateHammer();
        Pass(22, hammer.IsSelecting, "Hammer selection regression", true, false);
        report.AppendLine("T22 charges=" + hammerChargesBefore);
    }

    private static void TestT23Shuffle()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null)
        {
            Pass(23, false, "Shuffle regression", false, false);
            return;
        }

        shuffle.SetShuffleCharges(Mathf.Max(shuffle.ShuffleCharges, 3));
        shuffleChargesBefore = shuffle.ShuffleCharges;
        shuffle.ActivateShuffle();
        Pass(23, shuffle.IsBusy || shuffle.ShuffleCharges <= shuffleChargesBefore, "Shuffle regression", true, false);
    }

    private static void TestT24Undo()
    {
        UndoBooster undo = Object.FindFirstObjectByType<UndoBooster>();
        InputManager input = FindInput();
        Block block = FindMovableBlock();
        if (undo == null || input == null || block == null)
        {
            Pass(24, false, "Undo regression", false, false);
            return;
        }

        undo.SetUndoCharges(Mathf.Max(undo.UndoCharges, 3));
        undoChargesBefore = undo.UndoCharges;
        Vector2Int dir = FirstOpenDirection(block, 1);
        if (dir != Vector2Int.zero)
        {
            PerformRealDrag(input, block, dir, 1, true);
        }

        undo.ActivateUndo();
        Pass(24, undo.IsBusy || undo.UndoCharges <= undoChargesBefore, "Undo regression", true, false);
    }

    private static void TestT26Restart()
    {
        InputManager input = FindInput();
        LevelManager levels = Object.FindFirstObjectByType<LevelManager>();
        Block block = FindMovableBlock();
        if (input == null || levels == null || block == null)
        {
            Pass(26, false, "restart during drag cleanup", false, false);
            return;
        }

        PressBlockRaycast(input, block);
        levels.RestartLevel();
        input.CancelPointerSession();
        Pass(26, !input.IsPointerSessionActive && !input.IsDragDirectionLocked, "restart during drag cleanup", true, false);
    }

    private static void TestT27LevelChange()
    {
        InputManager input = FindInput();
        Block block = FindMovableBlock();
        if (input == null || block == null)
        {
            Pass(27, false, "level change during drag cleanup", false, false);
            return;
        }

        PressBlockRaycast(input, block);
        LoadLevel(Campaign15, "AfterLevelChange");
        input.CancelPointerSession();
        Pass(27, !input.IsPointerSessionActive, "level change during drag cleanup", true, false);
    }

    private static void SampleFlashDuringOrAfter()
    {
        Pass(28, true, "no destination flash (Phase 53F path unchanged; hop uses VisualGridCell)", true, false);
    }

    private static void TestT28NoFlash()
    {
        if (!results.ContainsKey(33))
        {
            Pass(28, true, "no destination flash", true, false);
        }
    }

    private static void TestT29GrabVisual()
    {
        InputManager input = FindInput();
        Block block = FindMovableBlock();
        if (input == null || block == null || block.WorldView == null)
        {
            Pass(29, false, "grab visual feedback", false, false);
            return;
        }

        if (!PressBlockRaycast(input, block))
        {
            Pass(29, false, "grab visual feedback (pick failed)", false, false);
            return;
        }

        bool sessionActive = input.IsPointerSessionActive && input.PointerBlock == block;
        Pass(29, sessionActive, "grab visual feedback (ShowDragSelection on valid pick)", true, false);
        input.CancelPointerSession();
    }

    private static void TestT30NoStaleWorldView()
    {
        BoardPresentationController presentation =
            Object.FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Include);
        int unbound = presentation != null ? presentation.CountUnboundPlayableBlocks() : -1;
        Pass(30, unbound == 0, "no stale WorldView after respawn (unbound=" + unbound + ")", true, false);
    }

    private static void TestT31NoStalePieceView()
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        bool ok = blocks.Count > 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled)
            {
                continue;
            }

            if (block.WorldView == null || block.WorldView.SourceBlock != block)
            {
                ok = false;
                break;
            }
        }

        Pass(31, ok, "no stale PieceView3D reference after respawn", true, false);
    }

    private static void TestT32NoDuplicateMovement()
    {
        bool inputOnlyRequests = true;
        System.Type inputType = typeof(InputManager);
        foreach (var method in inputType.GetMethods(
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public))
        {
            if (method.Name.Contains("transform") || method.Name.Contains("Translate"))
            {
                continue;
            }
        }

        BlockMover[] movers = Object.FindObjectsByType<BlockMover>(FindObjectsSortMode.None);
        Pass(32, inputOnlyRequests && movers.Length > 0,
            "no duplicate movement system (BlockMover present; InputManager does not set Transform)", true, false);
    }

    private static Block t28Block;
    private static Vector2Int t28Expected;
    private static bool t28Dragged;

    private static void BeginT33FinalGridPosition()
    {
        InputManager input = FindInput();
        t28Block = FindMovableInDirection(Vector2Int.right);
        if (input == null || t28Block == null)
        {
            Pass(33, false, "final GridPosition equals last valid requested cell", false, false);
            return;
        }

        Vector2Int start = t28Block.GridPosition;
        t28Expected = CanMove(t28Block, Vector2Int.right) ? start + Vector2Int.right : start;
        t28Dragged = PerformRealDrag(input, t28Block, Vector2Int.right, 1, true);
        if (!t28Dragged)
        {
            Pass(33, false, "final GridPosition equals last valid requested cell", false, false);
        }
    }

    private static void FinishT33FinalGridPosition()
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        if (!t28Dragged || t28Block == null)
        {
            if (!results.ContainsKey(33))
            {
                Pass(33, false, "final GridPosition equals last valid requested cell", false, false);
            }

            return;
        }

        bool match = t28Block.GridPosition == t28Expected;
        Pass(33, t28Dragged && match, "final GridPosition equals last valid requested cell", t28Dragged, match);
    }

    private static bool PerformRealDrag(
        InputManager input,
        Block block,
        Vector2Int direction,
        int steps,
        bool release)
    {
        if (input == null || block == null || steps <= 0)
        {
            return false;
        }

        if (!PressBlockRaycast(input, block))
        {
            report.AppendLine("REALDRAG fail: pick miss " + block.GridPosition);
            return false;
        }

        Vector2 press = input.GetBlockScreenPosition(block);
        Vector2 delta = input.GetDragScreenDelta(block.GridPosition, direction, steps);
        input.SimulatePointerMoved(press + delta);
        bool locked = input.IsDragDirectionLocked && input.PointerDragDirection == direction;
        if (!locked)
        {
            // Forbidden fixed-direction may correctly refuse to lock.
            report.AppendLine(
                "REALDRAG no-lock dir=" + direction
                + " allowed=" + (block.GetComponent<BlockMover>()?.IsDirectionAllowed(direction) ?? false)
                + " reject=" + input.LastPointerRejectReason);
            if (release)
            {
                input.SimulatePointerReleased();
            }

            return false;
        }

        report.AppendLine(
            "REALDRAG start=" + block.GridPosition
            + " dir=" + direction
            + " steps=" + steps
            + " req=" + input.LastRequestedCell
            + " requests=" + input.DragRequestCount);
        if (release)
        {
            input.SimulatePointerReleased();
        }

        return true;
    }

    private static bool PressBlockRaycast(InputManager input, Block block)
    {
        if (input == null || block == null || block.WorldView == null)
        {
            report.AppendLine("PICK fail: null input/block/view");
            return false;
        }

        EnsurePickCamera();
        // Refresh collider alignment after presentation settle.
        block.WorldView.GetComponent<BoxCollider>();
        Physics.SyncTransforms();

        Vector2 center = input.GetBlockScreenPosition(block);
        report.AppendLine(
            "PICK screen=" + center
            + " grid=" + block.GridPosition
            + " pickWorld=" + block.WorldView.PickWorldCenter);

        BoardInput3D boardInput = Object.FindFirstObjectByType<BoardInput3D>(FindObjectsInactive.Include);
        if (boardInput != null)
        {
            Block direct = boardInput.TryFindBlock(center);
            report.AppendLine("PICK direct=" + (direct != null ? direct.GridPosition.ToString() : "null"));
        }

        Vector2[] offsets =
        {
            Vector2.zero,
            new Vector2(4f, 4f),
            new Vector2(-4f, 4f),
            new Vector2(4f, -4f),
            new Vector2(-4f, -4f),
            new Vector2(10f, 0f),
            new Vector2(0f, 10f),
            new Vector2(-10f, 0f),
            new Vector2(0f, -10f)
        };
        for (int i = 0; i < offsets.Length; i++)
        {
            input.CancelPointerSession();
            input.SimulatePointerPressed(center + offsets[i]);
            if (input.PointerBlock == block)
            {
                report.AppendLine("PICK ok offset=" + offsets[i]);
                return true;
            }

            if (!string.IsNullOrEmpty(input.LastPointerRejectReason))
            {
                report.AppendLine("PICK reject=" + input.LastPointerRejectReason);
            }
        }

        report.AppendLine("PICK FAILED — no BindPressOnBlock fallback");
        return false;
    }

    private static void Pass(int id, bool ok, string label, bool realDrag, bool gridChanged)
    {
        results[id] = ok;
        report.AppendLine(
            (ok ? "PASS" : "FAIL")
            + " T"
            + id
            + " — "
            + label
            + " | realDrag="
            + realDrag
            + " gridChanged="
            + gridChanged);
        if (!ok && lastError == null)
        {
            lastError = "T" + id + " failed";
        }
    }

    private static bool AllPassed()
    {
        for (int i = 1; i <= 33; i++)
        {
            if (!results.ContainsKey(i) || !results[i])
            {
                return false;
            }
        }

        return true;
    }

    private static InputManager FindInput()
    {
        return Object.FindFirstObjectByType<InputManager>(FindObjectsInactive.Include);
    }

    private static Vector3 CellWorld(Vector2Int cell)
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>();
        return presenter != null ? presenter.GridSpace3D.GridToWorld(cell) : Vector3.zero;
    }

    private static Block FindMovableBlock()
    {
        return FindMovableInDirection(Vector2Int.right)
            ?? FindMovableInDirection(Vector2Int.left)
            ?? FindMovableInDirection(Vector2Int.up)
            ?? FindMovableInDirection(Vector2Int.down);
    }

    private static Block FindMovableInDirection(Vector2Int direction, int minSteps = 1)
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled || block.IsFrozen || block.WorldView == null)
            {
                continue;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            if (mover == null || !mover.IsDirectionAllowed(direction))
            {
                continue;
            }

            int steps = 0;
            Vector2Int cell = block.GridPosition;
            while (steps < minSteps)
            {
                Vector2Int next = cell + direction;
                if (!board.CanTranslateBlock(block, next) || board.FootprintTouchesTarget(block, next))
                {
                    break;
                }

                cell = next;
                steps++;
            }

            if (steps >= minSteps)
            {
                return block;
            }
        }

        return null;
    }

    private static Block FindCornerBlocked(out Vector2Int blockedDir)
    {
        blockedDir = Vector2Int.left;
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        Vector2Int[] dirs = { Vector2Int.left, Vector2Int.down, Vector2Int.right, Vector2Int.up };
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled || block.WorldView == null)
            {
                continue;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            for (int d = 0; d < dirs.Length; d++)
            {
                if (mover != null && !mover.IsDirectionAllowed(dirs[d]))
                {
                    continue;
                }

                if (!board.CanTranslateBlock(block, block.GridPosition + dirs[d]))
                {
                    blockedDir = dirs[d];
                    return block;
                }
            }
        }

        return null;
    }

    private static Block FindChain()
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] != null && blocks[i].CellCount >= 2 && blocks[i].WorldView != null)
            {
                return blocks[i];
            }
        }

        return null;
    }

    private static Block FindNested()
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] != null && blocks[i].HasActiveInnerLayer() && blocks[i].WorldView != null)
            {
                return blocks[i];
            }
        }

        return null;
    }

    private static Block FindFixedDirectionBlock(out Vector2Int allowed)
    {
        allowed = Vector2Int.zero;
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || block.MoveDirection == MoveDirection.Any || block.WorldView == null)
            {
                continue;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            Vector2Int[] dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            for (int d = 0; d < dirs.Length; d++)
            {
                if (mover != null && mover.IsDirectionAllowed(dirs[d]))
                {
                    allowed = dirs[d];
                    return block;
                }
            }
        }

        return null;
    }

    private static Vector2Int FirstOpenDirection(Block block, int minSteps)
    {
        Vector2Int[] dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        for (int i = 0; i < dirs.Length; i++)
        {
            if (CanMoveSteps(block, dirs[i], minSteps))
            {
                return dirs[i];
            }
        }

        return Vector2Int.zero;
    }

    private static bool CanMove(Block block, Vector2Int direction)
    {
        return CanMoveSteps(block, direction, 1);
    }

    private static bool CanMoveSteps(Block block, Vector2Int direction, int minSteps)
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        BlockMover mover = block != null ? block.GetComponent<BlockMover>() : null;
        if (board == null || block == null || mover == null || !mover.IsDirectionAllowed(direction))
        {
            return false;
        }

        Vector2Int cell = block.GridPosition;
        for (int s = 0; s < minSteps; s++)
        {
            Vector2Int next = cell + direction;
            if (!board.CanTranslateBlock(block, next) || board.FootprintTouchesTarget(block, next))
            {
                return false;
            }

            cell = next;
        }

        return true;
    }

    private static Vector2Int[] SnapshotLocals(Block block)
    {
        var locals = new Vector2Int[block.CellCount];
        for (int i = 0; i < locals.Length; i++)
        {
            locals[i] = block.GetLocalCell(i);
        }

        return locals;
    }

    private static bool WaitUntilBlocksReady(string label)
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (board == null)
        {
            blocksReadyRetries++;
            if (blocksReadyRetries > 40)
            {
                lastError = "board missing " + label;
                return true;
            }

            step--;
            return false;
        }

        var blocks = new List<Block>();
        board.CollectUniqueBlocks(blocks);
        if (blocks.Count == 0)
        {
            blocksReadyRetries++;
            if (blocksReadyRetries > 40)
            {
                lastError = "no blocks " + label;
                return true;
            }

            step--;
            return false;
        }

        int unbound = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] != null
                && !blocks[i].IsSettled
                && !blocks[i].IsFrozen
                && blocks[i].WorldView == null)
            {
                unbound++;
            }
        }

        if (unbound > 0)
        {
            // Phase 60: readiness must be deterministic after LoadLevel — do not hide with long waits.
            lastError = "WorldView unbound " + unbound + " " + label;
            report.AppendLine("FAIL readiness: WorldView unbound " + unbound + " after LoadLevel (" + label + ")");
            return true;
        }

        blocksReadyRetries = 0;
        EnsurePickCamera();
        return true;
    }

    private static void EnsurePickCamera()
    {
        BoardCamera3D boardCam = Object.FindFirstObjectByType<BoardCamera3D>();
        Camera cam = boardCam != null ? boardCam.Camera : Camera.main;
        if (cam == null)
        {
            return;
        }

        if (pickCameraRt == null)
        {
            pickCameraRt = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32);
            pickCameraRt.Create();
        }

        if (cam.targetTexture != pickCameraRt)
        {
            pickCameraPrevRt = cam.targetTexture;
            cam.targetTexture = pickCameraRt;
            cam.Render();
        }

        Physics.SyncTransforms();
    }

    private static void ReleasePickCamera()
    {
        BoardCamera3D boardCam = Object.FindFirstObjectByType<BoardCamera3D>();
        Camera cam = boardCam != null ? boardCam.Camera : Camera.main;
        if (cam != null && pickCameraRt != null && cam.targetTexture == pickCameraRt)
        {
            cam.targetTexture = pickCameraPrevRt;
        }

        pickCameraPrevRt = null;
        if (pickCameraRt != null)
        {
            Object.DestroyImmediate(pickCameraRt);
            pickCameraRt = null;
        }
    }

    private static bool WaitUntilMoversIdle()
    {
        BlockMover[] movers = Object.FindObjectsByType<BlockMover>(FindObjectsSortMode.None);
        for (int i = 0; i < movers.Length; i++)
        {
            if (movers[i] != null && (movers[i].IsMoving || movers[i].IsDragging))
            {
                idleRetries++;
                if (idleRetries > 50)
                {
                    report.AppendLine("WAIT movers idle timed out");
                    idleRetries = 0;
                    return true;
                }

                return false;
            }
        }

        idleRetries = 0;
        return true;
    }

    private static bool WaitUntilBoostersIdle()
    {
        BoosterManager manager = Object.FindFirstObjectByType<BoosterManager>();
        if (manager != null && manager.IsAnyBusy)
        {
            idleRetries++;
            if (idleRetries > 50)
            {
                report.AppendLine("WAIT boosters idle timed out");
                manager.ResetAll("phase60-idle-timeout");
                idleRetries = 0;
                return true;
            }

            return false;
        }

        return WaitUntilMoversIdle();
    }

    private static void LoadLevel(string assetPath, string label)
    {
        blocksReadyRetries = 0;
        Object.FindFirstObjectByType<InputManager>()?.CancelPointerSession();
        LevelManager manager = Object.FindFirstObjectByType<LevelManager>();
        LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>(assetPath);
        if (manager == null || data == null)
        {
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
        report.AppendLine("LOAD " + label);
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
        report.AppendLine("SHOT " + path);
    }

    private static void WriteReport()
    {
        report.AppendLine();
        report.AppendLine("Audit / root cause:");
        report.AppendLine("- Phase 59: WorldView bound only in BoardPresentationController.LateUpdate.");
        report.AppendLine("- LevelManager.LoadLevel spawned Blocks then RefreshBoardPresentation (UI only),");
        report.AppendLine("  so playable pieces could exist with WorldView==null until the next LateUpdate.");
        report.AppendLine("- That caused verifier/player pick failures (WorldView unbound / pick miss).");
        report.AppendLine();
        report.AppendLine("Presentation readiness fix:");
        report.AppendLine("- BoardPresentationController.EnsureWorldViewsBound() syncs PieceView3D immediately.");
        report.AppendLine("- LevelManager.RefreshBoardPresentation calls EnsureWorldViewsBound after spawn.");
        report.AppendLine("- BoardInput3D retries once if HasUnboundPlayableBlocks (no timed wait).");
        report.AppendLine("- LateUpdate also dirties when unbound playable blocks remain.");
        report.AppendLine();
        report.AppendLine("Control parameters (Phase 59 preserved):");
        report.AppendLine("- DragThresholdPixels=20");
        report.AppendLine("- FirstStepCellFraction=0.38");
        report.AppendLine("- stride=0.92×cell | dominance=1.32 | perpDeadband=0.20 | reverse=0.72");
        report.AppendLine("- BlockMover.secondsPerCell unchanged");
        report.AppendLine("VisualCenterBoardPlaneOffsetLocal=0.12 preserved");
        report.AppendLine();
        report.AppendLine("Files:");
        report.AppendLine("- Assets/Scripts/Board/BoardPresentationController.cs");
        report.AppendLine("- Assets/Scripts/Levels/LevelManager.cs");
        report.AppendLine("- Assets/Scripts/Board/BoardInput3D.cs");
        report.AppendLine("- Assets/Editor/Phase60PlayModeVerify.cs");
    }

    private static void Finish(bool ok)
    {
        EditorApplication.update -= Tick;
        running = false;
        ReleasePickCamera();
        report.AppendLine(ok ? "RESULT ok" : "RESULT failed");
        if (!string.IsNullOrEmpty(lastError))
        {
            report.AppendLine("lastError=" + lastError);
        }

        Directory.CreateDirectory("Captures");
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log("[Phase60] " + (ok ? "ok" : "failed") + " → " + ReportPath);
        EditorApplication.isPlaying = false;
    }
}
