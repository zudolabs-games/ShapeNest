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

        EnsureKeyLight();
    }

    private void EnsureKeyLight()
    {
        Light keyLight = FindFirstObjectByType<Light>();
        if (keyLight == null || keyLight.type != LightType.Directional)
        {
            GameObject lightGo = new GameObject("BoardKeyLight3D");
            lightGo.transform.SetParent(transform, false);
            keyLight = lightGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
        }

        keyLight.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
        keyLight.color = new Color(1.00f, 0.96f, 0.92f, 1f);
        keyLight.intensity = 1.25f;
        keyLight.shadows = LightShadows.Soft;
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
