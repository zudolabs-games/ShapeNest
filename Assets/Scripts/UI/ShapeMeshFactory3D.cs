using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural extruded meshes for World3D piece/nest presentation.
/// Phase 13B: clean geometric silhouettes + subtle extrusion/bevel (not cartoon toys).
/// </summary>
public static class ShapeMeshFactory3D
{
    private const float BevelFraction = 0.07f;
    private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

    public static void ClearCache()
    {
        Cache.Clear();
    }

    public static Mesh GetSolidMesh(ShapeType shape)
    {
        string key = "solid_v4_" + shape;
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        Vector2[] outline = GetOutline(shape, 0.5f);
        CenterOutlineAabb(outline, null);
        Mesh mesh = BuildBeveledExtruded(outline, null, 1f, BevelFraction, addNestFloor: false);
        mesh.name = "ShapeSolid_" + shape;
        Cache[key] = mesh;
        return mesh;
    }

    public static Mesh GetNestMesh(ShapeType shape)
    {
        string key = "nest_v4_" + shape;
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        Vector2[] outer = GetOutline(shape, 0.5f);
        Vector2[] inner = GetOutline(shape, 0.30f);
        CenterOutlineAabb(outer, inner);
        Mesh mesh = BuildBeveledExtruded(outer, inner, 1f, BevelFraction * 0.8f, addNestFloor: true);
        mesh.name = "ShapeNest_" + shape;
        Cache[key] = mesh;
        return mesh;
    }

    private static Vector2[] GetOutline(ShapeType shape, float radius)
    {
        switch (shape)
        {
            case ShapeType.Circle:
                return RegularPolygon(32, radius);
            case ShapeType.Triangle:
                return RegularPolygon(3, radius, -90f * Mathf.Deg2Rad);
            case ShapeType.Diamond:
                return RegularPolygon(4, radius, 0f);
            case ShapeType.Hexagon:
                return RegularPolygon(6, radius, 30f * Mathf.Deg2Rad);
            case ShapeType.Star:
                return StarPolygon(5, radius, radius * 0.45f, -90f * Mathf.Deg2Rad);
            case ShapeType.Square:
            default:
                return SquareOutline(radius);
        }
    }

    private static Vector2[] SquareOutline(float half)
    {
        return new[]
        {
            new Vector2(-half, -half),
            new Vector2(half, -half),
            new Vector2(half, half),
            new Vector2(-half, half)
        };
    }

    private static Vector2[] RegularPolygon(int sides, float radius, float rotation = 0f)
    {
        var points = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = rotation + (i * Mathf.PI * 2f / sides);
            points[i] = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
        }

        return points;
    }

    private static Vector2[] StarPolygon(int points, float outerRadius, float innerRadius, float rotation)
    {
        var verts = new Vector2[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            float a = rotation + (i * Mathf.PI / points);
            float r = (i % 2 == 0) ? outerRadius : innerRadius;
            verts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        return verts;
    }

    private static Vector2[] ScaleOutline(Vector2[] source, float scale)
    {
        var result = new Vector2[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            result[i] = source[i] * scale;
        }

        return result;
    }

    /// <summary>
    /// Translates outline points so the XZ AABB sits on the local origin.
    /// Inner nest rings use the outer ring's offset so walls stay similar.
    /// Square/Circle already have a zero AABB center and are unchanged.
    /// </summary>
    private static void CenterOutlineAabb(Vector2[] outer, Vector2[] inner)
    {
        if (outer == null || outer.Length == 0)
        {
            return;
        }

        Vector2 center = ComputeAabbCenter(outer);
        if (center.sqrMagnitude < 0.0000001f)
        {
            return;
        }

        Vector2 delta = -center;
        TranslateOutline(outer, delta);
        if (inner != null)
        {
            TranslateOutline(inner, delta);
        }
    }

    private static Vector2 ComputeAabbCenter(Vector2[] points)
    {
        float minX = points[0].x;
        float maxX = points[0].x;
        float minY = points[0].y;
        float maxY = points[0].y;
        for (int i = 1; i < points.Length; i++)
        {
            Vector2 p = points[i];
            if (p.x < minX)
            {
                minX = p.x;
            }

            if (p.x > maxX)
            {
                maxX = p.x;
            }

            if (p.y < minY)
            {
                minY = p.y;
            }

            if (p.y > maxY)
            {
                maxY = p.y;
            }
        }

        return new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
    }

    private static void TranslateOutline(Vector2[] points, Vector2 delta)
    {
        for (int i = 0; i < points.Length; i++)
        {
            points[i] += delta;
        }
    }

    /// <summary>
    /// Extrudes a polygon in XZ with a soft top bevel. Optional hollow nest + recessed floor.
    /// </summary>
    private static Mesh BuildBeveledExtruded(
        Vector2[] outer,
        Vector2[] inner,
        float height,
        float bevelFraction,
        bool addNestFloor)
    {
        float y0 = -height * 0.5f;
        float y1 = height * 0.5f;
        float bevel = Mathf.Clamp(bevelFraction, 0.04f, 0.28f) * height;
        float yBevel = y1 - bevel;
        float inset = 1f - Mathf.Clamp(bevelFraction, 0.04f, 0.28f) * 1.35f;
        Vector2[] outerTop = ScaleOutline(outer, inset);
        bool hollow = inner != null && inner.Length >= 3;
        Vector2[] innerTop = hollow ? ScaleOutline(inner, Mathf.Lerp(1f, inset, 0.35f)) : null;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        var uvs = new List<Vector2>();

        // Bottom
        AddCap(vertices, normals, triangles, uvs, outer, inner, y0, Vector3.down, hollow);

        // Top (inset)
        AddCap(vertices, normals, triangles, uvs, outerTop, innerTop, y1, Vector3.up, hollow);

        // Outer vertical + bevel
        AddWalls(vertices, normals, triangles, uvs, outer, y0, yBevel, outward: true);
        AddBevelBand(vertices, normals, triangles, uvs, outer, outerTop, yBevel, y1, outward: true);

        if (hollow)
        {
            AddWalls(vertices, normals, triangles, uvs, inner, y0, yBevel, outward: false);
            AddBevelBand(vertices, normals, triangles, uvs, inner, innerTop, yBevel, y1, outward: false);

            if (addNestFloor)
            {
                float floorY = Mathf.Lerp(y0, y1, 0.22f);
                AddCap(vertices, normals, triangles, uvs, inner, null, floorY, Vector3.up, hollow: false);
            }
        }

        var mesh = new Mesh
        {
            name = hollow ? "BeveledNest" : "BeveledSolid"
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static void AddCap(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int> triangles,
        List<Vector2> uvs,
        Vector2[] outer,
        Vector2[] inner,
        float y,
        Vector3 normal,
        bool hollow)
    {
        int start = vertices.Count;
        for (int i = 0; i < outer.Length; i++)
        {
            vertices.Add(new Vector3(outer[i].x, y, outer[i].y));
            normals.Add(normal);
            uvs.Add(outer[i] + Vector2.one * 0.5f);
        }

        if (!hollow)
        {
            for (int i = 1; i < outer.Length - 1; i++)
            {
                if (normal.y > 0f)
                {
                    triangles.Add(start);
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                }
                else
                {
                    triangles.Add(start);
                    triangles.Add(start + i + 1);
                    triangles.Add(start + i);
                }
            }

            return;
        }

        int innerStart = vertices.Count;
        for (int i = 0; i < inner.Length; i++)
        {
            vertices.Add(new Vector3(inner[i].x, y, inner[i].y));
            normals.Add(normal);
            uvs.Add(inner[i] + Vector2.one * 0.5f);
        }

        int count = Mathf.Min(outer.Length, inner.Length);
        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            int o0 = start + i;
            int o1 = start + i1;
            int n0 = innerStart + i;
            int n1 = innerStart + i1;
            if (normal.y > 0f)
            {
                triangles.Add(o0);
                triangles.Add(o1);
                triangles.Add(n1);
                triangles.Add(o0);
                triangles.Add(n1);
                triangles.Add(n0);
            }
            else
            {
                triangles.Add(o0);
                triangles.Add(n1);
                triangles.Add(o1);
                triangles.Add(o0);
                triangles.Add(n0);
                triangles.Add(n1);
            }
        }
    }

    private static void AddWalls(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int> triangles,
        List<Vector2> uvs,
        Vector2[] ring,
        float y0,
        float y1,
        bool outward)
    {
        int count = ring.Length;
        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            Vector2 a = ring[i];
            Vector2 b = ring[i1];
            Vector3 edge = new Vector3(b.x - a.x, 0f, b.y - a.y);
            Vector3 n = Vector3.Cross(Vector3.up, edge).normalized;
            if (!outward)
            {
                n = -n;
            }

            int v0 = vertices.Count;
            vertices.Add(new Vector3(a.x, y0, a.y));
            vertices.Add(new Vector3(b.x, y0, b.y));
            vertices.Add(new Vector3(b.x, y1, b.y));
            vertices.Add(new Vector3(a.x, y1, a.y));
            for (int k = 0; k < 4; k++)
            {
                normals.Add(n);
                uvs.Add(Vector2.zero);
            }

            if (outward)
            {
                triangles.Add(v0);
                triangles.Add(v0 + 1);
                triangles.Add(v0 + 2);
                triangles.Add(v0);
                triangles.Add(v0 + 2);
                triangles.Add(v0 + 3);
            }
            else
            {
                triangles.Add(v0);
                triangles.Add(v0 + 2);
                triangles.Add(v0 + 1);
                triangles.Add(v0);
                triangles.Add(v0 + 3);
                triangles.Add(v0 + 2);
            }
        }
    }

    private static void AddBevelBand(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int> triangles,
        List<Vector2> uvs,
        Vector2[] lower,
        Vector2[] upper,
        float y0,
        float y1,
        bool outward)
    {
        int count = Mathf.Min(lower.Length, upper.Length);
        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            Vector3 a0 = new Vector3(lower[i].x, y0, lower[i].y);
            Vector3 b0 = new Vector3(lower[i1].x, y0, lower[i1].y);
            Vector3 b1 = new Vector3(upper[i1].x, y1, upper[i1].y);
            Vector3 a1 = new Vector3(upper[i].x, y1, upper[i].y);

            Vector3 n = Vector3.Cross(b0 - a0, a1 - a0).normalized;
            if (!outward)
            {
                n = -n;
            }

            // Soften bevel normal toward up for nicer highlights.
            n = Vector3.Normalize(n + Vector3.up * 0.35f);

            int v0 = vertices.Count;
            vertices.Add(a0);
            vertices.Add(b0);
            vertices.Add(b1);
            vertices.Add(a1);
            for (int k = 0; k < 4; k++)
            {
                normals.Add(n);
                uvs.Add(Vector2.zero);
            }

            if (outward)
            {
                triangles.Add(v0);
                triangles.Add(v0 + 1);
                triangles.Add(v0 + 2);
                triangles.Add(v0);
                triangles.Add(v0 + 2);
                triangles.Add(v0 + 3);
            }
            else
            {
                triangles.Add(v0);
                triangles.Add(v0 + 2);
                triangles.Add(v0 + 1);
                triangles.Add(v0);
                triangles.Add(v0 + 3);
                triangles.Add(v0 + 2);
            }
        }
    }
}
