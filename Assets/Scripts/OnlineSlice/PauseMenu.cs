using UnityEngine;

/// <summary>
/// Esc menu during a match: resume, volumes, mouse sensitivity, leave. The game keeps running for
/// the other player; the local player just stops taking input (NetworkPlayerController checks IsOpen).
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    private static PauseMenu instance;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null)
            return;
        GameObject host = new("Pause Menu");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<PauseMenu>();
    }

    public static void Toggle()
    {
        IsOpen = !IsOpen;
        Sfx.Play2D("click", 0.4f);
        if (!IsOpen)
            GameSettings.Save();
    }

    public static void Close()
    {
        if (IsOpen)
            Toggle();
    }

    private void Update()
    {
        // Closed automatically when the match is gone (left, disconnected).
        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (IsOpen && (manager == null || !manager.IsListening))
            IsOpen = false;
    }

    private void OnGUI()
    {
        if (!IsOpen)
            return;

        titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        Rect panel = new(Screen.width * 0.5f - 200f, Screen.height * 0.5f - 190f, 400f, 380f);
        GUI.Box(panel, string.Empty);
        GUI.Label(new Rect(panel.x, panel.y + 12f, panel.width, 32f), "Paused", titleStyle);

        float x = panel.x + 30f;
        float y = panel.y + 62f;
        GameSettings.MasterVolume = Slider("Master volume", GameSettings.MasterVolume, 0f, 1f, x, ref y);
        GameSettings.SfxVolume = Slider("Sound effects", GameSettings.SfxVolume, 0f, 1f, x, ref y);
        GameSettings.AmbienceVolume = Slider("Ambience", GameSettings.AmbienceVolume, 0f, 1f, x, ref y);
        GameSettings.MouseSensitivity = Slider("Mouse sensitivity", GameSettings.MouseSensitivity, 0.25f, 3f, x, ref y);

        y += 10f;
        if (GUI.Button(new Rect(x, y, 340f, 36f), "Resume  (Esc)"))
        {
            Close();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        y += 46f;
        if (GUI.Button(new Rect(x, y, 340f, 36f), "Leave game"))
        {
            Close();
            RelayConnectionUI.DisconnectActive();
        }
    }

    private float Slider(string label, float value, float min, float max, float x, ref float y)
    {
        GUI.Label(new Rect(x, y, 200f, 22f), label, labelStyle);
        GUI.Label(new Rect(x + 280f, y, 60f, 22f), max > 1f ? value.ToString("0.00") + "x" : Mathf.RoundToInt(value * 100f) + "%", labelStyle);
        float result = GUI.HorizontalSlider(new Rect(x, y + 24f, 340f, 18f), value, min, max);
        y += 52f;
        return result;
    }
}
