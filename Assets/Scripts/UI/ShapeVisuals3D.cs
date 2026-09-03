using UnityEngine;

/// <summary>
/// Presentation-only materials for World3D shapes.
/// Shared runtime URP Lit materials (one per ShapeType + one chain connector).
/// Phase 52B–52I: molded-plastic response — non-metallic, moderate smoothness, no emission.
/// </summary>
public static class ShapeVisuals3D
{
    /// <summary>Phase 52I: solid movable pieces — soft plastic highlight for side-face depth.</summary>
    public const float BlockMetallic = 0f;
    public const float BlockSmoothness = 0.67f;

    /// <summary>Phase 52I: recessed sockets — rim catches soft plastic highlight.</summary>
    public const float NestMetallic = 0f;
    public const float NestSmoothness = 0.62f;

    /// <summary>Phase 52I: darker matte cavity floor/walls (same hue family).</summary>
    public const float NestCavityMetallic = 0f;
    public const float NestCavitySmoothness = 0.22f;

    /// <summary>Phase 52I: chain bars share the same plastic family as blocks.</summary>
    public const float ConnectorMetallic = 0f;
    public const float ConnectorSmoothness = 0.64f;

    private static Material[] blockMaterials;
    private static Material[] nestMaterials;
    private static Material[] nestCavityMaterials;
    private static Material[][] nestMaterialSets;
    private static Material chainConnectorMaterial;
    private static bool initialized;
    private static readonly System.Collections.Generic.Dictionary<int, Material> overrideBlockMaterials =
        new System.Collections.Generic.Dictionary<int, Material>();
    private static readonly System.Collections.Generic.Dictionary<int, Material> overrideNestMaterials =
        new System.Collections.Generic.Dictionary<int, Material>();
    private static readonly System.Collections.Generic.Dictionary<int, Material[]> overrideNestMaterialSets =
        new System.Collections.Generic.Dictionary<int, Material[]>();

    /// <summary>
    /// Saturated toy palette (ShapeType order). Used as primary Phase 13 look.
    /// </summary>
    private static readonly Color[] FallbackBlockColors =
    {
        new Color(1.00f, 0.82f, 0.18f, 1f), // Square — gold/yellow
        new Color(0.20f, 0.78f, 1.00f, 1f), // Circle — cyan
        new Color(1.00f, 0.32f, 0.38f, 1f), // Triangle — coral/red
        new Color(0.35f, 0.95f, 0.42f, 1f), // Diamond — lime green
        new Color(0.28f, 0.55f, 1.00f, 1f), // Hexagon — vivid blue
        new Color(0.95f, 0.35f, 0.95f, 1f), // Star — magenta
        new Color(1.00f, 0.55f, 0.12f, 1f)  // Pentagon — orange
    };

    public static Material BlockMaterial(ShapeType shape, ShapeNestTheme theme = null)
    {
        return BlockMaterial(shape, ShapeColor.Default, theme);
    }

    public static Material BlockMaterial(ShapeType shape, ShapeColor color, ShapeNestTheme theme = null)
    {
        Ensure(theme);
        if (color == ShapeColor.Default)
        {
            return blockMaterials[(int)shape];
        }

        return ResolveOverrideMaterial(shape, color, isNest: false, theme);
    }

    public static Material NestMaterial(ShapeType shape, ShapeNestTheme theme = null)
    {
        return NestMaterial(shape, ShapeColor.Default, theme);
    }

    public static Material NestMaterial(ShapeType shape, ShapeColor color, ShapeNestTheme theme = null)
    {
        Ensure(theme);
        if (color == ShapeColor.Default)
        {
            return nestMaterials[(int)shape];
        }

        return ResolveOverrideMaterial(shape, color, isNest: true, theme);
    }

    /// <summary>Phase 52C: darker cavity material for nest submesh 1 (same hue as rim).</summary>
    public static Material NestCavityMaterial(ShapeType shape, ShapeNestTheme theme = null)
    {
        Ensure(theme);
        return nestCavityMaterials[(int)shape];
    }

    /// <summary>Rim + cavity shared materials for procedural nest meshes (submesh 0/1).</summary>
    public static Material[] NestMaterialSet(ShapeType shape, ShapeNestTheme theme = null)
    {
        return NestMaterialSet(shape, ShapeColor.Default, theme);
    }

    public static Material[] NestMaterialSet(ShapeType shape, ShapeColor color, ShapeNestTheme theme = null)
    {
        Ensure(theme);
        if (color == ShapeColor.Default)
        {
            return nestMaterialSets[(int)shape];
        }

        return ResolveOverrideNestMaterialSet(shape, color, theme);
    }

    public static Color AccentColor(ShapeType shape, ShapeNestTheme theme = null)
    {
        return AccentColor(shape, ShapeColor.Default, theme);
    }

    public static Color AccentColor(ShapeType shape, ShapeColor color, ShapeNestTheme theme = null)
    {
        Ensure(theme);
        if (color == ShapeColor.Default)
        {
            return blockMaterials[(int)shape].color;
        }

        return ResolveOverrideMaterial(shape, color, isNest: false, theme).color;
    }

    /// <summary>
    /// World3D equivalent of 2D ChainLink Image color (theme.accent).
    /// </summary>
    public static Material ChainConnectorMaterial(ShapeNestTheme theme = null)
    {
        Ensure(theme);
        return chainConnectorMaterial;
    }

    public static void Invalidate()
    {
        initialized = false;
        blockMaterials = null;
        nestMaterials = null;
        nestCavityMaterials = null;
        nestMaterialSets = null;
        chainConnectorMaterial = null;
        overrideBlockMaterials.Clear();
        overrideNestMaterials.Clear();
        overrideNestMaterialSets.Clear();
    }

    private static void Ensure(ShapeNestTheme theme)
    {
        if (initialized
            && blockMaterials != null
            && nestMaterials != null
            && nestCavityMaterials != null
            && nestMaterialSets != null
            && chainConnectorMaterial != null)
        {
            return;
        }

        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        int count = System.Enum.GetValues(typeof(ShapeType)).Length;
        blockMaterials = new Material[count];
        nestMaterials = new Material[count];
        nestCavityMaterials = new Material[count];
        nestMaterialSets = new Material[count][];

        for (int i = 0; i < count; i++)
        {
            var shape = (ShapeType)i;
            Color blockColor = ResolveBlockColor(shape, theme);
            Color nestRim = ResolveNestColor(shape, blockColor);
            Color nestCavity = ResolveNestCavityColor(nestRim, blockColor);

            // Molded puzzle plastic — bevels catch key+fill light without mirror sheen.
            blockMaterials[i] = CreateLit(lit, "Block3D_" + shape, blockColor, BlockMetallic, BlockSmoothness);
            nestMaterials[i] = CreateLit(lit, "Nest3D_" + shape, nestRim, NestMetallic, NestSmoothness);
            nestCavityMaterials[i] = CreateLit(
                lit,
                "NestCavity3D_" + shape,
                nestCavity,
                NestCavityMetallic,
                NestCavitySmoothness);
            nestMaterialSets[i] = new[] { nestMaterials[i], nestCavityMaterials[i] };
        }

        Color linkColor = theme != null ? theme.accent : new Color(0.55f, 0.48f, 0.78f, 1f);
        linkColor.a = 1f;
        chainConnectorMaterial = CreateLit(lit, "ChainLink3D", linkColor, ConnectorMetallic, ConnectorSmoothness);

        initialized = true;
    }

    private static Material CreateLit(Shader shader, string name, Color color, float metallic, float smoothness)
    {
        var material = new Material(shader)
        {
            name = name,
            color = color
        };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }

        // Normal pieces must not rely on emission; keep keyword off.
        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
        }

        if (material.HasProperty("_SpecularHighlights"))
        {
            material.SetFloat("_SpecularHighlights", 1f);
        }

        if (material.HasProperty("_EnvironmentReflections"))
        {
            // Soft local specular from lights; avoid busy env reflections on mobile.
            material.SetFloat("_EnvironmentReflections", 0f);
        }

        return material;
    }

    private static Color ResolvePaletteColor(ShapeColor color, ShapeType shapeFallback)
    {
        switch (color)
        {
            case ShapeColor.Yellow:
                return new Color(1.00f, 0.82f, 0.18f, 1f);
            case ShapeColor.Cyan:
                return new Color(0.20f, 0.78f, 1.00f, 1f);
            case ShapeColor.Pink:
                return new Color(0.98f, 0.42f, 0.72f, 1f);
            case ShapeColor.Purple:
                return new Color(0.72f, 0.28f, 0.92f, 1f);
            case ShapeColor.Green:
                return new Color(0.35f, 0.95f, 0.42f, 1f);
            case ShapeColor.Red:
                return new Color(0.95f, 0.22f, 0.28f, 1f);
            case ShapeColor.Orange:
                return new Color(1.00f, 0.55f, 0.12f, 1f);
            case ShapeColor.White:
                return new Color(0.94f, 0.96f, 0.98f, 1f);
            case ShapeColor.Blue:
                return new Color(0.28f, 0.55f, 1.00f, 1f);
            default:
                int index = Mathf.Clamp((int)shapeFallback, 0, FallbackBlockColors.Length - 1);
                return FallbackBlockColors[index];
        }
    }

    private static int OverrideMaterialKey(ShapeType shape, ShapeColor color, bool isNest)
    {
        return ((int)color << 8) | ((int)shape << 4) | (isNest ? 1 : 0);
    }

    private static Material ResolveOverrideMaterial(ShapeType shape, ShapeColor color, bool isNest, ShapeNestTheme theme)
    {
        Ensure(theme);
        int key = OverrideMaterialKey(shape, color, isNest);
        var cache = isNest ? overrideNestMaterials : overrideBlockMaterials;
        if (cache.TryGetValue(key, out Material existing) && existing != null)
        {
            return existing;
        }

        Color blockColor = ResolvePaletteColor(color, shape);
        Color nestRim = ResolveNestColor(shape, blockColor);
        Color resolved = isNest ? nestRim : blockColor;
        float metallic = isNest ? NestMetallic : BlockMetallic;
        float smoothness = isNest ? NestSmoothness : BlockSmoothness;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        string prefix = isNest ? "Nest3D_" : "Block3D_";
        Material created = CreateLit(lit, prefix + shape + "_" + color, resolved, metallic, smoothness);
        cache[key] = created;
        return created;
    }

    private static Material[] ResolveOverrideNestMaterialSet(ShapeType shape, ShapeColor color, ShapeNestTheme theme)
    {
        Ensure(theme);
        int key = OverrideMaterialKey(shape, color, isNest: true);
        if (overrideNestMaterialSets.TryGetValue(key, out Material[] existing) && existing != null)
        {
            return existing;
        }

        Material rim = ResolveOverrideMaterial(shape, color, isNest: true, theme);
        Color blockColor = ResolvePaletteColor(color, shape);
        Color nestRim = ResolveNestColor(shape, blockColor);
        Color nestCavity = ResolveNestCavityColor(nestRim, blockColor);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material cavity = CreateLit(
            lit,
            "NestCavity3D_" + shape + "_" + color,
            nestCavity,
            NestCavityMetallic,
            NestCavitySmoothness);
        Material[] set = { rim, cavity };
        overrideNestMaterialSets[key] = set;
        return set;
    }

    private static Color ResolveBlockColor(ShapeType shape, ShapeNestTheme theme)
    {
        int index = Mathf.Clamp((int)shape, 0, FallbackBlockColors.Length - 1);
        Color fallback = FallbackBlockColors[index];

        Sprite sprite = theme != null
            ? ShapeVisuals.SpriteFor(
                shape,
                theme.blockSquare,
                theme.blockCircle,
                theme.blockTriangle,
                theme.blockDiamond,
                theme.blockHexagon,
                theme.blockStar)
            : null;

        if (TryAverageOpaqueColor(sprite, out Color sampled))
        {
            // Keep theme hue identity but push hard toward saturated toy look.
            return Color.Lerp(Saturate(sampled, 1.45f, 0.95f), fallback, 0.35f);
        }

        return fallback;
    }

    private static Color ResolveNestColor(ShapeType shape, Color blockColor)
    {
        // Same hue family as block — rim stays the established nest identity color.
        _ = shape;
        Color nest = Darken(Saturate(blockColor, 1.12f, 0.78f), 0.22f);
        nest.a = 1f;
        return nest;
    }

    private static Color ResolveNestCavityColor(Color nestRim, Color blockColor)
    {
        // Darker recess in the same family — not a new hue.
        _ = blockColor;
        Color cavity = Darken(nestRim, 0.36f);
        cavity.a = 1f;
        return cavity;
    }

    private static Color Saturate(Color color, float satMul, float valueTarget)
    {
        Color.RGBToHSV(color, out float h, out float s, out float v);
        s = Mathf.Clamp01(s * satMul);
        v = Mathf.Clamp01(Mathf.Max(v, valueTarget) * 0.98f);
        Color result = Color.HSVToRGB(h, s, v);
        result.a = 1f;
        return result;
    }

    private static Color Darken(Color color, float amount)
    {
        amount = Mathf.Clamp01(amount);
        return new Color(color.r * (1f - amount), color.g * (1f - amount), color.b * (1f - amount), 1f);
    }

    private static bool TryAverageOpaqueColor(Sprite sprite, out Color color)
    {
        color = Color.white;
        if (sprite == null || sprite.texture == null)
        {
            return false;
        }

        try
        {
            Texture2D tex = sprite.texture;
            Rect rect = sprite.textureRect;
            Color[] pixels = tex.GetPixels(
                Mathf.FloorToInt(rect.x),
                Mathf.FloorToInt(rect.y),
                Mathf.FloorToInt(rect.width),
                Mathf.FloorToInt(rect.height));

            float r = 0f;
            float g = 0f;
            float b = 0f;
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 8)
            {
                if (pixels[i].a < 0.2f)
                {
                    continue;
                }

                r += pixels[i].r;
                g += pixels[i].g;
                b += pixels[i].b;
                count++;
            }

            if (count == 0)
            {
                return false;
            }

            color = new Color(r / count, g / count, b / count, 1f);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
