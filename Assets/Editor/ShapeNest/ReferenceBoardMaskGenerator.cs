using System.Collections.Generic;
using UnityEngine;

internal static class ReferenceBoardMaskGenerator
{

    public static HashSet<Vector2Int> Generate(int width, int height, DifficultyTier difficulty, System.Random rng, int levelNumber = 0, GenerationStyle style = GenerationStyle.Mixed)
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        if (rng == null)
        {
            rng = new System.Random();
        }

        if (levelNumber > 0 && levelNumber <= 5)
        {
            return GenerateTutorialMask(width, height, levelNumber, rng);
        }

        int targetMin;
        int targetMax;
        switch (difficulty)
        {
            case DifficultyTier.Easy:
                targetMin = Mathf.Max(12, width * height * 3 / 5);
                targetMax = Mathf.Max(targetMin, width * height * 4 / 5);
                break;
            case DifficultyTier.Medium:
                targetMin = Mathf.Max(12, width * height * 2 / 3);
                targetMax = Mathf.Max(targetMin, width * height * 4 / 5);
                break;
            case DifficultyTier.Hard:
                targetMin = Mathf.Max(12, width * height * 3 / 5);
                targetMax = Mathf.Max(targetMin, width * height * 5 / 6);
                break;
            case DifficultyTier.Expert:
                targetMin = Mathf.Max(12, width * height * 2 / 3);
                targetMax = Mathf.Max(targetMin, width * height * 7 / 8);
                break;
            default:
                targetMin = Mathf.Max(12, width * height * 3 / 5);
                targetMax = Mathf.Max(targetMin, width * height * 4 / 5);
                break;
        }

        var playable = new HashSet<Vector2Int>();
        MaskStrategy strategy = (MaskStrategy)rng.Next(0, 9);

        switch (strategy)
        {
            case MaskStrategy.Rectangle:
                BuildRectangle(playable, width, height, rng, targetMin, targetMax);
                break;
            case MaskStrategy.LShape:
                BuildLShape(playable, width, height, rng, targetMin, targetMax);
                break;
            case MaskStrategy.TShape:
                BuildTShape(playable, width, height, rng, targetMin, targetMax);
                break;
            case MaskStrategy.UShape:
                BuildUShape(playable, width, height, rng, targetMin, targetMax);
                break;
            case MaskStrategy.Cross:
                BuildCross(playable, width, height, rng, targetMin, targetMax);
                break;
            case MaskStrategy.Corridor:
                BuildCorridor(playable, width, height, rng, targetMin, targetMax);
                break;
            case MaskStrategy.Stepped:
                BuildStepped(playable, width, height, rng, targetMin, targetMax);
                break;
            case MaskStrategy.ChamberCorridor:
                BuildChamberCorridor(playable, width, height, rng, targetMin, targetMax);
                break;
            default:
                BuildAsymmetricBlob(playable, width, height, rng, targetMin, targetMax);
                break;
        }

        if (playable.Count < targetMin)
        {
            ExpandToTarget(playable, width, height, rng, targetMin, targetMax);
        }

        if (playable.Count > targetMax)
        {
            TrimToTarget(playable, width, height, rng, targetMin, targetMax);
        }

        EnsureConnected(playable, width, height);

        if (playable.Count < targetMin)
        {
            FillFallback(playable, width, height, rng, targetMin, targetMax);
        }

        return playable;
    }

    private static void BuildRectangle(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        int xMin = rng.Next(0, Mathf.Max(1, width - 2));
        int yMin = rng.Next(0, Mathf.Max(1, height - 2));
        int xMax = rng.Next(xMin + 1, width);
        int yMax = rng.Next(yMin + 1, height);

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                cells.Add(new Vector2Int(x, y));
            }
        }

        if (cells.Count < minTarget)
        {
            ExpandToTarget(cells, width, height, rng, minTarget, maxTarget);
        }
    }

    private static void BuildLShape(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        int x1 = rng.Next(0, Mathf.Max(1, width - 2));
        int y1 = rng.Next(0, Mathf.Max(1, height - 2));
        int x2 = rng.Next(x1 + 1, width);
        int y2 = rng.Next(y1 + 1, height);

        for (int y = y1; y <= y2; y++)
        {
            for (int x = x1; x <= x2; x++)
            {
                cells.Add(new Vector2Int(x, y));
            }
        }

        int armLength = Mathf.Max(1, rng.Next(1, Mathf.Max(2, Mathf.Min(width, height) / 2)));
        int armDirection = rng.Next(0, 4);
        int armX = x2;
        int armY = y1;
        for (int i = 0; i < armLength; i++)
        {
            switch (armDirection)
            {
                case 0:
                    armX = Mathf.Min(width - 1, armX + 1);
                    break;
                case 1:
                    armX = Mathf.Max(0, armX - 1);
                    break;
                case 2:
                    armY = Mathf.Min(height - 1, armY + 1);
                    break;
                default:
                    armY = Mathf.Max(0, armY - 1);
                    break;
            }
            cells.Add(new Vector2Int(armX, armY));
        }

        if (cells.Count < minTarget)
        {
            ExpandToTarget(cells, width, height, rng, minTarget, maxTarget);
        }
    }

    private static void BuildTShape(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        int cx = rng.Next(0, width);
        int cy = rng.Next(0, height);
        int stem = rng.Next(2, Mathf.Max(3, height / 2));
        int span = rng.Next(2, Mathf.Max(3, width / 2));

        for (int i = 0; i < stem; i++)
        {
            cells.Add(new Vector2Int(cx, Mathf.Clamp(cy - i, 0, height - 1)));
        }

        int left = Mathf.Max(0, cx - span);
        int right = Mathf.Min(width - 1, cx + span);
        for (int x = left; x <= right; x++)
        {
            cells.Add(new Vector2Int(x, cy));
        }

        if (cells.Count < minTarget)
        {
            ExpandToTarget(cells, width, height, rng, minTarget, maxTarget);
        }
    }

    private static void BuildUShape(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        int xMin = rng.Next(0, Mathf.Max(1, width - 2));
        int yMin = rng.Next(0, Mathf.Max(1, height - 2));
        int xMax = rng.Next(xMin + 1, width);
        int yMax = rng.Next(yMin + 1, height);

        for (int y = yMin; y <= yMax; y++)
        {
            if (y == yMin || y == yMax)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }
            else
            {
                cells.Add(new Vector2Int(xMin, y));
                cells.Add(new Vector2Int(xMax, y));
            }
        }

        if (cells.Count < minTarget)
        {
            ExpandToTarget(cells, width, height, rng, minTarget, maxTarget);
        }
    }

    private static void BuildCross(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        int cx = rng.Next(1, width - 1);
        int cy = rng.Next(1, height - 1);
        int arm = Mathf.Max(2, Mathf.Min(width, height) / 3);

        for (int x = Mathf.Max(0, cx - arm); x <= Mathf.Min(width - 1, cx + arm); x++)
        {
            cells.Add(new Vector2Int(x, cy));
        }

        for (int y = Mathf.Max(0, cy - arm); y <= Mathf.Min(height - 1, cy + arm); y++)
        {
            cells.Add(new Vector2Int(cx, y));
        }

        if (cells.Count < minTarget)
        {
            ExpandToTarget(cells, width, height, rng, minTarget, maxTarget);
        }
    }

    private static void BuildCorridor(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        int x = rng.Next(0, width);
        int y = rng.Next(0, height);
        int length = Mathf.Max(4, Mathf.Min(width + height, 8 + rng.Next(0, 6)));

        for (int i = 0; i < length; i++)
        {
            cells.Add(new Vector2Int(x, y));
            int direction = rng.Next(0, 4);
            switch (direction)
            {
                case 0:
                    x = Mathf.Min(width - 1, x + 1);
                    break;
                case 1:
                    x = Mathf.Max(0, x - 1);
                    break;
                case 2:
                    y = Mathf.Min(height - 1, y + 1);
                    break;
                default:
                    y = Mathf.Max(0, y - 1);
                    break;
            }
        }

        ExpandToTarget(cells, width, height, rng, minTarget, maxTarget);
    }

    private static void BuildStepped(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        int x = rng.Next(0, width - 1);
        int y = rng.Next(0, height - 1);
        int steps = rng.Next(3, Mathf.Max(4, Mathf.Min(width, height) / 2));

        for (int i = 0; i < steps; i++)
        {
            int span = rng.Next(2, Mathf.Max(2, width / 2));
            for (int sx = 0; sx < span; sx++)
            {
                int px = Mathf.Clamp(x + sx, 0, width - 1);
                int py = Mathf.Clamp(y + i, 0, height - 1);
                cells.Add(new Vector2Int(px, py));
            }

            x = Mathf.Clamp(x + (rng.Next(0, 2) == 0 ? 1 : -1), 0, width - 1);
            y = Mathf.Clamp(y + (rng.Next(0, 2) == 0 ? 1 : 0), 0, height - 1);
        }

        if (cells.Count < minTarget)
        {
            ExpandToTarget(cells, width, height, rng, minTarget, maxTarget);
        }
    }

    private static void BuildAsymmetricBlob(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        int cx = rng.Next(0, width);
        int cy = rng.Next(0, height);
        int rx = Mathf.Max(2, rng.Next(2, Mathf.Max(3, width / 2)));
        int ry = Mathf.Max(2, rng.Next(2, Mathf.Max(3, height / 2)));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int dx = x - cx;
                int dy = y - cy;
                if (Mathf.Abs(dx) <= rx && Mathf.Abs(dy) <= ry)
                {
                    if ((dx * dx) / (float)(rx * rx) + (dy * dy) / (float)(ry * ry) <= 1.1f)
                    {
                        cells.Add(new Vector2Int(x, y));
                    }
                }
            }
        }

        if (cells.Count < minTarget)
        {
            ExpandToTarget(cells, width, height, rng, minTarget, maxTarget);
        }
    }

    private static void BuildChamberCorridor(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        int chamberWidth = Mathf.Max(2, width / 2 + rng.Next(0, 2));
        int chamberHeight = Mathf.Max(2, height / 3 + rng.Next(0, 2));
        int chamberX = rng.Next(0, Mathf.Max(1, width - chamberWidth + 1));
        int chamberY = rng.Next(0, Mathf.Max(1, height - chamberHeight + 1));

        for (int y = chamberY; y < chamberY + chamberHeight; y++)
        {
            for (int x = chamberX; x < chamberX + chamberWidth; x++)
            {
                cells.Add(new Vector2Int(x, y));
            }
        }

        bool vertical = height >= width && rng.NextDouble() < 0.75d;
        int corridorWidth = rng.NextDouble() < 0.8d ? 1 : 2;
        if (vertical)
        {
            int corridorX = chamberX + rng.Next(chamberWidth);
            int direction = rng.Next(2) == 0 ? -1 : 1;
            for (int y = chamberY + (direction < 0 ? 0 : chamberHeight - 1); y >= 0 && y < height; y += direction)
            {
                for (int x = corridorX; x < Mathf.Min(width, corridorX + corridorWidth); x++)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }
        }
        else
        {
            int corridorY = chamberY + rng.Next(chamberHeight);
            int direction = rng.Next(2) == 0 ? -1 : 1;
            for (int x = chamberX + (direction < 0 ? 0 : chamberWidth - 1); x >= 0 && x < width; x += direction)
            {
                for (int y = corridorY; y < Mathf.Min(height, corridorY + corridorWidth); y++)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }
        }

        if (cells.Count < minTarget)
        {
            ExpandToTarget(cells, width, height, rng, minTarget, maxTarget);
        }
    }

    private static void ExpandToTarget(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        var queue = new Queue<Vector2Int>(cells);
        while (queue.Count > 0 && cells.Count < maxTarget)
        {
            Vector2Int current = queue.Dequeue();
            var neighbors = new[]
            {
                current + Vector2Int.up,
                current + Vector2Int.down,
                current + Vector2Int.left,
                current + Vector2Int.right
            };

            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2Int candidate = neighbors[i];
                if (candidate.x < 0 || candidate.x >= width || candidate.y < 0 || candidate.y >= height)
                {
                    continue;
                }

                if (cells.Add(candidate))
                {
                    queue.Enqueue(candidate);
                    if (cells.Count >= maxTarget)
                    {
                        return;
                    }
                }
            }
        }

        while (cells.Count < minTarget)
        {
            int x = rng.Next(0, width);
            int y = rng.Next(0, height);
            cells.Add(new Vector2Int(x, y));
        }
    }

    private static void TrimToTarget(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        var list = new List<Vector2Int>(cells);
        while (cells.Count > maxTarget)
        {
            int index = rng.Next(0, list.Count);
            Vector2Int cell = list[index];
            list.RemoveAt(index);
            cells.Remove(cell);
        }

        EnsureConnected(cells, width, height);
        if (cells.Count < minTarget)
        {
            ExpandToTarget(cells, width, height, rng, minTarget, maxTarget);
        }
    }

    private static void EnsureConnected(HashSet<Vector2Int> cells, int width, int height)
    {
        if (cells.Count == 0)
        {
            cells.Add(new Vector2Int(0, 0));
            return;
        }

        var root = new List<Vector2Int>(cells);
        var visited = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        Vector2Int first = root[0];
        queue.Enqueue(first);
        visited.Add(first);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            var neighbors = new[]
            {
                current + Vector2Int.up,
                current + Vector2Int.down,
                current + Vector2Int.left,
                current + Vector2Int.right
            };

            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2Int candidate = neighbors[i];
                if (candidate.x < 0 || candidate.x >= width || candidate.y < 0 || candidate.y >= height)
                {
                    continue;
                }

                if (cells.Contains(candidate) && visited.Add(candidate))
                {
                    queue.Enqueue(candidate);
                }
            }
        }

        foreach (Vector2Int cell in new List<Vector2Int>(cells))
        {
            if (!visited.Contains(cell))
            {
                cells.Remove(cell);
            }
        }
    }

    private static void FillFallback(HashSet<Vector2Int> cells, int width, int height, System.Random rng, int minTarget, int maxTarget)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (cells.Count >= maxTarget)
                {
                    return;
                }

                if (rng.NextDouble() < 0.5d || cells.Count < minTarget)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }
        }

        EnsureConnected(cells, width, height);
    }

    private static HashSet<Vector2Int> GenerateTutorialMask(int width, int height, int levelNumber, System.Random rng)
    {
        var playable = new HashSet<Vector2Int>();
        switch (levelNumber)
        {
            case 1:
                // Level 1: Very simple board. Solid rectangular layout (all cells).
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        playable.Add(new Vector2Int(x, y));
                    }
                }
                break;

            case 2:
                // Level 2: Clean compact board. Rectangle or clean L-shape.
                bool isLShape = rng.Next(2) == 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (isLShape && x >= width - 2 && y >= height - 2)
                        {
                            continue; // cut out top-right 2x2 corner to form an L
                        }
                        playable.Add(new Vector2Int(x, y));
                    }
                }
                break;

            case 3:
                // Level 3: Introduce slightly more blocking. Clean T-shape, U-shape, or central obstacle.
                int shapeChoice = rng.Next(3);
                if (shapeChoice == 0)
                {
                    // T-shape: top 2 rows wide, stem 2 columns centered
                    int stemLeft = width / 3;
                    int stemRight = Mathf.Min(width - 1, stemLeft + 1);
                    int topBarHeight = Mathf.Max(2, height / 2);
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            if (y >= height - topBarHeight || (x >= stemLeft && x <= stemRight))
                            {
                                playable.Add(new Vector2Int(x, y));
                            }
                        }
                    }
                }
                else if (shapeChoice == 1)
                {
                    // U-shape: bottom 2 rows, plus left and right columns
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            if (y < 2 || x == 0 || x == width - 1)
                            {
                                playable.Add(new Vector2Int(x, y));
                            }
                        }
                    }
                }
                else
                {
                    // Symmetrical obstacle blocks in middle row(s)
                    int blockX = width / 2;
                    int blockY = height / 2;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            if ((x == blockX && y == blockY) || (x == blockX - 1 && y == blockY))
                            {
                                continue;
                            }
                            playable.Add(new Vector2Int(x, y));
                        }
                    }
                }
                break;

            case 4:
                // Level 4: More constrained corridors.
                // Divider wall creating corridors and a transit choke
                bool horizontalDivider = rng.Next(2) == 0;
                int wallX = width / 2;
                int wallY = height / 2;
                int passX = rng.Next(0, 2) == 0 ? 0 : width - 1;
                int passY = rng.Next(0, 2) == 0 ? 0 : height - 1;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (horizontalDivider)
                        {
                            // Horizontal wall across middle with passage at passX
                            if (y == wallY && x != passX)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            // Vertical wall down middle with passage at passY
                            if (x == wallX && y != passY)
                            {
                                continue;
                            }
                        }
                        playable.Add(new Vector2Int(x, y));
                    }
                }
                break;

            case 5:
                // Level 5: Densest/most interesting tutorial layout.
                // Cross silhouette or two connected chambers with bottleneck
                bool useCross = rng.Next(2) == 0;
                if (useCross)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            bool isCorner = (x == 0 || x == width - 1) && (y == 0 || y == height - 1);
                            if (!isCorner)
                            {
                                playable.Add(new Vector2Int(x, y));
                            }
                        }
                    }
                }
                else
                {
                    // Two chambers connected by a central doorway
                    int doorwayY = height / 2;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            if (x == width / 2 && y != doorwayY)
                            {
                                continue;
                            }
                            playable.Add(new Vector2Int(x, y));
                        }
                    }
                }
                break;

            default:
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        playable.Add(new Vector2Int(x, y));
                    }
                }
                break;
        }

        EnsureConnected(playable, width, height);
        return playable;
    }

    private enum MaskStrategy
    {
        Rectangle,
        LShape,
        TShape,
        UShape,
        Cross,
        Corridor,
        Stepped,
        ChamberCorridor,
        AsymmetricBlob
    }
}
