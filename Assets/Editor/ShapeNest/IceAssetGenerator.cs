using UnityEngine;
using UnityEditor;
using System.IO;

namespace ShapeNest.Editor
{
    public static class IceAssetGenerator
    {
        private const string ResourcePath = "Assets/Resources/Ice/";

        [MenuItem("Tools/ShapeNest/Regenerate Ice Textures")]
        public static void GenerateAssets()
        {
            if (!Directory.Exists(ResourcePath))
            {
                Directory.CreateDirectory(ResourcePath);
            }

            string facetPath = ResourcePath + "IceCrystalFacet.png";
            string normalPath = ResourcePath + "IceCrystalNormal.png";
            string crackPath = ResourcePath + "IceCrackMask.png";

            GenerateCrystalFacetMap(facetPath);
            GenerateCrystalNormalMap(normalPath);
            GenerateCrackMask(crackPath);

            AssetDatabase.Refresh();

            TextureImporter facetImporter = AssetImporter.GetAtPath(facetPath) as TextureImporter;
            if (facetImporter != null)
            {
                facetImporter.textureType = TextureImporterType.Default;
                facetImporter.wrapMode = TextureWrapMode.Repeat;
                facetImporter.filterMode = FilterMode.Bilinear;
                facetImporter.sRGBTexture = false;
                facetImporter.SaveAndReimport();
            }

            TextureImporter normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (normalImporter != null)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.wrapMode = TextureWrapMode.Repeat;
                normalImporter.filterMode = FilterMode.Bilinear;
                normalImporter.SaveAndReimport();
            }

            TextureImporter crackImporter = AssetImporter.GetAtPath(crackPath) as TextureImporter;
            if (crackImporter != null)
            {
                crackImporter.textureType = TextureImporterType.Default;
                crackImporter.wrapMode = TextureWrapMode.Clamp;
                crackImporter.filterMode = FilterMode.Bilinear;
                crackImporter.sRGBTexture = false;
                crackImporter.SaveAndReimport();
            }
        }

        private static void GenerateCrystalFacetMap(string path)
        {
            int size = 512;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);

            // 16 chunky Voronoi cell centers for bold crystalline facets visible from gameplay distance
            int numPoints = 16;
            Vector2[] points = new Vector2[numPoints];
            Random.InitState(1337);
            for (int i = 0; i < numPoints; i++)
            {
                points[i] = new Vector2(Random.value * size, Random.value * size);
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d1 = float.MaxValue;
                    float d2 = float.MaxValue;
                    int closestIdx = 0;

                    for (int i = 0; i < numPoints; i++)
                    {
                        float dx = Mathf.Abs(x - points[i].x);
                        float dy = Mathf.Abs(y - points[i].y);
                        if (dx > size / 2f) dx = size - dx;
                        if (dy > size / 2f) dy = size - dy;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);

                        if (dist < d1)
                        {
                            d2 = d1;
                            d1 = dist;
                            closestIdx = i;
                        }
                        else if (dist < d2)
                        {
                            d2 = dist;
                        }
                    }

                    // Facet value: distinct crystalline hue per facet
                    float cellHue = (closestIdx * 0.173f) % 1.0f;
                    float facet = Mathf.Clamp01(cellHue * 0.5f + (d1 / (size / 4f)) * 0.5f);

                    // Bold vein boundary between facets
                    float edgeDist = d2 - d1;
                    float vein = 1.0f - Mathf.Clamp01(edgeDist / 12.0f);
                    vein = Mathf.Pow(vein, 2.0f);

                    // High frequency micro noise
                    float u = x / (float)size;
                    float v = y / (float)size;
                    float micro = Mathf.PerlinNoise(u * 20f, v * 20f) * 0.25f;

                    tex.SetPixel(x, y, new Color(facet, vein, micro, 1f));
                }
            }

            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static void GenerateCrystalNormalMap(string path)
        {
            int size = 512;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);

            int numPoints = 16;
            Vector2[] points = new Vector2[numPoints];
            Random.InitState(1337);
            for (int i = 0; i < numPoints; i++)
            {
                points[i] = new Vector2(Random.value * size, Random.value * size);
            }

            float[,] heights = new float[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d1 = float.MaxValue;
                    for (int i = 0; i < numPoints; i++)
                    {
                        float dx = Mathf.Abs(x - points[i].x);
                        float dy = Mathf.Abs(y - points[i].y);
                        if (dx > size / 2f) dx = size - dx;
                        if (dy > size / 2f) dy = size - dy;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        if (dist < d1) d1 = dist;
                    }
                    heights[x, y] = d1 / (size / 4f);
                }
            }

            float normalStrength = 6.0f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float hL = heights[(x - 1 + size) % size, y];
                    float hR = heights[(x + 1) % size, y];
                    float hD = heights[x, (y - 1 + size) % size];
                    float hU = heights[x, (y + 1) % size];

                    Vector3 normal = new Vector3((hL - hR) * normalStrength, (hD - hU) * normalStrength, 1.0f).normalized;

                    // Tangent space normal map (R=X, G=Y, B=Z)
                    Color c = new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f,
                        1f);
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static void GenerateCrackMask(string path)
        {
            int size = 512;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;

                    // Multi-octave domain warped ridged noise for organic ice fractures
                    float qx = Mathf.PerlinNoise(u * 6f, v * 6f);
                    float qy = Mathf.PerlinNoise(u * 6f + 5.2f, v * 6f + 1.3f);

                    float r1 = Mathf.Abs(Mathf.PerlinNoise((u + qx * 0.25f) * 12f, (v + qy * 0.25f) * 12f) - 0.5f) * 2.0f;
                    float r2 = Mathf.Abs(Mathf.PerlinNoise((u - qx * 0.15f) * 24f + 30f, (v - qy * 0.15f) * 24f + 30f) - 0.5f) * 2.0f;

                    float crack1 = 1.0f - Mathf.Clamp01(r1 / 0.08f);
                    float crack2 = 1.0f - Mathf.Clamp01(r2 / 0.05f) * 0.7f;
                    float crackCombined = Mathf.Max(crack1, crack2);

                    // Distance from center / branching factor
                    float distFromCenter = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
                    float severity = Mathf.Clamp01(1.0f - distFromCenter * 1.2f + Mathf.PerlinNoise(u * 4f, v * 4f) * 0.5f);

                    // Red channel = crack progression threshold (0 = deep core, 1 = hairline tips)
                    float progress = crackCombined > 0.01f ? Mathf.Clamp01(crackCombined * severity) : 0f;

                    // Green channel = edge glow / internal refraction around crack
                    float glow = Mathf.Pow(crackCombined, 1.5f) * 0.8f;

                    tex.SetPixel(x, y, new Color(progress, glow, 0f, 1f));
                }
            }

            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
