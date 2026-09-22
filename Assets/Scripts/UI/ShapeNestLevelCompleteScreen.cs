using StarterKit.UIKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shape Nest level-complete screen. Observes LevelManager for the real level number.
/// Restart uses existing LevelManager.RestartLevel().
/// </summary>
public class ShapeNestLevelCompleteScreen : UIScreenBase
{
    [SerializeField] private LevelManager levelManager;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private ResultScreenIntro intro;

    private bool progressionRequested;

    public override void OnAwake()
    {
        base.OnAwake();
        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(OnContinueClicked);
            continueButton.onClick.AddListener(OnContinueClicked);
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(OnRestartClicked);
            restartButton.onClick.AddListener(OnRestartClicked);
        }
    }

    public override void OnScreenShowAnimationStarted()
    {
        base.OnScreenShowAnimationStarted();
        progressionRequested = false;
        if (levelManager != null && levelText != null)
        {
            levelText.text = levelManager.HasNextLevel
                ? $"LEVEL {levelManager.CurrentLevelIndex + 1}"
                : "ALL LEVELS COMPLETE";
        }

        if (continueButton != null)
        {
            bool hasNext = levelManager != null && levelManager.HasNextLevel;
            continueButton.gameObject.SetActive(hasNext);
            continueButton.interactable = hasNext;
        }

        if (restartButton != null)
        {
            restartButton.interactable = true;
        }

        if (intro != null)
        {
            intro.Play();
        }
    }

    private void OnContinueClicked()
    {
        if (progressionRequested || levelManager == null || !levelManager.HasNextLevel)
        {
            return;
        }

        progressionRequested = true;
        if (continueButton != null)
        {
            continueButton.interactable = false;
        }

        levelManager.LoadNextLevel();
    }

    private void OnRestartClicked()
    {
        if (progressionRequested || levelManager == null)
        {
            return;
        }

        progressionRequested = true;
        if (restartButton != null)
        {
            restartButton.interactable = false;
        }

        levelManager.RestartLevel();
    }
}
