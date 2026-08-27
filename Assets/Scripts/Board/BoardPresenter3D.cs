using UnityEngine;

/// <summary>
/// Builds and maintains the world-space 3D board mesh hierarchy from logical grid size.
/// Does not own occupancy, pieces, or input. Uses <see cref="GridSpace3D"/> for mapping.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class BoardPresenter3D : MonoBehaviour
{
    private const string SurfaceName = "BoardSurface";
    private const string CellsName = "Cells";
    private const string FrameName = "Frame";
    private const string PiecesName = "Pieces3D";
    private const string NestsName = "Nests3D";
    private const string IceName = "Ice3D";
    private const string ShuttersName = "Shutters3D";
    private const string VfxName = "Vfx3D";

    [SerializeField]
    private BoardManager boardManager;

    [SerializeField]
    [Min(0.05f)]
    [Tooltip("World-space edge length of each cell tile.")]
    private float cellWorldSize = 1f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Gap between adjacent cell tiles.")]
    private float cellGap = 0.08f;

    [SerializeField]
    [Min(0.05f)]
    private float boardThickness = 0.28f;

    [SerializeField]
    [Min(0f)]
    private float framePadding = 0.22f;

    [SerializeField]
    [Min(0.02f)]
    private float frameWallThickness = 0.14f;

    [SerializeField]
    [Range(0f, 0.2f)]
    [Tooltip("How far cell tops sit below the frame lip.")]
    private float cellRecess = 0.06f;

    [SerializeField]
    [Min(0f)]
    private float boardCornerRadius = 0.28f;

    [SerializeField]
    private GameObject cellPrefab;

    [SerializeField]
    private Material boardMaterial;

    [SerializeField]
    private Material cellMaterial;

    [SerializeField]
    private Material frameMaterial;

    private readonly GridSpace3D gridSpace = new GridSpace3D();
    private Transform surfaceRoot;
    private Transform cellsRoot;
    private Transform frameRoot;
    private Transform piecesRoot;
    private Transform nestsRoot;
    private Transform iceRoot;
    private Transform shuttersRoot;
    private Transform vfxRoot;
    private int builtWidth;
    private int builtHeight;
    private float builtCellSize;
    private float builtGap;

    public IGridSpace GridSpace => gridSpace;
    public GridSpace3D GridSpace3D => gridSpace;
    public float CellWorldSize => cellWorldSize;
    public float CellGap => cellGap;
    public int BuiltWidth => builtWidth;
    public int BuiltHeight => builtHeight;

    /// <summary>Parent for world-space piece views (not Canvas).</summary>
    public Transform PiecesRoot
    {
        get
        {
            EnsureHierarchy();
            return piecesRoot;
        }
    }

    /// <summary>Parent for world-space nest/target views.</summary>
    public Transform NestsRoot
    {
        get
        {
            EnsureHierarchy();
            return nestsRoot;
        }
    }

    /// <summary>Parent for world-space ice covers.</summary>
    public Transform IceRoot
    {
        get
        {
            EnsureHierarchy();
            return iceRoot;
        }
    }

    /// <summary>Parent for world-space shutter plates.</summary>
    public Transform ShuttersRoot
    {
        get
        {
            EnsureHierarchy();
            return shuttersRoot;
        }
    }

    /// <summary>Parent for ephemeral World3D VFX (particles/rings).</summary>
    public Transform VfxRoot
    {
        get
        {
            EnsureHierarchy();
            BoardVfx3D.SetEffectsRoot(vfxRoot);
            return vfxRoot;
        }
    }

    /// <summary>World Y of the playable cell top surface.</summary>
    public float CellSurfaceWorldY
    {
        get
        {
            Vector3 local = new Vector3(0f, gridSpace.SurfaceLocalY, 0f);
            return transform.TransformPoint(local).y;
        }
    }

    public Vector3 BoardCenterWorld => transform.TransformPoint(new Vector3(0f, boardThickness * 0.5f, 0f));

    public Vector2 BoardFootprint
    {
        get
        {
            Vector2 grid = gridSpace.GridFootprint;
            float pad = framePadding * 2f + frameWallThickness * 2f;
            return new Vector2(grid.x + pad, grid.y + pad);
        }
    }

    private void Awake()
    {
        EnsureHierarchy();
        gridSpace.Bind(transform);
        TryResolveBoardManager();
        SyncFromBoardManager(force: true);
    }

    private void OnEnable()
    {
        EnsureHierarchy();
        gridSpace.Bind(transform);
        TryResolveBoardManager();
        SyncFromBoardManager(force: true);
    }

    private void Update()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        SyncFromBoardManager(force: false);
    }

    private void OnValidate()
    {
        cellWorldSize = Mathf.Max(0.05f, cellWorldSize);
        cellGap = Mathf.Max(0f, cellGap);
        boardThickness = Mathf.Max(0.05f, boardThickness);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled)
                {
                    return;
                }

                builtWidth = 0;
                SyncFromBoardManager(force: true);
            };
            return;
        }
#endif
        if (isActiveAndEnabled)
        {
            builtWidth = 0;
            SyncFromBoardManager(force: true);
        }
    }

    public void SetBoardManager(BoardManager manager)
    {
        boardManager = manager;
        SyncFromBoardManager(force: true);
    }

    /// <summary>
    /// Phase 13 art proportions as ratios of presentation cell size (Phase 14 scales them).
    /// </summary>
    public void ApplyArtDirectionDefaults()
    {
        // Ratios only — actual meters come from ApplyPresentationScale.
        if (cellWorldSize < 0.05f)
        {
            cellWorldSize = BoardAdaptivePresentation3D.ReferenceCellSize;
        }
    }

    /// <summary>
    /// Sets runtime presentation cell size and proportional board chrome. Rebuilds meshes.
    /// Logical grid width/height are unchanged.
    /// </summary>
    public void ApplyPresentationScale(float presentationCellSize)
    {
        presentationCellSize = Mathf.Max(0.05f, presentationCellSize);
        cellWorldSize = presentationCellSize;
        cellGap = presentationCellSize * BoardAdaptivePresentation3D.GapRatio;
        boardThickness = presentationCellSize * BoardAdaptivePresentation3D.ThicknessRatio;
        framePadding = presentationCellSize * BoardAdaptivePresentation3D.FramePadRatio;
        frameWallThickness = presentationCellSize * BoardAdaptivePresentation3D.FrameWallRatio;
        cellRecess = presentationCellSize * BoardAdaptivePresentation3D.RecessRatio;
        boardCornerRadius = presentationCellSize * BoardAdaptivePresentation3D.CornerRadiusRatio;
        builtWidth = 0;
        SyncFromBoardManager(force: true);
    }

    public void Rebuild(int gridWidth, int gridHeight)
    {
        EnsureHierarchy();
        gridSpace.Bind(transform);

        gridWidth = Mathf.Max(1, gridWidth);
        gridHeight = Mathf.Max(1, gridHeight);

        float surfaceY = boardThickness - cellRecess;
        gridSpace.Configure(gridWidth, gridHeight, cellWorldSize, cellGap, surfaceY);

        RebuildSurface();
        RebuildFrame();
        RebuildCells(gridWidth, gridHeight);

        builtWidth = gridWidth;
        builtHeight = gridHeight;
        builtCellSize = cellWorldSize;
        builtGap = cellGap;
    }

    private void SyncFromBoardManager(bool force)
    {
        TryResolveBoardManager();
        int width = boardManager != null ? Mathf.Max(1, boardManager.Width) : Mathf.Max(1, builtWidth);
        int height = boardManager != null ? Mathf.Max(1, boardManager.Height) : Mathf.Max(1, builtHeight);
        if (width <= 0)
        {
            width = 5;
        }

        if (height <= 0)
        {
            height = 5;
        }

        bool dirty = force
            || width != builtWidth
            || height != builtHeight
            || !Mathf.Approximately(cellWorldSize, builtCellSize)
            || !Mathf.Approximately(cellGap, builtGap);

        if (dirty)
        {
            Rebuild(width, height);
        }
    }

    private void TryResolveBoardManager()
    {
        if (boardManager == null)
        {
            boardManager = FindFirstObjectByType<BoardManager>();
        }
    }

    private void EnsureHierarchy()
    {
        surfaceRoot = EnsureChild(SurfaceName);
        cellsRoot = EnsureChild(CellsName);
        frameRoot = EnsureChild(FrameName);
        piecesRoot = EnsureChild(PiecesName);
        nestsRoot = EnsureChild(NestsName);
        iceRoot = EnsureChild(IceName);
        shuttersRoot = EnsureChild(ShuttersName);
        vfxRoot = EnsureChild(VfxName);
        BoardVfx3D.SetEffectsRoot(vfxRoot);
    }

    private Transform EnsureChild(string childName)
    {
        Transform existing = transform.Find(childName);
        if (existing != null)
        {
            return existing;
        }

        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    private void RebuildSurface()
    {
        ClearChildren(surfaceRoot);
        Vector2 footprint = gridSpace.GridFootprint;
        float sizeX = footprint.x + framePadding * 2f;
        float sizeZ = footprint.y + framePadding * 2f;
        // Floor only — kept below cell tops so recessed tiles remain visible.
        float floorHeight = Mathf.Max(0.08f, boardThickness - cellRecess - 0.02f);

        if (ShapeNestVisualCatalog3D.TryGetBoardSurfacePrefab(out GameObject surfacePrefab))
        {
            GameObject instance = Instantiate(surfacePrefab);
            instance.name = "Slab";
            instance.transform.SetParent(surfaceRoot, false);
            instance.transform.localPosition = new Vector3(0f, floorHeight * 0.5f, 0f);
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = new Vector3(sizeX, floorHeight, sizeZ);
            return;
        }

        GameObject slab = new GameObject("Slab");
        slab.transform.SetParent(surfaceRoot, false);
        slab.transform.localPosition = new Vector3(0f, floorHeight * 0.5f, 0f);
        var filter = slab.AddComponent<MeshFilter>();
        filter.sharedMesh = BoardMeshFactory3D.GetRoundedBox(sizeX, floorHeight, sizeZ, boardCornerRadius, 4);
        slab.AddComponent<MeshRenderer>();
        ApplyMaterial(slab, boardMaterial, new Color(0.10f, 0.09f, 0.26f, 1f));
        TuneSharedMaterial(boardMaterial, new Color(0.10f, 0.09f, 0.26f, 1f), 0.05f, 0.45f);
    }

    private void RebuildFrame()
    {
        ClearChildren(frameRoot);
        Vector2 footprint = gridSpace.GridFootprint;
        float innerX = footprint.x + framePadding * 2f;
        float innerZ = footprint.y + framePadding * 2f;
        float outerX = innerX + frameWallThickness * 2f;
        float outerZ = innerZ + frameWallThickness * 2f;
        float wallHeight = boardThickness + 0.1f;
        float y = wallHeight * 0.5f;

        if (ShapeNestVisualCatalog3D.TryGetBoardFramePrefab(out GameObject framePrefab))
        {
            GameObject instance = Instantiate(framePrefab);
            instance.name = "DesignerFrame";
            instance.transform.SetParent(frameRoot, false);
            instance.transform.localPosition = new Vector3(0f, y, 0f);
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = new Vector3(outerX, wallHeight, outerZ);
            return;
        }

        float zEdge = (outerZ * 0.5f) - (frameWallThickness * 0.5f);
        float xEdge = (outerX * 0.5f) - (frameWallThickness * 0.5f);
        float corner = Mathf.Min(boardCornerRadius * 0.55f, frameWallThickness * 0.9f);

        CreateFrameWall("FrameNorth", new Vector3(0f, y, zEdge), new Vector3(outerX, wallHeight, frameWallThickness), corner);
        CreateFrameWall("FrameSouth", new Vector3(0f, y, -zEdge), new Vector3(outerX, wallHeight, frameWallThickness), corner);
        CreateFrameWall("FrameEast", new Vector3(xEdge, y, 0f), new Vector3(frameWallThickness, wallHeight, innerZ), corner);
        CreateFrameWall("FrameWest", new Vector3(-xEdge, y, 0f), new Vector3(frameWallThickness, wallHeight, innerZ), corner);
        TuneSharedMaterial(frameMaterial, new Color(0.18f, 0.13f, 0.38f, 1f), 0.06f, 0.62f);
    }

    private void CreateFrameWall(string wallName, Vector3 localPos, Vector3 localScale, float cornerRadius)
    {
        GameObject wall = new GameObject(wallName);
        wall.transform.SetParent(frameRoot, false);
        wall.transform.localPosition = localPos;
        var filter = wall.AddComponent<MeshFilter>();
        filter.sharedMesh = BoardMeshFactory3D.GetRoundedBox(
            localScale.x,
            localScale.y,
            localScale.z,
            Mathf.Min(cornerRadius, Mathf.Min(localScale.x, localScale.z) * 0.35f),
            3);
        wall.AddComponent<MeshRenderer>();
        ApplyMaterial(wall, frameMaterial, new Color(0.18f, 0.13f, 0.38f, 1f));
    }

    private void RebuildCells(int gridWidth, int gridHeight)
    {
        ClearChildren(cellsRoot);
        float tileThickness = Mathf.Max(0.06f, cellRecess + 0.04f);
        float tileFace = cellWorldSize * 0.88f;
        float floorTop = Mathf.Max(0.08f, boardThickness - cellRecess - 0.02f);
        float cellCenterY = floorTop + tileThickness * 0.5f;

        // Keep GridSpace surface at cell top centers for future piece placement.
        gridSpace.Configure(gridWidth, gridHeight, cellWorldSize, cellGap, floorTop + tileThickness);

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Vector3 center = gridSpace.GridToLocal(cell);
                GameObject tile = CreateCellTile(cell, tileFace, tileThickness, out bool keepDesignerMaterials);
                tile.transform.SetParent(cellsRoot, false);
                tile.transform.localPosition = new Vector3(center.x, cellCenterY, center.z);
                tile.transform.localRotation = Quaternion.identity;
                tile.transform.localScale = Vector3.one;
                if (!keepDesignerMaterials)
                {
                    ApplyMaterial(tile, cellMaterial, new Color(0.24f, 0.20f, 0.48f, 1f));
                }
            }
        }

        TuneSharedMaterial(cellMaterial, new Color(0.24f, 0.20f, 0.48f, 1f), 0.04f, 0.5f);
    }

    private GameObject CreateCellTile(Vector2Int cell, float face, float thickness, out bool keepDesignerMaterials)
    {
        keepDesignerMaterials = false;
        GameObject prefab = cellPrefab;
        if (prefab == null)
        {
            ShapeNestVisualCatalog3D.TryGetCellPrefab(out prefab);
            keepDesignerMaterials = prefab != null;
        }

        if (prefab != null)
        {
            GameObject instance = Instantiate(prefab);
            instance.name = $"Cell_{cell.x}_{cell.y}";
            instance.transform.localScale = new Vector3(face, thickness, face);
            return instance;
        }

        GameObject tile = new GameObject($"Cell_{cell.x}_{cell.y}");
        var filter = tile.AddComponent<MeshFilter>();
        filter.sharedMesh = BoardMeshFactory3D.GetCellTile(face, thickness, face, face * 0.12f);
        tile.AddComponent<MeshRenderer>();
        return tile;
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private static void ApplyMaterial(GameObject target, Material material, Color fallbackColor)
    {
        var renderer = target.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            return;
        }

        if (material != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return;
        }

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard"));
        mat.color = fallbackColor;
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", fallbackColor);
        }

        if (mat.HasProperty("_Smoothness"))
        {
            mat.SetFloat("_Smoothness", 0.4f);
        }

        if (mat.HasProperty("_Metallic"))
        {
            mat.SetFloat("_Metallic", 0.02f);
        }

        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    private static void TuneSharedMaterial(Material material, Color color, float metallic, float smoothness)
    {
        if (material == null)
        {
            return;
        }

        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }
    }
}
