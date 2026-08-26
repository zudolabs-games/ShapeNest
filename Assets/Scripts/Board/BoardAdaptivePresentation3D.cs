using UnityEngine;

/// <summary>
/// Presentation-only helpers: map Gameplay Area → board-plane world size,
/// and compute a uniform adaptive cell size for rows × columns.
/// Does not alter logical GridPosition / gameplay rules.
/// </summary>
public static class BoardAdaptivePresentation3D
{
    public const float ReferenceCellSize = 1f;
    public const float GapRatio = 0.08f;
    public const float FramePadRatio = 0.18f;
    public const float FrameWallRatio = 0.18f;
    public const float ThicknessRatio = 0.34f;
    public const float RecessRatio = 0.11f;
    public const float CornerRadiusRatio = 0.36f;
    public const float BlockHeightRatio = 0.22f;
    public const float NestHeightRatio = 0.09f;

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
