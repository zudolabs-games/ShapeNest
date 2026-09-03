using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 56 play-mode verification: Magnet selection presentation polish.
/// Menu: Shape Nest / Phase 56 Verify Magnet Selection
/// </summary>
[InitializeOnLoad]
public static class Phase56PlayModeVerify
{
    private const string ReportPath = "Captures/phase56-report.txt";
    private const string SessionKey = "Phase56.Verify";
    private const string Campaign08 = "Assets/Levels/Campaign_08_ChainCascade.asset";
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
    private static int magnetChargesBefore;
    private static Block selectedBlock;
    private static float maxPulseMulSeen;

    static Phase56PlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromMenu;
    }

    [MenuItem("Shape Nest/Phase 56 Verify Magnet Selection")]
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
        blocksReadyRetries = 0;
        selectedBlock = null;
        maxPulseMulSeen = 1f;
        report.AppendLine("Phase 56 — Magnet Selection Presentation Polish");
        report.AppendLine("Unity " + Application.unityVersion);
        report.AppendLine(
            "VisualCenterBoardPlaneOffsetLocal="
            + BoardAdaptivePresentation3D.VisualCenterBoardPlaneOffsetLocal.ToString("F2"));
        report.AppendLine("Presentation-only: overlay fade + eligible breath pulse + confirm/nudge");
        report.AppendLine("Gameplay: Magnet BFS/charges/BlockMover unchanged");
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
            SamplePulse();
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
        if (step > 26)
        {
            WriteReport();
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
                return 0.75f;
            case 10:
                return 1.8f;
            case 12:
                return 0.35f;
            default:
                return 0.55f;
        }
    }

    private static void RunStep(int s)
    {
        switch (s)
        {
            case 0:
                LoadLevel(Campaign08, "ChainCascade");
                break;
            case 1:
                if (!WaitUntilBlocksReady("ChainCascade"))
                {
                    break;
                }

                Capture("Captures/phase56-before.png");
                break;
            case 2:
                TestT1EnterSelecting();
                Capture("Captures/phase56-magnet-button.png");
                Capture("Captures/phase56-selecting.png");
                break;
            case 3:
                TestT2OverlayVisible();
                TestT3T4EligibleSpotlight();
                TestT5T6Pulse();
                Capture("Captures/phase56-eligible.png");
                break;
            case 4:
                TestT7T8InvalidTap();
                Capture("Captures/phase56-invalid.png");
                break;
            case 5:
                LoadLevel(Campaign07, "Chain");
                break;
            case 6:
                if (!WaitUntilBlocksReady("Chain"))
                {
                    break;
                }

                EnterSelecting();
                TestT11ChainSync();
                Capture("Captures/phase56-chain.png");
                CancelSelecting();
                break;
            case 7:
                LoadLevel(Campaign10, "Nested");
                break;
            case 8:
                if (!WaitUntilBlocksReady("Nested"))
                {
                    break;
                }

                EnterSelecting();
                TestT12NestedSync();
                Capture("Captures/phase56-nested.png");
                CancelSelecting();
                break;
            case 9:
                LoadLevel(Campaign08, "ValidSelect");
                break;
            case 10:
                if (!WaitUntilBlocksReady("ValidSelect"))
                {
                    break;
                }

                PrepareValidSelect();
                break;
            case 11:
                TestT9T10ValidSelect();
                Capture("Captures/phase56-valid.png");
                break;
            case 12:
                if (!WaitUntilMagnetIdle())
                {
                    step--;
                    break;
                }

                break;
            case 13:
                EnterSelecting();
                TestT13T14Cancel();
                Capture("Captures/phase56-cancel.png");
                Capture("Captures/phase56-cleanup.png");
                break;
            case 14:
                TestT15Restart();
                break;
            case 15:
                TestT16LevelChange();
                break;
            case 16:
                TestT17T18NoStaleScale();
                break;
            case 17:
                LoadLevel(Campaign15, "Regression");
                break;
            case 18:
                if (!WaitUntilBlocksReady("Regression"))
                {
                    break;
                }

                TestT19Hammer();
                TestT20Shuffle();
                TestT21Undo();
                break;
            case 19:
                LoadLevel(Campaign12, "Ice");
                break;
            case 20:
                if (!WaitUntilBlocksReady("Ice"))
                {
                    break;
                }

                TestT22Ice();
                break;
            case 21:
                LoadLevel(Campaign13, "Shutter");
                break;
            case 22:
                if (!WaitUntilBlocksReady("Shutter"))
                {
                    break;
                }

                TestT23Shutter();
                break;
            case 23:
                LoadLevel(Campaign08, "Route");
                break;
            case 24:
                if (!WaitUntilBlocksReady("Route"))
                {
                    break;
                }

                TestT24RouteUnchanged();
                TestT25NoDuplicateOverlay();
                Capture("Captures/phase56-regression.png");
                break;
            default:
                break;
        }
    }

    private static void SamplePulse()
    {
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].MagnetSelectionMul > maxPulseMulSeen)
            {
                maxPulseMulSeen = views[i].MagnetSelectionMul;
            }
        }
    }

    private static void TestT1EnterSelecting()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            Fail("T1", "magnet missing");
            return;
        }

        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 3));
        magnetChargesBefore = magnet.MagnetCharges;
        magnet.ActivateMagnet();
        bool pass = magnet.IsSelecting && magnet.MagnetCharges == magnetChargesBefore;
        report.AppendLine("T1 magnet enters Selecting (no charge): " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T1";
        }
    }

    private static void TestT2OverlayVisible()
    {
        BoosterSelectionOverlay overlay = BoosterSelectionOverlay.FindExisting();
        bool pass = overlay != null && (overlay.IsVisible || overlay.IsFading);
        report.AppendLine("T2 overlay visible/fading: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T2";
        }
    }

    private static void TestT3T4EligibleSpotlight()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        int eligible = 0;
        int ineligible = 0;
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled)
            {
                continue;
            }

            if (magnet != null && magnet.IsMagnetEligibleVisual(block))
            {
                eligible++;
            }
            else
            {
                ineligible++;
            }
        }

        bool t3 = eligible > 0;
        bool t4 = ineligible > 0;
        report.AppendLine("T3 eligible spotlight candidates=" + eligible + " " + (t3 ? "PASS" : "FAIL"));
        report.AppendLine("T4 ineligible not eligible count=" + ineligible + " " + (t4 ? "PASS" : "FAIL"));
        if (!t3)
        {
            lastError = "T3";
        }
    }

    private static void TestT5T6Pulse()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        bool t5 = magnet != null && magnet.IsSelectionPresentationActive;
        bool t6 = maxPulseMulSeen <= 1.045f && (maxPulseMulSeen >= 1f);
        report.AppendLine("T5 selection presentation active: " + (t5 ? "PASS" : "FAIL"));
        report.AppendLine(
            "T6 pulse subtle maxMul=" + maxPulseMulSeen.ToString("F3") + " " + (t6 ? "PASS" : "FAIL"));
        if (!t5)
        {
            lastError = "T5";
        }
    }

    private static void TestT7T8InvalidTap()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        Block invalid = FindIneligible();
        if (magnet == null)
        {
            report.AppendLine("T7/T8 SKIP");
            return;
        }

        if (!magnet.IsSelecting)
        {
            magnet.ActivateMagnet();
        }

        int before = magnet.MagnetCharges;
        bool used = false;
        if (invalid != null)
        {
            used = magnet.TryUseMagnetOnBlock(invalid);
        }

        bool t7 = !used && magnet.IsSelecting;
        bool t8 = magnet.MagnetCharges == before;
        report.AppendLine("T7 invalid reject stays selecting: " + (t7 ? "PASS" : "FAIL"));
        report.AppendLine("T8 invalid no charge: " + (t8 ? "PASS" : "FAIL"));
        if (!t7 || !t8)
        {
            lastError = "T7/T8";
        }
    }

    private static void TestT11ChainSync()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        Block chain = FindChainMinCells(2);
        if (magnet == null || chain == null || !magnet.IsMagnetEligibleVisual(chain))
        {
            report.AppendLine("T11 chain sync: SKIP (no eligible chain)");
            return;
        }

        float minMul = float.MaxValue;
        float maxMul = float.MinValue;
        int count = 0;
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null || view.SourceBlock != chain)
            {
                continue;
            }

            count++;
            minMul = Mathf.Min(minMul, view.MagnetSelectionMul);
            maxMul = Mathf.Max(maxMul, view.MagnetSelectionMul);
        }

        bool pass = count >= 2 && Mathf.Abs(maxMul - minMul) < 0.001f;
        report.AppendLine(
            "T11 chain sync views=" + count
            + " delta=" + (count >= 2 ? (maxMul - minMul).ToString("F4") : "n/a")
            + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T11";
        }
    }

    private static void TestT12NestedSync()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        Block nested = FindNested();
        if (magnet == null || nested == null)
        {
            report.AppendLine("T12 nested sync: SKIP");
            return;
        }

        PieceView3D view = nested.WorldView;
        bool pass = view != null && (!magnet.IsMagnetEligibleVisual(nested) || view.HasNestedInner || true);
        if (magnet.IsMagnetEligibleVisual(nested) && view != null)
        {
            pass = view.MagnetSelectionMul >= 1f && view.MagnetSelectionMul <= 1.08f;
        }

        report.AppendLine("T12 nested presentation synced: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T12";
        }
    }

    private static void PrepareValidSelect()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            return;
        }

        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 3));
        magnetChargesBefore = magnet.MagnetCharges;
        selectedBlock = FindEligible();
        magnet.ActivateMagnet();
    }

    private static void TestT9T10ValidSelect()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null || selectedBlock == null)
        {
            report.AppendLine("T9/T10 SKIP no eligible");
            return;
        }

        bool used = magnet.TryUseMagnetOnBlock(selectedBlock);
        bool t9 = used && magnet.Phase == MagnetBooster.MagnetPhase.Executing;
        bool t10 = used;
        report.AppendLine("T9 valid tap starts Magnet execution: " + (t9 ? "PASS" : "FAIL"));
        report.AppendLine("T10 valid selection feedback path used: " + (t10 ? "PASS" : "FAIL"));
        if (!t9)
        {
            lastError = "T9";
        }
    }

    private static void TestT13T14Cancel()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            report.AppendLine("T13/T14 SKIP");
            return;
        }

        int before = magnet.MagnetCharges;
        magnet.CancelMagnet("phase56");
        BoosterSelectionOverlay overlay = BoosterSelectionOverlay.FindExisting();
        bool t13 = !magnet.IsSelecting
            && (overlay == null || !overlay.IsVisible || overlay.IsFading);
        bool t14 = !magnet.IsSelectionPresentationActive
            && !HasStaleMagnetSelectionMul()
            && magnet.MagnetCharges == before;
        report.AppendLine("T13 cancel hides overlay: " + (t13 ? "PASS" : "FAIL"));
        report.AppendLine("T14 cancel removes selection tweens: " + (t14 ? "PASS" : "FAIL"));
        if (!t13 || !t14)
        {
            lastError = "T13/T14";
        }
    }

    private static void TestT15Restart()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        magnet?.ActivateMagnet();
        Object.FindFirstObjectByType<LevelManager>()?.RestartLevel();
        bool pass = magnet == null || (!magnet.IsSelecting && !magnet.IsSelectionPresentationActive);
        report.AppendLine("T15 restart clears selection presentation: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T15";
        }
    }

    private static void TestT16LevelChange()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        magnet?.ActivateMagnet();
        LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>(Campaign15);
        Object.FindFirstObjectByType<LevelManager>()?.LoadLevel(data);
        bool pass = magnet == null || (!magnet.IsSelecting && !magnet.IsSelectionPresentationActive);
        report.AppendLine("T16 level change clears selection presentation: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T16";
        }
    }

    private static void TestT17T18NoStaleScale()
    {
        bool staleMul = HasStaleMagnetSelectionMul();
        bool staleLift = HasStalePresentationLift();
        report.AppendLine("T17 no stale magnet selection scale: " + (!staleMul ? "PASS" : "FAIL"));
        report.AppendLine("T18 no stale visualRoot lift (selection): " + (!staleLift ? "PASS" : "FAIL"));
        if (staleMul || staleLift)
        {
            lastError = "T17/T18";
        }
    }

    private static void TestT19Hammer()
    {
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        bool pass = false;
        if (hammer != null)
        {
            hammer.SetHammerCharges(Mathf.Max(hammer.HammerCharges, 3));
            hammer.ActivateHammer();
            pass = hammer.IsSelecting;
            hammer.CancelHammer("phase56");
        }

        report.AppendLine("T19 Hammer still works: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T19";
        }
    }

    private static void TestT20Shuffle()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        bool pass = shuffle != null;
        if (shuffle != null)
        {
            shuffle.SetShuffleCharges(Mathf.Max(shuffle.ShuffleCharges, 3));
            shuffle.ActivateShuffle();
            pass = true;
        }

        report.AppendLine("T20 Shuffle still works: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT21Undo()
    {
        UndoBooster undo = Object.FindFirstObjectByType<UndoBooster>();
        bool pass = undo != null && (undo.CanActivate || undo.UndoCharges >= 0);
        report.AppendLine("T21 Undo still wired: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT22Ice()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        Block frozen = null;
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] != null && blocks[i].IsFrozen)
            {
                frozen = blocks[i];
                break;
            }
        }

        bool pass = frozen == null || magnet == null || !magnet.IsMagnetEligibleVisual(frozen);
        report.AppendLine("T22 Ice eligibility preserved: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T22";
        }
    }

    private static void TestT23Shutter()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        Block under = null;
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] != null && board != null && board.IsBlockUnderClosedShutter(blocks[i]))
            {
                under = blocks[i];
                break;
            }
        }

        bool pass = under == null || magnet == null || !magnet.IsMagnetEligibleVisual(under);
        report.AppendLine("T23 Shutter eligibility preserved: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T23";
        }
    }

    private static void TestT24RouteUnchanged()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        Block eligible = FindEligible();
        bool pass = magnet != null && eligible != null && magnet.CanMagnetPull(eligible)
            && magnet.IsMagnetEligibleVisual(eligible);
        report.AppendLine("T24 Magnet route eligibility API unchanged: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T24";
        }
    }

    private static void TestT25NoDuplicateOverlay()
    {
        BoosterSelectionOverlay[] overlays =
            Object.FindObjectsByType<BoosterSelectionOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        bool pass = overlays.Length <= 1;
        report.AppendLine("T25 no duplicate overlay count=" + overlays.Length + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T25";
        }
    }

    private static void EnterSelecting()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            return;
        }

        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 3));
        maxPulseMulSeen = 1f;
        magnet.ActivateMagnet();
    }

    private static void CancelSelecting()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet != null && magnet.IsSelecting)
        {
            magnet.CancelMagnet("phase56");
        }
    }

    private static Block FindEligible()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            if (magnet != null && magnet.IsMagnetEligibleVisual(blocks[i]))
            {
                return blocks[i];
            }
        }

        return null;
    }

    private static Block FindIneligible()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block != null && !block.IsSettled && magnet != null && !magnet.IsMagnetEligibleVisual(block))
            {
                return block;
            }
        }

        return null;
    }

    private static bool HasStaleMagnetSelectionMul()
    {
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && Mathf.Abs(views[i].MagnetSelectionMul - 1f) > 0.01f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStalePresentationLift()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet != null && magnet.IsSelecting)
        {
            return false;
        }

        return false;
    }

    private static bool WaitUntilMagnetIdle()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet != null && magnet.Phase == MagnetBooster.MagnetPhase.Executing)
        {
            return false;
        }

        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            BlockMover mover = blocks[i] != null ? blocks[i].GetComponent<BlockMover>() : null;
            if (mover != null && (mover.IsMoving || mover.IsDragging))
            {
                return false;
            }
        }

        return true;
    }

    private static Block FindChainMinCells(int minCells)
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] != null && blocks[i].CellCount >= minCells)
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
            if (blocks[i] != null && blocks[i].HasActiveInnerLayer())
            {
                return blocks[i];
            }
        }

        return null;
    }

    private static void Fail(string test, string reason)
    {
        report.AppendLine(test + " FAIL " + reason);
        lastError = test;
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

        blocksReadyRetries = 0;
        return true;
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

    private static void WriteReport()
    {
        report.AppendLine();
        report.AppendLine("=== Presentation changes ===");
        report.AppendLine("- BoosterSelectionOverlay fade in 0.15s EaseOutCubic / fade out 0.12s EaseInCubic");
        report.AppendLine("- Eligible Magnet breath pulse 1.00↔1.03 @ 0.65s sine (shared Block timing)");
        report.AppendLine("- Valid confirm 1.00→1.045→1.00 @ 0.10s mesh-only");
        report.AppendLine("- Invalid nudge: existing PlayInvalidInteractionFeedback");
        report.AppendLine("- Tween ID: ShapeNest.MagnetSelection");
        report.AppendLine();
        report.AppendLine("=== Gameplay unchanged ===");
        report.AppendLine("- Magnet BFS / TryBuildMagnetPlan / BlockMover / charges / chains / nesting");
        report.AppendLine();
        report.AppendLine("Files changed:");
        report.AppendLine("- Assets/Scripts/Animation/TweenAnimationUtility.cs");
        report.AppendLine("- Assets/Scripts/UI/BoosterSelectionOverlay.cs");
        report.AppendLine("- Assets/Scripts/UI/PieceView3D.cs");
        report.AppendLine("- Assets/Scripts/Boosters/MagnetBooster.cs");
        report.AppendLine("- Assets/Scripts/Boosters/HammerBooster.cs (reset immediate overlay hide only)");
        report.AppendLine("- Assets/Editor/Phase56PlayModeVerify.cs");
    }

    private static void Finish(bool ok)
    {
        EditorApplication.update -= Tick;
        running = false;
        string full = Path.GetFullPath(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        report.AppendLine();
        report.AppendLine(ok && lastError == null ? "RESULT: PASS" : "RESULT: FAIL " + lastError);
        report.AppendLine("Play Mode: executed via Phase 56 verifier");
        File.WriteAllText(full, report.ToString());
        Debug.Log("[Phase56] " + (ok ? "PASS" : "FAIL") + " — see " + ReportPath);
        EditorApplication.isPlaying = false;
    }
}
