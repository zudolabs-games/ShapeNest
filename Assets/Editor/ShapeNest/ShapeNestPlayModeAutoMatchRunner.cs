using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Play Mode harness for Circle→Triangle sequential auto-match.
/// Menu: Tools/Shape Nest/Play Mode Test Circle→Triangle (Aligned)
/// Menu: Tools/Shape Nest/Play Mode Test Circle→Triangle (Adjacent)
/// Writes Logs/AutoMatchPlayMode.log
/// </summary>
internal static class ShapeNestPlayModeAutoMatchRunner
{
    private const string LogPath = "Logs/AutoMatchPlayMode.log";
    private static bool waitingForPlayMode;
    private static StringBuilder liveLog;
    private static string levelName;
    private static bool expectManualMagnet;

    [MenuItem("Tools/Shape Nest/Play Mode Test Circle→Triangle (Aligned)")]
    public static void RunAlignedFromMenu()
    {
        ShapeNestSequentialAutoMatchDiag.CreateDebugLevel();
        StartPlayModeTest("Debug_CircleTriangle_AutoMatch", expectManual: false);
    }

    [MenuItem("Tools/Shape Nest/Play Mode Test Circle→Triangle (Adjacent)")]
    public static void RunAdjacentFromMenu()
    {
        ShapeNestSequentialAutoMatchDiag.CreateAdjacentDebugLevel();
        StartPlayModeTest("Debug_CircleTriangle_AdjacentAutoMatch", expectManual: true);
    }

    [MenuItem("Tools/Shape Nest/Play Mode Test Circle→Triangle→Square")]
    public static void RunCtsFromMenu()
    {
        ShapeNestSequentialAutoMatchDiag.CreateCircleTriangleSquareLevel();
        StartPlayModeTest("Debug_CircleTriangleSquare_AutoMatch", expectManual: false);
    }

    private static void StartPlayModeTest(string level, bool expectManual)
    {
        levelName = level;
        expectManualMagnet = expectManual;
        liveLog = new StringBuilder();
        liveLog.AppendLine($"=== Play Mode auto-match test level={level} manualMagnet={expectManual} ===");
        liveLog.AppendLine($"time={System.DateTime.Now:O}");

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        waitingForPlayMode = true;

        if (!EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = true;
        }
        else
        {
            EditorApplication.delayCall += StartHarnessInPlayMode;
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!waitingForPlayMode)
        {
            return;
        }

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += StartHarnessInPlayMode;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            waitingForPlayMode = false;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            FlushLog();
        }
    }

    private static void StartHarnessInPlayMode()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        LevelManager levelManager = Object.FindFirstObjectByType<LevelManager>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>($"Assets/Levels/{levelName}.asset");

        Append($"LevelManager={(levelManager != null)} Board={(board != null)} Level={(level != null)} name={levelName}");

        if (levelManager == null || level == null)
        {
            Append("FAIL: missing LevelManager or level asset in Play Mode scene");
            EditorApplication.isPlaying = false;
            return;
        }

        levelManager.LoadLevel(level);
        Append($"LoadLevel({levelName}) called");

        var host = new GameObject("AutoMatchPlayModeWatcher");
        host.AddComponent<PlayModeWatcher>().Init(
            Append,
            expectManualMagnet,
            () => { EditorApplication.isPlaying = false; });
    }

    private static void Append(string line)
    {
        liveLog ??= new StringBuilder();
        liveLog.AppendLine($"[{Time.frameCount}] {line}");
        //Debug.Log("[PlayModeAutoMatch] " + line);
    }

    private static void FlushLog()
    {
        if (liveLog == null)
        {
            return;
        }

        string path = Path.Combine(Application.dataPath, "..", LogPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Logs");
        File.WriteAllText(path, liveLog.ToString());
       // Debug.Log($"Wrote {path}");
        liveLog = null;
    }

    private sealed class PlayModeWatcher : MonoBehaviour
    {
        private System.Action<string> append;
        private System.Action done;
        private float start;
        private bool finished;
        private bool expectManual;
        private bool magnetTriggered;

        public void Init(System.Action<string> appendLog, bool manualMagnet, System.Action onDone)
        {
            append = appendLog;
            done = onDone;
            expectManual = manualMagnet;
            start = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (finished)
            {
                return;
            }

            LevelManager lm = Object.FindFirstObjectByType<LevelManager>();
            BoardManager board = Object.FindFirstObjectByType<BoardManager>();
            float elapsed = Time.realtimeSinceStartup - start;

            if (expectManual && !magnetTriggered && board != null && elapsed > 0.4f)
            {
                magnetTriggered = true;
                TriggerAdjacentMagnet(board);
            }

            if (lm != null && lm.Session == LevelManager.SessionState.Completed)
            {
                finished = true;
                append?.Invoke("PASS: Level Complete reached in Play Mode");
                done?.Invoke();
                return;
            }

            if (elapsed > 12f)
            {
                finished = true;
                DumpFail(board);
                done?.Invoke();
            }
        }

        private void TriggerAdjacentMagnet(BoardManager board)
        {
            var unique = new List<Block>();
            board.CollectUniqueBlocks(unique);
            append?.Invoke($"manual magnet: uniqueBlocks={unique.Count}");
            if (unique.Count == 0)
            {
                append?.Invoke("FAIL: no block to magnet");
                return;
            }

            Block chain = unique[0];
            BlockMover mover = chain.GetComponent<BlockMover>();
            if (mover == null)
            {
                append?.Invoke("FAIL: BlockMover missing");
                return;
            }

            // Circle nest is one cell below the chain anchor for the adjacent debug level.
            Vector2Int nest = chain.GridPosition + Vector2Int.down;
            append?.Invoke($"manual magnet PlayAlignedMagnetMatch nest={nest}");
            mover.StartCoroutine(mover.PlayAlignedMagnetMatch(board, nest));
        }

        private void DumpFail(BoardManager board)
        {
            if (board == null)
            {
                append?.Invoke("FAIL: timeout, board null");
                return;
            }

            int rebound = board.RebindChildBlockOccupancy();
            var unique = new List<Block>();
            board.CollectUniqueBlocks(unique);
            append?.Invoke(
                $"FAIL: timeout. uniqueBlocks={unique.Count} rebound={rebound} " +
                $"complete={board.AreAllMatchesComplete()}");
            Block[] children = board.GetComponentsInChildren<Block>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Block b = children[i];
                if (b == null)
                {
                    continue;
                }

                Target t = board.GetTargetAt(b.GridPosition);
                append?.Invoke(
                    $" remaining Block={b.GetInstanceID()} pos={b.GridPosition} " +
                    $"shape={b.GetActiveShape(0)} settled={b.IsSettled} " +
                    $"occ={(board.GetBlockAt(b.GridPosition) == b)} " +
                    $"target={(t != null ? t.RequiredShape.ToString() : "NULL")}");
                BlockMover.LogPostFirstMatchState(board, b);
            }
        }
    }
}
