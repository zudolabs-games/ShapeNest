using UnityEngine;

/// <summary>
/// Shared easing / curve helpers for piece and traveler presentation motion.
/// </summary>
public static class PieceMotionMath
{
    public static float EaseOutQuad(float t)
    {
        return 1f - ((1f - t) * (1f - t));
    }

    public static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - (inv * inv * inv);
    }

    public static float EaseOutSine(float t)
    {
        t = Mathf.Clamp01(t);
        return Mathf.Sin(t * (Mathf.PI * 0.5f));
    }

    public static float EaseInOutSine(float t)
    {
        t = Mathf.Clamp01(t);
        return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
    }

    /// <summary>
    /// Cell-hop cruise: mostly linear so chained hops keep speed, mixed with a
    /// light sine-out so a lone hop still settles. linearWeight 1 = constant speed.
    /// </summary>
    public static float EaseHopCruise(float t, float linearWeight)
    {
        t = Mathf.Clamp01(t);
        float linear = t;
        float settle = EaseOutSine(t);
        float w = Mathf.Clamp01(linearWeight);
        return (linear * w) + (settle * (1f - w));
    }

    public static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.5f)
        {
            return 4f * t * t * t;
        }

        float f = -2f * t + 2f;
        return 1f - ((f * f * f) * 0.5f);
    }

    /// <summary>
    /// Quintic ease-in-out: smooth velocity at both ends for automated Shuffle travel.
    /// </summary>
    public static float EaseInOutQuint(float t)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.5f)
        {
            return 16f * t * t * t * t * t;
        }

        float f = -2f * t + 2f;
        return 1f - ((f * f * f * f * f) * 0.5f);
    }

    public static float EaseInQuad(float t)
    {
        return t * t;
    }

    public static float EaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }

    /// <summary>
    /// Flattened micro-float envelope. 0 at both ends, slight plateau mid-hop.
    /// Sin(π) is a tiny negative in IEEE float; Pow(negative, 0.65) is NaN — clamp first.
    /// </summary>
    public static float MicroFloatEnvelope(float t)
    {
        t = Mathf.Clamp01(t);
        float s = Mathf.Sin(t * Mathf.PI);
        if (s <= 0f || float.IsNaN(s))
        {
            return 0f;
        }

        float envelope = Mathf.Pow(s, 0.65f);
        if (float.IsNaN(envelope) || float.IsInfinity(envelope))
        {
            return 0f;
        }

        return envelope;
    }

    public static Vector2 QuadraticBezier(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float u = 1f - t;
        return (u * u * p0) + (2f * u * t * p1) + (t * t * p2);
    }

    /// <summary>
    /// World3D carry height above rest seating. Driven by visible piece height
    /// so adaptive boards keep a readable lift under the near-top-down camera.
    /// </summary>
    public static float CarryLiftAmount(float cellAxis, float pieceHeight)
    {
        if (!IsFinite(cellAxis) || cellAxis < 0.01f)
        {
            cellAxis = 1f;
        }

        if (!IsFinite(pieceHeight) || pieceHeight < 0.01f)
        {
            pieceHeight = cellAxis * 0.22f;
        }

        float lift = pieceHeight * 0.80f;
        if (!IsFinite(lift))
        {
            lift = 0.07f;
        }

        float min = Mathf.Max(0.05f, cellAxis * 0.07f);
        float max = Mathf.Max(0.12f, cellAxis * 0.16f);
        return Mathf.Clamp(lift, min, max);
    }

    /// <summary>
    /// World3D nest-anticipation lift above the planted pose (1.5 × piece height).
    /// </summary>
    public static float NestAnticipateLiftAmount(float pieceHeight)
    {
        return SafePieceHeight(pieceHeight) * 1.5f;
    }

    /// <summary>
    /// World3D nest-jump bezier peak above rest seating (2.5 × piece height).
    /// </summary>
    public static float NestJumpPeakHeight(float pieceHeight)
    {
        return SafePieceHeight(pieceHeight) * 2.5f;
    }

    private static float SafePieceHeight(float pieceHeight)
    {
        if (!IsFinite(pieceHeight) || pieceHeight < 0.01f)
        {
            return 0.22f;
        }

        return pieceHeight;
    }

    public static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
