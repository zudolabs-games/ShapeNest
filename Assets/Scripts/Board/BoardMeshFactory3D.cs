using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Presentation-only procedural meshes for the World3D board (rounded slab, cells, soft shadow).
/// Does not affect gameplay grid coordinates.
/// </summary>
public static class BoardMeshFactory3D
{
    private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

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
}
