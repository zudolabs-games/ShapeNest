using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation-only depth for blocks and nests. Does not affect occupancy, input, or movement.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image))]
public class PiecePresentation : MonoBehaviour
{
    public enum PresentationKind
    {
        RaisedPiece = 0,
        RecessedNest = 1
    }

    [SerializeField]
    private ShapeNestTheme theme;

    [SerializeField]
    private PresentationKind kind = PresentationKind.RaisedPiece;

    [SerializeField]
    [Tooltip("Visual size inside the cell. Does not change occupancy or hop distance.")]
    private Vector2 visualSize = new Vector2(80f, 80f);

    [SerializeField]
    [Range(0.5f, 1.2f)]
    [Tooltip("Extra uniform scale of the drawn sprite inside visualSize.")]
    private float visualScale = 1f;

    [Header("Shadow")]
    [SerializeField]
    private Vector2 shadowOffset = new Vector2(1.5f, -2.5f);

    [SerializeField]
    [Range(0f, 0.5f)]
    private float shadowAmount = 0.22f;

    [Header("Highlight")]
    [SerializeField]
    private Vector2 highlightOffset = new Vector2(0f, 1.8f);

    [SerializeField]
    [Range(0f, 0.5f)]
    private float highlightAmount = 0.16f;

    [SerializeField]
    private Vector2 heldShadowBoost = new Vector2(0.5f, -0.8f);

    [SerializeField]
    [Range(0f, 0.2f)]
    [Tooltip("Extra shadow alpha while the piece is held. Raised pieces only.")]
    private float heldShadowExtra = 0.06f;

    [SerializeField]
    private bool applyVisualSize = true;

    private Image source;
    private Shadow meshShadow;
    private Shadow meshHighlight;
    private RectTransform cachedRect;
    private bool held;
    private float heldBlend;

    public float HeldBlend => heldBlend;

    private void Awake()
    {
        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Apply();
    }
#endif

    public void SetVisualSize(Vector2 size)
    {
        visualSize = size;
        Apply();
    }

    public void SetHeld(bool isHeld)
    {
        SetHeldBlend(isHeld ? 1f : 0f);
    }

    public void SetHeldBlend(float blend)
    {
        float next = Mathf.Clamp01(blend);
        if (Mathf.Abs(next - heldBlend) < 0.0001f)
        {
            return;
        }

        heldBlend = next;
        held = heldBlend > 0.001f;
        Apply();
    }

    public void Apply()
    {
        Cache();
        if (Application.isPlaying)
        {
            EnsureMeshEffects();
        }
        else
        {
            CacheExistingShadows();
        }

        if (applyVisualSize && cachedRect != null)
        {
            Vector2 size = visualSize * visualScale;
            if (cachedRect.sizeDelta != size)
            {
                cachedRect.sizeDelta = size;
            }
        }

        Color shadowColor = ResolveShadowColor();
        Color highlightColor = ResolveHighlightColor();
        Vector2 offset = shadowOffset;
        if (kind == PresentationKind.RaisedPiece && heldBlend > 0.001f)
        {
            offset += heldShadowBoost * heldBlend;
        }

        if (meshShadow != null)
        {
            meshShadow.effectDistance = offset;
            meshShadow.effectColor = shadowColor;
            meshShadow.useGraphicAlpha = true;
        }

        if (meshHighlight != null)
        {
            bool showHighlight = kind == PresentationKind.RaisedPiece && highlightAmount > 0.001f;
            meshHighlight.enabled = showHighlight;
            meshHighlight.effectDistance = highlightOffset;
            meshHighlight.effectColor = highlightColor;
            meshHighlight.useGraphicAlpha = true;
        }
    }

    private void Cache()
    {
        if (cachedRect == null)
        {
            cachedRect = (RectTransform)transform;
        }

        if (source == null)
        {
            source = GetComponent<Image>();
        }

        if (source != null)
        {
            source.preserveAspect = true;
        }
    }

    private void CacheExistingShadows()
    {
        Shadow[] existing = GetComponents<Shadow>();
        meshShadow = existing.Length > 0 ? existing[0] : null;
        meshHighlight = existing.Length > 1 ? existing[1] : null;
    }

    private void EnsureMeshEffects()
    {
        CacheExistingShadows();
        if (meshShadow == null)
        {
            meshShadow = gameObject.AddComponent<Shadow>();
        }

        if (meshHighlight == null)
        {
            meshHighlight = gameObject.AddComponent<Shadow>();
        }
    }

    private Color ResolveShadowColor()
    {
        Color color = theme != null ? theme.pieceShadow : new Color(0.08f, 0.06f, 0.12f, 1f);
        if (kind == PresentationKind.RecessedNest && theme != null)
        {
            color = theme.nestWell;
        }

        color.a *= shadowAmount;
        if (kind == PresentationKind.RaisedPiece && heldBlend > 0.001f)
        {
            color.a = Mathf.Min(0.5f, color.a + (heldShadowExtra * heldBlend));
        }

        return color;
    }

    private Color ResolveHighlightColor()
    {
        Color color = theme != null ? theme.pieceHighlight : Color.white;
        color.a *= highlightAmount;
        return color;
    }
}
