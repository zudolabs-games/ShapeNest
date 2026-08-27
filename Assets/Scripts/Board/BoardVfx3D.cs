using DG.Tweening;
using UnityEngine;

/// <summary>
/// Ephemeral World3D presentation effects. Never delays gameplay; effects self-destroy.
/// </summary>
public static class BoardVfx3D
{
    private static Transform effectsRoot;
    private static Material sharedBurstMaterial;
    private static Material sharedRingMaterial;

    public static void SetEffectsRoot(Transform root)
    {
        effectsRoot = root;
    }

    public static void ClearAll()
    {
        if (effectsRoot == null)
        {
            return;
        }

        for (int i = effectsRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = effectsRoot.GetChild(i);
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

    public static void PlayLanding(Vector3 worldPosition)
    {
        float s = PresentationScale();
        SpawnBurst(worldPosition, new Color(1f, 1f, 1f, 0.7f), 7, 0.22f * s, 0.28f);
    }

    public static void PlayNestMatch(Vector3 worldPosition, Color accent)
    {
        float s = PresentationScale();
        Color c = accent;
        c = Color.Lerp(c, Color.white, 0.15f);
        c.a = 0.95f;
        SpawnBurst(worldPosition + Vector3.up * (0.05f * s), c, 12, 0.32f * s, 0.38f);
        SpawnRing(worldPosition, c, 0.42f);
    }

    public static void PlayIceMelt(Vector3 worldPosition)
    {
        float s = PresentationScale();
        SpawnBurst(worldPosition, new Color(0.55f, 0.95f, 1f, 0.9f), 10, 0.28f * s, 0.35f);
    }

    public static void PlayShutterOpen(Vector3 worldPosition)
    {
        float s = PresentationScale();
        SpawnBurst(worldPosition, new Color(1f, 0.85f, 0.35f, 0.85f), 9, 0.26f * s, 0.34f);
    }

    /// <summary>
    /// Presentation-only Hammer smash at a world position. Does not delay gameplay.
    /// <paramref name="footprintMul"/> scales the ring for multi-cell chains (1 = single cell).
    /// </summary>
    public static void PlayHammerImpact(Vector3 worldPosition, Color accent, float footprintMul = 1f)
    {
        float s = PresentationScale();
        float footprint = Mathf.Clamp(footprintMul, 1f, 1.85f);
        Color c = Color.Lerp(accent, Color.white, 0.22f);
        c.a = 1f;
        int count = Mathf.Clamp(18 + Mathf.RoundToInt((footprint - 1f) * 6f), 18, 24);
        SpawnBurst(
            worldPosition + Vector3.up * (0.10f * s),
            c,
            count,
            0.55f * s * Mathf.Lerp(1f, 1.14f, footprint - 1f),
            0.32f,
            "HammerImpactVFX",
            0.16f * s);
        Color flash = Color.white;
        flash.a = 0.92f;
        SpawnBurst(
            worldPosition + Vector3.up * (0.12f * s),
            flash,
            8,
            0.28f * s,
            0.16f,
            "HammerImpactFlashVFX",
            0.20f * s);
        SpawnRing(worldPosition, c, 0.28f, "HammerImpactRingVFX", footprint);
    }

    /// <summary>
    /// Small presentation-only fragment burst at one chain cell. Same Hammer operation.
    /// </summary>
    public static void PlayHammerCellBurst(Vector3 worldPosition, Color accent)
    {
        float s = PresentationScale();
        Color c = Color.Lerp(accent, Color.white, 0.1f);
        c.a = 0.9f;
        SpawnBurst(
            worldPosition + Vector3.up * (0.06f * s),
            c,
            7,
            0.28f * s,
            0.3f,
            "HammerCellVFX",
            0.1f * s);
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
        float span = Mathf.Max(0.08f, Mathf.Min(sourceBounds.size.x, sourceBounds.size.z));
        float size = span * (0.22f + ((seed % 3) * 0.04f));
        size = Mathf.Clamp(size, 0.10f * scale, 0.28f * scale);
        go.transform.localScale = Vector3.one * size;
        go.transform.rotation = Quaternion.Euler(seed * 37f, seed * 53f, seed * 19f);

        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material != null ? material : GetBurstMaterial();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        var animator = go.AddComponent<HammerFragmentAnimator>();
        Vector3 outward = origin - sourceBounds.center;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = new Vector3(Mathf.Sin(seed * 1.7f + 0.4f), 0f, Mathf.Cos(seed * 1.3f + 1.1f));
        }

        outward.Normalize();
        Vector3 velocity = (outward * (0.52f * scale)) + (Vector3.up * (0.58f + ((seed % 4) * 0.08f)) * scale);
        animator.Play(velocity, 0.50f, accent);
    }

    /// <summary>
    /// Subtle nest fade-burst when Hammer removes a corresponding target. Not a match VFX.
    /// </summary>
    public static void PlayHammerNest(Vector3 worldPosition, Color accent)
    {
        float s = PresentationScale();
        Color c = Color.Lerp(accent, Color.white, 0.28f);
        c.a = 0.7f;
        SpawnBurst(
            worldPosition + Vector3.up * (0.05f * s),
            c,
            10,
            0.32f * s,
            0.28f,
            "HammerNestVFX",
            0.12f * s);
        SpawnRing(worldPosition, c, 0.24f, "HammerNestRingVFX", 0.9f);
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
        string objectName = null,
        float startSize = -1f)
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
        main.startSpeed = speed;
        main.startSize = startSize > 0f ? startSize : 0.06f * s;
        main.startColor = color;
        main.maxParticles = count;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.35f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f * s;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = GetBurstMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
        Object.Destroy(go, lifetime + 0.15f);
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
        float ringScale = 0.55f * s * Mathf.Clamp(scaleMul, 0.55f, 1.85f);
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = string.IsNullOrEmpty(objectName) ? "VfxRing3D" : objectName;
        ring.transform.SetParent(root, false);
        ring.transform.position = worldPosition + Vector3.up * (0.03f * s);
        ring.transform.localScale = new Vector3(ringScale, 0.018f * s, ringScale);
        Collider collider = ring.GetComponent<Collider>();
        if (collider != null)
        {
            Object.Destroy(collider);
        }

        MeshRenderer meshRenderer = ring.GetComponent<MeshRenderer>();
        Material mat = new Material(GetRingMaterial()) { color = color };
        meshRenderer.sharedMaterial = mat;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var animator = ring.AddComponent<BoardVfxRingAnimator>();
        animator.Play(lifetime, 1.65f);
    }

    private static Material GetBurstMaterial()
    {
        if (sharedBurstMaterial != null)
        {
            return sharedBurstMaterial;
        }

        Shader shader = Shader.Find("Particles/Standard Unlit")
            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Sprites/Default");
        sharedBurstMaterial = new Material(shader)
        {
            name = "BoardVfxBurst3D",
            color = Color.white
        };
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
            color = new Color(1f, 1f, 1f, 0.35f)
        };
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
        float duration = Mathf.Max(0.20f, lifetime);
        Vector3 start = transform.position;
        Vector3 startScale = transform.localScale;
        Quaternion startRot = transform.rotation;
        Quaternion endRot = startRot * Quaternion.Euler(95f, 160f, 70f);
        Vector3 horizontal = new Vector3(worldDelta.x, 0f, worldDelta.z);
        float up = worldDelta.y;
        const float gravity = 3.6f;

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
