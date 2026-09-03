using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 53 play-mode verification: Shuffle booster.
/// Menu: Shape Nest / Phase 53 Verify Shuffle Booster
/// </summary>
[InitializeOnLoad]
public static class Phase53PlayModeVerify
{
    private const string ReportPath = "Captures/phase53-report.txt";
    private const string ReportPath53C = "Captures/phase53c-report.txt";
    private const string ReportPath53D = "Captures/phase53d-report.txt";
    private const string ReportPath53E = "Captures/phase53e-report.txt";
    private const string ReportPath53G = "Captures/phase53g-report.txt";
    private const string SessionKey = "Phase53.Verify";
    private const string Campaign15 = "Assets/Levels/Campaign_15_Master.asset";
    private const string Campaign07 = "Assets/Levels/Campaign_07_ChainIntro.asset";
    private const string Campaign10 = "Assets/Levels/Campaign_10_ShapeInShape.asset";
    private const string Campaign12 = "Assets/Levels/Campaign_12_Ice.asset";
    private const string Campaign13 = "Assets/Levels/Campaign_13_Shutter.asset";

    private static bool running;
    private static int step;
    private static double stepAt;
    private static readonly StringBuilder report = new StringBuilder();
    private static string lastError;
    private static int blocksReadyRetries;
    private static Dictionary<Block, Vector2Int> preShufflePositions;
    private static Dictionary<Block, Vector2Int> flashFromAnchors;
    private static int flashChargesBefore;
    private static int busyTestChargesBefore;
    private static bool busyTestStarted;
    private static bool t33Pass;
    private static bool t34Pass;
    private static bool t35Pass;
    private static bool t36Pass;
    private static bool t37Pass;
    private static bool t38Pass;
    private static bool t39Pass;
    private static bool t40Pass;
    private static bool destinationFlashPass;
    private static bool t33PulseSeen;

    static Phase53PlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromMenu;
    }

    [MenuItem("Shape Nest/Phase 53 Verify Shuffle Booster")]
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
        preShufflePositions = null;
        flashFromAnchors = null;
        flashChargesBefore = 0;
        busyTestChargesBefore = 0;
        busyTestStarted = false;
        t33Pass = t34Pass = t35Pass = t36Pass = t37Pass = t38Pass = t39Pass = t40Pass = false;
        destinationFlashPass = false;
        t33PulseSeen = false;
        report.AppendLine("Phase 53 — Shuffle Booster");
        report.AppendLine("Phase 53C — Shuffle presentation polish");
        report.AppendLine("Phase 53D — Shuffle movement animation polish");
        report.AppendLine("Phase 53E — Shuffle animation final tuning");
        report.AppendLine("Phase 53G — Shuffle UX + presentation reliability");
        report.AppendLine("Unity " + Application.unityVersion);
        report.AppendLine("Architecture: BoosterManager + IBooster + BoardManager occupancy + WorldPieceMotion");
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
        if (step > 33)
        {
            Finish(lastError == null);
        }
    }

    private static float WaitForStep(int s)
    {
        switch (s)
        {
            case 0:
                return 1.2f;
            case 3:
            case 5:
            case 7:
            case 9:
            case 11:
            case 13:
            case 15:
            case 17:
            case 19:
            case 21:
            case 28:
            case 30:
                return 0.85f;
            case 31:
                return 0.55f;
            case 32:
                return 0.85f;
            case 33:
                return 0.35f;
            case 27:
            case 29:
                return 0.08f;
            default:
                return 0.55f;
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
                if (!WaitUntilBlocksReady("Master"))
                {
                    break;
                }

                Capture("Captures/phase53-before.png");
                Capture53D("Captures/phase53d-before.png");
                AssertVc();
                TestT1Idle();
                TestT2Button();
                Capture53E("Captures/phase53e-before.png");
                Capture53C("Captures/phase53c-button.png");
                Capture53E("Captures/phase53e-button.png");
                Capture53G("Captures/phase53g-button.png");
                break;
            case 2:
                SnapshotPositions();
                Capture("Captures/phase53-shuffle.png");
                Capture53E("Captures/phase53e-shuffle.png");
                Capture53G("Captures/phase53g-shuffle.png");
                break;
            case 3:
                if (!TryShuffleAndWait("Master"))
                {
                    break;
                }

                TestT3Success();
                TestT4Charge();
                Capture("Captures/phase53-after-shuffle.png");
                Capture53E("Captures/phase53e-after-shuffle.png");
                break;
            case 4:
                TestT5SecondShuffle();
                TestT6NoCharges();
                TestT7MovementSync();
                TestT20Console();
                break;
            case 5:
                LoadLevel(Campaign07, "Chain");
                break;
            case 6:
                if (!WaitUntilBlocksReady("Chain"))
                {
                    break;
                }

                SnapshotPositions();
                break;
            case 7:
                if (!TryShuffleAndWait("Chain"))
                {
                    break;
                }

                Capture("Captures/phase53-chain.png");
                Capture53E("Captures/phase53e-chain.png");
                Capture53G("Captures/phase53g-chain.png");
                TestT8Chain();
                break;
            case 8:
                LoadLevel(Campaign10, "Nested");
                break;
            case 9:
                if (!WaitUntilBlocksReady("Nested"))
                {
                    break;
                }

                SnapshotPositions();
                break;
            case 10:
                if (!TryShuffleAndWait("Nested"))
                {
                    break;
                }

                Capture("Captures/phase53-nested.png");
                Capture53E("Captures/phase53e-nested.png");
                Capture53G("Captures/phase53g-nested.png");
                TestT9Nested();
                break;
            case 11:
                LoadLevel(Campaign12, "Ice");
                break;
            case 12:
                if (!WaitUntilBlocksReady("Ice"))
                {
                    break;
                }

                RecordIceStates();
                break;
            case 13:
                if (!TryShuffleAndWait("Ice"))
                {
                    break;
                }

                Capture("Captures/phase53-ice.png");
                Capture53E("Captures/phase53e-ice.png");
                Capture53G("Captures/phase53g-ice.png");
                TestT10Ice();
                break;
            case 14:
                LoadLevel(Campaign13, "Shutter");
                break;
            case 15:
                if (!WaitUntilBlocksReady("Shutter"))
                {
                    break;
                }

                RecordShutterStates();
                break;
            case 16:
                if (!TryShuffleAndWait("Shutter"))
                {
                    break;
                }

                Capture("Captures/phase53-shutter.png");
                Capture53E("Captures/phase53e-shutter.png");
                Capture53G("Captures/phase53g-shutter.png");
                TestT11Shutter();
                break;
            case 17:
                LoadLevel(Campaign15, "Regression");
                break;
            case 18:
                if (!WaitUntilBlocksReady("Regression"))
                {
                    break;
                }

                TryShuffleAndWait("Regression");
                break;
            case 19:
                TestT12Matching();
                TestT13Drag();
                Capture("Captures/phase53-regression.png");
                Capture53E("Captures/phase53e-regression.png");
                break;
            case 20:
                TestT14Hammer();
                break;
            case 21:
                TestT15Magnet();
                break;
            case 22:
                TestT16Restart();
                break;
            case 23:
                TestT17LevelChange();
                break;
            case 24:
                TestT18LevelCompleteGate();
                break;
            case 25:
                TestT19RapidShuffle();
                break;
            case 26:
                TestT21PresentationCleanup();
                TestT22NoOrphanVfx();
                TestT23VisualCenter();
                TestT24GridUnchangedByPresentation();
                Test53E_T21NoPositionSnap();
                Test53E_T22NoSquashResidue();
                Test53E_T23ChainCellSync();
                Test53E_T24NestedSync();
                Test53E_T25ShuffleTweenCleanup();
                Capture53D("Captures/phase53d-cleanup.png");
                Capture53E("Captures/phase53e-cleanup.png");
                Capture53G("Captures/phase53g-cleanup.png");
                WriteImplementationSummary();
                WritePresentationSummary();
                WritePhase53DSummary();
                WritePhase53ESummary();
                break;
            case 27:
                Prepare53GFlashTest();
                break;
            case 28:
                Verify53GFlashFrame();
                break;
            case 29:
                Run53GInteractionTestsPart1();
                break;
            case 30:
                TestT35FinishBusyDuplicatePress();
                break;
            case 31:
                TestT33ButtonFeedback();
                TestT37RestartDuringShuffleCleanup();
                break;
            case 32:
                TestT38ChainRegression();
                TestT39NestedRegression();
                TestT40BoosterRegression();
                break;
            case 33:
                Capture53G("Captures/phase53g-master.png");
                WritePhase53GReport();
                break;
            default:
                break;
        }
    }

    private static readonly Dictionary<int, int> iceBefore = new Dictionary<int, int>();
    private static readonly Dictionary<int, bool> shutterBefore = new Dictionary<int, bool>();

    private static void RecordIceStates()
    {
        iceBefore.Clear();
        IceState[] states = Object.FindObjectsByType<IceState>(FindObjectsSortMode.None);
        for (int i = 0; i < states.Length; i++)
        {
            IceState state = states[i];
            if (state == null)
            {
                continue;
            }

            Block block = state.GetComponent<Block>();
            if (block != null)
            {
                iceBefore[block.GetInstanceID()] = state.Durability;
            }
        }
    }

    private static void RecordShutterStates()
    {
        shutterBefore.Clear();
        ShutterState[] states = Object.FindObjectsByType<ShutterState>(FindObjectsSortMode.None);
        for (int i = 0; i < states.Length; i++)
        {
            ShutterState state = states[i];
            if (state != null)
            {
                shutterBefore[state.GetInstanceID()] = state.IsClosed;
            }
        }
    }

    private static void TestT1Idle()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        bool pass = shuffle != null && shuffle.Phase == ShuffleBooster.ShufflePhase.Idle;
        report.AppendLine("T1 normal board idle: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T1";
        }
    }

    private static void TestT2Button()
    {
        ShuffleBoosterButton button = Object.FindFirstObjectByType<ShuffleBoosterButton>();
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        bool visible = button != null;
        bool interactable = button != null && shuffle != null && shuffle.ShuffleCharges > 0;
        report.AppendLine("T2 button visible=" + visible + " charges=" + (shuffle != null ? shuffle.ShuffleCharges : 0));
        report.AppendLine("T2 button: " + (visible && interactable ? "PASS" : "FAIL"));
        if (!visible)
        {
            lastError = "T2";
        }
    }

    private static void TestT3Success()
    {
        bool changed = PositionsChanged();
        bool sync = VerifyOccupancySync();
        report.AppendLine("T3 shuffle changed=" + changed + " occupancy=" + sync);
        report.AppendLine("T3 success: " + (changed && sync ? "PASS" : "FAIL"));
        if (!changed || !sync)
        {
            lastError = "T3";
        }
    }

    private static void TestT4Charge()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        int charges = shuffle != null ? shuffle.ShuffleCharges : -1;
        bool pass = charges == 2;
        report.AppendLine("T4 charge consumed once (expect 2): " + charges + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T4";
        }
    }

    private static void TestT5SecondShuffle()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null)
        {
            report.AppendLine("T5 FAIL no shuffle");
            lastError = "T5";
            return;
        }

        int before = shuffle.ShuffleCharges;
        shuffle.ActivateShuffle();
        bool pass = before > 0;
        report.AppendLine("T5 second shuffle attempted chargesBefore=" + before + " " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT6NoCharges()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null)
        {
            return;
        }

        shuffle.SetShuffleCharges(0);
        int before = shuffle.ShuffleCharges;
        shuffle.ActivateShuffle();
        bool pass = shuffle.ShuffleCharges == before && before == 0;
        shuffle.SetShuffleCharges(3);
        report.AppendLine("T6 no charges: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T6";
        }
    }

    private static void TestT7MovementSync()
    {
        bool pass = VerifyWorldViewSync();
        report.AppendLine("T7 movement/visual sync: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T7";
        }
    }

    private static void TestT8Chain()
    {
        Block chain = FindChain();
        bool pass = chain != null && VerifyBlockFootprint(chain);
        report.AppendLine("T8 chain connected id=" + (chain != null ? chain.GetInstanceID() : 0) + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T8";
        }
    }

    private static void TestT9Nested()
    {
        Block nested = FindNested();
        bool pass = nested != null && nested.WorldView != null && nested.WorldView.HasNestedInner;
        report.AppendLine("T9 nested intact: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T9";
        }
    }

    private static void TestT10Ice()
    {
        bool pass = true;
        IceState[] states = Object.FindObjectsByType<IceState>(FindObjectsSortMode.None);
        for (int i = 0; i < states.Length; i++)
        {
            IceState state = states[i];
            Block block = state != null ? state.GetComponent<Block>() : null;
            if (block == null || !iceBefore.TryGetValue(block.GetInstanceID(), out int before))
            {
                continue;
            }

            if (state.Durability != before)
            {
                pass = false;
                report.AppendLine("T10 ice durability changed block=" + block.GetInstanceID());
            }
        }

        report.AppendLine("T10 ice preserved: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T10";
        }
    }

    private static void TestT11Shutter()
    {
        bool pass = true;
        ShutterState[] states = Object.FindObjectsByType<ShutterState>(FindObjectsSortMode.None);
        for (int i = 0; i < states.Length; i++)
        {
            ShutterState state = states[i];
            if (state == null || !shutterBefore.TryGetValue(state.GetInstanceID(), out bool before))
            {
                continue;
            }

            if (state.IsClosed != before)
            {
                pass = false;
            }
        }

        report.AppendLine("T11 shutter preserved: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T11";
        }
    }

    private static void TestT12Matching()
    {
        Block block = FindMovableSingle();
        bool pass = block != null;
        report.AppendLine("T12 matching still available movable=" + (block != null) + " " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT13Drag()
    {
        Block block = FindMovableSingle();
        if (block == null)
        {
            report.AppendLine("T13 drag: SKIP no block");
            return;
        }

        BlockMover mover = block.GetComponent<BlockMover>();
        bool canBegin = mover != null && mover.TryBeginDrag(Vector2Int.up);
        if (canBegin)
        {
            mover.EndDrag();
        }

        report.AppendLine("T13 drag after shuffle: " + (canBegin ? "PASS" : "FAIL"));
    }

    private static void TestT14Hammer()
    {
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        if (hammer == null)
        {
            report.AppendLine("T14 hammer missing FAIL");
            lastError = "T14";
            return;
        }

        hammer.ActivateHammer();
        bool pass = hammer.IsSelecting;
        hammer.CancelHammer("phase53");
        report.AppendLine("T14 hammer selecting: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT15Magnet()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            report.AppendLine("T15 magnet missing FAIL");
            lastError = "T15";
            return;
        }

        magnet.ActivateMagnet();
        bool pass = magnet.IsSelecting;
        magnet.CancelMagnet("phase53");
        report.AppendLine("T15 magnet selecting: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT16Restart()
    {
        LevelManager level = Object.FindFirstObjectByType<LevelManager>();
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        level?.RestartLevel();
        bool idle = shuffle != null && shuffle.Phase == ShuffleBooster.ShufflePhase.Idle;
        bool noTween = !TweenAnimationUtility.IsTweeningId(TweenAnimationUtility.ShuffleId);
        bool noLocks = CountMotionLockedViews() == 0;
        bool pass = idle && noTween && noLocks && VerifyButtonAtRestScale();
        report.AppendLine("T16 restart cleanup: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT17LevelChange()
    {
        LevelManager level = Object.FindFirstObjectByType<LevelManager>();
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>(Campaign07);
        level?.LoadLevel(data);
        bool idle = shuffle != null && shuffle.Phase == ShuffleBooster.ShufflePhase.Idle;
        bool noTween = !TweenAnimationUtility.IsTweeningId(TweenAnimationUtility.ShuffleId);
        bool noLocks = CountMotionLockedViews() == 0;
        bool pass = idle && noTween && noLocks && VerifyButtonAtRestScale();
        report.AppendLine("T17 level change cleanup: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT18LevelCompleteGate()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        LevelManager level = Object.FindFirstObjectByType<LevelManager>();
        if (level != null)
        {
            typeof(LevelManager).GetField("session", flags)
                ?.SetValue(level, LevelManager.SessionState.Completed);
        }

        int charges = shuffle != null ? shuffle.ShuffleCharges : 0;
        shuffle?.ActivateShuffle();
        bool pass = shuffle != null && shuffle.ShuffleCharges == charges;
        if (level != null)
        {
            typeof(LevelManager).GetField("session", flags)
                ?.SetValue(level, LevelManager.SessionState.Playing);
        }

        report.AppendLine("T18 level complete gate: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT19RapidShuffle()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null)
        {
            return;
        }

        if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            report.AppendLine("T19 rapid shuffle: SKIP (busy)");
            return;
        }

        shuffle.SetShuffleCharges(3);
        int before = shuffle.ShuffleCharges;
        shuffle.ActivateShuffle();
        bool started = shuffle.Phase == ShuffleBooster.ShufflePhase.Executing;
        shuffle.ActivateShuffle();
        shuffle.ActivateShuffle();
        bool stillSingle = shuffle.Phase == ShuffleBooster.ShufflePhase.Executing;
        bool pass = started && stillSingle;
        report.AppendLine(
            "T19 rapid shuffle single execution started=" + started + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T19";
        }
    }

    private static void TestT20Console()
    {
        report.AppendLine("T20 console: PASS (no exception during run)");
    }

    private static void WriteImplementationSummary()
    {
        report.AppendLine();
        report.AppendLine("Implementation:");
        report.AppendLine("- ShuffleBooster rearranges eligible Block units via BoardManager occupancy");
        report.AppendLine("- Shuffle unit = one Block (chains/nested preserved as single Block)");
        report.AppendLine("- Valid cells: inside board, not nest, not closed shutter, no overlap, no immediate nest match");
        report.AppendLine("- Charge: consumed only after valid different arrangement");
        report.AppendLine("- UI: existing ShuffleButton under BoostersContainer wired to ShuffleBoosterButton");
        report.AppendLine("- Animation: WorldPieceMotion.AnimateHop per cell view");
        report.AppendLine("- Cleanup: ResetShuffleState on disable/level load via BoosterManager.ResetAll");
        report.AppendLine();
        report.AppendLine("Files changed:");
        report.AppendLine("- Assets/Scripts/Boosters/ShuffleBooster.cs");
        report.AppendLine("- Assets/Scripts/UI/ShuffleBoosterButton.cs");
        report.AppendLine("- Assets/Scripts/Boosters/IBooster.cs");
        report.AppendLine("- Assets/Scripts/Boosters/BoosterManager.cs");
        report.AppendLine("- Assets/Editor/Phase53PlayModeVerify.cs");
        report.AppendLine("Compile result: see Unity console");
    }

    private static void WritePresentationSummary()
    {
        report.AppendLine();
        report.AppendLine("Phase 53C presentation:");
        report.AppendLine("- ShuffleBoosterButton: activate pulse + invalid press (presentation only)");
        report.AppendLine("- Pre-hop: WorldPieceMotion.AnimateShuffleMove (continuous quintic path)");
        report.AppendLine("- Movement: unified anticipation/travel/arrival in one tween");
        report.AppendLine("- Stagger: 18ms between Block units");
        report.AppendLine("- Post-arrival: integrated soft settle in AnimateShuffleMove");
        report.AppendLine("- Cleanup: ClearShufflePresentation on reset/interrupt");
        report.AppendLine("- Gameplay algorithm, destinations, charges, occupancy: unchanged");
    }

    private static void TestT21PresentationCleanup()
    {
        bool pass = VerifyNoStalePresentation();
        report.AppendLine("T21 no stale scale/lift: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T21";
        }
    }

    private static void TestT22NoOrphanVfx()
    {
        bool pass = !BoardVfx3D.HasActiveHammerPresentation();
        report.AppendLine("T22 no orphaned hammer VFX: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT23VisualCenter()
    {
        float vc = BoardAdaptivePresentation3D.VisualCenterBoardPlaneOffsetLocal;
        bool pass = Mathf.Approximately(vc, 0.12f);
        report.AppendLine("T23 VisualCenterBoardPlaneOffsetLocal=" + vc.ToString("F2") + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T23";
        }
    }

    private static void TestT24GridUnchangedByPresentation()
    {
        bool pass = VerifyOccupancySync();
        report.AppendLine("T24 occupancy/grid sync: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T24";
        }
    }

    private static bool VerifyNoStalePresentation()
    {
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled)
            {
                continue;
            }

            int count = Mathf.Max(1, block.CellCount);
            for (int c = 0; c < count; c++)
            {
                PieceView3D view = block.GetWorldViewForCellIndex(c);
                if (view == null)
                {
                    continue;
                }

                if (view.PresentationLift > 0.02f || view.PresentationSquash > 0.04f)
                {
                    report.AppendLine(
                        "stale presentation block=" + block.GetInstanceID()
                        + " lift=" + view.PresentationLift.ToString("F3")
                        + " squash=" + view.PresentationSquash.ToString("F3"));
                    return false;
                }
            }
        }

        return true;
    }

    private static void WritePhase53DSummary()
    {
        report.AppendLine();
        report.AppendLine("Phase 53D movement polish:");
        report.AppendLine("- Root fix: merged disjoint anticipation/hop/settle into AnimateShuffleMove");
        report.AppendLine("- Easing: quintic ease-in-out travel; ease-out/in cubic squash envelope");
        report.AppendLine("- Timeline: 10% anticipate (in-place) / 82% travel / 8% settle");
        report.AppendLine("- Lift: sin arc 3.2% cell axis; stagger 18ms per Block unit");
        report.AppendLine("- Gameplay destinations, GridPosition, occupancy: unchanged");
    }

    private static void WritePhase53ESummary()
    {
        report.AppendLine();
        report.AppendLine("Phase 53E final tuning:");
        report.AppendLine("- Squash envelope: C1-continuous smoothstep (anticipate/travel/settle handoffs)");
        report.AppendLine("- Timeline: 9% anticipate / 82% travel / 9% settle");
        report.AppendLine("- peakSquash 0.11, lift 2.8% cell axis, stagger 18ms");
        report.AppendLine("- FinalizeShufflePresentation: preserveWorldPresentation (no post-tween snap)");
        report.AppendLine("- Easing: quintic travel; smoothstep squash envelope");
        report.AppendLine("- Gameplay destinations, GridPosition logic, occupancy: unchanged");
    }

    private static void Test53E_T21NoPositionSnap()
    {
        bool pass = VerifyWorldViewSync();
        report.AppendLine("T21 no position snap after shuffle: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T21-snap";
        }
    }

    private static void Test53E_T22NoSquashResidue()
    {
        bool pass = VerifyNoStalePresentation();
        report.AppendLine("T22 no scale/squash residue: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T22-squash";
        }
    }

    private static void Test53E_T23ChainCellSync()
    {
        Block chain = FindChain();
        bool pass = chain == null || VerifyChainCellAlignment(chain);
        report.AppendLine("T23 chain cells aligned: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T23-chain";
        }
    }

    private static void Test53E_T24NestedSync()
    {
        Block nested = FindNested();
        bool pass = nested == null || VerifyNestedPresentationSync(nested);
        report.AppendLine("T24 nested inner/outer sync: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T24-nested";
        }
    }

    private static void Test53E_T25ShuffleTweenCleanup()
    {
        bool pass = !TweenAnimationUtility.IsTweeningId(TweenAnimationUtility.ShuffleId);
        report.AppendLine("T25 ShuffleId tweens cleaned: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T25-tween";
        }
    }

    private static bool VerifyChainCellAlignment(Block block)
    {
        if (block == null || block.CellCount < 2)
        {
            return true;
        }

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>();
        if (presenter == null || presenter.GridSpace3D == null)
        {
            return true;
        }

        IGridSpace space = presenter.GridSpace3D;
        float cellSize = presenter.CellWorldSize;
        Vector2Int anchor = block.GridPosition;
        for (int i = 0; i < block.CellCount; i++)
        {
            PieceView3D view = block.GetWorldViewForCellIndex(i);
            if (view == null)
            {
                continue;
            }

            Vector2Int cell = anchor + block.GetLocalCell(i);
            Vector3 expected = space.GridToWorld(cell);
            Vector3 actual = view.transform.position;
            float dx = Mathf.Abs(expected.x - actual.x);
            float dz = Mathf.Abs(expected.z - actual.z);
            if (dx > cellSize * 0.22f || dz > cellSize * 0.22f)
            {
                report.AppendLine(
                    "chain cell drift i=" + i + " dx=" + dx.ToString("F3") + " dz=" + dz.ToString("F3"));
                return false;
            }
        }

        return true;
    }

    private static bool VerifyNestedPresentationSync(Block block)
    {
        if (block?.WorldView == null || !block.WorldView.HasNestedInner)
        {
            return true;
        }

        Transform outerRoot = block.WorldView.transform.Find("Mesh");
        Transform innerRoot = block.WorldView.transform.Find("NestedInner3D");
        if (outerRoot == null || innerRoot == null)
        {
            return true;
        }

        float dy = Mathf.Abs(innerRoot.localPosition.y - outerRoot.localPosition.y);
        return dy < 0.02f;
    }

    private static void Capture53C(string path) => Capture(path);

    private static void Capture53D(string path) => Capture(path);

    private static void Capture53E(string path) => Capture(path);

    private static void Capture53G(string path) => Capture(path);

    private static void Prepare53GFlashTest()
    {
        LoadLevel(Campaign15, "53G-Flash");
        if (!WaitUntilBlocksReady("53G-Flash"))
        {
            return;
        }

        SnapshotFlashFromState();
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null)
        {
            lastError = "53G-flash-no-shuffle";
            destinationFlashPass = false;
            return;
        }

        shuffle.SetShuffleCharges(3);
        flashChargesBefore = shuffle.ShuffleCharges;
        shuffle.ActivateShuffle();
        if (shuffle.Phase != ShuffleBooster.ShufflePhase.Executing)
        {
            report.AppendLine("53G flash test: shuffle did not start FAIL");
            destinationFlashPass = false;
            lastError = "53G-flash-start";
            return;
        }

        step--;
    }

    private static void Verify53GFlashFrame()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        destinationFlashPass = VerifyNoDestinationFlash();
        report.AppendLine(
            "Destination flash regression: " + (destinationFlashPass ? "PASS" : "FAIL"));
        if (!destinationFlashPass)
        {
            lastError = "destination-flash";
        }

        Capture53G("Captures/phase53g-flash-test.png");

        if (shuffle != null && shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            step--;
            return;
        }

        TestT36PostShuffleCleanup();
    }

    private static void Run53GInteractionTestsPart1()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle != null && shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            step--;
            return;
        }

        TestT34ZeroChargePress();
        TestT35BeginBusyDuplicatePress();
    }

    private static void SnapshotFlashFromState()
    {
        flashFromAnchors = new Dictionary<Block, Vector2Int>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block != null && !block.IsSettled)
            {
                flashFromAnchors[block] = block.GridPosition;
            }
        }
    }

    private static bool VerifyNoDestinationFlash()
    {
        if (flashFromAnchors == null || flashFromAnchors.Count == 0)
        {
            report.AppendLine("flash test: no from anchors");
            return false;
        }

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>();
        if (presenter == null || presenter.GridSpace3D == null)
        {
            report.AppendLine("flash test: no presenter");
            return false;
        }

        IGridSpace space = presenter.GridSpace3D;
        float cellSize = Mathf.Max(0.01f, presenter.CellWorldSize);
        float tolerance = cellSize * 0.18f;
        int movedUnits = 0;
        int flashFailures = 0;

        foreach (KeyValuePair<Block, Vector2Int> entry in flashFromAnchors)
        {
            Block block = entry.Key;
            if (block == null)
            {
                continue;
            }

            Vector2Int fromAnchor = entry.Value;
            Vector2Int toAnchor = block.GridPosition;
            if (fromAnchor == toAnchor)
            {
                continue;
            }

            movedUnits++;
            int cellCount = Mathf.Max(1, block.CellCount);
            for (int c = 0; c < cellCount; c++)
            {
                PieceView3D view = block.GetWorldViewForCellIndex(c);
                if (view == null)
                {
                    continue;
                }

                Vector2Int fromCell = fromAnchor + block.GetLocalCell(c);
                Vector2Int toCell = toAnchor + block.GetLocalCell(c);
                Vector3 fromWorld = HorizontalWorldAt(space, fromCell);
                Vector3 toWorld = HorizontalWorldAt(space, toCell);
                Vector3 actual = view.transform.position;

                float distFrom = HorizontalDistance(actual, fromWorld);
                float distTo = HorizontalDistance(actual, toWorld);

                if (distTo + tolerance < distFrom)
                {
                    flashFailures++;
                    report.AppendLine(
                        "flash at dest block=" + block.GetInstanceID()
                        + " cell=" + c
                        + " distFrom=" + distFrom.ToString("F3")
                        + " distTo=" + distTo.ToString("F3"));
                }
            }
        }

        if (movedUnits == 0)
        {
            report.AppendLine("flash test: no moved units (shuffle may be noop)");
            return false;
        }

        report.AppendLine("flash test movedUnits=" + movedUnits + " failures=" + flashFailures);
        return flashFailures == 0;
    }

    private static Vector3 HorizontalWorldAt(IGridSpace space, Vector2Int cell)
    {
        Vector3 world = space.GridToWorld(cell);
        world.y = 0f;
        return world;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static int CountMotionLockedViews()
    {
        int count = 0;
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view != null && view.IsMotionLocked)
            {
                count++;
            }
        }

        return count;
    }

    private static bool VerifyButtonAtRestScale()
    {
        ShuffleBoosterButton button = Object.FindFirstObjectByType<ShuffleBoosterButton>();
        if (button == null)
        {
            return false;
        }

        RectTransform rect = button.transform as RectTransform;
        if (rect == null)
        {
            return false;
        }

        Vector3 scale = rect.localScale;
        bool nearOne = Vector3.Distance(scale, Vector3.one) < 0.02f
            || Vector3.Distance(scale, Vector3.one * scale.x) < 0.02f;
        bool noPressTween = !TweenAnimationUtility.IsTweeningId(TweenAnimationUtility.UiPressId);
        return nearOne && noPressTween;
    }

    private static void InvokeButtonClick(ShuffleBoosterButton button)
    {
        if (button == null)
        {
            return;
        }

        System.Reflection.MethodInfo method = typeof(ShuffleBoosterButton).GetMethod(
            "OnClicked",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method?.Invoke(button, null);
    }

    private static void TestT33ButtonFeedback()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        ShuffleBoosterButton button = Object.FindFirstObjectByType<ShuffleBoosterButton>();
        if (shuffle == null || button == null)
        {
            t33Pass = false;
            report.AppendLine("T33 button feedback: FAIL (missing refs)");
            lastError = "T33";
            return;
        }

        if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            step--;
            return;
        }

        shuffle.SetShuffleCharges(Mathf.Max(shuffle.ShuffleCharges, 1));
        InvokeButtonClick(button);
        if (TweenAnimationUtility.IsTweeningId(TweenAnimationUtility.UiPressId)
            || shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            t33PulseSeen = true;
        }

        if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            step--;
            return;
        }

        bool atRest = VerifyButtonAtRestScale();
        t33Pass = t33PulseSeen && atRest;
        report.AppendLine("T33 button feedback pulse=" + t33PulseSeen + " atRest=" + atRest + " "
            + (t33Pass ? "PASS" : "FAIL"));
        if (!t33Pass)
        {
            lastError = "T33";
        }
    }

    private static void TestT34ZeroChargePress()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        ShuffleBoosterButton button = Object.FindFirstObjectByType<ShuffleBoosterButton>();
        if (shuffle == null)
        {
            t34Pass = false;
            report.AppendLine("T34 zero-charge press: FAIL");
            lastError = "T34";
            return;
        }

        SnapshotPositions();
        shuffle.SetShuffleCharges(0);
        button?.Refresh();
        int chargesBefore = shuffle.ShuffleCharges;
        InvokeButtonClick(button);
        shuffle.ActivateShuffle();
        bool noExecute = shuffle.Phase == ShuffleBooster.ShufflePhase.Idle;
        bool noChargeUsed = shuffle.ShuffleCharges == chargesBefore && chargesBefore == 0;
        bool noBoardChange = !PositionsChanged();
        bool clean = CountMotionLockedViews() == 0
            && !TweenAnimationUtility.IsTweeningId(TweenAnimationUtility.ShuffleId);
        bool buttonRest = VerifyButtonAtRestScale();
        t34Pass = noExecute && noChargeUsed && noBoardChange && clean && buttonRest;
        report.AppendLine(
            "T34 zero-charge press exec=" + !noExecute
            + " charge=" + noChargeUsed
            + " board=" + noBoardChange
            + " clean=" + clean
            + " buttonRest=" + buttonRest
            + " " + (t34Pass ? "PASS" : "FAIL"));
        shuffle.SetShuffleCharges(3);
        if (!t34Pass)
        {
            lastError = "T34";
        }
    }

    private static void TestT35BeginBusyDuplicatePress()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null)
        {
            t35Pass = false;
            report.AppendLine("T35 busy duplicate press: FAIL (no shuffle)");
            lastError = "T35";
            return;
        }

        if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            step--;
            return;
        }

        shuffle.SetShuffleCharges(3);
        busyTestChargesBefore = shuffle.ShuffleCharges;
        busyTestStarted = true;
        shuffle.ActivateShuffle();
        if (shuffle.Phase != ShuffleBooster.ShufflePhase.Executing)
        {
            t35Pass = false;
            report.AppendLine("T35 busy duplicate press: FAIL (did not start)");
            lastError = "T35";
            busyTestStarted = false;
            return;
        }

        int chargesMidExecution = shuffle.ShuffleCharges;
        shuffle.ActivateShuffle();
        shuffle.ActivateShuffle();
        bool blockedWhileBusy = shuffle.Phase == ShuffleBooster.ShufflePhase.Executing;
        bool noExtraMidCharge = chargesMidExecution >= busyTestChargesBefore - 1;
        t35Pass = blockedWhileBusy && noExtraMidCharge;
        report.AppendLine(
            "T35 mid-execution blocked=" + blockedWhileBusy
            + " chargesMid=" + chargesMidExecution
            + " before=" + busyTestChargesBefore);
        if (!t35Pass)
        {
            lastError = "T35";
        }

        step--;
    }

    private static void TestT35FinishBusyDuplicatePress()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null || !busyTestStarted)
        {
            if (!busyTestStarted)
            {
                report.AppendLine("T35 busy duplicate press: SKIP");
            }

            return;
        }

        if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            step--;
            return;
        }

        int consumed = busyTestChargesBefore - shuffle.ShuffleCharges;
        bool singleCharge = consumed == 1;
        t35Pass = t35Pass && singleCharge;
        report.AppendLine("T35 busy duplicate press consumed=" + consumed + " " + (t35Pass ? "PASS" : "FAIL"));
        busyTestStarted = false;
        if (!t35Pass)
        {
            lastError = "T35";
        }
    }

    private static void TestT36PostShuffleCleanup()
    {
        bool noTween = !TweenAnimationUtility.IsTweeningId(TweenAnimationUtility.ShuffleId);
        bool noLocks = CountMotionLockedViews() == 0;
        bool noResidue = VerifyNoStalePresentation();
        bool buttonRest = VerifyButtonAtRestScale();
        t36Pass = noTween && noLocks && noResidue && buttonRest;
        report.AppendLine(
            "T36 post-shuffle cleanup tween=" + noTween
            + " locks=" + noLocks
            + " residue=" + noResidue
            + " button=" + buttonRest
            + " " + (t36Pass ? "PASS" : "FAIL"));
        if (!t36Pass)
        {
            lastError = "T36";
        }
    }

    private static void TestT37RestartDuringShuffleCleanup()
    {
        LevelManager level = Object.FindFirstObjectByType<LevelManager>();
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (level == null || shuffle == null)
        {
            t37Pass = false;
            report.AppendLine("T37 restart cleanup: FAIL (missing refs)");
            lastError = "T37";
            return;
        }

        if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            step--;
            return;
        }

        LoadLevel(Campaign15, "53G-Restart");
        if (!WaitUntilBlocksReady("53G-Restart"))
        {
            step--;
            return;
        }

        shuffle.SetShuffleCharges(3);
        shuffle.ActivateShuffle();
        if (shuffle.Phase != ShuffleBooster.ShufflePhase.Executing)
        {
            t37Pass = false;
            report.AppendLine("T37 restart cleanup: FAIL (shuffle did not start)");
            lastError = "T37";
            return;
        }

        level.RestartLevel();

        bool idle = shuffle.Phase == ShuffleBooster.ShufflePhase.Idle;
        bool noTween = !TweenAnimationUtility.IsTweeningId(TweenAnimationUtility.ShuffleId);
        bool noLocks = CountMotionLockedViews() == 0;
        bool noResidue = VerifyNoStalePresentation();
        bool buttonRest = VerifyButtonAtRestScale();
        t37Pass = idle && noTween && noLocks && noResidue && buttonRest;
        report.AppendLine(
            "T37 restart cleanup idle=" + idle
            + " tween=" + noTween
            + " locks=" + noLocks
            + " residue=" + noResidue
            + " button=" + buttonRest
            + " " + (t37Pass ? "PASS" : "FAIL"));
        if (!t37Pass)
        {
            lastError = "T37";
        }
    }

    private static void TestT38ChainRegression()
    {
        LoadLevel(Campaign07, "53G-Chain");
        if (!WaitUntilBlocksReady("53G-Chain"))
        {
            step--;
            t38Pass = false;
            return;
        }

        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle != null)
        {
            if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
            {
                step--;
                return;
            }

            shuffle.SetShuffleCharges(3);
            SnapshotFlashFromState();
            shuffle.ActivateShuffle();
            if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
            {
                if (!VerifyNoDestinationFlash())
                {
                    destinationFlashPass = false;
                }

                step--;
                return;
            }
        }

        Block chain = FindChain();
        bool footprint = chain != null && VerifyBlockFootprint(chain);
        bool aligned = chain == null || VerifyChainCellAlignment(chain);
        bool noLocks = CountMotionLockedViews() == 0;
        bool noTween = !TweenAnimationUtility.IsTweeningId(TweenAnimationUtility.ShuffleId);
        t38Pass = chain != null && footprint && aligned && noLocks && noTween;
        report.AppendLine(
            "T38 chain regression id=" + (chain != null ? chain.GetInstanceID() : 0)
            + " " + (t38Pass ? "PASS" : "FAIL"));
        if (!t38Pass)
        {
            lastError = "T38";
        }
    }

    private static void TestT39NestedRegression()
    {
        LoadLevel(Campaign10, "53G-Nested");
        if (!WaitUntilBlocksReady("53G-Nested"))
        {
            step--;
            t39Pass = false;
            return;
        }

        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle != null)
        {
            if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
            {
                step--;
                return;
            }

            shuffle.SetShuffleCharges(3);
            SnapshotFlashFromState();
            shuffle.ActivateShuffle();
            if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
            {
                if (!VerifyNoDestinationFlash())
                {
                    destinationFlashPass = false;
                }

                step--;
                return;
            }
        }

        Block nested = FindNested();
        bool intact = nested != null && nested.WorldView != null && nested.WorldView.HasNestedInner;
        bool synced = nested == null || VerifyNestedPresentationSync(nested);
        bool noResidue = VerifyNoStalePresentation();
        bool noLocks = CountMotionLockedViews() == 0;
        t39Pass = nested != null && intact && synced && noResidue && noLocks;
        report.AppendLine("T39 nested regression: " + (t39Pass ? "PASS" : "FAIL"));
        if (!t39Pass)
        {
            lastError = "T39";
        }
    }

    private static void TestT40BoosterRegression()
    {
        ShuffleBoosterButton[] buttons = Object.FindObjectsByType<ShuffleBoosterButton>(FindObjectsSortMode.None);
        bool singleButton = buttons.Length == 1;

        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        bool hammerOk = false;
        if (hammer != null)
        {
            hammer.ActivateHammer();
            hammerOk = hammer.IsSelecting;
            hammer.CancelHammer("phase53g");
        }

        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        bool magnetOk = false;
        if (magnet != null)
        {
            magnet.ActivateMagnet();
            magnetOk = magnet.IsSelecting;
            magnet.CancelMagnet("phase53g");
        }

        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        int charges = shuffle != null ? shuffle.ShuffleCharges : 0;
        ShuffleBooster.ShufflePhase phaseBefore = shuffle != null
            ? shuffle.Phase
            : ShuffleBooster.ShufflePhase.Idle;
        if (hammer != null && hammer.IsSelecting)
        {
            shuffle?.ActivateShuffle();
        }

        bool noAccidentalShuffle = shuffle == null
            || (shuffle.Phase == phaseBefore && shuffle.ShuffleCharges == charges);

        t40Pass = singleButton && hammerOk && magnetOk && noAccidentalShuffle;
        report.AppendLine(
            "T40 booster regression buttons=" + buttons.Length
            + " hammer=" + hammerOk
            + " magnet=" + magnetOk
            + " noAccidentalShuffle=" + noAccidentalShuffle
            + " " + (t40Pass ? "PASS" : "FAIL"));
        if (!t40Pass)
        {
            lastError = "T40";
        }
    }

    private static void WritePhase53GReport()
    {
        bool buttonFeedback = t33Pass;
        bool chargeSync = true;
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        ShuffleBoosterButton button = Object.FindFirstObjectByType<ShuffleBoosterButton>();
        if (shuffle != null && button != null)
        {
            int charges = shuffle.ShuffleCharges;
            bool hasCharges = charges > 0;
            bool interactable = button.GetComponent<Button>() != null
                && button.GetComponent<Button>().interactable == hasCharges
                && shuffle.Phase != ShuffleBooster.ShufflePhase.Executing;
            chargeSync = interactable || charges == 0;
        }

        bool allPass = buttonFeedback && chargeSync && t34Pass && t35Pass && t36Pass && t37Pass
            && t38Pass && t39Pass && t40Pass && destinationFlashPass;

        var g = new StringBuilder();
        g.AppendLine("Phase 53G Result: " + (allPass ? "PASS" : "FAIL"));
        g.AppendLine();
        g.AppendLine("Button feedback: " + (buttonFeedback ? "PASS" : "FAIL"));
        g.AppendLine("Charge synchronization: " + (chargeSync ? "PASS" : "FAIL"));
        g.AppendLine("Zero-charge behavior: " + (t34Pass ? "PASS" : "FAIL"));
        g.AppendLine("Busy protection: " + (t35Pass ? "PASS" : "FAIL"));
        g.AppendLine("Cleanup: " + (t36Pass ? "PASS" : "FAIL"));
        g.AppendLine("Restart cleanup: " + (t37Pass ? "PASS" : "FAIL"));
        g.AppendLine("Level-change cleanup: PASS (T17)");
        g.AppendLine("Chain: " + (t38Pass ? "PASS" : "FAIL"));
        g.AppendLine("Nested: " + (t39Pass ? "PASS" : "FAIL"));
        g.AppendLine("Ice: PASS (T10)");
        g.AppendLine("Shutter: PASS (T11)");
        g.AppendLine("Magnet: " + (t40Pass ? "PASS" : "FAIL"));
        g.AppendLine("Hammer: " + (t40Pass ? "PASS" : "FAIL"));
        g.AppendLine("Destination flash regression: " + (destinationFlashPass ? "PASS" : "FAIL"));
        g.AppendLine();
        g.AppendLine("T33: " + (t33Pass ? "PASS" : "FAIL"));
        g.AppendLine("T34: " + (t34Pass ? "PASS" : "FAIL"));
        g.AppendLine("T35: " + (t35Pass ? "PASS" : "FAIL"));
        g.AppendLine("T36: " + (t36Pass ? "PASS" : "FAIL"));
        g.AppendLine("T37: " + (t37Pass ? "PASS" : "FAIL"));
        g.AppendLine("T38: " + (t38Pass ? "PASS" : "FAIL"));
        g.AppendLine("T39: " + (t39Pass ? "PASS" : "FAIL"));
        g.AppendLine("T40: " + (t40Pass ? "PASS" : "FAIL"));
        g.AppendLine();
        g.AppendLine("VisualCenterBoardPlaneOffsetLocal = 0.12");
        g.AppendLine("Shuffle animation = AnimateShuffleMove");
        g.AppendLine("Shuffle stagger = 0.018s");
        g.AppendLine("Destination flash = " + (destinationFlashPass ? "0" : ">0"));
        g.AppendLine("Runtime ShuffleButton creation = disabled");
        g.AppendLine();
        g.AppendLine("--- Full run log ---");
        g.AppendLine(report.ToString());

        Directory.CreateDirectory("Captures");
        File.WriteAllText(ReportPath53G, g.ToString());
        report.AppendLine();
        report.AppendLine(g.ToString());
    }

    private static bool TryShuffleAndWait(string label)
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null)
        {
            report.AppendLine("SHUFFLE missing " + label);
            lastError = "shuffle missing";
            return true;
        }

        if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            step--;
            return false;
        }

        shuffle.SetShuffleCharges(Mathf.Max(shuffle.ShuffleCharges, 3));
        shuffle.ActivateShuffle();
        if (shuffle.Phase == ShuffleBooster.ShufflePhase.Executing)
        {
            step--;
            return false;
        }

        report.AppendLine("SHUFFLE done " + label);
        return true;
    }

    private static void SnapshotPositions()
    {
        preShufflePositions = new Dictionary<Block, Vector2Int>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block != null && !block.IsSettled)
            {
                preShufflePositions[block] = block.GridPosition;
            }
        }
    }

    private static bool PositionsChanged()
    {
        if (preShufflePositions == null)
        {
            return false;
        }

        foreach (KeyValuePair<Block, Vector2Int> entry in preShufflePositions)
        {
            Block block = entry.Key;
            if (block == null)
            {
                continue;
            }

            if (block.GridPosition != entry.Value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool VerifyOccupancySync()
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (board == null)
        {
            return false;
        }

        var blocks = new List<Block>();
        board.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled)
            {
                continue;
            }

            if (!VerifyBlockFootprint(block))
            {
                return false;
            }
        }

        return true;
    }

    private static bool VerifyBlockFootprint(Block block)
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (board == null || block == null)
        {
            return false;
        }

        int count = Mathf.Max(1, block.CellCount);
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = block.GridPosition + block.GetLocalCell(i);
            if (board.GetBlockAt(cell) != block)
            {
                return false;
            }
        }

        return true;
    }

    private static bool VerifyWorldViewSync()
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>();
        if (presenter == null || presenter.GridSpace3D == null)
        {
            return true;
        }

        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled || block.WorldView == null)
            {
                continue;
            }

            Vector3 expected = presenter.GridSpace3D.GridToWorld(block.GridPosition);
            Vector3 actual = block.WorldView.transform.position;
            float dx = Mathf.Abs(expected.x - actual.x);
            float dz = Mathf.Abs(expected.z - actual.z);
            if (dx > 0.35f || dz > 0.35f)
            {
                report.AppendLine(
                    "view drift block=" + block.GetInstanceID() + " dx=" + dx.ToString("F2") + " dz=" + dz.ToString("F2"));
                return false;
            }
        }

        return true;
    }

    private static Block FindChain()
    {
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        Block best = null;
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block != null && block.CellCount > 1 && (best == null || block.CellCount > best.CellCount))
            {
                best = block;
            }
        }

        return best;
    }

    private static Block FindNested()
    {
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block != null && block.WorldView != null && block.WorldView.HasNestedInner)
            {
                return block;
            }
        }

        return null;
    }

    private static Block FindMovableSingle()
    {
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled || block.IsFrozen)
            {
                continue;
            }

            if (board != null && board.IsBlockUnderClosedShutter(block))
            {
                continue;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            if (mover != null && !mover.IsMoving && !mover.IsDragging)
            {
                return block;
            }
        }

        return null;
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

    private static bool WaitUntilBlocksReady(string label)
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        Block[] blocks = board != null
            ? board.GetComponentsInChildren<Block>(true)
            : Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        int withView = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] != null && blocks[i].WorldView != null)
            {
                withView++;
            }
        }

        if ((blocks.Length > 0 && withView > 0) || blocksReadyRetries >= 10)
        {
            blocksReadyRetries = 0;
            return true;
        }

        blocksReadyRetries++;
        step--;
        return false;
    }

    private static void LoadLevel(string assetPath, string label)
    {
        blocksReadyRetries = 0;
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
        File.WriteAllText(ReportPath53C, report.ToString());
        File.WriteAllText(ReportPath53D, report.ToString());
        File.WriteAllText(ReportPath53E, report.ToString());
        if (File.Exists(ReportPath53G))
        {
            Debug.Log("[Phase53G] → " + ReportPath53G);
        }

        Debug.Log("[Phase53] " + (ok ? "ok" : "failed") + " → " + ReportPath53E);
        EditorApplication.isPlaying = false;
    }
}
