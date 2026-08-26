using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation-only board panel. Does not affect grid math, occupancy, or input.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class BoardVisual : MonoBehaviour
{
    private const string BackgroundName = "BoardBackground";
    private const string RuntimeGridName = "RuntimeGrid";

    [SerializeField]
    private ShapeNestTheme theme;

    [SerializeField]
    private Sprite panelSprite;

    [SerializeField]
    [Tooltip("Used when no theme is assigned.")]
    private Color panelColor = new Color(1f, 1f, 1f, 1f);

    [SerializeField]
    private Vector2 panelPadding = new Vector2(18f, 18f);

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("9-slice corner softness. Higher usually reads as rounder on BoardPanel.")]
    private float panelPixelsPerUnitMultiplier = 1.15f;

    [SerializeField]
    private bool softenRuntimeGrid = true;

    [SerializeField]
    [Tooltip("Used when no theme is assigned.")]
    private Color runtimeGridColor = new Color(1f, 1f, 1f, 0.07f);

    [SerializeField]
    [Min(0.5f)]
    [Tooltip("Visual thickness of inner grid lines. Does not change cell math.")]
    private float gridLineThickness = 1.25f;

    [SerializeField]
    [Tooltip("Hide the outer grid lines so the rounded panel is the board edge.")]
    private bool hidePerimeterGrid = true;

    private RectTransform backgroundRect;
    private Image backgroundImage;
    private BoardManager boardManager;

    private void OnEnable()
    {
        EnsureBackground();
        ApplyPresentation();
    }

    private void Start()
    {
        ApplyPresentation();
    }

    public void RefreshPresentation()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        EnsureBackground();
        ApplyPresentation();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        ApplyPresentation();
    }

#if UNITY_EDITOR
    private void LateUpdate()
    {
        if (Application.isPlaying)
        {
            return;
        }

        EnsureBackground();
        ApplyPresentation();
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled || backgroundImage == null)
        {
            return;
        }

        ApplyPresentation();
    }
#endif

    private void EnsureBackground()
    {
        if (backgroundRect != null && backgroundImage != null)
        {
            return;
        }

        Transform existing = transform.Find(BackgroundName);
        GameObject backgroundObject;
        if (existing != null)
        {
            backgroundObject = existing.gameObject;
        }
        else
        {
            backgroundObject = new GameObject(BackgroundName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backgroundObject.layer = gameObject.layer;
            backgroundObject.transform.SetParent(transform, false);
        }

        backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundImage = backgroundObject.GetComponent<Image>();
    }

    private void ApplyPresentation()
    {
        EnsureBackground();
        if (backgroundRect == null || backgroundImage == null)
        {
            return;
        }

        Sprite sprite = ResolvePanelSprite();
        Color color = ResolvePanelColor();

        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = new Vector2(-panelPadding.x, -panelPadding.y);
        backgroundRect.offsetMax = new Vector2(panelPadding.x, panelPadding.y);
        backgroundRect.localScale = Vector3.one;
        backgroundRect.localRotation = Quaternion.identity;

        backgroundImage.sprite = sprite;
        backgroundImage.color = color;
        backgroundImage.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        backgroundImage.pixelsPerUnitMultiplier = panelPixelsPerUnitMultiplier;
        backgroundImage.raycastTarget = false;
        backgroundImage.maskable = true;

        backgroundRect.SetSiblingIndex(0);
        // Phase 11: World3D is the active board — keep UI board chrome hidden.
        backgroundRect.gameObject.SetActive(false);

        Transform grid = transform.Find(RuntimeGridName);
        if (grid == null)
        {
            return;
        }

        grid.SetSiblingIndex(1);
        grid.gameObject.SetActive(false);
        ApplyGridPresentation(grid);
    }

    private void ApplyGridPresentation(Transform grid)
    {
        if (!softenRuntimeGrid)
        {
            return;
        }

        Color lineColor = ResolveGridColor();
        if (boardManager == null)
        {
            boardManager = GetComponent<BoardManager>();
        }

        int width = boardManager != null ? boardManager.Width : 5;
        int height = boardManager != null ? boardManager.Height : 5;

        for (int i = 0; i < grid.childCount; i++)
        {
            Transform line = grid.GetChild(i);
            var lineRect = line as RectTransform;
            var image = line.GetComponent<Image>();
            if (lineRect == null || image == null)
            {
                continue;
            }

            image.color = lineColor;
            image.raycastTarget = false;

            bool perimeter = hidePerimeterGrid && IsPerimeterLine(line.name, width, height);
            line.gameObject.SetActive(!perimeter);
            if (perimeter)
            {
                continue;
            }

            bool vertical = line.name.StartsWith("Vertical_");
            lineRect.sizeDelta = vertical
                ? new Vector2(gridLineThickness, 0f)
                : new Vector2(0f, gridLineThickness);
        }
    }

    private static bool IsPerimeterLine(string lineName, int width, int height)
    {
        return lineName == "Vertical_0"
            || lineName == $"Vertical_{width}"
            || lineName == "Horizontal_0"
            || lineName == $"Horizontal_{height}";
    }

    private Sprite ResolvePanelSprite()
    {
        if (theme != null && theme.panelSprite != null)
        {
            return theme.panelSprite;
        }

        return panelSprite;
    }

    private Color ResolvePanelColor()
    {
        return theme != null ? theme.boardBackground : panelColor;
    }

    private Color ResolveGridColor()
    {
        return theme != null ? theme.boardGridTint : runtimeGridColor;
    }
}
