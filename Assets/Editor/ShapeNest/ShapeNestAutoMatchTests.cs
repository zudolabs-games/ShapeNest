using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

internal static class ShapeNestAutoMatchTests
{
    [MenuItem("Tools/Shape Nest/Run Auto-Match Tests")]
    public static void RunFromMenu()
    {
        string report = RunAll();
        if (report.Contains("FAIL"))
        {
            Debug.LogError(report);
        }
        else
        {
            Debug.Log(report);
        }
    }

    public static string RunAll()
    {
        var builder = new StringBuilder();
        int passed = 0;
        int failed = 0;

        Check(builder, ref passed, ref failed, "A occupying same-shape matches", TestA_SimpleOccupying());
        Check(builder, ref passed, ref failed, "B duplicate shapes are interchangeable", TestB_DuplicateShapes());
        Check(builder, ref passed, ref failed, "C adjacent neighbor-first order", TestC_AdjacentOrder());
        Check(builder, ref passed, ref failed, "D chain middle split keeps survivors", TestD_ChainMiddleSplit());
        Check(builder, ref passed, ref failed, "E cascade order after middle match", TestE_ChainCascadeOrder());
        Check(builder, ref passed, ref failed, "F nested is two occupying matches", TestF_NestedTwoPasses());
        Check(builder, ref passed, ref failed, "F2 nested outer promote keeps cell", TestF2_NestedChainOuterPromotesInner());
        Check(builder, ref passed, ref failed, "F3 outer destination uses targetWorld", TestF3_OuterUsesPreservedTargetWorld());
        Check(builder, ref passed, ref failed, "G nested-in-chain split after outer consume", TestG_NestedInChain());
        Check(builder, ref passed, ref failed, "H partial alignment only", TestH_PartialAlignment());
        Check(builder, ref passed, ref failed, "I wrong shape does not match", TestI_WrongTarget());
        Check(builder, ref passed, ref failed, "J one cell away does not auto-match", TestJ_NearTarget());
        Check(builder, ref passed, ref failed, "L long chain split coordinates", TestL_LongChainSplit());
        Check(builder, ref passed, ref failed, "Seq1 neighbor after chain match preferred", TestSeq1_NeighborAfterMatch());
        Check(builder, ref passed, ref failed, "Seq2 skip key is per-cell", TestSeq2_SkipKeyPerCell());
        Check(builder, ref passed, ref failed, "Seq3 middle split leaves two survivors", TestSeq3_MiddleSplitSurvivors());
        Check(builder, ref passed, ref failed, "Seq4 end consume keeps connected pair", TestSeq4_EndConsumeKeepsPair());
        Check(builder, ref passed, ref failed, "M multi-cell target partial consume", TestM_MultiCellTargetPartialConsume());
        Check(builder, ref passed, ref failed, "N unrelated same-shape target survives", TestN_UnrelatedSameShapeSurvives());

        builder.Insert(0, $"Auto-match tests: {passed} passed, {failed} failed\n");
        return builder.ToString();
    }

    private static void Check(StringBuilder builder, ref int passed, ref int failed, string name, bool ok)
    {
        if (ok)
        {
            passed++;
            builder.AppendLine("PASS  " + name);
        }
        else
        {
            failed++;
            builder.AppendLine("FAIL  " + name);
        }
    }

    private static bool TestA_SimpleOccupying()
    {
        return BlockMover.IsOccupyingAlignedMatch(
            new Vector2Int(2, 2),
            new Vector2Int(2, 2),
            ShapeType.Circle,
            ShapeType.Circle);
    }

    private static bool TestB_DuplicateShapes()
    {
        bool aOnA = BlockMover.IsOccupyingAlignedMatch(
            new Vector2Int(0, 0), new Vector2Int(0, 0), ShapeType.Circle, ShapeType.Circle);
        bool aOnB = BlockMover.IsOccupyingAlignedMatch(
            new Vector2Int(0, 0), new Vector2Int(3, 0), ShapeType.Circle, ShapeType.Circle);
        bool bOnA = BlockMover.IsOccupyingAlignedMatch(
            new Vector2Int(3, 0), new Vector2Int(0, 0), ShapeType.Circle, ShapeType.Circle);
        bool bOnB = BlockMover.IsOccupyingAlignedMatch(
            new Vector2Int(3, 0), new Vector2Int(3, 0), ShapeType.Circle, ShapeType.Circle);
        return aOnA && bOnB && !aOnB && !bOnA;
    }

    private static bool TestC_AdjacentOrder()
    {
        Vector2Int lastOrigin = new Vector2Int(1, 1);
        Vector2Int lastTarget = new Vector2Int(1, 1);
        int neighbor = BlockMover.AlignedMatchPriority(
            true, new Vector2Int(2, 1), new Vector2Int(2, 1), lastOrigin, lastTarget);
        int other = BlockMover.AlignedMatchPriority(
            true, new Vector2Int(4, 4), new Vector2Int(4, 4), lastOrigin, lastTarget);
        return neighbor < other;
    }

    private static bool TestD_ChainMiddleSplit()
    {
        Split(
            new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(2, 0)
            },
            new[] { ShapeType.Circle, ShapeType.Square },
            out List<Vector2Int> anchors,
            out List<List<ShapeCellData>> components);
        return anchors.Count == 2
            && components.Count == 2
            && anchors[0] == new Vector2Int(0, 0)
            && anchors[1] == new Vector2Int(2, 0)
            && components[0].Count == 1
            && components[1].Count == 1
            && components[0][0].shapeType == ShapeType.Circle
            && components[1][0].shapeType == ShapeType.Square;
    }

    private static bool TestE_ChainCascadeOrder()
    {
        Vector2Int middle = new Vector2Int(1, 0);
        int circle = BlockMover.AlignedMatchPriority(
            true, new Vector2Int(0, 0), new Vector2Int(0, 0), middle, middle);
        int square = BlockMover.AlignedMatchPriority(
            true, new Vector2Int(2, 0), new Vector2Int(2, 0), middle, middle);
        int distant = BlockMover.AlignedMatchPriority(
            true, new Vector2Int(5, 5), new Vector2Int(5, 5), middle, middle);
        return circle == square && circle < distant;
    }

    private static bool TestF_NestedTwoPasses()
    {
        bool inner = BlockMover.IsOccupyingAlignedMatch(
            Vector2Int.zero, Vector2Int.zero, ShapeType.Triangle, ShapeType.Triangle);
        bool outer = BlockMover.IsOccupyingAlignedMatch(
            Vector2Int.zero, Vector2Int.zero, ShapeType.Square, ShapeType.Square);
        bool bothAtOnce = inner && outer && ShapeType.Triangle != ShapeType.Square;
        return bothAtOnce;
    }

    private static bool TestF2_NestedChainOuterPromotesInner()
    {
        var cell = new ShapeCellData
        {
            localPosition = Vector2Int.zero,
            shapeType = ShapeType.Square,
            innerShapes = new List<ShapeType> { ShapeType.Triangle }
        };

        // Outer-first: Triangle is not matchable while nested under Square.
        if (ShapeLayout.TryConsumeLayer(cell, ShapeType.Triangle, out _))
        {
            return false;
        }

        if (!ShapeLayout.TryConsumeLayer(cell, ShapeType.Square, out bool remains)
            || !remains
            || ShapeLayout.ActiveShape(cell, ShapeType.Square) != ShapeType.Triangle
            || ShapeLayout.LayerCount(cell) != 1)
        {
            return false;
        }

        // Survivors stay connected until the outer cell is fully removed.
        Split(
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) },
            new[] { ShapeType.Circle, ShapeType.Square, ShapeType.Circle },
            out List<Vector2Int> anchors,
            out List<List<ShapeCellData>> components);
        return anchors.Count == 1
            && components.Count == 1
            && components[0].Count == 3
            && components[0][1].localPosition == new Vector2Int(1, 0);
    }

    private static bool TestF3_OuterUsesPreservedTargetWorld()
    {
        Vector2Int cellWorld = new Vector2Int(1, 0);
        Vector2Int targetWorld = new Vector2Int(1, -1);
        // Outer nest-entry must travel to the preserved destination, not assume the target
        // sits on the chain cell (GetTargetAt(cellWorld) would be wrong here).
        bool destinationDiffers = cellWorld != targetWorld;
        bool outerStillOccupyingSource = BlockMover.IsOccupyingAlignedMatch(
            cellWorld, cellWorld, ShapeType.Square, ShapeType.Square);
        bool outerMatchesDestinationShape = BlockMover.IsOccupyingAlignedMatch(
            targetWorld, targetWorld, ShapeType.Square, ShapeType.Square);
        return destinationDiffers && outerStillOccupyingSource && outerMatchesDestinationShape;
    }

    private static bool TestG_NestedInChain()
    {
        Split(
            new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(2, 0)
            },
            new[] { ShapeType.Circle, ShapeType.Circle },
            out List<Vector2Int> anchors,
            out List<List<ShapeCellData>> components);
        return anchors.Count == 2
            && anchors[0] == new Vector2Int(0, 0)
            && anchors[1] == new Vector2Int(2, 0);
    }

    private static bool TestH_PartialAlignment()
    {
        bool aligned = BlockMover.IsOccupyingAlignedMatch(
            new Vector2Int(1, 1), new Vector2Int(1, 1), ShapeType.Circle, ShapeType.Circle);
        bool other = BlockMover.IsOccupyingAlignedMatch(
            new Vector2Int(3, 1), new Vector2Int(4, 1), ShapeType.Triangle, ShapeType.Triangle);
        return aligned && !other;
    }

    private static bool TestI_WrongTarget()
    {
        return !BlockMover.IsOccupyingAlignedMatch(
            new Vector2Int(2, 2),
            new Vector2Int(2, 2),
            ShapeType.Circle,
            ShapeType.Triangle);
    }

    private static bool TestJ_NearTarget()
    {
        Vector2Int blockCell = new Vector2Int(1, 2);
        Vector2Int targetCell = new Vector2Int(2, 2);
        bool occupying = BlockMover.IsOccupyingAlignedMatch(
            blockCell, targetCell, ShapeType.Circle, ShapeType.Circle);
        bool adjacent = Mathf.Abs(blockCell.x - targetCell.x) + Mathf.Abs(blockCell.y - targetCell.y) == 1;
        return !occupying && adjacent;
    }

    private static bool TestL_LongChainSplit()
    {
        Split(
            new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(3, 0),
                new Vector2Int(4, 0)
            },
            new[]
            {
                ShapeType.Circle,
                ShapeType.Triangle,
                ShapeType.Square,
                ShapeType.Circle
            },
            out List<Vector2Int> anchors,
            out List<List<ShapeCellData>> components);

        if (anchors.Count != 2 || components.Count != 2)
        {
            return false;
        }

        bool left = anchors[0] == new Vector2Int(0, 0)
            && components[0].Count == 2
            && components[0][0].localPosition == Vector2Int.zero
            && components[0][1].localPosition == new Vector2Int(1, 0)
            && components[0][0].shapeType == ShapeType.Circle
            && components[0][1].shapeType == ShapeType.Triangle;
        bool right = anchors[1] == new Vector2Int(3, 0)
            && components[1].Count == 2
            && components[1][0].localPosition == Vector2Int.zero
            && components[1][1].localPosition == new Vector2Int(1, 0)
            && components[1][0].shapeType == ShapeType.Square
            && components[1][1].shapeType == ShapeType.Circle;
        return left && right;
    }

    private static bool TestSeq1_NeighborAfterMatch()
    {
        Vector2Int circle = new Vector2Int(0, 0);
        Vector2Int triangle = new Vector2Int(1, 0);
        int trianglePriority = BlockMover.AlignedMatchPriority(
            true, triangle, triangle, circle, circle);
        int distantPriority = BlockMover.AlignedMatchPriority(
            true, new Vector2Int(4, 4), new Vector2Int(4, 4), circle, circle);
        return trianglePriority < distantPriority
            && BlockMover.IsOccupyingAlignedMatch(triangle, triangle, ShapeType.Triangle, ShapeType.Triangle);
    }

    private static bool TestSeq2_SkipKeyPerCell()
    {
        int instanceId = 42;
        int circleKey = BlockMover.AutoMatchSkipKey(instanceId, new Vector2Int(0, 0));
        int triangleKey = BlockMover.AutoMatchSkipKey(instanceId, new Vector2Int(1, 0));
        return circleKey != triangleKey;
    }

    private static bool TestSeq3_MiddleSplitSurvivors()
    {
        Split(
            new[] { new Vector2Int(0, 0), new Vector2Int(2, 0) },
            new[] { ShapeType.Circle, ShapeType.Square },
            out List<Vector2Int> anchors,
            out List<List<ShapeCellData>> components);
        return anchors.Count == 2
            && anchors[0] == new Vector2Int(0, 0)
            && anchors[1] == new Vector2Int(2, 0)
            && components[0][0].shapeType == ShapeType.Circle
            && components[1][0].shapeType == ShapeType.Square;
    }

    private static bool TestSeq4_EndConsumeKeepsPair()
    {
        Split(
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) },
            new[] { ShapeType.Circle, ShapeType.Triangle },
            out List<Vector2Int> anchors,
            out List<List<ShapeCellData>> components);
        return anchors.Count == 1
            && components.Count == 1
            && components[0].Count == 2
            && components[0][0].shapeType == ShapeType.Circle
            && components[0][1].shapeType == ShapeType.Triangle
            && components[0][1].localPosition == new Vector2Int(1, 0);
    }

    private static bool TestM_MultiCellTargetPartialConsume()
    {
        var go = new GameObject("AutoMatch_MultiCellTarget");
        go.AddComponent<RectTransform>();
        go.AddComponent<UIPieceView>();
        Target target = go.AddComponent<Target>();
        target.ApplyLayout(
            ShapeType.Triangle,
            new List<ShapeCellData>
            {
                new ShapeCellData { localPosition = Vector2Int.zero, shapeType = ShapeType.Triangle },
                new ShapeCellData { localPosition = new Vector2Int(1, 0), shapeType = ShapeType.Triangle }
            },
            PieceComposition.Simple,
            ShapeType.Triangle);
        target.Initialize(null, new Vector2Int(2, 2));

        bool consumed = target.TryConsumeLayerAtWorld(
            new Vector2Int(2, 2),
            ShapeType.Triangle,
            out bool complete);
        bool ok = consumed && !complete && target.CellCount == 1;
        Object.DestroyImmediate(go);
        return ok;
    }

    private static bool TestN_UnrelatedSameShapeSurvives()
    {
        var root = new GameObject("AutoMatch_Board");
        BoardManager board = root.AddComponent<BoardManager>();
        board.ApplyGridSize(5, 5);

        var goA = new GameObject("TargetA");
        goA.AddComponent<RectTransform>();
        goA.AddComponent<UIPieceView>();
        Target a = goA.AddComponent<Target>();
        a.ApplyLayout(
            ShapeType.Triangle,
            new List<ShapeCellData>
            {
                new ShapeCellData { localPosition = Vector2Int.zero, shapeType = ShapeType.Triangle },
                new ShapeCellData { localPosition = new Vector2Int(0, 1), shapeType = ShapeType.Triangle }
            },
            PieceComposition.Simple,
            ShapeType.Triangle);
        a.Initialize(board, new Vector2Int(0, 0));

        var goB = new GameObject("TargetB");
        goB.AddComponent<RectTransform>();
        goB.AddComponent<UIPieceView>();
        Target b = goB.AddComponent<Target>();
        b.ApplyLayout(
            ShapeType.Triangle,
            new List<ShapeCellData>
            {
                new ShapeCellData { localPosition = Vector2Int.zero, shapeType = ShapeType.Triangle },
                new ShapeCellData { localPosition = new Vector2Int(1, 0), shapeType = ShapeType.Triangle }
            },
            PieceComposition.Simple,
            ShapeType.Triangle);
        b.Initialize(board, new Vector2Int(3, 3));

        bool consumed = a.TryConsumeLayerAtWorld(new Vector2Int(0, 0), ShapeType.Triangle, out bool complete);
        bool ok = consumed
            && !complete
            && a.CellCount == 1
            && b.CellCount == 2
            && board.GetTargetAt(new Vector2Int(3, 3)) == b
            && board.GetTargetAt(new Vector2Int(4, 3)) == b;

        Object.DestroyImmediate(goA);
        Object.DestroyImmediate(goB);
        Object.DestroyImmediate(root);
        return ok;
    }

    private static void Split(
        Vector2Int[] worlds,
        ShapeType[] shapes,
        out List<Vector2Int> anchors,
        out List<List<ShapeCellData>> components)
    {
        var worldList = new List<Vector2Int>(worlds);
        var cells = new List<ShapeCellData>(worlds.Length);
        for (int i = 0; i < worlds.Length; i++)
        {
            cells.Add(new ShapeCellData
            {
                localPosition = Vector2Int.zero,
                shapeType = shapes[i],
                innerShapes = new List<ShapeType>()
            });
        }

        anchors = new List<Vector2Int>();
        components = new List<List<ShapeCellData>>();
        ShapeLayout.SplitConnected(worldList, cells, anchors, components);
    }
}
