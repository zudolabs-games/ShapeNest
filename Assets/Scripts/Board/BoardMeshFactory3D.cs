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
        return GetRoundedBox(sizeX, sizeY, sizeZ, cornerRadius, 3);
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
    /// Phase 3K: Single cell prototype mesh representing a molded plastic socket cavity.
    /// Features continuous top shelf, smooth convex shoulder fillet, drafted inner wall (15°),
    /// smooth concave bottom fillet, and flat recessed floor.
    /// Submesh 0 = Top shelf flange, Submesh 1 = Recessed cavity interior.
    /// </summary>
    public static Mesh GetMoldedSocketTile(
        float cellSize = 1.0f,
        float socketDepth = 0.11f,
        float openRatio = 0.88f,
        float cornerRadius = 0.18f)
    {
        string key = $"proto_cell_molded_{cellSize:F3}_{socketDepth:F3}_{openRatio:F3}_{cornerRadius:F3}";
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        float halfP = cellSize * 0.5f;
        float openHalf = halfP * openRatio;
        float openR = Mathf.Clamp(cornerRadius, 0.02f, openHalf * 0.45f);

        const int segs = 8;
        const int count = 32;

        System.Func<float, float, float, (Vector2[] pts, Vector2[] outN)> getLoop = (hx, hz, r) =>
        {
            var pts = new Vector2[count];
            var outN = new Vector2[count];
            int idx = 0;
            float c0x = hx - r; float c0z = hz - r;
            for (int i = 0; i < segs; i++)
            {
                float a = (90f * i / segs) * Mathf.Deg2Rad;
                float ca = Mathf.Cos(a); float sa = Mathf.Sin(a);
                pts[idx] = new Vector2(c0x + ca * r, c0z + sa * r);
                outN[idx] = new Vector2(ca, sa).normalized;
                idx++;
            }
            float c1x = -hx + r; float c1z = hz - r;
            for (int i = 0; i < segs; i++)
            {
                float a = (90f + 90f * i / segs) * Mathf.Deg2Rad;
                float ca = Mathf.Cos(a); float sa = Mathf.Sin(a);
                pts[idx] = new Vector2(c1x + ca * r, c1z + sa * r);
                outN[idx] = new Vector2(ca, sa).normalized;
                idx++;
            }
            float c2x = -hx + r; float c2z = -hz + r;
            for (int i = 0; i < segs; i++)
            {
                float a = (180f + 90f * i / segs) * Mathf.Deg2Rad;
                float ca = Mathf.Cos(a); float sa = Mathf.Sin(a);
                pts[idx] = new Vector2(c2x + ca * r, c2z + sa * r);
                outN[idx] = new Vector2(ca, sa).normalized;
                idx++;
            }
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

        var (outerFlange, outerFlangeN) = getLoop(halfP, halfP, halfP * 0.12f);
        var (r0, r0N) = getLoop(openHalf, openHalf, openR);
        float y0 = 0f;

        float inset1 = openHalf * 0.05f;
        var (r1, r1N) = getLoop(openHalf - inset1, openHalf - inset1, Mathf.Max(0.02f, openR - inset1 * 0.5f));
        float y1 = -socketDepth * 0.12f;

        float inset2 = openHalf * 0.14f;
        var (r2, r2N) = getLoop(openHalf - inset2, openHalf - inset2, Mathf.Max(0.02f, openR - inset2 * 0.5f));
        float y2 = -socketDepth * 0.45f;

        float inset3 = openHalf * 0.22f;
        var (r3, r3N) = getLoop(openHalf - inset3, openHalf - inset3, Mathf.Max(0.02f, openR - inset3 * 0.5f));
        float y3 = -socketDepth * 0.80f;

        float inset4 = openHalf * 0.28f;
        var (r4, r4N) = getLoop(openHalf - inset4, openHalf - inset4, Mathf.Max(0.015f, openR - inset4 * 0.5f));
        float y4 = -socketDepth;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var shelfTris = new List<int>();
        var cavityTris = new List<int>();

        System.Action<List<int>, int, int, int, int> addQuadTo = (targetList, v0, v1, v2, v3) =>
        {
            targetList.Add(v0); targetList.Add(v1); targetList.Add(v2);
            targetList.Add(v0); targetList.Add(v2); targetList.Add(v3);
        };

        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            int v = verts.Count;
            verts.Add(new Vector3(r0[i].x, y0, r0[i].y));
            verts.Add(new Vector3(r0[i1].x, y0, r0[i1].y));
            verts.Add(new Vector3(outerFlange[i1].x, y0, outerFlange[i1].y));
            verts.Add(new Vector3(outerFlange[i].x, y0, outerFlange[i].y));

            for (int k = 0; k < 4; k++)
            {
                norms.Add(Vector3.up);
                uvs.Add(new Vector2(verts[v + k].x / cellSize + 0.5f, verts[v + k].z / cellSize + 0.5f));
            }
            addQuadTo(shelfTris, v, v + 1, v + 2, v + 3);
        }

        System.Action<List<int>, Vector2[], float, Vector2[], float, float, Vector2[], float, Vector2[], float, float> addBandTo =
            (targetList, innerPts, yIn, innerN, inW_In, upW_In, outerPts, yOut, outerN, inW_Out, upW_Out) =>
        {
            for (int i = 0; i < count; i++)
            {
                int i1 = (i + 1) % count;
                Vector3 inN0_in = new Vector3(-innerN[i].x, 0f, -innerN[i].y);
                Vector3 inN1_in = new Vector3(-innerN[i1].x, 0f, -innerN[i1].y);
                Vector3 inN0_out = new Vector3(-outerN[i].x, 0f, -outerN[i].y);
                Vector3 inN1_out = new Vector3(-outerN[i1].x, 0f, -outerN[i1].y);

                Vector3 nIn0 = Vector3.Normalize(inN0_in * inW_In + Vector3.up * upW_In);
                Vector3 nIn1 = Vector3.Normalize(inN1_in * inW_In + Vector3.up * upW_In);
                Vector3 nOut0 = Vector3.Normalize(inN0_out * inW_Out + Vector3.up * upW_Out);
                Vector3 nOut1 = Vector3.Normalize(inN1_out * inW_Out + Vector3.up * upW_Out);

                int v = verts.Count;
                verts.Add(new Vector3(innerPts[i].x, yIn, innerPts[i].y));
                verts.Add(new Vector3(innerPts[i1].x, yIn, innerPts[i1].y));
                verts.Add(new Vector3(outerPts[i1].x, yOut, outerPts[i1].y));
                verts.Add(new Vector3(outerPts[i].x, yOut, outerPts[i].y));

                norms.Add(nIn0); norms.Add(nIn1); norms.Add(nOut1); norms.Add(nOut0);
                for (int k = 0; k < 4; k++)
                {
                    uvs.Add(new Vector2(verts[v + k].x / cellSize + 0.5f, verts[v + k].z / cellSize + 0.5f));
                }
                addQuadTo(targetList, v, v + 1, v + 2, v + 3);
            }
        };

        addBandTo(cavityTris, r4, y4, r4N, 0.20f, 0.98f, r3, y3, r3N, 0.70f, 0.71f);
        addBandTo(cavityTris, r3, y3, r3N, 0.70f, 0.71f, r2, y2, r2N, 0.85f, 0.52f);
        addBandTo(cavityTris, r2, y2, r2N, 0.85f, 0.52f, r1, y1, r1N, 0.50f, 0.86f);
        addBandTo(cavityTris, r1, y1, r1N, 0.50f, 0.86f, r0, y0, r0N, 0.00f, 1.00f);

        int centerIdx = verts.Count;
        verts.Add(new Vector3(0f, y4, 0f));
        norms.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 0.5f));

        int floorStart = verts.Count;
        for (int i = 0; i < count; i++)
        {
            verts.Add(new Vector3(r4[i].x, y4, r4[i].y));
            norms.Add(Vector3.up);
            uvs.Add(new Vector2(r4[i].x / cellSize + 0.5f, r4[i].y / cellSize + 0.5f));
        }

        for (int i = 0; i < count; i++)
        {
            int i1 = (i + 1) % count;
            cavityTris.Add(centerIdx);
            cavityTris.Add(floorStart + i1);
            cavityTris.Add(floorStart + i);
        }

        var mesh = new Mesh { name = "BoardMoldedSocketTilePrototype" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(shelfTris, 0, true);
        mesh.SetTriangles(cavityTris, 1, true);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        Cache[key] = mesh;
        return mesh;
    }
}


