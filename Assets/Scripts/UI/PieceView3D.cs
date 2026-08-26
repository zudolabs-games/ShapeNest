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

    public float PieceHeight => pieceHeight;
    public float SurfaceLift => surfaceLift;
    public ShapeType ConfiguredShape => configuredShape;
    public bool ConfiguredAsNest => configuredAsNest;
    public bool HasNestedInner => hasNestedInner && nestedInnerRoot != null && nestedInnerRoot.gameObject.activeSelf;
    public ShapeType ConfiguredInnerShape => configuredInnerShape;
    public Vector3 ConfiguredFootprintScale => configuredFootprintScale;
    public bool IsMotionLocked => motionLockCount > 0;
    public MeshRenderer OuterMeshRenderer
    {
        get
        {
            EnsureMeshComponents();
            return meshRenderer;
        }
    }

    /// <summary>True when a mesh is assigned and the MeshRenderer can draw.</summary>
    public bool HasRenderableMesh
    {
        get
        {
            EnsureMeshComponents();
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

    public void ApplyGridPosition(IGridSpace gridSpace, Vector2Int gridPosition)
    {
        if (gridSpace == null || IsMotionLocked)
        {
            return;
        }

        Vector3 world = gridSpace.GridToWorld(gridPosition);
        float halfHeight = Mathf.Abs(transform.lossyScale.y) * 0.5f;
        world.y += surfaceLift + halfHeight;
        transform.position = world;
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

        Mesh mesh = asNest ? ShapeMeshFactory3D.GetNestMesh(shape) : ShapeMeshFactory3D.GetSolidMesh(shape);
        if (meshFilter != null)
        {
            meshFilter.sharedMesh = mesh;
        }

        SetMaterial(material);

        // Blocks sit proudly on the cell; nests sit slightly recessed as destinations.
        surfaceLift = asNest ? -0.02f : 0.025f;

        float size = Mathf.Max(0.01f, footprint);
        transform.localScale = new Vector3(size, pieceHeight, size);
        configuredFootprintScale = transform.localScale;
        hasRestScale = false;
        CaptureRestScale();
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
            }

            return;
        }

        hasNestedInner = true;
        configuredInnerShape = innerShape;
        nestedInnerRoot.gameObject.SetActive(true);
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
            meshRenderer.enabled = true;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
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
                nestedInnerRenderer.enabled = true;
            }
        }
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
