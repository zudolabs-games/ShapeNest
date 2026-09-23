using UnityEngine;

/// <summary>
/// Frames the World3D board camera. Single authority for gameplay camera composition.
/// Phase 1: perspective pitch/FOV/distance/target only — does not alter board world scale.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class BoardCamera3D : MonoBehaviour
{
    [SerializeField]
    private Camera cachedCamera;

    [SerializeField]
    [Range(20f, 75f)]
    private float fieldOfView = 36f;

    [SerializeField]
    [Tooltip("Pitch above the board in degrees (0 = horizontal, 90 = top-down).")]
    [Range(15f, 90f)]
    private float lookPitch = 54f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Scales framing distance. 1 = fit fill targets; >1 pulls back.")]
    private float distanceMultiplier = 1.0f;

    [SerializeField]
    [Min(0.3f)]
    [Tooltip("Legacy span factor; Phase 14 prefers Gameplay Area framing.")]
    private float orthographicSpanFactor = 0.85f;

    [SerializeField]
    [Range(0.4f, 0.98f)]
    [Tooltip("Target board height as a fraction of the Gameplay Area (or full Game view fallback).")]
    private float targetVerticalFill = 0.88f;

    [SerializeField]
    [Range(0.55f, 0.98f)]
    [Tooltip("Target board width as a fraction of the Gameplay Area (or full Game view fallback).")]
    private float targetHorizontalFill = 0.92f;

    [SerializeField]
    [Tooltip("Look-at bias in world space. Under default pitch, -Z raises the board on screen; +Z lowers it.")]
    private Vector3 lookOffset = new Vector3(0f, 0f, 0f);

    [SerializeField]
    private bool useOrthographic = false;

    private float lastFramedAspect = -1f;
    private Vector2 lastGameplayScreenSize = new Vector2(-1f, -1f);
    private Vector2 lastBoardFootprint = new Vector2(-1f, -1f);

    private void Awake()
    {
        CacheCamera();
    }

    private void OnValidate()
    {
        CacheCamera();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        float aspect = ResolveRenderAspect();
        BoardPresenter3D board = FindFirstObjectByType<BoardPresenter3D>();
        RectTransform area = BoardAdaptivePresentation3D.FindGameplayArea();
        Vector2 footprint = board != null ? board.BoardFootprint : Vector2.zero;
        Vector2 gpSize = Vector2.zero;
        Rect screenRect = default;
        if (area != null && BoardAdaptivePresentation3D.TryGetScreenRect(area, out screenRect))
        {
            gpSize = screenRect.size;
        }

        bool dirty = Mathf.Abs(aspect - lastFramedAspect) >= 0.005f
            || (gpSize - lastGameplayScreenSize).sqrMagnitude > 1f
            || (footprint - lastBoardFootprint).sqrMagnitude > 0.0001f;

        if (!dirty || board == null)
        {
            return;
        }

        FrameBoard(board, area);
    }

    public Camera Camera
    {
        get
        {
            CacheCamera();
            return cachedCamera;
        }
    }

    /// <summary>
    /// Ensures ortho + pitch so Gameplay Area → world measurement is valid.
    /// Uses a fixed orthographic size so measurement is not polluted by prior framing.
    /// </summary>
    public void PrepareMeasurementPose()
    {
        CacheCamera();
        if (cachedCamera == null)
        {
            return;
        }

        ApplyArtDirectionDefaults();
        cachedCamera.orthographic = true;
        // Fixed reference size: available Gameplay Area world units stay stable across levels.
        cachedCamera.orthographicSize = BoardAdaptivePresentation3D.MeasurementOrthoSize;

        Quaternion rotation = Quaternion.Euler(lookPitch, 0f, 0f);
        transform.rotation = rotation;
        transform.position = new Vector3(0f, 8f, 0f);

        cachedCamera.nearClipPlane = 0.05f;
        cachedCamera.farClipPlane = 100f;
    }

    public void FrameBoard(BoardPresenter3D board)
    {
        FrameBoard(board, BoardAdaptivePresentation3D.FindGameplayArea());
    }

    public void FrameBoard(BoardPresenter3D board, RectTransform gameplayArea)
    {
        CacheCamera();
        if (cachedCamera == null || board == null)
        {
            return;
        }

        ApplyArtDirectionDefaults();

        cachedCamera.nearClipPlane = 0.05f;
        cachedCamera.farClipPlane = 100f;

        Vector2 footprint = board.BoardFootprint;
        float span = Mathf.Max(footprint.x, footprint.y, 1f);
        Vector3 target = board.BoardCenterWorld + lookOffset;
        float aspect = ResolveRenderAspect();

        if (useOrthographic)
        {
            cachedCamera.orthographic = true;

            // Project board depth and physical thickness onto camera image plane:
            // The camera looks down at pitch theta (e.g. 72°), so board Z and block/board Y project vertically.
            float pitchRad = lookPitch * Mathf.Deg2Rad;
            float sinPitch = Mathf.Sin(pitchRad);
            float cosPitch = Mathf.Cos(pitchRad);
            float totalThickness = board.CellWorldSize * (BoardAdaptivePresentation3D.ThicknessRatio + BoardAdaptivePresentation3D.BlockHeightRatio);
            float projectedExtY = (footprint.y * sinPitch) + (totalThickness * cosPitch);

            Rect gpScreen = default;
            bool hasGameplayRect = gameplayArea != null
                && BoardAdaptivePresentation3D.TryGetScreenRect(gameplayArea, out gpScreen)
                && gpScreen.height > 2f
                && gpScreen.width > 2f;
            if (hasGameplayRect)
            {
                float screenH = Mathf.Max(1f, ResolveScreenHeight());
                float screenW = Mathf.Max(1f, ResolveScreenWidth());
                float gpFracH = gpScreen.height / screenH;
                float gpFracW = gpScreen.width / screenW;
                float sizeByHeight = (projectedExtY * 0.5f) / Mathf.Max(0.05f, gpFracH * targetVerticalFill);
                float sizeByWidth = (footprint.x * 0.5f) / (aspect * Mathf.Max(0.05f, gpFracW * targetHorizontalFill));
                cachedCamera.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth) * distanceMultiplier;
                lastGameplayScreenSize = gpScreen.size;
            }
            else
            {
                float sizeByHeight = (projectedExtY * 0.5f) / Mathf.Max(0.05f, targetVerticalFill);
                float sizeByWidth = (footprint.x * 0.5f) / (aspect * Mathf.Max(0.05f, targetHorizontalFill));
                cachedCamera.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth) * distanceMultiplier;
                lastGameplayScreenSize = new Vector2(-1f, -1f);
            }

            Quaternion rotation = Quaternion.Euler(lookPitch, 0f, 0f);
            float distance = span * Mathf.Max(0.1f, distanceMultiplier);
            Vector3 forwardDir = rotation * Vector3.forward;
            transform.position = target - forwardDir * distance;
            transform.rotation = rotation;
            lastFramedAspect = aspect;
            lastBoardFootprint = footprint;
        }
        else
        {
            cachedCamera.orthographic = false;
            cachedCamera.fieldOfView = fieldOfView;

            float pitchRad = lookPitch * Mathf.Deg2Rad;
            float sinPitch = Mathf.Sin(pitchRad);
            float cosPitch = Mathf.Cos(pitchRad);

            float totalThickness = board.CellWorldSize * (BoardAdaptivePresentation3D.ThicknessRatio + BoardAdaptivePresentation3D.BlockHeightRatio);
            float projectedExtY = (footprint.y * sinPitch) + (totalThickness * cosPitch);
            float projectedExtX = footprint.x;

            float halfFovRad = fieldOfView * 0.5f * Mathf.Deg2Rad;
            float tanHalfFov = Mathf.Tan(halfFovRad);

            Rect gpScreen = default;
            bool hasGameplayRect = gameplayArea != null
                && BoardAdaptivePresentation3D.TryGetScreenRect(gameplayArea, out gpScreen)
                && gpScreen.height > 2f
                && gpScreen.width > 2f;

            float fillH = targetVerticalFill;
            float fillW = targetHorizontalFill;
            if (hasGameplayRect)
            {
                float screenH = Mathf.Max(1f, ResolveScreenHeight());
                float screenW = Mathf.Max(1f, ResolveScreenWidth());
                fillH *= (gpScreen.height / screenH);
                fillW *= (gpScreen.width / screenW);
            }

            float distByHeight = (projectedExtY * 0.5f) / Mathf.Max(0.01f, tanHalfFov * fillH);
            float distByWidth = (projectedExtX * 0.5f) / Mathf.Max(0.01f, aspect * tanHalfFov * fillW);

            float requiredDist = Mathf.Max(distByHeight, distByWidth) * distanceMultiplier;
            Quaternion rotation = Quaternion.Euler(lookPitch, 0f, 0f);
            Vector3 forwardDir = rotation * Vector3.forward;
            transform.position = target - forwardDir * requiredDist;
            transform.rotation = rotation;
            lastFramedAspect = aspect;
            lastBoardFootprint = footprint;
        }
    }

    /// <summary>
    /// Reference composition defaults: Perspective mode with ~54° pitch camera showing top faces
    /// clearly while retaining rich 3D block depth and visible front walls.
    /// Matches the reference game where blocks read as chunky solid plastic toys in perspective.
    /// </summary>
    public void ApplyArtDirectionDefaults()
    {
        useOrthographic = false;
        lookPitch = 54f;
        distanceMultiplier = 1.0f;
        orthographicSpanFactor = 0.85f;
        targetVerticalFill = 0.88f;
        targetHorizontalFill = 0.92f;
        lookOffset = new Vector3(0f, 0f, 0f);
        fieldOfView = 36f;
    }

    /// <summary>
    /// Aspect of the camera that actually renders gameplay. Prefer pixelRect over Screen
    /// because Game View scale can make Screen.width/height diverge from camera pixels.
    /// </summary>
    private float ResolveRenderAspect()
    {
        CacheCamera();
        if (cachedCamera != null && cachedCamera.targetTexture != null && cachedCamera.targetTexture.height > 1)
        {
            return (float)cachedCamera.targetTexture.width / cachedCamera.targetTexture.height;
        }

        if (cachedCamera != null && cachedCamera.pixelHeight > 1)
        {
            return Mathf.Clamp(cachedCamera.aspect, 0.45f, 2f);
        }

        float screenAspect = (float)Screen.width / Mathf.Max(1f, Screen.height);
        return Mathf.Clamp(screenAspect, 0.45f, 2f);
    }

    private float ResolveScreenWidth()
    {
        CacheCamera();
        if (cachedCamera != null && cachedCamera.targetTexture != null)
        {
            return cachedCamera.targetTexture.width;
        }
        return Mathf.Max(1f, Screen.width);
    }

    private float ResolveScreenHeight()
    {
        CacheCamera();
        if (cachedCamera != null && cachedCamera.targetTexture != null)
        {
            return cachedCamera.targetTexture.height;
        }
        return Mathf.Max(1f, Screen.height);
    }

    private void CacheCamera()
    {
        if (cachedCamera == null)
        {
            cachedCamera = GetComponent<Camera>();
        }
    }
}
