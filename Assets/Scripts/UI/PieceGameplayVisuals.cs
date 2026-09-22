using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prototype chain connectors and nested-layer overlays.
/// Presentation only; occupancy and matching stay on Block/Target data.
/// </summary>
public static class PieceGameplayVisuals
{
    public const string InnerOverlayName = "InnerLayer";
    public const string InnerWellName = "InnerWell";
    public const string ConnectorPrefix = "ChainLink_";
    public const float PieceFill = 0.82f;
    public const float InnerScale = 0.52f;
    public const float ConnectorThickness = 0.22f;
    public const float ConnectorOverlap = 0.42f;

    public struct NestedInnerLook
    {
        public float scale;
        public Vector2 offset;
        public float recess;
        public Vector2 shadowOffset;
        public float darken;
        public Color recessColor;
        public float emergeDuration;
        public int sortingOffset;

        public static NestedInnerLook FromTheme(ShapeNestTheme theme)
        {
            if (theme == null)
            {
                return Default;
            }

            return new NestedInnerLook
            {
                scale = Mathf.Clamp(theme.nestedInnerScale, 0.4f, 0.7f),
                offset = theme.nestedInnerOffset,
                recess = theme.nestedInnerRecess,
                shadowOffset = theme.nestedInnerShadowOffset,
                darken = theme.nestedInnerDarken,
                recessColor = theme.nestWell,
                emergeDuration = Mathf.Max(0f, theme.nestedInnerEmergeDuration),
                sortingOffset = theme.nestedInnerSortingOffset
            };
        }

        public static NestedInnerLook Default => new NestedInnerLook
        {
            scale = InnerScale,
            offset = new Vector2(0f, -2f),
            recess = 0.2f,
            shadowOffset = new Vector2(0.8f, -1.4f),
            darken = 0.1f,
            recessColor = new Color(0.12f, 0.1f, 0.18f, 1f),
            emergeDuration = 0.08f,
            sortingOffset = 0
        };
    }

    public static Vector2 PieceSizeForCell(Vector2 cellSize)
    {
        float width = Mathf.Max(8f, cellSize.x * PieceFill);
        float height = Mathf.Max(8f, cellSize.y * PieceFill);
        return new Vector2(width, height);
    }

    public static bool CanMutateHierarchy(Transform transform)
    {
        if (transform == null)
        {
            return false;
        }

        if (!Application.isPlaying)
        {
            return false;
        }

#if UNITY_EDITOR
        if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(transform))
        {
            return false;
        }
#endif

        return true;
    }

    public static void SyncInnerOverlay(Transform parent, Sprite sprite, bool visible, Color color)
    {
        SyncInnerOverlay(parent, sprite, visible, color, NestedInnerLook.Default, null);
    }

    public static void SyncInnerOverlay(
        Transform parent,
        Sprite sprite,
        bool visible,
        Color color,
        NestedInnerLook look,
        Sprite wellSprite)
    {
        if (parent == null)
        {
            return;
        }

        Image well = CanMutateHierarchy(parent)
            ? FindOrCreateNamedImage(parent, InnerWellName)
            : FindNamedImage(parent, InnerWellName);
        Image overlay = CanMutateHierarchy(parent)
            ? FindOrCreateNamedImage(parent, InnerOverlayName)
            : FindNamedImage(parent, InnerOverlayName);

        bool show = visible && sprite != null;
        Color innerColor = color;
        float dim = 1f - Mathf.Clamp01(look.darken);
        innerColor.r *= dim;
        innerColor.g *= dim;
        innerColor.b *= dim;

        if (well != null)
        {
            well.raycastTarget = false;
            well.preserveAspect = true;
            well.enabled = show && wellSprite != null;
            if (show && wellSprite != null)
            {
                well.sprite = wellSprite;
                Color wellColor = look.recessColor;
                wellColor.a = Mathf.Clamp01(look.recess + 0.18f);
                well.color = wellColor;
            }

            LayoutContained(well.rectTransform, parent as RectTransform, look, 1.12f);
            if (CanMutateHierarchy(parent))
            {
                well.rectTransform.SetSiblingIndex(0);
            }
        }

        if (overlay == null)
        {
            return;
        }

        overlay.enabled = show;
        overlay.raycastTarget = false;
        overlay.preserveAspect = true;
        if (show)
        {
            overlay.sprite = sprite;
            overlay.color = innerColor;
            ApplyInnerRecess(overlay, look);
        }

        LayoutContained(overlay.rectTransform, parent as RectTransform, look, 1f);
        if (CanMutateHierarchy(parent))
        {
            overlay.rectTransform.SetAsLastSibling();
            ApplySortingOffset(overlay.gameObject, look.sortingOffset);
        }
    }

    public static void HideInnerOverlay(Transform parent)
    {
        HideNamedImage(parent, InnerOverlayName);
        HideNamedImage(parent, InnerWellName);
    }

    public static RectTransform CreateTravelingInner(
        RectTransform board,
        Sprite sprite,
        Vector2 size,
        Vector2 anchoredPosition,
        NestedInnerLook look)
    {
        if (board == null || sprite == null || !CanMutateHierarchy(board))
        {
            return null;
        }

        var traveler = new GameObject("InnerTravel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        traveler.layer = board.gameObject.layer;
        var rect = traveler.GetComponent<RectTransform>();
        rect.SetParent(board, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        rect.localScale = Vector3.one * look.scale;
        rect.SetAsLastSibling();

        var image = traveler.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        float dim = 1f - Mathf.Clamp01(look.darken);
        image.color = new Color(dim, dim, dim, 1f);
        ApplyInnerRecess(image, look);
        ApplySortingOffset(traveler, look.sortingOffset);
        return rect;
    }

    public static RectTransform CreateTravelingSprite(
        RectTransform board,
        Sprite sprite,
        Vector2 size,
        Vector2 anchoredPosition)
    {
        if (board == null || sprite == null || !CanMutateHierarchy(board))
        {
            return null;
        }

        var traveler = new GameObject("CellTravel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        traveler.layer = board.gameObject.layer;
        var rect = traveler.GetComponent<RectTransform>();
        rect.SetParent(board, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        rect.localScale = Vector3.one;
        rect.SetAsLastSibling();

        var image = traveler.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
        return rect;
    }

    public static void ApplyOverlayColor(Transform parent, Color color)
    {
        Transform overlay = parent != null ? parent.Find(InnerOverlayName) : null;
        ApplyNamedAlpha(overlay, color.a);
        Transform well = parent != null ? parent.Find(InnerWellName) : null;
        ApplyNamedAlpha(well, color.a);
    }

    private static void ApplyNamedAlpha(Transform child, float alpha)
    {
        if (child == null)
        {
            return;
        }

        Image image = child.GetComponent<Image>();
        if (image != null && image.enabled)
        {
            Color next = image.color;
            next.a = alpha;
            image.color = next;
        }
    }

    public static void RebuildConnectors(
        RectTransform root,
        IReadOnlyList<Vector2Int> locals,
        int cellCount,
        Vector2 cellSize,
        Color color)
    {
        if (!CanMutateHierarchy(root))
        {
            return;
        }

        ClearConnectors(root);
        if (root == null || locals == null || cellCount <= 1)
        {
            return;
        }

        int link = 0;
        for (int i = 0; i < cellCount; i++)
        {
            for (int j = i + 1; j < cellCount; j++)
            {
                Vector2Int a = locals[i];
                Vector2Int b = locals[j];
                Vector2Int delta = b - a;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                {
                    continue;
                }

                CreateConnector(root, a, b, cellSize, color, link);
                link++;
            }
        }
    }

    public static void ClearConnectors(RectTransform root)
    {
        if (root == null || !CanMutateHierarchy(root))
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null || !child.name.StartsWith(ConnectorPrefix))
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Object.DestroyImmediate(child.gameObject);
            }
            else
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }
    }

    private static void CreateConnector(
        RectTransform root,
        Vector2Int a,
        Vector2Int b,
        Vector2 cellSize,
        Color color,
        int index)
    {
        if (root == null || !CanMutateHierarchy(root))
        {
            return;
        }

        var linkObject = new GameObject($"{ConnectorPrefix}{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = linkObject.GetComponent<RectTransform>();
        rect.SetParent(root, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        Vector2 mid = new Vector2(
            (a.x + b.x) * 0.5f * cellSize.x,
            (a.y + b.y) * 0.5f * cellSize.y);
        rect.anchoredPosition = mid;

        bool horizontal = a.y == b.y;
        float thickness = Mathf.Max(6f, (horizontal ? cellSize.y : cellSize.x) * ConnectorThickness);
        float length = (horizontal ? cellSize.x : cellSize.y) * ConnectorOverlap;
        rect.sizeDelta = horizontal ? new Vector2(length, thickness) : new Vector2(thickness, length);
        rect.localScale = Vector3.one;
        linkObject.layer = root.gameObject.layer;

        var image = linkObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        image.maskable = true;
    }

    private static void ApplySortingOffset(GameObject target, int sortingOffset)
    {
        if (target == null || !CanMutateHierarchy(target.transform) || sortingOffset == 0)
        {
            return;
        }

        var canvas = target.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = target.AddComponent<Canvas>();
        }

        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOffset;
    }

    private static void LayoutContained(
        RectTransform rect,
        RectTransform parentRect,
        NestedInnerLook look,
        float wellMultiplier)
    {
        if (rect == null)
        {
            return;
        }

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = look.offset;
        float scale = look.scale * Mathf.Max(0.5f, wellMultiplier);
        rect.localScale = Vector3.one * scale;
        rect.sizeDelta = parentRect != null ? parentRect.sizeDelta : new Vector2(64f, 64f);
    }

    private static void ApplyInnerRecess(Image overlay, NestedInnerLook look)
    {
        if (overlay == null || look.recess <= 0.001f)
        {
            return;
        }

        if (!CanMutateHierarchy(overlay.transform) && overlay.GetComponent<Shadow>() == null)
        {
            return;
        }

        Shadow inset = overlay.GetComponent<Shadow>();
        if (inset == null)
        {
            if (!CanMutateHierarchy(overlay.transform))
            {
                return;
            }

            inset = overlay.gameObject.AddComponent<Shadow>();
        }

        Color color = look.recessColor;
        color.a = Mathf.Clamp01(look.recess);
        inset.effectDistance = look.shadowOffset;
        inset.effectColor = color;
        inset.useGraphicAlpha = true;
    }

    private static void HideNamedImage(Transform parent, string childName)
    {
        Image image = FindNamedImage(parent, childName);
        if (image != null)
        {
            image.enabled = false;
        }
    }

    private static Image FindNamedImage(Transform parent, string childName)
    {
        if (parent == null)
        {
            return null;
        }

        Transform existing = parent.Find(childName);
        return existing != null ? existing.GetComponent<Image>() : null;
    }

    private static Image FindOrCreateNamedImage(Transform parent, string childName)
    {
        Image existing = FindNamedImage(parent, childName);
        if (existing != null)
        {
            return existing;
        }

        if (!CanMutateHierarchy(parent))
        {
            return null;
        }

        var overlayObject = new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlayObject.layer = parent.gameObject.layer;
        var rect = overlayObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return overlayObject.GetComponent<Image>();
    }

    private static Image FindExistingOverlay(Transform parent)
    {
        return FindNamedImage(parent, InnerOverlayName);
    }

    private static Image FindOrCreateOverlay(Transform parent)
    {
        return FindOrCreateNamedImage(parent, InnerOverlayName);
    }
}
