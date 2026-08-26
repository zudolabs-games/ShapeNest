using UnityEngine;

/// <summary>
/// Current uGUI implementation of <see cref="IPieceView"/>.
/// Uses RectTransform + optional <see cref="PiecePresentation"/>; does not own gameplay state.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class UIPieceView : MonoBehaviour, IPieceView
{
    [SerializeField]
    private RectTransform cachedRect;

    [SerializeField]
    private PiecePresentation piecePresentation;

    public RectTransform RectTransform
    {
        get
        {
            if (cachedRect == null)
            {
                cachedRect = (RectTransform)transform;
            }

            return cachedRect;
        }
    }

    public Vector3 LocalScale
    {
        get => RectTransform.localScale;
        set => RectTransform.localScale = value;
    }

    private void Awake()
    {
        Cache();
    }

    private void OnValidate()
    {
        Cache();
    }

    public void ApplyGridPosition(IGridSpace gridSpace, Vector2Int gridPosition)
    {
        if (gridSpace == null)
        {
            return;
        }

        RectTransform.anchoredPosition = gridSpace.GridToLocal(gridPosition);
    }

    public void SetHeld(bool held)
    {
        PiecePresentation presentation = CachePresentation();
        if (presentation != null)
        {
            presentation.SetHeld(held);
        }
    }

    public void SetHeldBlend(float blend)
    {
        PiecePresentation presentation = CachePresentation();
        if (presentation != null)
        {
            presentation.SetHeldBlend(blend);
        }
    }

    /// <summary>Applies visual size to the root RectTransform and PiecePresentation when present.</summary>
    public void SetVisualSize(Vector2 size)
    {
        RectTransform.sizeDelta = size;
        PiecePresentation presentation = CachePresentation();
        if (presentation != null)
        {
            presentation.SetVisualSize(size);
        }
    }

    private void Cache()
    {
        if (cachedRect == null)
        {
            cachedRect = transform as RectTransform;
        }

        if (piecePresentation == null)
        {
            piecePresentation = GetComponent<PiecePresentation>();
        }
    }

    private PiecePresentation CachePresentation()
    {
        if (piecePresentation == null)
        {
            piecePresentation = GetComponent<PiecePresentation>();
        }

        return piecePresentation;
    }
}
