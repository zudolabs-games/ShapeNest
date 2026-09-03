using UnityEngine;

/// <summary>
/// Frames the World3D board camera. Phase 51: slight pitch so 3D piece thickness is readable.
/// Phase 14: final framing centers the adaptively sized board inside Gameplay Area.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class BoardCamera3D : MonoBehaviour
{
    [SerializeField]
    private Camera cachedCamera;

    [SerializeField]
    [Range(20f, 60f)]
    private float fieldOfView = 32f;

    [SerializeField]
    [Tooltip("Pitch above the board in degrees (0 = horizontal, 90 = top-down).")]
    [Range(25f, 90f)]
    private float lookPitch = 66f;

    [SerializeField]
    [Min(0.1f)]
    private float distanceMultiplier = 1.2f;

    [SerializeField]
    [Min(0.3f)]
    [Tooltip("Legacy span factor; Phase 14 prefers Gameplay Area framing.")]
    private float orthographicSpanFactor = 0.85f;

    [SerializeField]
    [Range(0.4f, 0.75f)]
    [Tooltip("Fallback vertical fill of full Game view when Gameplay Area is unavailable.")]
    private float targetVerticalFill = 0.58f;

    [SerializeField]
    [Range(0.55f, 0.9f)]
    [Tooltip("Fallback horizontal fill of full Game view when Gameplay Area is unavailable.")]
    private float targetHorizontalFill = 0.80f;

    [SerializeField]
    private Vector3 lookOffset = new Vector3(0f, 0.15f, 0f);

    [SerializeField]
    private bool useOrthographic = true;

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
        if (!Application.isPlaying || !useOrthographic)
        {
            return;
        }

        float aspect = ResolvePortraitAspect();
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
        float aspect = ResolvePortraitAspect();

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
                float screenH = Mathf.Max(1f, Screen.height);
                float screenW = Mathf.Max(1f, Screen.width);
                float gpFracH = gpScreen.height / screenH;
                float gpFracW = gpScreen.width / screenW;
                float sizeByHeight = (footprint.y * 0.5f) / Mathf.Max(0.05f, gpFracH);
                float sizeByWidth = (footprint.x * 0.5f) / (aspect * Mathf.Max(0.05f, gpFracW));
                // Cell sizing already applied fit padding; tiny safety so chrome never clips.
                cachedCamera.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth) * 1.02f;
                lastGameplayScreenSize = gpScreen.size;
            }
            else
            {
                float halfSpan = span * 0.5f;
                float sizeByHeight = halfSpan / Mathf.Max(0.05f, targetVerticalFill);
                float sizeByWidth = halfSpan / (aspect * Mathf.Max(0.05f, targetHorizontalFill));
                cachedCamera.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth) * 1.06f;
                lastGameplayScreenSize = new Vector2(-1f, -1f);
            }

            Quaternion rotation = Quaternion.Euler(lookPitch, 0f, 0f);
            float distance = span * distanceMultiplier;
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
            float distance = span * distanceMultiplier / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            Quaternion rotation = Quaternion.Euler(lookPitch, 0f, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -distance);
            transform.position = target + offset;
            transform.rotation = Quaternion.LookRotation(target - transform.position, Vector3.up);
            lastFramedAspect = aspect;
            lastBoardFootprint = footprint;
        }
    }

    /// <summary>
    /// Phase 51: slight downward pitch so extruded pieces show side faces. Fill targets are fallbacks only.
    /// </summary>
    public void ApplyArtDirectionDefaults()
    {
        useOrthographic = true;
        lookPitch = 66f;
        distanceMultiplier = 1.15f;
        orthographicSpanFactor = 0.85f;
        targetVerticalFill = 0.56f;
        targetHorizontalFill = 0.78f;
        lookOffset = new Vector3(0f, 0.12f, 0f);
        fieldOfView = 28f;
    }

    private float ResolvePortraitAspect()
    {
        float screenAspect = (float)Screen.width / Mathf.Max(1f, Screen.height);
        float camAspect = cachedCamera != null ? cachedCamera.aspect : screenAspect;
        float aspect = Mathf.Min(screenAspect, camAspect);
        return Mathf.Clamp(aspect, 0.45f, 2f);
    }

    private void CacheCamera()
    {
        if (cachedCamera == null)
        {
            cachedCamera = GetComponent<Camera>();
        }
    }
}
