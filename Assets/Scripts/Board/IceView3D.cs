using DG.Tweening;
using UnityEngine;

/// <summary>
/// World3D presentation for <see cref="IceState"/>. Gameplay ice state remains authoritative.
/// Owns crystalline ice shell rendering, DOTween durability transitions, freeze-on growth, and melt.
/// SyncFromSource never restarts an in-flight tween toward the same target state.
/// </summary>
[DisallowMultipleComponent]
public class IceView3D : MonoBehaviour
{
    private const float FreezeOnDuration = 0.40f;
    private const float DurabilityTransitionDuration = 0.24f;
    private const float MeltDuration = 0.36f;

    [SerializeField]
    private Transform shell;

    [SerializeField]
    private MeshRenderer shellRenderer;

    [SerializeField]
    private TMPro.TextMeshPro durabilityText;

    [SerializeField]
    private float thickness = 0.12f; // increased from 0.06 to give substantial ice volume

    [SerializeField]
    private float footprintPadding = 0.14f;

    [SerializeField]
    private float heightOverBlock = 0.05f;

    [SerializeField]
    private IceState source;
    private static Material sharedIceMaterial;

    /// <summary>Gameplay-facing frozen flag last observed for VFX edge detection.</summary>
    private bool wasFrozen;

    /// <summary>Presentation durability currently shown / tweening toward.</summary>
    private int presentedDurability = -1;

    private int targetDurability = -1;
    private bool isMelting;
    private bool meltVfxPlayed;
    private Sequence activeSequence;
    private float layoutSizeX = 1f;
    private float layoutSizeZ = 1f;
    private float layoutScale = 1f;
    private float layoutBlockHeight = 0.36f;
    private float layoutPieceCenterY;
    private ShapeType layoutShape = ShapeType.Square;
    private bool layoutIsChain;
    private float presentedThickness;
    private float presentedFreezeProgress = 1f;
    private float presentedCrackAmount;

    public IceState Source => source;
    public bool IsBound => source != null;

    /// <summary>True while a durability transition, freeze-on, or melt tween is running.</summary>
    public bool IsPresentationAnimating =>
        isMelting || (activeSequence != null && activeSequence.IsActive());

    public void Bind(IceState ice, Material material)
    {
        bool sameSource = source == ice;
        source = ice;
        EnsureShell();
        if (shellRenderer != null && material != null && !ShapeNestVisualCatalog3D.TryGetIcePrefab(out _))
        {
            shellRenderer.sharedMaterial = material;
        }

        Block block = ice != null ? ice.GetComponent<Block>() : null;
        Debug.Log($"[ICE_LIFECYCLE] Bind: IceView3D ID={GetInstanceID()}, IceState ID={(ice != null ? ice.GetInstanceID() : 0)}, Block ID={(block != null ? block.GetInstanceID() : 0)}, Cell={(block != null ? block.GridPosition : Vector2Int.zero)}, IsFrozen={(ice != null && ice.IsFrozen)}, Durability={(ice != null ? ice.Durability : 0)}, activeSelf={gameObject.activeSelf}");

        if (!sameSource)
        {
            KillOwnedTweens(false);
            isMelting = false;
            meltVfxPlayed = false;
            wasFrozen = ice != null && ice.IsFrozen;
            presentedDurability = -1;
            targetDurability = -1;
        }

        SyncFromSource();
    }

    public void ClearBind()
    {
        Debug.Log($"[ICE_LIFECYCLE] ClearBind called on IceView3D ID={GetInstanceID()}");
        KillOwnedTweens(false);
        source = null;
        wasFrozen = false;
        presentedDurability = -1;
        targetDurability = -1;
        isMelting = false;
        meltVfxPlayed = false;
        UpdateDurabilityNumber(0);
        if (shell != null)
        {
            shell.gameObject.SetActive(false);
        }
    }

    public void SyncFromSource()
    {
        EnsureShell();
        bool frozen = source != null && source.IsFrozen;
        int durability = frozen ? source.Durability : 0;

        if (isMelting)
        {
            // Melt owns the shell until OnComplete; do not snap or restart.
            return;
        }

        if (!frozen)
        {
            UpdateDurabilityNumber(0);
            if (wasFrozen || presentedDurability > 0)
            {
                Debug.Log($"[ICE_LIFECYCLE] SyncFromSource: IceState IsFrozen is false (wasFrozen={wasFrozen}, presentedDurability={presentedDurability}). Starting BeginMelt() on IceView3D ID={GetInstanceID()}.");
                BeginMelt();
            }
            else
            {
                Debug.Log($"[ICE_LIFECYCLE] SyncFromSource: IceState IsFrozen is false. Calling HideImmediate() on IceView3D ID={GetInstanceID()}.");
                HideImmediate();
            }

            wasFrozen = false;
            return;
        }

        bool previouslyFrozen = wasFrozen;
        if (!RefreshLayoutMetrics())
        {
            Debug.LogWarning($"[ICE_LIFECYCLE] SyncFromSource: RefreshLayoutMetrics returned false for IceView3D ID={GetInstanceID()} (source block missing or presenter null). Retaining frozen view.");
            if (shell != null && !shell.gameObject.activeSelf)
            {
                shell.gameObject.SetActive(true);
            }
            return;
        }

        wasFrozen = true;
        if (shell != null && !shell.gameObject.activeSelf)
        {
            shell.gameObject.SetActive(true);
        }

        UpdateDurabilityNumber(durability);

        if (presentedDurability < 1)
        {
            if (!previouslyFrozen)
            {
                BeginFreezeOn(durability);
            }
            else
            {
                // First show: snap to current gameplay durability (level load / bind).
                ApplyPresentedState(durability, animate: false);
            }
            return;
        }

        if (durability == targetDurability && IsPresentationAnimating)
        {
            // Already tweening toward this durability — keep following block layout only.
            ApplyLayoutTransform(presentedThickness);
            return;
        }

        if (durability == presentedDurability && !IsPresentationAnimating)
        {
            ApplyLayoutTransform(presentedThickness);
            ApplyAppearanceImmediate(presentedDurability, presentedFreezeProgress, presentedCrackAmount, 0f);
            return;
        }

        BeginDurabilityTransition(presentedDurability, durability);
    }

    private void OnDisable()
    {
        Debug.Log($"[ICE_LIFECYCLE] OnDisable on IceView3D ID={GetInstanceID()}");
        KillOwnedTweens(false);
        isMelting = false;
        activeSequence = null;
    }

    private void OnDestroy()
    {
        Debug.Log($"[ICE_LIFECYCLE] OnDestroy on IceView3D ID={GetInstanceID()}");
        KillOwnedTweens(false);
        isMelting = false;
        activeSequence = null;
    }

    private void Update()
    {
        if (shellRenderer != null && presentedDurability > 0 && shellRenderer.gameObject.activeInHierarchy)
        {
            float time = Time.time;
            
            // Subtle crystalline specular shimmer on idle ice
            if (!IsPresentationAnimating)
            {
                float shimmer = Mathf.Max(0f, Mathf.Sin(time * 2.2f) * 0.4f + Mathf.Sin(time * 0.7f) * 0.2f);
                var block = new MaterialPropertyBlock();
                shellRenderer.GetPropertyBlock(block);
                block.SetFloat("_Shimmer", shimmer);
                shellRenderer.SetPropertyBlock(block);
                
                if (Random.value < 0.003f)
                {
                    BoardVfx3D.PlayIceSparkle(transform.position);
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (source != null && source.IsFrozen)
        {
            Block block = source.GetComponent<Block>();
            if (block != null)
            {
                BoardPresenter3D presenter = FindPresenter();
                if (presenter != null)
                {
                    transform.position = CalculateBlockVisualCenter(block, presenter);
                }
            }
        }
    }

    private Vector3 CalculateBlockVisualCenter(Block block, BoardPresenter3D presenter)
    {
        float blockHeight = BoardAdaptivePresentation3D.BlockHeightRatio * presenter.CellWorldSize;
        float surfaceLift = 0.03f;
        float surfaceY = presenter.CellSurfaceWorldY;
        Vector3 center;

        if (block.WorldView != null)
        {
            blockHeight = block.WorldView.PieceHeight;
            surfaceLift = block.WorldView.SurfaceLift;
            center = block.WorldView.GetVisualRootWorldPosition();
        }
        else
        {
            GetBlockFootprint(block, out Vector2Int min, out Vector2Int max);
            IGridSpace space = presenter.GridSpace;
            Vector3 a = space.GridToWorld(min);
            Vector3 b = space.GridToWorld(max);
            center = (a + b) * 0.5f;

            float pieceFootprint = presenter.CellWorldSize * BoardAdaptivePresentation3D.BlockFootprintRatio;
            Vector3 offset = BoardAdaptivePresentation3D.ResolveBoardPlaneScreenDownWorld() * (BoardAdaptivePresentation3D.VisualCenterBoardPlaneOffsetLocal * pieceFootprint);
            center += offset;
        }

        center.y = surfaceY + surfaceLift + blockHeight * 0.5f;
        return center;
    }

    private bool RefreshLayoutMetrics()
    {
        Block block = source != null ? source.GetComponent<Block>() : null;
        if (block == null)
        {
            return false;
        }

        BoardPresenter3D presenter = FindPresenter();
        if (presenter == null)
        {
            return false;
        }

        GetBlockFootprint(block, out Vector2Int min, out Vector2Int max);
        float cell = presenter.CellWorldSize;
        layoutScale = cell / BoardAdaptivePresentation3D.ReferenceCellSize;
        layoutIsChain = (max.x - min.x) + (max.y - min.y) > 0;
        layoutShape = block.GetOuterShape(block.AnchorCellIndex);

        float pieceFootprint = cell * BoardAdaptivePresentation3D.BlockFootprintRatio;
        float blockHeight = BoardAdaptivePresentation3D.BlockHeightRatio * cell;

        if (block.WorldView != null)
        {
            pieceFootprint = Mathf.Max(0.05f, block.WorldView.ConfiguredFootprintScale.x);
            blockHeight = block.WorldView.PieceHeight;
        }

        Vector3 center = CalculateBlockVisualCenter(block, presenter);

        float rawWidth = (max.x - min.x) * cell + pieceFootprint;
        float rawDepth = (max.y - min.y) * cell + pieceFootprint;

        // Ice casing scale: width + 20%, height + 15%
        layoutSizeX = rawWidth * 1.20f;
        layoutSizeZ = rawDepth * 1.20f;

        layoutBlockHeight = blockHeight;
        layoutPieceCenterY = center.y;
        transform.position = center;
        ApplyLayoutTransform(presentedThickness);
        ApplyShellMesh();
        return true;
    }

    private Vector3 ComputePresentationCameraOffset()
    {
        Camera cam = Camera.main;
        var boardCam = FindFirstObjectByType<BoardCamera3D>();
        if (boardCam != null && boardCam.Camera != null)
        {
            cam = boardCam.Camera;
        }

        Vector3 dirToCamera = cam != null
            ? -cam.transform.forward
            : new Vector3(0f, 0.913545f, -0.406737f);

        const float offsetDistance = 0.035f;
        return dirToCamera * (offsetDistance * layoutScale);
    }

    private void ApplyLayoutTransform(float stageThickness, float xzMultiplier = 1f)
    {
        if (shell == null)
        {
            return;
        }

        Vector3 depthOffset = ComputePresentationCameraOffset();
        shell.localPosition = depthOffset;

        float y = Mathf.Max(0.001f, stageThickness);
        shell.localScale = new Vector3(
            layoutSizeX * xzMultiplier,
            y,
            layoutSizeZ * xzMultiplier);

        if (durabilityText != null)
        {
            float textY = (layoutBlockHeight * 0.5f) + 0.015f;
            durabilityText.transform.localPosition = depthOffset + new Vector3(0f, textY, -0.01f);
        }
    }

    private float ThicknessForDurability(int durability)
    {
        int stage = Mathf.Clamp(durability, 1, 3);
        float cover = layoutBlockHeight * 1.15f;
        return Mathf.Max(0.05f, cover * (0.98f + 0.02f * stage));
    }

    private void BeginFreezeOn(int durability)
    {
        durability = Mathf.Clamp(durability, 1, 3);
        RefreshLayoutMetrics();
        presentedDurability = durability;
        targetDurability = durability;
        
        float finalThickness = ThicknessForDurability(durability);
        float finalCrack = CrackAmountForDurability(durability);
        
        presentedThickness = 0.001f;
        presentedFreezeProgress = 0.05f;
        presentedCrackAmount = finalCrack;
        
        ApplyLayoutTransform(presentedThickness, 0.85f);
        ApplyAppearanceImmediate(durability, presentedFreezeProgress, presentedCrackAmount, 0f);
        
        if (shell != null && !shell.gameObject.activeSelf)
        {
            shell.gameObject.SetActive(true);
        }

        UpdateDurabilityNumber(durability);
        if (durabilityText != null && durabilityText.gameObject.activeSelf)
        {
            durabilityText.transform.DOKill();
            durabilityText.transform.localScale = Vector3.zero;
            durabilityText.transform.DOScale(Vector3.one, FreezeOnDuration).SetEase(Ease.OutBack);
        }

        BoardVfx3D.PlayIceFreezeFrost(transform.position);

        KillOwnedTweens(false);
        activeSequence = DOTween.Sequence().SetLink(gameObject);
        
        // Crystalline freeze-on growth transition
        activeSequence.Append(TweenAnimationUtility.Progress(FreezeOnDuration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateSmoothStep(t);
            presentedThickness = Mathf.LerpUnclamped(0.001f, finalThickness, eased);
            presentedFreezeProgress = Mathf.LerpUnclamped(0.05f, 1f, eased);
            
            // Subtle crystalline bounce on freeze lock
            float xz = Mathf.LerpUnclamped(0.85f, 1.05f, eased);
            if (t > 0.8f)
            {
                xz = Mathf.LerpUnclamped(1.05f, 1f, (t - 0.8f) * 5f);
            }
            
            ApplyLayoutTransform(presentedThickness, xz);
            ApplyAppearanceImmediate(durability, presentedFreezeProgress, presentedCrackAmount, 0f);
        }));
        
        activeSequence.AppendCallback(() => 
        {
            BoardVfx3D.PlayIceSparkle(transform.position);
        });

        activeSequence.OnComplete(() =>
        {
            presentedDurability = durability;
            targetDurability = durability;
            presentedThickness = finalThickness;
            presentedFreezeProgress = 1f;
            presentedCrackAmount = finalCrack;
            ApplyLayoutTransform(finalThickness);
            ApplyAppearanceImmediate(durability, 1f, finalCrack, 0f);
            UpdateDurabilityNumber(durability);
            activeSequence = null;
        });
    }

    private void ApplyPresentedState(int durability, bool animate)
    {
        durability = Mathf.Clamp(durability, 1, 3);
        RefreshLayoutMetrics();
        presentedDurability = durability;
        targetDurability = durability;
        presentedThickness = ThicknessForDurability(durability);
        presentedFreezeProgress = 1f;
        presentedCrackAmount = CrackAmountForDurability(durability);
        ApplyLayoutTransform(presentedThickness);
        ApplyAppearanceImmediate(durability, presentedFreezeProgress, presentedCrackAmount, 0f);
        UpdateDurabilityNumber(durability);
    }

    private void BeginDurabilityTransition(int from, int to)
    {
        to = Mathf.Clamp(to, 1, 3);
        from = Mathf.Clamp(from < 1 ? to : from, 1, 3);
        targetDurability = to;
        RefreshLayoutMetrics();

        if (from == to)
        {
            ApplyPresentedState(to, animate: false);
            return;
        }

        if (from > to)
        {
            BoardVfx3D.PlayIceBreakShards(transform.position, to);
        }

        KillOwnedTweens(false);

        float fromThickness = ThicknessForDurability(from);
        float toThickness = ThicknessForDurability(to);
        float fromCrack = CrackAmountForDurability(from);
        float toCrack = CrackAmountForDurability(to);

        presentedThickness = fromThickness;
        presentedFreezeProgress = 1f;
        presentedCrackAmount = fromCrack;
        ApplyLayoutTransform(fromThickness);
        ApplyAppearanceImmediate(from, 1f, fromCrack, 0f);
        UpdateDurabilityNumber(to, animatePunch: true);

        activeSequence = DOTween.Sequence().SetLink(gameObject);
        activeSequence.Append(TweenAnimationUtility.Progress(DurabilityTransitionDuration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateSmoothStep(t);
            presentedThickness = Mathf.LerpUnclamped(fromThickness, toThickness, eased);
            presentedCrackAmount = Mathf.LerpUnclamped(fromCrack, toCrack, eased);
            
            // Impact pulse on damage
            float pulse = 1f + (0.04f * Mathf.Sin(eased * Mathf.PI));
            ApplyLayoutTransform(presentedThickness, pulse);
            ApplyAppearanceImmediate(to, 1f, presentedCrackAmount, 0.4f * (1f - eased));
        }));
        
        activeSequence.OnComplete(() =>
        {
            presentedDurability = to;
            targetDurability = to;
            presentedThickness = toThickness;
            presentedCrackAmount = toCrack;
            presentedFreezeProgress = 1f;
            ApplyLayoutTransform(toThickness);
            ApplyAppearanceImmediate(to, 1f, toCrack, 0f);
            UpdateDurabilityNumber(to);
            activeSequence = null;
        });
    }

    private void BeginMelt()
    {
        if (isMelting)
        {
            return;
        }

        isMelting = true;
        targetDurability = 0;
        UpdateDurabilityNumber(0);
        KillOwnedTweens(false);

        if (!shell.gameObject.activeSelf)
        {
            shell.gameObject.SetActive(true);
        }

        RefreshLayoutMetrics();
        float startThickness = presentedThickness > 0.001f
            ? presentedThickness
            : ThicknessForDurability(Mathf.Max(1, presentedDurability));
        float startCrack = presentedCrackAmount;
        Vector3 startScale = shell.localScale;
        if (startScale.sqrMagnitude < 0.0001f)
        {
            startScale = new Vector3(layoutSizeX, startThickness, layoutSizeZ);
            shell.localScale = startScale;
        }

        if (!meltVfxPlayed)
        {
            meltVfxPlayed = true;
            BoardVfx3D.PlayIceBreakShards(transform.position, 0);
            BoardVfx3D.PlayIceMelt(transform.position);
        }

        float endThickness = startThickness * 0.12f;
        activeSequence = DOTween.Sequence().SetLink(gameObject);
        activeSequence.Append(TweenAnimationUtility.Progress(MeltDuration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseInQuad(t);
            float squash = Mathf.LerpUnclamped(1f, 1.06f, Mathf.Sin(t * Mathf.PI) * 0.5f);
            presentedThickness = Mathf.LerpUnclamped(startThickness, endThickness, eased);
            presentedFreezeProgress = Mathf.LerpUnclamped(1f, 0f, eased);
            presentedCrackAmount = Mathf.LerpUnclamped(startCrack, 1f, eased);
            float xz = Mathf.LerpUnclamped(1f, 0.70f, eased) * squash;
            ApplyLayoutTransform(presentedThickness, xz);
            ApplyAppearanceImmediate(1, presentedFreezeProgress, presentedCrackAmount, 0f);
        }));
        
        activeSequence.OnComplete(() =>
        {
            FinishMelt();
        });
    }

    private void FinishMelt()
    {
        isMelting = false;
        presentedDurability = 0;
        targetDurability = 0;
        presentedThickness = 0f;
        presentedFreezeProgress = 0f;
        presentedCrackAmount = 0f;
        wasFrozen = false;
        activeSequence = null;
        UpdateDurabilityNumber(0);
        HideImmediate();
    }

    private void HideImmediate()
    {
        if (shell != null)
        {
            shell.gameObject.SetActive(false);
        }

        UpdateDurabilityNumber(0);
        presentedDurability = 0;
        targetDurability = 0;
    }

    private static float CrackAmountForDurability(int durability)
    {
        int stage = Mathf.Clamp(durability, 1, 3);
        return stage == 3 ? 0f : stage == 2 ? 0.45f : 0.90f;
    }

    private void ApplyAppearanceImmediate(int durabilityStage, float freezeProgress, float crackAmount, float shimmer)
    {
        if (shellRenderer == null)
        {
            return;
        }

        Material mat = GetSharedIceMaterial();
        shellRenderer.sharedMaterial = mat;
        var block = new MaterialPropertyBlock();
        shellRenderer.GetPropertyBlock(block);

        Texture2D iceTex = mat != null && mat.HasProperty("_IceTex") ? (Texture2D)mat.GetTexture("_IceTex") : null;
        if (iceTex != null)
        {
            block.SetTexture("_IceTex", iceTex);
        }

        block.SetFloat("_FreezeProgress", Mathf.Clamp01(freezeProgress));
        block.SetFloat("_CrackAmount", Mathf.Clamp01(crackAmount));
        block.SetFloat("_Shimmer", Mathf.Clamp01(shimmer));
        block.SetFloat("_Smoothness", 0.50f);

        shellRenderer.SetPropertyBlock(block);

        Debug.Log($"[ICE_TEXTURE_BINDING] IceView3D ID={GetInstanceID()} -> ShellRenderer='{shellRenderer.name}', Material='{(mat != null ? mat.name : "null")}', Shader='{(mat != null && mat.shader != null ? mat.shader.name : "null")}', _IceTex='{(iceTex != null ? iceTex.name : "NULL")}'");
    }

    private void KillOwnedTweens(bool complete)
    {
        if (activeSequence != null && activeSequence.IsActive())
        {
            activeSequence.Kill(complete);
        }

        activeSequence = null;
    }

    private void EnsureShell()
    {
        if (shell == null)
        {
            Transform existing = transform.Find("IceShell");
            if (existing != null)
            {
                shell = existing;
            }
            else if (ShapeNestVisualCatalog3D.TryGetIcePrefab(out GameObject icePrefab))
            {
                GameObject instance = Instantiate(icePrefab);
                instance.name = "IceShell";
                instance.transform.SetParent(transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                shell = instance.transform;
            }
            else
            {
                GameObject cube = new GameObject("IceShell");
                cube.transform.SetParent(transform, false);
                var filter = cube.AddComponent<MeshFilter>();
                filter.sharedMesh = BoardMeshFactory3D.GetRoundedBox(1f, 1f, 1f, 0.22f, 4);
                cube.AddComponent<MeshRenderer>();
                shell = cube.transform;
            }
        }

        if (shellRenderer == null)
        {
            shellRenderer = shell.GetComponent<MeshRenderer>();
            if (shellRenderer == null)
            {
                shellRenderer = shell.GetComponentInChildren<MeshRenderer>(true);
            }
        }

        if (shellRenderer != null && shellRenderer.sharedMaterial == null)
        {
            shellRenderer.sharedMaterial = GetSharedIceMaterial();
        }
        if (shellRenderer != null)
        {
            shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            shellRenderer.receiveShadows = true;
        }
    }

    private void EnsureDurabilityText()
    {
        if (durabilityText != null)
        {
            return;
        }

        Transform existing = transform.Find("IceDurabilityText");
        if (existing != null)
        {
            durabilityText = existing.GetComponent<TMPro.TextMeshPro>();
        }

        if (durabilityText == null)
        {
            GameObject textGo = new GameObject("IceDurabilityText");
            textGo.transform.SetParent(transform, false);
            durabilityText = textGo.AddComponent<TMPro.TextMeshPro>();
        }

        durabilityText.transform.localRotation = Quaternion.Euler(66f, 0f, 0f);
        durabilityText.alignment = TMPro.TextAlignmentOptions.Center;
        durabilityText.fontStyle = TMPro.FontStyles.Bold;
        durabilityText.fontSize = 2.0f;
        durabilityText.color = new Color(0.92f, 0.97f, 1.0f, 0.95f);

        if (durabilityText.fontMaterial != null)
        {
            durabilityText.fontMaterial.EnableKeyword("OUTLINE_ON");
            durabilityText.fontMaterial.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, 0.14f);
            durabilityText.fontMaterial.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, new Color(0.08f, 0.18f, 0.35f, 0.80f));
            durabilityText.fontMaterial.DisableKeyword("UNDERLAY_ON");
            durabilityText.fontMaterial.renderQueue = 3030;
        }

        var mr = durabilityText.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingOrder = 5;
        }
    }

    private void UpdateDurabilityNumber(int durability, bool animatePunch = false)
    {
        EnsureDurabilityText();
        if (durabilityText == null)
        {
            return;
        }

        if (durability <= 0 || isMelting || !wasFrozen)
        {
            durabilityText.gameObject.SetActive(false);
            return;
        }

        durabilityText.gameObject.SetActive(true);
        durabilityText.text = durability.ToString();

        if (animatePunch)
        {
            durabilityText.transform.DOKill();
            durabilityText.transform.localScale = Vector3.one * 1.35f;
            durabilityText.transform.DOScale(Vector3.one, 0.22f).SetEase(Ease.OutBack);
        }
    }

    private void ApplyShellMesh()
    {
        if (shell == null || ShapeNestVisualCatalog3D.TryGetIcePrefab(out _))
        {
            return;
        }

        MeshFilter filter = shell.GetComponent<MeshFilter>();
        if (filter == null)
        {
            filter = shell.gameObject.AddComponent<MeshFilter>();
        }

        // Smooth, chunky rounded translucent ice chunk surrounding the entire block
        filter.sharedMesh = BoardMeshFactory3D.GetRoundedBox(1f, 1f, 1f, 0.22f, 8);
    }

    private static BoardPresenter3D FindPresenter()
    {
        return Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
    }

    private static void GetBlockFootprint(Block block, out Vector2Int min, out Vector2Int max)
    {
        min = block.GridPosition;
        max = block.GridPosition;
        if (block.CellCount <= 0)
        {
            return;
        }

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        for (int i = 0; i < block.CellCount; i++)
        {
            Vector2Int world = block.GridPosition + block.GetLocalCell(i);
            minX = Mathf.Min(minX, world.x);
            minY = Mathf.Min(minY, world.y);
            maxX = Mathf.Max(maxX, world.x);
            maxY = Mathf.Max(maxY, world.y);
        }

        min = new Vector2Int(minX, minY);
        max = new Vector2Int(maxX, maxY);
    }

    /// <summary>Clears cached ice material so presentation retunes pick up after domain reload.</summary>
    public static void InvalidateSharedIceMaterial()
    {
        sharedIceMaterial = null;
    }

    public static Material GetSharedIceMaterial()
    {
        if (sharedIceMaterial != null)
        {
            return sharedIceMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Custom/IceCrystalShell")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard");

        sharedIceMaterial = new Material(shader)
        {
            name = "Ice3D_CrystallineRuntime"
        };

        // Pale cyan / white ice colors for ice.jpeg presentation casing:
        // Center: pale translucent glaze allowing block readability
        // Body: translucent pale cyan volume
        // Rim: frosted white highlight
        Color centerIce = new Color(0.90f, 0.97f, 1.0f, 0.22f);
        Color baseIce = new Color(0.82f, 0.93f, 0.97f, 0.58f);
        Color frostRim = new Color(0.96f, 0.99f, 1.0f, 0.92f);
        Color crackTint = new Color(1.0f, 1.0f, 1.0f, 0.95f);

        if (sharedIceMaterial.HasProperty("_CenterColor"))
        {
            sharedIceMaterial.SetColor("_CenterColor", centerIce);
        }

        if (sharedIceMaterial.HasProperty("_BaseColor"))
        {
            sharedIceMaterial.SetColor("_BaseColor", baseIce);
        }

        if (sharedIceMaterial.HasProperty("_FrostColor"))
        {
            sharedIceMaterial.SetColor("_FrostColor", frostRim);
        }

        if (sharedIceMaterial.HasProperty("_CrackColor"))
        {
            sharedIceMaterial.SetColor("_CrackColor", crackTint);
        }

        if (sharedIceMaterial.HasProperty("_Smoothness"))
        {
            sharedIceMaterial.SetFloat("_Smoothness", 0.88f);
        }

        // Bind the temporary ice.jpeg texture supplied by designer
        Texture2D iceTex = Resources.Load<Texture2D>("Ice/ice");
#if UNITY_EDITOR
        if (iceTex == null)
        {
            iceTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/ice.jpeg");
        }
#endif
        if (iceTex != null && sharedIceMaterial.HasProperty("_IceTex"))
        {
            sharedIceMaterial.SetTexture("_IceTex", iceTex);
        }

        if (sharedIceMaterial.HasProperty("_Surface"))
        {
            sharedIceMaterial.SetFloat("_Surface", 1f);
            sharedIceMaterial.SetFloat("_Blend", 0f);
            sharedIceMaterial.SetOverrideTag("RenderType", "Transparent");
            sharedIceMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            sharedIceMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            sharedIceMaterial.SetInt("_ZWrite", 0);
            sharedIceMaterial.renderQueue = 3020;
            sharedIceMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        return sharedIceMaterial;
    }
}
