using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural 3D meshes for World3D piece/nest presentation.
/// ShapeType stays a gameplay identity; this factory is the only mesh mapping.
/// Meshes are unit-sized (XZ footprint 1, height 1) with AABB center at local origin
/// so a PieceView3D on a cell center renders the shape in that cell.
/// </summary>
public static class ShapeMeshFactory3D
{
    private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

    public static void ClearCache()
    {
        Cache.Clear();
    }

    public static Mesh GetSolidMesh(ShapeType shape)
    {
        string key = "solid_v8_" + shape;
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        Mesh mesh = BuildSolid(shape);
        mesh.name = "ShapeSolid_" + shape;
        Cache[key] = mesh;
        return mesh;
    }

    public static Mesh GetNestMesh(ShapeType shape)
    {
        string key = "nest_v9_" + shape;
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        Mesh mesh = BuildNest(shape);
        mesh.name = "ShapeNest_" + shape;
        Cache[key] = mesh;
        return mesh;
    }

    private static Mesh BuildSolid(ShapeType shape)
    {
        Vector2[] outline = GetOutline(shape, 0.5f);
        NormalizeOutlineAabb(outline, null);
        ApplyVisualSilhouetteScale(outline, null, shape);
        ShapeBuild build = GetBuild(shape, nest: false);
        return BuildBeveledPrism(
            outline,
            null,
            1f,
            build.bevel,
            build.smoothWalls,
            addBottomBevel: true,
            addNestFloor: false);
    }

    private static Mesh BuildNest(ShapeType shape)
    {
        Vector2[] outer = GetOutline(shape, 0.5f);
        // Phase 52C: cavity ~block/nest footprint ratio so solids read as fitting the socket.
        Vector2[] inner = GetOutline(shape, 0.42f);
        NormalizeOutlineAabb(outer, inner);
        ApplyVisualSilhouetteScale(outer, inner, shape);
        ShapeBuild build = GetBuild(shape, nest: true);
        return BuildBeveledPrism(
            outer,
            inner,
            1f,
            build.bevel,
            build.smoothWalls,
            addBottomBevel: false,
            addNestFloor: true);
    }

    private struct ShapeBuild
    {
        public float bevel;
        public bool smoothWalls;
    }

    private static ShapeBuild GetBuild(ShapeType shape, bool nest)
    {
        // Phase 52F: slightly stronger bevels for soft highlight catch — footprint unchanged.
        switch (shape)
        {
            case ShapeType.Circle:
                return new ShapeBuild { bevel = nest ? 0.11f : 0.11f, smoothWalls = true };
            case ShapeType.Square:
                return new ShapeBuild { bevel = nest ? 0.10f : 0.095f, smoothWalls = false };
            case ShapeType.Triangle:
                return new ShapeBuild { bevel = nest ? 0.07f : 0.065f, smoothWalls = false };
            case ShapeType.Diamond:
                return new ShapeBuild { bevel = nest ? 0.10f : 0.095f, smoothWalls = false };
            case ShapeType.Hexagon:
                return new ShapeBuild { bevel = nest ? 0.09f : 0.085f, smoothWalls = false };
            case ShapeType.Star:
                return new ShapeBuild { bevel = nest ? 0.06f : 0.055f, smoothWalls = false };
            case ShapeType.Pentagon:
                return new ShapeBuild { bevel = nest ? 0.085f : 0.08f, smoothWalls = false };
            default:
                return new ShapeBuild { bevel = 0.10f, smoothWalls = false };
        }
    }

    private static Vector2[] GetOutline(ShapeType shape, float radius)
    {
        switch (shape)
        {
            case ShapeType.Circle:
                return RegularPolygon(48, radius);
            case ShapeType.Triangle:
                return RegularPolygon(3, radius, -90f * Mathf.Deg2Rad);
            case ShapeType.Diamond:
                return RegularPolygon(4, radius, 0f);
            case ShapeType.Hexagon:
                return RegularPolygon(6, radius, 30f * Mathf.Deg2Rad);
            case ShapeType.Star:
                return StarPolygon(5, radius, radius * 0.42f, -90f * Mathf.Deg2Rad);
            case ShapeType.Pentagon:
                return RegularPolygon(5, radius, -90f * Mathf.Deg2Rad);
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

    /// <summary>
    /// After AABB normalization, nudges silhouettes that read smaller at the same unit AABB
    /// (concave star, pointy triangle, flat-top hex) so perceived footprint matches square/circle.
    /// Scales about the origin — center stays at 0,0.
    /// </summary>
    private static void ApplyVisualSilhouetteScale(Vector2[] outer, Vector2[] inner, ShapeType shape)
    {
        float scale = GetVisualSilhouetteScale(shape);
        if (Mathf.Abs(scale - 1f) < 0.0001f)
        {
            return;
        }

        ScaleOutlineInPlace(outer, scale);
        if (inner != null)
        {
            ScaleOutlineInPlace(inner, scale);
        }
    }

    private static float GetVisualSilhouetteScale(ShapeType shape)
    {
        switch (shape)
        {
            case ShapeType.Triangle:
                return 1.04f;
            case ShapeType.Hexagon:
                return 1.03f;
            case ShapeType.Star:
                return 1.08f;
            case ShapeType.Pentagon:
                return 1.03f;
            default:
                return 1f;
        }
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
    /// Centers the XZ AABB on the origin, then scales so the larger axis fills a unit square.
    /// Uses the footprint AABB (not the polygon centroid) so the shape sits in the cell.
    /// Inner nest rings share the outer transform so walls stay even.
    /// </summary>
    private static void NormalizeOutlineAabb(Vector2[] outer, Vector2[] inner)
    {
        if (outer == null || outer.Length == 0)
        {
            return;
        }

        CenterOutlineAabb(outer, inner);
        GetAabb(outer, out float minX, out float maxX, out float minY, out float maxY);
        float extent = Mathf.Max(maxX - minX, maxY - minY, 0.0001f);
        float scale = 1f / extent;
        if (Mathf.Abs(scale - 1f) >= 0.0001f)
        {
            ScaleOutlineInPlace(outer, scale);
            if (inner != null)
            {
                ScaleOutlineInPlace(inner, scale);
            }
        }

        // Second pass kills float drift so the footprint center stays at 0,0.
        CenterOutlineAabb(outer, inner);
    }

    private static void CenterOutlineAabb(Vector2[] outer, Vector2[] inner)
    {
        GetAabb(outer, out float minX, out float maxX, out float minY, out float maxY);
        Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        if (center.sqrMagnitude <= 0.0000001f)
        {
            return;
        }

        TranslateOutline(outer, -center);
        if (inner != null)
        {
            TranslateOutline(inner, -center);
        }
    }

    private static void GetAabb(
        Vector2[] points,
        out float minX,
        out float maxX,
        out float minY,
        out float maxY)
    {
        minX = points[0].x;
        maxX = points[0].x;
        minY = points[0].y;
        maxY = points[0].y;
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
    }

    private static void TranslateOutline(Vector2[] points, Vector2 delta)
    {
        for (int i = 0; i < points.Length; i++)
        {
            points[i] += delta;
        }
    }

    private static void ScaleOutlineInPlace(Vector2[] points, float scale)
    {
        for (int i = 0; i < points.Length; i++)
        {
            points[i] *= scale;
        }
    }

    /// <summary>
    /// Extrudes a polygon in XZ into a real prism with thickness, side faces, and optional bevels.
    /// Circle uses smooth radial side normals (cylinder). Other shapes keep hard edges.
    /// </summary>
    private static Mesh BuildBeveledPrism(
        Vector2[] outer,
        Vector2[] inner,
        float height,
        float bevelFraction,
        bool smoothWalls,
        bool addBottomBevel,
        bool addNestFloor)
    {
        float y0 = -height * 0.5f;
        float y1 = height * 0.5f;
        float bevel = Mathf.Clamp(bevelFraction, 0.03f, 0.22f) * height;
        float yBevelTop = y1 - bevel;
        float yBevelBottom = addBottomBevel ? y0 + bevel : y0;
        // Keep the top face close to the footprint AABB so a heavy chamfer cannot
        // read as a shift when the board camera shows the near wall.
        float inset = 1f - Mathf.Clamp(bevelFraction, 0.03f, 0.22f) * 0.5f;
        Vector2[] outerTop = ScaleOutline(outer, inset);
        Vector2[] outerBottom = addBottomBevel ? ScaleOutline(outer, inset) : outer;
        bool hollow = inner != null && inner.Length >= 3;
        Vector2[] innerTop = hollow ? ScaleOutline(inner, Mathf.Lerp(1f, inset, 0.35f)) : null;
        Vector2[] innerBottom = hollow && addBottomBevel
            ? ScaleOutline(inner, Mathf.Lerp(1f, inset, 0.35f))
            : inner;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var rimTriangles = new List<int>();
        var cavityTriangles = new List<int>();
        bool splitNest = hollow && addNestFloor;

        AddCap(vertices, normals, rimTriangles, uvs, outerBottom, innerBottom, y0, Vector3.down, hollow);
        AddCap(vertices, normals, rimTriangles, uvs, outerTop, innerTop, y1, Vector3.up, hollow);

        if (addBottomBevel)
        {
            AddBevelBand(vertices, normals, rimTriangles, uvs, outerBottom, outer, y0, yBevelBottom, outward: true);
        }

        if (smoothWalls)
        {
            AddSmoothWalls(vertices, normals, rimTriangles, uvs, outer, yBevelBottom, yBevelTop, outward: true);
        }
        else
        {
            AddWalls(vertices, normals, rimTriangles, uvs, outer, yBevelBottom, yBevelTop, outward: true);
        }

        AddBevelBand(vertices, normals, rimTriangles, uvs, outer, outerTop, yBevelTop, y1, outward: true);

        if (hollow)
        {
            List<int> cavity = splitNest ? cavityTriangles : rimTriangles;
            if (addBottomBevel)
            {
                AddBevelBand(vertices, normals, cavity, uvs, innerBottom, inner, y0, yBevelBottom, outward: false);
            }

            if (smoothWalls)
            {
                AddSmoothWalls(vertices, normals, cavity, uvs, inner, yBevelBottom, yBevelTop, outward: false);
            }
            else
            {
                AddWalls(vertices, normals, cavity, uvs, inner, yBevelBottom, yBevelTop, outward: false);
            }

            AddBevelBand(vertices, normals, cavity, uvs, inner, innerTop, yBevelTop, y1, outward: false);

            if (addNestFloor)
            {
                // Deeper socket floor; tiny Y bias avoids coplanar z-fight with rim bevels.
                float floorY = Mathf.Lerp(y0, y1, 0.10f) - 0.001f;
                AddCap(vertices, normals, cavity, uvs, inner, null, floorY, Vector3.up, hollow: false);
            }
        }

        var mesh = new Mesh
        {
            name = hollow ? "PrismNest" : "PrismSolid"
        };
        CenterVerticesOnOrigin(vertices);
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        if (splitNest)
        {
            mesh.subMeshCount = 2;
            mesh.SetTriangles(rimTriangles, 0);
            mesh.SetTriangles(cavityTriangles, 1);
        }
        else
        {
            mesh.SetTriangles(rimTriangles, 0);
        }

        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Shifts the finished prism so the 3D AABB center is local (0,0,0).
    /// PieceView3D then sits on a cell center without a per-shape offset.
    /// </summary>
    private static void CenterVerticesOnOrigin(List<Vector3> vertices)
    {
        if (vertices == null || vertices.Count == 0)
        {
            return;
        }

        Vector3 min = vertices[0];
        Vector3 max = vertices[0];
        for (int i = 1; i < vertices.Count; i++)
        {
            Vector3 v = vertices[i];
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        Vector3 center = (min + max) * 0.5f;
        if (center.sqrMagnitude <= 0.0000001f)
        {
            return;
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            vertices[i] -= center;
        }
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
            // Fan from the footprint origin (AABB center), not the vertex-average
            // centroid, so Triangle/Star mass does not pull the hub off-cell.
            int centerIndex = vertices.Count;
            vertices.Add(new Vector3(0f, y, 0f));
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i < outer.Length; i++)
            {
                int i1 = (i + 1) % outer.Length;
                int ring0 = start + i;
                int ring1 = start + i1;
                if (normal.y > 0f)
                {
                    triangles.Add(centerIndex);
                    triangles.Add(ring0);
                    triangles.Add(ring1);
                }
                else
                {
                    triangles.Add(centerIndex);
                    triangles.Add(ring1);
                    triangles.Add(ring0);
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

            AddQuad(triangles, v0, outward);
        }
    }

    private static void AddSmoothWalls(
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
            Vector3 n0 = RadialNormal(a, outward);
            Vector3 n1 = RadialNormal(b, outward);

            int v0 = vertices.Count;
            vertices.Add(new Vector3(a.x, y0, a.y));
            vertices.Add(new Vector3(b.x, y0, b.y));
            vertices.Add(new Vector3(b.x, y1, b.y));
            vertices.Add(new Vector3(a.x, y1, a.y));
            normals.Add(n0);
            normals.Add(n1);
            normals.Add(n1);
            normals.Add(n0);
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);

            AddQuad(triangles, v0, outward);
        }
    }

    private static Vector3 RadialNormal(Vector2 point, bool outward)
    {
        Vector3 n = new Vector3(point.x, 0f, point.y);
        if (n.sqrMagnitude < 0.000001f)
        {
            n = Vector3.forward;
        }
        else
        {
            n.Normalize();
        }

        return outward ? n : -n;
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

            n = Vector3.Normalize(n + Vector3.up * 0.28f);

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

            AddQuad(triangles, v0, outward);
        }
    }

    private static void AddQuad(List<int> triangles, int v0, bool outward)
    {
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
