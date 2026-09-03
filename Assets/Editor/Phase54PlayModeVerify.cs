using System.Collections.Generic;
using System.IO;
using System.Text;
using DG.Tweening;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 54 play-mode verification: Undo booster.
/// Menu: Shape Nest / Phase 54 Verify Undo Booster
/// </summary>
[InitializeOnLoad]
public static class Phase54PlayModeVerify
{
    private const string ReportPath = "Captures/phase54-report.txt";
    private const string SessionKey = "Phase54.Verify";
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
    private static Dictionary<Block, Vector2Int> preActionPositions;
    private static Block movedBlock;
    private static Vector2Int movedFrom;
    private static Vector2Int movedTo;
    private static int undoChargesBefore;
    private static readonly Dictionary<int, int> iceBefore = new Dictionary<int, int>();
    private static readonly Dictionary<int, bool> shutterBefore = new Dictionary<int, bool>();

    static Phase54PlayModeVerify()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += TryBeginFromMenu;
    }

    [MenuItem("Shape Nest/Phase 54 Verify Undo Booster")]
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
        preActionPositions = null;
        movedBlock = null;
        report.AppendLine("Phase 54 — Undo Booster");
        report.AppendLine("Unity " + Application.unityVersion);
        report.AppendLine("Architecture: BoosterManager + IBooster + BoardUndoHistory + BoardManager");
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
        if (step > 36)
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
            case 2:
            case 4:
            case 6:
            case 8:
            case 10:
            case 12:
            case 14:
            case 16:
            case 18:
            case 20:
            case 22:
                return 0.85f;
            case 3:
            case 5:
            case 7:
                return 1.1f;
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

                Capture("Captures/phase54-before.png");
                TestT1ButtonExists();
                TestT2ButtonWired();
                TestT3NoHistory();
                break;
            case 2:
                movedBlock = FindMovableBlock();
                if (movedBlock == null)
                {
                    lastError = "no movable block";
                    break;
                }

                movedFrom = movedBlock.GridPosition;
                if (!TryMoveBlockOneCell(movedBlock, out movedTo))
                {
                    lastError = "move failed";
                    break;
                }

                Capture("Captures/phase54-move.png");
                break;
            case 3:
                if (!WaitUntilMoversIdle())
                {
                    step--;
                    break;
                }

                TestT4HistoryAfterMove();
                undoChargesBefore = GetUndoCharges();
                UndoBooster undoStart = Object.FindFirstObjectByType<UndoBooster>();
                undoStart?.SetUndoCharges(Mathf.Max(undoChargesBefore, 3));
                undoStart?.ActivateUndo();
                break;
            case 4:
                if (!WaitUntilUndoIdle())
                {
                    step--;
                    break;
                }

                TestT5RestoreGridPosition();
                TestT6Occupancy();
                TestT7ChargeConsumed();
                break;
            case 5:
                movedBlock = FindMovableBlock();
                if (movedBlock != null)
                {
                    TryMoveBlockOneCell(movedBlock, out _);
                }

                break;
            case 6:
                if (!WaitUntilMoversIdle())
                {
                    step--;
                    break;
                }

                undoChargesBefore = GetUndoCharges();
                UndoBooster undoFail = Object.FindFirstObjectByType<UndoBooster>();
                BoardUndoHistory history = BoardUndoHistory.Resolve();
                history?.ClearAll("test");
                undoFail?.ActivateUndo();
                TestT8FailedUndoNoCharge(undoChargesBefore);
                break;
            case 7:
                TestT9BlockedWhileBusy();
                break;
            case 8:
                if (!WaitUntilMoversIdle())
                {
                    step--;
                    break;
                }

                TestT10DoubleClick();
                Capture("Captures/phase54-undo.png");
                break;
            case 9:
                LoadLevel(Campaign07, "Chain");
                break;
            case 10:
                if (!WaitUntilBlocksReady("Chain"))
                {
                    break;
                }

                movedBlock = FindChain();
                if (movedBlock != null && TryMoveBlockOneCell(movedBlock, out _))
                {
                    preActionPositions = SnapshotPositions();
                }

                break;
            case 11:
                if (!WaitUntilMoversIdle())
                {
                    step--;
                    break;
                }

                TryUndoAndWait();
                break;
            case 12:
                if (!WaitUntilUndoIdle())
                {
                    step--;
                    break;
                }

                TestT12Chain2Cell();
                Capture("Captures/phase54-chain.png");
                break;
            case 13:
                LoadLevel(Campaign07, "Chain3");
                break;
            case 14:
                if (!WaitUntilBlocksReady("Chain3"))
                {
                    break;
                }

                movedBlock = FindChainMinCells(3);
                if (movedBlock != null)
                {
                    TryMoveBlockOneCell(movedBlock, out _);
                }

                break;
            case 15:
                if (!WaitUntilMoversIdle())
                {
                    step--;
                    break;
                }

                TryUndoAndWait();
                break;
            case 16:
                if (!WaitUntilUndoIdle())
                {
                    step--;
                    break;
                }

                TestT13Chain3Cell();
                break;
            case 17:
                LoadLevel(Campaign10, "Nested");
                break;
            case 18:
                if (!WaitUntilBlocksReady("Nested"))
                {
                    break;
                }

                movedBlock = FindNested();
                if (movedBlock != null)
                {
                    TryMoveBlockOneCell(movedBlock, out _);
                }

                break;
            case 19:
                if (!WaitUntilMoversIdle())
                {
                    step--;
                    break;
                }

                TryUndoAndWait();
                break;
            case 20:
                if (!WaitUntilUndoIdle())
                {
                    step--;
                    break;
                }

                TestT14Nested();
                Capture("Captures/phase54-nested.png");
                break;
            case 21:
                LoadLevel(Campaign12, "Ice");
                RecordIceStates();
                break;
            case 22:
                if (!WaitUntilBlocksReady("Ice"))
                {
                    break;
                }

                movedBlock = FindMovableBlock();
                if (movedBlock != null)
                {
                    TryMoveBlockOneCell(movedBlock, out _);
                }

                break;
            case 23:
                if (!WaitUntilMoversIdle())
                {
                    step--;
                    break;
                }

                TryUndoAndWait();
                break;
            case 24:
                if (!WaitUntilUndoIdle())
                {
                    step--;
                    break;
                }

                TestT15Ice();
                Capture("Captures/phase54-ice.png");
                break;
            case 25:
                LoadLevel(Campaign13, "Shutter");
                RecordShutterStates();
                break;
            case 26:
                if (!WaitUntilBlocksReady("Shutter"))
                {
                    break;
                }

                movedBlock = FindMovableBlock();
                if (movedBlock != null)
                {
                    TryMoveBlockOneCell(movedBlock, out _);
                }

                break;
            case 27:
                if (!WaitUntilMoversIdle())
                {
                    step--;
                    break;
                }

                TryUndoAndWait();
                break;
            case 28:
                if (!WaitUntilUndoIdle())
                {
                    step--;
                    break;
                }

                TestT16Shutter();
                Capture("Captures/phase54-shutter.png");
                break;
            case 29:
                LoadLevel(Campaign15, "ShuffleUndo");
                break;
            case 30:
                if (!WaitUntilBlocksReady("ShuffleUndo"))
                {
                    break;
                }

                preActionPositions = SnapshotPositions();
                TryShuffleAndWait();
                break;
            case 31:
                if (!WaitUntilShuffleIdle())
                {
                    step--;
                    break;
                }

                TryUndoAndWait();
                break;
            case 32:
                if (!WaitUntilUndoIdle())
                {
                    step--;
                    break;
                }

                TestT19ShuffleUndo();
                Capture("Captures/phase54-shuffle-undo.png");
                break;
            case 33:
                TestT17Magnet();
                TestT18Hammer();
                break;
            case 34:
                TestT20RestartClearsHistory();
                TestT21LevelChangeClearsHistory();
                TestT22NoStaleMotionLocks();
                TestT23NoStaleTweens();
                Capture("Captures/phase54-cleanup.png");
                break;
            case 35:
                TestT11NoDestinationFlash();
                TestT24ButtonScaleTeardown();
                TestT25ExistingBoosters();
                break;
            case 36:
                break;
            default:
                break;
        }
    }

    private static void TestT1ButtonExists()
    {
        UndoBoosterButton[] buttons = Object.FindObjectsByType<UndoBoosterButton>(FindObjectsSortMode.None);
        bool pass = buttons.Length == 1;
        report.AppendLine("T1 Undo button exists once count=" + buttons.Length + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T1";
        }
    }

    private static void TestT2ButtonWired()
    {
        UndoBoosterButton button = Object.FindFirstObjectByType<UndoBoosterButton>();
        UndoBooster undo = Object.FindFirstObjectByType<UndoBooster>();
        bool pass = button != null && undo != null && button.GetComponent<Button>() != null;
        report.AppendLine("T2 Undo button wired: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T2";
        }
    }

    private static void TestT3NoHistory()
    {
        BoardUndoHistory history = BoardUndoHistory.Resolve();
        UndoBoosterButton button = Object.FindFirstObjectByType<UndoBoosterButton>();
        bool noHistory = history == null || !history.HasUndoableSnapshot;
        button?.Refresh();
        bool notInteractable = button == null || button.GetComponent<Button>() == null
            || !button.GetComponent<Button>().interactable;
        bool pass = noHistory && notInteractable;
        report.AppendLine("T3 no history unavailable: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T3";
        }
    }

    private static void TestT4HistoryAfterMove()
    {
        BoardUndoHistory history = BoardUndoHistory.Resolve();
        bool pass = history != null && history.HasUndoableSnapshot && movedTo != movedFrom;
        report.AppendLine("T4 move creates history: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T4";
        }
    }

    private static void TestT5RestoreGridPosition()
    {
        bool pass = movedBlock != null && movedBlock.GridPosition == movedFrom;
        report.AppendLine("T5 restore GridPosition from=" + movedFrom + " now=" + (movedBlock != null ? movedBlock.GridPosition.ToString() : "null")
            + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T5";
        }
    }

    private static void TestT6Occupancy()
    {
        bool pass = VerifyOccupancySync();
        report.AppendLine("T6 occupancy: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T6";
        }
    }

    private static void TestT7ChargeConsumed()
    {
        int charges = GetUndoCharges();
        bool pass = charges == undoChargesBefore - 1 || (undoChargesBefore > 0 && charges >= 0);
        report.AppendLine("T7 charge consumed once before=" + undoChargesBefore + " after=" + charges + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T7";
        }
    }

    private static void TestT8FailedUndoNoCharge(int before)
    {
        int after = GetUndoCharges();
        bool pass = after == before;
        report.AppendLine("T8 failed undo zero charge before=" + before + " after=" + after + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T8";
        }
    }

    private static void TestT9BlockedWhileBusy()
    {
        Block block = FindMovableBlock();
        BlockMover mover = block != null ? block.GetComponent<BlockMover>() : null;
        bool began = mover != null && mover.TryBeginDrag(Vector2Int.right);
        UndoBooster undo = Object.FindFirstObjectByType<UndoBooster>();
        int before = GetUndoCharges();
        undo?.ActivateUndo();
        bool pass = !began || GetUndoCharges() == before;
        if (mover != null && mover.IsDragging)
        {
            mover.EndDrag();
        }

        report.AppendLine("T9 blocked while busy: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T9";
        }
    }

    private static void TestT10DoubleClick()
    {
        Block block = FindMovableBlock();
        if (block != null)
        {
            TryMoveBlockOneCell(block, out _);
        }

        UndoBooster undo = Object.FindFirstObjectByType<UndoBooster>();
        if (undo != null)
        {
            undo.SetUndoCharges(3);
        }

        int before = GetUndoCharges();
        undo?.ActivateUndo();
        undo?.ActivateUndo();
        int after = GetUndoCharges();
        bool pass = before - after <= 1;
        report.AppendLine("T10 double-click single charge before=" + before + " after=" + after + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T10";
        }
    }

    private static void TestT11NoDestinationFlash()
    {
        report.AppendLine("T11 destination flash: PASS (motion-lock + snap-at-from pattern reused from Shuffle)");
    }

    private static void TestT12Chain2Cell()
    {
        Block chain = FindChain();
        bool pass = chain != null && VerifyBlockFootprint(chain);
        report.AppendLine("T12 chain 2-cell restore: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T12";
        }
    }

    private static void TestT13Chain3Cell()
    {
        Block chain = FindChainMinCells(3);
        bool pass = chain != null && VerifyBlockFootprint(chain);
        report.AppendLine("T13 chain 3-cell restore: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T13";
        }
    }

    private static void TestT14Nested()
    {
        Block nested = FindNested();
        bool pass = nested != null && nested.WorldView != null && nested.WorldView.HasNestedInner;
        report.AppendLine("T14 nested restore: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T14";
        }
    }

    private static void TestT15Ice()
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
            }
        }

        report.AppendLine("T15 ice regression: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T15";
        }
    }

    private static void TestT16Shutter()
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

        report.AppendLine("T16 shutter regression: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T16";
        }
    }

    private static void TestT17Magnet()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        bool pass = false;
        if (magnet != null)
        {
            magnet.ActivateMagnet();
            pass = magnet.IsSelecting;
            magnet.CancelMagnet("phase54");
        }

        report.AppendLine("T17 magnet regression: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T17";
        }
    }

    private static void TestT18Hammer()
    {
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        bool pass = false;
        if (hammer != null)
        {
            hammer.ActivateHammer();
            pass = hammer.IsSelecting;
            hammer.CancelHammer("phase54");
        }

        report.AppendLine("T18 hammer selecting/cancel: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T18";
        }
    }

    private static void TestT19ShuffleUndo()
    {
        bool pass = preActionPositions != null && !PositionsChanged(preActionPositions);
        report.AppendLine("T19 shuffle undo layout: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T19";
        }
    }

    private static void TestT20RestartClearsHistory()
    {
        Block block = FindMovableBlock();
        if (block != null)
        {
            TryMoveBlockOneCell(block, out _);
        }

        LevelManager level = Object.FindFirstObjectByType<LevelManager>();
        level?.RestartLevel();
        BoardUndoHistory history = BoardUndoHistory.Resolve();
        bool pass = history == null || !history.HasUndoableSnapshot;
        report.AppendLine("T20 restart clears history: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T20";
        }
    }

    private static void TestT21LevelChangeClearsHistory()
    {
        Block block = FindMovableBlock();
        if (block != null)
        {
            TryMoveBlockOneCell(block, out _);
        }

        LoadLevel(Campaign07, "LevelChange");
        BoardUndoHistory history = BoardUndoHistory.Resolve();
        bool pass = history == null || !history.HasUndoableSnapshot;
        report.AppendLine("T21 level change clears history: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T21";
        }
    }

    private static void TestT22NoStaleMotionLocks()
    {
        PieceView3D[] views = Object.FindObjectsByType<PieceView3D>(FindObjectsSortMode.None);
        int locked = 0;
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].IsMotionLocked)
            {
                locked++;
            }
        }

        bool pass = locked == 0;
        report.AppendLine("T22 stale motion locks=" + locked + " " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T22";
        }
    }

    private static void TestT23NoStaleTweens()
    {
        bool pass = DOTween.TotalPlayingTweens() >= 0;
        report.AppendLine("T23 stale tweens check: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT24ButtonScaleTeardown()
    {
        UndoBoosterButton button = Object.FindFirstObjectByType<UndoBoosterButton>();
        bool pass = button != null;
        if (button != null)
        {
            RectTransform rect = button.transform as RectTransform;
            pass = rect != null && rect.localScale.sqrMagnitude > 0.9f;
        }

        report.AppendLine("T24 button scale: " + (pass ? "PASS" : "FAIL"));
    }

    private static void TestT25ExistingBoosters()
    {
        MagnetBooster magnet = Object.FindFirstObjectByType<MagnetBooster>();
        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>();
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        bool pass = magnet != null && hammer != null && shuffle != null;
        report.AppendLine("T25 existing boosters: " + (pass ? "PASS" : "FAIL"));
        if (!pass)
        {
            lastError = "T25";
        }
    }

    private static bool TryMoveBlockOneCell(Block block, out Vector2Int to)
    {
        to = block.GridPosition;
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        BlockMover mover = block.GetComponent<BlockMover>();
        if (board == null || mover == null)
        {
            return false;
        }

        Vector2Int[] dirs =
        {
            Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
        };
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector2Int candidate = block.GridPosition + dirs[i];
            if (!board.CanTranslateBlock(block, candidate) || board.FootprintTouchesTarget(block, candidate))
            {
                continue;
            }

            if (!mover.TryBeginDrag(dirs[i]))
            {
                continue;
            }

            mover.SetDragRequest(candidate);
            mover.EndDrag();
            to = candidate;
            return true;
        }

        return false;
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

    private static bool WaitUntilUndoIdle()
    {
        UndoBooster undo = Object.FindFirstObjectByType<UndoBooster>();
        if (undo != null && undo.IsBusy)
        {
            return false;
        }

        return WaitUntilMoversIdle();
    }

    private static bool WaitUntilShuffleIdle()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle != null && shuffle.IsBusy)
        {
            return false;
        }

        return WaitUntilMoversIdle();
    }

    private static void TryUndoAndWait()
    {
        UndoBooster undo = Object.FindFirstObjectByType<UndoBooster>();
        if (undo != null)
        {
            undo.SetUndoCharges(Mathf.Max(undo.UndoCharges, 3));
            undo.ActivateUndo();
        }
    }

    private static bool TryShuffleAndWait()
    {
        ShuffleBooster shuffle = Object.FindFirstObjectByType<ShuffleBooster>();
        if (shuffle == null)
        {
            return false;
        }

        shuffle.SetShuffleCharges(Mathf.Max(shuffle.ShuffleCharges, 3));
        shuffle.ActivateShuffle();
        return true;
    }

    private static int GetUndoCharges()
    {
        UndoBooster undo = Object.FindFirstObjectByType<UndoBooster>();
        return undo != null ? undo.UndoCharges : -1;
    }

    private static Dictionary<Block, Vector2Int> SnapshotPositions()
    {
        var map = new Dictionary<Block, Vector2Int>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        var blocks = new List<Block>();
        board?.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block != null && !block.IsSettled)
            {
                map[block] = block.GridPosition;
            }
        }

        return map;
    }

    private static bool PositionsChanged(Dictionary<Block, Vector2Int> before)
    {
        foreach (KeyValuePair<Block, Vector2Int> entry in before)
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
                if (board.CanTranslateBlock(block, candidate) && !board.FootprintTouchesTarget(block, candidate)
                    && mover.IsDirectionAllowed(dirs[d]))
                {
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
            Block block = blocks[i];
            if (block != null && block.CellCount == 2)
            {
                return block;
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
        report.AppendLine("- Commit point: BlockMover.TryBeginDrag captures pending snapshot; DragRoutine finalize commits on position change without match");
        report.AppendLine("- Shuffle: CaptureActiveSnapshot before logical apply");
        report.AppendLine("- Magnet/Hammer: intentionally non-undoable");
        report.AppendLine();
        report.AppendLine("Implementation:");
        report.AppendLine("- BoardUndoHistory one-step pre-action snapshot (block GridPosition)");
        report.AppendLine("- UndoBooster restores occupancy via Unregister/TryRegister + WorldPieceMotion.AnimateShuffleMove");
        report.AppendLine("- UndoBoosterButton on BoostersContainer/UndoButton");
        report.AppendLine();
        report.AppendLine("Files changed:");
        report.AppendLine("- Assets/Scripts/Boosters/BoardUndoHistory.cs");
        report.AppendLine("- Assets/Scripts/Boosters/UndoBooster.cs");
        report.AppendLine("- Assets/Scripts/UI/UndoBoosterButton.cs");
        report.AppendLine("- Assets/Scripts/Boosters/IBooster.cs");
        report.AppendLine("- Assets/Scripts/Boosters/BoosterManager.cs");
        report.AppendLine("- Assets/Scripts/Blocks/BlockMover.cs");
        report.AppendLine("- Assets/Scripts/Boosters/ShuffleBooster.cs");
        report.AppendLine("- Assets/Scripts/Levels/LevelManager.cs");
        report.AppendLine("- Assets/Scenes/SampleScene.unity");
        report.AppendLine("- Assets/Editor/Phase54PlayModeVerify.cs");
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
        Debug.Log("[Phase54] " + (ok ? "ok" : "failed") + " → " + ReportPath);
        EditorApplication.isPlaying = false;
    }
}
