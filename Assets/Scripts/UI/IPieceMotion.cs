using System.Collections;
using UnityEngine;

/// <summary>
/// Presentation-only piece motion. Gameplay sequencing stays in <see cref="BlockMover"/>.
/// </summary>
public interface IPieceMotion
{
    /// <summary>Snaps the visual to a board-local anchored position (UI space).</summary>
    void SnapToLocal(Vector2 localPosition);

    /// <summary>Snaps the visual to a logical grid cell via the given presentation space.</summary>
    void SnapToGrid(IGridSpace gridSpace, Vector2Int cell);

    /// <summary>Cell-to-cell hop (optional wind-up + arc/squash).</summary>
    IEnumerator AnimateHop(
        IGridSpace gridSpace,
        Vector2 visualCellSize,
        Vector2Int from,
        Vector2Int to,
        float duration,
        bool anticipate,
        Vector2Int anticipateDirection,
        float anticipateDuration,
        float anticipatePercent,
        float hopTravelScale,
        float hopLiftPercent);

    /// <summary>Pre-nest lift + scale pump.</summary>
    IEnumerator AnimateNestAnticipate(
        Vector2 visualCellSize,
        Vector2 restPosition,
        Vector3 restScale,
        float duration,
        float liftPercent,
        float anticipateScale);

    /// <summary>Curved hop into a nest cell, then sit.</summary>
    IEnumerator AnimateNestEntry(
        IGridSpace gridSpace,
        Vector2 visualCellSize,
        Vector2Int from,
        Vector2Int to,
        Vector3 restScale,
        float liftPercent,
        float arcDuration,
        float sitDuration,
        float hopScale);
}
