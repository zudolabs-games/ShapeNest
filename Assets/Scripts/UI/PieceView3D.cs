using DG.Tweening;
using UnityEngine;

/// <summary>
/// World-space <see cref="IPieceView"/> for 3D piece/nest presentation.
/// Does not own gameplay state; position comes from <see cref="IGridSpace"/>.
/// Pick colliders exist only for World3D input (Phase 7).
/// </summary>
[DisallowMultipleComponent]
public class PieceView3D : MonoBehaviour, IPieceView
{
    [SerializeField]
    private Transform visualRoot;

    [SerializeField]
    [Tooltip("Raises the piece so its bottom sits on the board cell surface.")]
    private float surfaceLift = 0.03f;

    [SerializeField]
    private float pieceHeight = 0.26f;

    [SerializeField]
    private MeshFilter meshFilter;

    [SerializeField]
    private MeshRenderer meshRenderer;

    [SerializeField]
    private BoxCollider pickCollider;

    private Vector3 restScale = Vector3.one;
    private bool hasRestScale;
    private ShapeType configuredShape = ShapeType.Square;
    private bool configuredAsNest;
    private ShapeType configuredInnerShape = ShapeType.Square;
    private bool hasNestedInner;
    private Transform nestedInnerRoot;
    private MeshFilter nestedInnerFilter;
    private MeshRenderer nestedInnerRenderer;
    private Block sourceBlock;
    private Vector3 configuredFootprintScale = Vector3.one;
    private int motionLockCount;
    private float presentationLift;
    private float carryMeshScale = 1f;
    private float presentationSquash;
    private Transform contactShadow;
    private MeshRenderer contactShadowRenderer;
    private static Material sharedContactShadowMaterial;
    private const string DesignerVisualName = "DesignerVisual";
    private const string DesignerInnerName = "DesignerInner";
    private GameObject designerVisualInstance;
    private GameObject designerVisualPrefab;
    private MeshRenderer designerVisualRenderer;
    private GameObject designerInnerInstance;
    private GameObject designerInnerPrefab;

    public float PieceHeight => pieceHeight;
    public float SurfaceLift => surfaceLift;
    public ShapeType ConfiguredShape => configuredShape;
    public bool ConfiguredAsNest => configuredAsNest;
    public bool HasNestedInner => hasNestedInner && nestedInnerRoot != null && nestedInnerRoot.gameObject.activeSelf;
    public ShapeType ConfiguredInnerShape => configuredInnerShape;
    public Vector3 ConfiguredFootprintScale => configuredFootprintScale;
    public bool IsMotionLocked => motionLockCount > 0;
    public float CarryMeshScale => carryMeshScale;

    public float PresentationSquash => presentationSquash;

    /// <summary>
    /// Extra world-Y above rest seating while the piece is carried. Presentation only.
    /// </summary>
    public float PresentationLift
    {
        get
        {
            if (!PieceMotionMath.IsFinite(presentationLift))
            {
                return 0f;
            }

            return Mathf.Max(0f, presentationLift);
        }
    }

    public MeshRenderer OuterMeshRenderer
    {
        get
        {
            EnsureMeshComponents();
            if (designerVisualRenderer != null && designerVisualRenderer.enabled)
            {
                return designerVisualRenderer;
            }

            return meshRenderer;
        }
    }

    /// <summary>True when a mesh is assigned and the MeshRenderer can draw.</summary>
    public bool HasRenderableMesh
    {
        get
        {
            EnsureMeshComponents();
            if (designerVisualInstance != null && designerVisualInstance.activeSelf)
            {
                return true;
            }

            return meshFilter != null
                && meshFilter.sharedMesh != null
                && meshRenderer != null
                && meshRenderer.enabled
                && meshRenderer.gameObject.activeSelf;
        }
    }

    /// <summary>True when local scale is a usable non-zero presentation footprint.</summary>
    public bool HasValidPresentationScale =>
        transform.localScale.x > 0.0001f
        && transform.localScale.y > 0.0001f
        && transform.localScale.z > 0.0001f;

    /// <summary>Gameplay block this view presents. Null for nests / unbound views.</summary>
    public Block SourceBlock => sourceBlock;

    /// <summary>True when this view can be selected by World3D input.</summary>
    public bool IsSelectable => !configuredAsNest && sourceBlock != null;

    public Vector3 LocalScale
    {
        get => transform.localScale;
        set => transform.localScale = value;
    }

    private void Awake()
    {
        EnsureMeshComponents();
        CaptureRestScale();
    }

    private void OnValidate()
    {
        EnsureMeshComponents();
        pieceHeight = Mathf.Max(0.01f, pieceHeight);
        surfaceLift = Mathf.Max(0f, surfaceLift);
    }

    private void OnDisable()
    {
        ClearCarryPresentation(applyToTransform: false);
    }

    private void OnDestroy()
    {
        ClearCarryPresentation(applyToTransform: false);
    }

    public void ApplyGridPosition(IGridSpace gridSpace, Vector2Int gridPosition)
    {
        if (gridSpace == null || IsMotionLocked)
        {
            return;
        }

        Vector3 world = gridSpace.GridToWorld(gridPosition);
        float halfHeight = Mathf.Abs(transform.lossyScale.y) * 0.5f;
        world.y += surfaceLift + halfHeight + PresentationLift;
        if (!PieceMotionMath.IsFinite(world))
        {
            world = gridSpace.GridToWorld(gridPosition);
            world.y += surfaceLift + halfHeight;
        }

        if (PieceMotionMath.IsFinite(world))
        {
            transform.position = world;
        }

        RefreshContactShadow();
    }

    /// <summary>
    /// Sets carry height (world Y extra) and mesh-only scale. Does not move the transform.
    /// Mesh scale is independent of root held/selection scale. Clears squash.
    /// </summary>
    public void SetPresentationLift(float lift, float visualScaleMul = 1f)
    {
        SetPresentationAnticipation(lift, visualScaleMul, 0f);
    }

    /// <summary>
    /// Presentation-only lift, uniform mesh scale, and squash on the mesh root.
    /// Squash compresses Y and widens XZ. Does not write GridPosition or occupancy.
    /// </summary>
    public void SetPresentationAnticipation(float lift, float visualScaleMul, float squash)
    {
        if (!PieceMotionMath.IsFinite(lift))
        {
            lift = 0f;
        }

        presentationLift = Mathf.Max(0f, lift);
        presentationSquash = Mathf.Clamp01(squash);
        ApplyCarryVisualScale(visualScaleMul);
        RefreshContactShadow();
    }

    /// <summary>
    /// Copies carry lift/scale/shadow from the primary so chain extras stay visually carried.
    /// Does not write world position.
    /// </summary>
    public void MatchCarryPresentation(PieceView3D primary)
    {
        if (primary == null || primary == this)
        {
            return;
        }

        SetPresentationAnticipation(
            primary.PresentationLift,
            primary.CarryMeshScale,
            primary.PresentationSquash);
    }

    /// <summary>
    /// Zeros carry height and mesh scale. Optionally subtracts remaining lift from world Y.
    /// </summary>
    public void ClearCarryPresentation(bool applyToTransform)
    {
        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.CarryId, false);
        float lift = PresentationLift;
        presentationLift = 0f;
        presentationSquash = 0f;
        ApplyCarryVisualScale(1f);
        if (applyToTransform && Mathf.Abs(lift) >= 0.00001f)
        {
            Vector3 world = transform.position;
            world.y -= lift;
            if (PieceMotionMath.IsFinite(world))
            {
                transform.position = world;
            }
        }

        RefreshContactShadow();
    }

    private void ApplyCarryVisualScale(float visualScaleMul)
    {
        if (visualRoot == null)
        {
            EnsureMeshComponents();
        }

        if (visualRoot == null)
        {
            return;
        }

        if (!PieceMotionMath.IsFinite(visualScaleMul) || visualScaleMul < 0.45f || visualScaleMul > 1.35f)
        {
            visualScaleMul = 1f;
        }

        carryMeshScale = visualScaleMul;
        float squash = Mathf.Clamp01(presentationSquash);
        float xz = visualScaleMul * (1f + (0.14f * squash));
        float y = visualScaleMul * (1f - (0.24f * squash));
        Vector3 meshScale = new Vector3(xz, y, xz);
        if (PieceMotionMath.IsFinite(meshScale))
        {
            visualRoot.localScale = meshScale;
        }
    }

    private void RefreshContactShadow()
    {
        EnsureContactShadow();
        if (contactShadow == null)
        {
            return;
        }

        bool show = !configuredAsNest && isActiveAndEnabled;
        if (contactShadow.gameObject.activeSelf != show)
        {
            contactShadow.gameObject.SetActive(show);
        }

        if (!show)
        {
            return;
        }

        if (contactShadowRenderer != null)
        {
            contactShadowRenderer.enabled = true;
        }

        float lift = PresentationLift;
        float parentY = Mathf.Abs(transform.localScale.y);
        if (!PieceMotionMath.IsFinite(parentY) || parentY < 0.0001f)
        {
            parentY = 1f;
        }

        float drop = 0.5f + ((surfaceLift + lift) / parentY);
        if (!PieceMotionMath.IsFinite(drop))
        {
            drop = 0.5f;
        }

        Vector3 local = new Vector3(0f, -drop, 0f);
        if (PieceMotionMath.IsFinite(local))
        {
            contactShadow.localPosition = local;
        }

        float blend = 0f;
        float height = Mathf.Max(pieceHeight, configuredFootprintScale.y);
        if (height > 0.0001f)
        {
            blend = Mathf.Clamp01(lift / Mathf.Max(0.0001f, height * 0.34f));
        }

        float radius = 0.46f * (1f + (0.42f * blend));
        Vector3 shadowScale = new Vector3(radius, 0.02f, radius);
        if (PieceMotionMath.IsFinite(shadowScale))
        {
            contactShadow.localScale = shadowScale;
        }
    }

    private void EnsureContactShadow()
    {
        if (contactShadow != null)
        {
            return;
        }

        Transform existing = transform.Find("ContactShadow3D");
        if (existing != null)
        {
            contactShadow = existing;
        }
        else
        {
            var go = new GameObject("ContactShadow3D");
            go.transform.SetParent(transform, false);
            contactShadow = go.transform;
        }

        var filter = contactShadow.GetComponent<MeshFilter>();
        if (filter == null)
        {
            filter = contactShadow.gameObject.AddComponent<MeshFilter>();
        }

        filter.sharedMesh = BoardMeshFactory3D.GetShadowDisc(28);
        contactShadowRenderer = contactShadow.GetComponent<MeshRenderer>();
        if (contactShadowRenderer == null)
        {
            contactShadowRenderer = contactShadow.gameObject.AddComponent<MeshRenderer>();
        }

        contactShadowRenderer.sharedMaterial = GetContactShadowMaterial();
        contactShadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        contactShadowRenderer.receiveShadows = false;
        contactShadow.localRotation = Quaternion.identity;
    }

    private static Material GetContactShadowMaterial()
    {
        if (sharedContactShadowMaterial != null)
        {
            return sharedContactShadowMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        sharedContactShadowMaterial = new Material(shader)
        {
            name = "PieceContactShadow3D_Runtime",
            color = new Color(0.02f, 0.01f, 0.05f, 0.32f)
        };
        if (sharedContactShadowMaterial.HasProperty("_BaseColor"))
        {
            sharedContactShadowMaterial.SetColor("_BaseColor", sharedContactShadowMaterial.color);
        }

        if (sharedContactShadowMaterial.HasProperty("_Metallic"))
        {
            sharedContactShadowMaterial.SetFloat("_Metallic", 0f);
        }

        if (sharedContactShadowMaterial.HasProperty("_Smoothness"))
        {
            sharedContactShadowMaterial.SetFloat("_Smoothness", 0f);
        }

        if (sharedContactShadowMaterial.HasProperty("_Surface"))
        {
            sharedContactShadowMaterial.SetFloat("_Surface", 1f);
            sharedContactShadowMaterial.SetOverrideTag("RenderType", "Transparent");
            sharedContactShadowMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            sharedContactShadowMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            sharedContactShadowMaterial.SetInt("_ZWrite", 0);
            sharedContactShadowMaterial.renderQueue = 3000;
            sharedContactShadowMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        return sharedContactShadowMaterial;
    }

    public void BeginMotionLock()
    {
        motionLockCount++;
    }

    public void EndMotionLock()
    {
        motionLockCount = Mathf.Max(0, motionLockCount - 1);
    }

    public void SetHeld(bool held)
    {
        SetHeldBlend(held ? 1f : 0f);
    }

    public void SetHeldBlend(float blend)
    {
        CaptureRestScale();
        float pump = Mathf.Lerp(1f, 1.08f, Mathf.Clamp01(blend));
        transform.localScale = restScale * pump;
    }

    /// <summary>
    /// Cosmetic landing pulse. Fire-and-forget — does not hold BlockMover.
    /// </summary>
    public void PlayCosmeticLandingPulse()
    {
        if (!isActiveAndEnabled || IsMotionLocked)
        {
            return;
        }

        CaptureRestScale();
        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.VfxId);
        Vector3 peak = restScale * 1.045f;
        const float half = 0.045f;
        Sequence pulse = DOTween.Sequence().SetId(TweenAnimationUtility.VfxId).SetLink(gameObject);
        pulse.Append(transform.DOScale(peak, half).SetEase(Ease.OutQuad));
        pulse.Append(transform.DOScale(restScale, half).SetEase(Ease.InQuad));
        pulse.OnKill(() =>
        {
            if (this != null)
            {
                transform.localScale = restScale;
            }
        });
    }

    public void SetMaterial(Material material)
    {
        EnsureMeshComponents();
        if (meshRenderer != null && material != null)
        {
            meshRenderer.sharedMaterial = material;
        }
    }

    public void SetPieceHeight(float height)
    {
        pieceHeight = Mathf.Max(0.01f, height);
    }

    /// <summary>
    /// Applies shape mesh (solid block or hollow nest) and material.
    /// Footprint scale is XY on XZ plane; Y scale maps extruded unit height to <see cref="pieceHeight"/>.
    /// </summary>
    public void ConfigureVisual(ShapeType shape, Material material, bool asNest, float footprint, float height)
    {
        EnsureMeshComponents();
        configuredShape = shape;
        configuredAsNest = asNest;
        pieceHeight = Mathf.Max(0.01f, height);

        if (ShapeNestVisualCatalog3D.TryGetPiecePrefab(shape, asNest, out GameObject prefab))
        {
            ApplyDesignerVisual(prefab);
        }
        else
        {
            ClearDesignerVisual();
            Mesh mesh = asNest ? ShapeMeshFactory3D.GetNestMesh(shape) : ShapeMeshFactory3D.GetSolidMesh(shape);
            if (meshFilter != null)
            {
                meshFilter.sharedMesh = mesh;
            }

            SetMaterial(material);
        }

        // Blocks sit proudly on the cell; nests sit slightly recessed as destinations.
        surfaceLift = asNest ? -0.02f : 0.025f;

        float size = Mathf.Max(0.01f, footprint);
        transform.localScale = new Vector3(size, pieceHeight, size);
        configuredFootprintScale = transform.localScale;
        hasRestScale = false;
        CaptureRestScale();
        ClearCarryPresentation(applyToTransform: false);
        RefreshPickCollider();
        EnsurePresentationVisible();
    }

    /// <summary>
    /// Presentation-only nested inner mesh. Child of this cell so hop squash and
    /// chain follow move outer+inner together. Does not own gameplay layers.
    /// </summary>
    public void ConfigureNestedInner(bool show, ShapeType innerShape, Material material, float relativeScale, bool asNest)
    {
        EnsureNestedInner();
        if (!show)
        {
            hasNestedInner = false;
            if (nestedInnerRoot != null)
            {
                nestedInnerRoot.gameObject.SetActive(false);
            ClearDesignerInner();
            }

            return;
        }

        hasNestedInner = true;
        configuredInnerShape = innerShape;
        nestedInnerRoot.gameObject.SetActive(true);
        if (ShapeNestVisualCatalog3D.TryGetPiecePrefab(innerShape, asNest, out GameObject prefab))
        {
            ApplyDesignerInner(prefab);
        }
        else
        {
            ClearDesignerInner();
            Mesh mesh = asNest
                ? ShapeMeshFactory3D.GetNestMesh(innerShape)
                : ShapeMeshFactory3D.GetSolidMesh(innerShape);
            if (nestedInnerFilter != null)
            {
                nestedInnerFilter.sharedMesh = mesh;
            }

            if (nestedInnerRenderer != null && material != null)
            {
                nestedInnerRenderer.sharedMaterial = material;
                nestedInnerRenderer.enabled = true;
                nestedInnerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                nestedInnerRenderer.receiveShadows = true;
            }
        }

        float scale = Mathf.Clamp(relativeScale, 0.4f, 0.7f);
        nestedInnerRoot.localScale = new Vector3(scale, scale * 0.72f, scale);
        nestedInnerRoot.localPosition = new Vector3(0f, asNest ? 0.06f : 0.16f, 0f);
        nestedInnerRoot.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Restores baseline visibility after bind/reuse without changing shape or materials.
    /// Does not move the piece — presentation lifecycle only.
    /// </summary>
    public void EnsurePresentationVisible()
    {
        EnsureMeshComponents();

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (visualRoot != null && !visualRoot.gameObject.activeSelf)
        {
            visualRoot.gameObject.SetActive(true);
        }

        if (meshRenderer != null)
        {
            bool showProcedural = designerVisualInstance == null;
            meshRenderer.enabled = showProcedural;
            if (showProcedural)
            {
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                meshRenderer.receiveShadows = true;
            }
        }

        if (!HasValidPresentationScale)
        {
            Vector3 footprint = configuredFootprintScale;
            if (footprint.x <= 0.0001f || footprint.y <= 0.0001f || footprint.z <= 0.0001f)
            {
                footprint = new Vector3(1f, Mathf.Max(0.01f, pieceHeight), 1f);
                configuredFootprintScale = footprint;
            }

            transform.localScale = footprint;
            hasRestScale = false;
            CaptureRestScale();
        }

        if (hasNestedInner && nestedInnerRoot != null)
        {
            nestedInnerRoot.gameObject.SetActive(true);
            if (nestedInnerRenderer != null)
            {
                nestedInnerRenderer.enabled = designerInnerInstance == null;
            }
        }

        RefreshContactShadow();
    }

    public void BindSourceBlock(Block block)
    {
        sourceBlock = block;
        RefreshPickCollider();
    }

    public void ClearSourceBlock()
    {
        sourceBlock = null;
        RefreshPickCollider();
    }

    private void RefreshPickCollider()
    {
        EnsureMeshComponents();
        bool enablePick = IsSelectable;
        if (!enablePick)
        {
            if (pickCollider != null)
            {
                pickCollider.enabled = false;
            }

            return;
        }

        if (pickCollider == null)
        {
            pickCollider = GetComponent<BoxCollider>();
            if (pickCollider == null)
            {
                pickCollider = gameObject.AddComponent<BoxCollider>();
            }
        }

        // Mesh is unit-sized in local space; root scale maps to world footprint/height.
        pickCollider.isTrigger = true;
        pickCollider.center = Vector3.zero;
        pickCollider.size = Vector3.one;
        pickCollider.enabled = true;
    }

    private void EnsureMeshComponents()
    {
        if (visualRoot == null)
        {
            Transform existing = transform.Find("Mesh");
            if (existing != null)
            {
                visualRoot = existing;
            }
            else
            {
                var meshObject = new GameObject("Mesh");
                meshObject.transform.SetParent(transform, false);
                visualRoot = meshObject.transform;
            }
        }

        if (meshFilter == null)
        {
            meshFilter = visualRoot.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = visualRoot.gameObject.AddComponent<MeshFilter>();
            }
        }

        if (meshRenderer == null)
        {
            meshRenderer = visualRoot.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = visualRoot.gameObject.AddComponent<MeshRenderer>();
            }
        }

        // Never put physics colliders on the mesh child — picking lives on the root.
        Collider meshCollider = visualRoot.GetComponent<Collider>();
        if (meshCollider != null)
        {
            if (Application.isPlaying)
            {
                Destroy(meshCollider);
            }
            else
            {
                DestroyImmediate(meshCollider);
            }
        }
    }

    private void EnsureNestedInner()
    {
        if (nestedInnerRoot == null)
        {
            Transform existing = transform.Find("NestedInner3D");
            if (existing != null)
            {
                nestedInnerRoot = existing;
            }
            else
            {
                var innerObject = new GameObject("NestedInner3D");
                innerObject.transform.SetParent(transform, false);
                nestedInnerRoot = innerObject.transform;
            }
        }

        if (nestedInnerFilter == null)
        {
            nestedInnerFilter = nestedInnerRoot.GetComponent<MeshFilter>();
            if (nestedInnerFilter == null)
            {
                nestedInnerFilter = nestedInnerRoot.gameObject.AddComponent<MeshFilter>();
            }
        }

        if (nestedInnerRenderer == null)
        {
            nestedInnerRenderer = nestedInnerRoot.GetComponent<MeshRenderer>();
            if (nestedInnerRenderer == null)
            {
                nestedInnerRenderer = nestedInnerRoot.gameObject.AddComponent<MeshRenderer>();
            }
        }

        Collider innerCollider = nestedInnerRoot.GetComponent<Collider>();
        if (innerCollider != null)
        {
            if (Application.isPlaying)
            {
                Destroy(innerCollider);
            }
            else
            {
                DestroyImmediate(innerCollider);
            }
        }
    }

    private void ApplyDesignerVisual(GameObject prefab)
    {
        EnsureMeshComponents();
        if (prefab == designerVisualPrefab && designerVisualInstance != null)
        {
            SetProceduralMeshVisible(false);
            return;
        }

        ClearDesignerVisual();
        designerVisualPrefab = prefab;
        designerVisualInstance = Instantiate(prefab, visualRoot, false);
        designerVisualInstance.name = DesignerVisualName;
        designerVisualInstance.transform.localPosition = Vector3.zero;
        designerVisualInstance.transform.localRotation = Quaternion.identity;
        designerVisualInstance.transform.localScale = Vector3.one;
        StripColliders(designerVisualInstance);
        designerVisualRenderer = designerVisualInstance.GetComponentInChildren<MeshRenderer>(true);
        SetProceduralMeshVisible(false);
    }

    private void ClearDesignerVisual()
    {
        if (designerVisualInstance != null)
        {
            DestroyVisualObject(designerVisualInstance);
        }

        designerVisualInstance = null;
        designerVisualPrefab = null;
        designerVisualRenderer = null;
        SetProceduralMeshVisible(true);
    }

    private void ApplyDesignerInner(GameObject prefab)
    {
        EnsureNestedInner();
        if (prefab == designerInnerPrefab && designerInnerInstance != null)
        {
            if (nestedInnerRenderer != null)
            {
                nestedInnerRenderer.enabled = false;
            }

            return;
        }

        ClearDesignerInner();
        designerInnerPrefab = prefab;
        designerInnerInstance = Instantiate(prefab, nestedInnerRoot, false);
        designerInnerInstance.name = DesignerInnerName;
        designerInnerInstance.transform.localPosition = Vector3.zero;
        designerInnerInstance.transform.localRotation = Quaternion.identity;
        designerInnerInstance.transform.localScale = Vector3.one;
        StripColliders(designerInnerInstance);
        if (nestedInnerRenderer != null)
        {
            nestedInnerRenderer.enabled = false;
        }
    }

    private void ClearDesignerInner()
    {
        if (designerInnerInstance != null)
        {
            DestroyVisualObject(designerInnerInstance);
        }

        designerInnerInstance = null;
        designerInnerPrefab = null;
        if (nestedInnerRenderer != null)
        {
            nestedInnerRenderer.enabled = true;
        }
    }

    private void SetProceduralMeshVisible(bool visible)
    {
        if (meshRenderer == null)
        {
            return;
        }

        meshRenderer.enabled = visible;
        if (visible)
        {
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
        }
    }

    private static void StripColliders(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(colliders[i]);
            }
            else
            {
                DestroyImmediate(colliders[i]);
            }
        }
    }

    private static void DestroyVisualObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void CaptureRestScale()
    {
        if (hasRestScale)
        {
            return;
        }

        restScale = transform.localScale;
        if (restScale.sqrMagnitude < 0.0001f)
        {
            restScale = Vector3.one;
        }

        hasRestScale = true;
    }
}
