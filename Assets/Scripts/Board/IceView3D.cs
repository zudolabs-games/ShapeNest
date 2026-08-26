using DG.Tweening;
using UnityEngine;

/// <summary>
/// World3D presentation for <see cref="IceState"/>. Gameplay ice state remains authoritative.
/// Owns DOTween durability transitions and melt; SyncFromSource never restarts an in-flight tween
/// toward the same target state.
/// </summary>
[DisallowMultipleComponent]
public class IceView3D : MonoBehaviour
{
    private const float DurabilityTransitionDuration = 0.22f;
    private const float MeltDuration = 0.34f;

    [SerializeField]
    private Transform shell;

    [SerializeField]
    private MeshRenderer shellRenderer;

    [SerializeField]
    private float thickness = 0.12f;

    [SerializeField]
    private float footprintPadding = 0.06f;

    [SerializeField]
    private float heightOverBlock = 0.04f;

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
    private float presentedThickness;
    private float presentedAlpha = 0.62f;
    private Color presentedEmission = new Color(0.25f, 0.75f, 1.1f) * 0.35f;

    public IceState Source => source;
    public bool IsBound => source != null;

    /// <summary>True while a durability transition or melt tween is running.</summary>
    public bool IsPresentationAnimating =>
        isMelting || (activeSequence != null && activeSequence.IsActive());

    public void Bind(IceState ice, Material material)
    {
        bool sameSource = source == ice;
        source = ice;
        EnsureShell();
        if (shellRenderer != null && material != null)
        {
            shellRenderer.sharedMaterial = material;
        }

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
        KillOwnedTweens(false);
        source = null;
        wasFrozen = false;
        presentedDurability = -1;
        targetDurability = -1;
        isMelting = false;
        meltVfxPlayed = false;
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
            if (wasFrozen || presentedDurability > 0)
            {
                BeginMelt();
            }
            else
            {
                HideImmediate();
            }

            wasFrozen = false;
            return;
        }

        wasFrozen = true;
        if (!RefreshLayoutMetrics())
        {
            shell.gameObject.SetActive(false);
            return;
        }

        shell.gameObject.SetActive(true);

        if (presentedDurability < 1)
        {
            // First show: snap to current gameplay durability (level load / bind).
            ApplyPresentedState(durability, animate: false);
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
            ApplyAppearanceImmediate(presentedDurability, presentedAlpha, presentedEmission);
            return;
        }

        BeginDurabilityTransition(presentedDurability, durability);
    }

    private void OnDisable()
    {
        KillOwnedTweens(false);
        isMelting = false;
        activeSequence = null;
    }

    private void OnDestroy()
    {
        KillOwnedTweens(false);
        isMelting = false;
        activeSequence = null;
    }

    private void ApplyPresentedState(int durability, bool animate)
    {
        durability = Mathf.Clamp(durability, 1, 3);
        RefreshLayoutMetrics();
        presentedDurability = durability;
        targetDurability = durability;
        presentedThickness = ThicknessForDurability(durability);
        presentedAlpha = AlphaForDurability(durability);
        presentedEmission = EmissionForDurability(durability);
        ApplyLayoutTransform(presentedThickness);
        ApplyAppearanceImmediate(durability, presentedAlpha, presentedEmission);
        EnsureCrackOverlays(durability);
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

        KillOwnedTweens(false);

        float fromThickness = ThicknessForDurability(from);
        float toThickness = ThicknessForDurability(to);
        float fromAlpha = AlphaForDurability(from);
        float toAlpha = AlphaForDurability(to);
        Color fromEmission = EmissionForDurability(from);
        Color toEmission = EmissionForDurability(to);

        presentedThickness = fromThickness;
        presentedAlpha = fromAlpha;
        presentedEmission = fromEmission;
        ApplyLayoutTransform(fromThickness);
        ApplyAppearanceImmediate(from, fromAlpha, fromEmission);
        EnsureCrackOverlays(from);

        activeSequence = DOTween.Sequence().SetLink(gameObject);
        activeSequence.Append(TweenAnimationUtility.Progress(DurabilityTransitionDuration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateSmoothStep(t);
            presentedThickness = Mathf.LerpUnclamped(fromThickness, toThickness, eased);
            presentedAlpha = Mathf.LerpUnclamped(fromAlpha, toAlpha, eased);
            presentedEmission = Color.LerpUnclamped(fromEmission, toEmission, eased);
            float pulse = 1f + (0.03f * Mathf.Sin(eased * Mathf.PI));
            ApplyLayoutTransform(presentedThickness, pulse);
            ApplyAppearanceImmediate(to, presentedAlpha, presentedEmission);
        }));
        activeSequence.OnComplete(() =>
        {
            presentedDurability = to;
            targetDurability = to;
            presentedThickness = toThickness;
            presentedAlpha = toAlpha;
            presentedEmission = toEmission;
            ApplyLayoutTransform(toThickness);
            ApplyAppearanceImmediate(to, toAlpha, toEmission);
            EnsureCrackOverlays(to);
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
        KillOwnedTweens(false);

        if (!shell.gameObject.activeSelf)
        {
            shell.gameObject.SetActive(true);
        }

        RefreshLayoutMetrics();
        float startThickness = presentedThickness > 0.001f
            ? presentedThickness
            : ThicknessForDurability(Mathf.Max(1, presentedDurability));
        float startAlpha = presentedAlpha;
        Color startEmission = presentedEmission;
        Vector3 startScale = shell.localScale;
        if (startScale.sqrMagnitude < 0.0001f)
        {
            startScale = new Vector3(layoutSizeX, startThickness, layoutSizeZ);
            shell.localScale = startScale;
        }

        if (!meltVfxPlayed)
        {
            meltVfxPlayed = true;
            BoardVfx3D.PlayIceMelt(transform.position);
        }

        float endThickness = startThickness * 0.15f;
        activeSequence = DOTween.Sequence().SetLink(gameObject);
        activeSequence.Append(TweenAnimationUtility.Progress(MeltDuration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseInQuad(t);
            float squash = Mathf.LerpUnclamped(1f, 1.08f, Mathf.Sin(t * Mathf.PI) * 0.5f);
            presentedThickness = Mathf.LerpUnclamped(startThickness, endThickness, eased);
            presentedAlpha = Mathf.LerpUnclamped(startAlpha, 0f, eased);
            presentedEmission = Color.LerpUnclamped(startEmission, Color.black, eased);
            float xz = Mathf.LerpUnclamped(1f, 0.72f, eased) * squash;
            ApplyLayoutTransform(presentedThickness, xz);
            ApplyAppearanceImmediate(1, presentedAlpha, presentedEmission);
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
        presentedAlpha = 0f;
        wasFrozen = false;
        activeSequence = null;
        HideImmediate();
    }

    private void HideImmediate()
    {
        if (shell != null)
        {
            shell.gameObject.SetActive(false);
        }

        presentedDurability = 0;
        targetDurability = 0;
    }

    private bool RefreshLayoutMetrics()
    {
        Block block = source != null ? source.GetComponent<Block>() : null;
        if (block == null || block.Board == null)
        {
            return false;
        }

        BoardPresenter3D presenter = FindPresenter();
        if (presenter == null)
        {
            return false;
        }

        GetBlockFootprint(block, out Vector2Int min, out Vector2Int max);
        IGridSpace space = presenter.GridSpace;
        Vector3 a = space.GridToWorld(min);
        Vector3 b = space.GridToWorld(max);
        Vector3 center = (a + b) * 0.5f;
        float cell = presenter.CellWorldSize;
        layoutScale = cell / BoardAdaptivePresentation3D.ReferenceCellSize;
        float pad = footprintPadding * layoutScale;
        layoutSizeX = (max.x - min.x + 1) * cell + pad * 2f;
        layoutSizeZ = (max.y - min.y + 1) * cell + pad * 2f;

        float surfaceY = presenter.CellSurfaceWorldY;
        float blockHeight = BoardAdaptivePresentation3D.BlockHeightRatio * cell;
        if (block.WorldView != null)
        {
            blockHeight = block.WorldView.PieceHeight;
        }

        float stageThickness = presentedThickness > 0.001f
            ? presentedThickness
            : ThicknessForDurability(Mathf.Max(1, source.Durability));
        float blockTop = surfaceY + blockHeight + heightOverBlock * layoutScale;
        center.y = blockTop + stageThickness * 0.5f;
        transform.position = center;
        if (shell != null)
        {
            shell.localPosition = Vector3.zero;
        }

        return true;
    }

    private void ApplyLayoutTransform(float stageThickness, float xzMultiplier = 1f)
    {
        if (shell == null)
        {
            return;
        }

        RefreshLayoutMetrics();
        float y = Mathf.Max(0.001f, stageThickness);
        shell.localScale = new Vector3(
            layoutSizeX * xzMultiplier,
            y,
            layoutSizeZ * xzMultiplier);
    }

    private float ThicknessForDurability(int durability)
    {
        int stage = Mathf.Clamp(durability, 1, 3);
        return thickness * layoutScale * (0.75f + 0.12f * stage);
    }

    private static float AlphaForDurability(int durability)
    {
        int stage = Mathf.Clamp(durability, 1, 3);
        return stage == 3 ? 0.62f : stage == 2 ? 0.5f : 0.4f;
    }

    private static Color EmissionForDurability(int durability)
    {
        int stage = Mathf.Clamp(durability, 1, 3);
        float crack = (3 - stage) / 2f;
        return new Color(0.25f, 0.75f, 1.1f) * (0.35f + crack * 0.25f);
    }

    private void ApplyAppearanceImmediate(int durabilityStage, float alpha, Color emission)
    {
        if (shellRenderer == null)
        {
            return;
        }

        shellRenderer.sharedMaterial = GetSharedIceMaterial();
        var block = new MaterialPropertyBlock();
        shellRenderer.GetPropertyBlock(block);
        Color c = new Color(0.45f, 0.92f, 1f, alpha);
        block.SetColor("_BaseColor", c);
        block.SetColor("_Color", c);
        block.SetColor("_EmissionColor", emission);
        shellRenderer.SetPropertyBlock(block);
        EnsureCrackOverlays(Mathf.Clamp(durabilityStage, 1, 3));
    }

    private void EnsureCrackOverlays(int durabilityStage)
    {
        Transform cracks = transform.Find("IceCracks");
        if (cracks == null && shell != null)
        {
            cracks = shell.Find("IceCracks");
        }

        if (cracks == null)
        {
            var go = new GameObject("IceCracks");
            go.transform.SetParent(shell != null ? shell : transform, false);
            cracks = go.transform;
            CreateCrackLine(cracks, "CrackA", new Vector3(0.15f, 0.51f, -0.1f), 28f);
            CreateCrackLine(cracks, "CrackB", new Vector3(-0.12f, 0.51f, 0.18f), -35f);
            CreateCrackLine(cracks, "CrackC", new Vector3(0.02f, 0.51f, 0.05f), 70f);
        }

        int visible = durabilityStage >= 3 ? 0 : durabilityStage == 2 ? 1 : 3;
        for (int i = 0; i < cracks.childCount; i++)
        {
            cracks.GetChild(i).gameObject.SetActive(i < visible);
        }
    }

    private void KillOwnedTweens(bool complete)
    {
        if (activeSequence != null && activeSequence.IsActive())
        {
            activeSequence.Kill(complete);
        }

        activeSequence = null;
    }

    private static void CreateCrackLine(Transform parent, string name, Vector3 localPos, float yaw)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = name;
        line.transform.SetParent(parent, false);
        line.transform.localPosition = localPos;
        line.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        line.transform.localScale = new Vector3(0.72f, 0.02f, 0.035f);
        Collider col = line.GetComponent<Collider>();
        if (col != null)
        {
            Object.Destroy(col);
        }

        MeshRenderer renderer = line.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader)
            {
                name = "IceCrack3D",
                color = new Color(0.45f, 0.7f, 0.9f, 0.65f)
            };
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", mat.color);
            }

            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3001;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
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
            else
            {
                GameObject cube = new GameObject("IceShell");
                cube.transform.SetParent(transform, false);
                var filter = cube.AddComponent<MeshFilter>();
                filter.sharedMesh = BoardMeshFactory3D.GetRoundedBox(1f, 1f, 1f, 0.18f, 3);
                cube.AddComponent<MeshRenderer>();
                shell = cube.transform;
            }
        }

        if (shellRenderer == null)
        {
            shellRenderer = shell.GetComponent<MeshRenderer>();
        }

        if (shellRenderer != null && shellRenderer.sharedMaterial == null)
        {
            shellRenderer.sharedMaterial = GetSharedIceMaterial();
        }
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

    public static Material GetSharedIceMaterial()
    {
        if (sharedIceMaterial != null)
        {
            return sharedIceMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        sharedIceMaterial = new Material(shader)
        {
            name = "Ice3D_Runtime",
            color = new Color(0.45f, 0.92f, 1f, 0.55f)
        };
        if (sharedIceMaterial.HasProperty("_BaseColor"))
        {
            sharedIceMaterial.SetColor("_BaseColor", sharedIceMaterial.color);
        }

        if (sharedIceMaterial.HasProperty("_Smoothness"))
        {
            sharedIceMaterial.SetFloat("_Smoothness", 0.95f);
        }

        if (sharedIceMaterial.HasProperty("_Metallic"))
        {
            sharedIceMaterial.SetFloat("_Metallic", 0.12f);
        }

        if (sharedIceMaterial.HasProperty("_EmissionColor"))
        {
            sharedIceMaterial.EnableKeyword("_EMISSION");
            sharedIceMaterial.SetColor("_EmissionColor", new Color(0.3f, 0.85f, 1.2f) * 0.45f);
        }

        if (sharedIceMaterial.HasProperty("_Surface"))
        {
            sharedIceMaterial.SetFloat("_Surface", 1f);
            sharedIceMaterial.SetFloat("_Blend", 0f);
            sharedIceMaterial.SetOverrideTag("RenderType", "Transparent");
            sharedIceMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            sharedIceMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            sharedIceMaterial.SetInt("_ZWrite", 0);
            sharedIceMaterial.renderQueue = 3000;
            sharedIceMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        return sharedIceMaterial;
    }
}
