using StarterKit.UIKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shape Nest TIME UP / Game Over screen. Displays frozen LevelManager remaining time.
/// Restart uses existing LevelManager.RestartLevel().
/// </summary>
public class ShapeNestGameOverScreen : UIScreenBase
{
    [SerializeField] private LevelManager levelManager;
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private Button restartButton;
    [SerializeField] private ResultScreenIntro intro;

    public override void OnAwake()
    {
        base.OnAwake();
        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(OnRestartClicked);
            restartButton.onClick.AddListener(OnRestartClicked);
        }
    }

    public override void OnScreenShowAnimationStarted()
    {
        base.OnScreenShowAnimationStarted();
        if (timeText != null)
        {
            int seconds = 0;
            if (levelManager != null)
            {
                seconds = Mathf.Max(0, Mathf.CeilToInt(levelManager.RemainingSeconds));
            }

            int minutes = seconds / 60;
            int remainder = seconds % 60;
            timeText.text = $"{minutes:00}:{remainder:00}";
        }

        if (intro != null)
        {
            intro.Play();
        }
    }

    private void OnRestartClicked()
    {
        if (levelManager != null)
        {
            levelManager.RestartLevel();
        }
    }
}
