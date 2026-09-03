using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 66 — nested chain matching + synchronized match-wave collection.
/// Menu: Shape Nest / Phase 66 Verify Nested Chain + Synchronized Match
/// </summary>
public static class Phase66NestedChainSyncVerify
{
    private const string ReportPath = "Captures/phase66-report.txt";
    private const string Level43Path = "Assets/Levels/Campaign_43_Reference.asset";

    [MenuItem("Shape Nest/Phase 66 Verify Nested Chain + Synchronized Match")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 66 — NESTED CHAIN + SYNCHRONIZED MATCH WAVE");
        report.AppendLine("=================================================");

        bool t1 = TestSameShapeSameColor(report);
        bool t2 = TestSameShapeDifferentColor(report);
        bool t3 = TestOuterNestedMatchesFirst(report);
        bool t4 = TestInnerActiveAfterOuterConsume(report);
        bool t5 = TestInnerPreservesShapeAndColor(report);
        bool t6 = TestInnerCanMatchAfterPromotion(report);
        bool t7 = TestMultiCellPartialConsume(report);
        bool t8 = TestMatchWaveCollectsMultiple(report);
        bool t9 = TestMatchWaveOwnTargets(report);
        bool t10 = TestWaveIgnoresTravelDistanceOrdering(report);
        bool t11 = TestWaveDedupesBlockAndNestCell(report);
        bool t12 = TestNoDuplicateNestedPromotion(report);
        bool t13 = TestLevel43IdentityRegression(report);

        report.AppendLine();
        report.AppendLine($"1 same shape+color: {(t1 ? "PASS" : "FAIL")}");
        report.AppendLine($"2 same shape different color: {(t2 ? "PASS" : "FAIL")}");
        report.AppendLine($"3 outer nested first: {(t3 ? "PASS" : "FAIL")}");
        report.AppendLine($"4 inner active after outer: {(t4 ? "PASS" : "FAIL")}");
        report.AppendLine($"5 inner shape+color: {(t5 ? "PASS" : "FAIL")}");
        report.AppendLine($"6 inner matchable after promote: {(t6 ? "PASS" : "FAIL")}");
        report.AppendLine($"7 multi-cell partial: {(t7 ? "PASS" : "FAIL")}");
        report.AppendLine($"8 match wave collects multiple: {(t8 ? "PASS" : "FAIL")}");
        report.AppendLine($"9 wave members keep own targets: {(t9 ? "PASS" : "FAIL")}");
        report.AppendLine($"10 distance does not gate wave membership: {(t10 ? "PASS" : "FAIL")}");
        report.AppendLine($"11 dedupe block + nest cell: {(t11 ? "PASS" : "FAIL")}");
        report.AppendLine($"12 no duplicate nested promote: {(t12 ? "PASS" : "FAIL")}");
        report.AppendLine($"13 Level 43 identity regression: {(t13 ? "PASS" : "FAIL")}");

        bool all = t1 && t2 && t3 && t4 && t5 && t6 && t7 && t8 && t9 && t10 && t11 && t12 && t13;
        report.AppendLine();
        report.AppendLine(all ? "RESULT: PASS" : "RESULT: FAIL");
        report.AppendLine(
            "Note: Synchronized START of match-wave movement is implemented in LevelManager " +
            "(parallel PlayResolvedAutoMatch). Play Mode concurrent travel was not executed by this editor check.");

        WriteReport(report.ToString());
        if (all)
        {
            Debug.Log(report.ToString());
        }
        else
        {
            Debug.LogError(report.ToString());
        }
    }

    private static bool TestSameShapeSameColor(StringBuilder report)
    {
        bool ok = ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Circle, ShapeColor.Cyan),
            new MatchIdentity(ShapeType.Circle, ShapeColor.Cyan));
        report.AppendLine($"  T1: {ok}");
        return ok;
    }

    private static bool TestSameShapeDifferentColor(StringBuilder report)
    {
        bool ok = !ShapeMatch.AreMatchingLayers(
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
            new MatchIdentity(ShapeType.Square, ShapeColor.Purple));
        report.AppendLine($"  T2: {ok}");
        return ok;
    }

    private static bool TestOuterNestedMatchesFirst(StringBuilder report)
    {
        var cell = NestedCell(ShapeType.Circle, ShapeColor.Cyan, ShapeType.Triangle, ShapeColor.Pink);
        bool innerReject = !ShapeLayout.TryConsumeLayer(
            cell,
            new MatchIdentity(ShapeType.Triangle, ShapeColor.Pink),
            out _);
        bool outerOk = ShapeLayout.TryConsumeLayer(
            cell,
            new MatchIdentity(ShapeType.Circle, ShapeColor.Cyan),
            out bool remains);
        bool ok = innerReject && outerOk && remains;
        report.AppendLine($"  T3: innerReject={innerReject} outerOk={outerOk} remains={remains}");
        return ok;
    }

    private static bool TestInnerActiveAfterOuterConsume(StringBuilder report)
    {
        var cell = NestedCell(ShapeType.Circle, ShapeColor.Cyan, ShapeType.Triangle, ShapeColor.Pink);
        ShapeLayout.TryConsumeLayer(cell, new MatchIdentity(ShapeType.Circle, ShapeColor.Cyan), out bool remains);
        bool ok = remains
            && cell.shapeType == ShapeType.Triangle
            && (cell.innerShapes == null || cell.innerShapes.Count == 0);
        report.AppendLine($"  T4: remains={remains} active={cell.shapeType}");
        return ok;
    }

    private static bool TestInnerPreservesShapeAndColor(StringBuilder report)
    {
        var cell = NestedCell(ShapeType.Diamond, ShapeColor.Green, ShapeType.Diamond, ShapeColor.Red);
        ShapeLayout.TryConsumeLayer(cell, new MatchIdentity(ShapeType.Diamond, ShapeColor.Green), out _);
        MatchIdentity id = ShapeMatch.FromCell(cell);
        bool ok = id.Shape == ShapeType.Diamond && id.Color == ShapeColor.Red;
        report.AppendLine($"  T5: shape={id.Shape} color={id.Color}");
        return ok;
    }

    private static bool TestInnerCanMatchAfterPromotion(StringBuilder report)
    {
        var cell = NestedCell(ShapeType.Pentagon, ShapeColor.Orange, ShapeType.Circle, ShapeColor.Cyan);
        ShapeLayout.TryConsumeLayer(cell, new MatchIdentity(ShapeType.Pentagon, ShapeColor.Orange), out _);
        bool matches = ShapeMatch.AreMatchingLayers(
            ShapeMatch.FromCell(cell),
            new MatchIdentity(ShapeType.Circle, ShapeColor.Cyan));
        bool rejectsOld = !ShapeMatch.AreMatchingLayers(
            ShapeMatch.FromCell(cell),
            new MatchIdentity(ShapeType.Pentagon, ShapeColor.Orange));
        report.AppendLine($"  T6: matches={matches} rejectsOld={rejectsOld}");
        return matches && rejectsOld;
    }

    private static bool TestMultiCellPartialConsume(StringBuilder report)
    {
        Target target = CreateTarget(
            new Vector2Int(2, 2),
            new[]
            {
                Colored(Vector2Int.zero, ShapeType.Triangle, ShapeColor.Pink),
                Colored(new Vector2Int(1, 0), ShapeType.Triangle, ShapeColor.Pink),
                Colored(new Vector2Int(0, 1), ShapeType.Triangle, ShapeColor.Pink)
            });
        try
        {
            bool consumed = target.TryConsumeLayerAtWorld(
                new Vector2Int(3, 2),
                new MatchIdentity(ShapeType.Triangle, ShapeColor.Pink),
                out bool complete);
            bool ok = consumed
                && !complete
                && target.CellCount == 2
                && target.FindCellIndexAtWorld(new Vector2Int(3, 2)) < 0
                && target.FindCellIndexAtWorld(new Vector2Int(2, 2)) >= 0
                && target.FindCellIndexAtWorld(new Vector2Int(2, 3)) >= 0;
            report.AppendLine($"  T7: consumed={consumed} complete={complete} cells={target.CellCount}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(target.gameObject);
        }
    }

    private static bool TestMatchWaveCollectsMultiple(StringBuilder report)
    {
        using (var fixture = new OccupyingWaveFixture())
        {
            var wave = new List<BlockMover.AlignedMatchWaveMember>();
            int count = BlockMover.CollectAlignedMatchWave(
                fixture.Board,
                fixture.Scratch,
                null,
                hasLastMatch: false,
                lastMatchOrigin: Vector2Int.zero,
                lastMatchTargetCell: Vector2Int.zero,
                wave);
            bool ok = count == 3 && wave.Count == 3;
            report.AppendLine($"  T8: waveCount={count}");
            return ok;
        }
    }

    private static bool TestMatchWaveOwnTargets(StringBuilder report)
    {
        using (var fixture = new OccupyingWaveFixture())
        {
            var wave = new List<BlockMover.AlignedMatchWaveMember>();
            BlockMover.CollectAlignedMatchWave(
                fixture.Board,
                fixture.Scratch,
                null,
                false,
                Vector2Int.zero,
                Vector2Int.zero,
                wave);

            var nests = new HashSet<Vector2Int>();
            bool distinct = true;
            for (int i = 0; i < wave.Count; i++)
            {
                if (!nests.Add(wave[i].NestTo))
                {
                    distinct = false;
                }
            }

            bool expected =
                nests.Contains(fixture.CellA)
                && nests.Contains(fixture.CellB)
                && nests.Contains(fixture.CellC);
            report.AppendLine($"  T9: distinct={distinct} expectedCells={expected} nests={wave.Count}");
            return distinct && expected && wave.Count == 3;
        }
    }

    private static bool TestWaveIgnoresTravelDistanceOrdering(StringBuilder report)
    {
        // Occupying matches have zero travel distance; membership must still include all three
        // (distance must not gate collection — sequential play was the old bug).
        using (var fixture = new OccupyingWaveFixture())
        {
            var wave = new List<BlockMover.AlignedMatchWaveMember>();
            int count = BlockMover.CollectAlignedMatchWave(
                fixture.Board,
                fixture.Scratch,
                null,
                false,
                Vector2Int.zero,
                Vector2Int.zero,
                wave);
            bool ok = count == 3;
            report.AppendLine($"  T10: allReadyTogether={ok}");
            return ok;
        }
    }

    private static bool TestWaveDedupesBlockAndNestCell(StringBuilder report)
    {
        using (var fixture = new OccupyingWaveFixture())
        {
            var wave = new List<BlockMover.AlignedMatchWaveMember>();
            BlockMover.CollectAlignedMatchWave(
                fixture.Board,
                fixture.Scratch,
                null,
                false,
                Vector2Int.zero,
                Vector2Int.zero,
                wave);

            var blocks = new HashSet<int>();
            var nests = new HashSet<Vector2Int>();
            bool unique = true;
            for (int i = 0; i < wave.Count; i++)
            {
                if (wave[i].Subject == null)
                {
                    unique = false;
                    break;
                }

                if (!blocks.Add(wave[i].Subject.GetInstanceID()) || !nests.Add(wave[i].NestTo))
                {
                    unique = false;
                    break;
                }
            }

            report.AppendLine($"  T11: uniqueBlocksAndNests={unique} count={wave.Count}");
            return unique && wave.Count == 3;
        }
    }

    private static bool TestNoDuplicateNestedPromotion(StringBuilder report)
    {
        var cell = NestedCell(ShapeType.Square, ShapeColor.Yellow, ShapeType.Square, ShapeColor.Purple);
        bool first = ShapeLayout.TryConsumeLayer(
            cell,
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
            out bool remains);
        bool second = ShapeLayout.TryConsumeLayer(
            cell,
            new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
            out _);
        bool ok = first && remains && !second && cell.outerColor == ShapeColor.Purple;
        report.AppendLine($"  T12: first={first} secondReject={(!second)} color={cell.outerColor}");
        return ok;
    }

    private static bool TestLevel43IdentityRegression(StringBuilder report)
    {
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(Level43Path);
        if (level?.blocks == null || level.targets == null)
        {
            report.AppendLine("  T13: missing Level 43");
            return false;
        }

        bool yellow = HasBlock(level, ShapeType.Square, ShapeColor.Yellow);
        bool purple = HasNestedInner(level, ShapeType.Square, ShapeColor.Yellow, ShapeType.Square, ShapeColor.Purple);
        bool green = HasBlock(level, ShapeType.Diamond, ShapeColor.Green);
        bool red = HasNestedInner(level, ShapeType.Diamond, ShapeColor.Green, ShapeType.Diamond, ShapeColor.Red);
        bool cyan = HasBlock(level, ShapeType.Circle, ShapeColor.Cyan);
        bool pink = HasNestedInner(level, ShapeType.Circle, ShapeColor.Cyan, ShapeType.Circle, ShapeColor.Pink);
        bool orange = HasBlock(level, ShapeType.Pentagon, ShapeColor.Orange);
        bool cyanPent = HasNestedInner(
            level,
            ShapeType.Pentagon,
            ShapeColor.Orange,
            ShapeType.Pentagon,
            ShapeColor.Cyan);
        bool ok = yellow && purple && green && red && cyan && pink && orange && cyanPent;
        report.AppendLine(
            $"  T13: Y={yellow} Pu={purple} G={green} R={red} C={cyan} Pi={pink} O={orange} CP={cyanPent}");
        return ok;
    }

    private sealed class OccupyingWaveFixture : System.IDisposable
    {
        public BoardManager Board { get; }
        public List<Block> Scratch { get; } = new List<Block>();
        public Vector2Int CellA { get; } = new Vector2Int(1, 1);
        public Vector2Int CellB { get; } = new Vector2Int(3, 1);
        public Vector2Int CellC { get; } = new Vector2Int(5, 1);

        private readonly List<Object> owned = new List<Object>();

        public OccupyingWaveFixture()
        {
            var boardGo = new GameObject("Phase66_Board", typeof(RectTransform), typeof(BoardManager));
            owned.Add(boardGo);
            Board = boardGo.GetComponent<BoardManager>();
            Board.ApplyGridSize(8, 6);

            SpawnOccupying(CellA, ShapeType.Circle, ShapeColor.Cyan);
            SpawnOccupying(CellB, ShapeType.Square, ShapeColor.Yellow);
            SpawnOccupying(CellC, ShapeType.Diamond, ShapeColor.Green);
        }

        private void SpawnOccupying(Vector2Int cell, ShapeType shape, ShapeColor color)
        {
            var blockGo = new GameObject(
                $"Phase66_Block_{shape}_{color}",
                typeof(RectTransform),
                typeof(Block),
                typeof(BlockMover));
            owned.Add(blockGo);
            Block block = blockGo.GetComponent<Block>();
            block.ApplyLayout(
                shape,
                new List<ShapeCellData> { Colored(Vector2Int.zero, shape, color) },
                PieceComposition.Simple,
                shape);
            block.Initialize(Board, cell);

            var targetGo = new GameObject(
                $"Phase66_Target_{shape}_{color}",
                typeof(RectTransform),
                typeof(Target));
            owned.Add(targetGo);
            Target target = targetGo.GetComponent<Target>();
            target.ApplyLayout(
                shape,
                new List<ShapeCellData> { Colored(Vector2Int.zero, shape, color) },
                PieceComposition.Simple,
                shape);
            target.Initialize(Board, cell);
        }

        public void Dispose()
        {
            for (int i = 0; i < owned.Count; i++)
            {
                if (owned[i] != null)
                {
                    Object.DestroyImmediate(owned[i]);
                }
            }
        }
    }

    private static ShapeCellData NestedCell(
        ShapeType outer,
        ShapeColor outerColor,
        ShapeType inner,
        ShapeColor innerColor)
    {
        return new ShapeCellData
        {
            localPosition = Vector2Int.zero,
            shapeType = outer,
            outerColor = outerColor,
            innerShapes = new List<ShapeType> { inner },
            innerShapeColors = new List<ShapeColor> { innerColor }
        };
    }

    private static ShapeCellData Colored(Vector2Int local, ShapeType shape, ShapeColor color)
    {
        return new ShapeCellData
        {
            localPosition = local,
            shapeType = shape,
            outerColor = color,
            innerShapes = new List<ShapeType>(),
            innerShapeColors = new List<ShapeColor>()
        };
    }

    private static Target CreateTarget(Vector2Int anchor, ShapeCellData[] cells)
    {
        var go = new GameObject("Phase66_Target");
        go.AddComponent<RectTransform>();
        go.AddComponent<UIPieceView>();
        Target target = go.AddComponent<Target>();
        var list = new List<ShapeCellData>(cells);
        target.ApplyLayout(cells[0].shapeType, list, PieceComposition.Simple, cells[0].shapeType);
        target.Initialize(null, anchor);
        return target;
    }

    private static bool HasBlock(LevelData level, ShapeType shape, ShapeColor color)
    {
        for (int i = 0; i < level.blocks.Count; i++)
        {
            LevelBlockData block = level.blocks[i];
            if (block?.cells == null)
            {
                continue;
            }

            for (int c = 0; c < block.cells.Count; c++)
            {
                ShapeCellData cell = block.cells[c];
                if (cell != null && cell.shapeType == shape && cell.outerColor == color)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasNestedInner(
        LevelData level,
        ShapeType outer,
        ShapeColor outerColor,
        ShapeType inner,
        ShapeColor innerColor)
    {
        for (int i = 0; i < level.blocks.Count; i++)
        {
            LevelBlockData block = level.blocks[i];
            if (block?.cells == null)
            {
                continue;
            }

            for (int c = 0; c < block.cells.Count; c++)
            {
                ShapeCellData cell = block.cells[c];
                if (cell == null
                    || cell.shapeType != outer
                    || cell.outerColor != outerColor
                    || cell.innerShapes == null
                    || cell.innerShapes.Count == 0
                    || cell.innerShapes[0] != inner
                    || cell.innerShapeColors == null
                    || cell.innerShapeColors.Count == 0
                    || cell.innerShapeColors[0] != innerColor)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static void WriteReport(string text)
    {
        string dir = Path.GetDirectoryName(ReportPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(ReportPath, text);
        AssetDatabase.Refresh();
    }
}
