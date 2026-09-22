using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode simulation of Circle consume → Triangle survivor auto-match.
/// Steps the real PlayResolvedAutoMatch coroutines with durations zeroed.
/// </summary>
internal static class ShapeNestSequentialAutoMatchDiag
{
    [MenuItem("Tools/Shape Nest/Diagnose Circle→Triangle Auto-Match")]
    public static void RunFromMenu()
    {
        string report = RunCircleTriangleSimulation();
        if (report.Contains("FAIL"))
        {
            Debug.LogError(report);
        }
        else
        {
            Debug.Log(report);
        }
    }

    [MenuItem("Tools/Shape Nest/Create Debug Circle-Triangle Level")]
    public static void CreateDebugLevel()
    {
        var blocks = new List<LevelBlockData>
        {
            new LevelBlockData
            {
                shapeType = ShapeType.Circle,
                moveDirection = MoveDirection.Any,
                gridPosition = new Vector2Int(1, 2),
                cells = new List<ShapeCellData>
                {
                    new ShapeCellData
                    {
                        localPosition = Vector2Int.zero,
                        shapeType = ShapeType.Circle,
                        innerShapes = new List<ShapeType>()
                    },
                    new ShapeCellData
                    {
                        localPosition = new Vector2Int(1, 0),
                        shapeType = ShapeType.Triangle,
                        innerShapes = new List<ShapeType>()
                    }
                },
                composition = PieceComposition.Simple,
                outerShape = ShapeType.Circle
            }
        };
        var targets = new List<LevelTargetData>
        {
            new LevelTargetData
            {
                shapeType = ShapeType.Circle,
                gridPosition = new Vector2Int(1, 2),
                cells = new List<ShapeCellData>(),
                composition = PieceComposition.Simple,
                outerShape = ShapeType.Circle
            },
            new LevelTargetData
            {
                shapeType = ShapeType.Triangle,
                gridPosition = new Vector2Int(2, 2),
                cells = new List<ShapeCellData>(),
                composition = PieceComposition.Simple,
                outerShape = ShapeType.Triangle
            }
        };

        LevelData asset = LevelAssetUtility.SaveLevelData(
            "Debug_CircleTriangle_AutoMatch",
            blocks,
            targets,
            overwrite: true,
            gridWidth: 4,
            gridHeight: 4);
        LevelAssetUtility.AddToLevelDatabase(asset);
        AssetDatabase.SaveAssets();
        Debug.Log(
            asset != null
                ? "Created Debug_CircleTriangle_AutoMatch (4x4 Circle—Triangle aligned)."
                : "Failed to create debug level.");
    }

    [MenuItem("Tools/Shape Nest/Create Debug Circle-Triangle Adjacent Level")]
    public static void CreateAdjacentDebugLevel()
    {
        var blocks = new List<LevelBlockData>
        {
            new LevelBlockData
            {
                shapeType = ShapeType.Circle,
                moveDirection = MoveDirection.Any,
                gridPosition = new Vector2Int(1, 2),
                cells = new List<ShapeCellData>
                {
                    new ShapeCellData
                    {
                        localPosition = Vector2Int.zero,
                        shapeType = ShapeType.Circle,
                        innerShapes = new List<ShapeType>()
                    },
                    new ShapeCellData
                    {
                        localPosition = new Vector2Int(1, 0),
                        shapeType = ShapeType.Triangle,
                        innerShapes = new List<ShapeType>()
                    }
                },
                composition = PieceComposition.Simple,
                outerShape = ShapeType.Circle
            }
        };
        var targets = new List<LevelTargetData>
        {
            new LevelTargetData
            {
                shapeType = ShapeType.Circle,
                gridPosition = new Vector2Int(1, 1),
                cells = new List<ShapeCellData>(),
                composition = PieceComposition.Simple,
                outerShape = ShapeType.Circle
            },
            new LevelTargetData
            {
                shapeType = ShapeType.Triangle,
                gridPosition = new Vector2Int(2, 1),
                cells = new List<ShapeCellData>(),
                composition = PieceComposition.Simple,
                outerShape = ShapeType.Triangle
            }
        };

        LevelData asset = LevelAssetUtility.SaveLevelData(
            "Debug_CircleTriangle_AdjacentAutoMatch",
            blocks,
            targets,
            overwrite: true,
            gridWidth: 4,
            gridHeight: 4);
        LevelAssetUtility.AddToLevelDatabase(asset);
        AssetDatabase.SaveAssets();
        Debug.Log(
            asset != null
                ? "Created Debug_CircleTriangle_AdjacentAutoMatch (chain one row above nests)."
                : "Failed to create adjacent debug level.");
    }

    public static void RunBatch()
    {
        CreateDebugLevel();
        CreateAdjacentDebugLevel();
        CreateCircleTriangleSquareLevel();
        string occupying = RunCircleTriangleSimulation();
        string adjacent = RunCircleTriangleAdjacentMagnetSimulation();
        string middle = RunMiddleCellSurvivorSimulation();
        string cts = RunAdjacentCtsSequentialSimulation();
        string report = occupying + "\n" + adjacent + "\n" + middle + "\n" + cts;
        Debug.Log(report);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    [MenuItem("Tools/Shape Nest/Create Debug Circle-Triangle-Square Level")]
    public static void CreateCircleTriangleSquareLevel()
    {
        var blocks = new List<LevelBlockData>
        {
            new LevelBlockData
            {
                shapeType = ShapeType.Circle,
                moveDirection = MoveDirection.Any,
                gridPosition = new Vector2Int(1, 2),
                cells = new List<ShapeCellData>
                {
                    new ShapeCellData
                    {
                        localPosition = Vector2Int.zero,
                        shapeType = ShapeType.Circle,
                        innerShapes = new List<ShapeType>()
                    },
                    new ShapeCellData
                    {
                        localPosition = new Vector2Int(1, 0),
                        shapeType = ShapeType.Triangle,
                        innerShapes = new List<ShapeType>()
                    },
                    new ShapeCellData
                    {
                        localPosition = new Vector2Int(2, 0),
                        shapeType = ShapeType.Square,
                        innerShapes = new List<ShapeType>()
                    }
                },
                composition = PieceComposition.Simple,
                outerShape = ShapeType.Circle
            }
        };
        var targets = new List<LevelTargetData>
        {
            new LevelTargetData
            {
                shapeType = ShapeType.Circle,
                gridPosition = new Vector2Int(1, 2),
                cells = new List<ShapeCellData>(),
                composition = PieceComposition.Simple,
                outerShape = ShapeType.Circle
            },
            new LevelTargetData
            {
                shapeType = ShapeType.Triangle,
                gridPosition = new Vector2Int(2, 2),
                cells = new List<ShapeCellData>(),
                composition = PieceComposition.Simple,
                outerShape = ShapeType.Triangle
            },
            new LevelTargetData
            {
                shapeType = ShapeType.Square,
                gridPosition = new Vector2Int(3, 2),
                cells = new List<ShapeCellData>(),
                composition = PieceComposition.Simple,
                outerShape = ShapeType.Square
            }
        };

        LevelData asset = LevelAssetUtility.SaveLevelData(
            "Debug_CircleTriangleSquare_AutoMatch",
            blocks,
            targets,
            overwrite: true,
            gridWidth: 5,
            gridHeight: 4);
        LevelAssetUtility.AddToLevelDatabase(asset);
        AssetDatabase.SaveAssets();
        Debug.Log(
            asset != null
                ? "Created Debug_CircleTriangleSquare_AutoMatch (5x4 CTS aligned)."
                : "Failed to create CTS debug level.");
    }

    /// <summary>
    /// Adjacent magnet: only Circle traveler moves. Triangle must stay on its original cell
    /// (no whole-chain TryMoveBlock). Auto-match only if Triangle is already occupying.
    /// </summary>
    public static string RunCircleTriangleAdjacentMagnetSimulation()
    {
        var log = new StringBuilder();
        log.AppendLine("=== Circle→Triangle ADJACENT: focused traveler only (no whole-chain move) ===");

        var boardGo = new GameObject("DiagBoardAdj", typeof(RectTransform), typeof(BoardManager));
        var chainGo = new GameObject("DiagChainAdj", typeof(RectTransform), typeof(Block), typeof(BlockMover));
        var circleTargetGo = new GameObject("DiagCircleTargetAdj", typeof(RectTransform), typeof(Target));
        var triangleTargetGo = new GameObject("DiagTriangleTargetAdj", typeof(RectTransform), typeof(Target));

        try
        {
            BoardManager board = boardGo.GetComponent<BoardManager>();
            board.ApplyGridSize(4, 4);

            Vector2Int circleStart = new Vector2Int(1, 2);
            Vector2Int triangleStart = new Vector2Int(2, 2);
            Vector2Int circleNest = new Vector2Int(1, 1);
            Vector2Int triangleNest = new Vector2Int(2, 1);

            Block chain = chainGo.GetComponent<Block>();
            chain.ApplyLayout(
                ShapeType.Circle,
                new List<ShapeCellData>
                {
                    new ShapeCellData
                    {
                        localPosition = Vector2Int.zero,
                        shapeType = ShapeType.Circle,
                        innerShapes = new List<ShapeType>()
                    },
                    new ShapeCellData
                    {
                        localPosition = new Vector2Int(1, 0),
                        shapeType = ShapeType.Triangle,
                        innerShapes = new List<ShapeType>()
                    }
                },
                PieceComposition.Simple,
                ShapeType.Circle);
            chain.Initialize(board, circleStart);

            circleTargetGo.GetComponent<Target>().ApplyLayout(ShapeType.Circle, null, PieceComposition.Simple, ShapeType.Circle);
            circleTargetGo.GetComponent<Target>().Initialize(board, circleNest);
            triangleTargetGo.GetComponent<Target>().ApplyLayout(ShapeType.Triangle, null, PieceComposition.Simple, ShapeType.Triangle);
            triangleTargetGo.GetComponent<Target>().Initialize(board, triangleNest);

            BlockMover mover = chainGo.GetComponent<BlockMover>();
            typeof(BlockMover)
                .GetField("block", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(mover, chain);
            ZeroMatchDurations(mover);

            log.AppendLine($"BEFORE: Circle={circleStart} Triangle={triangleStart}");

            BlockMover.LastConsumeSucceeded = false;
            RunCoroutineToEnd(mover.PlayAlignedMagnetMatch(board, circleNest));
            log.AppendLine($"After Circle magnet: LastConsumeSucceeded={BlockMover.LastConsumeSucceeded}");
            log.AppendLine($"  survivor GridPosition={chain.GridPosition} shape={chain.GetActiveShape(0)} cells={chain.CellCount}");

            if (!BlockMover.LastConsumeSucceeded)
            {
                log.AppendLine("FAIL: Circle magnet did not consume");
                return log.ToString();
            }

            // Triangle must remain on its ORIGINAL cell — not translated with the chain.
            if (chain.GridPosition != triangleStart
                || board.GetBlockAt(triangleStart) != chain
                || board.GetBlockAt(triangleNest) != null)
            {
                log.AppendLine(
                    $"FAIL: Triangle moved or wrong occupancy. expectedStay={triangleStart} " +
                    $"got={chain.GridPosition} atStart={(board.GetBlockAt(triangleStart) == chain)} " +
                    $"atNest={(board.GetBlockAt(triangleNest) != null)}");
                return log.ToString();
            }

            log.AppendLine("PASS: Adjacent Circle traveler only — Triangle stayed on original cell");

            BlockMover.LogChainAutoMatchPostMatch(board, chain, circleStart, ShapeType.Circle);

            var unique = new List<Block>();
            bool found = BlockMover.TryFindNextAlignedMatch(
                board,
                unique,
                null,
                true,
                circleStart,
                circleNest,
                out Block next,
                out Vector2Int nestTo);
            string occupyingReject = BlockMover.ExplainAlignedCellRejection(board, chain, 0, triangleStart, null);
            bool adj = BlockMover.TryGetAdjacentAutoMatchDest(
                board, chain, 0, triangleStart, null, out Vector2Int adjDest, out string adjReject);
            log.AppendLine(
                $"POST-CIRCLE occupyingReject={(occupyingReject ?? "none")} " +
                $"adjacent={adj} dest={adjDest} adjReject={(adjReject ?? "none")} " +
                $"scanFound={found} nestTo={nestTo}");

            if (!found || nestTo != triangleNest || next != chain)
            {
                log.AppendLine("FAIL: Triangle adjacent dest was not selected after Circle match");
                return log.ToString();
            }

            BlockMover.LastConsumeSucceeded = false;
            RunCoroutineToEnd(mover.PlayResolvedAutoMatch(board, triangleNest));
            if (!BlockMover.LastConsumeSucceeded
                || board.GetBlockAt(triangleStart) != null
                || board.GetBlockAt(triangleNest) != null
                || board.GetTargetAt(triangleNest) != null)
            {
                log.AppendLine("FAIL: Triangle sequential auto-match did not consume");
                return log.ToString();
            }

            log.AppendLine("PASS: Adjacent Circle then Triangle sequential focused-cell auto-match");
        }
        finally
        {
            Object.DestroyImmediate(chainGo);
            Object.DestroyImmediate(circleTargetGo);
            Object.DestroyImmediate(triangleTargetGo);
            Object.DestroyImmediate(boardGo);
        }

        return log.ToString();
    }

    /// <summary>Test A: middle Triangle match leaves Circle and Square on exact original cells.</summary>
    public static string RunMiddleCellSurvivorSimulation()
    {
        var log = new StringBuilder();
        log.AppendLine("=== Middle cell C—T—S survivor positions ===");

        var boardGo = new GameObject("DiagBoardMid", typeof(RectTransform), typeof(BoardManager));
        var chainGo = new GameObject("DiagChainMid", typeof(RectTransform), typeof(Block), typeof(BlockMover));
        var targetGo = new GameObject("DiagTriangleTargetMid", typeof(RectTransform), typeof(Target));

        try
        {
            BoardManager board = boardGo.GetComponent<BoardManager>();
            board.ApplyGridSize(5, 4);

            Vector2Int circleWorld = new Vector2Int(1, 2);
            Vector2Int triangleWorld = new Vector2Int(2, 2);
            Vector2Int squareWorld = new Vector2Int(3, 2);
            // Adjacent magnet nest below Triangle only.
            Vector2Int triangleNest = new Vector2Int(2, 1);

            Block chain = chainGo.GetComponent<Block>();
            chain.ApplyLayout(
                ShapeType.Circle,
                new List<ShapeCellData>
                {
                    new ShapeCellData
                    {
                        localPosition = Vector2Int.zero,
                        shapeType = ShapeType.Circle,
                        innerShapes = new List<ShapeType>()
                    },
                    new ShapeCellData
                    {
                        localPosition = new Vector2Int(1, 0),
                        shapeType = ShapeType.Triangle,
                        innerShapes = new List<ShapeType>()
                    },
                    new ShapeCellData
                    {
                        localPosition = new Vector2Int(2, 0),
                        shapeType = ShapeType.Square,
                        innerShapes = new List<ShapeType>()
                    }
                },
                PieceComposition.Simple,
                ShapeType.Circle);
            chain.Initialize(board, circleWorld);

            targetGo.GetComponent<Target>().ApplyLayout(ShapeType.Triangle, null, PieceComposition.Simple, ShapeType.Triangle);
            targetGo.GetComponent<Target>().Initialize(board, triangleNest);

            BlockMover mover = chainGo.GetComponent<BlockMover>();
            typeof(BlockMover)
                .GetField("block", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(mover, chain);
            ZeroMatchDurations(mover);

            // SpawnSplitBlock needs LevelManager + blockPrefab + boardManager.
            var lmGo = new GameObject("DiagLevelManagerMid");
            LevelManager lm = lmGo.AddComponent<LevelManager>();
            typeof(LevelManager)
                .GetField("boardManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(lm, board);
            typeof(LevelManager)
                .GetField("blockPrefab", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(lm, chain);
            mover.SetLevelManager(lm);

            // Magnet focus is occupancy+direction (same as drag TryGetAdjacentMatchingTarget),
            // not the middle cell's nest world. Delta must be cardinal from chain anchor.
            Vector2Int magnetFocus = circleWorld + Vector2Int.down;
            log.AppendLine($"BEFORE: C={circleWorld} T={triangleWorld} S={squareWorld} magnetFocus={magnetFocus}");

            BlockMover.LastConsumeSucceeded = false;
            RunCoroutineToEnd(mover.PlayAlignedMagnetMatch(board, magnetFocus));

            // After middle consume, expect two components at original C and S cells.
            var unique = new List<Block>();
            board.CollectUniqueBlocks(unique);
            log.AppendLine($"After middle match: uniqueBlocks={unique.Count} LastConsume={BlockMover.LastConsumeSucceeded}");

            Block atC = board.GetBlockAt(circleWorld);
            Block atS = board.GetBlockAt(squareWorld);
            Block atT = board.GetBlockAt(triangleWorld);
            Block atNest = board.GetBlockAt(triangleNest);

            log.AppendLine(
                $"  atC={(atC != null ? atC.GetActiveShape(0).ToString() : "NULL")} " +
                $"atS={(atS != null ? atS.GetActiveShape(0).ToString() : "NULL")} " +
                $"atT={(atT != null)} atNest={(atNest != null)}");

            bool ok = BlockMover.LastConsumeSucceeded
                && atC != null
                && atC.GetActiveShape(0) == ShapeType.Circle
                && atS != null
                && atS.GetActiveShape(0) == ShapeType.Square
                && atT == null
                && atNest == null
                && atC.GridPosition == circleWorld
                && atS.GridPosition == squareWorld;

            log.AppendLine(ok
                ? "PASS: Middle Triangle match — C and S stayed on original cells"
                : "FAIL: Middle Triangle match moved survivors or wrong occupancy");

            Object.DestroyImmediate(lmGo);
        }
        finally
        {
            Object.DestroyImmediate(chainGo);
            Object.DestroyImmediate(targetGo);
            Object.DestroyImmediate(boardGo);
        }

        return log.ToString();
    }

    /// <summary>Circle—Triangle—Square one row above matching targets; sequential focused-cell auto-match.</summary>
    public static string RunAdjacentCtsSequentialSimulation()
    {
        var log = new StringBuilder();
        log.AppendLine("=== Adjacent C—T—S sequential focused-cell auto-match ===");

        var boardGo = new GameObject("DiagBoardCts", typeof(RectTransform), typeof(BoardManager));
        var chainGo = new GameObject("DiagChainCts", typeof(RectTransform), typeof(Block), typeof(BlockMover));
        var cGo = new GameObject("C", typeof(RectTransform), typeof(Target));
        var tGo = new GameObject("T", typeof(RectTransform), typeof(Target));
        var sGo = new GameObject("S", typeof(RectTransform), typeof(Target));

        try
        {
            BoardManager board = boardGo.GetComponent<BoardManager>();
            board.ApplyGridSize(5, 4);
            Vector2Int cPos = new Vector2Int(1, 2);
            Vector2Int tPos = new Vector2Int(2, 2);
            Vector2Int sPos = new Vector2Int(3, 2);
            Vector2Int cNest = new Vector2Int(1, 1);
            Vector2Int tNest = new Vector2Int(2, 1);
            Vector2Int sNest = new Vector2Int(3, 1);

            Block chain = chainGo.GetComponent<Block>();
            chain.ApplyLayout(
                ShapeType.Circle,
                new List<ShapeCellData>
                {
                    new ShapeCellData { localPosition = Vector2Int.zero, shapeType = ShapeType.Circle, innerShapes = new List<ShapeType>() },
                    new ShapeCellData { localPosition = new Vector2Int(1, 0), shapeType = ShapeType.Triangle, innerShapes = new List<ShapeType>() },
                    new ShapeCellData { localPosition = new Vector2Int(2, 0), shapeType = ShapeType.Square, innerShapes = new List<ShapeType>() }
                },
                PieceComposition.Simple,
                ShapeType.Circle);
            chain.Initialize(board, cPos);
            cGo.GetComponent<Target>().ApplyLayout(ShapeType.Circle, null, PieceComposition.Simple, ShapeType.Circle);
            cGo.GetComponent<Target>().Initialize(board, cNest);
            tGo.GetComponent<Target>().ApplyLayout(ShapeType.Triangle, null, PieceComposition.Simple, ShapeType.Triangle);
            tGo.GetComponent<Target>().Initialize(board, tNest);
            sGo.GetComponent<Target>().ApplyLayout(ShapeType.Square, null, PieceComposition.Simple, ShapeType.Square);
            sGo.GetComponent<Target>().Initialize(board, sNest);

            BlockMover mover = chainGo.GetComponent<BlockMover>();
            typeof(BlockMover).GetField("block", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(mover, chain);
            ZeroMatchDurations(mover);

            BlockMover.LastConsumeSucceeded = false;
            RunCoroutineToEnd(mover.PlayAlignedMagnetMatch(board, cNest));
            if (!BlockMover.LastConsumeSucceeded || board.GetBlockAt(tPos) != chain || board.GetBlockAt(sPos) != chain)
            {
                log.AppendLine($"FAIL: after Circle, T/S moved. T={board.GetBlockAt(tPos) == chain} S={board.GetBlockAt(sPos) == chain}");
                return log.ToString();
            }

            var unique = new List<Block>();
            if (!BlockMover.TryFindNextAlignedMatch(board, unique, null, true, cPos, cNest, out _, out Vector2Int nestTo)
                || nestTo != tNest)
            {
                log.AppendLine($"FAIL: expected Triangle dest {tNest} got {nestTo}");
                return log.ToString();
            }

            BlockMover.LastConsumeSucceeded = false;
            RunCoroutineToEnd(mover.PlayResolvedAutoMatch(board, tNest));
            if (!BlockMover.LastConsumeSucceeded || board.GetBlockAt(sPos) != chain)
            {
                log.AppendLine("FAIL: after Triangle, Square did not stay");
                return log.ToString();
            }

            if (!BlockMover.TryFindNextAlignedMatch(board, unique, null, true, tPos, tNest, out _, out nestTo)
                || nestTo != sNest)
            {
                log.AppendLine($"FAIL: expected Square dest {sNest} got {nestTo}");
                return log.ToString();
            }

            BlockMover.LastConsumeSucceeded = false;
            RunCoroutineToEnd(mover.PlayResolvedAutoMatch(board, sNest));
            if (!BlockMover.LastConsumeSucceeded
                || board.GetBlockAt(sPos) != null
                || board.GetTargetAt(sNest) != null)
            {
                log.AppendLine("FAIL: Square sequential auto-match did not consume");
                return log.ToString();
            }

            log.AppendLine("PASS: Adjacent C→T→S sequential focused-cell auto-match");
        }
        finally
        {
            Object.DestroyImmediate(chainGo);
            Object.DestroyImmediate(cGo);
            Object.DestroyImmediate(tGo);
            Object.DestroyImmediate(sGo);
            Object.DestroyImmediate(boardGo);
        }

        return log.ToString();
    }

    public static string RunCircleTriangleSimulation()
    {
        var log = new StringBuilder();
        log.AppendLine("=== Circle→Triangle PlayResolvedAutoMatch simulation ===");

        var boardGo = new GameObject("DiagBoard", typeof(RectTransform), typeof(BoardManager));
        var chainGo = new GameObject("DiagChain", typeof(RectTransform), typeof(Block), typeof(BlockMover));
        var circleTargetGo = new GameObject("DiagCircleTarget", typeof(RectTransform), typeof(Target));
        var triangleTargetGo = new GameObject("DiagTriangleTarget", typeof(RectTransform), typeof(Target));

        try
        {
            BoardManager board = boardGo.GetComponent<BoardManager>();
            board.ApplyGridSize(4, 4);

            Vector2Int circleWorld = new Vector2Int(1, 2);
            Vector2Int triangleWorld = new Vector2Int(2, 2);

            Block chain = chainGo.GetComponent<Block>();
            chain.ApplyLayout(
                ShapeType.Circle,
                new List<ShapeCellData>
                {
                    new ShapeCellData
                    {
                        localPosition = Vector2Int.zero,
                        shapeType = ShapeType.Circle,
                        innerShapes = new List<ShapeType>()
                    },
                    new ShapeCellData
                    {
                        localPosition = new Vector2Int(1, 0),
                        shapeType = ShapeType.Triangle,
                        innerShapes = new List<ShapeType>()
                    }
                },
                PieceComposition.Simple,
                ShapeType.Circle);
            chain.Initialize(board, circleWorld);

            circleTargetGo.GetComponent<Target>().ApplyLayout(ShapeType.Circle, null, PieceComposition.Simple, ShapeType.Circle);
            circleTargetGo.GetComponent<Target>().Initialize(board, circleWorld);
            triangleTargetGo.GetComponent<Target>().ApplyLayout(ShapeType.Triangle, null, PieceComposition.Simple, ShapeType.Triangle);
            triangleTargetGo.GetComponent<Target>().Initialize(board, triangleWorld);

            BlockMover mover = chainGo.GetComponent<BlockMover>();
            typeof(BlockMover)
                .GetField("block", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(mover, chain);
            ZeroMatchDurations(mover);

            log.AppendLine($"BEFORE: Block={chain.GetInstanceID()} cells={chain.CellCount} at {chain.GridPosition}");

            BlockMover.LastConsumeSucceeded = false;
            RunCoroutineToEnd(mover.PlayResolvedAutoMatch(board, circleWorld));
            log.AppendLine($"After Circle PlayResolvedAutoMatch: LastConsumeSucceeded={BlockMover.LastConsumeSucceeded}");
            log.AppendLine($"  survivor pos={chain.GridPosition} cells={chain.CellCount} settled={chain.IsSettled} shape={chain.GetActiveShape(0)}");
            log.AppendLine($"  GetBlockAt(C)={(board.GetBlockAt(circleWorld) != null)} GetBlockAt(T)={(board.GetBlockAt(triangleWorld) != null)}");
            log.AppendLine($"  GetTargetAt(C)={(board.GetTargetAt(circleWorld) != null)} GetTargetAt(T)={(board.GetTargetAt(triangleWorld) != null)}");

            if (!BlockMover.LastConsumeSucceeded)
            {
                log.AppendLine("FAIL: Circle PlayResolvedAutoMatch did not consume");
                return log.ToString();
            }

            string reject = BlockMover.ExplainAlignedCellRejection(board, chain, 0, triangleWorld, null);
            var unique = new List<Block>();
            bool found = BlockMover.TryFindNextAlignedMatch(
                board, unique, null, true, circleWorld, circleWorld, out Block subject, out Vector2Int nestTo);
            log.AppendLine($"POST-CIRCLE reject={(reject ?? "none")} found={found} nestTo={nestTo}");
            log.AppendLine($"CollectUnique contains survivor={unique.Contains(chain)}");
            log.AppendLine($"IsWorldCellOccupying={BlockMover.IsWorldCellOccupyingAlignedMatch(board, chain, triangleWorld)}");
            log.AppendLine($"TryRevalidate={mover.TryRevalidateAlignedCandidate(board, triangleWorld)}");

            if (reject != null)
            {
                log.AppendLine($"REJECT Triangle:\n- {reject}");
                log.AppendLine("FAIL: Triangle not occupying-aligned after Circle path");
                return log.ToString();
            }

            if (!found || nestTo != triangleWorld)
            {
                log.AppendLine("FAIL: scan did not select Triangle");
                return log.ToString();
            }

            log.AppendLine("PASS: Triangle is occupying candidate after Circle PlayResolvedAutoMatch");

            BlockMover.LastConsumeSucceeded = false;
            RunCoroutineToEnd(mover.PlayResolvedAutoMatch(board, triangleWorld));
            log.AppendLine($"After Triangle PlayResolvedAutoMatch: LastConsumeSucceeded={BlockMover.LastConsumeSucceeded}");
            log.AppendLine($"  GetBlockAt(T)={(board.GetBlockAt(triangleWorld) != null)} GetTargetAt(T)={(board.GetTargetAt(triangleWorld) != null)}");

            if (!BlockMover.LastConsumeSucceeded
                || board.GetBlockAt(triangleWorld) != null
                || board.GetTargetAt(triangleWorld) != null)
            {
                log.AppendLine("FAIL: Triangle PlayResolvedAutoMatch did not fully consume");
            }
            else
            {
                log.AppendLine("PASS: Circle then Triangle sequential auto-match via PlayResolvedAutoMatch");
            }
        }
        finally
        {
            Object.DestroyImmediate(chainGo);
            Object.DestroyImmediate(circleTargetGo);
            Object.DestroyImmediate(triangleTargetGo);
            Object.DestroyImmediate(boardGo);
        }

        return log.ToString();
    }

    private static void ZeroMatchDurations(BlockMover mover)
    {
        if (mover == null)
        {
            return;
        }

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string[] fields =
        {
            "matchingTargetPause",
            "matchingTargetAnticipateDuration",
            "matchingTargetArcDuration",
            "matchingTargetSitDuration",
            "matchingTargetPulseDuration"
        };
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = typeof(BlockMover).GetField(fields[i], flags);
            if (field != null && field.FieldType == typeof(float))
            {
                field.SetValue(mover, 0f);
            }
        }
    }

    private static void RunCoroutineToEnd(System.Collections.IEnumerator routine)
    {
        if (routine == null)
        {
            return;
        }

        int guard = 0;
        while (routine.MoveNext())
        {
            guard++;
            if (guard > 100000)
            {
                Debug.LogError("Coroutine stepper exceeded guard");
                return;
            }

            // Unity runs nested IEnumerators; a manual stepper must too.
            if (routine.Current is System.Collections.IEnumerator nested)
            {
                RunCoroutineToEnd(nested);
            }
        }
    }
}
