using UnityEngine;

/// <summary>
/// Presentation-only World3D bar between two 4-connected chain cells.
/// Mirrors <see cref="PieceGameplayVisuals"/> ChainLink Images; not a gameplay object.
/// </summary>
[DisallowMultipleComponent]
public class ChainConnectorView3D : MonoBehaviour
{
    private Vector3 restScale = Vector3.one;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    public Vector3 RestScale => restScale;

    public void Configure(Vector3 scale, Material material)
    {
        EnsureMesh();
        restScale = scale;
        transform.localScale = scale;
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
        }
    }

    public void Follow(Vector3 worldPosition, Vector3 scaleFactor)
    {
        transform.position = worldPosition;
        transform.localScale = new Vector3(
            restScale.x * scaleFactor.x,
            restScale.y * scaleFactor.y,
            restScale.z * scaleFactor.z);
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
            meshFilter.sharedMesh = ChainConnectorView3D.SharedCube();
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

    private static Mesh sharedCube;

    private static Mesh SharedCube()
    {
        if (sharedCube != null)
        {
            return sharedCube;
        }

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh source = temp.GetComponent<MeshFilter>().sharedMesh;
        sharedCube = source;
        if (Application.isPlaying)
        {
            Destroy(temp);
        }
        else
        {
            DestroyImmediate(temp);
        }

        return sharedCube;
    }
}
