using UnityEngine;

/// <summary>Player settings from the pause menu, saved in PlayerPrefs.</summary>
public static class GameSettings
{
    private const string MasterKey = "gb.volume.master";
    private const string SfxKey = "gb.volume.sfx";
    private const string AmbienceKey = "gb.volume.ambience";
    private const string SensitivityKey = "gb.mouse.sensitivity";

    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(MasterKey, 0.8f);
        set
        {
            PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(value));
            AudioListener.volume = MasterVolume;
        }
    }

    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat(SfxKey, 1f);
        set => PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value));
    }

    public static float AmbienceVolume
    {
        get => PlayerPrefs.GetFloat(AmbienceKey, 0.6f);
        set
        {
            PlayerPrefs.SetFloat(AmbienceKey, Mathf.Clamp01(value));
            Sfx.RefreshAmbience();
        }
    }

    /// <summary>Multiplier on the controller's base mouse sensitivity (0.25 to 3).</summary>
    public static float MouseSensitivity
    {
        get => PlayerPrefs.GetFloat(SensitivityKey, 1f);
        set => PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, 0.25f, 3f));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Apply() => AudioListener.volume = MasterVolume;

    public static void Save() => PlayerPrefs.Save();
}
