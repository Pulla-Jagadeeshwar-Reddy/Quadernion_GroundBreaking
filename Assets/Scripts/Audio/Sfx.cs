using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sound effects by group name. Clips live in Resources/Audio named "group_N" (e.g. step_floor_1,
/// step_floor_2); one of the group is picked at random with a little pitch variation. A missing
/// group is silent, so gameplay never depends on audio being present.
/// </summary>
public static class Sfx
{
    private static Dictionary<string, List<AudioClip>> groups;
    private static Runner runner;
    private static AudioSource ambience;
    private const float AmbienceLevel = 0.15f;

    private static void Init()
    {
        if (groups != null)
            return;

        groups = new Dictionary<string, List<AudioClip>>();
        foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Audio"))
        {
            string name = clip.name;
            int cut = name.LastIndexOf('_');
            string group = cut > 0 && int.TryParse(name.Substring(cut + 1), out _) ? name.Substring(0, cut) : name;
            if (!groups.TryGetValue(group, out List<AudioClip> list))
                groups[group] = list = new List<AudioClip>();
            list.Add(clip);
        }

        GameObject host = new("Sfx");
        Object.DontDestroyOnLoad(host);
        runner = host.AddComponent<Runner>();
    }

    public static bool Has(string group)
    {
        Init();
        return groups.ContainsKey(group);
    }

    /// <summary>
    /// 3D one-shot at a world position. Returns how long it plays (0 when silent). With
    /// <paramref name="fitSeconds"/> the clip is slowed down or sped up a little (0.8-1.25x) to last that long.
    /// </summary>
    public static float Play(string group, Vector3 position, float volume = 1f, float maxDistance = 22f, float pitch = 1f, float fitSeconds = 0f)
    {
        Init();
        if (!groups.TryGetValue(group, out List<AudioClip> clips) || clips.Count == 0)
            return 0f;

        AudioSource source = runner.Take();
        source.transform.position = position;
        source.spatialBlend = 1f;
        source.maxDistance = maxDistance;
        source.clip = clips[Random.Range(0, clips.Count)];
        source.volume = volume * GameSettings.SfxVolume;
        source.pitch = fitSeconds > 0f
            ? pitch * Mathf.Clamp(source.clip.length / fitSeconds, 0.8f, 1.25f)
            : pitch * Random.Range(0.92f, 1.08f);
        source.Play();
        return source.clip.length / Mathf.Max(0.1f, source.pitch);
    }

    /// <summary>Flat (non-positional) sound for UI and stings.</summary>
    public static void Play2D(string group, float volume = 1f)
    {
        Init();
        if (!groups.TryGetValue(group, out List<AudioClip> clips) || clips.Count == 0)
            return;

        AudioSource source = runner.Take();
        source.spatialBlend = 0f;
        source.clip = clips[Random.Range(0, clips.Count)];
        source.volume = volume * GameSettings.SfxVolume;
        source.pitch = 1f;
        source.Play();
    }

    /// <summary>Starts the looping background drone (no-op without an amb_drone clip).</summary>
    public static void StartAmbience()
    {
        Init();
        if (ambience != null || !groups.TryGetValue("amb_drone", out List<AudioClip> clips))
            return;

        ambience = runner.gameObject.AddComponent<AudioSource>();
        ambience.clip = clips[0];
        ambience.loop = true;
        ambience.spatialBlend = 0f;
        ambience.volume = AmbienceLevel * GameSettings.AmbienceVolume;
        ambience.Play();
    }

    public static void RefreshAmbience()
    {
        if (ambience != null)
            ambience.volume = AmbienceLevel * GameSettings.AmbienceVolume;
    }

    private sealed class Runner : MonoBehaviour
    {
        private readonly List<AudioSource> pool = new();

        public AudioSource Take()
        {
            foreach (AudioSource source in pool)
            {
                if (!source.isPlaying)
                    return source;
            }

            GameObject child = new("Sfx Source");
            child.transform.SetParent(transform, false);
            AudioSource created = child.AddComponent<AudioSource>();
            created.playOnAwake = false;
            created.rolloffMode = AudioRolloffMode.Linear;
            created.minDistance = 1.5f;
            created.dopplerLevel = 0f;
            pool.Add(created);
            return created;
        }
    }
}

/// <summary>
/// Repeats a sound group while something is being held (hammering, hacking, rummaging), at the
/// object's position. Driven by whoever owns it: Begin on hold start, End on stop.
/// </summary>
public sealed class SfxRepeater
{
    private readonly string group;
    private readonly float interval;
    private readonly float volume;
    private readonly bool fitToHold;
    private float next = -1f;
    private float holdEnd = -1f;

    /// <param name="fitToHold">One long clip stretched to last until the hold completes (no restarts).</param>
    public SfxRepeater(string group, float interval, float volume = 0.8f, bool fitToHold = false)
    {
        this.group = group;
        this.interval = interval;
        this.volume = volume;
        this.fitToHold = fitToHold;
    }

    public bool Running => next >= 0f;

    /// <summary>Starts the loop; <paramref name="secondsLeft"/> is how long the hold still has to go.</summary>
    public void Begin(float secondsLeft = 0f)
    {
        next = Time.time;
        holdEnd = secondsLeft > 0f ? Time.time + secondsLeft : -1f;
    }

    public void End() => next = -1f;

    public void Tick(Vector3 position)
    {
        if (next < 0f || Time.time < next)
            return;
        if (fitToHold && holdEnd > 0f)
        {
            // One clip timed to end with the hold; no new one once the hold is nearly done.
            float left = holdEnd - Time.time;
            if (left < 0.6f)
            {
                next = -1f;
                return;
            }

            float played = Sfx.Play(group, position, volume, 22f, 1f, left);
            next = Time.time + played + 0.1f;
            return;
        }

        // The next one starts only after this one has finished, so long clips never stack.
        float length = Sfx.Play(group, position, volume);
        next = Time.time + Mathf.Max(interval * Random.Range(0.85f, 1.15f), length + 0.25f);
    }
}
