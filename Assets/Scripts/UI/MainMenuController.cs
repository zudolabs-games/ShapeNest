using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main-menu bridge for starting gameplay. Progress and level loading remain
/// owned by PlayerProgressManager and LevelManager respectively.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [SerializeField]
    private LevelManager levelManager;

    [SerializeField]
    private LevelDatabase levelDatabase;

    [SerializeField]
    private Button continueButton;

    [SerializeField]
    private Button newGameButton;

    [SerializeField]
    private GameObject menuRoot;

    public bool CanContinue => GetContinueLevelIndex() >= 0;

    private void Awake()
    {
        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(OnContinueClicked);
            continueButton.onClick.AddListener(OnContinueClicked);
        }

        if (newGameButton != null)
        {
            newGameButton.onClick.RemoveListener(OnNewGameClicked);
            newGameButton.onClick.AddListener(OnNewGameClicked);
        }
    }

    private void OnDestroy()
    {
        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(OnContinueClicked);
        }

        if (newGameButton != null)
        {
            newGameButton.onClick.RemoveListener(OnNewGameClicked);
        }
    }

    private void OnEnable()
    {
        if (continueButton != null)
        {
            continueButton.interactable = CanContinue;
        }
    }

    public int GetContinueLevelIndex()
    {
        if (levelDatabase == null)
        {
            Debug.LogWarning("MainMenuController: LevelDatabase is not assigned.", this);
            return -1;
        }

        return PlayerProgressManager.Instance.GetContinueLevelIndex(levelDatabase.Count);
    }

    private void OnContinueClicked()
    {
        LoadMenuLevel(GetContinueLevelIndex());
    }

    private void OnNewGameClicked()
    {
        LoadMenuLevel(0);
    }

    private void LoadMenuLevel(int levelIndex)
    {
        if (levelIndex < 0)
        {
            return;
        }

        if (levelManager == null)
        {
            Debug.LogWarning("MainMenuController: LevelManager is not assigned.", this);
            return;
        }

        if (levelDatabase == null || levelIndex >= levelDatabase.Count)
        {
            Debug.LogWarning(
                $"MainMenuController: Cannot load invalid level index {levelIndex}.",
                this);
            return;
        }

        if (levelManager.LoadLevel(levelIndex) && menuRoot != null)
        {
            menuRoot.SetActive(false);
        }
    }
}
