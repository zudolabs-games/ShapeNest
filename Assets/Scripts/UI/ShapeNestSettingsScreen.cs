using StarterKit.UIKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shape Nest pause/settings screen. Session state stays on LevelManager.
/// Sound and haptics are session-only toggles on the existing feedback components.
/// </summary>
public class ShapeNestSettingsScreen : UIScreenBase
{
    [SerializeField] private LevelManager levelManager;
    [SerializeField] private AudioFeedback audioFeedback;
    [SerializeField] private HapticFeedback hapticFeedback;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button soundButton;
    [SerializeField] private Button hapticsButton;
    [SerializeField] private TMP_Text soundLabel;
    [SerializeField] private TMP_Text hapticsLabel;
    [SerializeField] private ResultScreenIntro intro;

    public override void OnAwake()
    {
        base.OnAwake();
        ResolveFeedbackInstances();
        Bind(resumeButton, OnResumeClicked);
        Bind(restartButton, OnRestartClicked);
        Bind(soundButton, OnSoundClicked);
        Bind(hapticsButton, OnHapticsClicked);
    }

    public override void OnScreenShowAnimationStarted()
    {
        base.OnScreenShowAnimationStarted();
        ResolveFeedbackInstances();
        RefreshToggleLabels();
        if (intro != null)
        {
            intro.Play();
        }
    }

    private void OnResumeClicked()
    {
        if (levelManager != null)
        {
            levelManager.ResumeSession();
        }
    }

    private void OnRestartClicked()
    {
        if (levelManager != null)
        {
            levelManager.RestartLevel();
        }
    }

    private void OnSoundClicked()
    {
        if (audioFeedback != null)
        {
            audioFeedback.SoundEnabled = !audioFeedback.SoundEnabled;
        }

        RefreshToggleLabels();
    }

    private void OnHapticsClicked()
    {
        if (hapticFeedback != null)
        {
            hapticFeedback.Enabled = !hapticFeedback.Enabled;
        }

        RefreshToggleLabels();
    }

    private void RefreshToggleLabels()
    {
        if (soundLabel != null && audioFeedback != null)
        {
            soundLabel.text = audioFeedback.SoundEnabled ? "SOUND ON" : "SOUND OFF";
        }

        if (hapticsLabel != null && hapticFeedback != null)
        {
            hapticsLabel.text = hapticFeedback.Enabled ? "HAPTICS ON" : "HAPTICS OFF";
        }
    }

    /// <summary>
    /// Uses serialized scene refs when present. Falls back to the existing scene
    /// instances used by gameplay. Never creates new feedback objects.
    /// </summary>
    private void ResolveFeedbackInstances()
    {
        if (audioFeedback == null)
        {
            audioFeedback = FindFirstObjectByType<AudioFeedback>();
            if (audioFeedback == null)
            {
                Debug.LogWarning("ShapeNestSettingsScreen: AudioFeedback was not found.", this);
            }
        }

        if (hapticFeedback == null)
        {
            hapticFeedback = FindFirstObjectByType<HapticFeedback>();
            if (hapticFeedback == null)
            {
                Debug.LogWarning("ShapeNestSettingsScreen: HapticFeedback was not found.", this);
            }
        }
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }
}
