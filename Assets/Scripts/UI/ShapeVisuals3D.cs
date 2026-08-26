using UnityEngine;

/// <summary>
/// Presentation-only materials for World3D shapes.
/// Phase 13: dark-board / bright glossy toy palette.
/// Shared runtime materials (one per ShapeType).
/// </summary>
public static class ShapeVisuals3D
{
    private static Material[] blockMaterials;
    private static Material[] nestMaterials;
    private static Material chainConnectorMaterial;
    private static bool initialized;

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
        new Color(0.95f, 0.35f, 0.95f, 1f)  // Star — magenta
    };

    public static Material BlockMaterial(ShapeType shape, ShapeNestTheme theme = null)
    {
        Ensure(theme);
        return blockMaterials[(int)shape];
    }

    public static Material NestMaterial(ShapeType shape, ShapeNestTheme theme = null)
    {
        Ensure(theme);
        return nestMaterials[(int)shape];
    }

    public static Color AccentColor(ShapeType shape, ShapeNestTheme theme = null)
    {
        Ensure(theme);
        return blockMaterials[(int)shape].color;
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
        chainConnectorMaterial = null;
    }

    private static void Ensure(ShapeNestTheme theme)
    {
        if (initialized && blockMaterials != null && nestMaterials != null && chainConnectorMaterial != null)
        {
            return;
        }

        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        int count = System.Enum.GetValues(typeof(ShapeType)).Length;
        blockMaterials = new Material[count];
        nestMaterials = new Material[count];

        for (int i = 0; i < count; i++)
        {
            var shape = (ShapeType)i;
            Color blockColor = ResolveBlockColor(shape, theme);
            Color nestColor = ResolveNestColor(shape, blockColor);

            // Clean puzzle plastic — saturated, moderate gloss, no cartoon glow.
            blockMaterials[i] = CreateLit(lit, "Block3D_" + shape, blockColor, metallic: 0.04f, smoothness: 0.58f, emission: Color.black);
            nestMaterials[i] = CreateLit(lit, "Nest3D_" + shape, nestColor, metallic: 0.04f, smoothness: 0.38f, emission: Color.black);
        }

        Color linkColor = theme != null ? theme.accent : new Color(0.55f, 0.48f, 0.78f, 1f);
        linkColor.a = 1f;
        chainConnectorMaterial = CreateLit(lit, "ChainLink3D", linkColor, metallic: 0.04f, smoothness: 0.5f, emission: Color.black);

        initialized = true;
    }

    private static Material CreateLit(Shader shader, string name, Color color, float metallic, float smoothness, Color emission)
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

        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
        }

        return material;
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
        // Same family as block, darker socket look.
        Color nest = Darken(Saturate(blockColor, 1.15f, 0.82f), 0.18f);
        nest.a = 1f;
        return nest;
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
