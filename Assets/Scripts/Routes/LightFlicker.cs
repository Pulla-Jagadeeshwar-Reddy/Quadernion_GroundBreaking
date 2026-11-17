using UnityEngine;

/// <summary>
/// Makes a light buzz and drop out now and then, like a failing tube. Purely local (seeded from
/// the position), so every player sees roughly the same flicker without any networking.
/// </summary>
[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    [SerializeField] private Renderer fixture;
    [SerializeField] private Color fixtureEmission = new(1.4f, 1.1f, 0.7f);
    [SerializeField, Range(0f, 1f)] private float buzz = 0.15f;
    [SerializeField] private float dropoutEvery = 6f;

    private Light target;
    private float baseIntensity;
    private float seed;
    private float nextDropout;
    private float dropoutEnd;
    private MaterialPropertyBlock block;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public void Configure(Renderer fixtureRenderer, Color emission, float buzzAmount, float dropoutSeconds)
    {
        fixture = fixtureRenderer;
        fixtureEmission = emission;
        buzz = buzzAmount;
        dropoutEvery = dropoutSeconds;
    }

    private void Awake()
    {
        target = GetComponent<Light>();
        baseIntensity = target.intensity;
        seed = Mathf.Abs(transform.position.x * 12.9898f + transform.position.z * 78.233f) % 100f;
        nextDropout = Time.time + Random.Range(1f, dropoutEvery);
        block = new MaterialPropertyBlock();
    }

    private void Update()
    {
        float t = Time.time;
        if (t >= nextDropout)
        {
            dropoutEnd = t + Random.Range(0.05f, 0.6f);
            nextDropout = t + Random.Range(dropoutEvery * 0.5f, dropoutEvery * 1.5f);
        }

        float level = 1f - buzz * Mathf.PerlinNoise(seed, t * 9f);
        if (t < dropoutEnd)
            level = Mathf.PerlinNoise(seed + 3f, t * 40f) > 0.6f ? 0.6f : 0.05f;

        target.intensity = baseIntensity * level;
        if (fixture != null)
        {
            fixture.GetPropertyBlock(block);
            block.SetColor(EmissionId, fixtureEmission * level);
            fixture.SetPropertyBlock(block);
        }
    }
}
