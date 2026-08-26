using UnityEngine;

/// <summary>
/// Presentation contract for a board piece (block or nest).
/// Gameplay state stays on Block/Target; views apply presentation only.
/// </summary>
public interface IPieceView
{
    /// <summary>Applies grid cell presentation using the active grid-space.</summary>
    void ApplyGridPosition(IGridSpace gridSpace, Vector2Int gridPosition);

    /// <summary>Presentation local scale (UI: RectTransform.localScale).</summary>
    Vector3 LocalScale { get; set; }

    void SetHeld(bool held);

    void SetHeldBlend(float blend);
}
