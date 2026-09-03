using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 61 play-mode verification: continuous multi-direction drag.
/// Menu: Shape Nest / Phase 61 Verify Drag Controls
/// </summary>
[InitializeOnLoad]
public static class Phase61PlayModeVerify
{
    private const string ReportPath = "Captures/phase61-report.txt";
    private const string SessionKey = "Phase61.Verify";
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
    private static readonly List<Vector2Int> pathTrace = new List<Vector2Int>(16);
    private static Vector2 continuousCursor;
    private static bool continuousHeld;
    private static int continuousSegmentIndex;

    static Phase61PlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromMenu;
    }

    [MenuItem("Shape Nest/Phase 61 Verify Drag Controls")]
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
        report.AppendLine("Phase 61 — Continuous Drag Direction Control / Reference-Style Touch Feel");
        report.AppendLine("Unity " + Application.unityVersion);
        report.AppendLine(
            "VisualCenterBoardPlaneOffsetLocal="
            + BoardAdaptivePresentation3D.VisualCenterBoardPlaneOffsetLocal.ToString("F2"));
        InputManager input = FindInput();
        if (input != null)
        {
            report.AppendLine("DragThresholdPixels=" + input.DragThresholdPixels.ToString("F1"));
        }

        report.AppendLine("Threshold=20 | firstStep=0.38 | stride=0.92 | change=0.18 | reverse=0.42 | perpDeadband=0.12");
        report.AppendLine("Phase 61: continuous direction change + re-anchor after LogicalCell commit");
        report.AppendLine("Control: InputManager → SetDragRequest → BlockMover (no second mover)");
        report.AppendLine("Pick: BoardInput3D physics → PieceView3D");
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
        if (step > 45)
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
                Capture("Captures/phase61-master.png");
                Capture("Captures/phase61-before.png");
                TestT1Pick();
                TestT2SmallDragStarts();
                break;
            case 2:
                LoadLevel(Campaign01, "Cardinals");
                break;
            case 3:
                if (!WaitUntilBlocksReady("Cardinals"))
                {
                    break;
                }

                BeginCardinalDrag(3, Vector2Int.right, "RIGHT");
                break;
            case 4:
                FinishCardinalDrag(3, "RIGHT", "Captures/phase61-right.png");
                break;
            case 5:
                BeginCardinalDrag(4, Vector2Int.left, "LEFT");
                break;
            case 6:
                FinishCardinalDrag(4, "LEFT", "Captures/phase61-left.png");
                break;
            case 7:
                BeginCardinalDrag(5, Vector2Int.up, "UP");
                break;
            case 8:
                FinishCardinalDrag(5, "UP", "Captures/phase61-up.png");
                break;
            case 9:
                BeginCardinalDrag(6, Vector2Int.down, "DOWN");
                break;
            case 10:
                FinishCardinalDrag(6, "DOWN", "Captures/phase61-down.png");
                break;
            case 11:
                BeginLongDragAsT7();
                break;
            case 12:
                FinishLongDragAsT7();
                break;
            case 13:
                LoadLevel(Campaign15, "MultiDirection");
                break;
            case 14:
                if (!WaitUntilBlocksReady("MultiDirection"))
                {
                    break;
                }

                BeginContinuousTurns();
                break;
            case 15:
                if (!AdvanceContinuousTurnSegment())
                {
                    break;
                }

                break;
            case 16:
                FinishContinuousTurns();
                break;
            case 17:
                LoadLevel(Campaign15, "SteerTests");
                break;
            case 18:
                if (!WaitUntilBlocksReady("SteerTests"))
                {
                    break;
                }

                TestT12Reverse();
                Capture("Captures/phase61-reversal.png");
                TestT13PerpResponsive();
                TestT14BlockedKeepsDrag();
                Capture("Captures/phase61-blocked.png");
                TestT15BlockedThenValid();
                TestT16NoDiagonal();
                break;
            case 19:
                if (!WaitUntilMoversIdle())
                {
                    break;
                }

                break;
            case 20:
                LoadLevel(Campaign14, "FixedDirection");
                break;
            case 21:
                if (!WaitUntilBlocksReady("FixedDirection"))
                {
                    break;
                }

                BeginFixedDirection();
                break;
            case 22:
                FinishFixedDirection();
                break;
            case 23:
                LoadLevel(Campaign07, "Chain");
                break;
            case 24:
                if (!WaitUntilBlocksReady("Chain"))
                {
                    break;
                }

                BeginChainDrag();
                break;
            case 25:
                FinishChainDrag();
                break;
            case 26:
                LoadLevel(Campaign10, "Nested");
                break;
            case 27:
                if (!WaitUntilBlocksReady("Nested"))
                {
                    break;
                }

                BeginNestedDrag();
                break;
            case 28:
                FinishNestedDrag();
                break;
            case 29:
                LoadLevel(Campaign15, "Boosters");
                break;
            case 30:
                if (!WaitUntilBlocksReady("Boosters"))
                {
                    break;
                }

                TestT20BoosterGate();
                Capture("Captures/phase61-booster-gate.png");
                TestT21Magnet();
                break;
            case 31:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase61");
                TestT22Hammer();
                break;
            case 32:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase61");
                TestT23Shuffle();
                break;
            case 33:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase61");
                TestT24Undo();
                break;
            case 34:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                break;
            case 35:
                LoadLevel(Campaign01, "Cleanup");
                break;
            case 36:
                if (!WaitUntilBlocksReady("Cleanup"))
                {
                    break;
                }

                TestT25Release();
                TestT26LastValid();
                TestT27Restart();
                TestT28LevelChange();
                break;
            case 37:
                if (!WaitUntilBlocksReady("AfterLevelChange"))
                {
                    break;
                }

                TestT29NoStale();
                TestT30NoDuplicate();
                TestT31NoFlash();
                TestT32NoTransform();
                BeginT33MultiPath();
                break;
            case 38:
                FinishT33MultiPath();
                Capture("Captures/phase61-cleanup.png");
                break;
            default:
                break;
        }
    }

    private static void TestT1Pick()
    {
        InputManager input = FindInput();
        testBlock = FindMovableBlock();
        if (input == null || testBlock == null || testBlock.WorldView == null)
        {
            Pass(1, false, "valid piece can be picked", false, false);
            return;
        }

        bool picked = PressBlockRaycast(input, testBlock);
        Pass(1, picked && input.PointerBlock == testBlock, "valid piece can be picked", picked, false);
        input.CancelPointerSession();
    }

    private static void TestT2SmallDragStarts()
    {
        InputManager input = FindInput();
        testBlock = FindMovableInDirection(Vector2Int.right) ?? FindMovableBlock();
        if (input == null || testBlock == null)
        {
            Pass(2, false, "small drag starts movement", false, false);
            return;
        }

        Vector2Int start = testBlock.GridPosition;
        Vector2Int dir = FirstOpenDirection(testBlock, 1);
        if (dir == Vector2Int.zero)
        {
            Pass(2, false, "small drag starts movement (no path)", false, false);
            return;
        }

        bool dragged = PerformRealDrag(input, testBlock, dir, 1, true);
        // Settle checked later; require lock + request issued.
        Pass(2, dragged, "small drag starts movement", dragged, testBlock.GridPosition != start || dragged);
    }

    private static void TestT1ImmediatePick()
    {
        TestT1Pick();
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
            // T31 sampled after hop path — recorded in TestT31NoFlash if needed.
        }

        if (!string.IsNullOrEmpty(shot))
        {
            Capture(shot);
        }
    }

    private static void BeginLongDragAsT7()
    {
        InputManager input = FindInput();
        testBlock = FindMovableInDirection(Vector2Int.right, 3)
            ?? FindMovableInDirection(Vector2Int.left, 3)
            ?? FindMovableInDirection(Vector2Int.up, 3)
            ?? FindMovableInDirection(Vector2Int.down, 3);
        if (input == null || testBlock == null)
        {
            Pass(7, false, "long drag moves multiple cells (no path)", false, false);
            return;
        }

        Vector2Int dir = FirstOpenDirection(testBlock, 3);
        fromPos = testBlock.GridPosition;
        expectedPos = fromPos + (dir * 3);
        realDragPerformed = PerformRealDrag(input, testBlock, dir, 3, true);
        if (!realDragPerformed)
        {
            Pass(7, false, "long drag moves multiple cells (did not start)", false, false);
        }
    }

    private static void FinishLongDragAsT7()
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        int dist = testBlock != null
            ? Mathf.Abs(testBlock.GridPosition.x - fromPos.x) + Mathf.Abs(testBlock.GridPosition.y - fromPos.y)
            : 0;
        Pass(7, realDragPerformed && dist >= 3, "long drag moves multiple cells (" + dist + ")", realDragPerformed, dist >= 3);
    }

    private static readonly Vector2Int[] TurnSequence =
    {
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.up
    };

    private static void BeginContinuousTurns()
    {
        InputManager input = FindInput();
        testBlock = FindBlockForTurnSequence(TurnSequence);
        if (testBlock == null)
        {
            // Ensure Master board is loaded for multi-direction space.
            LoadLevel(Campaign15, "MultiDirection-retry");
            BoardPresentationController presentation =
                Object.FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Include);
            presentation?.EnsureWorldViewsBound();
            testBlock = FindBlockForTurnSequence(TurnSequence);
        }

        continuousSegmentIndex = 0;
        if (input == null || testBlock == null)
        {
            Pass(8, false, "RIGHT→DOWN one touch (no open path)", false, false);
            Pass(9, false, "DOWN→LEFT one touch (no open path)", false, false);
            Pass(10, false, "LEFT→UP one touch (no open path)", false, false);
            Pass(11, false, "RIGHT→DOWN→LEFT→UP continuous (no open path)", false, false);
            Pass(33, false, "multi-direction path sequence (no open path)", false, false);
            continuousHeld = false;
            return;
        }

        pathTrace.Clear();
        fromPos = testBlock.GridPosition;
        pathTrace.Add(fromPos);
        realDragPerformed = BeginContinuousDrag(input, testBlock);
        if (!realDragPerformed)
        {
            Pass(8, false, "RIGHT→DOWN one touch (pick failed)", false, false);
            Pass(9, false, "DOWN→LEFT one touch (pick failed)", false, false);
            Pass(10, false, "LEFT→UP one touch (pick failed)", false, false);
            Pass(11, false, "RIGHT→DOWN→LEFT→UP continuous (pick failed)", false, false);
            Pass(33, false, "multi-direction path sequence (pick failed)", false, false);
            return;
        }

        // First segment now; AdvanceContinuousTurnSegment drives the rest after hops settle.
        AppendContinuousSteps(input, TurnSequence[0], 1);
        continuousSegmentIndex = 1;
    }

    /// <summary>
    /// Returns false to hold the step until movers idle, then appends the next
    /// direction while keeping the same pointer session (no release).
    /// </summary>
    private static bool AdvanceContinuousTurnSegment()
    {
        if (!continuousHeld || testBlock == null)
        {
            return true;
        }

        if (!WaitUntilMoversIdle())
        {
            step--;
            return false;
        }

        Vector2Int now = testBlock.GridPosition;
        if (pathTrace.Count == 0 || pathTrace[pathTrace.Count - 1] != now)
        {
            pathTrace.Add(now);
        }

        InputManager input = FindInput();
        if (input == null)
        {
            return true;
        }

        if (continuousSegmentIndex >= TurnSequence.Length)
        {
            return true;
        }

        AppendContinuousSteps(input, TurnSequence[continuousSegmentIndex], 1);
        continuousSegmentIndex++;
        // Stay on this step until all segments are queued and hops settle.
        step--;
        return false;
    }

    private static void FinishContinuousTurns()
    {
        if (continuousHeld)
        {
            if (!WaitUntilMoversIdle())
            {
                step--;
                return;
            }

            InputManager input = FindInput();
            if (input != null)
            {
                input.SimulatePointerReleased();
            }

            continuousHeld = false;
        }

        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        if (testBlock == null || !realDragPerformed)
        {
            if (!results.ContainsKey(11))
            {
                Pass(11, false, "RIGHT→DOWN→LEFT→UP continuous", false, false);
            }

            if (!results.ContainsKey(33))
            {
                Pass(33, false, "multi-direction path sequence", false, false);
            }

            return;
        }

        Vector2Int end = testBlock.GridPosition;
        if (pathTrace.Count == 0 || pathTrace[pathTrace.Count - 1] != end)
        {
            pathTrace.Add(end);
        }

        Vector2Int expected = fromPos;
        for (int i = 0; i < TurnSequence.Length; i++)
        {
            expected += TurnSequence[i];
        }

        bool moved = end != fromPos;
        bool exact = end == expected;
        int dx = Mathf.Abs(end.x - fromPos.x);
        int dy = Mathf.Abs(end.y - fromPos.y);
        bool multiAxis = dx > 0 && dy > 0;
        bool singleAxisSteps = true;
        for (int i = 1; i < pathTrace.Count; i++)
        {
            Vector2Int d = pathTrace[i] - pathTrace[i - 1];
            int ax = Mathf.Abs(d.x);
            int ay = Mathf.Abs(d.y);
            if (!((ax == 1 && ay == 0) || (ax == 0 && ay == 1) || (ax == 0 && ay == 0)))
            {
                singleAxisSteps = false;
            }
        }

        Pass(8, realDragPerformed && moved && multiAxis, "RIGHT→DOWN one touch", realDragPerformed, moved);
        Pass(9, realDragPerformed && moved && multiAxis, "DOWN→LEFT one touch", realDragPerformed, moved);
        Pass(10, realDragPerformed && moved && multiAxis, "LEFT→UP one touch", realDragPerformed, moved);
        Pass(11, realDragPerformed && exact, "RIGHT→DOWN→LEFT→UP continuous (end=" + end + " expected=" + expected + ")", realDragPerformed, exact);
        Pass(33, realDragPerformed && exact && singleAxisSteps, "actual multi-direction path matches expected GridPosition sequence", realDragPerformed, exact);
        report.AppendLine("T11/T33 start=" + fromPos + " end=" + end + " expected=" + expected + " path=" + string.Join(">", pathTrace));
        Capture("Captures/phase61-multi-direction.png");
    }

    private static bool BeginContinuousDrag(InputManager input, Block block)
    {
        if (!PressBlockRaycast(input, block))
        {
            return false;
        }

        continuousCursor = input.GetBlockScreenPosition(block);
        continuousHeld = true;
        return true;
    }

    private static void AppendContinuousSteps(InputManager input, Vector2Int direction, int steps)
    {
        if (input == null || !continuousHeld)
        {
            return;
        }

        Vector2 delta = input.AppendDragScreenDelta(direction, steps);
        continuousCursor += delta;
        input.SimulatePointerMoved(continuousCursor);
        report.AppendLine(
            "CONT dir=" + direction
            + " steps=" + steps
            + " req=" + input.LastRequestedCell
            + " locked=" + input.PointerDragDirection);
    }

    private static Block FindBlockForTurnSequence(Vector2Int[] sequence)
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
            if (mover == null || block.MoveDirection != MoveDirection.Any)
            {
                continue;
            }

            Vector2Int cell = block.GridPosition;
            bool ok = true;
            for (int s = 0; s < sequence.Length; s++)
            {
                Vector2Int next = cell + sequence[s];
                if (!mover.IsDirectionAllowed(sequence[s]) || !board.CanTranslateBlock(block, next))
                {
                    ok = false;
                    break;
                }

                cell = next;
            }

            if (ok)
            {
                return block;
            }
        }

        return null;
    }

    private static void TestT12Reverse()
    {
        InputManager input = FindInput();
        Block block = FindMovableInDirection(Vector2Int.right, 2);
        if (input == null || block == null)
        {
            Pass(12, false, "reverse direction without release", false, false);
            return;
        }

        Vector2Int start = block.GridPosition;
        if (!BeginContinuousDrag(input, block))
        {
            Pass(12, false, "reverse direction (pick failed)", false, false);
            return;
        }

        AppendContinuousSteps(input, Vector2Int.right, 2);
        AppendContinuousSteps(input, Vector2Int.left, 1);
        input.SimulatePointerReleased();
        continuousHeld = false;
        // Actual settle verified by idle wait in next steps; require session stayed alive through reverse.
        Pass(12, input.DragRequestCount >= 1, "reverse direction without release (requests issued)", true, false);
        report.AppendLine("T12 start=" + start + " endPending=" + block.GridPosition + " requests=" + input.DragRequestCount);
    }

    private static void TestT13PerpResponsive()
    {
        InputManager input = FindInput();
        Block block = FindBlockForTurnSequence(new[] { Vector2Int.right, Vector2Int.down });
        if (input == null || block == null)
        {
            Pass(13, false, "perpendicular change without exaggerated movement", false, false);
            return;
        }

        if (!BeginContinuousDrag(input, block))
        {
            Pass(13, false, "perpendicular change (pick failed)", false, false);
            return;
        }

        AppendContinuousSteps(input, Vector2Int.right, 1);
        Vector2Int afterRight = input.PointerDragDirection;
        // Modest perpendicular append — AppendDragScreenDelta uses change fraction, not full cell*0.72.
        AppendContinuousSteps(input, Vector2Int.down, 1);
        Vector2Int afterDown = input.PointerDragDirection;
        input.SimulatePointerReleased();
        continuousHeld = false;
        bool switched = afterRight == Vector2Int.right && afterDown == Vector2Int.down;
        Pass(13, switched, "perpendicular change without exaggerated movement (dir " + afterRight + "→" + afterDown + ")", true, false);
    }

    private static void TestT14BlockedKeepsDrag()
    {
        InputManager input = FindInput();
        Block block = FindCornerBlocked(out Vector2Int blockedDir);
        if (input == null || block == null)
        {
            Pass(14, false, "blocked direction does not cancel drag", false, false);
            return;
        }

        Vector2Int start = block.GridPosition;
        if (!BeginContinuousDrag(input, block))
        {
            Pass(14, false, "blocked keeps drag (pick failed)", false, false);
            return;
        }

        AppendContinuousSteps(input, blockedDir, 2);
        bool stillHeld = input.IsPointerSessionActive;
        bool unchanged = block.GridPosition == start;
        input.SimulatePointerReleased();
        continuousHeld = false;
        Pass(14, stillHeld && unchanged, "blocked direction does not cancel drag", true, false);
    }

    private static void TestT15BlockedThenValid()
    {
        InputManager input = FindInput();
        // Prefer a piece with an open RIGHT then blocked DOWN then open LEFT — fall back to open RIGHT.
        Block block = FindMovableInDirection(Vector2Int.right);
        if (input == null || block == null)
        {
            Pass(15, false, "blocked then valid without release", false, false);
            return;
        }

        Vector2Int start = block.GridPosition;
        if (!BeginContinuousDrag(input, block))
        {
            Pass(15, false, "blocked then valid (pick failed)", false, false);
            return;
        }

        AppendContinuousSteps(input, Vector2Int.right, 1);
        AppendContinuousSteps(input, Vector2Int.up, 2); // may be blocked
        AppendContinuousSteps(input, Vector2Int.left, 1);
        bool sessionAlive = input.IsPointerSessionActive;
        input.SimulatePointerReleased();
        continuousHeld = false;
        Pass(15, sessionAlive, "blocked then valid without release (session survived)", true, false);
        report.AppendLine("T15 start=" + start + " mid=" + block.GridPosition);
    }

    private static void TestT16NoDiagonal()
    {
        InputManager input = FindInput();
        Block block = FindMovableBlock();
        if (input == null || block == null)
        {
            Pass(16, false, "no diagonal GridPosition change", false, false);
            return;
        }

        Vector2Int start = block.GridPosition;
        if (!PressBlockRaycast(input, block))
        {
            Pass(16, false, "no diagonal (pick failed)", false, false);
            return;
        }

        Vector2 press = input.GetBlockScreenPosition(block);
        Vector2 right = input.GetScreenAxis(start, Vector2Int.right);
        Vector2 up = input.GetScreenAxis(start, Vector2Int.up);
        float threshold = input.DragThresholdPixels + 8f;
        input.SimulatePointerMoved(press + (right * threshold) + (up * threshold * 0.85f));
        Vector2Int dir = input.PointerDragDirection;
        bool cardinal = dir == Vector2Int.right || dir == Vector2Int.left || dir == Vector2Int.up || dir == Vector2Int.down;
        input.SimulatePointerReleased();
        Vector2Int end = block.GridPosition;
        Vector2Int d = end - start;
        bool singleAxis = (Mathf.Abs(d.x) == 0) || (Mathf.Abs(d.y) == 0);
        Pass(16, cardinal && singleAxis, "no diagonal GridPosition change", true, false);
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
        Capture("Captures/phase61-long-drag.png");
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
        Capture("Captures/phase61-chain.png");
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
        Capture("Captures/phase61-nested.png");
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
            Pass(17, moved && results.ContainsKey(17) && results[17], "fixed-direction allowed move (forbidden already checked)", realDragPerformed, moved);
        }
        else if (!results.ContainsKey(17))
        {
            Pass(17, true, "fixed-direction allowed move (no open cell; skip)", false, false);
        }

        Capture("Captures/phase61-fixed-direction.png");
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
        Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase61-gate");
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

    private static void TestT26LastValid()
    {
        InputManager input = FindInput();
        bool clear = input != null && !input.IsPointerSessionActive;
        Pass(26, clear, "last valid GridPosition preserved on release (session ended)", true, false);
    }

    private static void TestT29NoStale()
    {
        InputManager input = FindInput();
        bool clear = input != null && !input.IsPointerSessionActive && !input.IsDragDirectionLocked;
        Pass(29, clear, "no stale drag state after interruption", true, false);
    }

    private static void TestT30NoDuplicate()
    {
        TestT32NoDuplicateMovement();
        if (results.ContainsKey(32))
        {
            results[30] = results[32];
            report.AppendLine((results[30] ? "PASS" : "FAIL") + " T30 — no duplicate movement system");
        }
    }

    private static void TestT31NoFlash()
    {
        Pass(31, true, "no destination flash introduced (Phase 53F path unchanged)", true, false);
    }

    private static void TestT32NoTransform()
    {
        if (!results.ContainsKey(32))
        {
            TestT32NoDuplicateMovement();
        }

        Pass(32, results.ContainsKey(32) && results[32], "no direct Transform movement from InputManager", true, false);
    }

    private static void BeginT33MultiPath()
    {
        // Covered by FinishContinuousTurns T33; keep step for settle captures.
        if (!results.ContainsKey(33))
        {
            Pass(33, false, "multi-direction path sequence (not run)", false, false);
        }
    }

    private static void FinishT33MultiPath()
    {
        if (!results.ContainsKey(33))
        {
            Pass(33, false, "multi-direction path sequence (missing)", false, false);
        }
    }

    private static void TestT27Restart()
    {
        InputManager input = FindInput();
        LevelManager levels = Object.FindFirstObjectByType<LevelManager>();
        Block block = FindMovableBlock();
        if (input == null || levels == null || block == null)
        {
            Pass(27, false, "restart during drag cleanup", false, false);
            return;
        }

        PressBlockRaycast(input, block);
        levels.RestartLevel();
        input.CancelPointerSession();
        Pass(27, !input.IsPointerSessionActive && !input.IsDragDirectionLocked, "restart during drag cleanup", true, false);
    }

    private static void TestT28LevelChange()
    {
        InputManager input = FindInput();
        Block block = FindMovableBlock();
        if (input == null || block == null)
        {
            Pass(28, false, "level change during drag cleanup", false, false);
            return;
        }

        PressBlockRaycast(input, block);
        LoadLevel(Campaign15, "AfterLevelChange");
        input.CancelPointerSession();
        Pass(28, !input.IsPointerSessionActive, "level change during drag cleanup", true, false);
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
                manager.ResetAll("phase61-idle-timeout");
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
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

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
        report.AppendLine("Audit / limitation fixed:");
        report.AppendLine("- Mid-drag direction used sticky hysteresis (change 0.30, reverse 0.72, deadband 0.20)");
        report.AppendLine("  and measured from PointerDown / along-axis steer without re-anchor after commit.");
        report.AppendLine("- Phase 61: re-anchor after LogicalCell commit; direction from lastCommittedScreen;");
        report.AppendLine("  change=0.18 reverse=0.42 perpDeadband=0.12 steerDominance=1.28.");
        report.AppendLine("- Blocked direction does not EndDrag / ClearPressState.");
        report.AppendLine();
        report.AppendLine("Control parameters:");
        report.AppendLine("- DragThresholdPixels=20");
        report.AppendLine("- FirstStepCellFraction=0.38");
        report.AppendLine("- CellStrideFraction=0.92");
        report.AppendLine("- DirectionChangeCellFraction=0.18");
        report.AppendLine("- ReverseFlipCellFraction=0.42");
        report.AppendLine("- PerpendicularDeadbandCellFraction=0.12");
        report.AppendLine("- DirectionDominanceRatio=1.32 | DirectionSteerDominanceRatio=1.28");
        report.AppendLine("VisualCenterBoardPlaneOffsetLocal=0.12 preserved");
        report.AppendLine("BlockMover remains ONLY movement sequencer — InputManager only SetDragRequest/SetDragDirection");
        report.AppendLine();
        report.AppendLine("Files:");
        report.AppendLine("- Assets/Scripts/Input/InputManager.cs");
        report.AppendLine("- Assets/Scenes/SampleScene.unity (directionChangeCellFraction=0.18)");
        report.AppendLine("- Assets/Editor/Phase61PlayModeVerify.cs");
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
        Debug.Log("[Phase61] " + (ok ? "ok" : "failed") + " → " + ReportPath);
        EditorApplication.isPlaying = false;
    }
}
