using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the dedicated Regression_01..12 LevelData assets and appends them to LevelDatabase.
/// Data-only; does not change gameplay code.
/// </summary>
internal static class CreateRegressionLevels
{
    private const string Folder = LevelAssetUtility.LevelsFolder;

    private static readonly string[] LevelNames =
    {
        "Regression_01_SimpleAnyMatch",
        "Regression_02_ChainMiddleSplit",
        "Regression_03_ChainEndSplit",
        "Regression_04_ChainAutoMatch",
        "Regression_05_AdjacentAutoMatch",
        "Regression_06_NestedSimple",
        "Regression_07_NestedChainMiddle",
        "Regression_08_NestedChainEnd",
        "Regression_09_NestedChainAutoCascade",
        "Regression_10_DuplicateTargets",
        "Regression_11_LongChain",
        "Regression_12_CombinedRegression"
    };

    [MenuItem("Tools/Shape Nest/Create Regression Levels")]
    public static void CreateFromMenu()
    {
        string report = CreateAll(overwrite: true);
        if (report.Contains("FAIL") || report.Contains("INVALID"))
        {
            Debug.LogError(report);
        }
        else
        {
            Debug.Log(report);
        }
    }

    [MenuItem("Tools/Shape Nest/Validate Regression Levels")]
    public static void ValidateFromMenu()
    {
        string report = ValidateRegressionOnly();
        if (report.Contains("INVALID") || report.Contains("FAIL"))
        {
            Debug.LogError(report);
        }
        else
        {
            Debug.Log(report);
        }
    }

    public static string CreateAll(bool overwrite)
    {
        var builder = new StringBuilder();
        int created = 0;
        int failed = 0;

        LevelAssetUtility.EnsureLevelsFolder();

        for (int i = 0; i < LevelNames.Length; i++)
        {
            string name = LevelNames[i];
            BuildLevel(i, out int width, out int height, out List<LevelBlockData> blocks, out List<LevelTargetData> targets);
            LevelEditorValidationResult validation = LevelEditorValidation.Validate(name, blocks, targets, width, height);
            if (!validation.IsValid)
            {
                failed++;
                builder.AppendLine($"FAIL  {name}");
                for (int e = 0; e < validation.Errors.Count; e++)
                {
                    builder.AppendLine("      " + validation.Errors[e]);
                }

                continue;
            }

            LevelData asset = LevelAssetUtility.SaveLevelData(name, blocks, targets, overwrite, width, height);
            if (asset == null)
            {
                failed++;
                builder.AppendLine($"FAIL  {name} (asset not saved)");
                continue;
            }

            created++;
            builder.AppendLine($"OK    {name}  {width}x{height}  blocks={blocks.Count} targets={targets.Count}");
        }

        builder.Insert(0, $"Regression levels: {created} saved, {failed} failed\n");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return builder.ToString();
    }

    public static string ValidateRegressionOnly()
    {
        var builder = new StringBuilder();
        int valid = 0;
        int invalid = 0;
        for (int i = 0; i < LevelNames.Length; i++)
        {
            string path = $"{Folder}/{LevelNames[i]}.asset";
            LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (level == null)
            {
                invalid++;
                builder.AppendLine($"MISSING  {LevelNames[i]}");
                continue;
            }

            LevelEditorValidationResult result = LevelEditorValidation.Validate(
                level.name,
                level.blocks,
                level.targets,
                level.ResolvedGridWidth,
                level.ResolvedGridHeight);
            if (result.IsValid)
            {
                valid++;
                builder.AppendLine($"VALID  {LevelNames[i]}");
            }
            else
            {
                invalid++;
                builder.AppendLine($"INVALID  {LevelNames[i]}");
                for (int e = 0; e < result.Errors.Count; e++)
                {
                    builder.AppendLine("         " + result.Errors[e]);
                }
            }
        }

        builder.Insert(0, $"Regression validation: {valid} valid, {invalid} invalid\n");
        return builder.ToString();
    }

    private static void BuildLevel(
        int index,
        out int width,
        out int height,
        out List<LevelBlockData> blocks,
        out List<LevelTargetData> targets)
    {
        blocks = new List<LevelBlockData>();
        targets = new List<LevelTargetData>();
        width = 5;
        height = 5;

        switch (index)
        {
            case 0:
                Build01(ref width, ref height, blocks, targets);
                break;
            case 1:
                Build02(ref width, ref height, blocks, targets);
                break;
            case 2:
                Build03(ref width, ref height, blocks, targets);
                break;
            case 3:
                Build04(ref width, ref height, blocks, targets);
                break;
            case 4:
                Build05(ref width, ref height, blocks, targets);
                break;
            case 5:
                Build06(ref width, ref height, blocks, targets);
                break;
            case 6:
                Build07(ref width, ref height, blocks, targets);
                break;
            case 7:
                Build08(ref width, ref height, blocks, targets);
                break;
            case 8:
                Build09(ref width, ref height, blocks, targets);
                break;
            case 9:
                Build10(ref width, ref height, blocks, targets);
                break;
            case 10:
                Build11(ref width, ref height, blocks, targets);
                break;
            default:
                Build12(ref width, ref height, blocks, targets);
                break;
        }
    }

    // Any Circle ↔ any Circle target.
    private static void Build01(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 5;
        h = 5;
        blocks.Add(SimpleBlock(ShapeType.Circle, 1, 1));
        blocks.Add(SimpleBlock(ShapeType.Circle, 3, 3));
        targets.Add(SimpleTarget(ShapeType.Circle, 1, 3));
        targets.Add(SimpleTarget(ShapeType.Circle, 3, 1));
    }

    // C—T—S; Triangle target one cell below middle. C/S targets elsewhere.
    private static void Build02(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 6;
        h = 6;
        blocks.Add(ChainBlock(1, 4, ShapeType.Circle, ShapeType.Triangle, ShapeType.Square));
        targets.Add(SimpleTarget(ShapeType.Triangle, 2, 3));
        targets.Add(SimpleTarget(ShapeType.Circle, 0, 0));
        targets.Add(SimpleTarget(ShapeType.Square, 5, 0));
    }

    // C—T—S; Square is the first easy match (one cell below end).
    private static void Build03(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 6;
        h = 6;
        blocks.Add(ChainBlock(1, 4, ShapeType.Circle, ShapeType.Triangle, ShapeType.Square));
        targets.Add(SimpleTarget(ShapeType.Square, 3, 3));
        targets.Add(SimpleTarget(ShapeType.Circle, 0, 0));
        targets.Add(SimpleTarget(ShapeType.Triangle, 5, 0));
    }

    // T—C—S all occupying matching targets → sequential auto-match T then C then S.
    private static void Build04(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 6;
        h = 6;
        blocks.Add(ChainBlock(1, 3, ShapeType.Triangle, ShapeType.Circle, ShapeType.Square));
        targets.Add(SimpleTarget(ShapeType.Triangle, 1, 3));
        targets.Add(SimpleTarget(ShapeType.Circle, 2, 3));
        targets.Add(SimpleTarget(ShapeType.Square, 3, 3));
    }

    // Circle — Triangle both occupying matching targets on a 4×4 (minimal sequential auto-match).
    private static void BuildCircleTriangleDiag(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 4;
        h = 4;
        blocks.Add(new LevelBlockData
        {
            shapeType = ShapeType.Circle,
            moveDirection = MoveDirection.Any,
            gridPosition = new Vector2Int(1, 2),
            cells = new List<ShapeCellData>
            {
                PlainCell(new Vector2Int(0, 0), ShapeType.Circle),
                PlainCell(new Vector2Int(1, 0), ShapeType.Triangle)
            },
            composition = PieceComposition.Simple,
            outerShape = ShapeType.Circle
        });
        targets.Add(SimpleTarget(ShapeType.Circle, 1, 2));
        targets.Add(SimpleTarget(ShapeType.Triangle, 2, 2));
    }

    // Adjacent occupying pairs auto-match sequentially.
    private static void Build05(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 5;
        h = 5;
        blocks.Add(SimpleBlock(ShapeType.Circle, 1, 2));
        blocks.Add(SimpleBlock(ShapeType.Triangle, 2, 2));
        blocks.Add(SimpleBlock(ShapeType.Square, 1, 0));
        blocks.Add(SimpleBlock(ShapeType.Circle, 2, 0));
        targets.Add(SimpleTarget(ShapeType.Circle, 1, 2));
        targets.Add(SimpleTarget(ShapeType.Triangle, 2, 2));
        targets.Add(SimpleTarget(ShapeType.Square, 1, 0));
        targets.Add(SimpleTarget(ShapeType.Circle, 2, 0));
    }

    // Standalone Square⊃Triangle one hop above nested target.
    private static void Build06(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 4;
        h = 4;
        blocks.Add(NestedBlock(ShapeType.Square, ShapeType.Triangle, 1, 2));
        targets.Add(NestedTarget(ShapeType.Square, ShapeType.Triangle, 1, 0));
    }

    // C—[S⊃T]—C; nested target one cell below middle.
    private static void Build07(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 7;
        h = 7;
        blocks.Add(NestedMiddleChain(2, 5));
        targets.Add(NestedTarget(ShapeType.Square, ShapeType.Triangle, 3, 4));
        targets.Add(SimpleTarget(ShapeType.Circle, 0, 0));
        targets.Add(SimpleTarget(ShapeType.Circle, 6, 0));
    }

    // C—T—[S⊃C]; nested target one cell below end.
    private static void Build08(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 7;
        h = 7;
        blocks.Add(NestedEndChain(2, 5));
        targets.Add(NestedTarget(ShapeType.Square, ShapeType.Circle, 4, 4));
        targets.Add(SimpleTarget(ShapeType.Circle, 0, 0));
        targets.Add(SimpleTarget(ShapeType.Triangle, 6, 0));
    }

    // Nested middle chain + independent Square; tests nested inner→outer then remaining solve.
    private static void Build09(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 8;
        h = 8;
        blocks.Add(NestedMiddleChain(2, 5));
        blocks.Add(SimpleBlock(ShapeType.Square, 6, 5));
        // Nested target adjacent below middle. Independent Square already occupying.
        // Circle targets elsewhere so load auto-match does not consume the chain first;
        // after Triangle→Square outer, survivors stay put for the player / later auto if moved.
        targets.Add(NestedTarget(ShapeType.Square, ShapeType.Triangle, 3, 4));
        targets.Add(SimpleTarget(ShapeType.Square, 6, 5));
        targets.Add(SimpleTarget(ShapeType.Circle, 0, 1));
        targets.Add(SimpleTarget(ShapeType.Circle, 7, 1));
    }

    // Duplicate Circles/Triangles, any-to-any.
    private static void Build10(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 7;
        h = 7;
        blocks.Add(SimpleBlock(ShapeType.Circle, 0, 5));
        blocks.Add(SimpleBlock(ShapeType.Circle, 2, 5));
        blocks.Add(SimpleBlock(ShapeType.Circle, 4, 5));
        blocks.Add(SimpleBlock(ShapeType.Triangle, 0, 3));
        blocks.Add(SimpleBlock(ShapeType.Triangle, 2, 3));
        targets.Add(SimpleTarget(ShapeType.Circle, 1, 1));
        targets.Add(SimpleTarget(ShapeType.Circle, 3, 1));
        targets.Add(SimpleTarget(ShapeType.Circle, 5, 1));
        targets.Add(SimpleTarget(ShapeType.Triangle, 1, 0));
        targets.Add(SimpleTarget(ShapeType.Triangle, 2, 0));
    }

    // Five-cell chain; middle Square matches first (adjacent below).
    private static void Build11(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 8;
        h = 8;
        blocks.Add(FiveChain(1, 5));
        targets.Add(SimpleTarget(ShapeType.Square, 3, 4));
        targets.Add(SimpleTarget(ShapeType.Circle, 0, 0));
        targets.Add(SimpleTarget(ShapeType.Triangle, 1, 0));
        targets.Add(SimpleTarget(ShapeType.Circle, 6, 0));
        targets.Add(SimpleTarget(ShapeType.Triangle, 7, 0));
    }

    // Combined integration board.
    private static void Build12(ref int w, ref int h, List<LevelBlockData> blocks, List<LevelTargetData> targets)
    {
        w = 8;
        h = 8;
        blocks.Add(ChainBlock(0, 6, ShapeType.Circle, ShapeType.Triangle, ShapeType.Square));
        blocks.Add(NestedMiddleChain(4, 6));
        blocks.Add(SimpleBlock(ShapeType.Circle, 0, 3));
        blocks.Add(SimpleBlock(ShapeType.Square, 2, 3));
        blocks.Add(SimpleBlock(ShapeType.Triangle, 4, 3));
        targets.Add(SimpleTarget(ShapeType.Triangle, 1, 5));
        targets.Add(SimpleTarget(ShapeType.Circle, 0, 0));
        targets.Add(SimpleTarget(ShapeType.Square, 2, 0));
        targets.Add(NestedTarget(ShapeType.Square, ShapeType.Triangle, 5, 5));
        targets.Add(SimpleTarget(ShapeType.Circle, 4, 0));
        targets.Add(SimpleTarget(ShapeType.Circle, 6, 0));
        targets.Add(SimpleTarget(ShapeType.Circle, 7, 3));
        targets.Add(SimpleTarget(ShapeType.Square, 2, 2));
        targets.Add(SimpleTarget(ShapeType.Triangle, 4, 1));
    }

    private static LevelBlockData SimpleBlock(ShapeType shape, int x, int y)
    {
        return new LevelBlockData
        {
            shapeType = shape,
            moveDirection = MoveDirection.Any,
            gridPosition = new Vector2Int(x, y),
            cells = new List<ShapeCellData>(),
            composition = PieceComposition.Simple,
            outerShape = shape
        };
    }

    private static LevelTargetData SimpleTarget(ShapeType shape, int x, int y)
    {
        return new LevelTargetData
        {
            shapeType = shape,
            gridPosition = new Vector2Int(x, y),
            cells = new List<ShapeCellData>(),
            composition = PieceComposition.Simple,
            outerShape = shape
        };
    }

    private static LevelBlockData NestedBlock(ShapeType outer, ShapeType inner, int x, int y)
    {
        return new LevelBlockData
        {
            shapeType = outer,
            moveDirection = MoveDirection.Any,
            gridPosition = new Vector2Int(x, y),
            cells = new List<ShapeCellData>
            {
                NestedCell(Vector2Int.zero, outer, inner)
            },
            composition = PieceComposition.Simple,
            outerShape = outer
        };
    }

    private static LevelTargetData NestedTarget(ShapeType outer, ShapeType inner, int x, int y)
    {
        return new LevelTargetData
        {
            shapeType = outer,
            gridPosition = new Vector2Int(x, y),
            cells = new List<ShapeCellData>
            {
                NestedCell(Vector2Int.zero, outer, inner)
            },
            composition = PieceComposition.Simple,
            outerShape = outer
        };
    }

    private static ShapeCellData NestedCell(Vector2Int local, ShapeType outer, ShapeType inner)
    {
        return new ShapeCellData
        {
            localPosition = local,
            shapeType = outer,
            innerShapes = new List<ShapeType> { inner }
        };
    }

    private static ShapeCellData PlainCell(Vector2Int local, ShapeType shape)
    {
        return new ShapeCellData
        {
            localPosition = local,
            shapeType = shape,
            innerShapes = new List<ShapeType>()
        };
    }

    private static LevelBlockData ChainBlock(int x, int y, ShapeType a, ShapeType b, ShapeType c)
    {
        return new LevelBlockData
        {
            shapeType = a,
            moveDirection = MoveDirection.Any,
            gridPosition = new Vector2Int(x, y),
            cells = new List<ShapeCellData>
            {
                PlainCell(new Vector2Int(0, 0), a),
                PlainCell(new Vector2Int(1, 0), b),
                PlainCell(new Vector2Int(2, 0), c)
            },
            composition = PieceComposition.Simple,
            outerShape = a
        };
    }

    private static LevelBlockData NestedMiddleChain(int x, int y)
    {
        return new LevelBlockData
        {
            shapeType = ShapeType.Circle,
            moveDirection = MoveDirection.Any,
            gridPosition = new Vector2Int(x, y),
            cells = new List<ShapeCellData>
            {
                PlainCell(new Vector2Int(0, 0), ShapeType.Circle),
                NestedCell(new Vector2Int(1, 0), ShapeType.Square, ShapeType.Triangle),
                PlainCell(new Vector2Int(2, 0), ShapeType.Circle)
            },
            composition = PieceComposition.Simple,
            outerShape = ShapeType.Circle
        };
    }

    private static LevelBlockData NestedEndChain(int x, int y)
    {
        return new LevelBlockData
        {
            shapeType = ShapeType.Circle,
            moveDirection = MoveDirection.Any,
            gridPosition = new Vector2Int(x, y),
            cells = new List<ShapeCellData>
            {
                PlainCell(new Vector2Int(0, 0), ShapeType.Circle),
                PlainCell(new Vector2Int(1, 0), ShapeType.Triangle),
                NestedCell(new Vector2Int(2, 0), ShapeType.Square, ShapeType.Circle)
            },
            composition = PieceComposition.Simple,
            outerShape = ShapeType.Circle
        };
    }

    private static LevelBlockData FiveChain(int x, int y)
    {
        return new LevelBlockData
        {
            shapeType = ShapeType.Circle,
            moveDirection = MoveDirection.Any,
            gridPosition = new Vector2Int(x, y),
            cells = new List<ShapeCellData>
            {
                PlainCell(new Vector2Int(0, 0), ShapeType.Circle),
                PlainCell(new Vector2Int(1, 0), ShapeType.Triangle),
                PlainCell(new Vector2Int(2, 0), ShapeType.Square),
                PlainCell(new Vector2Int(3, 0), ShapeType.Circle),
                PlainCell(new Vector2Int(4, 0), ShapeType.Triangle)
            },
            composition = PieceComposition.Simple,
            outerShape = ShapeType.Circle
        };
    }
}
