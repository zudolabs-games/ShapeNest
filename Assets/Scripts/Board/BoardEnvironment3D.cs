using UnityEngine;

/// <summary>
/// World3D backdrop for Phase 13 dark/saturated art direction.
/// Presentation only — not Canvas UI.
/// </summary>
[DisallowMultipleComponent]
public class BoardEnvironment3D : MonoBehaviour
{
    [SerializeField]
    private Transform backdropPlane;

    [SerializeField]
    private Camera targetCamera;

    [SerializeField]
    private Color clearColor = new Color(0.165f, 0.120f, 0.430f, 1f);

    [SerializeField]
    private Color ambientColor = new Color(0.320f, 0.280f, 0.520f, 1f);

    private static Material sharedBackdropMaterial;

    public void Apply(BoardPresenter3D board, Camera camera)
    {
        targetCamera = camera;

        // Reference video background: saturated clean indigo-purple #2A1F6E.
        clearColor = new Color(0.165f, 0.120f, 0.430f, 1f);
        ambientColor = new Color(0.320f, 0.280f, 0.520f, 1f);

        if (targetCamera != null)
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = clearColor;
        }

        // Clean up any legacy fake shadow quads, discs, or floors.
        CleanupLegacyShadowObjects();

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;
        RenderSettings.ambientIntensity = 1.0f;
        RenderSettings.reflectionIntensity = 0.85f;
    }

    private void CleanupLegacyShadowObjects()
    {
        string[] legacyNames = { "SoftFloor", "BoardContactShadow", "BoardFloor", "Floor", "BoardBackdropPlane" };
        for (int i = 0; i < legacyNames.Length; i++)
        {
            Transform t = transform.Find(legacyNames[i]);
            if (t != null)
            {
                if (Application.isPlaying) Destroy(t.gameObject);
                else DestroyImmediate(t.gameObject);
            }
        }
    }
}
