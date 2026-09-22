using UnityEngine;

/// <summary>
/// Sizes and centers the Board RectTransform inside a gameplay-area rectangle.
/// Assign Gameplay Area to a future UI panel, or leave empty to use the parent rect / defaults.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(BoardManager))]
public class BoardLayout : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Future gameplay-area panel. When assigned, its rect size drives cell sizing.")]
    private RectTransform gameplayArea;

    [SerializeField]
    [Tooltip("When Gameplay Area is not assigned, use the Board parent RectTransform if present.")]
    private bool useParentAsGameplayArea = true;

    [SerializeField]
    [Tooltip("Fallback gameplay-area size when no RectTransform reference is available.")]
    private Vector2 defaultGameplayAreaSize = new Vector2(600f, 600f);

    [SerializeField]
    [Tooltip("Padding inside the gameplay area before the board is sized.")]
    private Vector2 gameplayAreaPadding = new Vector2(16f, 16f);

    private BoardManager boardManager;
    private RectTransform boardRect;
    private Vector2 lastResolvedAreaSize;
    private int lastGridWidth;
    private int lastGridHeight;

    public RectTransform GameplayArea
    {
        get => gameplayArea;
        set
        {
            if (gameplayArea == value)
            {
                return;
            }

            gameplayArea = value;
            RefreshLayout();
        }
    }

    public Vector2 DefaultGameplayAreaSize => defaultGameplayAreaSize;
    public Vector2 GameplayAreaPadding => gameplayAreaPadding;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        RefreshLayout();
    }

    private void Update()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        Vector2 areaSize = ResolveGameplayAreaSize();
        if (areaSize != lastResolvedAreaSize)
        {
            lastResolvedAreaSize = areaSize;
            RefreshLayout();
        }
    }

    private void OnValidate()
    {
        defaultGameplayAreaSize.x = Mathf.Max(1f, defaultGameplayAreaSize.x);
        defaultGameplayAreaSize.y = Mathf.Max(1f, defaultGameplayAreaSize.y);
        gameplayAreaPadding.x = Mathf.Max(0f, gameplayAreaPadding.x);
        gameplayAreaPadding.y = Mathf.Max(0f, gameplayAreaPadding.y);
    }

    public void ApplyLayout(int gridWidth, int gridHeight)
    {
        CacheReferences();
        if (boardManager == null || boardRect == null)
        {
            return;
        }

        gridWidth = Mathf.Max(1, gridWidth);
        gridHeight = Mathf.Max(1, gridHeight);
        lastGridWidth = gridWidth;
        lastGridHeight = gridHeight;

        Vector2 areaSize = ResolveGameplayAreaSize();
        lastResolvedAreaSize = areaSize;

        float cellSize = BoardLayoutMath.ComputeSquareCellSize(
            gridWidth,
            gridHeight,
            areaSize.x,
            areaSize.y,
            gameplayAreaPadding.x,
            gameplayAreaPadding.y);

        Vector2 boardSize = BoardLayoutMath.ComputeBoardSize(
            gridWidth,
            gridHeight,
            cellSize,
            boardManager.GridPadding);

        boardRect.anchorMin = new Vector2(0.5f, 0.5f);
        boardRect.anchorMax = new Vector2(0.5f, 0.5f);
        boardRect.pivot = new Vector2(0.5f, 0.5f);
        boardRect.anchoredPosition = Vector2.zero;
        boardRect.sizeDelta = boardSize;

        boardManager.RefreshRuntimeGridAfterLayout();
        RefreshDependentVisuals();
    }

    public void RefreshLayout()
    {
        CacheReferences();
        if (boardManager == null)
        {
            return;
        }

        ApplyLayout(boardManager.Width, boardManager.Height);
    }

    private void CacheReferences()
    {
        if (boardManager == null)
        {
            boardManager = GetComponent<BoardManager>();
        }

        if (boardRect == null)
        {
            boardRect = transform as RectTransform;
        }
    }

    private Vector2 ResolveGameplayAreaSize()
    {
        Vector2 areaSize;
        if (gameplayArea != null)
        {
            areaSize = gameplayArea.rect.size;
        }
        else if (useParentAsGameplayArea && transform.parent is RectTransform parentArea)
        {
            areaSize = parentArea.rect.size;
        }
        else
        {
            areaSize = defaultGameplayAreaSize;
        }

        if (areaSize.x <= 1f || areaSize.y <= 1f)
        {
            return defaultGameplayAreaSize;
        }

        return areaSize;
    }

    private void RefreshDependentVisuals()
    {
        Block[] blocks = GetComponentsInChildren<Block>(true);
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] != null)
            {
                blocks[i].RefreshLayoutVisuals();
            }
        }

        Target[] targets = GetComponentsInChildren<Target>(true);
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
            {
                targets[i].RefreshLayoutVisuals();
            }
        }

        ShutterState[] shutters = GetComponentsInChildren<ShutterState>(true);
        for (int i = 0; i < shutters.Length; i++)
        {
            if (shutters[i] != null)
            {
                shutters[i].RefreshLayoutVisuals();
            }
        }

        BoardVisual visual = GetComponent<BoardVisual>();
        if (visual != null)
        {
            visual.RefreshPresentation();
        }
    }
}
