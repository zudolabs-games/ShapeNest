using TMPro;
using UnityEngine;

/// <summary>
/// Presentation-only visual configuration. Designer-owned colors and sprites.
/// Does not drive gameplay. Empty sprite slots keep prefab/scene placeholders.
/// </summary>
[CreateAssetMenu(fileName = "ShapeNestTheme", menuName = "Shape Nest/Theme", order = 0)]
public class ShapeNestTheme : ScriptableObject
{
    [Header("Surfaces")]
    public Color primaryBackground = new Color(0.1f, 0.08f, 0.16f, 1f);
    public Color panelBackground = new Color(0.22f, 0.18f, 0.34f, 0.96f);

    [Header("Text")]
    public Color primaryText = new Color(0.93f, 0.91f, 0.98f, 1f);
    public Color secondaryText = new Color(0.6313726f, 0.6156863f, 0.9411765f, 1f);

    [Header("Buttons")]
    public Color buttonNormal = new Color(0.45f, 0.4f, 0.75f, 1f);
    public Color buttonPressed = new Color(0.351f, 0.312f, 0.585f, 1f);
    public Color buttonDisabled = new Color(0.45f, 0.4f, 0.75f, 0.5f);

    [Header("Accents")]
    public Color accent = new Color(0.45f, 0.4f, 0.75f, 1f);
    public Color success = new Color(0.22f, 0.18f, 0.34f, 0.96f);
    public Color warning = new Color(0.9f, 0.48f, 0.58f, 1f);

    [Header("Board")]
    public Color boardBackground = new Color(1f, 1f, 1f, 1f);
    public Color boardGridTint = new Color(1f, 1f, 1f, 0.08f);

    [Header("Pieces")]
    [Tooltip("Soft local shadow on raised pieces. Not a final palette.")]
    public Color pieceShadow = new Color(0.08f, 0.06f, 0.12f, 1f);
    [Tooltip("Soft top/edge highlight on raised pieces.")]
    public Color pieceHighlight = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Recessed nest well tint. Designer can replace later.")]
    public Color nestWell = new Color(0.12f, 0.1f, 0.18f, 1f);

    [Header("Nested shape presentation")]
    [Range(0.4f, 0.7f)]
    [Tooltip("Inner contained shape size relative to the outer piece.")]
    public float nestedInnerScale = 0.52f;

    [Tooltip("Local offset of the contained inner shape. Presentation only.")]
    public Vector2 nestedInnerOffset = new Vector2(0f, -2f);

    [Range(0f, 0.4f)]
    [Tooltip("Inset shadow strength on the inner shape.")]
    public float nestedInnerRecess = 0.2f;

    [Tooltip("Inset shadow offset for a recessed inner shape.")]
    public Vector2 nestedInnerShadowOffset = new Vector2(0.8f, -1.4f);

    [Range(0f, 0.25f)]
    [Tooltip("How much the inner sprite is darkened so it sits inside the outer.")]
    public float nestedInnerDarken = 0.1f;

    [Min(0f)]
    [Tooltip("Time for the inner shape to emerge from the outer before nest-entry.")]
    public float nestedInnerEmergeDuration = 0.08f;

    [Tooltip("Extra Canvas sorting for the contained inner shape. 0 keeps default UI order.")]
    public int nestedInnerSortingOffset = 0;

    [Header("Optional assets")]
    public TMP_FontAsset mainFont;
    public TMP_FontAsset buttonFont;
    public Sprite panelSprite;
    public Sprite buttonSprite;
    public Sprite pauseButtonSprite;
    public Sprite restartButtonSprite;

    [Header("Optional shape art")]
    [Tooltip("Leave empty to keep the Block prefab sprite.")]
    public Sprite blockSquare;
    public Sprite blockCircle;
    public Sprite blockTriangle;
    public Sprite blockDiamond;
    public Sprite blockHexagon;
    public Sprite blockStar;
    [Tooltip("Leave empty to keep the Target prefab sprite.")]
    public Sprite targetSquare;
    public Sprite targetCircle;
    public Sprite targetTriangle;
    public Sprite targetDiamond;
    public Sprite targetHexagon;
    public Sprite targetStar;
    [Tooltip("Leave empty to keep the MatchEffect prefab sprite.")]
    public Sprite matchSquareGlow;
    public Sprite matchCircleGlow;
    public Sprite matchTriangleGlow;
    public Sprite matchDiamondGlow;
    public Sprite matchHexagonGlow;
    public Sprite matchStarGlow;
}
