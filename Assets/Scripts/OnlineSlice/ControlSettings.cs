using UnityEngine;

/// <summary>
/// Local, per-machine control preferences. The Runner can move with WASD (default) or the
/// arrow keys; F2 switches in game and the choice is remembered.
/// </summary>
public static class ControlSettings
{
    private const string RunnerWasdKey = "Controls.RunnerUsesWasd";
    private static bool? runnerUsesWasd;

    public static bool RunnerUsesWasd
    {
        get
        {
            if (!runnerUsesWasd.HasValue)
            {
                try
                {
                    runnerUsesWasd = PlayerPrefs.GetInt(RunnerWasdKey, 1) == 1;
                }
                catch
                {
                    runnerUsesWasd = true;
                }
            }

            return runnerUsesWasd.Value;
        }
        set
        {
            runnerUsesWasd = value;
            try
            {
                PlayerPrefs.SetInt(RunnerWasdKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch
            {
                // Preferences are a convenience; ignore storage failures.
            }
        }
    }

    public static string RunnerMoveLabel => RunnerUsesWasd ? "WASD" : "Arrows";
}
