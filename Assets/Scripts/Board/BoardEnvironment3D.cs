using UnityEngine;

/// <summary>
/// World3D backdrop for Phase 13 dark/saturated art direction.
/// Presentation only — not Canvas UI.
/// </summary>
[DisallowMultipleComponent]
public class BoardEnvironment3D : MonoBehaviour
{
    [SerializeField]
    private Transform floor;

    [SerializeField]
    private Transform underBoardShadow;

    [SerializeField]
    private Camera targetCamera;

    [SerializeField]
    private Color clearColor = new Color(0.137f, 0.098f, 0.392f, 1f);

    [SerializeField]
    private Color floorColor = new Color(0.12f, 0.09f, 0.34f, 1f);

    [SerializeField]
    private Color ambientColor = new Color(0.18f, 0.14f, 0.32f, 1f);

    private static Material sharedFloorMaterial;
    private static Material sharedShadowMaterial;

    public void Apply(BoardPresenter3D board, Camera camera)
    {
        targetCamera = camera;
        EnsureFloor();
        EnsureUnderShadow(board);

        // Deep indigo / purple — high contrast backdrop for bright pieces.
        clearColor = new Color(0.11f, 0.07f, 0.34f, 1f); // ~28,18,87
        floorColor = clearColor;
        ambientColor = new Color(0.12f, 0.09f, 0.22f, 1f);

        if (targetCamera != null)
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = clearColor;
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;
        RenderSettings.ambientIntensity = 1f;

        if (board != null)
        {
            Vector2 footprint = board.BoardFootprint;
            float span = Mathf.Max(footprint.x, footprint.y) * 10f;
            if (floor != null)
            {
                floor.position = new Vector3(board.transform.position.x, -0.06f, board.transform.position.z);
                floor.localScale = new Vector3(span, 1f, span);
                ApplyMatColor(floor.GetComponent<MeshRenderer>(), floorColor);
            }

            if (underBoardShadow != null)
            {
                float shadowSpan = Mathf.Max(footprint.x, footprint.y) * 1.05f;
                underBoardShadow.position = new Vector3(
                    board.BoardCenterWorld.x,
                    0.002f,
                    board.BoardCenterWorld.z + footprint.y * 0.04f);
                underBoardShadow.localScale = new Vector3(shadowSpan, 1f, shadowSpan * 0.88f);
                ApplyMatColor(underBoardShadow.GetComponent<MeshRenderer>(), new Color(0.01f, 0.005f, 0.04f, 0.72f));
            }
        }
    }

    private static void ApplyMatColor(MeshRenderer renderer, Color color)
    {
        if (renderer == null || renderer.sharedMaterial == null)
        {
            return;
        }

        renderer.sharedMaterial.color = color;
        if (renderer.sharedMaterial.HasProperty("_BaseColor"))
        {
            renderer.sharedMaterial.SetColor("_BaseColor", color);
        }
    }

    private void EnsureFloor()
    {
        if (floor == null)
        {
            Transform existing = transform.Find("SoftFloor");
            if (existing != null)
            {
                floor = existing;
            }
            else
            {
                var go = new GameObject("SoftFloor");
                go.transform.SetParent(transform, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = BoardMeshFactory3D.GetShadowDisc(48);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = GetFloorMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                floor = go.transform;
            }
        }
    }

    private void EnsureUnderShadow(BoardPresenter3D board)
    {
        if (underBoardShadow == null)
        {
            Transform existing = transform.Find("BoardContactShadow");
            if (existing != null)
            {
                underBoardShadow = existing;
            }
            else
            {
                var go = new GameObject("BoardContactShadow");
                go.transform.SetParent(transform, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = BoardMeshFactory3D.GetShadowDisc(40);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = GetShadowMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                underBoardShadow = go.transform;
            }
        }
    }

    private static Material GetFloorMaterial()
    {
        if (sharedFloorMaterial != null)
        {
            return sharedFloorMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        sharedFloorMaterial = new Material(shader)
        {
            name = "BoardFloor3D_Runtime",
            color = new Color(0.11f, 0.08f, 0.32f, 1f)
        };
        ApplyLit(sharedFloorMaterial, sharedFloorMaterial.color, 0f, 0.15f);
        return sharedFloorMaterial;
    }

    private static Material GetShadowMaterial()
    {
        if (sharedShadowMaterial != null)
        {
            return sharedShadowMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        sharedShadowMaterial = new Material(shader)
        {
            name = "BoardContactShadow3D_Runtime",
            color = new Color(0.02f, 0.01f, 0.06f, 0.55f)
        };
        ApplyLit(sharedShadowMaterial, sharedShadowMaterial.color, 0f, 0f);
        if (sharedShadowMaterial.HasProperty("_Surface"))
        {
            sharedShadowMaterial.SetFloat("_Surface", 1f);
            sharedShadowMaterial.SetOverrideTag("RenderType", "Transparent");
            sharedShadowMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            sharedShadowMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            sharedShadowMaterial.SetInt("_ZWrite", 0);
            sharedShadowMaterial.renderQueue = 3000;
            sharedShadowMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        return sharedShadowMaterial;
    }

    private static void ApplyLit(Material material, Color color, float metallic, float smoothness)
    {
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
    }
}
