using UnityEditor;
using UnityEngine;

/// <summary>Phase 73 menu: run rigid chain travel probe / requirement suite in Play Mode.</summary>
public static class Phase73RigidChainMenu
{
    [MenuItem("Shape Nest/Phase 73 Verify Rigid Chain Travel")]
    public static void Verify()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[Phase73] Enter Play Mode first, then run this menu.");
            return;
        }

        Phase73RigidChainProbe existing = Object.FindFirstObjectByType<Phase73RigidChainProbe>();
        if (existing != null)
        {
            Object.Destroy(existing.gameObject);
        }

        var go = new GameObject("Phase73RigidChainProbe");
        Phase73RigidChainProbe probe = go.AddComponent<Phase73RigidChainProbe>();
        probe.Begin();
        Debug.Log("[Phase73] Probe started — see Captures/phase73-rigid-chain-report.txt");
    }

    [MenuItem("Shape Nest/Phase 73 Requirement Suite (T1-T8)")]
    public static void RequirementSuite()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[Phase73Suite] Enter Play Mode first, then run this menu.");
            return;
        }

        Application.runInBackground = true;
        EditorApplication.isPaused = false;
        Time.timeScale = 1f;

        Phase73RequirementSuite existing = Object.FindFirstObjectByType<Phase73RequirementSuite>();
        if (existing != null)
        {
            Object.Destroy(existing.gameObject);
        }

        var go = new GameObject("Phase73RequirementSuite");
        Phase73RequirementSuite suite = go.AddComponent<Phase73RequirementSuite>();
        suite.Begin();
        Debug.Log("[Phase73Suite] started — see Captures/phase73-requirement-suite.txt");

        // Drive frames while the Game view may be unfocused / paused.
        EditorApplication.update -= DriveSuiteSteps;
        EditorApplication.update += DriveSuiteSteps;
    }

    private static void DriveSuiteSteps()
    {
        if (!Application.isPlaying)
        {
            EditorApplication.update -= DriveSuiteSteps;
            return;
        }

        Phase73RequirementSuite suite = Object.FindFirstObjectByType<Phase73RequirementSuite>();
        if (suite == null || suite.Done)
        {
            EditorApplication.isPaused = false;
            EditorApplication.update -= DriveSuiteSteps;
            return;
        }

        EditorApplication.isPaused = false;
        EditorApplication.Step();
        EditorApplication.isPaused = false;
    }
}
