using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives GameplayCanvas/Parent/Overlay - Image for Hammer targeting.
/// Dims the scene with the existing overlay and punches screen-space holes
/// so eligible 3D PieceView3D meshes and HammerButton keep their original look.
/// Does not tint block materials, move hierarchy, or create canvases/cameras.
/// </summary>
[DisallowMultipleComponent]
public class BoosterSelectionOverlay : MonoBehaviour
{
    public const string OverlayObjectName = "Overlay - Image";
    public const string SpotlightShaderName = "UI/HammerSpotlightOverlay";

    private const int MaxHoles = 24;
    private const float HolePadPixels = 10f;
    private const float HolePadFraction = 0.08f;

    private static readonly int HoleCountId = Shader.PropertyToID("_SpotlightHoleCount");
    private static readonly int HolesId = Shader.PropertyToID("_SpotlightHoles");
    private static readonly int OverlayRectId = Shader.PropertyToID("_OverlayRect");

    private static Sprite whiteSprite;

    [SerializeField]
    private Image overlayImage;

    private bool wantVisible;
    private Material spotlightMaterial;
    private readonly Vector4[] holes = new Vector4[MaxHoles];
    private RectTransform hammerButtonRect;
    private Camera boardCamera;

    public bool IsVisible => wantVisible && gameObject.activeSelf;

    public static BoosterSelectionOverlay Ensure()
    {
        Transform overlayTransform = FindOverlayTransform();
        if (overlayTransform == null)
        {
            Debug.LogWarning("BoosterSelectionOverlay: GameplayCanvas/Parent/Overlay - Image not found.");
            return null;
        }

        BoosterSelectionOverlay overlay = overlayTransform.GetComponent<BoosterSelectionOverlay>();
        if (overlay == null)
        {
            overlay = overlayTransform.gameObject.AddComponent<BoosterSelectionOverlay>();
        }

        overlay.CacheImage();
        return overlay;
    }

    public static BoosterSelectionOverlay FindExisting()
    {
        Transform overlayTransform = FindOverlayTransform();
        if (overlayTransform != null)
        {
            return overlayTransform.GetComponent<BoosterSelectionOverlay>();
        }

        return Object.FindFirstObjectByType<BoosterSelectionOverlay>(FindObjectsInactive.Include);
    }

    public static void HideExisting()
    {
        BoosterSelectionOverlay existing = FindExisting();
        if (existing != null)
        {
            existing.SetVisible(false);
            return;
        }

        Transform overlayTransform = FindOverlayTransform();
        if (overlayTransform != null && overlayTransform.gameObject.activeSelf)
        {
            overlayTransform.gameObject.SetActive(false);
        }
    }

    public void SetVisible(bool visible)
    {
        CacheImage();
        wantVisible = visible;
        if (!visible)
        {
            ClearHoles();
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            return;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        ConfigureExistingImage();
        ApplySpotlightHoles();
    }

    private void LateUpdate()
    {
        if (!wantVisible || !gameObject.activeSelf)
        {
            return;
        }

        ApplySpotlightHoles();
    }

    private void OnDisable()
    {
        if (!wantVisible)
        {
            ClearHoles();
        }
    }

    private void OnDestroy()
    {
        if (spotlightMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(spotlightMaterial);
            }
            else
            {
                DestroyImmediate(spotlightMaterial);
            }

            spotlightMaterial = null;
        }
    }

    private void CacheImage()
    {
        if (overlayImage == null)
        {
            overlayImage = GetComponent<Image>();
        }
    }

    private void ConfigureExistingImage()
    {
        if (overlayImage == null)
        {
            return;
        }

        overlayImage.raycastTarget = false;
        overlayImage.maskable = false;
        if (overlayImage.sprite == null)
        {
            overlayImage.sprite = ResolveWhiteSprite();
        }

        EnsureSpotlightMaterial();
        if (spotlightMaterial != null)
        {
            overlayImage.material = spotlightMaterial;
        }
    }

    private void EnsureSpotlightMaterial()
    {
        if (spotlightMaterial != null)
        {
            return;
        }

        Shader shader = Shader.Find(SpotlightShaderName);
        if (shader == null)
        {
            Debug.LogWarning("BoosterSelectionOverlay: shader " + SpotlightShaderName + " not found.");
            return;
        }

        spotlightMaterial = new Material(shader);
        spotlightMaterial.name = "HammerSpotlightOverlay";
        spotlightMaterial.hideFlags = HideFlags.HideAndDontSave;
    }

    private void ApplySpotlightHoles()
    {
        EnsureSpotlightMaterial();
        if (spotlightMaterial == null)
        {
            return;
        }

        int count = 0;
        AddRectHole(ResolveHammerButtonRect(), ref count);

        HammerBooster hammer = Object.FindFirstObjectByType<HammerBooster>(FindObjectsInactive.Exclude);
        if (hammer != null && hammer.IsBusy)
        {
            Camera cam = ResolveBoardCamera();
            PieceView3D[] pieces = Object.FindObjectsByType<PieceView3D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int i;
            for (i = 0; i < pieces.Length; i++)
            {
                PieceView3D view = pieces[i];
                if (view == null || !hammer.IsHammerEligibleVisual(view))
                {
                    continue;
                }

                AddPieceHoles(view, cam, ref count);
            }
        }

        int h;
        for (h = count; h < MaxHoles; h++)
        {
            holes[h] = Vector4.zero;
        }

        Rect overlayRect = overlayImage != null
            ? overlayImage.rectTransform.rect
            : ((RectTransform)transform).rect;
        spotlightMaterial.SetVector(OverlayRectId, new Vector4(overlayRect.xMin, overlayRect.yMin, overlayRect.xMax, overlayRect.yMax));
        spotlightMaterial.SetFloat(HoleCountId, count);
        spotlightMaterial.SetVectorArray(HolesId, holes);
        if (overlayImage != null)
        {
            overlayImage.SetMaterialDirty();
        }
    }

    private void AddPieceHoles(PieceView3D view, Camera cam, ref int count)
    {
        MeshRenderer[] renderers = view.GetComponentsInChildren<MeshRenderer>(false);
        int i;
        for (i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (TryBoundsToHole(renderer.bounds, cam, out Vector4 hole))
            {
                AddHole(hole, ref count);
            }
        }
    }

    private void AddRectHole(RectTransform rect, ref int count)
    {
        if (rect == null || !rect.gameObject.activeInHierarchy)
        {
            return;
        }

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float minU = float.PositiveInfinity;
        float minV = float.PositiveInfinity;
        float maxU = float.NegativeInfinity;
        float maxV = float.NegativeInfinity;
        int i;
        for (i = 0; i < 4; i++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
            if (!TryScreenToOverlayUv(screen, out Vector2 uv))
            {
                return;
            }

            minU = Mathf.Min(minU, uv.x);
            minV = Mathf.Min(minV, uv.y);
            maxU = Mathf.Max(maxU, uv.x);
            maxV = Mathf.Max(maxV, uv.y);
        }

        AddUvHole(minU, minV, maxU, maxV, ref count);
    }

    private bool TryBoundsToHole(Bounds bounds, Camera cam, out Vector4 hole)
    {
        hole = Vector4.zero;
        if (cam == null)
        {
            return false;
        }

        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;
        float minU = float.PositiveInfinity;
        float minV = float.PositiveInfinity;
        float maxU = float.NegativeInfinity;
        float maxV = float.NegativeInfinity;
        int i;
        int valid = 0;
        for (i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);
            Vector3 camScreen = cam.WorldToScreenPoint(corner);
            if (camScreen.z <= 0f)
            {
                continue;
            }

            Vector2 screen = new Vector2(camScreen.x, camScreen.y);
            if (!TryScreenToOverlayUv(screen, out Vector2 uv))
            {
                continue;
            }

            minU = Mathf.Min(minU, uv.x);
            minV = Mathf.Min(minV, uv.y);
            maxU = Mathf.Max(maxU, uv.x);
            maxV = Mathf.Max(maxV, uv.y);
            valid++;
        }

        if (valid == 0 || minU > maxU)
        {
            return false;
        }

        hole = PaddedUvHole(minU, minV, maxU, maxV);
        return true;
    }

    private bool TryScreenToOverlayUv(Vector2 screenPoint, out Vector2 uv)
    {
        uv = Vector2.zero;
        RectTransform overlayRect = overlayImage != null
            ? overlayImage.rectTransform
            : transform as RectTransform;
        if (overlayRect == null)
        {
            return false;
        }

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRect, screenPoint, null, out local))
        {
            return false;
        }

        Rect rect = overlayRect.rect;
        if (rect.width < 0.01f || rect.height < 0.01f)
        {
            return false;
        }

        uv = new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
        return true;
    }

    private void AddUvHole(float minU, float minV, float maxU, float maxV, ref int count)
    {
        AddHole(PaddedUvHole(minU, minV, maxU, maxV), ref count);
    }

    private static Vector4 PaddedUvHole(float minU, float minV, float maxU, float maxV)
    {
        float padU = HolePadPixels / Mathf.Max(Screen.width, 1) + (maxU - minU) * HolePadFraction;
        float padV = HolePadPixels / Mathf.Max(Screen.height, 1) + (maxV - minV) * HolePadFraction;
        return new Vector4(minU - padU, minV - padV, maxU + padU, maxV + padV);
    }

    private void AddHole(Vector4 hole, ref int count)
    {
        if (count >= MaxHoles)
        {
            return;
        }

        holes[count] = hole;
        count++;
    }

    private void ClearHoles()
    {
        if (spotlightMaterial == null)
        {
            return;
        }

        int i;
        for (i = 0; i < MaxHoles; i++)
        {
            holes[i] = Vector4.zero;
        }

        spotlightMaterial.SetFloat(HoleCountId, 0f);
        spotlightMaterial.SetVectorArray(HolesId, holes);
    }

    private RectTransform ResolveHammerButtonRect()
    {
        if (hammerButtonRect != null)
        {
            return hammerButtonRect;
        }

        Transform parent = transform.parent;
        if (parent == null)
        {
            return null;
        }

        Transform button = parent.Find("BoostersContainer/HammerButton");
        if (button != null)
        {
            hammerButtonRect = button as RectTransform;
        }

        return hammerButtonRect;
    }

    private Camera ResolveBoardCamera()
    {
        if (boardCamera != null && boardCamera.isActiveAndEnabled)
        {
            return boardCamera;
        }

        BoardCamera3D boardCam = Object.FindFirstObjectByType<BoardCamera3D>(FindObjectsInactive.Exclude);
        if (boardCam != null)
        {
            boardCamera = boardCam.Camera;
        }

        if (boardCamera == null)
        {
            boardCamera = Camera.main;
        }

        return boardCamera;
    }

    private static Transform FindOverlayTransform()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int i;
        for (i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas.name != "GameplayCanvas")
            {
                continue;
            }

            Transform parent = canvas.transform.Find("Parent");
            if (parent == null)
            {
                continue;
            }

            Transform overlay = parent.Find(OverlayObjectName);
            if (overlay != null)
            {
                return overlay;
            }
        }

        return null;
    }

    private static Sprite ResolveWhiteSprite()
    {
        if (whiteSprite != null)
        {
            return whiteSprite;
        }

        Texture2D tex = Texture2D.whiteTexture;
        whiteSprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            4f,
            0,
            SpriteMeshType.FullRect);
        whiteSprite.name = "OverlayImageWhite";
        return whiteSprite;
    }
}
