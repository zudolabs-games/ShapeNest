using UnityEngine;

/// <summary>
/// Presentation-only helpers: map Gameplay Area → board-plane world size,
/// and compute a uniform adaptive cell size for rows × columns.
/// Does not alter logical GridPosition / gameplay rules.
/// </summary>
public static class BoardAdaptivePresentation3D
{
    public const float ReferenceCellSize = 1f;
    public const float GapRatio = 0.04f;
    public const float FramePadRatio = 0.14f;
    public const float FrameWallRatio = 0.24f;
    public const float ThicknessRatio = 0.76f; // Thick chunky 3D tray box matching reference
    public const float RecessRatio = 0.18f;
    public const float CornerRadiusRatio = 0.38f;
    public const float BlockHeightRatio = 0.52f; // Chunky physical height matching reference 3D blocks
    public const float NestHeightRatio = 0.22f;  // Socket tile height flush on the board surface

    /// <summary>Visible recessed tile face as fraction of cell pitch (matches BoardPresenter3D).</summary>
    public const float CellTileFaceRatio = 0.84f;

    /// <summary>
    /// Solid block XZ footprint as fraction of cell pitch. Fits neatly into target sockets.
    /// </summary>
    public const float BlockFootprintRatio = 0.78f;

    /// <summary>Nest outer footprint as fraction of cell pitch.</summary>
    public const float NestFootprintRatio = 0.84f;

    /// <summary>Phase 52A: chain connector bar radius as fraction of cell pitch (~18% diameter stem).</summary>
    public const float ConnectorRadiusRatio = 0.09f;

    /// <summary>Phase 52A: connector span along link (0.30 cell pitch spans the gap with small hidden overlap).</summary>
    public const float ConnectorLengthOverlapRatio = 0.30f;

    /// <summary>Phase 52A: connector cross-section height as fraction of block height (inset below block top bevel).</summary>
    public const float ConnectorCrossHeightRatio = 0.55f;

    /// <summary>Phase 52A: vertical midpoint drop as fraction of block height (recesses stem into gap between side walls).</summary>
    public const float ConnectorOcclusionDropRatio = 0.06f;

    /// <summary>
    /// Phase 51F: presentation-only board-plane shift for block visuals (unit mesh space).
    /// Moves the mesh toward screen-down on the XZ board without changing physical height,
    /// so pieces read centered under BoardCamera3D without sinking into tiles.
    /// Nests are not shifted. Does not move PieceView3D roots or chain spacing.
    /// </summary>
    public const float VisualCenterBoardPlaneOffsetLocal = 0f;

    /// <summary>
    /// Board-plane local offset for <see cref="PieceView3D"/> visualRoot.
    /// Y is always 0. Nests return zero. Direction follows BoardCamera3D screen-up projected on XZ.
    /// </summary>
    public static Vector3 ComputeVisualCenterOffsetLocal(bool asNest, Transform pieceRoot)
    {
        if (asNest || pieceRoot == null)
        {
            return Vector3.zero;
        }

        float amount = VisualCenterBoardPlaneOffsetLocal;
        if (amount <= 0.0001f || float.IsNaN(amount) || float.IsInfinity(amount))
        {
            return Vector3.zero;
        }

        Vector3 screenDownWorld = ResolveBoardPlaneScreenDownWorld();
        Vector3 local = pieceRoot.InverseTransformDirection(screenDownWorld);
        local.y = 0f;
        if (local.sqrMagnitude <= 0.0000001f)
        {
            local = new Vector3(0f, 0f, -1f);
        }
        else
        {
            local.Normalize();
        }

        return local * amount;
    }

    /// <summary>
    /// Direction on the board XZ plane that moves a point downward in the Game View.
    /// Opposite of camera.up projected onto the board plane.
    /// </summary>
    public static Vector3 ResolveBoardPlaneScreenDownWorld()
    {
        BoardCamera3D boardCam = Object.FindFirstObjectByType<BoardCamera3D>();
        Transform cam = boardCam != null ? boardCam.transform : null;
        if (cam == null && Camera.main != null)
        {
            cam = Camera.main.transform;
        }

        if (cam != null)
        {
            Vector3 projected = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            if (projected.sqrMagnitude > 0.0001f)
            {
                return -projected.normalized;
            }
        }

        // BoardCamera3D default: pitch 72°, yaw 0° → screen-up on board is +Z.
        return new Vector3(0f, 0f, -1f);
    }

    /// <summary>Orthographic size used only while measuring Gameplay Area → world.</summary>
    public const float MeasurementOrthoSize = 5f;

    public static RectTransform FindGameplayArea()
    {
        BoardLayout layout = Object.FindFirstObjectByType<BoardLayout>(FindObjectsInactive.Include);
        if (layout != null && layout.GameplayArea != null)
        {
            return layout.GameplayArea;
        }

        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || !t.gameObject.scene.IsValid())
            {
                continue;
            }

            if (t.name == "Gameplay Area")
            {
                return t as RectTransform ?? t.GetComponent<RectTransform>();
            }
        }

        return null;
    }

    /// <summary>
    /// Gameplay Area as a fraction of the root canvas (stable across CanvasScaler / Game view sizes).
    /// </summary>
    public static bool TryGetCanvasFractions(
        RectTransform gameplayArea,
        out float fracWidth,
        out float fracHeight,
        out Vector2 normalizedCenter)
    {
        fracWidth = 0f;
        fracHeight = 0f;
        normalizedCenter = new Vector2(0.5f, 0.5f);
        if (gameplayArea == null)
        {
            return false;
        }

        Canvas canvas = gameplayArea.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            return false;
        }

        RectTransform root = canvas.rootCanvas.transform as RectTransform;
        if (root == null)
        {
            return false;
        }

        Vector3[] areaCorners = new Vector3[4];
        Vector3[] rootCorners = new Vector3[4];
        gameplayArea.GetWorldCorners(areaCorners);
        root.GetWorldCorners(rootCorners);

        float rootMinX = Mathf.Min(rootCorners[0].x, rootCorners[2].x);
        float rootMaxX = Mathf.Max(rootCorners[0].x, rootCorners[2].x);
        float rootMinY = Mathf.Min(rootCorners[0].y, rootCorners[1].y);
        float rootMaxY = Mathf.Max(rootCorners[0].y, rootCorners[1].y);
        float rootW = Mathf.Max(0.01f, rootMaxX - rootMinX);
        float rootH = Mathf.Max(0.01f, rootMaxY - rootMinY);

        float areaMinX = Mathf.Min(areaCorners[0].x, areaCorners[2].x);
        float areaMaxX = Mathf.Max(areaCorners[0].x, areaCorners[2].x);
        float areaMinY = Mathf.Min(areaCorners[0].y, areaCorners[1].y);
        float areaMaxY = Mathf.Max(areaCorners[0].y, areaCorners[1].y);

        fracWidth = Mathf.Clamp01((areaMaxX - areaMinX) / rootW);
        fracHeight = Mathf.Clamp01((areaMaxY - areaMinY) / rootH);
        normalizedCenter = new Vector2(
            Mathf.Clamp01((((areaMinX + areaMaxX) * 0.5f) - rootMinX) / rootW),
            Mathf.Clamp01((((areaMinY + areaMaxY) * 0.5f) - rootMinY) / rootH));

        return fracWidth > 0.01f && fracHeight > 0.01f;
    }

    public static bool TryGetScreenRect(RectTransform area, out Rect screenRect)
    {
        screenRect = default;
        if (!TryGetCanvasFractions(area, out float fracW, out float fracH, out Vector2 center))
        {
            return false;
        }

        float w = Screen.width * fracW;
        float h = Screen.height * fracH;
        float x = Screen.width * center.x - w * 0.5f;
        float y = Screen.height * center.y - h * 0.5f;
        screenRect = new Rect(x, y, w, h);
        return w > 2f && h > 2f;
    }

    public static bool TryMeasureBoardPlaneRect(
        Camera camera,
        RectTransform gameplayArea,
        float planeY,
        out Vector2 worldSize,
        out Vector3 worldCenter)
    {
        worldSize = Vector2.zero;
        worldCenter = Vector3.zero;
        if (camera == null || gameplayArea == null || !camera.orthographic)
        {
            return false;
        }

        if (!TryGetCanvasFractions(gameplayArea, out float fracW, out float fracH, out Vector2 normalizedCenter))
        {
            return false;
        }

        float aspect = (float)Screen.width / Mathf.Max(1f, Screen.height);
        float fullH = 2f * camera.orthographicSize;
        float fullW = fullH * aspect;
        worldSize = new Vector2(fullW * fracW, fullH * fracH);

        Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        Ray ray = camera.ViewportPointToRay(new Vector3(normalizedCenter.x, normalizedCenter.y, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            worldCenter = ray.GetPoint(enter);
        }
        else
        {
            worldCenter = new Vector3(0f, planeY, 0f);
        }

        return worldSize.x > 0.01f && worldSize.y > 0.01f;
    }

    /// <summary>
    /// Largest uniform cell size so the full board (cells + gaps + frame) fits in available world size.
    /// </summary>
    public static float ComputeAdaptiveCellSize(
        int columns,
        int rows,
        float availableWorldWidth,
        float availableWorldHeight,
        float fitPadding)
    {
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        fitPadding = Mathf.Clamp(fitPadding, 0.5f, 1f);

        float coeffX = columns + (columns - 1) * GapRatio + 2f * (FramePadRatio + FrameWallRatio);
        float coeffZ = rows + (rows - 1) * GapRatio + 2f * (FramePadRatio + FrameWallRatio);
        float byW = availableWorldWidth / Mathf.Max(0.01f, coeffX);
        float byH = availableWorldHeight / Mathf.Max(0.01f, coeffZ);
        return Mathf.Max(0.05f, Mathf.Min(byW, byH) * fitPadding);
    }
}
