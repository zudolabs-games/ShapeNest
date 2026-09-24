using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Presentation-only 3D neck connector between two 4-connected chain cells.
/// Uses a purpose-built beveled bridge mesh with flat end faces (so end caps remain
/// 100% concealed inside shape bodies without protruding rounded capsule caps).
/// </summary>
[DisallowMultipleComponent]
public class ChainConnectorView3D : MonoBehaviour
{
    private const float UnitMeshLength = 1f;
    private const float UnitMeshDiameter = 1f;

    private float restLength;
    private float restCrossHeight;
    private float restCrossThickness;
    private float restOcclusionDrop;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    public float RestLength => restLength;
    public float RestCrossHeight => restCrossHeight;
    public float RestCrossThickness => restCrossThickness;

    public void Configure(float cellPitch, float blockHeight, Material material)
    {
        EnsureMesh();
        float pitch = Mathf.Max(0.01f, cellPitch);
        float height = Mathf.Max(0.01f, blockHeight);
        restLength = pitch * BoardAdaptivePresentation3D.ConnectorLengthOverlapRatio;
        restCrossHeight = height * BoardAdaptivePresentation3D.ConnectorCrossHeightRatio;
        restCrossThickness = pitch * BoardAdaptivePresentation3D.ConnectorRadiusRatio * 2f;
        restOcclusionDrop = height * BoardAdaptivePresentation3D.ConnectorOcclusionDropRatio;

        if (meshRenderer != null && material != null)
        {
            meshRenderer.sharedMaterial = material;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
        }
    }

    /// <summary>
    /// Places and orients the bridge between existing chain endpoints. Does not alter endpoint logic.
    /// </summary>
    public void Follow(Vector3 worldA, Vector3 worldB, Vector3 scaleFactor, float occlusionDropScale = 1f)
    {
        Vector3 delta = worldB - worldA;
        Vector3 axis = new Vector3(delta.x, 0f, delta.z);
        float span = axis.magnitude;
        if (span <= 0.0001f)
        {
            axis = Vector3.right;
        }
        else
        {
            axis /= span;
        }

        Vector3 mid = (worldA + worldB) * 0.5f;
        mid.y -= restOcclusionDrop * Mathf.Max(0.25f, occlusionDropScale);

        bool mostlyX = Mathf.Abs(axis.x) >= Mathf.Abs(axis.z);
        float axisFactor = mostlyX ? scaleFactor.x : scaleFactor.z;
        float length = restLength * axisFactor;
        float crossHeight = restCrossHeight * Mathf.Max(0.35f, scaleFactor.y);
        float crossThickness = restCrossThickness * Mathf.Max(0.35f, scaleFactor.y);

        transform.position = mid;
        transform.rotation = Quaternion.FromToRotation(Vector3.up, axis);

        if (mostlyX)
        {
            // Bridge Y→world X: local X→world Y (height), local Z→world Z (thickness).
            transform.localScale = new Vector3(
                crossHeight / UnitMeshDiameter,
                length / UnitMeshLength,
                crossThickness / UnitMeshDiameter);
        }
        else
        {
            // Bridge Y→world Z: local Z→world Y (height), local X→world X (thickness).
            transform.localScale = new Vector3(
                crossThickness / UnitMeshDiameter,
                length / UnitMeshLength,
                crossHeight / UnitMeshDiameter);
        }

        if (Time.frameCount % 300 == 1)
        {
            Debug.Log($"[ChainConnectorView3D Diagnostic] Name={name}, WorldPos={transform.position}, WorldScale={transform.lossyScale}, Enabled={(meshRenderer != null && meshRenderer.enabled)}, Mat={(meshRenderer != null && meshRenderer.sharedMaterial != null ? meshRenderer.sharedMaterial.name : "null")}, Bounds={(meshRenderer != null ? meshRenderer.bounds.ToString() : "null")}");
        }
    }

    private void EnsureMesh()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        if (meshFilter == null)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        if (meshRenderer == null)
        {
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        if (meshFilter.sharedMesh == null)
        {
            meshFilter.sharedMesh = GetBridgeMesh();
        }

        Collider collider = GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }
    }

    private static Mesh sharedBridgeMesh;

    private static Mesh GetBridgeMesh()
    {
        if (sharedBridgeMesh != null)
        {
            return sharedBridgeMesh;
        }

        // Purpose-built 3D bridge prism with flat end faces (Y = -0.5 to Y = +0.5)
        // and rounded/beveled longitudinal edges along the chain direction.
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        float halfW = 0.5f;
        float bevel = 0.14f;
        float innerW = halfW - bevel;

        // 8-point chamfered cross-section in XZ (unit size 1.0 x 1.0)
        Vector2[] section = new Vector2[]
        {
            new Vector2( innerW,  halfW),
            new Vector2( halfW,   innerW),
            new Vector2( halfW,  -innerW),
            new Vector2( innerW, -halfW),
            new Vector2(-innerW, -halfW),
            new Vector2(-halfW,  -innerW),
            new Vector2(-halfW,   innerW),
            new Vector2(-innerW,  halfW),
        };

        int N = section.Length;
        float y0 = -0.5f;
        float y1 = 0.5f;

        // 1. Flat End Cap at Y = -0.5 (Facing -Y)
        int startBottom = verts.Count;
        for (int i = 0; i < N; i++)
        {
            verts.Add(new Vector3(section[i].x, y0, section[i].y));
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(section[i].x + 0.5f, section[i].y + 0.5f));
        }
        for (int i = 1; i < N - 1; i++)
        {
            tris.Add(startBottom);
            tris.Add(startBottom + i + 1);
            tris.Add(startBottom + i);
        }

        // 2. Flat End Cap at Y = +0.5 (Facing +Y)
        int startTop = verts.Count;
        for (int i = 0; i < N; i++)
        {
            verts.Add(new Vector3(section[i].x, y1, section[i].y));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(section[i].x + 0.5f, section[i].y + 0.5f));
        }
        for (int i = 1; i < N - 1; i++)
        {
            tris.Add(startTop);
            tris.Add(startTop + i);
            tris.Add(startTop + i + 1);
        }

        // 3. Side faces connecting Y = -0.5 to Y = +0.5
        for (int i = 0; i < N; i++)
        {
            int next = (i + 1) % N;
            Vector2 p0 = section[i];
            Vector2 p1 = section[next];

            Vector3 n0 = new Vector3(p0.x, 0f, p0.y).normalized;
            Vector3 n1 = new Vector3(p1.x, 0f, p1.y).normalized;

            int vIdx = verts.Count;
            verts.Add(new Vector3(p0.x, y0, p0.y));
            verts.Add(new Vector3(p1.x, y0, p1.y));
            verts.Add(new Vector3(p1.x, y1, p1.y));
            verts.Add(new Vector3(p0.x, y1, p0.y));

            normals.Add(n0);
            normals.Add(n1);
            normals.Add(n1);
            normals.Add(n0);

            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.right);
            uvs.Add(Vector2.one);
            uvs.Add(Vector2.up);

            tris.Add(vIdx);
            tris.Add(vIdx + 3);
            tris.Add(vIdx + 2);
            tris.Add(vIdx);
            tris.Add(vIdx + 2);
            tris.Add(vIdx + 1);
        }

        sharedBridgeMesh = new Mesh
        {
            name = "ProceduralBridgeMesh3D"
        };
        sharedBridgeMesh.SetVertices(verts);
        sharedBridgeMesh.SetNormals(normals);
        sharedBridgeMesh.SetUVs(0, uvs);
        sharedBridgeMesh.SetTriangles(tris, 0);
        sharedBridgeMesh.RecalculateBounds();
        return sharedBridgeMesh;
    }
}
