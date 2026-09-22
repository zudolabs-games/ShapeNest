#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Audits and exercises the existing Shutter mechanic without rewriting gameplay.
/// Menu: Tools/Shape Nest/Verify Shutter Mechanic
/// </summary>
public static class ShutterMechanicVerification
{
    private const string ReportPath = "Assets/ShutterMechanicVerificationReport.txt";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    private static readonly StringBuilder Report = new StringBuilder();
    private static int passCount;
    private static int failCount;

    [MenuItem("Tools/Shape Nest/Verify Shutter Mechanic")]
    public static void RunFromMenu()
    {
        Run(false);
    }

    public static void RunFromCommandLine()
    {
        Run(true);
    }

    private static void Run(bool quitWhenDone)
    {
        Report.Clear();
        passCount = 0;
        failCount = 0;
        Report.AppendLine("Shutter Mechanic Verification");
        Report.AppendLine($"Unity {Application.unityVersion}");
        Report.AppendLine($"Time: {System.DateTime.Now:O}");
        Report.AppendLine($"PlayMode: {EditorApplication.isPlaying}");
        Report.AppendLine();

        AuditSourceContracts();
        AuditExistingLevelAssets();
        RunSyntheticRuntimeCases();
        AttemptLiveSceneInspection();

        Report.AppendLine();
        Report.AppendLine($"SUMMARY: PASS={passCount} FAIL={failCount}");
        File.WriteAllText(ReportPath, Report.ToString());
        AssetDatabase.Refresh();
        Debug.Log(Report.ToString());

        if (quitWhenDone)
        {
            EditorApplication.Exit(failCount > 0 ? 1 : 0);
        }
    }

    private static void AuditSourceContracts()
    {
        Report.AppendLine("=== Source contract audit ===");

        Expect(
            "NotifySuccessfulMatch calls ConsumeSuccessfulMatch on every spawned shutter",
            SourceContains(
                "Assets/Scripts/Levels/LevelManager.cs",
                "shutter.ConsumeSuccessfulMatch();"));

        Expect(
            "ConsumeAndRebuild notifies LevelManager once per successful cell consume",
            SourceContains(
                "Assets/Scripts/Blocks/BlockMover.cs",
                "levelManager.NotifySuccessfulMatch();")
            && CountOccurrences(
                "Assets/Scripts/Blocks/BlockMover.cs",
                "NotifySuccessfulMatch()") == 1);

        Expect(
            "ConsumeAndRebuild processes only the first focused nest cell per call",
            SourceContains(
                "Assets/Scripts/Blocks/BlockMover.cs",
                "int cellIndex = nestCellIndices[0];"));

        Expect(
            "ShutterState keeps a single durability int",
            SourceContains("Assets/Scripts/Levels/ShutterState.cs", "private int durability;"));

        Expect(
            "ShutterState creates one durability label object",
            SourceContains("Assets/Scripts/Levels/ShutterState.cs", "ShutterDurability")
            && CountOccurrences("Assets/Scripts/Levels/ShutterState.cs", "new GameObject(\"ShutterDurability\"") == 1);

        Expect(
            "Closed shutter blocks movement via BoardManager",
            SourceContains("Assets/Scripts/Board/BoardManager.cs", "DoesFootprintTouchClosedShutter")
            && SourceContains("Assets/Scripts/Blocks/BlockMover.cs", "IsBlockUnderClosedShutter(block)"));

        Expect(
            "Auto-match skips blocks under closed shutters",
            SourceContains("Assets/Scripts/Blocks/BlockMover.cs", "IsBlockUnderClosedShutter(candidate)"));

        Expect(
            "Open unlock unregisters shutter from BoardManager",
            SourceContains("Assets/Scripts/Levels/ShutterState.cs", "boardManager.UnregisterShutter(this);"));

        Report.AppendLine();
    }

    private static void AuditExistingLevelAssets()
    {
        Report.AppendLine("=== Existing shutter level assets ===");

        LevelData level7 = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Levels/Level7.asset");
        LevelData level8 = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Levels/Level8.asset");
        LevelData level9 = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Levels/Level9.asset");
        LevelData level10 = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Levels/Level10.asset");

        Expect("Level7 has one single-cell shutter", level7 != null && level7.shutters != null && level7.shutters.Count == 1 && level7.shutters[0].cells.Count == 1);
        Expect("Level8 has multiple independent shutters", level8 != null && level8.shutters != null && level8.shutters.Count >= 2);
        Expect("Level9 has multiple independent shutters", level9 != null && level9.shutters != null && level9.shutters.Count >= 2);
        Expect(
            "Level10 has one multi-cell shutter covering multiple structures",
            level10 != null
            && level10.shutters != null
            && level10.shutters.Count == 1
            && level10.shutters[0].cells.Count == 4
            && level10.shutters[0].durability == 2);

        if (level10 != null && level10.shutters != null && level10.shutters.Count == 1)
        {
            HashSet<Vector2Int> covered = new HashSet<Vector2Int>(level10.shutters[0].cells);
            HashSet<ShapeType> shapesUnder = new HashSet<ShapeType>();
            int structuresUnder = 0;
            for (int i = 0; i < level10.blocks.Count; i++)
            {
                LevelBlockData block = level10.blocks[i];
                if (block == null)
                {
                    continue;
                }

                if (covered.Contains(block.gridPosition))
                {
                    structuresUnder++;
                    shapesUnder.Add(block.shapeType);
                }
            }

            Expect(
                "Level10 shutter covers multiple different structures (Square/Circle/Triangle)",
                structuresUnder >= 3 && shapesUnder.Count >= 3);
            Report.AppendLine($"  Level10 under-shutter structures={structuresUnder} shapes={shapesUnder.Count}");
        }

        Report.AppendLine();
    }

    private static void RunSyntheticRuntimeCases()
    {
        Report.AppendLine("=== Synthetic runtime cases ===");

        var host = new GameObject("ShutterVerificationHost");
        try
        {
            var boardObject = new GameObject("Board", typeof(RectTransform), typeof(BoardManager));
            boardObject.transform.SetParent(host.transform, false);
            BoardManager board = boardObject.GetComponent<BoardManager>();
            board.ApplyGridSize(6, 6);

            // Case A: multi-cell region, single counter, unlock all at once.
            ShutterState multi = CreateShutter(
                board,
                "MultiRegion",
                durability: 3,
                new Vector2Int(1, 1),
                new Vector2Int(2, 1),
                new Vector2Int(1, 2),
                new Vector2Int(2, 2));

            Expect("Multi-cell shutter starts closed", multi.IsClosed && multi.Durability == 3);
            Expect("Multi-cell shutter covers all authored cells", multi.CoversCell(new Vector2Int(1, 1)) && multi.CoversCell(new Vector2Int(2, 2)));
            Expect("Multi-cell shutter has exactly one durability TMP label", CountDurabilityLabels(multi) == 1);
            Expect("Board blocks every covered cell while closed",
                board.IsCellBlockedByClosedShutter(new Vector2Int(1, 1))
                && board.IsCellBlockedByClosedShutter(new Vector2Int(2, 1))
                && board.IsCellBlockedByClosedShutter(new Vector2Int(1, 2))
                && board.IsCellBlockedByClosedShutter(new Vector2Int(2, 2)));

            multi.ConsumeSuccessfulMatch();
            Expect("Match #1 decrements multi shutter exactly once (3→2)", multi.Durability == 2 && multi.IsClosed);
            Expect("After match #1 still exactly one durability label", CountDurabilityLabels(multi) == 1);

            multi.ConsumeSuccessfulMatch();
            Expect("Match #2 decrements multi shutter exactly once (2→1)", multi.Durability == 1 && multi.IsClosed);

            multi.ConsumeSuccessfulMatch();
            Expect("Match #3 opens shutter (1→0)", multi.Durability == 0 && !multi.IsClosed);
            Expect(
                "Zero durability unlocks entire multi-cell region simultaneously",
                !multi.CoversCell(new Vector2Int(1, 1))
                && !multi.CoversCell(new Vector2Int(2, 1))
                && !multi.CoversCell(new Vector2Int(1, 2))
                && !multi.CoversCell(new Vector2Int(2, 2))
                && !board.IsCellBlockedByClosedShutter(new Vector2Int(1, 1))
                && !board.IsCellBlockedByClosedShutter(new Vector2Int(2, 2)));

            // Case B: independent shutters share the global match event bus.
            ShutterState a = CreateShutter(board, "IndependentA", 2, new Vector2Int(0, 0));
            ShutterState b = CreateShutter(board, "IndependentB", 4, new Vector2Int(5, 5));
            Expect("Independent shutters each have one durability label", CountDurabilityLabels(a) == 1 && CountDurabilityLabels(b) == 1);

            SimulateGlobalMatchNotify(new[] { a, b });
            Expect("Global match decrements every closed shutter once", a.Durability == 1 && b.Durability == 3);
            SimulateGlobalMatchNotify(new[] { a, b });
            Expect("Second global match opens A and leaves B closed", a.Durability == 0 && !a.IsClosed && b.Durability == 2 && b.IsClosed);
            Expect("Opened shutter A no longer blocks; B still blocks",
                !board.IsCellBlockedByClosedShutter(new Vector2Int(0, 0))
                && board.IsCellBlockedByClosedShutter(new Vector2Int(5, 5)));
            SimulateGlobalMatchNotify(new[] { a, b });
            Expect("Opened shutter ignores further match consumes", a.Durability == 0 && b.Durability == 1);

            // Case C: LevelManager.NotifySuccessfulMatch wiring via reflection against live list field.
            Expect(
                "LevelManager.NotifySuccessfulMatch method exists",
                typeof(LevelManager).GetMethod("NotifySuccessfulMatch", BindingFlags.Instance | BindingFlags.Public) != null);

            FieldInfo shuttersField = typeof(LevelManager).GetField("spawnedShutters", BindingFlags.Instance | BindingFlags.NonPublic);
            Expect("LevelManager stores spawnedShutters list", shuttersField != null && shuttersField.FieldType == typeof(List<ShutterState>));

            // Case D: coverage is cell-based, structure-agnostic (single/chain/SIS share same gate).
            Expect(
                "BoardManager shutter gate is cell/footprint based (structure-agnostic)",
                SourceContains("Assets/Scripts/Board/BoardManager.cs", "DoesFootprintTouchClosedShutter(Block block, Vector2Int toAnchor)")
                && SourceContains("Assets/Scripts/Board/BoardManager.cs", "block.GetLocalCell(i)"));
        }
        finally
        {
            Object.DestroyImmediate(host);
        }

        Report.AppendLine();
    }

    private static void AttemptLiveSceneInspection()
    {
        Report.AppendLine("=== Live scene / Play Mode inspection ===");

        if (!File.Exists(ScenePath))
        {
            Fail("SampleScene missing");
            return;
        }

        if (!EditorApplication.isPlaying)
        {
            Report.AppendLine("SKIP live Play Mode shutter spawn checks (Editor not in Play Mode).");
            Report.AppendLine("Open SampleScene, enter Play Mode, load Level10/Level8, then re-run this menu.");
            Report.AppendLine();
            return;
        }

        LevelManager levelManager = Object.FindFirstObjectByType<LevelManager>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (levelManager == null || board == null)
        {
            Fail("Play Mode missing LevelManager or BoardManager");
            return;
        }

        LevelData level10 = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Levels/Level10.asset");
        if (level10 != null)
        {
            levelManager.LoadLevel(level10);
            ShutterState[] shutters = board.GetComponentsInChildren<ShutterState>(true);
            Expect("Level10 spawns exactly one ShutterState", shutters.Length == 1);
            if (shutters.Length == 1)
            {
                Expect("Level10 runtime durability is 2", shutters[0].Durability == 2);
                Expect("Level10 runtime covers 4 cells", shutters[0].Cells.Count == 4);
                Expect("Level10 runtime has one durability label", CountDurabilityLabels(shutters[0]) == 1);

                List<Block> under = new List<Block>();
                board.CollectUniqueBlocks(under);
                int blocked = 0;
                for (int i = 0; i < under.Count; i++)
                {
                    if (board.IsBlockUnderClosedShutter(under[i]))
                    {
                        blocked++;
                    }
                }

                Expect("Level10 blocks multiple structures under the closed shutter", blocked >= 3);
                Report.AppendLine($"  Live Level10 blocked structures={blocked}");
            }
        }

        LevelData level8 = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Levels/Level8.asset");
        if (level8 != null)
        {
            levelManager.LoadLevel(level8);
            ShutterState[] shutters = board.GetComponentsInChildren<ShutterState>(true);
            Expect("Level8 spawns multiple independent ShutterState instances", shutters.Length >= 2);
            int labelTotal = 0;
            for (int i = 0; i < shutters.Length; i++)
            {
                labelTotal += CountDurabilityLabels(shutters[i]);
            }

            Expect("Level8 has exactly one durability label per shutter", labelTotal == shutters.Length);
        }

        Report.AppendLine();
    }

    private static ShutterState CreateShutter(BoardManager board, string name, int durability, params Vector2Int[] cells)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(ShutterState));
        go.transform.SetParent(board.transform, false);
        ShutterState shutter = go.GetComponent<ShutterState>();
        var data = new LevelShutterData
        {
            durability = durability,
            cells = new List<Vector2Int>(cells)
        };
        shutter.Configure(board, data);
        return shutter;
    }

    private static void SimulateGlobalMatchNotify(IReadOnlyList<ShutterState> shutters)
    {
        for (int i = 0; i < shutters.Count; i++)
        {
            if (shutters[i] != null)
            {
                shutters[i].ConsumeSuccessfulMatch();
            }
        }
    }

    private static int CountDurabilityLabels(ShutterState shutter)
    {
        if (shutter == null)
        {
            return 0;
        }

        TMP_Text[] texts = shutter.GetComponentsInChildren<TMP_Text>(true);
        int count = 0;
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].name.Contains("ShutterDurability"))
            {
                count++;
            }
        }

        return count;
    }

    private static bool SourceContains(string relativePath, string needle)
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
        if (!File.Exists(path))
        {
            return false;
        }

        return File.ReadAllText(path).Contains(needle);
    }

    private static int CountOccurrences(string relativePath, string needle)
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
        if (!File.Exists(path))
        {
            return 0;
        }

        string text = File.ReadAllText(path);
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static void Expect(string label, bool condition)
    {
        if (condition)
        {
            passCount++;
            Report.AppendLine($"PASS: {label}");
        }
        else
        {
            failCount++;
            Report.AppendLine($"FAIL: {label}");
        }
    }

    private static void Fail(string label)
    {
        Expect(label, false);
    }
}
#endif
