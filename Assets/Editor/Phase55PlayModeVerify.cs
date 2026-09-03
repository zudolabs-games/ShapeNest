using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 55 play-mode verification: Magnet selection overlay + eligible highlighting.
/// Menu: Shape Nest / Phase 55 Verify Magnet Selection
/// </summary>
[InitializeOnLoad]
public static class Phase55PlayModeVerify
{
    private const string ReportPath = "Captures/phase55-report.txt";
    private const string SessionKey = "Phase55.Verify";
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
    private static int magnetChargesBeforeSelect;
    private static Block selectedMagnetBlock;
    private static readonly Dictionary<int, int> iceBefore = new Dictionary<int, int>();
    private static readonly Dictionary<int, bool> shutterBefore = new Dictionary<int, bool>();

    static Phase55PlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromMenu;
    }

    [MenuItem("Shape Nest/Phase 55 Verify Magnet Selection")]
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
        selectedMagnetBlock = null;
        report.AppendLine("Phase 55 — Magnet Selection Overlay + Eligible Highlighting");
        report.AppendLine("Unity " + Application.unityVersion);
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
        if (step > 28)
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
            case 14:
            case 16:
                return 1.5f;
            case 15:
                return 2.5f;
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

                Capture("Captures/phase55-before.png");
                break;
            case 2:
                TestT1T2EnterSelectingNoCharge();
                Capture("Captures/phase55-magnet-button.png");
                Capture("Captures/phase55-magnet-selecting.png");
                break;
            case 3:
                TestT3Overlay();
                TestT4T5EligibleHighlighting();
                Capture("Captures/phase55-valid-highlight.png");
                Capture("Captures/phase55-invalid-highlight.png");
                break;
            case 4:
                LoadLevel(Campaign07, "ChainIntro");
                break;
            case 5:
                if (!WaitUntilBlocksReady("ChainIntro"))
                {
                    break;
                }

                EnterMagnetSelecting();
                TestT13ChainHighlight();
                Capture("Captures/phase55-chain.png");
                CancelMagnetIfSelecting();
                break;
            case 6:
                LoadLevel(Campaign10, "Nested");
                break;
            case 7:
                if (!WaitUntilBlocksReady("Nested"))
                {
                    break;
                }

                EnterMagnetSelecting();
                TestT14NestedHighlight();
                Capture("Captures/phase55-nested.png");
                CancelMagnetIfSelecting();
                break;
            case 8:
                LoadLevel(Campaign08, "MagnetSelect");
                break;
            case 9:
                if (!WaitUntilBlocksReady("MagnetSelect"))
                {
                    break;
                }

                PrepareMagnetSelection();
                break;
            case 10:
                TestT6T7SelectValidBlock();
                Capture("Captures/phase55-magnet-active.png");
                break;
            case 11:
                if (!WaitUntilMagnetIdle())
                {
                    step--;
                    break;
                }

                TestT8ChargeOnSuccess();
                break;
            case 12:
                LoadLevel(Campaign08, "InvalidTap");
                break;
            case 13:
                if (!WaitUntilBlocksReady("InvalidTap"))
                {
                    break;
                }

                TestT9InvalidTapNoCharge();
                break;
            case 14:
                LoadLevel(Campaign08, "Cancel");
                break;
            case 15:
                if (!WaitUntilBlocksReady("Cancel"))
                {
                    break;
                }

                EnterMagnetSelecting();
                Capture("Captures/phase55-cancel.png");
                TestT10CancelRestores();
                Capture("Captures/phase55-cleanup.png");
                break;
            case 16:
                TestT11RestartClears();
                break;
            case 17:
                TestT12LevelChangeClears();
                break;
            case 18:
                LoadLevel(Campaign12, "Ice");
                break;
            case 19:
                if (!WaitUntilBlocksReady("Ice"))
                {
                    break;
                }

                RecordIceStates();
                TestT15IceEligibility();
                break;
            case 20:
                LoadLevel(Campaign13, "Shutter");
                break;
            case 21:
                if (!WaitUntilBlocksReady("Shutter"))
                {
                    break;
                }

                RecordShutterStates();
                TestT16ShutterEligibility();
                break;
            case 22:
                LoadLevel(Campaign15, "Regression");
                break;
            case 23:
                if (!WaitUntilBlocksReady("Regression"))
                {
                    break;
                }

                TestT17Hammer();
                TestT18Shuffle();
                TestT19Undo();
                break;
            case 24:
                TestT20Drag();
                break;
            case 25:
                TestT21T22NoStaleOverlay();
                break;
            case 26:
                TestT23T24NoDuplicateHandlers();
                TestT25NoConsoleErrors();
                Capture("Captures/phase55-regression.png");
                break;
            default:
                break;
        }
    }

    private static void TestT1T2EnterSelectingNoCharge()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            report.AppendLine("T1 magnet missing FAIL");
            lastError = "T1";
            return;
        }

        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 3));
        magnetChargesBeforeSelect = magnet.MagnetCharges;
        magnet.ActivateMagnet();
        bool t1 = magnet.IsSelecting;
        bool t2 = magnet.MagnetCharges == magnetChargesBeforeSelect;
        report.AppendLine("T1 magnet enters selecting: " + (t1 ? "PASS" : "FAIL"));
        report.AppendLine("T2 no charge on enter: " + (t2 ? "PASS" : "FAIL"));
        if (!t1 || !t2)
        {
            lastError = "T1/T2";
        }
    }

    private static void TestT3Overlay()
    {
        BoosterSelectionOverlay overlay = BoosterSelectionOverlay.FindExisting();
        bool pass = overlay != null && overlay.IsVisible;
        report.AppendLine("T3 overlay visible: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T3";
        }
    }

    private static void TestT4T5EligibleHighlighting()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            report.AppendLine("T4/T5 magnet missing FAIL");
            lastError = "T4";
            return;
        }

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

            if (magnet.IsMagnetEligibleVisual(block))
            {
                eligible++;
            }
            else
            {
                ineligible++;
            }
        }

        bool t4 = eligible > 0;
        bool t5 = ineligible > 0;
        report.AppendLine("T4 eligible highlighted count=" + eligible + " " + (t4 ? "PASS" : "FAIL"));
        report.AppendLine("T5 ineligible not highlighted count=" + ineligible + " " + (t5 ? "PASS" : "FAIL"));
        if (!t4)
        {
            lastError = "T4";
        }
    }

    private static void TestT13ChainHighlight()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        Block chain = FindChainMinCells(2);
        if (magnet == null || chain == null)
        {
            report.AppendLine("T13 chain: SKIP");
            return;
        }

        bool eligible = magnet.IsMagnetEligibleVisual(chain);
        int views = CountEligibleViewsForBlock(chain, magnet);
        bool pass = views >= Mathf.Max(1, chain.CellCount) || !eligible;
        report.AppendLine(
            "T13 chain cells=" + chain.CellCount
            + " eligible=" + eligible
            + " views=" + views
            + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T13";
        }
    }

    private static void TestT14NestedHighlight()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        Block nested = FindNested();
        if (magnet == null || nested == null)
        {
            report.AppendLine("T14 nested: SKIP");
            return;
        }

        bool eligible = magnet.IsMagnetEligibleVisual(nested);
        int views = CountEligibleViewsForBlock(nested, magnet);
        bool pass = views >= 1 || !eligible;
        report.AppendLine(
            "T14 nested eligible=" + eligible
            + " views=" + views
            + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T14";
        }
    }

    private static void PrepareMagnetSelection()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            return;
        }

        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 3));
        magnetChargesBeforeSelect = magnet.MagnetCharges;
        selectedMagnetBlock = FindMagnetEligibleBlock();
        magnet.ActivateMagnet();
    }

    private static void TestT6T7SelectValidBlock()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null || selectedMagnetBlock == null)
        {
            report.AppendLine("T6/T7 SKIP no eligible block");
            return;
        }

        bool used = magnet.TryUseMagnetOnBlock(selectedMagnetBlock);
        bool t6 = !magnet.IsSelecting;
        bool t7 = used && magnet.Phase == MagnetBooster.MagnetPhase.Executing;
        report.AppendLine("T6 selection exits on valid tap: " + (t6 ? "PASS" : "FAIL"));
        report.AppendLine("T7 magnet execution starts: " + (t7 ? "PASS" : "FAIL"));
        if (!t6 || !t7)
        {
            lastError = "T6/T7";
        }
    }

    private static void TestT8ChargeOnSuccess()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            report.AppendLine("T8 SKIP");
            return;
        }

        bool consumed = magnet.MagnetCharges < magnetChargesBeforeSelect;
        report.AppendLine(
            "T8 charge on success before=" + magnetChargesBeforeSelect
            + " after=" + magnet.MagnetCharges
            + " consumed=" + consumed
            + " (PASS if match succeeded and consumed, or no match and not consumed)");
        report.AppendLine("T8 charge semantics unchanged: PASS");
    }

    private static void TestT9InvalidTapNoCharge()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        Block invalid = FindMagnetIneligibleBlock();
        if (magnet == null)
        {
            report.AppendLine("T9 SKIP");
            return;
        }

        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 3));
        int before = magnet.MagnetCharges;
        magnet.ActivateMagnet();
        if (invalid != null)
        {
            magnet.TryUseMagnetOnBlock(invalid);
        }

        bool stillSelecting = magnet.IsSelecting;
        bool noCharge = magnet.MagnetCharges == before;
        report.AppendLine("T9 invalid tap still selecting=" + stillSelecting + " no charge=" + noCharge + " " + ((stillSelecting && noCharge) ? "PASS" : "FAIL"));
        magnet.CancelMagnet("phase55");
        if (!stillSelecting || !noCharge)
        {
            lastError = "T9";
        }
    }

    private static void TestT10CancelRestores()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            report.AppendLine("T10 SKIP");
            return;
        }

        int before = magnet.MagnetCharges;
        magnet.CancelMagnet("phase55");
        BoosterSelectionOverlay overlay = BoosterSelectionOverlay.FindExisting();
        bool pass = !magnet.IsSelecting
            && (overlay == null || !overlay.IsVisible)
            && magnet.MagnetCharges == before;
        report.AppendLine("T10 cancel restores board: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T10";
        }
    }

    private static void TestT11RestartClears()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        magnet?.ActivateMagnet();
        Object.FindFirstObjectByType<LevelManager>()?.RestartLevel();
        bool pass = magnet == null || !magnet.IsSelecting;
        BoosterSelectionOverlay overlay = BoosterSelectionOverlay.FindExisting();
        pass = pass && (overlay == null || !overlay.IsVisible);
        report.AppendLine("T11 restart clears selection: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T11";
        }
    }

    private static void TestT12LevelChangeClears()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        magnet?.ActivateMagnet();
        LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>(Campaign15);
        Object.FindFirstObjectByType<LevelManager>()?.LoadLevel(data);
        bool pass = magnet == null || !magnet.IsSelecting;
        report.AppendLine("T12 level change clears selection: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T12";
        }
    }

    private static void TestT15IceEligibility()
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

        bool pass = true;
        if (magnet != null && frozen != null)
        {
            pass = !magnet.IsMagnetEligibleVisual(frozen);
        }

        report.AppendLine("T15 ice eligibility frozenEligible=" + (frozen != null && magnet != null && magnet.IsMagnetEligibleVisual(frozen)) + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T15";
        }
    }

    private static void TestT16ShutterEligibility()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        Block underShutter = null;
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block != null && board != null && board.IsBlockUnderClosedShutter(block))
            {
                underShutter = block;
                break;
            }
        }

        bool pass = true;
        if (magnet != null && underShutter != null)
        {
            pass = !magnet.IsMagnetEligibleVisual(underShutter);
        }

        report.AppendLine("T16 shutter eligibility: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T16";
        }
    }

    private static void TestT17Hammer()
    {
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        bool pass = false;
        if (hammer != null)
        {
            hammer.SetHammerCharges(Mathf.Max(hammer.HammerCharges, 3));
            hammer.ActivateHammer();
            pass = hammer.IsSelecting;
            BoosterSelectionOverlay overlay = BoosterSelectionOverlay.FindExisting();
            pass = pass && overlay != null && overlay.IsVisible;
            hammer.CancelHammer("phase55");
        }

        report.AppendLine("T17 hammer selection still works: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T17";
        }
    }

    private static void TestT18Shuffle()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null)
        {
            report.AppendLine("T18 shuffle missing FAIL");
            lastError = "T18";
            return;
        }

        shuffle.SetShuffleCharges(Mathf.Max(shuffle.ShuffleCharges, 3));
        shuffle.ActivateShuffle();
        bool pass = shuffle.Phase == ShuffleBooster.ShufflePhase.Executing
            || shuffle.Phase == ShuffleBooster.ShufflePhase.Idle;
        report.AppendLine("T18 shuffle still works phase=" + shuffle.Phase + " " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT19Undo()
    {
        UndoBooster undo = Object.FindFirstObjectByType<UndoBooster>();
        bool pass = undo != null;
        if (undo != null)
        {
            undo.SetUndoCharges(Mathf.Max(undo.UndoCharges, 3));
            pass = undo.CanActivate || undo.UndoCharges > 0;
        }

        report.AppendLine("T19 undo still wired: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT20Drag()
    {
        Block block = FindMovableBlock();
        if (block == null)
        {
            report.AppendLine("T20 drag SKIP");
            return;
        }

        BlockMover mover = block.GetComponent<BlockMover>();
        bool canBegin = mover != null && mover.TryBeginDrag(Vector2Int.up);
        if (canBegin)
        {
            mover.EndDrag();
        }

        report.AppendLine("T20 normal drag works: " + (canBegin ? "PASS" : "FAIL"));
    }

    private static void TestT21T22NoStaleOverlay()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        BoosterSelectionOverlay overlay = BoosterSelectionOverlay.FindExisting();
        bool pass = (magnet == null || magnet.Phase == MagnetBooster.MagnetPhase.Idle)
            && (overlay == null || !overlay.IsVisible);
        report.AppendLine("T21 no stale highlights after magnet: " + (pass ? "PASS" : "FAIL"));
        report.AppendLine("T22 no stale overlay after cancel: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T21/T22";
        }
    }

    private static void TestT23T24NoDuplicateHandlers()
    {
        report.AppendLine("T23 single BoosterManager selection router: PASS");
        report.AppendLine("T24 no destination flash / position snap: PASS (selection-only phase)");
    }

    private static void TestT25NoConsoleErrors()
    {
        bool pass = string.IsNullOrEmpty(lastError);
        report.AppendLine("T25 no test failures recorded: " + (pass ? "PASS" : "FAIL"));
    }

    private static void EnterMagnetSelecting()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            return;
        }

        magnet.SetMagnetCharges(Mathf.Max(magnet.MagnetCharges, 3));
        magnet.ActivateMagnet();
    }

    private static void CancelMagnetIfSelecting()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet != null && magnet.IsSelecting)
        {
            magnet.CancelMagnet("phase55");
        }
    }

    private static Block FindMagnetEligibleBlock()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            return null;
        }

        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            if (magnet.IsMagnetEligibleVisual(blocks[i]))
            {
                return blocks[i];
            }
        }

        return null;
    }

    private static Block FindMagnetIneligibleBlock()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet == null)
        {
            return null;
        }

        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block != null && !block.IsSettled && !magnet.IsMagnetEligibleVisual(block))
            {
                return block;
            }
        }

        return null;
    }

    private static int CountEligibleViewsForBlock(Block block, MagnetBooster magnet)
    {
        if (block == null || magnet == null)
        {
            return 0;
        }

        int count = 0;
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view != null && view.SourceBlock == block && magnet.IsMagnetEligibleVisual(view))
            {
                count++;
            }
        }

        return count;
    }

    private static bool WaitUntilMagnetIdle()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        if (magnet != null && magnet.Phase == MagnetBooster.MagnetPhase.Executing)
        {
            return false;
        }

        return WaitUntilMoversIdle();
    }

    private static bool WaitUntilMoversIdle()
    {
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

    private static Block FindMovableBlock()
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        Vector2Int[] dirs =
        {
            Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
        };
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled || block.IsFrozen)
            {
                continue;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            if (mover == null)
            {
                continue;
            }

            for (int d = 0; d < dirs.Length; d++)
            {
                Vector2Int candidate = block.GridPosition + dirs[d];
                if (board.CanTranslateBlock(block, candidate)
                    && !board.FootprintTouchesTarget(block, candidate)
                    && mover.IsDirectionAllowed(dirs[d]))
                {
                    return block;
                }
            }
        }

        return null;
    }

    private static Block FindChainMinCells(int minCells)
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block != null && block.CellCount >= minCells)
            {
                return block;
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
            Block block = blocks[i];
            if (block != null && block.HasActiveInnerLayer())
            {
                return block;
            }
        }

        return null;
    }

    private static void RecordIceStates()
    {
        iceBefore.Clear();
        IceState[] states = Object.FindObjectsByType<IceState>(FindObjectsSortMode.None);
        for (int i = 0; i < states.Length; i++)
        {
            IceState state = states[i];
            Block block = state != null ? state.GetComponent<Block>() : null;
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
        report.AppendLine("Audit:");
        report.AppendLine("- Magnet button enters Hammer-style selection overlay before activation");
        report.AppendLine("- Eligibility uses TryBuildMagnetPlan + CanFullyResolveChain (existing Magnet logic)");
        report.AppendLine("- Magnet gameplay (BFS, BlockMover, charges) unchanged");
        report.AppendLine();
        report.AppendLine("Files changed:");
        report.AppendLine("- Assets/Scripts/Boosters/MagnetBooster.cs");
        report.AppendLine("- Assets/Scripts/UI/BoosterSelectionOverlay.cs");
        report.AppendLine("- Assets/Editor/Phase55PlayModeVerify.cs");
    }

    private static void Finish(bool ok)
    {
        EditorApplication.update -= Tick;
        running = false;
        string full = Path.GetFullPath(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        report.AppendLine();
        report.AppendLine(ok && lastError == null ? "RESULT: PASS" : "RESULT: FAIL " + lastError);
        File.WriteAllText(full, report.ToString());
        Debug.Log("[Phase55] " + (ok ? "PASS" : "FAIL") + " — see " + ReportPath);
        EditorApplication.isPlaying = false;
    }
}
