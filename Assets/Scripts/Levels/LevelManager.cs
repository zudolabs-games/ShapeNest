using System.Collections;
using System.Collections.Generic;
using StarterKit.UIKit;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Loads a LevelData asset onto the Board. Owns level lifecycle, session timer, and completion.
/// Does not own movement, occupancy, or matching.
/// </summary>
public class LevelManager : MonoBehaviour
{
    public enum SessionState
    {
        Playing,
        Paused,
        Completed,
        TimeExpired
    }

    [SerializeField]
    private LevelData currentLevel;

    [SerializeField]
    private LevelDatabase levelDatabase;

    [SerializeField]
    private BoardManager boardManager;

    [SerializeField]
    private Block blockPrefab;

    [SerializeField]
    private Target targetPrefab;

    [SerializeField]
    private AudioFeedback audioFeedback;

    [SerializeField]
    private HapticFeedback hapticFeedback;

    [SerializeField]
    [Min(1f)]
    [Tooltip("Countdown duration in seconds for each level session.")]
    private float timeLimitSeconds = 90f;

    private readonly List<Block> spawnedBlocks = new List<Block>();
    private readonly List<Target> spawnedTargets = new List<Target>();
    private readonly List<ShutterState> spawnedShutters = new List<ShutterState>();
    private bool isLoading;
    private bool isLevelActive;
    private bool levelComplete;
    private int currentLevelIndex;
    private SessionState session = SessionState.Playing;
    private float remainingSeconds;
    private bool timerRunning;
    private bool timeUpSoundPlayed;
    private Coroutine pauseTimeFreezeRoutine;
    private Coroutine alignedMatchRoutine;
    private Coroutine levelCompletePresentationRoutine;
    private int pieceMatchSequenceDepth;
    private bool alignedMatchRunning;
    private bool hasLastMatch;
    private Vector2Int lastMatchOrigin;
    private Vector2Int lastMatchTargetCell;
    private readonly List<Block> alignedScanBlocks = new List<Block>();
    private readonly List<BlockMover.AlignedMatchAction> alignedMatchActions = new List<BlockMover.AlignedMatchAction>();
    private readonly List<BlockMover.AlignedMovementGroup> alignedMovementGroups = new List<BlockMover.AlignedMovementGroup>();
    private readonly HashSet<int> autoMatchSkipIds = new HashSet<int>();
    private int successfulMatchCount;

    public bool IsAlignedMatchRunning => alignedMatchRunning;

    public int CurrentLevelIndex => currentLevelIndex;
    public LevelData CurrentLevel => currentLevel;
    public SessionState Session => session;
    public float RemainingSeconds => remainingSeconds;
    public bool IsGameplayInputAllowed => session == SessionState.Playing;
    public bool IsPieceInputAllowed => session == SessionState.Playing && pieceMatchSequenceDepth == 0;

    /// <summary>Monotonic match counter for additive systems (e.g. Magnet success detection).</summary>
    public int SuccessfulMatchCount => successfulMatchCount;

    private void Awake()
    {
        if (audioFeedback == null)
        {
            audioFeedback = GetComponent<AudioFeedback>();
        }

        if (hapticFeedback == null)
        {
            hapticFeedback = GetComponent<HapticFeedback>();
        }

        if (GetComponent<BoosterManager>() == null && FindFirstObjectByType<BoosterManager>() == null)
        {
            gameObject.AddComponent<BoosterManager>();
        }

        SyncCurrentLevelIndex(currentLevel);
        remainingSeconds = timeLimitSeconds;
    }

    private void OnDisable()
    {
        Time.timeScale = 1f;
        CancelLevelCompletePresentationWait();
    }

    private void Start()
    {
        if (levelDatabase != null && levelDatabase.Count > 0)
        {
            PlayerProgressManager savedProgress = PlayerProgressManager.Instance;
            int savedIndex = savedProgress.CurrentLevelIndex;
            int unlockedIndex = savedProgress.HighestUnlockedLevel;
            int maxIndex = levelDatabase.Count - 1;
            int startingIndex = Mathf.Clamp(Mathf.Min(savedIndex, unlockedIndex), 0, maxIndex);
            LoadLevel(startingIndex);
        }
        else
        {
            LoadLevel(currentLevel);
        }

        StartCoroutine(SyncSessionScreenWhenUiReady());
    }

    private void Update()
    {
        HandleBackButton();

        if (!timerRunning || session != SessionState.Playing)
        {
            return;
        }

        remainingSeconds -= Time.deltaTime;
        if (remainingSeconds > 0f)
        {
            return;
        }

        remainingSeconds = 0f;
        ExpireTime();
    }

    [ContextMenu("Load Level 0")]
    private void LoadLevelZeroDebug()
    {
        LoadLevel(0);
    }

    public bool LoadLevel(int levelIndex)
    {
        if (isLoading)
        {
            return false;
        }

        if (levelDatabase == null)
        {
            Debug.LogError("LevelManager: LevelDatabase is not assigned.", this);
            return false;
        }

        LevelData level = levelDatabase.GetLevel(levelIndex);
        if (level == null)
        {
            Debug.LogError($"LevelManager: Could not load level at index {levelIndex}.", this);
            return false;
        }

        currentLevelIndex = levelIndex;
        LoadLevel(level);
        return true;
    }

    public bool HasNextLevel =>
        levelDatabase != null && currentLevelIndex + 1 < levelDatabase.Count;

    public bool LoadNextLevel()
    {
        if (isLoading || session != SessionState.Completed)
        {
            return false;
        }

        if (levelDatabase == null || levelDatabase.Count == 0)
        {
            return false;
        }

        bool wrapping = !HasNextLevel;
        int nextIndex = wrapping ? 0 : currentLevelIndex + 1;
        if (wrapping)
        {
            Debug.Log("LevelManager: Final level completed. Looping back to level 0.", this);
        }

        return LoadLevel(nextIndex);
    }

    public void OnNextLevelButton()
    {
        LoadNextLevel();
    }

    [ContextMenu("Restart Level")]
    public void RestartLevel()
    {
        if (currentLevel == null)
        {
            Debug.LogError("LevelManager: Cannot restart because Current Level is not assigned.", this);
            return;
        }

        LoadLevel(currentLevel);
    }

    public void LoadLevel(LevelData level)
    {
        if (isLoading)
        {
            return;
        }

        isLoading = true;

        try
        {
            Time.timeScale = 1f;
            if (pauseTimeFreezeRoutine != null)
            {
                StopCoroutine(pauseTimeFreezeRoutine);
                pauseTimeFreezeRoutine = null;
            }
            StopTimer();
            CancelLevelCompletePresentationWait();
            session = SessionState.Playing;
            isLevelActive = false;
            levelComplete = false;
            timeUpSoundPlayed = false;
            pieceMatchSequenceDepth = 0;
            if (audioFeedback != null)
            {
                audioFeedback.ResetSessionCues();
            }
            StopAlignedMatchQueue();
            hasLastMatch = false;
            currentLevel = level;
            SyncCurrentLevelIndex(level);
            ClearRuntimeLevel();

            BoosterManager boosters = GetComponent<BoosterManager>();
            if (boosters == null)
            {
                boosters = FindFirstObjectByType<BoosterManager>();
            }

            if (boosters != null)
            {
                boosters.ResetAll("level load");
            }
            else
            {
                MagnetBooster magnet = GetComponent<MagnetBooster>();
                if (magnet == null)
                {
                    magnet = FindFirstObjectByType<MagnetBooster>();
                }

                if (magnet != null)
                {
                    magnet.ResetMagnetState("level load");
                }
            }

            BoardUndoHistory undoHistory = GetComponent<BoardUndoHistory>();
            if (undoHistory == null)
            {
                undoHistory = FindFirstObjectByType<BoardUndoHistory>();
            }

            undoHistory?.ClearAll("level load");

            successfulMatchCount = 0;

            if (currentLevel == null)
            {
                Debug.LogError("LevelManager: LevelData is not assigned.", this);
                return;
            }

            if (boardManager == null)
            {
                Debug.LogError("LevelManager: BoardManager is not assigned.", this);
                return;
            }

            boardManager.ApplyGridSize(currentLevel.ResolvedGridWidth, currentLevel.ResolvedGridHeight);
            boardManager.SetStaticBlockedCells(currentLevel.blockedCells);
            SpawnTargets();
            SpawnBlocks();
            SpawnShutters();
            RefreshBoardPresentation();
            isLevelActive = true;
            remainingSeconds = timeLimitSeconds;
            timerRunning = true;
            PlayerProgressManager.Instance.SetCurrentLevel(currentLevelIndex);
        }
        finally
        {
            isLoading = false;
        }

        if (isLevelActive)
        {
            WaitForAlignedMatchQueue();
        }

        SyncSessionScreen();
    }

    public void PauseSession()
    {
        if (session != SessionState.Playing)
        {
            return;
        }

        session = SessionState.Paused;
        timerRunning = false;
        SyncSessionScreen();
        if (pauseTimeFreezeRoutine != null)
        {
            StopCoroutine(pauseTimeFreezeRoutine);
        }

        pauseTimeFreezeRoutine = StartCoroutine(FreezeTimeAfterPauseUi());
    }

    public void ResumeSession()
    {
        if (session != SessionState.Paused)
        {
            return;
        }

        if (pauseTimeFreezeRoutine != null)
        {
            StopCoroutine(pauseTimeFreezeRoutine);
            pauseTimeFreezeRoutine = null;
        }

        Time.timeScale = 1f;
        session = SessionState.Playing;
        timerRunning = true;
        SyncSessionScreen();
    }

    public void RememberLastMatch(Vector2Int origin, Vector2Int targetCell)
    {
        hasLastMatch = true;
        lastMatchOrigin = origin;
        lastMatchTargetCell = targetCell;
    }

    public void StopAlignedMatchQueue()
    {
        if (alignedMatchRoutine != null)
        {
            StopCoroutine(alignedMatchRoutine);
            alignedMatchRoutine = null;
        }

        alignedMatchRunning = false;
        autoMatchSkipIds.Clear();
        if (pieceMatchSequenceDepth > 0)
        {
            pieceMatchSequenceDepth = 0;
        }
    }

    public Coroutine WaitForAlignedMatchQueue()
    {
        if (alignedMatchRoutine != null)
        {
            return alignedMatchRoutine;
        }

        alignedMatchRoutine = StartCoroutine(ResolveAlreadyAlignedMatchQueue());
        return alignedMatchRoutine;
    }

    private IEnumerator ResolveAlreadyAlignedMatchQueue()
    {
        if (alignedMatchRunning || boardManager == null)
        {
            alignedMatchRoutine = null;
            yield break;
        }

        alignedMatchRunning = true;
        autoMatchSkipIds.Clear();
        BlockMover.ResetMatchSequenceIndex();
        BeginPieceMatchSequence();
        try
        {
            const int maxPasses = 32;
            bool attemptedOrphanRebind = false;
            bool attemptedSkipClear = false;

            for (int pass = 0; pass < maxPasses; pass++)
            {
                boardManager.RebindChildBlockOccupancy();
                alignedScanBlocks.Clear();
                boardManager.CollectUniqueBlocks(alignedScanBlocks);
                for (int i = 0; i < alignedScanBlocks.Count; i++)
                {
                    BlockMover.EnsureSubjectOccupancy(boardManager, alignedScanBlocks[i]);
                }

                // Phase 66/67: collect match actions, fold into connected-block movement groups,
                // then start all groups together.
                int groupCount = BlockMover.CollectAlignedMovementGroups(
                    boardManager,
                    alignedScanBlocks,
                    autoMatchSkipIds,
                    hasLastMatch,
                    lastMatchOrigin,
                    lastMatchTargetCell,
                    alignedMatchActions,
                    alignedMovementGroups);

                if (groupCount <= 0)
                {
                    if (!attemptedOrphanRebind)
                    {
                        attemptedOrphanRebind = true;
                        int n = boardManager.RebindChildBlockOccupancy();
                        if (n > 0)
                        {
                            continue;
                        }
                    }

                    if (!attemptedSkipClear && autoMatchSkipIds.Count > 0)
                    {
                        attemptedSkipClear = true;
                        autoMatchSkipIds.Clear();
                        continue;
                    }

                    yield break;
                }

                attemptedOrphanRebind = false;
                attemptedSkipClear = false;

                var runners = new List<(BlockMover acting, BlockMover.AlignedMovementGroup group)>(groupCount);
                for (int w = 0; w < alignedMovementGroups.Count; w++)
                {
                    BlockMover.AlignedMovementGroup group = alignedMovementGroups[w];
                    Block subject = group != null ? group.Subject : null;
                    if (subject == null || group.Actions.Count == 0)
                    {
                        continue;
                    }

                    BlockMover acting = subject.GetComponent<BlockMover>();
                    if (acting == null)
                    {
                        Vector2Int skipNest = group.Actions[0].NestTo;
                        autoMatchSkipIds.Add(BlockMover.AutoMatchSkipKey(subject.GetInstanceID(), skipNest));
                        continue;
                    }

                    BlockMover.EnsureSubjectOccupancy(boardManager, subject);
                    bool anyValid = false;
                    for (int a = 0; a < group.Actions.Count; a++)
                    {
                        if (BlockMover.IsChainCellAutoMatchValid(
                                boardManager,
                                subject,
                                group.Actions[a].NestTo))
                        {
                            anyValid = true;
                            break;
                        }
                    }

                    if (!anyValid)
                    {
                        boardManager.RebindChildBlockOccupancy();
                        BlockMover.EnsureSubjectOccupancy(boardManager, subject);
                        for (int a = 0; a < group.Actions.Count; a++)
                        {
                            if (BlockMover.IsChainCellAutoMatchValid(
                                    boardManager,
                                    subject,
                                    group.Actions[a].NestTo))
                            {
                                anyValid = true;
                                break;
                            }
                        }
                    }

                    if (!anyValid)
                    {
                        Vector2Int skipNest = group.Actions[0].NestTo;
                        autoMatchSkipIds.Add(BlockMover.AutoMatchSkipKey(subject.GetInstanceID(), skipNest));
                        continue;
                    }

                    runners.Add((acting, group));
                }

                if (runners.Count == 0)
                {
                    continue;
                }

                int remaining = runners.Count;
                bool anyConsumeSucceeded = false;
                float gap = 0.22f;

                for (int r = 0; r < runners.Count; r++)
                {
                    BlockMover acting = runners[r].acting;
                    BlockMover.AlignedMovementGroup group = runners[r].group;
                    if (acting.MatchingTargetPause > gap)
                    {
                        gap = acting.MatchingTargetPause;
                    }

                    acting.StartCoroutine(PlayWaveGroupThenSignal(
                        acting,
                        group,
                        () =>
                        {
                            remaining--;
                            if (acting.LastResolvedConsumeSucceeded)
                            {
                                anyConsumeSucceeded = true;
                            }
                            else if (group.Actions.Count > 0)
                            {
                                autoMatchSkipIds.Add(
                                    BlockMover.AutoMatchSkipKey(
                                        group.Subject.GetInstanceID(),
                                        group.Actions[0].NestTo));
                            }

                            BlockMover.LogAutoChainSequenceAfterMatch(boardManager, group.Subject);
                        }));
                }

                while (remaining > 0)
                {
                    yield return null;
                }

                yield return WaitRealtimeGap(gap);

                if (anyConsumeSucceeded)
                {
                    autoMatchSkipIds.Clear();
                    yield return null;
                    boardManager.RebindChildBlockOccupancy();
                    yield return null;
                    continue;
                }
            }
        }
        finally
        {
            alignedMatchRunning = false;
            alignedMatchRoutine = null;
            EndPieceMatchSequence();
            NotifyBlockSettled();
        }
    }

    private IEnumerator PlayWaveGroupThenSignal(
        BlockMover acting,
        BlockMover.AlignedMovementGroup group,
        System.Action onComplete)
    {
        try
        {
            BlockMover.LastConsumeSucceeded = false;
            if (acting != null)
            {
                yield return acting.StartCoroutine(acting.PlayResolvedMovementGroup(boardManager, group));
            }
        }
        finally
        {
            onComplete?.Invoke();
        }
    }

    private IEnumerator PlayWaveMemberThenSignal(
        BlockMover acting,
        Block subject,
        Vector2Int nestTo,
        System.Action onComplete)
    {
        try
        {
            BlockMover.LastConsumeSucceeded = false;
            if (acting != null)
            {
                yield return acting.StartCoroutine(acting.PlayResolvedAutoMatch(boardManager, nestTo));
            }
        }
        finally
        {
            onComplete?.Invoke();
        }
    }

    private IEnumerator WaitRealtimeGap(float seconds)
    {
        if (seconds <= 0f)
        {
            yield return null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    public void BeginPieceMatchSequence()
    {
        pieceMatchSequenceDepth++;
    }

    public void EndPieceMatchSequence()
    {
        if (pieceMatchSequenceDepth > 0)
        {
            pieceMatchSequenceDepth--;
        }
    }

    public void NotifySuccessfulMatch()
    {
        successfulMatchCount++;

        for (int i = 0; i < spawnedBlocks.Count; i++)
        {
            Block candidate = spawnedBlocks[i];
            if (candidate == null)
            {
                continue;
            }

            IceState ice = candidate.GetComponent<IceState>();
            if (ice != null)
            {
                ice.ConsumeSuccessfulMatch();
            }
        }

        for (int i = 0; i < spawnedShutters.Count; i++)
        {
            ShutterState shutter = spawnedShutters[i];
            if (shutter != null)
            {
                shutter.ConsumeSuccessfulMatch();
            }
        }
    }

    public void NotifyBlockSettled()
    {
        if (!isLevelActive || isLoading || levelComplete || boardManager == null)
        {
            return;
        }

        if (session == SessionState.TimeExpired || session == SessionState.Paused)
        {
            return;
        }

        if (!boardManager.AreAllMatchesComplete())
        {
            return;
        }

        levelComplete = true;
        session = SessionState.Completed;
        StopTimer();
        Time.timeScale = 1f;
        PlayerProgressManager.Instance.MarkLevelCompleted(
            currentLevelIndex,
            levelDatabase != null ? levelDatabase.Count : -1);

        if (IsHammerCompletionPresentationActive())
        {
            if (levelCompletePresentationRoutine != null)
            {
                StopCoroutine(levelCompletePresentationRoutine);
            }

            levelCompletePresentationRoutine = StartCoroutine(PresentLevelCompleteWhenHammerFinishes());
            return;
        }

        PresentLevelCompleteScreen();
    }

    private static bool IsHammerCompletionPresentationActive()
    {
        HammerBooster hammer = FindFirstObjectByType<HammerBooster>(FindObjectsInactive.Exclude);
        return hammer != null && hammer.IsPresentationActive;
    }

    private IEnumerator PresentLevelCompleteWhenHammerFinishes()
    {
        while (IsHammerCompletionPresentationActive())
        {
            yield return null;
        }

        levelCompletePresentationRoutine = null;
        if (!levelComplete || session != SessionState.Completed || isLoading)
        {
            yield break;
        }

        PresentLevelCompleteScreen();
    }

    private void PresentLevelCompleteScreen()
    {
        Debug.Log("LEVEL COMPLETE!");
        if (audioFeedback != null)
        {
            audioFeedback.PlayLevelComplete();
        }

        if (hapticFeedback != null)
        {
            hapticFeedback.PlayLevelComplete();
        }

        SyncSessionScreen();
    }

    private void CancelLevelCompletePresentationWait()
    {
        if (levelCompletePresentationRoutine == null)
        {
            return;
        }

        StopCoroutine(levelCompletePresentationRoutine);
        levelCompletePresentationRoutine = null;
    }

    private void ExpireTime()
    {
        if (session != SessionState.Playing || levelComplete)
        {
            return;
        }

        session = SessionState.TimeExpired;
        StopTimer();
        remainingSeconds = 0f;
        Time.timeScale = 1f;
        if (!timeUpSoundPlayed)
        {
            timeUpSoundPlayed = true;
            if (audioFeedback != null)
            {
                audioFeedback.PlayFailure();
            }

            if (hapticFeedback != null)
            {
                hapticFeedback.PlayFailure();
            }
        }

        SyncSessionScreen();
    }

    private IEnumerator SyncSessionScreenWhenUiReady()
    {
        yield return null;
        SyncSessionScreen();
    }

    private void SyncSessionScreen()
    {
        UIController ui = UIController.instance;
        if (ui == null)
        {
            return;
        }

        ScreenType wanted = ScreenType.Gameplay;
        if (session == SessionState.Completed)
        {
            wanted = ScreenType.LevelComplete;
        }
        else if (session == SessionState.TimeExpired)
        {
            wanted = ScreenType.GameOver;
        }
        else if (session == SessionState.Paused)
        {
            wanted = ScreenType.Settings;
        }

        ScreenType active = ui.GetActiveScreen();
        if (active == wanted || active == ScreenType.None)
        {
            return;
        }

        ui.ShowNextScreen(wanted);
    }

    private void HandleBackButton()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        if (session == SessionState.Playing)
        {
            PauseSession();
            return;
        }

        if (session == SessionState.Paused)
        {
            ResumeSession();
        }
    }

    private IEnumerator FreezeTimeAfterPauseUi()
    {
        yield return null;
        if (session == SessionState.Paused)
        {
            Time.timeScale = 0f;
        }

        pauseTimeFreezeRoutine = null;
    }

    private void StopTimer()
    {
        timerRunning = false;
    }

    private void SyncCurrentLevelIndex(LevelData level)
    {
        if (levelDatabase == null || level == null)
        {
            return;
        }

        IReadOnlyList<LevelData> levels = levelDatabase.Levels;
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] == level)
            {
                currentLevelIndex = i;
                return;
            }
        }
    }

    private void SpawnTargets()
    {
        if (currentLevel.targets == null)
        {
            return;
        }

        if (targetPrefab == null)
        {
            Debug.LogError("LevelManager: Target prefab is not assigned.", this);
            return;
        }

        var boardRect = (RectTransform)boardManager.transform;

        for (int i = 0; i < currentLevel.targets.Count; i++)
        {
            LevelTargetData data = currentLevel.targets[i];
            if (data == null)
            {
                continue;
            }

            Target target = Instantiate(targetPrefab, boardRect, false);
            target.ApplyLayout(data.shapeType, data.cells, data.composition, data.outerShape);
            target.Initialize(boardManager, data.gridPosition);
            spawnedTargets.Add(target);
        }
    }

    private void SpawnShutters()
    {
        if (currentLevel == null || currentLevel.shutters == null || currentLevel.shutters.Count == 0 || boardManager == null)
        {
            return;
        }

        var boardRect = (RectTransform)boardManager.transform;
        for (int i = 0; i < currentLevel.shutters.Count; i++)
        {
            LevelShutterData data = currentLevel.shutters[i];
            if (data == null || data.cells == null || data.cells.Count == 0)
            {
                continue;
            }

            GameObject shutterObject = new GameObject($"Shutter_{i + 1}", typeof(RectTransform));
            shutterObject.transform.SetParent(boardRect, false);
            ShutterState shutter = shutterObject.AddComponent<ShutterState>();
            shutter.Configure(boardManager, data);
            spawnedShutters.Add(shutter);
        }
    }

    private void SpawnBlocks()
    {
        if (currentLevel.blocks == null)
        {
            return;
        }

        if (blockPrefab == null)
        {
            Debug.LogError("LevelManager: Block prefab is not assigned.", this);
            return;
        }

        var boardRect = (RectTransform)boardManager.transform;

        for (int i = 0; i < currentLevel.blocks.Count; i++)
        {
            LevelBlockData data = currentLevel.blocks[i];
            if (data == null)
            {
                continue;
            }

            Block block = Instantiate(blockPrefab, boardRect, false);
            block.ApplyLayout(data.shapeType, data.cells, data.composition, data.outerShape);
            block.MoveDirection = data.moveDirection;
            block.Initialize(boardManager, data.gridPosition);
            block.ConfigureIce(data.hasIce, data.iceDurability);

            BlockMover mover = block.GetComponent<BlockMover>();
            if (mover != null)
            {
                mover.SetLevelManager(this);
                mover.SetAudioFeedback(audioFeedback);
                mover.SetHapticFeedback(hapticFeedback);
            }

            spawnedBlocks.Add(block);
        }
    }

    public Block SpawnSplitBlock(Block template, IReadOnlyList<ShapeCellData> remainingCells, Vector2Int worldAnchor)
    {
        if (blockPrefab == null || boardManager == null || template == null)
        {
            return null;
        }

        var boardRect = (RectTransform)boardManager.transform;
        Block block = Instantiate(blockPrefab, boardRect, false);
        block.MoveDirection = template.MoveDirection;
        block.ApplyLayout(
            remainingCells.Count > 0 && remainingCells[0] != null
                ? remainingCells[0].shapeType
                : template.ShapeType,
            remainingCells,
            PieceComposition.Simple,
            remainingCells.Count > 0 && remainingCells[0] != null
                ? remainingCells[0].shapeType
                : template.OuterShape);
        block.Initialize(boardManager, worldAnchor);

        BlockMover mover = block.GetComponent<BlockMover>();
        if (mover != null)
        {
            mover.SetLevelManager(this);
            mover.SetAudioFeedback(audioFeedback);
            mover.SetHapticFeedback(hapticFeedback);
        }

        spawnedBlocks.Add(block);
        return block;
    }

    private void ClearRuntimeLevel()
    {
        StopAlignedMatchQueue();

        for (int i = spawnedShutters.Count - 1; i >= 0; i--)
        {
            ShutterState shutter = spawnedShutters[i];
            if (shutter == null)
            {
                continue;
            }

            if (boardManager != null)
            {
                boardManager.UnregisterShutter(shutter);
            }

            DestroyRuntimeLevelObject(shutter.gameObject);
        }

        spawnedShutters.Clear();
        if (boardManager != null)
        {
            boardManager.ClearStaticBlockedCells();
        }

        pieceMatchSequenceDepth = 0;
        if (boardManager != null)
        {
            MatchEffect[] effects = boardManager.GetComponentsInChildren<MatchEffect>(true);
            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i] != null)
                {
                    DestroyRuntimeLevelObject(effects[i].gameObject);
                }
            }

            DestroyNamedBoardLeftovers(boardManager.transform, "InnerTravel");
            DestroyNamedBoardLeftovers(boardManager.transform, "CellTravel");
        }

        for (int i = spawnedBlocks.Count - 1; i >= 0; i--)
        {
            Block block = spawnedBlocks[i];
            if (block == null)
            {
                continue;
            }

            if (boardManager != null)
            {
                boardManager.UnregisterBlock(block);
            }

            DestroyRuntimeLevelObject(block.gameObject);
        }

        spawnedBlocks.Clear();

        for (int i = spawnedTargets.Count - 1; i >= 0; i--)
        {
            Target target = spawnedTargets[i];
            if (target == null)
            {
                continue;
            }

            if (boardManager != null)
            {
                boardManager.UnregisterTarget(target);
            }

            DestroyRuntimeLevelObject(target.gameObject);
        }

        spawnedTargets.Clear();

        // Sweep any orphans under the board (include inactive), e.g. split survivors
        // lost from tracking lists or legacy test spawns that bypassed LevelManager.
        DestroyOrphanRuntimeLevelObjects();

        if (boardManager != null)
        {
            boardManager.ClearRuntimeRegistrations();
        }

        BoardPresentationController.ClearNestedInnerTravelers();
    }

    /// <summary>
    /// Destroys remaining Block / Target / ShutterState children under the board,
    /// including inactive objects. Safe to call after the tracked spawned* lists
    /// have already been cleared.
    /// </summary>
    private void DestroyOrphanRuntimeLevelObjects()
    {
        if (boardManager == null)
        {
            return;
        }

        Transform board = boardManager.transform;

        Block[] blocks = board.GetComponentsInChildren<Block>(true);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null)
            {
                continue;
            }

            boardManager.UnregisterBlock(block);
            DestroyRuntimeLevelObject(block.gameObject);
        }

        Target[] targets = board.GetComponentsInChildren<Target>(true);
        for (int i = 0; i < targets.Length; i++)
        {
            Target target = targets[i];
            if (target == null)
            {
                continue;
            }

            boardManager.UnregisterTarget(target);
            DestroyRuntimeLevelObject(target.gameObject);
        }

        ShutterState[] shutters = board.GetComponentsInChildren<ShutterState>(true);
        for (int i = 0; i < shutters.Length; i++)
        {
            ShutterState shutter = shutters[i];
            if (shutter == null)
            {
                continue;
            }

            boardManager.UnregisterShutter(shutter);
            DestroyRuntimeLevelObject(shutter.gameObject);
        }
    }

    /// <summary>
    /// Immediate destroy so ClearRuntimeLevel + Spawn can run in the same frame
    /// without leaving inactive pending-Destroy clones under the board.
    /// </summary>
    private static void DestroyRuntimeLevelObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        target.SetActive(false);
        DestroyImmediate(target);
    }

    private static void DestroyNamedBoardLeftovers(Transform board, string childName)
    {
        if (board == null || string.IsNullOrEmpty(childName))
        {
            return;
        }

        for (int i = board.childCount - 1; i >= 0; i--)
        {
            Transform child = board.GetChild(i);
            if (child != null && child.name == childName)
            {
                DestroyRuntimeLevelObject(child.gameObject);
            }
        }
    }

    private void RefreshBoardPresentation()
    {
        if (boardManager == null)
        {
            return;
        }

        BoardVisual visual = boardManager.GetComponent<BoardVisual>();
        if (visual != null)
        {
            visual.RefreshPresentation();
        }

        for (int i = 0; i < spawnedBlocks.Count; i++)
        {
            if (spawnedBlocks[i] != null)
            {
                spawnedBlocks[i].RefreshLayoutVisuals();
            }
        }

        for (int i = 0; i < spawnedTargets.Count; i++)
        {
            if (spawnedTargets[i] != null)
            {
                spawnedTargets[i].RefreshLayoutVisuals();
            }
        }

        for (int i = 0; i < spawnedShutters.Count; i++)
        {
            if (spawnedShutters[i] != null)
            {
                spawnedShutters[i].RefreshLayoutVisuals();
            }
        }

        // Phase 60: bind PieceView3D immediately after spawn so picking does not
        // depend on waiting for BoardPresentationController.LateUpdate.
        BoardPresentationController presentation =
            FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Include);
        if (presentation != null)
        {
            presentation.EnsureWorldViewsBound();
        }
    }
}
