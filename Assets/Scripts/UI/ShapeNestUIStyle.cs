using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opt-in presentation helper. Applies one ShapeNestTheme category to a
/// specific Image, TMP_Text, or Button. No scene search, no Update loop.
/// </summary>
[DisallowMultipleComponent]
public class ShapeNestUIStyle : MonoBehaviour
{
    public enum Category
    {
        None = 0,
        PrimaryBackground = 1,
        Panel = 2,
        PrimaryText = 3,
        SecondaryText = 4,
        ButtonNormal = 5,
        Accent = 6,
        Success = 7,
        Warning = 8,
        BoardBackground = 9,
        BoardGrid = 10,
        PieceShadow = 11,
        PieceHighlight = 12,
        NestWell = 13,
        PauseIcon = 14,
        RestartIcon = 15
    }

    [SerializeField] private ShapeNestTheme theme;
    [SerializeField] private Image image;
    [SerializeField] private TMP_Text text;
    [SerializeField] private Button button;
    [SerializeField] private Category category = Category.None;
    [SerializeField] private bool applySprite;
    [SerializeField] private bool useColorOverride;
    [SerializeField] private Color colorOverride = Color.white;
    [SerializeField] private bool useFontOverride;
    [SerializeField] private TMP_FontAsset fontOverride;

    private void Awake()
    {
        CacheTargets();
        Apply();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheTargets();
        Apply();
    }
#endif

    public void Apply()
    {
        CacheTargets();
        Color color = ResolveColor();
        if (image != null)
        {
            image.color = color;
            if (applySprite)
            {
                Sprite sprite = ResolveSprite();
                if (sprite != null)
                {
                    image.sprite = sprite;
                    bool sliced = sprite.border.sqrMagnitude > 0.01f;
                    image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
                }
            }
        }

        if (text != null)
        {
            text.color = color;
            TMP_FontAsset font = ResolveFont();
            if (font != null)
            {
                text.font = font;
            }
        }

        if (button != null && theme != null && category == Category.ButtonNormal && !useColorOverride)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = theme.buttonNormal;
            colors.highlightedColor = theme.buttonNormal;
            colors.selectedColor = theme.buttonNormal;
            colors.pressedColor = theme.buttonPressed;
            colors.disabledColor = theme.buttonDisabled;
            button.colors = colors;
        }
    }

    private void CacheTargets()
    {
        if (image == null)
        {
            image = GetComponent<Image>();
        }

        if (text == null)
        {
            text = GetComponent<TMP_Text>();
        }

        if (button == null)
        {
            button = GetComponent<Button>();
        }
    }

    private Color ResolveColor()
    {
        if (useColorOverride)
        {
            return colorOverride;
        }

        if (category == Category.PauseIcon || category == Category.RestartIcon)
        {
            return Color.white;
        }

        if (theme == null)
        {
            return image != null ? image.color : (text != null ? text.color : Color.white);
        }

        switch (category)
        {
            case Category.PrimaryBackground:
                return theme.primaryBackground;
            case Category.Panel:
                return theme.panelBackground;
            case Category.PrimaryText:
                return theme.primaryText;
            case Category.SecondaryText:
                return theme.secondaryText;
            case Category.ButtonNormal:
                return theme.buttonNormal;
            case Category.Accent:
                return theme.accent;
            case Category.Success:
                return theme.success;
            case Category.Warning:
                return theme.warning;
            case Category.BoardBackground:
                return theme.boardBackground;
            case Category.BoardGrid:
                return theme.boardGridTint;
            case Category.PieceShadow:
                return theme.pieceShadow;
            case Category.PieceHighlight:
                return theme.pieceHighlight;
            case Category.NestWell:
                return theme.nestWell;
            default:
                return image != null ? image.color : (text != null ? text.color : Color.white);
        }
    }

    private Sprite ResolveSprite()
    {
        if (theme == null)
        {
            return null;
        }

        switch (category)
        {
            case Category.Panel:
                return theme.panelSprite;
            case Category.ButtonNormal:
                return theme.buttonSprite;
            case Category.PauseIcon:
                return theme.pauseButtonSprite;
            case Category.RestartIcon:
                return theme.restartButtonSprite;
            default:
                return null;
        }
    }

    private TMP_FontAsset ResolveFont()
    {
        if (useFontOverride)
        {
            return fontOverride;
        }

        if (theme == null)
        {
            return null;
        }

        if (category == Category.ButtonNormal)
        {
            return theme.buttonFont != null ? theme.buttonFont : theme.mainFont;
        }

        return theme.mainFont;
    }
}
