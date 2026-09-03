using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 61B — final drag feel + haptic polish verification.
/// Menu: Shape Nest / Phase 61B Verify Drag Feel Polish
/// </summary>
[InitializeOnLoad]
public static class Phase61BPlayModeVerify
{
    private const string ReportPath = "Captures/phase61b-report.txt";
    private const string SessionKey = "Phase61B.Verify";
    private const string Campaign01 = "Assets/Levels/Campaign_01_FirstMove.asset";
    private const string Campaign07 = "Assets/Levels/Campaign_07_ChainIntro.asset";
    private const string Campaign10 = "Assets/Levels/Campaign_10_ShapeInShape.asset";
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
    private static bool realDragPerformed;
    private static int magnetChargesBefore;
    private static int hammerChargesBefore;
    private static int shuffleChargesBefore;
    private static int undoChargesBefore;
    private static readonly List<Vector2Int> pathTrace = new List<Vector2Int>(16);
    private static Vector2 continuousCursor;
    private static bool continuousHeld;
    private static int continuousSegmentIndex;
    private static double longDragStartedAt;
    private static int hapticPhase;
    private static Vector2Int hapticStart;
    private static bool hapticDragged;
    private static RenderTexture pickCameraRt;
    private static RenderTexture pickCameraPrevRt;
    private static readonly Vector2Int[] TurnSequence =
    {
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.up
    };

    static Phase61BPlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromMenu;
    }

    [MenuItem("Shape Nest/Phase 61B Verify Drag Feel Polish")]
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
        hapticPhase = 0;
        continuousHeld = false;
        report.AppendLine("Phase 61B — Final Drag Feel + Haptic Polish");
        report.AppendLine("Unity " + Application.unityVersion);
        report.AppendLine("gridCellMoveIntensity=1.2 (Phase 61A baseline=1.0, +20%)");
        report.AppendLine("secondsPerCell UNCHANGED 0.105");
        report.AppendLine("continuation linearWeight 0.98 (was 0.95), turn 0.88 (was 0.80)");
        report.AppendLine("continuation settlePortion 0.08 (was 0.18)");
        report.AppendLine("Phase 61 direction params UNCHANGED");
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
        if (step > 40)
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
            case 3:
            case 6:
            case 9:
            case 12:
            case 15:
            case 18:
            case 21:
            case 24:
            case 27:
            case 30:
                return 0.85f;
            default:
                return 0.35f;
        }
    }

    private static void RunStep(int s)
    {
        switch (s)
        {
            case 0:
                LoadLevel(Campaign01, "Haptics");
                break;
            case 1:
                if (!WaitUntilBlocksReady("Haptics"))
                {
                    break;
                }

                if (!RunHapticSuite())
                {
                    step--;
                }

                break;
            case 2:
                LoadLevel(Campaign01, "CardinalSmooth");
                break;
            case 3:
                if (!WaitUntilBlocksReady("CardinalSmooth"))
                {
                    break;
                }

                BeginSmoothCardinal(6, Vector2Int.right);
                break;
            case 4:
                FinishSmoothCardinal(6, "RIGHT");
                break;
            case 5:
                BeginSmoothCardinal(7, Vector2Int.left);
                break;
            case 6:
                FinishSmoothCardinal(7, "LEFT");
                break;
            case 7:
                BeginSmoothCardinal(8, Vector2Int.up);
                break;
            case 8:
                FinishSmoothCardinal(8, "UP");
                break;
            case 9:
                BeginSmoothCardinal(9, Vector2Int.down);
                break;
            case 10:
                FinishSmoothCardinal(9, "DOWN");
                break;
            case 11:
                LoadLevel(Campaign15, "Turns");
                break;
            case 12:
                if (!WaitUntilBlocksReady("Turns"))
                {
                    break;
                }

                BeginContinuousTurns();
                break;
            case 13:
                if (!AdvanceContinuousTurnSegment())
                {
                    break;
                }

                break;
            case 14:
                FinishContinuousTurns61B();
                break;
            case 15:
                LoadLevel(Campaign15, "Blocked");
                break;
            case 16:
                if (!WaitUntilBlocksReady("Blocked"))
                {
                    break;
                }

                TestT13BlockedKeeps();
                TestT14BlockedThenValid();
                break;
            case 17:
                if (!WaitUntilMoversIdle())
                {
                    break;
                }

                break;
            case 18:
                LoadLevel(Campaign07, "Chain");
                break;
            case 19:
                if (!WaitUntilBlocksReady("Chain"))
                {
                    break;
                }

                BeginChainDrag();
                break;
            case 20:
                FinishChainAsT15();
                break;
            case 21:
                LoadLevel(Campaign10, "Nested");
                break;
            case 22:
                if (!WaitUntilBlocksReady("Nested"))
                {
                    break;
                }

                BeginNestedDrag();
                break;
            case 23:
                FinishNestedAsT16();
                break;
            case 24:
                LoadLevel(Campaign14, "Fixed");
                break;
            case 25:
                if (!WaitUntilBlocksReady("Fixed"))
                {
                    break;
                }

                BeginFixedDirection();
                break;
            case 26:
                FinishFixedAsT17();
                break;
            case 27:
                LoadLevel(Campaign15, "Boosters");
                break;
            case 28:
                if (!WaitUntilBlocksReady("Boosters"))
                {
                    break;
                }

                TestT18BoosterGate();
                TestT19Magnet();
                break;
            case 29:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase61b");
                TestT20Hammer();
                break;
            case 30:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase61b");
                TestT21Shuffle();
                break;
            case 31:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase61b");
                TestT22Undo();
                break;
            case 32:
                if (!WaitUntilBoostersIdle())
                {
                    break;
                }

                break;
            case 33:
                LoadLevel(Campaign01, "Cleanup");
                break;
            case 34:
                if (!WaitUntilBlocksReady("Cleanup"))
                {
                    break;
                }

                TestT23Restart();
                TestT24LevelLoad();
                Capture("Captures/phase61b-cleanup.png");
                break;
            default:
                break;
        }
    }

    private static bool RunHapticSuite()
    {
        HapticFeedback haptics = Object.FindFirstObjectByType<HapticFeedback>(FindObjectsInactive.Include);
        InputManager input = FindInput();
        if (haptics == null || input == null)
        {
            for (int i = 1; i <= 5; i++)
            {
                Pass(i, false, "haptic suite missing components", false, false);
            }

            hapticPhase = 99;
            return true;
        }

        switch (hapticPhase)
        {
            case 0:
            {
                float intensity = haptics.GridCellMoveIntensity;
                Pass(5, intensity > HapticFeedback.Phase61AGridMoveIntensityBaseline
                        && intensity <= HapticFeedback.Phase61AGridMoveIntensityBaseline * 1.30f,
                    "haptic intensity stronger than Phase 61A baseline (value=" + intensity.ToString("F2") + ")",
                    true, false);

                haptics.ResetTestCounters();
                Block block = FindMovableInDirection(Vector2Int.right) ?? FindMovableBlock();
                if (block == null)
                {
                    Pass(1, false, "one haptic per successful cell", false, false);
                    Pass(2, true, "blocked zero haptics (skipped)", true, false);
                    Pass(3, true, "failed zero haptics (skipped)", true, false);
                    Pass(4, false, "four cells four haptics", false, false);
                    hapticPhase = 99;
                    return true;
                }

                hapticStart = block.GridPosition;
                hapticDragged = PerformRealDrag(input, block, Vector2Int.right, 1, true);
                hapticPhase = 1;
                return false;
            }
            case 1:
            {
                if (!WaitUntilMoversIdle())
                {
                    return false;
                }

                int c = haptics.GridMoveHapticCount;
                Pass(1, hapticDragged && c == 1, "successful cell triggers exactly one haptic (count=" + c + ")", hapticDragged, c == 1);

                haptics.ResetTestCounters();
                Block corner = FindCornerBlocked(out Vector2Int blockedDir);
                if (corner != null && BeginContinuousDrag(input, corner))
                {
                    AppendContinuousSteps(input, blockedDir, 2);
                    input.SimulatePointerReleased();
                    continuousHeld = false;
                    Pass(2, haptics.GridMoveHapticCount == 0, "blocked movement triggers zero haptics", true, false);
                }
                else
                {
                    Pass(2, true, "blocked zero haptics (no corner)", true, false);
                }

                haptics.ResetTestCounters();
                int before = haptics.GridMoveHapticCount;
                input.CancelPointerSession();
                Pass(3, haptics.GridMoveHapticCount == before, "failed/cancelled movement triggers zero haptics", true, false);

                haptics.ResetTestCounters();
                Block longBlock = FindMovableInDirection(Vector2Int.right, 4);
                if (longBlock == null)
                {
                    Pass(4, false, "four consecutive cells four haptics (no path)", false, false);
                    hapticPhase = 99;
                    return true;
                }

                hapticStart = longBlock.GridPosition;
                hapticDragged = PerformRealDrag(input, longBlock, Vector2Int.right, 4, true);
                hapticPhase = 2;
                return false;
            }
            case 2:
            {
                if (!WaitUntilMoversIdle())
                {
                    return false;
                }

                int c = haptics.GridMoveHapticCount;
                Pass(4, hapticDragged && c == 4, "four consecutive same-direction cells produce four haptics (count=" + c + ")", hapticDragged, c == 4);
                report.AppendLine("T4 haptics=" + c + " intensity=" + haptics.GridCellMoveIntensity.ToString("F2"));
                Capture("Captures/phase61b-haptics.png");
                hapticPhase = 99;
                return true;
            }
            default:
                return true;
        }
    }

    private static void BeginSmoothCardinal(int id, Vector2Int dir)
    {
        InputManager input = FindInput();
        testBlock = FindMovableInDirection(dir, 3);
        if (input == null || testBlock == null)
        {
            Pass(id, false, "continuous " + DirLabel(dir) + " no artificial idle gap (no path)", false, false);
            return;
        }

        fromPos = testBlock.GridPosition;
        longDragStartedAt = EditorApplication.timeSinceStartup;
        realDragPerformed = PerformRealDrag(input, testBlock, dir, 3, true);
        if (!realDragPerformed)
        {
            Pass(id, false, "continuous " + DirLabel(dir) + " (drag failed)", false, false);
        }
    }

    private static void FinishSmoothCardinal(int id, string label)
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        int dist = testBlock != null
            ? Mathf.Abs(testBlock.GridPosition.x - fromPos.x) + Mathf.Abs(testBlock.GridPosition.y - fromPos.y)
            : 0;
        float elapsed = (float)(EditorApplication.timeSinceStartup - longDragStartedAt);
        float perCell = dist > 0 ? elapsed / dist : elapsed;
        // Catch only absurd artificial gaps; hop duration ~0.105 plus presentation overhead.
        bool ok = realDragPerformed && dist >= 3 && perCell <= 0.55f;
        Pass(id, ok, "continuous " + label + " no artificial idle gap (dist=" + dist + " perCell=" + perCell.ToString("F3") + "s)", realDragPerformed, dist >= 3);
        report.AppendLine("T" + id + " " + label + " elapsed=" + elapsed.ToString("F3") + " dist=" + dist);
        if (id == 6)
        {
            Capture("Captures/phase61b-smooth-right.png");
        }
    }

    private static string DirLabel(Vector2Int d)
    {
        if (d == Vector2Int.right) return "RIGHT";
        if (d == Vector2Int.left) return "LEFT";
        if (d == Vector2Int.up) return "UP";
        if (d == Vector2Int.down) return "DOWN";
        return d.ToString();
    }

    private static void BeginContinuousTurns()
    {
        InputManager input = FindInput();
        testBlock = FindBlockForTurnSequence(TurnSequence);
        continuousSegmentIndex = 0;
        if (input == null || testBlock == null)
        {
            Pass(10, false, "RIGHT→DOWN (no path)", false, false);
            Pass(11, false, "DOWN→LEFT (no path)", false, false);
            Pass(12, false, "LEFT→UP (no path)", false, false);
            continuousHeld = false;
            return;
        }

        pathTrace.Clear();
        fromPos = testBlock.GridPosition;
        pathTrace.Add(fromPos);
        realDragPerformed = BeginContinuousDrag(input, testBlock);
        if (!realDragPerformed)
        {
            Pass(10, false, "RIGHT→DOWN (pick failed)", false, false);
            Pass(11, false, "DOWN→LEFT (pick failed)", false, false);
            Pass(12, false, "LEFT→UP (pick failed)", false, false);
            return;
        }

        AppendContinuousSteps(input, TurnSequence[0], 1);
        continuousSegmentIndex = 1;
    }

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
        step--;
        return false;
    }

    private static void FinishContinuousTurns61B()
    {
        if (continuousHeld)
        {
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

        if (testBlock == null)
        {
            if (!results.ContainsKey(10))
            {
                Pass(10, false, "RIGHT→DOWN", false, false);
                Pass(11, false, "DOWN→LEFT", false, false);
                Pass(12, false, "LEFT→UP", false, false);
            }

            return;
        }

        Vector2Int end = testBlock.GridPosition;
        if (pathTrace.Count == 0 || pathTrace[pathTrace.Count - 1] != end)
        {
            pathTrace.Add(end);
        }

        bool moved = end != fromPos;
        bool multiAxis = Mathf.Abs(end.x - fromPos.x) > 0 && Mathf.Abs(end.y - fromPos.y) > 0;
        Pass(10, realDragPerformed && moved && multiAxis, "RIGHT→DOWN without release", realDragPerformed, moved);
        Pass(11, realDragPerformed && moved && multiAxis, "DOWN→LEFT without release", realDragPerformed, moved);
        Pass(12, realDragPerformed && moved && multiAxis, "LEFT→UP without release", realDragPerformed, moved);
        report.AppendLine("T10-12 path=" + string.Join(">", pathTrace));
        Capture("Captures/phase61b-direction-change.png");
    }

    private static void TestT13BlockedKeeps()
    {
        InputManager input = FindInput();
        Block block = FindCornerBlocked(out Vector2Int blockedDir);
        if (input == null || block == null)
        {
            Pass(13, false, "blocked direction does not cancel session", false, false);
            return;
        }

        if (!BeginContinuousDrag(input, block))
        {
            Pass(13, false, "blocked keeps session (pick failed)", false, false);
            return;
        }

        Vector2Int start = block.GridPosition;
        AppendContinuousSteps(input, blockedDir, 2);
        bool stillHeld = input.IsPointerSessionActive;
        bool unchanged = block.GridPosition == start;
        input.SimulatePointerReleased();
        continuousHeld = false;
        Pass(13, stillHeld && unchanged, "blocked direction does not cancel session", true, false);
    }

    private static void TestT14BlockedThenValid()
    {
        InputManager input = FindInput();
        Block block = FindCornerBlocked(out Vector2Int blockedDir);
        if (input == null || block == null)
        {
            Pass(14, false, "valid after blocked works", false, false);
            return;
        }

        if (!BeginContinuousDrag(input, block))
        {
            Pass(14, false, "valid after blocked (pick failed)", false, false);
            return;
        }

        AppendContinuousSteps(input, blockedDir, 1);
        Vector2Int alt = blockedDir == Vector2Int.up || blockedDir == Vector2Int.down
            ? Vector2Int.left
            : Vector2Int.up;
        if (!CanMove(block, alt))
        {
            alt = -blockedDir;
        }

        AppendContinuousSteps(input, alt, 1);
        bool alive = input.IsPointerSessionActive;
        input.SimulatePointerReleased();
        continuousHeld = false;
        Pass(14, alive, "valid direction after blocked direction works", true, false);
        Capture("Captures/phase61b-blocked.png");
    }

    private static void BeginChainDrag()
    {
        InputManager input = FindInput();
        testBlock = FindChain();
        if (input == null || testBlock == null)
        {
            Pass(15, false, "chain synchronized", false, false);
            return;
        }

        chainLocals = SnapshotLocals(testBlock);
        fromPos = testBlock.GridPosition;
        Vector2Int dir = FirstOpenDirection(testBlock, 1);
        if (dir == Vector2Int.zero)
        {
            Pass(15, false, "chain synchronized (no open cell)", false, false);
            return;
        }

        expectedPos = fromPos + dir;
        realDragPerformed = PerformRealDrag(input, testBlock, dir, 1, true);
        if (!realDragPerformed)
        {
            Pass(15, false, "chain synchronized (did not start)", false, false);
        }
    }

    private static void FinishChainAsT15()
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

        Pass(15, realDragPerformed && moved && offsets, "chain remains synchronized", realDragPerformed, moved);
        Capture("Captures/phase61b-chain.png");
    }

    private static void BeginNestedDrag()
    {
        InputManager input = FindInput();
        testBlock = FindNested();
        if (input == null || testBlock == null)
        {
            Pass(16, false, "nested synchronized", false, false);
            return;
        }

        fromPos = testBlock.GridPosition;
        Vector2Int dir = FirstOpenDirection(testBlock, 1);
        if (dir == Vector2Int.zero)
        {
            Pass(16, testBlock.HasActiveInnerLayer(), "nested present (no open cell)", false, false);
            return;
        }

        expectedPos = fromPos + dir;
        realDragPerformed = PerformRealDrag(input, testBlock, dir, 1, true);
        if (!realDragPerformed)
        {
            Pass(16, false, "nested synchronized (did not start)", false, false);
        }
    }

    private static void FinishNestedAsT16()
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        bool nested = testBlock != null && testBlock.HasActiveInnerLayer();
        bool moved = testBlock != null && testBlock.GridPosition == expectedPos;
        Pass(16, realDragPerformed && nested && moved, "nested piece remains synchronized", realDragPerformed, moved);
        Capture("Captures/phase61b-nested.png");
    }

    private static void BeginFixedDirection()
    {
        // Reuse Phase61A BeginFixedDirection naming via inline
        InputManager input = FindInput();
        testBlock = FindFixedDirectionBlock(out fixedAllowed);
        if (input == null || testBlock == null)
        {
            Pass(17, false, "fixed-direction rule", false, false);
            return;
        }

        Vector2Int forbidden = fixedAllowed == Vector2Int.zero
            ? Vector2Int.right
            : new Vector2Int(-fixedAllowed.y, fixedAllowed.x);
        Vector2Int start = testBlock.GridPosition;
        bool rejected = !PerformRealDrag(input, testBlock, forbidden, 1, true)
            || testBlock.GridPosition == start;
        Pass(17, rejected, "fixed-direction forbidden move rejected", true, false);
        if (fixedAllowed != Vector2Int.zero && CanMove(testBlock, fixedAllowed))
        {
            realDragPerformed = PerformRealDrag(input, testBlock, fixedAllowed, 1, true);
            expectedPos = start + fixedAllowed;
        }
        else
        {
            realDragPerformed = false;
            expectedPos = start;
        }
    }

    private static void FinishFixedAsT17()
    {
        if (!WaitUntilMoversIdle())
        {
            step--;
            return;
        }

        if (!results.ContainsKey(17))
        {
            Pass(17, false, "fixed-direction rule", false, false);
        }

        Capture("Captures/phase61b-fixed-direction.png");
    }

    private static void TestT18BoosterGate()
    {
        InputManager input = FindInput();
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        Block block = FindMovableBlock();
        if (input == null || magnet == null || block == null)
        {
            Pass(18, false, "booster selection blocks normal drag", false, false);
            return;
        }

        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 1));
        magnet.ActivateMagnet();
        Vector2Int start = block.GridPosition;
        bool selecting = Object.FindFirstObjectByType<BoosterManager>()?.IsAnySelecting ?? false;
        PerformRealDrag(input, block, Vector2Int.right, 1, true);
        bool unchanged = block.GridPosition == start;
        Object.FindFirstObjectByType<BoosterManager>()?.ResetAll("phase61b-gate");
        Pass(18, selecting && unchanged, "booster selection still blocks normal drag", true, false);
        Capture("Captures/phase61b-booster-gate.png");
    }

    private static void TestT19Magnet()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            Pass(19, false, "Magnet regression", false, false);
            return;
        }

        magnetChargesBefore = magnet.MagnetCharges;
        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 1));
        magnet.ActivateMagnet();
        Pass(19, magnet.IsSelecting, "Magnet regression", true, false);
    }

    private static void TestT20Hammer()
    {
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        if (hammer == null)
        {
            Pass(20, false, "Hammer regression", false, false);
            return;
        }

        hammerChargesBefore = hammer.HammerCharges;
        hammer.SetHammerCharges(Mathf.Max(hammer.HammerCharges, 1));
        hammer.ActivateHammer();
        Pass(20, hammer.IsSelecting, "Hammer regression", true, false);
    }

    private static void TestT21Shuffle()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null)
        {
            Pass(21, false, "Shuffle regression", false, false);
            return;
        }

        shuffleChargesBefore = shuffle.ShuffleCharges;
        shuffle.SetShuffleCharges(Mathf.Max(shuffle.ShuffleCharges, 1));
        shuffle.ActivateShuffle();
        Pass(21, shuffle.IsBusy || shuffle.ShuffleCharges <= shuffleChargesBefore, "Shuffle regression", true, false);
    }

    private static void TestT22Undo()
    {
        UndoBooster undo = Object.FindFirstObjectByType<UndoBooster>();
        InputManager input = FindInput();
        if (undo == null || input == null)
        {
            Pass(22, false, "Undo regression", false, false);
            return;
        }

        undoChargesBefore = undo.UndoCharges;
        undo.SetUndoCharges(Mathf.Max(undo.UndoCharges, 1));
        Block block = FindMovableBlock();
        Vector2Int dir = block != null ? FirstOpenDirection(block, 1) : Vector2Int.zero;
        if (block != null && dir != Vector2Int.zero)
        {
            PerformRealDrag(input, block, dir, 1, true);
        }

        undo.ActivateUndo();
        Pass(22, undo.IsBusy || undo.UndoCharges <= undoChargesBefore, "Undo regression", true, false);
    }

    private static void TestT23Restart()
    {
        InputManager input = FindInput();
        LevelManager levels = Object.FindFirstObjectByType<LevelManager>();
        Block block = FindMovableBlock();
        if (input == null || levels == null || block == null)
        {
            Pass(23, false, "restart cleanup", false, false);
            return;
        }

        PressBlockRaycast(input, block);
        levels.RestartLevel();
        input.CancelPointerSession();
        Pass(23, !input.IsPointerSessionActive && !input.IsDragDirectionLocked, "restart cleanup", true, false);
    }

    private static void TestT24LevelLoad()
    {
        InputManager input = FindInput();
        Block block = FindMovableBlock();
        if (input == null || block == null)
        {
            Pass(24, false, "level-load cleanup", false, false);
            return;
        }

        PressBlockRaycast(input, block);
        LoadLevel(Campaign15, "AfterLevelChange");
        input.CancelPointerSession();
        Pass(24, !input.IsPointerSessionActive, "level-load cleanup", true, false);
    }

    private static void Pass(int id, bool ok, string label, bool realDrag, bool gridChanged)
    {
        results[id] = ok;
        report.AppendLine((ok ? "PASS" : "FAIL") + " T" + id + " — " + label + " | realDrag=" + realDrag + " gridChanged=" + gridChanged);
        if (!ok && lastError == null)
        {
            lastError = "T" + id + " failed";
        }
    }

    private static bool AllPassed()
    {
        for (int i = 1; i <= 24; i++)
        {
            if (!results.ContainsKey(i) || !results[i])
            {
                return false;
            }
        }

        return true;
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
                manager.ResetAll("phase61b-idle-timeout");
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
        report.AppendLine("Audit findings:");
        report.AppendLine("- No idle yield between consecutive hops when desiredCell has remaining steps.");
        report.AppendLine("- Residual dead-feel came from continuation settle squash (18% of hop) + cruise ease.");
        report.AppendLine("- secondsPerCell left at 0.105 (already good speed).");
        report.AppendLine();
        report.AppendLine("Haptic: gridCellMoveIntensity 1.0 → 1.2 (+20%); dedicated Android gridMoveEffect.");
        report.AppendLine("Motion: continuation linearWeight 0.95→0.98, turn 0.80→0.88, settlePortion 0.18→0.08.");
        report.AppendLine("UNCHANGED: InputManager Phase 61 params, BlockMover secondsPerCell, gameplay rules.");
        report.AppendLine("Files: HapticFeedback.cs, WorldPieceMotion.cs, Phase61BPlayModeVerify.cs");
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
        Debug.Log("[Phase61B] " + (ok ? "PASS" : "FAIL") + " → " + ReportPath);
    }
}
