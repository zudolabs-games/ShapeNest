using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation-only match/merge effect. Does not touch occupancy, matching, or completion.
/// Glow is World3D (SpriteRenderer under BoardPresenter3D.VfxRoot); dissolve still drives Block/Target.
/// </summary>
public class MatchEffect : MonoBehaviour
{
    [SerializeField]
    private Sprite squareGlow;

    [SerializeField]
    private Sprite circleGlow;

    [SerializeField]
    private Sprite triangleGlow;

    [SerializeField]
    private Sprite diamondGlow;

    [SerializeField]
    private Sprite hexagonGlow;

    [SerializeField]
    private Sprite starGlow;

    [SerializeField]
    [Tooltip("Optional. Theme glow sprites override prefab sprites when assigned.")]
    private ShapeNestTheme theme;

    [SerializeField]
    private Image glowImage;

    [SerializeField]
    private Image outlineImage;

    [SerializeField]
    private Color glowColor = new Color(1f, 0.95f, 0.7f, 1f);

    [SerializeField]
    [Range(1.05f, 1.1f)]
    [Tooltip("Peak piece scale at match contact, relative to captured rest scale.")]
    private float impactScale = 1.08f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Duration of the contact click pulse.")]
    private float impactDuration = 0.12f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Full glow lifetime: appear, expand slightly, fade out.")]
    private float glowDuration = 0.22f;

    [SerializeField]
    [Min(0.5f)]
    [Tooltip("Glow footprint relative to the cell. 1 matches the block/target silhouette.")]
    private float glowScale = 1f;

    [SerializeField]
    [Range(0.2f, 1f)]
    [Tooltip("Peak glow opacity.")]
    private float glowPeakAlpha = 0.85f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Block and target shrink/fade after the contact pulse.")]
    private float dissolveDuration = 0.1f;

    private Sequence playSequence;
    private Block presentedBlock;
    private Target presentedTarget;
    private ShapeType presentedShape;
    private bool presentationStarted;
    private bool presentationFinalized;

    private bool worldPresentationReady;
    private Vector3 resolvedWorldPosition;
    private float cellWorldSize = 1f;
    private float baseFootprint = 1f;
    private SpriteRenderer glowRenderer;
    private SpriteRenderer outlineRenderer;
    private static Material sharedSpriteMaterial;

    /// <summary>
    /// Legacy accessor for old UI callers. World3D path does not use RectTransform positioning.
    /// </summary>
    public RectTransform RectTransform => transform as RectTransform;

    private void OnDisable()
    {
        FinalizePresentation(playVfx: false);
    }

    private void OnDestroy()
    {
        FinalizePresentation(playVfx: false);
    }

    /// <summary>
    /// Parents under a World3D VFX root and places the glow at the matched nest world position.
    /// Must be called before <see cref="Play"/>.
    /// </summary>
    public void SetupWorldPresentation(Transform vfxRoot, Vector3 nestWorldPosition, float presentationCellSize)
    {
        if (vfxRoot != null)
        {
            transform.SetParent(vfxRoot, false);
        }

        resolvedWorldPosition = nestWorldPosition;
        cellWorldSize = Mathf.Max(0.01f, presentationCellSize);
        // Anchor exactly on the visible nest AABB center (XZ + Y). No extra lift —
        // small Y offsets read as screen-space separation under the near-top-down ortho camera.
        transform.position = nestWorldPosition;
        // Flat on the board (XZ), facing up toward the ortho camera.
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        baseFootprint = cellWorldSize * 0.92f;
        EnsureWorldVisuals();
        DisableLegacyUiImages();
        worldPresentationReady = true;
        SetGlow(glowScale * 0.94f, 0f);
    }

    public IEnumerator Play(ShapeType shapeType, Block block, Target target)
    {
        presentedShape = shapeType;
        presentedBlock = block;
        presentedTarget = target;
        presentationStarted = true;
        presentationFinalized = false;

        if (!worldPresentationReady)
        {
            // Safety: if caller forgot SetupWorldPresentation, still try World3D from views.
            Vector3 fallback = ResolveFallbackWorldPosition(block, target);
            float cell = 1f;
            BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
            if (presenter != null)
            {
                cell = presenter.CellWorldSize;
                SetupWorldPresentation(presenter.VfxRoot, fallback, cell);
            }
            else
            {
                EnsureWorldVisuals();
                DisableLegacyUiImages();
                transform.position = fallback;
                baseFootprint = 1f;
                worldPresentationReady = true;
            }
        }

        Sprite sprite = ShapeVisuals.SpriteFor(
            shapeType,
            ShapeVisuals.First(theme != null ? theme.matchSquareGlow : null, squareGlow),
            ShapeVisuals.First(theme != null ? theme.matchCircleGlow : null, circleGlow),
            ShapeVisuals.First(theme != null ? theme.matchTriangleGlow : null, triangleGlow),
            ShapeVisuals.First(theme != null ? theme.matchDiamondGlow : null, diamondGlow),
            ShapeVisuals.First(theme != null ? theme.matchHexagonGlow : null, hexagonGlow),
            ShapeVisuals.First(theme != null ? theme.matchStarGlow : null, starGlow));
        ApplyWorldSprite(glowRenderer, sprite);
        ApplyWorldSprite(outlineRenderer, sprite);
        SetGlow(glowScale * 0.94f, 0f);

        if (block != null)
        {
            block.BeginMatchPresentation();
            block.SetMatchPresentation(1f, 1f);
        }

        if (target != null)
        {
            target.BeginMatchPresentation();
            target.SetMatchPresentation(1f, 1f);
        }

        KillSequenceOnly();
        float contactDuration = Mathf.Max(impactDuration, glowDuration);
        playSequence = DOTween.Sequence().SetLink(gameObject);

        playSequence.Append(TweenAnimationUtility.Progress(contactDuration, t =>
        {
            float elapsed = t * contactDuration;
            ApplyImpact(block, target, elapsed);
            EvaluateGlow(elapsed / glowDuration, out float glowSize, out float glowAlpha);
            SetGlow(glowSize, glowAlpha);
        }));

        playSequence.AppendCallback(() =>
        {
            ApplyImpact(block, target, impactDuration);
            SetGlow(glowScale, 0f);
        });

        playSequence.Append(TweenAnimationUtility.Progress(dissolveDuration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateSmoothStep(t);
            float pieceScale = Mathf.LerpUnclamped(1f, 0f, eased);
            float pieceAlpha = Mathf.LerpUnclamped(1f, 0f, eased);
            if (block != null)
            {
                block.SetMatchPresentation(pieceScale, pieceAlpha);
            }

            if (target != null)
            {
                target.SetMatchPresentation(pieceScale, pieceAlpha);
            }
        }));

        playSequence.OnComplete(() =>
        {
            FinalizePresentation(playVfx: true);
        });

        yield return TweenAnimationUtility.Wait(playSequence);
    }

    /// <summary>
    /// Presentation cleanup only. Safe to call multiple times; does not change gameplay state.
    /// </summary>
    public void AbortPresentation()
    {
        FinalizePresentation(playVfx: false);
    }

    private void FinalizePresentation(bool playVfx)
    {
        if (!presentationStarted || presentationFinalized)
        {
            KillSequenceOnly();
            return;
        }

        presentationFinalized = true;
        KillSequenceOnly();

        if (playVfx)
        {
            PlayNestMatchVfx();
        }

        if (presentedBlock != null)
        {
            presentedBlock.CompleteMatchPresentation();
        }

        if (presentedTarget != null)
        {
            presentedTarget.CompleteMatchPresentation();
        }

        SetGlow(0f, 0f);
        presentedBlock = null;
        presentedTarget = null;
    }

    private void PlayNestMatchVfx()
    {
        // Use the center resolved at SetupWorldPresentation (visible nest AABB), not live
        // WorldView.position — keeps burst aligned with the glow and avoids Triangle/Star bias.
        Vector3 worldPos = resolvedWorldPosition;
        if (worldPos.sqrMagnitude < 0.0000001f)
        {
            worldPos = ResolveFallbackWorldPosition(presentedBlock, presentedTarget);
        }

        BoardVfx3D.PlayNestMatch(worldPos, ShapeVisuals3D.AccentColor(presentedShape));
    }

    private static Vector3 ResolveFallbackWorldPosition(Block block, Target target)
    {
        if (TryGetVisiblePieceCenter(target != null ? target.WorldView : null, out Vector3 nestCenter))
        {
            return nestCenter;
        }

        if (TryGetVisiblePieceCenter(block != null ? block.WorldView : null, out Vector3 blockCenter))
        {
            return blockCenter;
        }

        if (target != null && target.WorldView != null)
        {
            return target.WorldView.transform.position;
        }

        if (block != null && block.WorldView != null)
        {
            return block.WorldView.transform.position;
        }

        return Vector3.zero;
    }

    private static bool TryGetVisiblePieceCenter(PieceView3D pieceView, out Vector3 center)
    {
        center = Vector3.zero;
        if (pieceView == null || !pieceView.gameObject.activeInHierarchy)
        {
            return false;
        }

        MeshRenderer renderer = pieceView.GetComponentInChildren<MeshRenderer>();
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
        {
            return false;
        }

        // XZ = visual AABB center (Phase 19D); Y = nest top surface (Phase 19F).
        Bounds bounds = renderer.bounds;
        center = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        return true;
    }

    private void KillSequenceOnly()
    {
        if (playSequence != null && playSequence.IsActive())
        {
            playSequence.Kill(false);
        }

        playSequence = null;
    }

    private void ApplyImpact(Block block, Target target, float elapsed)
    {
        float pieceScale = ImpactMultiplier(elapsed, impactDuration, impactScale);
        if (block != null)
        {
            block.SetMatchPresentation(pieceScale, 1f);
        }

        if (target != null)
        {
            target.SetMatchPresentation(pieceScale, 1f);
        }
    }

    private void EvaluateGlow(float t, out float scale, out float alpha)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.28f)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / 0.28f);
            scale = Mathf.LerpUnclamped(0.94f, 1f, u) * glowScale;
            alpha = Mathf.LerpUnclamped(0f, glowPeakAlpha, u);
            return;
        }

        if (t < 0.48f)
        {
            float u = Mathf.SmoothStep(0f, 1f, (t - 0.28f) / 0.2f);
            scale = Mathf.LerpUnclamped(1f, 1.04f, u) * glowScale;
            alpha = glowPeakAlpha;
            return;
        }

        float fade = Mathf.SmoothStep(0f, 1f, (t - 0.48f) / 0.52f);
        scale = Mathf.LerpUnclamped(1.04f, 1f, fade) * glowScale;
        alpha = Mathf.LerpUnclamped(glowPeakAlpha, 0f, fade);
    }

    private static float ImpactMultiplier(float elapsed, float duration, float peak)
    {
        if (duration <= 0f)
        {
            return 1f;
        }

        float t = Mathf.Clamp01(elapsed / duration);
        const float rise = 0.45f;
        if (t < rise)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / rise);
            return Mathf.LerpUnclamped(1f, peak, u);
        }

        float v = Mathf.SmoothStep(0f, 1f, (t - rise) / (1f - rise));
        return Mathf.LerpUnclamped(peak, 1f, v);
    }

    private void SetGlow(float scale, float alpha)
    {
        SetWorldSprite(glowRenderer, scale, alpha * 0.7f);
        SetWorldSprite(outlineRenderer, scale, alpha);
    }

    private void SetWorldSprite(SpriteRenderer renderer, float scale, float alpha)
    {
        if (renderer == null)
        {
            return;
        }

        // scale is the impact/pulse multiplier (≈0.94–1.04), not a world size.
        // Sprite bounds are ~1.28 units; divide so WORLD footprint = baseFootprint * multiplier.
        float desiredWorld = baseFootprint * Mathf.Max(0.01f, scale);
        float spriteWidth = renderer.sprite != null ? renderer.sprite.bounds.size.x : 0f;
        float spriteScale = spriteWidth > 0.0001f
            ? desiredWorld / spriteWidth
            : desiredWorld;
        renderer.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);
        Color color = glowColor;
        color.a = glowColor.a * Mathf.Clamp01(alpha);
        renderer.color = color;
        renderer.enabled = alpha > 0.001f && renderer.sprite != null;
    }

    private void EnsureWorldVisuals()
    {
        if (glowRenderer == null)
        {
            glowRenderer = CreateWorldSprite("Glow3D", 0);
        }

        if (outlineRenderer == null)
        {
            outlineRenderer = CreateWorldSprite("Outline3D", 1);
        }
    }

    private SpriteRenderer CreateWorldSprite(string childName, int sortingOrder)
    {
        Transform existing = transform.Find(childName);
        GameObject child = existing != null ? existing.gameObject : new GameObject(childName);
        if (existing == null)
        {
            child.transform.SetParent(transform, false);
        }

        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;

        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null)
        {
            renderer = child.AddComponent<SpriteRenderer>();
        }

        renderer.sharedMaterial = GetSharedSpriteMaterial();
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        return renderer;
    }

    private void DisableLegacyUiImages()
    {
        if (glowImage != null)
        {
            glowImage.enabled = false;
            glowImage.gameObject.SetActive(false);
        }

        if (outlineImage != null)
        {
            outlineImage.enabled = false;
            outlineImage.gameObject.SetActive(false);
        }

        // Hide any leftover runtime UI Image children from older prefab instances.
        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null)
            {
                images[i].enabled = false;
                images[i].gameObject.SetActive(false);
            }
        }
    }

    private static void ApplyWorldSprite(SpriteRenderer renderer, Sprite sprite)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.sprite = sprite;
        renderer.enabled = sprite != null;
    }

    private static Material GetSharedSpriteMaterial()
    {
        if (sharedSpriteMaterial != null)
        {
            return sharedSpriteMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
            ?? Shader.Find("Universal Render Pipeline/Unlit");
        sharedSpriteMaterial = new Material(shader)
        {
            name = "MatchGlow3D_Runtime"
        };
        return sharedSpriteMaterial;
    }
}
