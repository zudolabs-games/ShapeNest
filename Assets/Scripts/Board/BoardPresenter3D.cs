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
    private const string ObstaclesName = "Obstacles3D";
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
    private Transform obstaclesRoot;
    private Transform vfxRoot;
    private int builtWidth;
    private int builtHeight;
    private float builtCellSize;
    private float builtGap;

    public static readonly Vector3 DefaultPresentationRotationEuler = new Vector3(0f, 0f, 0f);

    public IGridSpace GridSpace => gridSpace;
    public GridSpace3D GridSpace3D => gridSpace;
    public float CellWorldSize => cellWorldSize;
    public float CellGap => cellGap;
    public float BoardThickness => boardThickness;
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

    /// <summary>Parent for permanent static obstacle visuals.</summary>
    public Transform ObstaclesRoot
    {
        get
        {
            EnsureHierarchy();
            return obstaclesRoot;
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
        transform.localRotation = Quaternion.Euler(DefaultPresentationRotationEuler);
        EnsureHierarchy();
        gridSpace.Bind(transform);
        TryResolveBoardManager();
        SyncFromBoardManager(force: true);
    }

    private void OnEnable()
    {
        transform.localRotation = Quaternion.Euler(DefaultPresentationRotationEuler);
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
        transform.localRotation = Quaternion.Euler(DefaultPresentationRotationEuler);
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

    /// <summary>
    /// Phase 3J: Renders an isolated single socket prototype with surrounding molded tray shelf
    /// and chunky outer casing under the locked gameplay camera.
    /// </summary>
    public void BuildSingleSocketPrototype(
        float traySize = 4.80f,
        float openSize = 2.40f,
        float socketDepth = 0.28f,
        float openRadius = 0.55f)
    {
        EnsureHierarchy();
        ClearChildren(surfaceRoot);
        ClearChildren(cellsRoot);
        ClearChildren(frameRoot);

        gridSpace.Bind(transform);
        gridSpace.Configure(1, 1, openSize, 0f, 0f);

        GameObject proto = new GameObject("SingleSocketPrototype");
        proto.transform.SetParent(surfaceRoot, false);
        proto.transform.localPosition = Vector3.zero;
        proto.transform.localRotation = Quaternion.identity;
        proto.transform.localScale = Vector3.one;

        var filter = proto.AddComponent<MeshFilter>();
        filter.sharedMesh = BoardMeshFactory3D.GetSingleSocketPrototype(
            traySize,
            openSize,
            socketDepth,
            openRadius,
            0.45f,
            0.12f,
            0.45f);

        var renderer = proto.AddComponent<MeshRenderer>();
        var matFrame = boardMaterial != null ? boardMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        var matCavity = cellMaterial != null ? cellMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

        Color trayColor = new Color(0.28f, 0.23f, 0.55f, 1f);
        Color pocketColor = new Color(0.18f, 0.14f, 0.38f, 1f);

        TuneSharedMaterial(matFrame, trayColor, 0.02f, 0.60f);
        TuneSharedMaterial(matCavity, pocketColor, 0.02f, 0.45f);

        renderer.sharedMaterials = new Material[] { matFrame, matCavity };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
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
        obstaclesRoot = EnsureChild(ObstaclesName);
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
        if (surfaceRoot != null)
        {
            surfaceRoot.gameObject.SetActive(false);
        }
    }

    private void RebuildFrame()
    {
        ClearChildren(frameRoot);
        Vector2 footprint = gridSpace.GridFootprint;
        float innerX = footprint.x + framePadding * 2f;
        float innerZ = footprint.y + framePadding * 2f;
        float outerX = innerX + frameWallThickness * 2f;
        float outerZ = innerZ + frameWallThickness * 2f;
        float floorHeight = boardThickness;
        float rimLipHeight = cellWorldSize * 0.35f;
        float totalHeight = boardThickness + rimLipHeight;
        float outerCorner = Mathf.Max(boardCornerRadius, frameWallThickness * 1.5f);
        float innerCorner = Mathf.Max(0.08f, outerCorner - frameWallThickness * 0.8f);
        float rimBevel = Mathf.Min(frameWallThickness * 0.35f, cellWorldSize * 0.12f);

        if (ShapeNestVisualCatalog3D.TryGetBoardFramePrefab(out GameObject framePrefab))
        {
            GameObject instance = Instantiate(framePrefab);
            instance.name = "DesignerFrame";
            instance.transform.SetParent(frameRoot, false);
            instance.transform.localPosition = new Vector3(0f, totalHeight * 0.5f, 0f);
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = new Vector3(outerX, totalHeight, outerZ);
            return;
        }

        GameObject tray = new GameObject("MoldedTray");
        tray.transform.SetParent(frameRoot, false);
        tray.transform.localPosition = Vector3.zero;
        tray.transform.localRotation = Quaternion.identity;
        tray.transform.localScale = Vector3.one;

        var filter = tray.AddComponent<MeshFilter>();
        filter.sharedMesh = BoardMeshFactory3D.GetMoldedBoardTray(
            outerX,
            outerZ,
            innerX,
            innerZ,
            totalHeight,
            floorHeight,
            outerCorner,
            innerCorner,
            rimBevel,
            8);

        var renderer = tray.AddComponent<MeshRenderer>();
        var matFrame = frameMaterial != null ? frameMaterial : new Material(ShapeVisuals3D.GetDefaultLitShader());
        var matFloor = boardMaterial != null ? boardMaterial : new Material(ShapeVisuals3D.GetDefaultLitShader());

        Color trayColor = new Color(0.33f, 0.21f, 0.83f, 1f);
        // Deep midnight purple-black seam floor — creates clean groove contrast between cell tiles
        Color basinSeamColor = new Color(0.06f, 0.04f, 0.18f, 1f);

        TuneSharedMaterial(matFrame, trayColor, 0f, 0.80f);
        TuneSharedMaterial(matFloor, basinSeamColor, 0f, 0.30f);
        renderer.sharedMaterials = new Material[] { matFrame, matFloor };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;

        // Grounding shadow: soft rounded-rect contact shadow hugging molded tray silhouette
        GameObject shadowGo = new GameObject("GroundingShadow");
        shadowGo.transform.SetParent(frameRoot, false);
        shadowGo.transform.localPosition = new Vector3(0.07f, -0.015f, -0.12f);
        shadowGo.transform.localRotation = Quaternion.identity;
        shadowGo.transform.localScale = Vector3.one;

        var shadowFilter = shadowGo.AddComponent<MeshFilter>();
        shadowFilter.sharedMesh = BoardMeshFactory3D.GetSoftBoardContactShadow(
            outerX,
            outerZ,
            outerCorner,
            0.58f);

        var shadowRenderer = shadowGo.AddComponent<MeshRenderer>();
        shadowRenderer.sharedMaterial = GetGroundingShadowMaterial();
        shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shadowRenderer.receiveShadows = false;
    }

    private static Material sharedGroundingShadowMaterial;

    private static Material GetGroundingShadowMaterial()
    {
        if (sharedGroundingShadowMaterial != null)
        {
            return sharedGroundingShadowMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
            ?? Shader.Find("UI/Default")
            ?? Shader.Find("Universal Render Pipeline/Unlit");

        Color shadowTint = new Color(0.025f, 0.015f, 0.08f, 0.60f);
        sharedGroundingShadowMaterial = new Material(shader)
        {
            name = "BoardGroundingShadow_Runtime",
            color = shadowTint
        };
        if (sharedGroundingShadowMaterial.HasProperty("_Color"))
        {
            sharedGroundingShadowMaterial.SetColor("_Color", shadowTint);
        }
        if (sharedGroundingShadowMaterial.HasProperty("_BaseColor"))
        {
            sharedGroundingShadowMaterial.SetColor("_BaseColor", shadowTint);
        }

        return sharedGroundingShadowMaterial;
    }

    private void RebuildCells(int gridWidth, int gridHeight)
    {
        ClearChildren(cellsRoot);
        float cellTopY = boardThickness;

        // Keep GridSpace surface at playbed top for piece placement.
        gridSpace.Configure(gridWidth, gridHeight, cellWorldSize, cellGap, cellTopY);

        // Pass B: cell tile color — lighter than basin seam for 3-level value contrast
        // purple rim (bright) → cell pad (mid-navy, specular) → seam groove (very dark)
        Color playbedTileColor = new Color(0.14f, 0.12f, 0.34f, 1f);

        var matTile = cellMaterial != null ? cellMaterial : new Material(ShapeVisuals3D.GetDefaultLitShader());
        TuneSharedMaterial(matTile, playbedTileColor, 0f, 0.65f);

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Vector3 center = gridSpace.GridToLocal(cell);

                GameObject tile = CreateCellTile(cell, cellWorldSize, cellGap, out bool keepDesignerMaterials);
                tile.transform.SetParent(cellsRoot, false);
                tile.transform.localPosition = new Vector3(center.x, cellTopY, center.z);
                tile.transform.localRotation = Quaternion.identity;
                tile.transform.localScale = Vector3.one;
                if (!keepDesignerMaterials)
                {
                    var renderer = tile.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = matTile;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                        renderer.receiveShadows = true;
                    }
                }
            }
        }
    }

    private GameObject CreateMoldedSocketPrototypeTile(Vector2Int cell, float cellSize, float recessDepth)
    {
        GameObject tile = new GameObject($"Cell_Proto_{cell.x}_{cell.y}");
        var filter = tile.AddComponent<MeshFilter>();
        filter.sharedMesh = BoardMeshFactory3D.GetMoldedPlaybedTile(cellSize, cellGap);
        var renderer = tile.AddComponent<MeshRenderer>();

        var matTile = cellMaterial != null ? cellMaterial : new Material(ShapeVisuals3D.GetDefaultLitShader());
        TuneSharedMaterial(matTile, new Color(0.14f, 0.12f, 0.34f, 1f), 0f, 0.65f);

        renderer.sharedMaterial = matTile;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return tile;
    }

    private GameObject CreateCellTile(Vector2Int cell, float cellSize, float gap, out bool keepDesignerMaterials)
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
            instance.transform.localScale = new Vector3(cellSize, 0.05f, cellSize);
            return instance;
        }

        GameObject tile = new GameObject($"Cell_{cell.x}_{cell.y}");
        var filter = tile.AddComponent<MeshFilter>();
        filter.sharedMesh = BoardMeshFactory3D.GetMoldedPlaybedTile(cellSize, gap);
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
            if (child == null)
            {
                continue;
            }

            child.gameObject.SetActive(false);
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

        var mat = new Material(ShapeVisuals3D.GetDefaultLitShader());
        mat.color = fallbackColor;
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", fallbackColor);
        }

        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", fallbackColor);
        }

        if (mat.HasProperty("_Smoothness"))
        {
            mat.SetFloat("_Smoothness", 0.4f);
        }

        if (mat.HasProperty("_Glossiness"))
        {
            mat.SetFloat("_Glossiness", 0.4f);
        }

        if (mat.HasProperty("_Metallic"))
        {
            mat.SetFloat("_Metallic", 0.02f);
        }

        if (mat.HasProperty("_SpecularHighlights"))
        {
            mat.SetFloat("_SpecularHighlights", 1f);
        }

        if (mat.HasProperty("_GlossyReflections"))
        {
            mat.SetFloat("_GlossyReflections", 1f);
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

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }

        if (material.HasProperty("_Glossiness"))
        {
            material.SetFloat("_Glossiness", smoothness);
        }

        if (material.HasProperty("_SpecularHighlights"))
        {
            material.SetFloat("_SpecularHighlights", 1f);
        }

        if (material.HasProperty("_GlossyReflections"))
        {
            material.SetFloat("_GlossyReflections", 1f);
        }
    }
}
