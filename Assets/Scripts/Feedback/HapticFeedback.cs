using UnityEngine;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Scene-scoped fire-and-forget haptics. Gameplay calls named methods only;
/// Android/iOS details stay inside this component.
/// </summary>
[DisallowMultipleComponent]
public class HapticFeedback : MonoBehaviour
{
    [SerializeField]
    [Tooltip("When off, haptic calls are ignored. Audio and gameplay are unchanged.")]
    private bool enableHaptics = true;

    /// <summary>Phase 61A baseline multiplier for successful grid-cell moves.</summary>
    public const float Phase61AGridMoveIntensityBaseline = 1.0f;

    [SerializeField]
    [Range(0.5f, 2f)]
    [Tooltip("Relative strength for PlayGridCellMove. Phase 61A = 1.0. Phase 61B target ≈ 1.2 (+20%).")]
    private float gridCellMoveIntensity = 1.2f;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject vibrator;
    private AndroidJavaObject lightEffect;
    private AndroidJavaObject gridMoveEffect;
    private AndroidJavaObject mediumEffect;
    private AndroidJavaObject strongEffect;
    private AndroidJavaObject timeUpEffect;
    private bool androidReady;
    private bool useVibrationEffect;
#endif

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void ShapeNest_PlayImpact(int style);

    [DllImport("__Internal")]
    private static extern void ShapeNest_PlaySelection();

    [DllImport("__Internal")]
    private static extern void ShapeNest_PlayNotification(int type);
#endif

    public bool Enabled
    {
        get => enableHaptics;
        set => enableHaptics = value;
    }

    /// <summary>Configured grid-move strength (1.0 = Phase 61A baseline).</summary>
    public float GridCellMoveIntensity => gridCellMoveIntensity;

    /// <summary>Test-only: successful grid-cell move haptic invocations since last reset.</summary>
    public int GridMoveHapticCount { get; private set; }

    /// <summary>Test-only: total named haptic method invocations since last reset.</summary>
    public int TotalHapticInvokeCount { get; private set; }

    public void ResetTestCounters()
    {
        GridMoveHapticCount = 0;
        TotalHapticInvokeCount = 0;
    }

    /// <summary>
    /// Subtle confirmation for one successful logical grid-cell translation.
    /// Called only from BlockMover after occupancy commit — not from presentation.
    /// </summary>
    public void PlayGridCellMove()
    {
        GridMoveHapticCount++;
        TotalHapticInvokeCount++;
        if (!enableHaptics)
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        // Discrete UIKit styles: stay on light impact for ~15–25% polish (style 1 is a larger jump).
        ShapeNest_PlayImpact(0);
#elif UNITY_ANDROID && !UNITY_EDITOR
        long fallbackMs = (long)Mathf.Max(8f, 8f * gridCellMoveIntensity);
        PlayAndroid(gridMoveEffect != null ? gridMoveEffect : lightEffect, fallbackMs);
#endif
    }

    public void PlayGrab()
    {
        TotalHapticInvokeCount++;
        if (!enableHaptics)
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        ShapeNest_PlaySelection();
#elif UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(lightEffect, 8L);
#endif
    }

    public void PlayHop()
    {
        // Legacy alias — same subtle grid-move pulse (keeps older callers working).
        PlayGridCellMove();
    }

    private void Awake()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        InitializeAndroid();
#endif
    }

    private void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        DisposeAndroid();
#endif
    }

    public void PlayNestEntry()
    {
        TotalHapticInvokeCount++;
        if (!enableHaptics)
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        ShapeNest_PlayImpact(1);
#elif UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(mediumEffect, 18L);
#endif
    }

    public void PlayMatch()
    {
        if (!enableHaptics)
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        ShapeNest_PlayImpact(2);
#elif UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(strongEffect, 28L);
#endif
    }

    public void PlayBlocked()
    {
        if (!enableHaptics)
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        ShapeNest_PlaySelection();
#elif UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(lightEffect, 6L);
#endif
    }

    public void PlayChainMatch()
    {
        if (!enableHaptics)
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        ShapeNest_PlayImpact(1);
#elif UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(mediumEffect, 20L);
#endif
    }

    public void PlayNestedMatch()
    {
        if (!enableHaptics)
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        ShapeNest_PlayImpact(0);
#elif UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(lightEffect, 14L);
#endif
    }

    public void PlayFullConsume()
    {
        if (!enableHaptics)
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        ShapeNest_PlayImpact(2);
#elif UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(strongEffect, 34L);
#endif
    }

    public void PlayLogicalMatch(bool consumedInnerLayer, bool fullyConsumed)
    {
        if (consumedInnerLayer && !fullyConsumed)
        {
            PlayNestedMatch();
            return;
        }

        if (fullyConsumed)
        {
            PlayFullConsume();
            return;
        }

        PlayChainMatch();
    }

    public void PlayLevelComplete()
    {
        if (!enableHaptics)
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        ShapeNest_PlayNotification(0);
#elif UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(strongEffect, 32L);
#endif
    }

    public void PlayTimeUp()
    {
        if (!enableHaptics)
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        ShapeNest_PlayNotification(1);
#elif UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(timeUpEffect, 24L);
#endif
    }

    public void PlayFailure()
    {
        PlayTimeUp();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void InitializeAndroid()
    {
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            if (vibrator == null || !vibrator.Call<bool>("hasVibrator"))
            {
                DisposeAndroid();
                return;
            }

            int sdkInt;
            using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                sdkInt = version.GetStatic<int>("SDK_INT");
            }

            useVibrationEffect = sdkInt >= 26;
            if (useVibrationEffect)
            {
                using (AndroidJavaClass vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                {
                    lightEffect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", 12L, 40);
                    // Phase 61B: ~20% stronger than Phase 61A light pulse (12ms/40 → scaled).
                    long gridMs = (long)Mathf.Clamp(Mathf.RoundToInt(12f * gridCellMoveIntensity), 8, 24);
                    int gridAmp = Mathf.Clamp(Mathf.RoundToInt(40f * gridCellMoveIntensity), 1, 255);
                    gridMoveEffect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                        "createOneShot",
                        gridMs,
                        gridAmp);
                    mediumEffect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", 18L, 90);
                    strongEffect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", 28L, 180);
                    timeUpEffect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", 24L, 140);
                }
            }

            androidReady = true;
        }
        catch (System.Exception)
        {
            DisposeAndroid();
        }
    }

    private void PlayAndroid(AndroidJavaObject effect, long fallbackMs)
    {
        if (!androidReady || vibrator == null)
        {
            return;
        }

        try
        {
            if (useVibrationEffect && effect != null)
            {
                vibrator.Call("vibrate", effect);
            }
            else
            {
                vibrator.Call("vibrate", fallbackMs);
            }
        }
        catch (System.Exception)
        {
            androidReady = false;
        }
    }

    private void DisposeAndroid()
    {
        androidReady = false;
        lightEffect?.Dispose();
        gridMoveEffect?.Dispose();
        mediumEffect?.Dispose();
        strongEffect?.Dispose();
        timeUpEffect?.Dispose();
        vibrator?.Dispose();
        lightEffect = null;
        gridMoveEffect = null;
        mediumEffect = null;
        strongEffect = null;
        timeUpEffect = null;
        vibrator = null;
    }
#endif
}
