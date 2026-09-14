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
    private float fieldOfView = 44f;

    [SerializeField]
    [Tooltip("Pitch above the board in degrees (0 = horizontal, 90 = top-down).")]
    [Range(25f, 90f)]
    private float lookPitch = 63f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Scales framing distance. 1 = fit fill targets; >1 pulls back.")]
    private float distanceMultiplier = 1.08f;

    [SerializeField]
    [Min(0.3f)]
    [Tooltip("Legacy span factor; Phase 14 prefers Gameplay Area framing.")]
    private float orthographicSpanFactor = 0.85f;

    [SerializeField]
    [Range(0.4f, 0.98f)]
    [Tooltip("Target board height as a fraction of the Gameplay Area (or full Game view fallback).")]
    private float targetVerticalFill = 0.92f;

    [SerializeField]
    [Range(0.55f, 0.98f)]
    [Tooltip("Target board width as a fraction of the Gameplay Area (or full Game view fallback).")]
    private float targetHorizontalFill = 0.90f;

    [SerializeField]
    [Tooltip("Look-at bias in world space. Under default pitch, -Z raises the board on screen; +Z lowers it.")]
    private Vector3 lookOffset = new Vector3(0f, 0.03f, -0.10f);

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
        transform.position = new Vector3(0f, 8f, -0.3f);

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
                float sizeByHeight = (footprint.y * 0.5f) / Mathf.Max(0.05f, gpFracH * targetVerticalFill);
                float sizeByWidth = (footprint.x * 0.5f) / (aspect * Mathf.Max(0.05f, gpFracW * targetHorizontalFill));
                cachedCamera.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth) * distanceMultiplier;
                lastGameplayScreenSize = gpScreen.size;
            }
            else
            {
                float halfSpan = span * 0.5f;
                float sizeByHeight = halfSpan / Mathf.Max(0.05f, targetVerticalFill);
                float sizeByWidth = halfSpan / (aspect * Mathf.Max(0.05f, targetHorizontalFill));
                cachedCamera.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth) * distanceMultiplier;
                lastGameplayScreenSize = new Vector2(-1f, -1f);
            }

            Quaternion rotation = Quaternion.Euler(lookPitch, 0f, 0f);
            float distance = span * Mathf.Max(0.1f, distanceMultiplier);
            Vector3 offset = rotation * new Vector3(0f, 0f, -distance);
            transform.position = target + offset;
            transform.rotation = Quaternion.LookRotation(target - transform.position, Vector3.up);
            lastFramedAspect = aspect;
            lastBoardFootprint = footprint;
        }
        else
        {
            cachedCamera.orthographic = false;
            cachedCamera.fieldOfView = fieldOfView;

            Rect gpScreen = default;
            bool hasGameplayRect = gameplayArea != null
                && BoardAdaptivePresentation3D.TryGetScreenRect(gameplayArea, out gpScreen)
                && gpScreen.height > 2f
                && gpScreen.width > 2f;

            float distByWidth;
            float distByHeight;

            float pitchRad = lookPitch * Mathf.Deg2Rad;
            float halfFovRad = fieldOfView * 0.5f * Mathf.Deg2Rad;
            float tanHalfFov = Mathf.Tan(halfFovRad);

            float blockHeight = board.CellWorldSize * BoardAdaptivePresentation3D.BlockHeightRatio;
            float projectedExtY = (footprint.y * Mathf.Sin(pitchRad) + blockHeight * Mathf.Cos(pitchRad)) * 0.5f;
            float projectedExtX = footprint.x * 0.5f;

            float fillH = Mathf.Clamp(targetVerticalFill, 0.4f, 0.98f);
            float fillW = Mathf.Clamp(targetHorizontalFill, 0.55f, 0.98f);

            if (hasGameplayRect)
            {
                float screenH = Mathf.Max(1f, ResolveScreenHeight());
                float screenW = Mathf.Max(1f, ResolveScreenWidth());
                // Gameplay Area fractions are Screen-normalized; camera aspect comes from pixelRect.
                float gpFracH = gpScreen.height / screenH;
                float gpFracW = gpScreen.width / screenW;

                float effectiveTanH = tanHalfFov * gpFracH * fillH;
                float effectiveTanW = aspect * tanHalfFov * gpFracW * fillW;

                distByHeight = projectedExtY / Mathf.Max(0.01f, effectiveTanH);
                distByWidth = projectedExtX / Mathf.Max(0.01f, effectiveTanW);
                lastGameplayScreenSize = gpScreen.size;
            }
            else
            {
                float effectiveTanH = tanHalfFov * fillH;
                float effectiveTanW = aspect * tanHalfFov * fillW;

                distByHeight = projectedExtY / Mathf.Max(0.01f, effectiveTanH);
                distByWidth = projectedExtX / Mathf.Max(0.01f, effectiveTanW);
                lastGameplayScreenSize = new Vector2(-1f, -1f);
            }

            float requiredDist = Mathf.Max(distByHeight, distByWidth) * distanceMultiplier;
            Quaternion rotation = Quaternion.Euler(lookPitch, 0f, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -requiredDist);
            transform.position = target + offset;
            transform.rotation = Quaternion.LookRotation(target - transform.position, Vector3.up);
            lastFramedAspect = aspect;
            lastBoardFootprint = footprint;
        }
    }

    /// <summary>
    /// Phase 1 composition defaults matching the reference camera: perspective, moderate FOV,
    /// toy-tray pitch, intentional Gameplay Area margins, slight downward board bias.
    /// </summary>
    public void ApplyArtDirectionDefaults()
    {
        useOrthographic = false;
        lookPitch = 63f;
        distanceMultiplier = 1.08f;
        orthographicSpanFactor = 0.85f;
        targetVerticalFill = 0.92f;
        targetHorizontalFill = 0.90f;
        lookOffset = new Vector3(0f, 0.03f, -0.10f);
        fieldOfView = 44f;
    }

    /// <summary>
    /// Aspect of the camera that actually renders gameplay. Prefer pixelRect over Screen
    /// because Game View scale can make Screen.width/height diverge from camera pixels.
    /// </summary>
    private float ResolveRenderAspect()
    {
        CacheCamera();
        if (cachedCamera != null && cachedCamera.pixelHeight > 1)
        {
            return Mathf.Clamp(cachedCamera.aspect, 0.45f, 2f);
        }

        float screenAspect = (float)Screen.width / Mathf.Max(1f, Screen.height);
        return Mathf.Clamp(screenAspect, 0.45f, 2f);
    }

    private float ResolveScreenWidth()
    {
        return Mathf.Max(1f, Screen.width);
    }

    private float ResolveScreenHeight()
    {
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
