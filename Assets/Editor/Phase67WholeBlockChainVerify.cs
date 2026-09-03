using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 67 — whole multi-cell nested block chain movement grouping.
/// Menu: Shape Nest / Phase 67 Verify Whole Block Chain Movement
/// </summary>
public static class Phase67WholeBlockChainVerify
{
    private const string ReportPath = "Captures/phase67-report.txt";
    private const string Level43Path = "Assets/Levels/Campaign_43_Reference.asset";

    [MenuItem("Shape Nest/Phase 67 Verify Whole Block Chain Movement")]
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("PHASE 67 — WHOLE BLOCK CHAIN MOVEMENT");
        report.AppendLine("=====================================");

        bool a = TestSingleCellGroup(report);
        bool b = TestTwoCellSameTranslation(report);
        bool c = TestThreeCellSameTranslation(report);
        bool d = TestTwoByTwoSameTranslation(report);
        bool e = TestMultipleBlocksStartTogether(report);
        bool f = TestMultiCellPlusMultiBlock(report);
        bool g = TestNestedOuterIdentity(report);
        bool h = TestNestedPromotionFootprint(report);
        bool i = TestInnerChainIdentity(report);
        bool j = TestDifferentTranslationsNotGrouped(report);
        bool k = TestInvalidTranslationSplit(report);
        bool l = TestLevel43(report);
        bool m = TestPhase65IdentityUnchanged(report);
        bool n = TestPhase63PartialConsume(report);

        report.AppendLine();
        report.AppendLine($"A single-cell: {(a ? "PASS" : "FAIL")}");
        report.AppendLine($"B 2-cell same translation: {(b ? "PASS" : "FAIL")}");
        report.AppendLine($"C 3-cell same translation: {(c ? "PASS" : "FAIL")}");
        report.AppendLine($"D 2x2 same translation: {(d ? "PASS" : "FAIL")}");
        report.AppendLine($"E multi-block groups: {(e ? "PASS" : "FAIL")}");
        report.AppendLine($"F multi-cell + multi-block: {(f ? "PASS" : "FAIL")}");
        report.AppendLine($"G nested outer identity: {(g ? "PASS" : "FAIL")}");
        report.AppendLine($"H nested promotion footprint: {(h ? "PASS" : "FAIL")}");
        report.AppendLine($"I inner chain identity: {(i ? "PASS" : "FAIL")}");
        report.AppendLine($"J different distances still group by block: {(j ? "PASS" : "FAIL")}");
        report.AppendLine($"K invalid translation not force-grouped: {(k ? "PASS" : "FAIL")}");
        report.AppendLine($"L Level 43 regression: {(l ? "PASS" : "FAIL")}");
        report.AppendLine($"M Phase 65 identity: {(m ? "PASS" : "FAIL")}");
        report.AppendLine($"N Phase 63 partial consume: {(n ? "PASS" : "FAIL")}");

        bool all = a && b && c && d && e && f && g && h && i && j && k && l && m && n;
        report.AppendLine();
        report.AppendLine(all ? "RESULT: PASS" : "RESULT: FAIL");
        report.AppendLine(
            "Note: Connected whole-block nest travel animation is implemented via " +
            "AlignedMovementGroup + PlayWholeBlockAlignedMatch. Play Mode Level 43 was not executed.");

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

    private static bool TestSingleCellGroup(StringBuilder report)
    {
        var actions = new List<BlockMover.AlignedMatchAction>
        {
            FakeAction(null, 0, new Vector2Int(1, 1), new Vector2Int(1, 1))
        };
        // Need a real block id — use fixture.
        using (var fx = new VerticalBlockFixture(1, ShapeType.Square, ShapeColor.Yellow))
        {
            actions.Clear();
            actions.Add(new BlockMover.AlignedMatchAction(
                fx.Block, 0, fx.Anchor, fx.Anchor));
            var groups = new List<BlockMover.AlignedMovementGroup>();
            int n = BlockMover.BuildAlignedMovementGroups(actions, groups);
            bool ok = n == 1 && groups[0].Actions.Count == 1;
            report.AppendLine($"  A: groups={n} actions={groups[0].Actions.Count}");
            return ok;
        }
    }

    private static bool TestTwoCellSameTranslation(StringBuilder report)
    {
        using (var fx = new VerticalBlockFixture(2, ShapeType.Square, ShapeColor.Yellow))
        {
            var actions = OccupyingActions(fx);
            var groups = new List<BlockMover.AlignedMovementGroup>();
            BlockMover.BuildAlignedMovementGroups(actions, groups);
            bool ok = groups.Count == 1
                && groups[0].Actions.Count == 2
                && groups[0].Translation == Vector2Int.zero;
            report.AppendLine($"  B: groups={groups.Count} cells={groups[0].Actions.Count} t={groups[0].Translation}");
            return ok;
        }
    }

    private static bool TestThreeCellSameTranslation(StringBuilder report)
    {
        using (var fx = new VerticalBlockFixture(3, ShapeType.Square, ShapeColor.Yellow))
        {
            var actions = OccupyingActions(fx);
            var groups = new List<BlockMover.AlignedMovementGroup>();
            BlockMover.BuildAlignedMovementGroups(actions, groups);
            bool ok = groups.Count == 1
                && groups[0].Actions.Count == 3
                && groups[0].Translation == Vector2Int.zero
                && groups[0].Subject == fx.Block;
            report.AppendLine($"  C: groups={groups.Count} cells={groups[0].Actions.Count}");
            return ok;
        }
    }

    private static bool TestTwoByTwoSameTranslation(StringBuilder report)
    {
        using (var fx = new FootprintFixture(
            new[]
            {
                Vector2Int.zero,
                new Vector2Int(1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(1, 1)
            },
            ShapeType.Circle,
            ShapeColor.Cyan))
        {
            var actions = OccupyingActions(fx);
            var groups = new List<BlockMover.AlignedMovementGroup>();
            BlockMover.BuildAlignedMovementGroups(actions, groups);
            bool ok = groups.Count == 1 && groups[0].Actions.Count == 4;
            report.AppendLine($"  D: groups={groups.Count} cells={groups[0].Actions.Count}");
            return ok;
        }
    }

    private static bool TestMultipleBlocksStartTogether(StringBuilder report)
    {
        using (var a = new VerticalBlockFixture(1, ShapeType.Square, ShapeColor.Yellow, new Vector2Int(0, 0)))
        using (var b = new VerticalBlockFixture(1, ShapeType.Diamond, ShapeColor.Green, new Vector2Int(2, 0)))
        using (var c = new VerticalBlockFixture(1, ShapeType.Circle, ShapeColor.Cyan, new Vector2Int(4, 0)))
        {
            var actions = new List<BlockMover.AlignedMatchAction>();
            actions.AddRange(OccupyingActions(a));
            actions.AddRange(OccupyingActions(b));
            actions.AddRange(OccupyingActions(c));
            var groups = new List<BlockMover.AlignedMovementGroup>();
            BlockMover.BuildAlignedMovementGroups(actions, groups);
            bool ok = groups.Count == 3;
            report.AppendLine($"  E: groups={groups.Count}");
            return ok;
        }
    }

    private static bool TestMultiCellPlusMultiBlock(StringBuilder report)
    {
        using (var a = new VerticalBlockFixture(3, ShapeType.Square, ShapeColor.Yellow, new Vector2Int(0, 0)))
        using (var b = new VerticalBlockFixture(2, ShapeType.Circle, ShapeColor.Cyan, new Vector2Int(2, 0)))
        using (var c = new VerticalBlockFixture(1, ShapeType.Diamond, ShapeColor.Green, new Vector2Int(4, 0)))
        {
            var actions = new List<BlockMover.AlignedMatchAction>();
            actions.AddRange(OccupyingActions(a));
            actions.AddRange(OccupyingActions(b));
            actions.AddRange(OccupyingActions(c));
            var groups = new List<BlockMover.AlignedMovementGroup>();
            BlockMover.BuildAlignedMovementGroups(actions, groups);
            bool ok = groups.Count == 3;
            int cellsA = 0, cellsB = 0, cellsC = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Subject == a.Block)
                {
                    cellsA = groups[i].Actions.Count;
                }
                else if (groups[i].Subject == b.Block)
                {
                    cellsB = groups[i].Actions.Count;
                }
                else if (groups[i].Subject == c.Block)
                {
                    cellsC = groups[i].Actions.Count;
                }
            }

            ok = ok && cellsA == 3 && cellsB == 2 && cellsC == 1;
            report.AppendLine($"  F: groups={groups.Count} A={cellsA} B={cellsB} C={cellsC}");
            return ok;
        }
    }

    private static bool TestNestedOuterIdentity(StringBuilder report)
    {
        var cell = Nested(ShapeType.Square, ShapeColor.Yellow, ShapeType.Square, ShapeColor.Purple);
        MatchIdentity outer = ShapeMatch.FromCell(cell);
        bool ok = outer.Shape == ShapeType.Square && outer.Color == ShapeColor.Yellow;
        report.AppendLine($"  G: {outer.Shape}/{outer.Color}");
        return ok;
    }

    private static bool TestNestedPromotionFootprint(StringBuilder report)
    {
        var cells = new List<ShapeCellData>
        {
            Nested(ShapeType.Square, ShapeColor.Yellow, ShapeType.Square, ShapeColor.Purple),
            NestedAt(new Vector2Int(0, 1), ShapeType.Square, ShapeColor.Yellow, ShapeType.Square, ShapeColor.Purple),
            NestedAt(new Vector2Int(0, 2), ShapeType.Square, ShapeColor.Yellow, ShapeType.Square, ShapeColor.Purple)
        };

        for (int i = 0; i < cells.Count; i++)
        {
            ShapeLayout.TryConsumeLayer(
                cells[i],
                new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
                out bool remains);
            if (!remains
                || cells[i].shapeType != ShapeType.Square
                || cells[i].outerColor != ShapeColor.Purple)
            {
                report.AppendLine($"  H: fail at {i}");
                return false;
            }
        }

        report.AppendLine("  H: 3 cells promoted purple square in place");
        return true;
    }

    private static bool TestInnerChainIdentity(StringBuilder report)
    {
        var cell = Nested(ShapeType.Circle, ShapeColor.Cyan, ShapeType.Circle, ShapeColor.Pink);
        ShapeLayout.TryConsumeLayer(cell, new MatchIdentity(ShapeType.Circle, ShapeColor.Cyan), out _);
        bool matches = ShapeMatch.AreMatchingLayers(
            ShapeMatch.FromCell(cell),
            new MatchIdentity(ShapeType.Circle, ShapeColor.Pink));
        bool rejectsCyan = !ShapeMatch.AreMatchingLayers(
            ShapeMatch.FromCell(cell),
            new MatchIdentity(ShapeType.Circle, ShapeColor.Cyan));
        report.AppendLine($"  I: pinkMatch={matches} rejectCyan={rejectsCyan}");
        return matches && rejectsCyan;
    }

    private static bool TestDifferentTranslationsNotGrouped(StringBuilder report)
    {
        // Different blocks with different travel distances still produce separate groups that
        // the wave starts together — grouping is per block, not by distance.
        using (var a = new VerticalBlockFixture(1, ShapeType.Square, ShapeColor.Yellow, new Vector2Int(0, 0)))
        using (var b = new VerticalBlockFixture(1, ShapeType.Square, ShapeColor.Purple, new Vector2Int(3, 0)))
        {
            var actions = new List<BlockMover.AlignedMatchAction>
            {
                new BlockMover.AlignedMatchAction(a.Block, 0, a.Anchor, a.Anchor + new Vector2Int(1, 0)),
                new BlockMover.AlignedMatchAction(b.Block, 0, b.Anchor, b.Anchor + new Vector2Int(3, 0))
            };
            var groups = new List<BlockMover.AlignedMovementGroup>();
            BlockMover.BuildAlignedMovementGroups(actions, groups);
            bool ok = groups.Count == 2
                && groups[0].Translation != groups[1].Translation;
            report.AppendLine($"  J: groups={groups.Count} t0={groups[0].Translation} t1={groups[1].Translation}");
            return ok;
        }
    }

    private static bool TestInvalidTranslationSplit(StringBuilder report)
    {
        using (var fx = new VerticalBlockFixture(3, ShapeType.Square, ShapeColor.Yellow))
        {
            var actions = new List<BlockMover.AlignedMatchAction>
            {
                new BlockMover.AlignedMatchAction(
                    fx.Block, 0, fx.Anchor, fx.Anchor + new Vector2Int(1, 0)),
                new BlockMover.AlignedMatchAction(
                    fx.Block, 1, fx.Anchor + new Vector2Int(0, 1), fx.Anchor + new Vector2Int(1, 1)),
                new BlockMover.AlignedMatchAction(
                    fx.Block, 2, fx.Anchor + new Vector2Int(0, 2), fx.Anchor + new Vector2Int(2, 2))
            };
            var groups = new List<BlockMover.AlignedMovementGroup>();
            BlockMover.BuildAlignedMovementGroups(actions, groups);
            // First two share +1x; third is +2x — only largest consistent subset.
            bool ok = groups.Count == 1
                && groups[0].Actions.Count == 2
                && groups[0].Translation == new Vector2Int(1, 0);
            report.AppendLine(
                $"  K: groups={groups.Count} cells={groups[0].Actions.Count} t={groups[0].Translation}");
            return ok;
        }
    }

    private static bool TestLevel43(StringBuilder report)
    {
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(Level43Path);
        if (level?.blocks == null)
        {
            report.AppendLine("  L: missing Level 43");
            return false;
        }

        bool yellow3 = HasVerticalNested(level, ShapeType.Square, ShapeColor.Yellow, ShapeColor.Purple, 3);
        bool cyan = HasVerticalNested(level, ShapeType.Circle, ShapeColor.Cyan, ShapeColor.Pink, 2)
            || HasVerticalNested(level, ShapeType.Circle, ShapeColor.Cyan, ShapeColor.Pink, 3);
        bool green = HasVerticalNested(level, ShapeType.Diamond, ShapeColor.Green, ShapeColor.Red, 2)
            || HasVerticalNested(level, ShapeType.Diamond, ShapeColor.Green, ShapeColor.Red, 3);
        bool ok = yellow3 && cyan && green;
        report.AppendLine($"  L: yellow3={yellow3} cyanPink={cyan} greenRed={green}");
        return ok;
    }

    private static bool TestPhase65IdentityUnchanged(StringBuilder report)
    {
        bool ok = ShapeMatch.AreMatchingLayers(
                new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
                new MatchIdentity(ShapeType.Square, ShapeColor.Yellow))
            && !ShapeMatch.AreMatchingLayers(
                new MatchIdentity(ShapeType.Square, ShapeColor.Yellow),
                new MatchIdentity(ShapeType.Square, ShapeColor.Purple));
        report.AppendLine($"  M: {ok}");
        return ok;
    }

    private static bool TestPhase63PartialConsume(StringBuilder report)
    {
        Target target = CreateTarget(
            new Vector2Int(1, 1),
            new[]
            {
                Colored(Vector2Int.zero, ShapeType.Triangle, ShapeColor.Pink),
                Colored(new Vector2Int(1, 0), ShapeType.Triangle, ShapeColor.Pink),
                Colored(new Vector2Int(0, 1), ShapeType.Triangle, ShapeColor.Pink)
            });
        try
        {
            bool consumed = target.TryConsumeLayerAtWorld(
                new Vector2Int(2, 1),
                new MatchIdentity(ShapeType.Triangle, ShapeColor.Pink),
                out bool complete);
            bool ok = consumed && !complete && target.CellCount == 2;
            report.AppendLine($"  N: consumed={consumed} complete={complete} cells={target.CellCount}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(target.gameObject);
        }
    }

    private static List<BlockMover.AlignedMatchAction> OccupyingActions(IBlockFixture fx)
    {
        var list = new List<BlockMover.AlignedMatchAction>();
        for (int i = 0; i < fx.Block.CellCount; i++)
        {
            Vector2Int world = fx.Block.GridPosition + fx.Block.GetLocalCell(i);
            list.Add(new BlockMover.AlignedMatchAction(fx.Block, i, world, world));
        }

        return list;
    }

    private static BlockMover.AlignedMatchAction FakeAction(
        Block subject,
        int cellIndex,
        Vector2Int world,
        Vector2Int nest)
    {
        return new BlockMover.AlignedMatchAction(subject, cellIndex, world, nest);
    }

    private interface IBlockFixture : System.IDisposable
    {
        Block Block { get; }
        Vector2Int Anchor { get; }
    }

    private sealed class VerticalBlockFixture : IBlockFixture
    {
        public Block Block { get; }
        public Vector2Int Anchor { get; }
        private readonly GameObject go;

        public VerticalBlockFixture(
            int height,
            ShapeType shape,
            ShapeColor color,
            Vector2Int? anchor = null)
        {
            Anchor = anchor ?? new Vector2Int(1, 1);
            go = new GameObject($"Phase67_V{height}", typeof(RectTransform), typeof(Block), typeof(BlockMover));
            Block = go.GetComponent<Block>();
            var cells = new List<ShapeCellData>();
            for (int i = 0; i < height; i++)
            {
                cells.Add(Colored(new Vector2Int(0, i), shape, color));
            }

            Block.ApplyLayout(shape, cells, PieceComposition.Simple, shape);
            Block.Initialize(null, Anchor);
        }

        public void Dispose()
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    private sealed class FootprintFixture : IBlockFixture
    {
        public Block Block { get; }
        public Vector2Int Anchor { get; }
        private readonly GameObject go;

        public FootprintFixture(Vector2Int[] locals, ShapeType shape, ShapeColor color)
        {
            Anchor = new Vector2Int(2, 2);
            go = new GameObject("Phase67_Foot", typeof(RectTransform), typeof(Block), typeof(BlockMover));
            Block = go.GetComponent<Block>();
            var cells = new List<ShapeCellData>();
            for (int i = 0; i < locals.Length; i++)
            {
                cells.Add(Colored(locals[i], shape, color));
            }

            Block.ApplyLayout(shape, cells, PieceComposition.Simple, shape);
            Block.Initialize(null, Anchor);
        }

        public void Dispose()
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    private static bool HasVerticalNested(
        LevelData level,
        ShapeType shape,
        ShapeColor outer,
        ShapeColor inner,
        int minCells)
    {
        for (int i = 0; i < level.blocks.Count; i++)
        {
            LevelBlockData block = level.blocks[i];
            if (block?.cells == null || block.cells.Count < minCells)
            {
                continue;
            }

            int nested = 0;
            for (int c = 0; c < block.cells.Count; c++)
            {
                ShapeCellData cell = block.cells[c];
                if (cell == null
                    || cell.shapeType != shape
                    || cell.outerColor != outer
                    || cell.innerShapes == null
                    || cell.innerShapes.Count == 0
                    || cell.innerShapes[0] != shape
                    || cell.innerShapeColors == null
                    || cell.innerShapeColors.Count == 0
                    || cell.innerShapeColors[0] != inner)
                {
                    continue;
                }

                nested++;
            }

            if (nested >= minCells)
            {
                return true;
            }
        }

        return false;
    }

    private static ShapeCellData Nested(
        ShapeType outer,
        ShapeColor outerColor,
        ShapeType inner,
        ShapeColor innerColor)
    {
        return NestedAt(Vector2Int.zero, outer, outerColor, inner, innerColor);
    }

    private static ShapeCellData NestedAt(
        Vector2Int local,
        ShapeType outer,
        ShapeColor outerColor,
        ShapeType inner,
        ShapeColor innerColor)
    {
        return new ShapeCellData
        {
            localPosition = local,
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
        var go = new GameObject("Phase67_Target");
        go.AddComponent<RectTransform>();
        go.AddComponent<UIPieceView>();
        Target target = go.AddComponent<Target>();
        target.ApplyLayout(cells[0].shapeType, new List<ShapeCellData>(cells), PieceComposition.Simple, cells[0].shapeType);
        target.Initialize(null, anchor);
        return target;
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
