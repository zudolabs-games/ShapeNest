using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Phase 79K: presentation-only directional hint — three curved motion strokes between
/// a matching target and the approaching block. Does not modify target geometry.
/// </summary>
[DisallowMultipleComponent]
public sealed class TargetDirectionHintEffect : MonoBehaviour
{
    private const int CurveCount = 3;
    private const int PointsPerCurve = 14;
    private const float LoopDuration = 0.75f;

    /// <summary>Perpendicular gap between adjacent curve centers, in cellPitch fractions.</summary>
    private const float LineSpacingFraction = 0.16f;

    /// <summary>Shared along-axis distance from target toward block (all curves use this).</summary>
    private const float AlongBaseFraction = 0.34f;

    /// <summary>Bezier mid bulge — must stay below lineSpacing so arcs do not overlap.</summary>
    private const float BulgeFraction = 0.07f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static Material sharedStrokeMaterial;

    private readonly LineRenderer[] curves = new LineRenderer[CurveCount];
    private readonly Material[] curveMaterials = new Material[CurveCount];
    private readonly Vector3[][] scratchPoints = new Vector3[CurveCount][];
    private readonly float[] phaseOffsets = { 0f, 0.22f, 0.44f };
    private MaterialPropertyBlock mpb;

    private Target target;
    private Block followBlock;
    // The block that owns the current drag session. This can differ from followBlock
    // when chain hints are shown for other blocks on the board.
    private Block sessionOwner;
    private Vector2Int towardBlockGrid;
    private Vector2Int focusWorldCell;
    private bool hasFocusWorldCell;
    private Color strokeColor = Color.white;
    private bool visible;
    private float loopTime;
    private float cellPitch = 1f;
    private Transform curveRoot;

    public bool IsVisible => visible;

    public static TargetDirectionHintEffect Ensure(Target nest)
    {
        if (nest == null)
        {
            return null;
        }

        TargetDirectionHintEffect effect = nest.GetComponent<TargetDirectionHintEffect>();
        if (effect == null)
        {
            effect = nest.gameObject.AddComponent<TargetDirectionHintEffect>();
        }

        effect.target = nest;
        effect.EnsureCurves();
        return effect;
    }

    public void Show(Block block, Vector2Int towardBlockDirection, Color color)
    {
        Vector2Int cell = nestFocusFallback(block);
        Show(block, towardBlockDirection, color, cell, block);
    }

    public void Show(Block block, Vector2Int towardBlockDirection, Color color, Vector2Int matchWorldCell)
    {
        Show(block, towardBlockDirection, color, matchWorldCell, block);
    }

    public void Show(
        Block block,
        Vector2Int towardBlockDirection,
        Color color,
        Vector2Int matchWorldCell,
        Block dragSessionOwner)
    {
        if (target == null)
        {
            target = GetComponent<Target>();
        }

        followBlock = block;
        sessionOwner = dragSessionOwner != null ? dragSessionOwner : block;
        towardBlockGrid = towardBlockDirection;
        focusWorldCell = matchWorldCell;
        hasFocusWorldCell = true;
        strokeColor = color;
        strokeColor.a = 1f;
        cellPitch = ResolveCellPitch();
        EnsureCurves();
        visible = true;
        if (curveRoot != null)
        {
            curveRoot.gameObject.SetActive(true);
        }

        for (int i = 0; i < CurveCount; i++)
        {
            if (curves[i] != null)
            {
                curves[i].enabled = true;
                curves[i].gameObject.SetActive(true);
            }
        }

        ApplyLayoutAndAnimation();
    }

    public void Refresh(Block block, Vector2Int towardBlockDirection, Color color)
    {
        Refresh(block, towardBlockDirection, color, hasFocusWorldCell ? focusWorldCell : nestFocusFallback(block), block);
    }

    public void Refresh(Block block, Vector2Int towardBlockDirection, Color color, Vector2Int matchWorldCell)
    {
        Refresh(block, towardBlockDirection, color, matchWorldCell, block);
    }

    public void Refresh(
        Block block,
        Vector2Int towardBlockDirection,
        Color color,
        Vector2Int matchWorldCell,
        Block dragSessionOwner)
    {
        if (!visible)
        {
            Show(
                block,
                towardBlockDirection,
                color,
                matchWorldCell,
                dragSessionOwner);
            return;
        }

        followBlock = block;
        sessionOwner = dragSessionOwner != null ? dragSessionOwner : block;
        towardBlockGrid = towardBlockDirection;
        focusWorldCell = matchWorldCell;
        hasFocusWorldCell = true;
        strokeColor = color;
        strokeColor.a = 1f;
        cellPitch = ResolveCellPitch();
        ApplyLayoutAndAnimation();
    }

    private Vector2Int nestFocusFallback(Block block)
    {
        return target != null ? target.GridPosition : Vector2Int.zero;
    }

    private void Awake()
    {
        // Pair hints are created as child GameObjects of the Target.
        // Resolve the Target from the parent hierarchy as well.
        target = GetComponent<Target>();

        if (target == null)
        {
            target = GetComponentInParent<Target>();
        }

        EnsureCurves();
        Hide();
    }

    private void OnDisable()
    {
        Hide();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < CurveCount; i++)
        {
            if (curveMaterials[i] != null)
            {
                Destroy(curveMaterials[i]);
                curveMaterials[i] = null;
            }

            curves[i] = null;
        }

        if (curveRoot != null)
        {
            Destroy(curveRoot.gameObject);
            curveRoot = null;
        }
    }

    public void Hide()
    {
        visible = false;
        followBlock = null;
        sessionOwner = null;
        towardBlockGrid = Vector2Int.zero;
        focusWorldCell = Vector2Int.zero;
        hasFocusWorldCell = false;
        for (int i = 0; i < CurveCount; i++)
        {
            if (curves[i] == null)
            {
                continue;
            }

            curves[i].enabled = false;
            ApplyCurveColor(i, 0f);
        }

        if (curveRoot != null)
        {
            curveRoot.gameObject.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        if (!visible)
        {
            return;
        }

        if (target == null || target.IsMatched || !target.HasLiveNestCells || target.IsMatchPresentationActive)
        {
            Hide();
            return;
        }

        // Phase 79L: never leave curves up after the followed block's drag session ends.
        if (sessionOwner != null)
        {
            BlockMover mover = sessionOwner.GetComponent<BlockMover>();
            if (mover == null || !mover.IsDragging)
            {
                Hide();
                TargetCallEffect call = target.GetComponent<TargetCallEffect>();
                call?.ResetCall("follow-block-idle");
                return;
            }
        }

        loopTime += Time.unscaledDeltaTime;
        ApplyLayoutAndAnimation();
    }

    private void EnsureCurves()
    {
        Transform parent = ResolveWorldCurveParent();
        if (curveRoot == null || curveRoot.parent != parent)
        {
            if (curveRoot != null)
            {
                Destroy(curveRoot.gameObject);
                curveRoot = null;
                for (int i = 0; i < CurveCount; i++)
                {
                    curves[i] = null;
                    if (curveMaterials[i] != null)
                    {
                        Destroy(curveMaterials[i]);
                        curveMaterials[i] = null;
                    }
                }
            }

            var rootGo = new GameObject("TargetDirectionHintCurves");
            rootGo.transform.SetParent(parent, false);
            rootGo.layer = 0;
            curveRoot = rootGo.transform;
        }

        // Remove orphaned curve children.
        for (int c = curveRoot.childCount - 1; c >= 0; c--)
        {
            Transform child = curveRoot.GetChild(c);
            if (child != null && child.name.StartsWith("DirectionHintCurve_"))
            {
                bool tracked = false;
                for (int i = 0; i < CurveCount; i++)
                {
                    if (curves[i] != null && curves[i].transform == child)
                    {
                        tracked = true;
                        break;
                    }
                }

                if (!tracked)
                {
                    Destroy(child.gameObject);
                }
            }
        }

        Material shared = ResolveStrokeMaterial();
        for (int i = 0; i < CurveCount; i++)
        {
            if (scratchPoints[i] == null)
            {
                scratchPoints[i] = new Vector3[PointsPerCurve];
            }

            if (curves[i] != null)
            {
                continue;
            }

            var go = new GameObject("DirectionHintCurve_" + i);
            go.layer = 0;
            go.transform.SetParent(curveRoot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            LineRenderer lr = go.AddComponent<LineRenderer>();
            curveMaterials[i] = new Material(shared)
            {
                name = "TargetDirectionHintStroke_" + i
            };
            lr.sharedMaterial = curveMaterials[i];
            lr.positionCount = PointsPerCurve;
            lr.useWorldSpace = true;
            lr.loop = false;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.allowOcclusionWhenDynamic = false;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 3;
            lr.sortingOrder = 40;
            lr.enabled = false;
            curves[i] = lr;
        }
    }

    private Transform ResolveWorldCurveParent()
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter != null)
        {
            return presenter.transform;
        }

        return transform;
    }

    private void ApplyLayoutAndAnimation()
    {
        if (target == null)
        {
            return;
        }

        Vector3 targetWorld = ResolveTargetWorld();
        Vector3 towardBlock = ResolveTowardWorld(towardBlockGrid);
        if (towardBlock.sqrMagnitude < 0.0000001f)
        {
            return;
        }

        Vector3 perpendicular = new Vector3(-towardBlock.z, 0f, towardBlock.x);
        if (perpendicular.sqrMagnitude < 0.0000001f)
        {
            perpendicular = Vector3.right;
        }
        else
        {
            perpendicular.Normalize();
        }

        float pitch = Mathf.Max(0.01f, cellPitch);
        float lineSpacing = pitch * LineSpacingFraction;
        float baseDist = pitch * AlongBaseFraction;
        float halfSpan = pitch * 0.26f;
        // Keep bulge < spacing so the three parallel strokes read as separate curves.
        float bulge = pitch * Mathf.Min(BulgeFraction, LineSpacingFraction * 0.45f);
        float lift = pitch * 0.14f;
        float width = pitch * 0.028f;
        float travel = pitch * 0.06f;
        float y = targetWorld.y + lift;

        for (int i = 0; i < CurveCount; i++)
        {
            LineRenderer lr = curves[i];
            if (lr == null)
            {
                continue;
            }

            float phase = Mathf.Repeat((loopTime / LoopDuration) + phaseOffsets[i], 1f);
            float appear = phase < 0.12f
                ? phase / 0.12f
                : (phase > 0.70f ? 1f - ((phase - 0.70f) / 0.30f) : 1f);
            appear = Mathf.Clamp01(appear);
            float alpha = Mathf.Clamp01(0.35f + (0.65f * appear));
            float travelT = Mathf.SmoothStep(0f, 1f, phase);
            float towardTarget = 1f - travelT;

            // Same along-axis for all three; separate with perpendicular gaps.
            float along = baseDist + (towardTarget * travel);
            float lateralOffset = (i - 1) * lineSpacing; // -spacing, 0, +spacing
            Vector3 center = targetWorld
                + (towardBlock * along)
                + (perpendicular * lateralOffset);
            center.y = y;

            Vector3 p0 = center - (towardBlock * halfSpan * 0.65f);
            Vector3 p2 = center + (towardBlock * halfSpan * 0.65f);
            Vector3 p1 = center + (perpendicular * bulge);
            p0.y = y;
            p1.y = y;
            p2.y = y;

            for (int p = 0; p < PointsPerCurve; p++)
            {
                float t = p / (float)(PointsPerCurve - 1);
                scratchPoints[i][p] = EvaluateQuadraticBezier(p0, p1, p2, t);
            }

            lr.positionCount = PointsPerCurve;
            lr.SetPositions(scratchPoints[i]);
            lr.startWidth = width;
            lr.endWidth = width * 0.9f;
            ApplyCurveColor(i, alpha);
            lr.enabled = visible;
        }
    }

    private void ApplyCurveColor(int index, float alpha)
    {
        LineRenderer lr = curves[index];
        if (lr == null)
        {
            return;
        }

        Color c = strokeColor;
        c.a = alpha;
        lr.startColor = c;
        lr.endColor = c;

        Material mat = curveMaterials[index];
        if (mat != null)
        {
            if (mat.HasProperty(BaseColorId))
            {
                mat.SetColor(BaseColorId, c);
            }

            if (mat.HasProperty(ColorId))
            {
                mat.SetColor(ColorId, c);
            }
        }

        if (mpb == null)
        {
            mpb = new MaterialPropertyBlock();
        }

        lr.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, c);
        mpb.SetColor(ColorId, c);
        lr.SetPropertyBlock(mpb);
    }

    private Vector3 ResolveTargetWorld()
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        Vector2Int cell = hasFocusWorldCell
            ? focusWorldCell
            : (target != null ? target.GridPosition : Vector2Int.zero);

        // Prefer GridSpace3D — Target.transform is often Canvas/UI space, not World3D.
        if (presenter != null && presenter.GridSpace3D != null)
        {
            Vector3 gridWorld = presenter.GridSpace3D.GridToWorld(cell);
            PieceView3D view = ResolveNestViewAtCell(cell);
            if (view != null)
            {
                Vector3 pick = view.PickWorldCenter;
                // Keep XZ from grid cell (authoritative), lift Y from live nest view.
                return new Vector3(gridWorld.x, pick.y, gridWorld.z);
            }

            return gridWorld;
        }

        PieceView3D fallbackView = ResolveNestViewAtCell(cell);
        if (fallbackView != null)
        {
            return fallbackView.PickWorldCenter;
        }

        return target != null ? target.transform.position : transform.position;
    }

    private PieceView3D ResolveNestViewAtCell(Vector2Int worldCell)
    {
        if (target == null)
        {
            return null;
        }

        if (worldCell == target.GridPosition && target.WorldView != null)
        {
            return target.WorldView;
        }

        int index = target.FindCellIndexAtWorld(worldCell);
        if (index < 0)
        {
            return target.WorldView;
        }

        if (index == target.AnchorCellIndex)
        {
            return target.WorldView;
        }

        // ExtraWorldViews follow non-anchor cell order (same as BoardPresentationController).
        IReadOnlyList<PieceView3D> extras = target.ExtraWorldViews;
        if (extras == null || extras.Count == 0)
        {
            return target.WorldView;
        }

        int extraSlot = 0;
        for (int i = 0; i < target.CellCount; i++)
        {
            if (i == target.AnchorCellIndex)
            {
                continue;
            }

            if (i == index)
            {
                return extraSlot < extras.Count ? extras[extraSlot] : target.WorldView;
            }

            extraSlot++;
        }

        return target.WorldView;
    }

    private static Vector3 EvaluateQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return (u * u * p0) + (2f * u * t * p1) + (t * t * p2);
    }

    private static Vector3 ResolveTowardWorld(Vector2Int towardBlock)
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter != null && presenter.GridSpace3D != null)
        {
            Vector3 a = presenter.GridSpace3D.GridToWorld(Vector2Int.zero);
            Vector3 b = presenter.GridSpace3D.GridToWorld(towardBlock);
            Vector3 delta = b - a;
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.0000001f)
            {
                return delta.normalized;
            }
        }

        Vector3 fallback = new Vector3(towardBlock.x, 0f, towardBlock.y);
        return fallback.sqrMagnitude > 0.0000001f ? fallback.normalized : Vector3.forward;
    }

    private static float ResolveCellPitch()
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter != null)
        {
            return Mathf.Max(0.01f, presenter.CellWorldSize);
        }

        return 1f;
    }

    private static Material ResolveStrokeMaterial()
    {
        if (sharedStrokeMaterial != null)
        {
            return sharedStrokeMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard");

        var material = new Material(shader)
        {
            name = "TargetDirectionHintStroke",
            color = Color.white
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        }

        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.renderQueue = 3100;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        sharedStrokeMaterial = material;
        return sharedStrokeMaterial;
    }
}
