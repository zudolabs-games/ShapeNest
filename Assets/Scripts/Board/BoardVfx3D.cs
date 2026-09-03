using DG.Tweening;
using UnityEngine;

/// <summary>
/// Ephemeral World3D presentation effects. Never delays gameplay; effects self-destroy.
/// Phase 52L/52N: molded-plastic mesh particles + soft contact bursts (presentation only).
/// </summary>
public static class BoardVfx3D
{
    private static Transform effectsRoot;
    private static Material sharedBurstMaterial;
    private static Material sharedRingMaterial;
    private static Mesh sharedParticleMesh;

    private enum BurstFeel
    {
        MatchFlash,
        MatchBurst,
        IceMelt,
        ShutterOpen,
        HammerImpact,
        HammerFlash,
        HammerCell,
        HammerNest,
        Landing
    }

    public static void SetEffectsRoot(Transform root)
    {
        effectsRoot = root;
    }

    public static void ClearAll()
    {
        Transform root = ResolveRoot();
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null || child.name == HammerSmashView3D.ObjectName)
            {
                continue;
            }

            // Immediate destroy so level/restart cleanup leaves no one-frame VFX ghosts.
            Object.DestroyImmediate(child.gameObject);
        }
    }

    public static void PlayLanding(Vector3 worldPosition)
    {
        float s = PresentationScale();
        SpawnBurst(
            worldPosition,
            SoftCream(0.42f),
            5,
            0.14f * s,
            0.22f,
            "LandingVFX",
            0.048f * s,
            BurstFeel.Landing);
    }

    public static void PlayNestMatch(Vector3 worldPosition, Color accent)
    {
        float s = PresentationScale();
        // Soft radial settle after contact flash — same call site / no gameplay wait.
        Color c = SoftAccent(accent, 0.62f, 0.68f);
        SpawnBurst(
            worldPosition + Vector3.up * (0.038f * s),
            c,
            8,
            0.20f * s,
            0.30f,
            "NestMatchBurstVFX",
            0.048f * s,
            BurstFeel.MatchBurst);
        Color ring = c;
        ring.a = 0.32f;
        SpawnRing(worldPosition, ring, 0.30f, "NestMatchRingVFX", 0.82f);
    }

    /// <summary>
    /// Phase 52H/52L/52N: brief contact flash at match impact start. Presentation only; no gameplay wait.
    /// </summary>
    public static void PlayNestMatchImpactFlash(Vector3 worldPosition, Color accent)
    {
        float s = PresentationScale();
        Color c = SoftAccent(accent, 0.70f, 0.58f);
        SpawnBurst(
            worldPosition + Vector3.up * (0.028f * s),
            c,
            5,
            0.11f * s,
            0.16f,
            "NestMatchFlashVFX",
            0.065f * s,
            BurstFeel.MatchFlash);
        Color ring = c;
        ring.a = 0.28f;
        SpawnRing(worldPosition, ring, 0.14f, "NestMatchFlashRingVFX", 0.62f);
    }

    public static void PlayIceMelt(Vector3 worldPosition)
    {
        float s = PresentationScale();
        // Soft frost breakup — pale blue-white, upward float. Timing owned by IceView3D.
        Color frost = new Color(0.78f, 0.90f, 0.96f, 0.55f);
        SpawnBurst(
            worldPosition + Vector3.up * (0.035f * s),
            frost,
            8,
            0.14f * s,
            0.36f,
            "IceMeltVFX",
            0.040f * s,
            BurstFeel.IceMelt);
        Color mist = new Color(0.88f, 0.94f, 0.98f, 0.28f);
        SpawnBurst(
            worldPosition + Vector3.up * (0.018f * s),
            mist,
            4,
            0.08f * s,
            0.26f,
            "IceMeltMistVFX",
            0.055f * s,
            BurstFeel.IceMelt);
    }

    public static void PlayShutterOpen(Vector3 worldPosition)
    {
        float s = PresentationScale();
        // Warm molded-plastic release — not neon orange. Timing owned by ShutterView3D.
        Color plastic = new Color(0.86f, 0.74f, 0.52f, 0.52f);
        SpawnBurst(
            worldPosition + Vector3.up * (0.05f * s),
            plastic,
            6,
            0.16f * s,
            0.26f,
            "ShutterOpenVFX",
            0.042f * s,
            BurstFeel.ShutterOpen);
        Color dust = new Color(0.82f, 0.76f, 0.64f, 0.30f);
        SpawnBurst(
            worldPosition + Vector3.up * (0.025f * s),
            dust,
            4,
            0.10f * s,
            0.20f,
            "ShutterOpenDustVFX",
            0.050f * s,
            BurstFeel.ShutterOpen);
        Color ring = plastic;
        ring.a = 0.26f;
        SpawnRing(worldPosition, ring, 0.18f, "ShutterOpenRingVFX", 0.70f);
    }

    /// <summary>
    /// Presentation-only Hammer smash at a world position. Does not delay gameplay.
    /// <paramref name="footprintMul"/> scales the ring for multi-cell chains (1 = single cell).
    /// </summary>
    public static void PlayHammerImpact(Vector3 worldPosition, Color accent, float footprintMul = 1f)
    {
        float s = PresentationScale();
        float footprint = Mathf.Clamp(footprintMul, 1f, 1.85f);
        Color c = SoftAccent(accent, 0.55f, 0.62f);
        int count = Mathf.Clamp(10 + Mathf.RoundToInt((footprint - 1f) * 4f), 10, 15);
        SpawnBurst(
            worldPosition + Vector3.up * (0.06f * s),
            c,
            count,
            0.32f * s * Mathf.Lerp(1f, 1.08f, footprint - 1f),
            0.28f,
            "HammerImpactVFX",
            0.09f * s,
            BurstFeel.HammerImpact);
        // Soft warm contact flash — not a white screen blast.
        Color flash = SoftCream(0.48f);
        SpawnBurst(
            worldPosition + Vector3.up * (0.08f * s),
            flash,
            5,
            0.15f * s,
            0.12f,
            "HammerImpactFlashVFX",
            0.10f * s,
            BurstFeel.HammerFlash);
        // Ground / contact pulse — thin board-relative ring.
        Color ring = c;
        ring.a = 0.34f;
        SpawnRing(worldPosition, ring, 0.22f, "HammerImpactRingVFX", footprint * 0.92f);
    }

    /// <summary>
    /// Small presentation-only fragment burst at one chain cell. Same Hammer operation.
    /// </summary>
    public static void PlayHammerCellBurst(Vector3 worldPosition, Color accent)
    {
        float s = PresentationScale();
        Color c = SoftAccent(accent, 0.50f, 0.55f);
        SpawnBurst(
            worldPosition + Vector3.up * (0.04f * s),
            c,
            5,
            0.18f * s,
            0.24f,
            "HammerCellVFX",
            0.07f * s,
            BurstFeel.HammerCell);
    }

    /// <summary>
    /// Presentation-only mesh shards at a PieceView3D. Never registers Blocks or occupancy.
    /// </summary>
    public static void PlayHammerBreakFragments(PieceView3D view, Color accent)
    {
        if (view == null || !Application.isPlaying)
        {
            return;
        }

        Transform root = ResolveRoot();
        if (root == null)
        {
            return;
        }

        MeshRenderer[] renderers = view.GetComponentsInChildren<MeshRenderer>(false);
        float s = PresentationScale();
        int spawned = 0;
        const int maxPerView = 8;
        for (int r = 0; r < renderers.Length; r++)
        {
            MeshRenderer meshRenderer = renderers[r];
            if (meshRenderer == null
                || !meshRenderer.enabled
                || !meshRenderer.gameObject.activeInHierarchy
                || meshRenderer.name.IndexOf("Shadow") >= 0)
            {
                continue;
            }

            MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            int remaining = maxPerView - spawned;
            if (remaining <= 0)
            {
                break;
            }

            bool inner = IsNestedRenderer(meshRenderer);
            int shards = Mathf.Min(remaining, inner ? 4 : 6);
            Bounds bounds = meshRenderer.bounds;
            Vector3 origin = bounds.center;
            for (int i = 0; i < shards; i++)
            {
                int seed = i + spawned + (r * 11);
                Vector3 spawn = origin;
                spawn.x += (((seed % 3) - 1) * bounds.extents.x * 0.32f);
                spawn.y += bounds.extents.y * (0.08f + ((seed % 4) * 0.05f));
                spawn.z += (((seed % 5) - 2) * bounds.extents.z * 0.22f);
                SpawnHammerFragment(
                    root,
                    spawn,
                    filter.sharedMesh,
                    meshRenderer.sharedMaterial,
                    accent,
                    s,
                    seed,
                    bounds);
            }

            spawned += shards;
        }
    }

    private static bool IsNestedRenderer(MeshRenderer meshRenderer)
    {
        Transform current = meshRenderer != null ? meshRenderer.transform : null;
        while (current != null)
        {
            if (current.name.IndexOf("Nested") >= 0)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void SpawnHammerFragment(
        Transform root,
        Vector3 origin,
        Mesh mesh,
        Material material,
        Color accent,
        float scale,
        int seed,
        Bounds sourceBounds)
    {
        var go = new GameObject("HammerFragmentVFX");
        go.transform.SetParent(root, false);
        go.transform.position = origin;
        go.transform.rotation = Quaternion.Euler(seed * 37f, seed * 53f, seed * 19f);

        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material != null ? material : GetBurstMaterial();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        // Source mesh is unit-sized; world bounds carry the actual 3D piece proportions.
        float mul = 0.20f + ((seed % 3) * 0.045f);
        Vector3 fragmentScale = new Vector3(
            Mathf.Max(0.08f, sourceBounds.size.x) * mul,
            Mathf.Max(0.06f, sourceBounds.size.y) * mul,
            Mathf.Max(0.08f, sourceBounds.size.z) * mul);
        go.transform.localScale = fragmentScale;

        var animator = go.AddComponent<HammerFragmentAnimator>();
        Vector3 outward = origin - sourceBounds.center;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = new Vector3(Mathf.Sin(seed * 1.7f + 0.4f), 0f, Mathf.Cos(seed * 1.3f + 1.1f));
        }

        outward.Normalize();
        // Slightly tighter scatter — still same 0.50s fragment lifetime.
        Vector3 velocity = (outward * (0.44f * scale)) + (Vector3.up * (0.50f + ((seed % 4) * 0.07f)) * scale);
        animator.Play(velocity, 0.50f, accent);
    }

    /// <summary>
    /// Subtle nest fade-burst when Hammer removes a corresponding target. Not a match VFX.
    /// </summary>
    public static void PlayHammerNest(Vector3 worldPosition, Color accent)
    {
        float s = PresentationScale();
        Color c = SoftAccent(accent, 0.58f, 0.45f);
        SpawnBurst(
            worldPosition + Vector3.up * (0.035f * s),
            c,
            6,
            0.18f * s,
            0.22f,
            "HammerNestVFX",
            0.07f * s,
            BurstFeel.HammerNest);
        Color ring = c;
        ring.a = 0.28f;
        SpawnRing(worldPosition, ring, 0.18f, "HammerNestRingVFX", 0.75f);
    }

    /// <summary>
    /// Drops leftover Hammer presentation objects. Safe on level load/restart.
    /// Does not touch gameplay or non-Hammer VFX.
    /// </summary>
    public static void ClearHammerEffects()
    {
        Transform root = ResolveRoot();
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null
                || !child.name.StartsWith("Hammer")
                || child.name == HammerSmashView3D.ObjectName)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(child.gameObject);
            }
            else
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }
    }

    /// <summary>
    /// True while Hammer smash fragments, bursts, or rings still exist under VfxRoot.
    /// Does not include the persistent HammerSmashView3D object.
    /// </summary>
    public static bool HasActiveHammerPresentation()
    {
        Transform root = ResolveRoot();
        if (root == null)
        {
            return false;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null
                || !child.gameObject.activeInHierarchy
                || !child.name.StartsWith("Hammer")
                || child.name == HammerSmashView3D.ObjectName)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static float PresentationScale()
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter == null)
        {
            return 1f;
        }

        return Mathf.Max(0.05f, presenter.CellWorldSize / BoardAdaptivePresentation3D.ReferenceCellSize);
    }

    private static Transform ResolveRoot()
    {
        if (effectsRoot != null)
        {
            return effectsRoot;
        }

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter != null)
        {
            effectsRoot = presenter.VfxRoot;
        }

        return effectsRoot;
    }

    private static void SpawnBurst(
        Vector3 worldPosition,
        Color color,
        int count,
        float speed,
        float lifetime,
        string objectName,
        float startSize,
        BurstFeel feel)
    {
        Transform root = ResolveRoot();
        if (root == null || !Application.isPlaying)
        {
            return;
        }

        float s = PresentationScale();
        var go = new GameObject(string.IsNullOrEmpty(objectName) ? "VfxBurst3D" : objectName);
        go.transform.SetParent(root, false);
        go.transform.position = worldPosition;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = lifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.72f, speed);
        main.startSize = startSize > 0f ? startSize : 0.05f * s;
        main.startColor = color;
        main.maxParticles = count;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = GravityForFeel(feel);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = ShapeRadiusForFeel(feel, s);

        var sizeOver = ps.sizeOverLifetime;
        sizeOver.enabled = true;
        sizeOver.size = new ParticleSystem.MinMaxCurve(1f, SoftSizeCurve());

        var colorOver = ps.colorOverLifetime;
        colorOver.enabled = true;
        colorOver.color = SoftFadeGradient(color);

        var velocity = ps.velocityOverLifetime;
        if (feel == BurstFeel.IceMelt)
        {
            // All axes must share the same MinMaxCurve mode (TwoConstants).
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.08f * s, 0.22f * s);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }
        else if (feel == BurstFeel.ShutterOpen)
        {
            // All axes must share the same MinMaxCurve mode (TwoConstants).
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.04f * s, 0.14f * s);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }
        else
        {
            velocity.enabled = false;
        }

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = GetSharedParticleMesh();
        renderer.sharedMaterial = GetBurstMaterial();
        renderer.alignment = ParticleSystemRenderSpace.World;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
        Object.Destroy(go, lifetime + 0.15f);
    }

    private static float GravityForFeel(BurstFeel feel)
    {
        switch (feel)
        {
            case BurstFeel.IceMelt:
                return -0.08f;
            case BurstFeel.ShutterOpen:
                return 0.55f;
            case BurstFeel.MatchFlash:
                return 0.12f;
            case BurstFeel.HammerFlash:
                return 0.20f;
            case BurstFeel.HammerImpact:
                return 0.70f;
            case BurstFeel.Landing:
                return 0.90f;
            default:
                return 0.40f;
        }
    }

    private static float ShapeRadiusForFeel(BurstFeel feel, float scale)
    {
        switch (feel)
        {
            case BurstFeel.MatchFlash:
                return 0.04f * scale;
            case BurstFeel.IceMelt:
                return 0.10f * scale;
            case BurstFeel.HammerImpact:
                return 0.10f * scale;
            case BurstFeel.ShutterOpen:
                return 0.09f * scale;
            default:
                return 0.07f * scale;
        }
    }

    private static AnimationCurve SoftSizeCurve()
    {
        // Fast response → soft expansion settle → smooth fade (no harsh pop).
        return new AnimationCurve(
            new Keyframe(0f, 0.85f),
            new Keyframe(0.18f, 1f),
            new Keyframe(0.55f, 0.72f),
            new Keyframe(1f, 0.05f));
    }

    private static Gradient SoftFadeGradient(Color baseColor)
    {
        var gradient = new Gradient();
        Color mid = baseColor;
        mid.a = baseColor.a * 0.85f;
        Color end = baseColor;
        end.a = 0f;
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(baseColor, 0f),
                new GradientColorKey(mid, 0.55f),
                new GradientColorKey(baseColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.75f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    private static void SpawnRing(
        Vector3 worldPosition,
        Color color,
        float lifetime,
        string objectName = null,
        float scaleMul = 1f)
    {
        Transform root = ResolveRoot();
        if (root == null || !Application.isPlaying)
        {
            return;
        }

        float s = PresentationScale();
        float ringScale = 0.42f * s * Mathf.Clamp(scaleMul, 0.55f, 1.85f);
        // Thin torus-like contact pulse via scaled cylinder (existing approach, softer proportions).
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = string.IsNullOrEmpty(objectName) ? "VfxRing3D" : objectName;
        ring.transform.SetParent(root, false);
        ring.transform.position = worldPosition + Vector3.up * (0.022f * s);
        ring.transform.localScale = new Vector3(ringScale, 0.010f * s, ringScale);
        Collider collider = ring.GetComponent<Collider>();
        if (collider != null)
        {
            Object.Destroy(collider);
        }

        MeshRenderer meshRenderer = ring.GetComponent<MeshRenderer>();
        Material mat = new Material(GetRingMaterial()) { color = color };
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }

        if (mat.HasProperty("_Metallic"))
        {
            mat.SetFloat("_Metallic", 0f);
        }

        if (mat.HasProperty("_Smoothness"))
        {
            mat.SetFloat("_Smoothness", 0.35f);
        }

        meshRenderer.sharedMaterial = mat;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var animator = ring.AddComponent<BoardVfxRingAnimator>();
        animator.Play(lifetime, 1.32f);
    }

    /// <summary>Soft cream / warm-neutral VFX tint (Phase 52N cohesion).</summary>
    private static Color SoftCream(float alpha)
    {
        return new Color(0.98f, 0.95f, 0.88f, Mathf.Clamp01(alpha));
    }

    /// <summary>Accent pulled toward cream so bursts stay molded-plastic, not neon.</summary>
    private static Color SoftAccent(Color accent, float creamBlend, float alpha)
    {
        Color cream = new Color(0.98f, 0.95f, 0.88f, 1f);
        Color c = Color.Lerp(accent, cream, Mathf.Clamp01(creamBlend));
        c.a = Mathf.Clamp01(alpha);
        return c;
    }

    private static Mesh GetSharedParticleMesh()
    {
        if (sharedParticleMesh != null)
        {
            return sharedParticleMesh;
        }

        // Tiny molded shard: low-poly sphere reads as a soft plastic bead under board lighting.
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sharedParticleMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying)
        {
            Object.Destroy(temp);
        }
        else
        {
            Object.DestroyImmediate(temp);
        }

        return sharedParticleMesh;
    }

    private static Material GetBurstMaterial()
    {
        if (sharedBurstMaterial != null)
        {
            return sharedBurstMaterial;
        }

        // Soft unlit particle material — no neon additive bloom.
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Particles/Standard Unlit")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Sprites/Default");
        sharedBurstMaterial = new Material(shader)
        {
            name = "BoardVfxBurst3D_Mesh",
            color = Color.white
        };
        if (sharedBurstMaterial.HasProperty("_BaseColor"))
        {
            sharedBurstMaterial.SetColor("_BaseColor", Color.white);
        }

        if (sharedBurstMaterial.HasProperty("_Surface"))
        {
            sharedBurstMaterial.SetFloat("_Surface", 1f);
            sharedBurstMaterial.SetOverrideTag("RenderType", "Transparent");
            sharedBurstMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            sharedBurstMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            sharedBurstMaterial.SetInt("_ZWrite", 0);
            sharedBurstMaterial.renderQueue = 3000;
            sharedBurstMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        return sharedBurstMaterial;
    }

    private static Material GetRingMaterial()
    {
        if (sharedRingMaterial != null)
        {
            return sharedRingMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        sharedRingMaterial = new Material(shader)
        {
            name = "BoardVfxRing3D",
            color = new Color(1f, 1f, 1f, 0.30f)
        };
        if (sharedRingMaterial.HasProperty("_Metallic"))
        {
            sharedRingMaterial.SetFloat("_Metallic", 0f);
        }

        if (sharedRingMaterial.HasProperty("_Smoothness"))
        {
            sharedRingMaterial.SetFloat("_Smoothness", 0.32f);
        }

        ApplyTransparent(sharedRingMaterial);
        return sharedRingMaterial;
    }

    private static void ApplyTransparent(Material material)
    {
        if (material == null || !material.HasProperty("_Surface"))
        {
            return;
        }

        material.SetFloat("_Surface", 1f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.renderQueue = 3000;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }
}

/// <summary>Self-destroying ring scale/fade for nest match polish.</summary>
public sealed class BoardVfxRingAnimator : MonoBehaviour
{
    private Sequence sequence;

    public void Play(float life, float scaleMul)
    {
        float duration = Mathf.Max(0.05f, life);
        float targetScale = Mathf.Max(1.05f, scaleMul);
        Vector3 startScale = transform.localScale;
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        Material material = meshRenderer != null ? meshRenderer.material : null;
        Color startColor = material != null ? material.color : Color.white;

        if (sequence != null && sequence.IsActive())
        {
            sequence.Kill(false);
        }

        sequence = DOTween.Sequence().SetId(TweenAnimationUtility.VfxId).SetLink(gameObject);
        sequence.Append(TweenAnimationUtility.Progress(duration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutQuad(t);
            float mul = Mathf.Lerp(1f, targetScale, eased);
            transform.localScale = new Vector3(startScale.x * mul, startScale.y, startScale.z * mul);
            if (material != null)
            {
                Color c = startColor;
                c.a = startColor.a * (1f - t);
                material.color = c;
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", c);
                }
            }
        }));
        sequence.OnComplete(() =>
        {
            if (this != null)
            {
                Destroy(gameObject);
            }
        });
    }

    private void OnDestroy()
    {
        if (sequence != null && sequence.IsActive())
        {
            sequence.Kill(false);
        }

        sequence = null;
    }
}

/// <summary>Presentation-only shard flight. Destroys itself. Not a Block.</summary>
public sealed class HammerFragmentAnimator : MonoBehaviour
{
    private Sequence sequence;

    public void Play(Vector3 worldDelta, float lifetime, Color accent)
    {
        // lifetime must stay 0.50f from BoardVfx3D callers (Hammer presentation contract).
        float duration = Mathf.Max(0.20f, lifetime);
        Vector3 start = transform.position;
        Vector3 startScale = transform.localScale;
        Quaternion startRot = transform.rotation;
        Quaternion endRot = startRot * Quaternion.Euler(95f, 160f, 70f);
        Vector3 horizontal = new Vector3(worldDelta.x, 0f, worldDelta.z);
        float up = worldDelta.y;
        const float gravity = 3.6f;
        _ = accent;

        if (sequence != null && sequence.IsActive())
        {
            sequence.Kill(false);
        }

        sequence = DOTween.Sequence().SetId(TweenAnimationUtility.VfxId).SetLink(gameObject);
        sequence.Append(TweenAnimationUtility.Progress(duration, t =>
        {
            float time = t * duration;
            Vector3 p = start + (horizontal * t);
            p.y = start.y + (up * t) - (0.5f * gravity * time * time);
            float fade = t < 0.55f ? 0f : TweenAnimationUtility.EvaluateEaseInQuad((t - 0.55f) / 0.45f);
            transform.position = p;
            transform.rotation = Quaternion.Slerp(startRot, endRot, TweenAnimationUtility.EvaluateEaseOutQuad(t));
            transform.localScale = Vector3.LerpUnclamped(startScale, startScale * 0.12f, fade);
        }));
        sequence.OnComplete(() =>
        {
            if (this != null)
            {
                Destroy(gameObject);
            }
        });
    }

    private void OnDestroy()
    {
        if (sequence != null && sequence.IsActive())
        {
            sequence.Kill(false);
        }

        sequence = null;
    }
}
