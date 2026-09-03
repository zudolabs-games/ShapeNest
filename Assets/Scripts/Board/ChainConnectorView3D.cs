using UnityEngine;

/// <summary>
/// Presentation-only 3D bar between two 4-connected chain cells.
/// Mirrors <see cref="PieceGameplayVisuals"/> ChainLink Images; not a gameplay object.
/// Phase 52A: rounded capsule rod oriented along existing endpoint positions with
/// block-height cross section so it reads as physical 3D geometry from BoardCamera3D.
/// </summary>
[DisallowMultipleComponent]
public class ChainConnectorView3D : MonoBehaviour
{
    private const float UnitCapsuleHeight = 2f;
    private const float UnitCapsuleDiameter = 1f;

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
    /// Places and orients the rod between existing chain endpoints. Does not alter endpoint logic.
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
            // Capsule Y→world X: local X→world Y (height), local Z→world Z (thickness).
            transform.localScale = new Vector3(
                crossHeight / UnitCapsuleDiameter,
                length / UnitCapsuleHeight,
                crossThickness / UnitCapsuleDiameter);
        }
        else
        {
            // Capsule Y→world Z: local Z→world Y (height), local X→world X (thickness).
            transform.localScale = new Vector3(
                crossThickness / UnitCapsuleDiameter,
                length / UnitCapsuleHeight,
                crossHeight / UnitCapsuleDiameter);
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
            meshFilter.sharedMesh = SharedCapsule();
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

    private static Mesh sharedCapsule;

    private static Mesh SharedCapsule()
    {
        if (sharedCapsule != null)
        {
            return sharedCapsule;
        }

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Mesh source = temp.GetComponent<MeshFilter>().sharedMesh;
        sharedCapsule = source;
        if (Application.isPlaying)
        {
            Destroy(temp);
        }
        else
        {
            DestroyImmediate(temp);
        }

        return sharedCapsule;
    }
}
