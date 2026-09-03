using UnityEngine;

public enum ShapeType
{
    Square = 0,
    Circle = 1,
    Triangle = 2,
    Diamond = 3,
    Hexagon = 4,
    Star = 5,
    Pentagon = 6
}

/// <summary>
/// Optional per-cell visual color. Default keeps the shape-type palette from <see cref="ShapeVisuals3D"/>.
/// </summary>
public enum ShapeColor
{
    Default = 0,
    Yellow = 1,
    Cyan = 2,
    Pink = 3,
    Purple = 4,
    Green = 5,
    Red = 6,
    Orange = 7,
    White = 8,
    Blue = 9
}

public enum MoveDirection
{
    Any,
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Authoritative gameplay matching identity: shape and configured color must both agree.
/// </summary>
public readonly struct MatchIdentity : System.IEquatable<MatchIdentity>
{
    public readonly ShapeType Shape;
    public readonly ShapeColor Color;

    public MatchIdentity(ShapeType shape, ShapeColor color)
    {
        Shape = shape;
        Color = color;
    }

    public bool Equals(MatchIdentity other) => Shape == other.Shape && Color == other.Color;

    public override bool Equals(object obj) => obj is MatchIdentity other && Equals(other);

    public override int GetHashCode() => ((int)Shape * 397) ^ (int)Color;

    public static bool operator ==(MatchIdentity a, MatchIdentity b) => a.Equals(b);

    public static bool operator !=(MatchIdentity a, MatchIdentity b) => !a.Equals(b);

    public override string ToString() => $"{Shape}:{Color}";
}

/// <summary>
/// Single authority for whether two gameplay layers match (ShapeType + ShapeColor).
/// </summary>
public static class ShapeMatch
{
    public static bool AreMatchingLayers(MatchIdentity a, MatchIdentity b) => a == b;

    public static bool AreMatchingLayers(
        ShapeType shapeA,
        ShapeColor colorA,
        ShapeType shapeB,
        ShapeColor colorB)
    {
        return shapeA == shapeB && colorA == colorB;
    }

    public static MatchIdentity FromCell(ShapeCellData cell, ShapeType fallbackShape = ShapeType.Square)
    {
        if (cell == null)
        {
            return new MatchIdentity(fallbackShape, ShapeColor.Default);
        }

        return new MatchIdentity(cell.shapeType, ShapeLayout.EffectiveOuterColor(cell));
    }
}

/// <summary>
/// How a piece or nest is composed. Simple uses only <see cref="ShapeCellData"/> cells.
/// ShapeInShape also requires a matching outer shape plus the inner cell configuration.
/// </summary>
public enum PieceComposition
{
    Simple = 0,
    ShapeInShape = 1
}

/// <summary>
/// Presentation-only sprite lookup. Gameplay identity remains ShapeType.
/// </summary>
public static class ShapeVisuals
{
    public static Sprite SpriteFor(
        ShapeType shapeType,
        Sprite square,
        Sprite circle,
        Sprite triangle,
        Sprite diamond = null,
        Sprite hexagon = null,
        Sprite star = null)
    {
        switch (shapeType)
        {
            case ShapeType.Circle:
                return First(circle, square);
            case ShapeType.Triangle:
                return First(triangle, square);
            case ShapeType.Diamond:
                return First(diamond, square);
            case ShapeType.Hexagon:
                return First(hexagon, square);
            case ShapeType.Star:
                return First(star, square);
            case ShapeType.Pentagon:
                return First(hexagon, square);
            default:
                return square;
        }
    }

    public static Sprite First(Sprite preferred, Sprite fallback)
    {
        return preferred != null ? preferred : fallback;
    }
}
