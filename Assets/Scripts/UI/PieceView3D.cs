using System.Collections;
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
    private float pieceHeight = 0.36f;

    [SerializeField]
    private MeshFilter meshFilter;

    [SerializeField]
    private MeshRenderer meshRenderer;

    [SerializeField]
    private BoxCollider pickCollider;

    private Vector3 restScale = Vector3.one;
    private bool hasRestScale;
    private ShapeType configuredShape = ShapeType.Square;
    private Material configuredSolidMaterial;
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
    private float interactionHeldBlend;
    private float interactionScaleMul = 1f;
    private float interactionLiftLocal;
    private float tapPunchMul = 1f;
    private float magnetSelectionMul = 1f;
    private Vector3 nestedInnerRestScale = Vector3.one;
    private Transform contactShadow;
    private MeshRenderer contactShadowRenderer;
    private static Material sharedContactShadowMaterial;
    private const string DesignerVisualName = "DesignerVisual";
    private const string DesignerInnerName = "DesignerInner";
    private const float InteractionScalePeak = 1.04f;
    private const float InteractionLiftLocal = 0.045f;
    private const float TapPunchUpDuration = 0.05f;
    private const float TapPunchDownDuration = 0.07f;
    private const float InvalidNudgeScale = 0.97f;
    private const float InvalidNudgeDuration = 0.10f;
    private GameObject designerVisualInstance;
    private GameObject designerVisualPrefab;
    private MeshRenderer designerVisualRenderer;
    private GameObject designerInnerInstance;
    private GameObject designerInnerPrefab;

    public float PieceHeight => pieceHeight;
    public float SurfaceLift => surfaceLift;
    public ShapeType ConfiguredShape => configuredShape;
    public Material ConfiguredSolidMaterial => configuredSolidMaterial;
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

    /// <summary>
    /// World-space center used for pointer picking / screen projection.
    /// Follows the visible mesh (including VisualCenterBoardPlaneOffsetLocal),
    /// not only the logical root seat.
    /// </summary>
    public Vector3 PickWorldCenter
    {
        get
        {
            if (pickCollider != null && pickCollider.enabled)
            {
                return pickCollider.bounds.center;
            }

            if (meshRenderer != null && meshRenderer.enabled)
            {
                return meshRenderer.bounds.center;
            }

            if (visualRoot != null)
            {
                return visualRoot.position;
            }

            return transform.position;
        }
    }

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
    /// Seats world presentation at a grid cell while motion-locked (Shuffle start pose).
    /// Does not change Block GridPosition or occupancy.
    /// </summary>
    public void SnapWorldPresentationToGrid(IGridSpace gridSpace, Vector2Int gridPosition)
    {
        if (gridSpace == null)
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
        // Carry owns height once active — clear press micro-lift so they do not stack.
        if (presentationLift > 0.02f)
        {
            interactionLiftLocal = 0f;
        }
        else if (interactionHeldBlend > 0.001f)
        {
            interactionLiftLocal = InteractionLiftLocal * interactionHeldBlend;
        }

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
        if (interactionHeldBlend > 0.001f)
        {
            interactionLiftLocal = InteractionLiftLocal * interactionHeldBlend;
        }

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
        float scaleMul = visualScaleMul * interactionScaleMul * tapPunchMul * magnetSelectionMul;
        if (!PieceMotionMath.IsFinite(scaleMul) || scaleMul < 0.45f || scaleMul > 1.35f)
        {
            scaleMul = 1f;
        }

        float squash = Mathf.Clamp01(presentationSquash);
        float xz = scaleMul * (1f + (0.14f * squash));
        float y = scaleMul * (1f - (0.24f * squash));
        Vector3 meshScale = new Vector3(xz, y, xz);
        if (PieceMotionMath.IsFinite(meshScale))
        {
            visualRoot.localScale = meshScale;
        }

        // Nested inner shares the same presentation scale/squash so outer+inner stay coherent.
        if (hasNestedInner && nestedInnerRoot != null && nestedInnerRoot.gameObject.activeSelf)
        {
            Vector3 inner = Vector3.Scale(nestedInnerRestScale, meshScale);
            if (PieceMotionMath.IsFinite(inner))
            {
                nestedInnerRoot.localScale = inner;
            }
        }

        ApplyVisualCenterOffset(configuredFootprintScale.x);
    }

    private void RefreshContactShadow()
    {
        EnsureContactShadow();
        if (contactShadow == null)
        {
            return;
        }

        bool show = isActiveAndEnabled;
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
            contactShadowRenderer.sharedMaterial = configuredAsNest
                ? GetNestContactShadowMaterial()
                : GetContactShadowMaterial();
        }

        float lift = PresentationLift;
        float parentY = Mathf.Abs(transform.localScale.y);
        if (!PieceMotionMath.IsFinite(parentY) || parentY < 0.0001f)
        {
            parentY = 1f;
        }

        // Sit the disc on the tile under the piece bottom (local unit mesh center at 0).
        float drop = 0.5f + ((surfaceLift + lift) / parentY);
        if (!PieceMotionMath.IsFinite(drop))
        {
            drop = 0.5f;
        }

        // Phase 52D: align soft contact with visualRoot board-plane centering (not gameplay root).
        Vector3 local = BoardAdaptivePresentation3D.ComputeVisualCenterOffsetLocal(
            configuredAsNest,
            transform);
        if (!PieceMotionMath.IsFinite(local))
        {
            local = Vector3.zero;
        }

        local.y = -drop;
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

        // Soft disc under the visual footprint; nests use a tighter inset recess shadow.
        // Held selection strengthens the shadow slightly for visual priority (no material swap).
        // Nest-entry squash / insert scale tightens the disc (Phase 52H contact response).
        float heldBoost = 0.10f * interactionHeldBlend;
        float insertTighten = (0.18f * Mathf.Clamp01(presentationSquash))
            + (0.12f * Mathf.Clamp01(1f - carryMeshScale));
        // Phase 52I: slightly tighter footprint so soft falloff stays in-cell.
        float baseRadius = configuredAsNest ? 0.36f : 0.48f;
        float radius = baseRadius * (1f + (0.32f * blend) + heldBoost) * (1f - insertTighten);
        Vector3 shadowScale = new Vector3(radius, 0.010f, radius);
        if (PieceMotionMath.IsFinite(shadowScale))
        {
            contactShadow.localScale = shadowScale;
        }
    }

    private void EnsureContactShadow()
    {
        if (contactShadow == null)
        {
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
        }

        var filter = contactShadow.GetComponent<MeshFilter>();
        if (filter == null)
        {
            filter = contactShadow.gameObject.AddComponent<MeshFilter>();
        }

        filter.sharedMesh = BoardMeshFactory3D.GetSoftContactShadowDisc(40, 5);
        contactShadowRenderer = contactShadow.GetComponent<MeshRenderer>();
        if (contactShadowRenderer == null)
        {
            contactShadowRenderer = contactShadow.gameObject.AddComponent<MeshRenderer>();
        }

        contactShadowRenderer.sharedMaterial = configuredAsNest
            ? GetNestContactShadowMaterial()
            : GetContactShadowMaterial();
        contactShadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        contactShadowRenderer.receiveShadows = false;
        contactShadow.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Clears cached contact-shadow materials so presentation retunes pick up on mode apply.
    /// </summary>
    public static void InvalidateContactShadowMaterials()
    {
        sharedContactShadowMaterial = null;
        sharedNestContactShadowMaterial = null;
    }

    private static Material sharedNestContactShadowMaterial;

    private static Material GetContactShadowMaterial()
    {
        if (sharedContactShadowMaterial != null)
        {
            return sharedContactShadowMaterial;
        }

        // Phase 52I: slightly stronger center alpha; soft indigo — not a hard black cookie.
        sharedContactShadowMaterial = CreateSoftContactShadowMaterial(
            "PieceContactShadow3D_Runtime",
            new Color(0.032f, 0.018f, 0.085f, 0.34f));
        return sharedContactShadowMaterial;
    }

    private static Material GetNestContactShadowMaterial()
    {
        if (sharedNestContactShadowMaterial != null)
        {
            return sharedNestContactShadowMaterial;
        }

        // Slightly stronger / tighter recess cue for sockets.
        sharedNestContactShadowMaterial = CreateSoftContactShadowMaterial(
            "NestContactShadow3D_Runtime",
            new Color(0.022f, 0.010f, 0.065f, 0.40f));
        return sharedNestContactShadowMaterial;
    }

    private static Material CreateSoftContactShadowMaterial(string materialName, Color tint)
    {
        // Particles/Unlit multiplies vertex colors → radial soft falloff from soft disc mesh.
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard");

        var material = new Material(shader)
        {
            name = materialName,
            color = tint
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", tint);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", tint);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0f);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0f);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = 3000;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        else
        {
            material.renderQueue = 3000;
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", 0f); // Off — visible from below camera angles
        }

        return material;
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

    /// <summary>
    /// Presentation-only held feel: mesh scale + visualRoot micro-lift.
    /// Does not change root footprint scale, GridPosition, or occupancy.
    /// </summary>
    public void SetHeldBlend(float blend)
    {
        interactionHeldBlend = Mathf.Clamp01(blend);
        interactionScaleMul = Mathf.Lerp(1f, InteractionScalePeak, interactionHeldBlend);
        if (presentationLift < 0.02f)
        {
            interactionLiftLocal = InteractionLiftLocal * interactionHeldBlend;
        }
        else
        {
            interactionLiftLocal = 0f;
        }

        ApplyCarryVisualScale(carryMeshScale);
        RefreshContactShadow();
    }

    /// <summary>
    /// Short ease-out select response. Micro lift pulse on visualRoot only;
    /// held scale comes from <see cref="SetHeldBlend"/> so values do not stack.
    /// </summary>
    public void PlayTapFeedback()
    {
        if (!isActiveAndEnabled || configuredAsNest)
        {
            return;
        }

        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.InteractionId, false);
        float startLift = interactionLiftLocal;
        float peakLift = Mathf.Max(startLift, 0.055f);
        Sequence punch = DOTween.Sequence()
            .SetId(TweenAnimationUtility.InteractionId)
            .SetLink(gameObject);
        punch.Append(TweenAnimationUtility.Progress(TapPunchUpDuration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
            interactionLiftLocal = Mathf.LerpUnclamped(startLift, peakLift, eased);
            ApplyVisualCenterOffset(configuredFootprintScale.x);
            RefreshContactShadow();
        }));
        punch.Append(TweenAnimationUtility.Progress(TapPunchDownDuration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
            float settle = presentationLift < 0.02f
                ? InteractionLiftLocal * interactionHeldBlend
                : 0f;
            interactionLiftLocal = Mathf.LerpUnclamped(peakLift, settle, eased);
            ApplyVisualCenterOffset(configuredFootprintScale.x);
            RefreshContactShadow();
        }));
        punch.OnKill(() =>
        {
            if (this == null)
            {
                return;
            }

            if (presentationLift < 0.02f)
            {
                interactionLiftLocal = InteractionLiftLocal * interactionHeldBlend;
            }
            else
            {
                interactionLiftLocal = 0f;
            }

            ApplyVisualCenterOffset(configuredFootprintScale.x);
            RefreshContactShadow();
        });
    }

    /// <summary>
    /// Tiny nest-socket response when a matching block inserts. visualRoot only.
    /// Does not move the nest gameplay root / GridPosition.
    /// </summary>
    public void PlayNestSocketPulse()
    {
        if (!isActiveAndEnabled || !configuredAsNest || visualRoot == null)
        {
            return;
        }

        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.NestSocketId, false);
        EnsureMeshComponents();
        Vector3 restMesh = visualRoot.localScale;
        if (!PieceMotionMath.IsFinite(restMesh) || restMesh.sqrMagnitude < 0.0001f)
        {
            restMesh = Vector3.one;
        }

        const float peak = 1.035f;
        const float up = 0.07f;
        const float down = 0.08f;
        Sequence pulse = DOTween.Sequence()
            .SetId(TweenAnimationUtility.NestSocketId)
            .SetLink(gameObject);
        pulse.Append(TweenAnimationUtility.Progress(up, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
            float mul = Mathf.LerpUnclamped(1f, peak, eased);
            Vector3 s = restMesh * mul;
            if (PieceMotionMath.IsFinite(s) && visualRoot != null)
            {
                visualRoot.localScale = s;
            }
        }));
        pulse.Append(TweenAnimationUtility.Progress(down, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseInCubic(t);
            float mul = Mathf.LerpUnclamped(peak, 1f, eased);
            Vector3 s = restMesh * mul;
            if (PieceMotionMath.IsFinite(s) && visualRoot != null)
            {
                visualRoot.localScale = s;
            }
        }));
        pulse.OnKill(() =>
        {
            if (this == null || visualRoot == null)
            {
                return;
            }

            visualRoot.localScale = restMesh;
        });
        pulse.OnComplete(() =>
        {
            if (visualRoot != null)
            {
                visualRoot.localScale = restMesh;
            }
        });
    }

    /// <summary>
    /// Legacy standalone compress (superseded by WorldPieceMotion.AnimateShuffleMove).
    /// Kept for cleanup/interrupt paths via <see cref="ClearShufflePresentation"/>.
    /// </summary>
    public IEnumerator PlayShuffleAnticipation(float duration = 0.10f)
    {
        if (!isActiveAndEnabled || configuredAsNest || IsMotionLocked)
        {
            yield break;
        }

        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.ShuffleId, false);
        const float peakSquash = 0.14f;
        float half = Mathf.Max(0.02f, duration * 0.45f);
        float meshMul = carryMeshScale;
        Sequence sequence = DOTween.Sequence()
            .SetId(TweenAnimationUtility.ShuffleId)
            .SetLink(gameObject);
        sequence.Append(TweenAnimationUtility.Progress(half, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
            SetPresentationAnticipation(0f, meshMul, peakSquash * eased);
        }));
        sequence.Append(TweenAnimationUtility.Progress(duration - half, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseInCubic(t);
            SetPresentationAnticipation(0f, meshMul, peakSquash * (1f - eased));
        }));
        sequence.OnKill(() =>
        {
            if (this == null)
            {
                return;
            }

            SetPresentationAnticipation(0f, carryMeshScale, 0f);
        });
        yield return TweenAnimationUtility.Wait(sequence);
    }

    /// <summary>
    /// Clears Shuffle presentation squash/lift. Safe after interrupt or level change.
    /// </summary>
    public void ClearShufflePresentation()
    {
        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.ShuffleId, false);
        SetPresentationAnticipation(0f, carryMeshScale, 0f);
    }

    /// <summary>
    /// Legacy standalone settle (superseded by WorldPieceMotion.AnimateShuffleMove).
    /// </summary>
    public IEnumerator PlayShuffleSettle(float duration = 0.06f)
    {
        if (!isActiveAndEnabled || configuredAsNest || IsMotionLocked)
        {
            yield break;
        }

        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.ShuffleId, false);
        const float peakSquash = 0.10f;
        float meshMul = carryMeshScale;
        Sequence sequence = DOTween.Sequence()
            .SetId(TweenAnimationUtility.ShuffleId)
            .SetLink(gameObject);
        sequence.Append(TweenAnimationUtility.Progress(duration, t =>
        {
            float pulse = peakSquash * Mathf.Sin(t * Mathf.PI) * (1f - (0.35f * t));
            SetPresentationAnticipation(0f, meshMul, pulse);
        }));
        sequence.OnKill(() =>
        {
            if (this == null)
            {
                return;
            }

            SetPresentationAnticipation(0f, carryMeshScale, 0f);
        });
        sequence.OnComplete(() =>
        {
            if (this != null)
            {
                SetPresentationAnticipation(0f, carryMeshScale, 0f);
            }
        });
        yield return TweenAnimationUtility.Wait(sequence);
    }

    /// <summary>
    /// Tiny press response for rejected interactions. Presentation only.
    /// </summary>
    public void PlayInvalidNudge()
    {
        if (!isActiveAndEnabled || configuredAsNest)
        {
            return;
        }

        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.InteractionId, false);
        tapPunchMul = 1f;
        float half = InvalidNudgeDuration * 0.5f;
        Sequence nudge = DOTween.Sequence()
            .SetId(TweenAnimationUtility.InteractionId)
            .SetLink(gameObject);
        nudge.Append(TweenAnimationUtility.Progress(half, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
            tapPunchMul = Mathf.LerpUnclamped(1f, InvalidNudgeScale, eased);
            ApplyCarryVisualScale(carryMeshScale);
        }));
        nudge.Append(TweenAnimationUtility.Progress(half, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
            tapPunchMul = Mathf.LerpUnclamped(InvalidNudgeScale, 1f, eased);
            ApplyCarryVisualScale(carryMeshScale);
        }));
        nudge.OnKill(() =>
        {
            if (this == null)
            {
                return;
            }

            tapPunchMul = 1f;
            ApplyCarryVisualScale(carryMeshScale);
        });
        nudge.OnComplete(() =>
        {
            tapPunchMul = 1f;
            ApplyCarryVisualScale(carryMeshScale);
        });
    }

    /// <summary>
    /// Presentation-only Magnet selection breath/confirm scale (mesh only).
    /// Does not move root, GridPosition, or nested parenting.
    /// </summary>
    public float MagnetSelectionMul => magnetSelectionMul;

    public void SetMagnetSelectionEmphasis(float scaleMul)
    {
        if (!isActiveAndEnabled || configuredAsNest)
        {
            return;
        }

        if (!PieceMotionMath.IsFinite(scaleMul) || scaleMul < 0.9f || scaleMul > 1.08f)
        {
            scaleMul = 1f;
        }

        magnetSelectionMul = scaleMul;
        ApplyCarryVisualScale(carryMeshScale);
    }

    /// <summary>
    /// Short confirm punch when an eligible Magnet target is chosen. Fire-and-forget.
    /// </summary>
    public void PlayMagnetSelectionConfirm()
    {
        if (!isActiveAndEnabled || configuredAsNest)
        {
            return;
        }

        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.MagnetSelectionId, false);
        float start = magnetSelectionMul;
        const float peak = 1.045f;
        const float half = 0.05f;
        Sequence confirm = DOTween.Sequence()
            .SetId(TweenAnimationUtility.MagnetSelectionId)
            .SetLink(gameObject);
        confirm.Append(TweenAnimationUtility.Progress(half, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
            magnetSelectionMul = Mathf.LerpUnclamped(start, peak, eased);
            ApplyCarryVisualScale(carryMeshScale);
        }));
        confirm.Append(TweenAnimationUtility.Progress(half, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseInCubic(t);
            magnetSelectionMul = Mathf.LerpUnclamped(peak, 1f, eased);
            ApplyCarryVisualScale(carryMeshScale);
        }));
        confirm.OnKill(() =>
        {
            if (this == null)
            {
                return;
            }

            magnetSelectionMul = 1f;
            ApplyCarryVisualScale(carryMeshScale);
        });
        confirm.OnComplete(() =>
        {
            magnetSelectionMul = 1f;
            ApplyCarryVisualScale(carryMeshScale);
        });
    }

    /// <summary>
    /// Clears Magnet selection emphasis and kills MagnetSelection tweens on this view.
    /// </summary>
    public void ClearMagnetSelectionPresentation()
    {
        TweenAnimationUtility.KillById(transform, TweenAnimationUtility.MagnetSelectionId, false);
        if (Mathf.Abs(magnetSelectionMul - 1f) < 0.0001f)
        {
            return;
        }

        magnetSelectionMul = 1f;
        ApplyCarryVisualScale(carryMeshScale);
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
        ApplyProceduralMaterials(material, configuredAsNest, configuredShape, null);
    }

    private void ApplyProceduralMaterials(Material material, bool asNest, ShapeType shape, Material[] nestMaterials)
    {
        EnsureMeshComponents();
        if (meshRenderer == null)
        {
            return;
        }

        if (asNest)
        {
            if (nestMaterials != null && nestMaterials.Length > 0)
            {
                meshRenderer.sharedMaterials = nestMaterials;
            }
            else if (material != null)
            {
                meshRenderer.sharedMaterials = new[] { material, material };
            }
            else
            {
                meshRenderer.sharedMaterials = ShapeVisuals3D.NestMaterialSet(shape);
            }

            return;
        }

        if (material != null)
        {
            meshRenderer.sharedMaterial = material;
        }
    }

    private void ApplyMaterialToActiveVisual(Material material, bool asNest, Material[] nestMaterials)
    {
        if (material == null && (nestMaterials == null || nestMaterials.Length == 0))
        {
            return;
        }

        if (designerVisualInstance == null)
        {
            return;
        }

        MeshRenderer[] renderers = designerVisualInstance.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (asNest)
            {
                if (nestMaterials != null && nestMaterials.Length > 0)
                {
                    renderer.sharedMaterials = nestMaterials;
                }
                else if (material != null)
                {
                    renderer.sharedMaterials = new[] { material, material };
                }
            }
            else if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        designerVisualRenderer = designerVisualInstance.GetComponentInChildren<MeshRenderer>(true);
    }

    public void SetPieceHeight(float height)
    {
        pieceHeight = Mathf.Max(0.01f, height);
    }

    /// <summary>
    /// Applies shape mesh (solid block or hollow nest) and material.
    /// Footprint scale is XY on XZ plane; Y scale maps extruded unit height to <see cref="pieceHeight"/>.
    /// </summary>
    public void ConfigureVisual(
        ShapeType shape,
        Material material,
        bool asNest,
        float footprint,
        float height,
        Material[] nestMaterials = null,
        bool activate = true)
    {
        EnsureMeshComponents();
        configuredShape = shape;
        configuredAsNest = asNest;
        configuredSolidMaterial = material;
        pieceHeight = Mathf.Max(0.01f, height);

        if (ShapeNestVisualCatalog3D.TryGetPiecePrefab(shape, asNest, out GameObject prefab))
        {
            ApplyDesignerVisual(prefab);
            ApplyMaterialToActiveVisual(material, asNest, nestMaterials);
        }
        else
        {
            ClearDesignerVisual();
            Mesh mesh = asNest ? ShapeMeshFactory3D.GetNestMesh(shape) : ShapeMeshFactory3D.GetSolidMesh(shape);
            if (meshFilter != null)
            {
                meshFilter.sharedMesh = mesh;
            }

            if (visualRoot != null)
            {
                visualRoot.localRotation = Quaternion.identity;
            }

            ApplyProceduralMaterials(material, asNest, shape, nestMaterials);
        }

        // Blocks sit proudly on the cell; nests sit slightly recessed as destinations.
        surfaceLift = asNest ? -0.02f : 0.025f;

        float size = Mathf.Max(0.01f, footprint);
        transform.localScale = new Vector3(size, pieceHeight, size);
        configuredFootprintScale = transform.localScale;
        hasRestScale = false;
        CaptureRestScale();
        ApplyVisualCenterOffset(footprint);
        ClearCarryPresentation(applyToTransform: false);
        RefreshPickCollider();
        if (activate)
        {
            EnsurePresentationVisible();
        }
    }

    /// <summary>
    /// Presentation-only: drop any cached designer instance then build a solid block mesh.
    /// Used after nested outer peel so Diamond:Green cannot reuse the prior designer materials.
    /// </summary>
    public void ForceRebuildSolidVisual(
        ShapeType shape,
        Material material,
        float footprint,
        float height,
        bool activate = true)
    {
        ClearDesignerVisual();
        designerVisualPrefab = null;
        ConfigureVisual(shape, material, asNest: false, footprint, height, nestMaterials: null, activate: activate);
    }

    /// <summary>
    /// Presentation-only visualRoot offset so the mesh reads centered in the cell under
    /// BoardCamera3D. Uses a board-plane (XZ) shift — never sinks the mesh into the tile.
    /// Nests are left at local zero. Does not move the piece root or chain spacing.
    /// </summary>
    private void ApplyVisualCenterOffset(float footprintWorld)
    {
        if (visualRoot == null)
        {
            return;
        }

        Vector3 local = BoardAdaptivePresentation3D.ComputeVisualCenterOffsetLocal(
            configuredAsNest,
            transform);
        if (!PieceMotionMath.IsFinite(local))
        {
            local = Vector3.zero;
        }

        // Keep physical height from centering: micro interaction lift is presentation-only.
        local.y = interactionLiftLocal;
        visualRoot.localPosition = local;
        ApplyNestedInnerVisualCenterOffset();
        // Keep the pick volume on the visible mesh, not the unshifted logical root.
        AlignPickColliderToVisual();
    }

    /// <summary>
    /// Presentation-only nested inner mesh. Child of this cell so hop squash and
    /// chain follow move outer+inner together. Does not own gameplay layers.
    /// </summary>
    /// <summary>
    /// Phase 68B: reparent NestedInner3D under <paramref name="newParent"/> while preserving
    /// world position/rotation/scale. Clears ownership on this view so outer-only travel
    /// can move without carrying the inner. Does not destroy the inner GameObject.
    /// </summary>
    public bool TryDetachNestedInnerPreservingWorld(Transform newParent, out Transform detached)
    {
        detached = null;
        if (!hasNestedInner || nestedInnerRoot == null || !nestedInnerRoot.gameObject.activeSelf)
        {
            return false;
        }

        detached = nestedInnerRoot;
        Transform parent = newParent != null ? newParent : detached.root;
        detached.SetParent(parent, worldPositionStays: true);

        nestedInnerRoot = null;
        nestedInnerFilter = null;
        nestedInnerRenderer = null;
        hasNestedInner = false;
        configuredInnerShape = default;
        // Designer instance (if any) remains under the detached root; drop local refs only.
        designerInnerInstance = null;
        designerInnerPrefab = null;
        return true;
    }

    /// <summary>
    /// True when a nested-inner child mesh is currently shown under this cell view.
    /// </summary>
    public bool HasDetachedNestedInnerCandidate =>
        hasNestedInner && nestedInnerRoot != null && nestedInnerRoot.gameObject.activeSelf;

    public void ConfigureNestedInner(bool show, ShapeType innerShape, Material material, float relativeScale, bool asNest)
    {
        // Phase 69B: show=false must not allocate NestedInner3D. After detach, the anchored
        // residual owns the nested presentation — recreating under the traveler caused the
        // duplicate-inner / target-promotion bug.
        // Phase 72D: destroy NestedInner3D entirely on hide. Leaving it inactive under the
        // traveler let EnsurePresentationVisible / residual races flash green-over-red.
        if (!show)
        {
            hasNestedInner = false;
            configuredInnerShape = default;
            ClearDesignerInner();
            if (nestedInnerRoot != null)
            {
                DestroyVisualObject(nestedInnerRoot.gameObject);
                nestedInnerRoot = null;
                nestedInnerFilter = null;
                nestedInnerRenderer = null;
            }

            // Stray child left by a failed detach / rebind — remove so it cannot ghost.
            Transform stray = transform.Find("NestedInner3D");
            if (stray != null)
            {
                DestroyVisualObject(stray.gameObject);
            }

            return;
        }

        EnsureNestedInner();
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
                if (asNest)
                {
                    nestedInnerRenderer.sharedMaterials = new[] { material, material };
                }
                else
                {
                    nestedInnerRenderer.sharedMaterial = material;
                }

                nestedInnerRenderer.enabled = true;
                nestedInnerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                nestedInnerRenderer.receiveShadows = true;
            }
        }

        ApplyMaterialToDesignerInner(material, asNest);

        float scale = Mathf.Clamp(relativeScale, 0.4f, 0.7f);
        // Slightly flatter Y so the inner extrusion reads seated inside the outer rim.
        nestedInnerRestScale = new Vector3(scale, scale * 0.72f, scale);
        nestedInnerRoot.localScale = nestedInnerRestScale;
        ApplyNestedInnerVisualCenterOffset();
        nestedInnerRoot.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Aligns nested inner mesh with the outer piece's board-plane visual center.
    /// Uses the same optical offset as visualRoot without moving gameplay/root transforms.
    /// </summary>
    private void ApplyNestedInnerVisualCenterOffset()
    {
        if (nestedInnerRoot == null || !hasNestedInner)
        {
            return;
        }

        EnsureMeshComponents();
        Vector3 local = visualRoot != null ? visualRoot.localPosition : Vector3.zero;
        if (!PieceMotionMath.IsFinite(local))
        {
            local = Vector3.zero;
        }

        // Follow outer visualRoot including interaction micro-lift so nested stays aligned.
        nestedInnerRoot.localPosition = local;
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

        ApplyVisualCenterOffset(configuredFootprintScale.x);

        if (hasNestedInner && nestedInnerRoot != null)
        {
            ApplyNestedInnerVisualCenterOffset();
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
        // Center follows visualRoot so VisualCenterBoardPlaneOffsetLocal stays pickable.
        pickCollider.isTrigger = true;
        AlignPickColliderToVisual();
        pickCollider.enabled = true;
    }

    private void AlignPickColliderToVisual()
    {
        if (pickCollider == null)
        {
            return;
        }

        Vector3 center = Vector3.zero;
        if (visualRoot != null)
        {
            center = visualRoot.localPosition;
        }

        pickCollider.center = center;
        // Slightly larger than the unit mesh so edge / near-edge taps register.
        pickCollider.size = new Vector3(1.22f, 1.1f, 1.22f);
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

            visualRoot.localRotation = Quaternion.identity;
            ApplyVisualCenterOffset(configuredFootprintScale.x);
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
                Phase69AForensic.LogNestedCreated(this, innerObject);
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

    private void ApplyMaterialToDesignerInner(Material material, bool asNest)
    {
        if (material == null || designerInnerInstance == null)
        {
            return;
        }

        MeshRenderer renderer = designerInnerInstance.GetComponentInChildren<MeshRenderer>(true);
        if (renderer == null)
        {
            return;
        }

        if (asNest)
        {
            renderer.sharedMaterials = new[] { material, material };
        }
        else
        {
            renderer.sharedMaterial = material;
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
