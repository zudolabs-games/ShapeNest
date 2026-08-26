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

    private static void SpawnBurst(Vector3 worldPosition, Color color, int count, float speed, float lifetime)
    {
        Transform root = ResolveRoot();
        if (root == null || !Application.isPlaying)
        {
            return;
        }

        float s = PresentationScale();
        var go = new GameObject("VfxBurst3D");
        go.transform.SetParent(root, false);
        go.transform.position = worldPosition;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = 0.06f * s;
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

    private static void SpawnRing(Vector3 worldPosition, Color color, float lifetime)
    {
        Transform root = ResolveRoot();
        if (root == null || !Application.isPlaying)
        {
            return;
        }

        float s = PresentationScale();
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "VfxRing3D";
        ring.transform.SetParent(root, false);
        ring.transform.position = worldPosition + Vector3.up * (0.02f * s);
        ring.transform.localScale = new Vector3(0.35f * s, 0.01f * s, 0.35f * s);
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
