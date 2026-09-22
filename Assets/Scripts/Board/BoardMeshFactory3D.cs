using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Presentation-only procedural meshes for the World3D board (rounded slab, cells, soft shadow).
/// Does not affect gameplay grid coordinates.
/// </summary>
public static class BoardMeshFactory3D
{
    private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

    public static void ClearCache()
    {
        Cache.Clear();
    }

    public static Mesh GetRoundedBox(float sizeX, float sizeY, float sizeZ, float cornerRadius, int cornerSegments = 4)
    {
        string key = $"rbox_{sizeX:F3}_{sizeY:F3}_{sizeZ:F3}_{cornerRadius:F3}_{cornerSegments}";
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        float hx = sizeX * 0.5f;
        float hy = sizeY * 0.5f;
        float hz = sizeZ * 0.5f;
        float r = Mathf.Clamp(cornerRadius, 0.01f, Mathf.Min(hx, hz) * 0.45f);
        Vector2[] outline = RoundedRectOutline(hx, hz, r, cornerSegments);

        Mesh mesh = ExtrudeFlat(outline, -hy, hy);
        mesh.name = "BoardRoundedBox";
        Cache[key] = mesh;
        return mesh;
    }

    public static Mesh GetCellTile(float sizeX, float sizeY, float sizeZ, float cornerRadius)
    {
        return GetMoldedPlaybedTile(sizeX, 0.04f * sizeX, 0.024f * sizeX, cornerRadius, 0.04f * sizeX, 4);
    }

    public static Mesh GetMoldedCellTile(float sizeX, float sizeY, float sizeZ, float cornerRadius, float topBevel)
    {
        string key = $"cell_molded_{sizeX:F3}_{sizeY:F3}_{sizeZ:F3}_{cornerRadius:F3}_{topBevel:F3}";
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        float hx = sizeX * 0.5f;
        float hy = sizeY * 0.5f;
        float hz = sizeZ * 0.5f;
        float r = Mathf.Clamp(cornerRadius, 0.01f, Mathf.Min(hx, hz) * 0.45f);
        float bevel = Mathf.Clamp(topBevel, 0.005f, Mathf.Min(hx, hz, hy) * 0.35f);

        Vector2[] loopBottom = RoundedRectOutline(hx, hz, r, 4);
        Vector2[] loopBevelStart = RoundedRectOutline(hx, hz, r, 4);
        Vector2[] loopTop = RoundedRectOutline(hx - bevel, hz - bevel, Mathf.Max(0.005f, r - bevel), 4);

        float y0 = -hy;
        float yBevel = hy - bevel;
        float y1 = hy;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        // Bottom cap
        AddCap(vertices, normals, tris, uvs, loopBottom, y0, Vector3.down);

        // Vertical side walls
        AddWallBand(vertices, normals, tris, uvs, loopBottom, loopBevelStart, y0, yBevel, outward: true, smooth: true);

        // Top chamfer bevel
        AddBevelBandBetweenLoops(vertices, normals, tris, uvs, loopBevelStart, loopTop, yBevel, y1, outward: true);

        // Top face
        AddCap(vertices, normals, tris, uvs, loopTop, y1, Vector3.up);

        var mesh = new Mesh { name = "BoardMoldedCellTile" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        Cache[key] = mesh;
        return mesh;
    }

    public static Mesh GetMoldedBoardTray(
        float outerX,
        float outerZ,
        float innerX,
        float innerZ,
        float totalHeight,
        float floorHeight,
        float outerRadius,
        float innerRadius,
        float rimBevel = 0.08f,
        int cornerSegments = 8)
    {
        string key = $"molded_tray_{outerX:F3}_{outerZ:F3}_{innerX:F3}_{innerZ:F3}_{totalHeight:F3}_{floorHeight:F3}_{outerRadius:F3}_{innerRadius:F3}_{rimBevel:F3}_{cornerSegments}";
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        float outerHx = outerX * 0.5f;
        float outerHz = outerZ * 0.5f;
        float innerHx = innerX * 0.5f;
        float innerHz = innerZ * 0.5f;

        float outerR = Mathf.Clamp(outerRadius, 0.02f, Mathf.Min(outerHx, outerHz) * 0.45f);
        float innerR = Mathf.Clamp(innerRadius, 0.02f, Mathf.Min(innerHx, innerHz) * 0.45f);
        float bevel = Mathf.Clamp(rimBevel, 0.01f, Mathf.Min(outerHx - innerHx, totalHeight - floorHeight) * 0.45f);

        Vector2[] loopOuterBottom = RoundedRectOutline(outerHx, outerHz, outerR, cornerSegments);
        Vector2[] loopOuterTopBevel = RoundedRectOutline(outerHx, outerHz, outerR, cornerSegments);
        Vector2[] loopRimOuter = RoundedRectOutline(outerHx - bevel, outerHz - bevel, Mathf.Max(0.01f, outerR - bevel), cornerSegments);
        Vector2[] loopRimInner = RoundedRectOutline(innerHx + bevel, innerHz + bevel, Mathf.Max(0.01f, innerR + bevel), cornerSegments);
        Vector2[] loopInnerFloor = RoundedRectOutline(innerHx, innerHz, innerR, cornerSegments);

        float y0 = 0f;
        float yBevelOuter = totalHeight - bevel;
        float yTop = totalHeight;
        float yFloor = floorHeight;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var trayTris = new List<int>();
        var floorTris = new List<int>();

        // 1. Bottom base cap (y = 0)
        AddCap(vertices, normals, trayTris, uvs, loopOuterBottom, y0, Vector3.down);

        // 2. Outer side walls (y0 -> yBevelOuter) with smooth normals around corners
        AddWallBand(vertices, normals, trayTris, uvs, loopOuterBottom, loopOuterTopBevel, y0, yBevelOuter, outward: true, smooth: true);

        // 3. Outer top bevel (yBevelOuter -> yTop) catching 45° specular highlight
        AddBevelBandBetweenLoops(vertices, normals, trayTris, uvs, loopOuterTopBevel, loopRimOuter, yBevelOuter, yTop, outward: true, smooth: true);

        // 4. Top rim flat (yTop: loopRimOuter -> loopRimInner)
        AddBridgeBand(vertices, normals, trayTris, uvs, loopRimOuter, loopRimInner, yTop, Vector3.up);

        // 5. Inner sloping walls (yTop -> yFloor: loopRimInner -> loopInnerFloor)
        AddBevelBandBetweenLoops(vertices, normals, trayTris, uvs, loopRimInner, loopInnerFloor, yTop, yFloor, outward: false, smooth: true);

        // 6. Recessed floor cap (submesh 1: yFloor)
        AddCap(vertices, normals, floorTris, uvs, loopInnerFloor, yFloor, Vector3.up);

        var mesh = new Mesh { name = "BoardMoldedTray" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(trayTris, 0);
        mesh.SetTriangles(floorTris, 1);
        mesh.RecalculateBounds();
        Cache[key] = mesh;
        return mesh;
    }

    private static void AddWallBand(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int> triangles,
        List<Vector2> uvs,
        Vector2[] loopBottom,
        Vector2[] loopTop,
        float y0,
        float y1,
        bool outward,
        bool smooth)
    {
        int count = Mathf.Min(loopBottom.Length, loopTop.Length);
        Vector3[] smoothNormals = null;
        if (smooth)
        {
            smoothNormals = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int prev = (i - 1 + count) % count;
                int next = (i + 1) % count;
                Vector2 dir = (loopBottom[next] - loopBottom[prev]).normalized;
                Vector3 n = Vector3.Cross(Vector3.up, new Vector3(dir.x, 0f, dir.y)).normalized;
                if (!outward) n = -n;
                smoothNormals[i] = n;
            }
        }

        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            Vector2 a2 = loopBottom[i];
            Vector2 b2 = loopBottom[i1];
            Vector3 a = new Vector3(a2.x, y0, a2.y);
            Vector3 b = new Vector3(b2.x, y0, b2.y);
            Vector3 c = new Vector3(loopTop[i1].x, y1, loopTop[i1].y);
            Vector3 d = new Vector3(loopTop[i].x, y1, loopTop[i].y);

            Vector3 edge = new Vector3(b2.x - a2.x, 0f, b2.y - a2.y);
            Vector3 nFlat = Vector3.Cross(Vector3.up, edge).normalized;
            if (!outward) nFlat = -nFlat;

            Vector3 na = smooth ? smoothNormals[i] : nFlat;
            Vector3 nb = smooth ? smoothNormals[i1] : nFlat;

            int v0 = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);

            normals.Add(na);
            normals.Add(nb);
            normals.Add(nb);
            normals.Add(na);

            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);

            if (outward)
            {
                triangles.Add(v0);
                triangles.Add(v0 + 2);
                triangles.Add(v0 + 1);
                triangles.Add(v0);
                triangles.Add(v0 + 3);
                triangles.Add(v0 + 2);
            }
            else
            {
                triangles.Add(v0);
                triangles.Add(v0 + 1);
                triangles.Add(v0 + 2);
                triangles.Add(v0);
                triangles.Add(v0 + 2);
                triangles.Add(v0 + 3);
            }
        }
    }

    private static void AddBevelBandBetweenLoops(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int> triangles,
        List<Vector2> uvs,
        Vector2[] loopA,
        Vector2[] loopB,
        float yA,
        float yB,
        bool outward,
        bool smooth = true)
    {
        int count = Mathf.Min(loopA.Length, loopB.Length);
        Vector3[] smoothNormals = null;
        if (smooth)
        {
            smoothNormals = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int prev = (i - 1 + count) % count;
                int next = (i + 1) % count;
                Vector2 dirA = (loopA[next] - loopA[prev]).normalized;
                Vector3 edgeA = new Vector3(dirA.x, 0f, dirA.y);
                Vector3 radial = Vector3.Cross(Vector3.up, edgeA).normalized;
                if (!outward) radial = -radial;
                float dy = yB - yA;
                smoothNormals[i] = (radial * 0.7071f + Vector3.up * (dy >= 0f ? 0.7071f : -0.7071f) * (outward ? 1f : -1f)).normalized;
            }
        }

        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            Vector3 a = new Vector3(loopA[i].x, yA, loopA[i].y);
            Vector3 b = new Vector3(loopA[i1].x, yA, loopA[i1].y);
            Vector3 c = new Vector3(loopB[i1].x, yB, loopB[i1].y);
            Vector3 d = new Vector3(loopB[i].x, yB, loopB[i].y);

            Vector3 side1 = b - a;
            Vector3 side2 = d - a;
            Vector3 nFlat = Vector3.Cross(side1, side2).normalized;
            if (!outward) nFlat = -nFlat;

            Vector3 na = smooth ? smoothNormals[i] : nFlat;
            Vector3 nb = smooth ? smoothNormals[i1] : nFlat;

            int v0 = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);

            normals.Add(na);
            normals.Add(nb);
            normals.Add(nb);
            normals.Add(na);

            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);

            if (outward)
            {
                triangles.Add(v0);
                triangles.Add(v0 + 2);
                triangles.Add(v0 + 1);
                triangles.Add(v0);
                triangles.Add(v0 + 3);
                triangles.Add(v0 + 2);
            }
            else
            {
                triangles.Add(v0);
                triangles.Add(v0 + 1);
                triangles.Add(v0 + 2);
                triangles.Add(v0);
                triangles.Add(v0 + 2);
                triangles.Add(v0 + 3);
            }
        }
    }

    private static void AddBridgeBand(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int> triangles,
        List<Vector2> uvs,
        Vector2[] loopOuter,
        Vector2[] loopInner,
        float y,
        Vector3 normal)
    {
        int count = Mathf.Min(loopOuter.Length, loopInner.Length);
        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            Vector3 a = new Vector3(loopOuter[i].x, y, loopOuter[i].y);
            Vector3 b = new Vector3(loopOuter[i1].x, y, loopOuter[i1].y);
            Vector3 c = new Vector3(loopInner[i1].x, y, loopInner[i1].y);
            Vector3 d = new Vector3(loopInner[i].x, y, loopInner[i].y);

            int v0 = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);

            for (int k = 0; k < 4; k++)
            {
                normals.Add(normal);
                uvs.Add(new Vector2(vertices[v0 + k].x * 0.1f + 0.5f, vertices[v0 + k].z * 0.1f + 0.5f));
            }

            triangles.Add(v0);
            triangles.Add(v0 + 2);
            triangles.Add(v0 + 1);
            triangles.Add(v0);
            triangles.Add(v0 + 3);
            triangles.Add(v0 + 2);
        }
    }

    /// <summary>
    /// Rounded highlight pad used only by destination-cell presentation.
    /// Submesh 0 = soft fill, submesh 1 = brighter rim + thin outer lip.
    /// </summary>
    public static Mesh GetHighlightTile(float sizeX, float sizeY, float sizeZ, float cornerRadius)
    {
        string key = $"hlite_{sizeX:F3}_{sizeY:F3}_{sizeZ:F3}_{cornerRadius:F3}";
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        float hx = sizeX * 0.5f;
        float hy = Mathf.Max(0.008f, sizeY * 0.5f);
        float hz = sizeZ * 0.5f;
        float r = Mathf.Clamp(cornerRadius, 0.01f, Mathf.Min(hx, hz) * 0.45f);
        float rim = Mathf.Clamp(Mathf.Min(hx, hz) * 0.11f, 0.016f, 0.05f);
        Vector2[] outer = RoundedRectOutline(hx, hz, r, 4);
        float innerHx = Mathf.Max(0.02f, hx - rim);
        float innerHz = Mathf.Max(0.02f, hz - rim);
        float innerR = Mathf.Clamp(r * 0.72f, 0.008f, Mathf.Min(innerHx, innerHz) * 0.45f);
        Vector2[] inner = RoundedRectOutline(innerHx, innerHz, innerR, 4);

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var fillTris = new List<int>();
        var rimTris = new List<int>();

        float topY = hy;

        int fillStart = vertices.Count;
        vertices.Add(new Vector3(0f, topY, 0f));
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < inner.Length; i++)
        {
            vertices.Add(new Vector3(inner[i].x, topY, inner[i].y));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(inner[i].x / (hx * 2f) + 0.5f, inner[i].y / (hz * 2f) + 0.5f));
        }

        for (int i = 0; i < inner.Length; i++)
        {
            fillTris.Add(fillStart);
            fillTris.Add(fillStart + 1 + i);
            fillTris.Add(fillStart + 1 + ((i + 1) % inner.Length));
        }

        int rimCount = Mathf.Min(outer.Length, inner.Length);
        for (int i = 0; i < rimCount; i++)
        {
            int i1 = (i + 1) % rimCount;
            int v = vertices.Count;
            vertices.Add(new Vector3(inner[i].x, topY, inner[i].y));
            vertices.Add(new Vector3(outer[i].x, topY, outer[i].y));
            vertices.Add(new Vector3(outer[i1].x, topY, outer[i1].y));
            vertices.Add(new Vector3(inner[i1].x, topY, inner[i1].y));
            for (int k = 0; k < 4; k++)
            {
                normals.Add(Vector3.up);
                uvs.Add(Vector2.one * 0.5f);
            }

            rimTris.Add(v);
            rimTris.Add(v + 1);
            rimTris.Add(v + 2);
            rimTris.Add(v);
            rimTris.Add(v + 2);
            rimTris.Add(v + 3);
        }

        for (int i = 0; i < outer.Length; i++)
        {
            int i1 = (i + 1) % outer.Length;
            Vector2 a = outer[i];
            Vector2 b = outer[i1];
            Vector3 edge = new Vector3(b.x - a.x, 0f, b.y - a.y);
            Vector3 n = Vector3.Cross(Vector3.up, edge).normalized;
            int v = vertices.Count;
            vertices.Add(new Vector3(a.x, -hy, a.y));
            vertices.Add(new Vector3(b.x, -hy, b.y));
            vertices.Add(new Vector3(b.x, topY, b.y));
            vertices.Add(new Vector3(a.x, topY, a.y));
            for (int k = 0; k < 4; k++)
            {
                normals.Add(n);
                uvs.Add(Vector2.zero);
            }

            rimTris.Add(v);
            rimTris.Add(v + 1);
            rimTris.Add(v + 2);
            rimTris.Add(v);
            rimTris.Add(v + 2);
            rimTris.Add(v + 3);
        }

        var mesh = new Mesh { name = "BoardHighlightTile" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(fillTris, 0, true);
        mesh.SetTriangles(rimTris, 1, true);
        mesh.RecalculateBounds();
        Cache[key] = mesh;
        return mesh;
    }

    public static Mesh GetShadowDisc(int segments = 32)
    {
        const string key = "shadow_disc";
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        var vertices = new List<Vector3>(segments + 1);
        var normals = new List<Vector3>(segments + 1);
        var uvs = new List<Vector2>(segments + 1);
        var triangles = new List<int>(segments * 3);

        vertices.Add(Vector3.zero);
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 0.5f));

        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float x = Mathf.Cos(a);
            float z = Mathf.Sin(a);
            vertices.Add(new Vector3(x, 0f, z));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f));
        }

        for (int i = 0; i < segments; i++)
        {
            int i1 = 1 + i;
            int i2 = 1 + ((i + 1) % segments);
            triangles.Add(0);
            triangles.Add(i1);
            triangles.Add(i2);
        }

        var mesh = new Mesh { name = "BoardShadowDisc" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        Cache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// Soft contact shadow disc with radial alpha falloff via vertex colors.
    /// Phase 52I: stronger center plateau, softer outer rim (not a hard cookie).
    /// </summary>
    public static Mesh GetSoftContactShadowDisc(int segments = 36, int rings = 4)
    {
        segments = Mathf.Clamp(segments, 12, 64);
        rings = Mathf.Clamp(rings, 3, 8);
        string key = "soft_contact_shadow_v2_" + segments + "_" + rings;
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        int ringVertCount = segments * rings + 1;
        var vertices = new List<Vector3>(ringVertCount);
        var normals = new List<Vector3>(ringVertCount);
        var uvs = new List<Vector2>(ringVertCount);
        var colors = new List<Color>(ringVertCount);
        var triangles = new List<int>(segments * rings * 6);

        vertices.Add(Vector3.zero);
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 0.5f));
        // Center stays fully opaque for a grounded contact cue.
        colors.Add(new Color(1f, 1f, 1f, 1f));

        for (int r = 1; r <= rings; r++)
        {
            float t = r / (float)rings;
            // Smoothstep then ease: holds center strength, softens the outer edge.
            float s = t * t * (3f - (2f * t));
            float alpha = 1f - (s * s);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float x = Mathf.Cos(a) * t;
                float z = Mathf.Sin(a) * t;
                vertices.Add(new Vector3(x, 0f, z));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f));
                colors.Add(new Color(1f, 1f, 1f, alpha));
            }
        }

        // Center fan to first ring.
        for (int i = 0; i < segments; i++)
        {
            int i1 = 1 + i;
            int i2 = 1 + ((i + 1) % segments);
            triangles.Add(0);
            triangles.Add(i1);
            triangles.Add(i2);
        }

        // Concentric bands.
        for (int r = 0; r < rings - 1; r++)
        {
            int inner = 1 + (r * segments);
            int outer = 1 + ((r + 1) * segments);
            for (int i = 0; i < segments; i++)
            {
                int i1 = (i + 1) % segments;
                int a = inner + i;
                int b = inner + i1;
                int c = outer + i;
                int d = outer + i1;
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(d);
                triangles.Add(a);
                triangles.Add(d);
                triangles.Add(b);
            }
        }

        var mesh = new Mesh { name = "SoftContactShadowDisc_v2" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        Cache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// Soft rounded-box board grounding shadow with smooth vertex-alpha falloff.
    /// Perfectly matches the tray footprint with no artificial disc/oval boundary.
    /// </summary>
    public static Mesh GetSoftBoardContactShadow(float sizeX, float sizeZ, float cornerRadius, float softness = 0.22f)
    {
        string key = $"soft_board_shadow_{sizeX:F2}_{sizeZ:F2}_{cornerRadius:F2}_{softness:F2}";
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        float hx = sizeX * 0.5f;
        float hz = sizeZ * 0.5f;
        float r = Mathf.Clamp(cornerRadius, 0.05f, Mathf.Min(hx, hz) * 0.45f);

        Vector2[] innerLoop = RoundedRectOutline(hx, hz, r, 6);
        Vector2[] outerLoop = RoundedRectOutline(hx + softness, hz + softness, r + softness, 6);

        int count = innerLoop.Length;
        var vertices = new List<Vector3>(count * 2 + 1);
        var normals = new List<Vector3>(count * 2 + 1);
        var uvs = new List<Vector2>(count * 2 + 1);
        var colors = new List<Color>(count * 2 + 1);
        var triangles = new List<int>(count * 9);

        int centerIdx = vertices.Count;
        vertices.Add(Vector3.zero);
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 0.5f));
        colors.Add(new Color(1f, 1f, 1f, 1f));

        int innerStart = vertices.Count;
        for (int i = 0; i < count; i++)
        {
            vertices.Add(new Vector3(innerLoop[i].x, 0f, innerLoop[i].y));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(innerLoop[i].x / (sizeX + softness * 2f) + 0.5f, innerLoop[i].y / (sizeZ + softness * 2f) + 0.5f));
            colors.Add(new Color(1f, 1f, 1f, 1f));
        }

        int outerStart = vertices.Count;
        for (int i = 0; i < count; i++)
        {
            vertices.Add(new Vector3(outerLoop[i].x, 0f, outerLoop[i].y));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(outerLoop[i].x / (sizeX + softness * 2f) + 0.5f, outerLoop[i].y / (sizeZ + softness * 2f) + 0.5f));
            colors.Add(new Color(1f, 1f, 1f, 0f));
        }

        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            triangles.Add(centerIdx);
            triangles.Add(innerStart + i);
            triangles.Add(innerStart + i1);
        }

        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            int a = innerStart + i;
            int b = innerStart + i1;
            int c = outerStart + i1;
            int d = outerStart + i;

            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);

            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
        }

        var mesh = new Mesh { name = "SoftBoardContactShadow" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        Cache[key] = mesh;
        return mesh;
    }

    private static Vector2[] RoundedRectOutline(float halfX, float halfZ, float radius, int segments)
    {
        var points = new List<Vector2>((segments + 1) * 4);
        AddCornerArc(points, halfX - radius, halfZ - radius, radius, 0f, 90f, segments);
        AddCornerArc(points, -halfX + radius, halfZ - radius, radius, 90f, 180f, segments);
        AddCornerArc(points, -halfX + radius, -halfZ + radius, radius, 180f, 270f, segments);
        AddCornerArc(points, halfX - radius, -halfZ + radius, radius, 270f, 360f, segments);
        return points.ToArray();
    }

    private static void AddCornerArc(
        List<Vector2> points,
        float cx,
        float cz,
        float radius,
        float startDeg,
        float endDeg,
        int segments)
    {
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float a = Mathf.Lerp(startDeg, endDeg, t) * Mathf.Deg2Rad;
            points.Add(new Vector2(cx + Mathf.Cos(a) * radius, cz + Mathf.Sin(a) * radius));
        }
    }

    private static Mesh ExtrudeFlat(Vector2[] outline, float y0, float y1)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        AddCap(vertices, normals, triangles, uvs, outline, y1, Vector3.up);
        AddCap(vertices, normals, triangles, uvs, outline, y0, Vector3.down);
        AddWalls(vertices, normals, triangles, uvs, outline, y0, y1);

        var mesh = new Mesh { name = "ExtrudedRounded" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddCap(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int> triangles,
        List<Vector2> uvs,
        Vector2[] outline,
        float y,
        Vector3 normal)
    {
        int start = vertices.Count;
        for (int i = 0; i < outline.Length; i++)
        {
            vertices.Add(new Vector3(outline[i].x, y, outline[i].y));
            normals.Add(normal);
            uvs.Add(outline[i] * 0.1f + Vector2.one * 0.5f);
        }

        for (int i = 1; i < outline.Length - 1; i++)
        {
            if (normal.y > 0f)
            {
                triangles.Add(start);
                triangles.Add(start + i + 1);
                triangles.Add(start + i);
            }
            else
            {
                triangles.Add(start);
                triangles.Add(start + i);
                triangles.Add(start + i + 1);
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
        float y1)
    {
        for (int i = 0; i < ring.Length; i++)
        {
            int i1 = (i + 1) % ring.Length;
            Vector2 a = ring[i];
            Vector2 b = ring[i1];
            Vector3 edge = new Vector3(b.x - a.x, 0f, b.y - a.y);
            Vector3 n = Vector3.Cross(Vector3.up, edge).normalized;

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

            triangles.Add(v0);
            triangles.Add(v0 + 1);
            triangles.Add(v0 + 2);
            triangles.Add(v0);
            triangles.Add(v0 + 2);
            triangles.Add(v0 + 3);
        }
    }

    /// <summary>
    /// Phase 3J: Isolated single molded socket prototype on an integrated toy-plastic tray.
    /// Creates a continuous physical manufactured cavity with smooth shoulder fillet,
    /// drafted wall, bottom fillet, flat recessed floor, surrounding shelf, and chunky outer casing.
    /// Submesh 0 = Tray frame & top shelf, Submesh 1 = Molded recessed socket cavity.
    /// </summary>
    public static Mesh GetSingleSocketPrototype(
        float traySize = 4.80f,
        float openSize = 2.40f,
        float socketDepth = 0.28f,
        float openRadius = 0.55f,
        float frameWall = 0.45f,
        float frameHeight = 0.12f,
        float boardThickness = 0.45f)
    {
        string key = $"proto_socket_v6_{traySize:F3}_{openSize:F3}_{socketDepth:F3}_{openRadius:F3}_{frameWall:F3}_{frameHeight:F3}_{boardThickness:F3}";
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        float outerHx = traySize * 0.5f;
        float outerHz = traySize * 0.5f;
        float outerR = Mathf.Clamp(traySize * 0.18f, 0.12f, outerHx * 0.45f);

        float innerHx = outerHx - frameWall;
        float innerHz = outerHz - frameWall;
        float innerR = Mathf.Max(0.04f, outerR - frameWall * 0.45f);

        float openHalf = openSize * 0.5f;
        float openR = Mathf.Clamp(openRadius, 0.04f, openHalf * 0.45f);

        const int segs = 8;
        const int count = 32;

        System.Func<float, float, float, (Vector2[] pts, Vector2[] outN)> getLoopWithNormals = (hx, hz, r) =>
        {
            var pts = new Vector2[count];
            var outN = new Vector2[count];
            int idx = 0;

            // Corner 0: (+X, +Z)
            float c0x = hx - r; float c0z = hz - r;
            for (int i = 0; i < segs; i++)
            {
                float a = (90f * i / segs) * Mathf.Deg2Rad;
                float ca = Mathf.Cos(a); float sa = Mathf.Sin(a);
                pts[idx] = new Vector2(c0x + ca * r, c0z + sa * r);
                outN[idx] = new Vector2(ca, sa).normalized;
                idx++;
            }
            // Corner 1: (-X, +Z)
            float c1x = -hx + r; float c1z = hz - r;
            for (int i = 0; i < segs; i++)
            {
                float a = (90f + 90f * i / segs) * Mathf.Deg2Rad;
                float ca = Mathf.Cos(a); float sa = Mathf.Sin(a);
                pts[idx] = new Vector2(c1x + ca * r, c1z + sa * r);
                outN[idx] = new Vector2(ca, sa).normalized;
                idx++;
            }
            // Corner 2: (-X, -Z)
            float c2x = -hx + r; float c2z = -hz + r;
            for (int i = 0; i < segs; i++)
            {
                float a = (180f + 90f * i / segs) * Mathf.Deg2Rad;
                float ca = Mathf.Cos(a); float sa = Mathf.Sin(a);
                pts[idx] = new Vector2(c2x + ca * r, c2z + sa * r);
                outN[idx] = new Vector2(ca, sa).normalized;
                idx++;
            }
            // Corner 3: (+X, -Z)
            float c3x = hx - r; float c3z = -hz + r;
            for (int i = 0; i < segs; i++)
            {
                float a = (270f + 90f * i / segs) * Mathf.Deg2Rad;
                float ca = Mathf.Cos(a); float sa = Mathf.Sin(a);
                pts[idx] = new Vector2(c3x + ca * r, c3z + sa * r);
                outN[idx] = new Vector2(ca, sa).normalized;
                idx++;
            }
            return (pts, outN);
        };

        var (outerLoop, outerOutN) = getLoopWithNormals(outerHx, outerHz, outerR);
        var (outerLipLoop, outerLipOutN) = getLoopWithNormals(outerHx - frameWall * 0.30f, outerHz - frameWall * 0.30f, Mathf.Max(0.06f, outerR - frameWall * 0.20f));
        var (frameInnerLoop, frameInnerOutN) = getLoopWithNormals(innerHx, innerHz, innerR);

        // Cavity profile:
        var (r0, r0OutN) = getLoopWithNormals(openHalf, openHalf, openR);
        float y0 = 0f;

        float inset1 = openSize * 0.05f;
        var (r1, r1OutN) = getLoopWithNormals(openHalf - inset1, openHalf - inset1, Mathf.Max(0.05f, openR - inset1 * 0.5f));
        float y1 = -socketDepth * 0.10f;

        float inset2 = openSize * 0.12f;
        var (r2, r2OutN) = getLoopWithNormals(openHalf - inset2, openHalf - inset2, Mathf.Max(0.04f, openR - inset2 * 0.5f));
        float y2 = -socketDepth * 0.32f;

        float inset3 = openSize * 0.20f;
        var (r3, r3OutN) = getLoopWithNormals(openHalf - inset3, openHalf - inset3, Mathf.Max(0.03f, openR - inset3 * 0.5f));
        float y3 = -socketDepth * 0.65f;

        float inset4 = openSize * 0.26f;
        var (r4, r4OutN) = getLoopWithNormals(openHalf - inset4, openHalf - inset4, Mathf.Max(0.02f, openR - inset4 * 0.5f));
        float y4 = -socketDepth * 0.90f;

        float inset5 = openSize * 0.30f;
        var (r5, r5OutN) = getLoopWithNormals(openHalf - inset5, openHalf - inset5, Mathf.Max(0.015f, openR - inset5 * 0.5f));
        float y5 = -socketDepth;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var frameTris = new List<int>();
        var cavityTris = new List<int>();

        System.Action<List<int>, int, int, int, int> addQuadTo = (targetList, v0, v1, v2, v3) =>
        {
            targetList.Add(v0); targetList.Add(v1); targetList.Add(v2);
            targetList.Add(v0); targetList.Add(v2); targetList.Add(v3);
        };

        float yOuterBot = -boardThickness;
        float yOuterMid = frameHeight * 0.5f;
        float yOuterLip = frameHeight;
        float yShelf = 0f;

        // 1. Outer side wall (outward facing -> frameTris)
        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            Vector3 n0 = new Vector3(outerOutN[i].x, 0f, outerOutN[i].y);
            Vector3 n1 = new Vector3(outerOutN[i1].x, 0f, outerOutN[i1].y);
            Vector3 nMid0 = Vector3.Normalize(n0 * 0.85f + Vector3.up * 0.52f);
            Vector3 nMid1 = Vector3.Normalize(n1 * 0.85f + Vector3.up * 0.52f);

            int v = verts.Count;
            verts.Add(new Vector3(outerLoop[i].x, yOuterBot, outerLoop[i].y));
            verts.Add(new Vector3(outerLoop[i].x, yOuterMid, outerLoop[i].y));
            verts.Add(new Vector3(outerLoop[i1].x, yOuterMid, outerLoop[i1].y));
            verts.Add(new Vector3(outerLoop[i1].x, yOuterBot, outerLoop[i1].y));
            norms.Add(n0);
            norms.Add(nMid0);
            norms.Add(nMid1);
            norms.Add(n1);
            for (int k = 0; k < 4; k++) uvs.Add(Vector2.zero);
            addQuadTo(frameTris, v, v + 1, v + 2, v + 3);
        }

        // 2. Outer Lip Bevel (outward/upward facing -> frameTris)
        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            Vector3 nMid0 = Vector3.Normalize(new Vector3(outerOutN[i].x, 0f, outerOutN[i].y) * 0.85f + Vector3.up * 0.52f);
            Vector3 nMid1 = Vector3.Normalize(new Vector3(outerOutN[i1].x, 0f, outerOutN[i1].y) * 0.85f + Vector3.up * 0.52f);
            Vector3 nLip0 = Vector3.Normalize(new Vector3(outerLipOutN[i].x, 0f, outerLipOutN[i].y) * 0.35f + Vector3.up * 0.94f);
            Vector3 nLip1 = Vector3.Normalize(new Vector3(outerLipOutN[i1].x, 0f, outerLipOutN[i1].y) * 0.35f + Vector3.up * 0.94f);

            int v = verts.Count;
            verts.Add(new Vector3(outerLoop[i].x, yOuterMid, outerLoop[i].y));
            verts.Add(new Vector3(outerLipLoop[i].x, yOuterLip, outerLipLoop[i].y));
            verts.Add(new Vector3(outerLipLoop[i1].x, yOuterLip, outerLipLoop[i1].y));
            verts.Add(new Vector3(outerLoop[i1].x, yOuterMid, outerLoop[i1].y));
            norms.Add(nMid0);
            norms.Add(nLip0);
            norms.Add(nLip1);
            norms.Add(nMid1);
            for (int k = 0; k < 4; k++) uvs.Add(Vector2.zero);
            addQuadTo(frameTris, v, v + 1, v + 2, v + 3);
        }

        // Concentric band helper
        System.Action<List<int>, Vector2[], float, Vector2[], float, float, Vector2[], float, Vector2[], float, float> addConcentricBandTo =
            (targetList, innerPts, yIn, innerNorms2D, inWeightIn, upWeightIn,
             outerPts, yOut, outerNorms2D, inWeightOut, upWeightOut) =>
        {
            for (int i = 0; i < count; i++)
            {
                int i1 = (i + 1) % count;

                Vector3 inN0_in = new Vector3(-innerNorms2D[i].x, 0f, -innerNorms2D[i].y);
                Vector3 inN1_in = new Vector3(-innerNorms2D[i1].x, 0f, -innerNorms2D[i1].y);
                Vector3 inN0_out = new Vector3(-outerNorms2D[i].x, 0f, -outerNorms2D[i].y);
                Vector3 inN1_out = new Vector3(-outerNorms2D[i1].x, 0f, -outerNorms2D[i1].y);

                Vector3 nIn0 = Vector3.Normalize(inN0_in * inWeightIn + Vector3.up * upWeightIn);
                Vector3 nIn1 = Vector3.Normalize(inN1_in * inWeightIn + Vector3.up * upWeightIn);
                Vector3 nOut0 = Vector3.Normalize(inN0_out * inWeightOut + Vector3.up * upWeightOut);
                Vector3 nOut1 = Vector3.Normalize(inN1_out * inWeightOut + Vector3.up * upWeightOut);

                int v = verts.Count;
                verts.Add(new Vector3(innerPts[i].x, yIn, innerPts[i].y));
                verts.Add(new Vector3(innerPts[i1].x, yIn, innerPts[i1].y));
                verts.Add(new Vector3(outerPts[i1].x, yOut, outerPts[i1].y));
                verts.Add(new Vector3(outerPts[i].x, yOut, outerPts[i].y));

                norms.Add(nIn0);
                norms.Add(nIn1);
                norms.Add(nOut1);
                norms.Add(nOut0);

                for (int k = 0; k < 4; k++)
                {
                    uvs.Add(new Vector2(verts[v + k].x / traySize + 0.5f, verts[v + k].z / traySize + 0.5f));
                }
                addQuadTo(targetList, v, v + 1, v + 2, v + 3);
            }
        };

        // 3. Inner Frame Slope (-> frameTris)
        addConcentricBandTo(
            frameTris,
            frameInnerLoop, yShelf, frameInnerOutN, 0f, 1f,
            outerLipLoop, yOuterLip, outerLipOutN, -0.35f, 0.94f);

        // 4. Surrounding Flat Tray Shelf (-> frameTris)
        addConcentricBandTo(
            frameTris,
            r0, y0, r0OutN, 0f, 1f,
            frameInnerLoop, yShelf, frameInnerOutN, 0f, 1f);

        // 5. Cavity Bands (-> cavityTris)
        // R4 -> R5 (Bottom Fillet into Flat Floor: 0 deg Up to 30 deg)
        addConcentricBandTo(cavityTris, r5, y5, r5OutN, 0.00f, 1.00f, r4, y4, r4OutN, 0.50f, 0.86f);

        // R3 -> R4 (Bottom Fillet Entry: 30 deg to 60 deg)
        addConcentricBandTo(cavityTris, r4, y4, r4OutN, 0.50f, 0.86f, r3, y3, r3OutN, 0.86f, 0.50f);

        // R2 -> R3 (Drafted Wall: 60 deg to 38 deg)
        addConcentricBandTo(cavityTris, r3, y3, r3OutN, 0.86f, 0.50f, r2, y2, r2OutN, 0.62f, 0.79f);

        // R1 -> R2 (Shoulder Curve: 38 deg to 15 deg)
        addConcentricBandTo(cavityTris, r2, y2, r2OutN, 0.62f, 0.79f, r1, y1, r1OutN, 0.26f, 0.96f);

        // R0 -> R1 (Shoulder Entry: 15 deg to Top Flat)
        addConcentricBandTo(cavityTris, r1, y1, r1OutN, 0.26f, 0.96f, r0, y0, r0OutN, 0.00f, 1.00f);

        // 6. Flat Floor Cap (Y = -socketDepth -> cavityTris)
        int centerIdx = verts.Count;
        verts.Add(new Vector3(0f, y5, 0f));
        norms.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 0.5f));

        int floorStart = verts.Count;
        for (int i = 0; i < count; i++)
        {
            verts.Add(new Vector3(r5[i].x, y5, r5[i].y));
            norms.Add(Vector3.up);
            uvs.Add(new Vector2(r5[i].x / traySize + 0.5f, r5[i].y / traySize + 0.5f));
        }

        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            cavityTris.Add(centerIdx);
            cavityTris.Add(floorStart + i1);
            cavityTris.Add(floorStart + i);
        }

        // 7. Bottom Tray Cap (-> frameTris)
        int capStart = verts.Count;
        for (int i = 0; i < count; i++)
        {
            verts.Add(new Vector3(outerLoop[i].x, yOuterBot, outerLoop[i].y));
            norms.Add(Vector3.down);
            uvs.Add(Vector2.zero);
        }
        for (int i = 1; i < count - 1; i++)
        {
            frameTris.Add(capStart);
            frameTris.Add(capStart + i + 1);
            frameTris.Add(capStart + i);
        }

        var mesh = new Mesh { name = "BoardSingleSocketPrototype" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(frameTris, 0, true);
        mesh.SetTriangles(cavityTris, 1, true);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        Cache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// Pass B: Molded playbed cell tile with full-gap seam exposure, proportionally wider chamfer
    /// bevel derived from cellGap, and larger corner radius for injection-molded plastic feel.
    /// Seam = full cellGap, bevel = 1.5×gap, corner radius = 2.5×gap.
    /// Submesh 0 = full tile (single-material, no submesh split needed).
    /// </summary>
    public static Mesh GetMoldedPlaybedTile(
        float cellSize = 1.0f,
        float cellGap = 0.04f,
        float reliefHeight = 0.024f,
        float cornerRadius = 0.08f,
        float bevelWidth = 0.04f,
        int cornerSegments = 6)
    {
        // Derive all proportions from cellGap for consistent molded feel
        float gap = cellGap > 0.001f ? cellGap : cellSize * 0.04f;

        // Full-gap seam: tile occupies cellSize - gap so the seam = gap wide
        float span = cellSize - gap;
        float halfSpan = span * 0.5f;

        // Molded plastic parameters derived from gap
        float r   = Mathf.Clamp(gap * 2.5f, 0.01f, halfSpan * 0.28f);
        float bevel = Mathf.Clamp(gap * 1.5f, 0.01f, halfSpan * 0.20f);
        float h   = Mathf.Max(0.005f, gap * 0.80f);

        // Override with explicit params if callers provide non-default values
        if (!Mathf.Approximately(cornerRadius, 0.08f)) r = Mathf.Clamp(cornerRadius, 0.01f, halfSpan * 0.28f);
        if (!Mathf.Approximately(bevelWidth, 0.04f))   bevel = Mathf.Clamp(bevelWidth, 0.01f, halfSpan * 0.20f);
        if (!Mathf.Approximately(reliefHeight, 0.024f)) h = Mathf.Max(0.005f, reliefHeight);

        string key = $"playbed_tile_{cellSize:F3}_{gap:F3}_{h:F3}_{r:F3}_{bevel:F3}_{cornerSegments}";
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        // Three profile loops: base perimeter, bevel-start, and chamfered top
        Vector2[] loopBase        = RoundedRectOutline(halfSpan, halfSpan, r, cornerSegments);
        Vector2[] loopBevelBase   = RoundedRectOutline(halfSpan, halfSpan, r, cornerSegments);
        Vector2[] loopTop         = RoundedRectOutline(
            Mathf.Max(0.01f, halfSpan - bevel),
            Mathf.Max(0.01f, halfSpan - bevel),
            Mathf.Max(0.005f, r - bevel),
            cornerSegments);

        // Y-levels: base at 0, bevel kick-in at 15% of height, full top at h
        float y0     = 0f;
        float yBevel = h * 0.15f;
        float yTop   = h;

        var vertices  = new List<Vector3>();
        var normals   = new List<Vector3>();
        var uvs       = new List<Vector2>();
        var triangles = new List<int>();

        // 1. Bottom cap (faces down, hidden but needed for shadow casting)
        AddCap(vertices, normals, triangles, uvs, loopBase, y0, Vector3.down);

        // 2. Short vertical skirt (y0 → yBevel) with outward smooth normals
        AddWallBand(vertices, normals, triangles, uvs, loopBase, loopBevelBase, y0, yBevel, outward: true, smooth: true);

        // 3. Chamfer bevel (yBevel → yTop) — 45° outward+upward normals catch keylight
        AddBevelBandBetweenLoops(vertices, normals, triangles, uvs, loopBevelBase, loopTop, yBevel, yTop, outward: true, smooth: true);

        // 4. Flat top playbed surface
        AddCap(vertices, normals, triangles, uvs, loopTop, yTop, Vector3.up);

        var mesh = new Mesh { name = "BoardMoldedPlaybedTile" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        Cache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// Legacy alias for cell visual generation — routes directly to molded playbed tile.
    /// </summary>
    public static Mesh GetMoldedSocketTile(
        float cellSize = 1.0f,
        float socketDepth = 0.11f,
        float openRatio = 0.88f,
        float cornerRadius = 0.18f)
    {
        return GetMoldedPlaybedTile(cellSize, 0.04f * cellSize, 0.024f * cellSize, cornerRadius, 0.04f * cellSize, 4);
    }
}


