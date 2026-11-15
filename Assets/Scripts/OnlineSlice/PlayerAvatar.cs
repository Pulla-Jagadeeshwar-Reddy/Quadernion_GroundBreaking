using UnityEngine;

/// <summary>
/// Shows the Runner or Granny body on the player prefab and swings its limbs while it moves.
/// Works on every machine (driven by observed movement, not input), and hides the owner's own
/// body from their camera while keeping its shadow.
/// </summary>
public class PlayerAvatar : MonoBehaviour
{
    [SerializeField] private GameObject runnerModel;
    [SerializeField] private GameObject grannyModel;
    [SerializeField] private float swingDegrees = 35f;
    [SerializeField] private float strideFrequency = 1.6f;

    private Transform legLeft, legRight, armLeft, armRight;
    private Vector3 lastPosition;
    private float phase;
    private float swingAmount;

    public bool HasModels => runnerModel != null && grannyModel != null;

    /// <summary>Builder setup.</summary>
    public void Configure(GameObject runner, GameObject granny)
    {
        runnerModel = runner;
        grannyModel = granny;
    }

    public void Show(bool granny, bool isLocalOwner)
    {
        if (!HasModels)
            return;

        runnerModel.SetActive(!granny);
        grannyModel.SetActive(granny);
        GameObject active = granny ? grannyModel : runnerModel;

        foreach (Renderer part in active.GetComponentsInChildren<Renderer>(true))
        {
            part.shadowCastingMode = isLocalOwner
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : UnityEngine.Rendering.ShadowCastingMode.On;
        }

        legLeft = active.transform.Find("Leg L");
        legRight = active.transform.Find("Leg R");
        armLeft = active.transform.Find("Arm L");
        armRight = active.transform.Find("Arm R");
        lastPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (legLeft == null)
            return;

        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;
        delta.y = 0f;
        float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;

        float target = Mathf.Clamp01(speed / 4f);
        swingAmount = Mathf.MoveTowards(swingAmount, target, Time.deltaTime * 4f);
        phase += Time.deltaTime * strideFrequency * Mathf.Lerp(1f, 2.2f, target) * Mathf.PI * 2f;

        float angle = Mathf.Sin(phase) * swingDegrees * swingAmount;
        legLeft.localRotation = Quaternion.Euler(angle, 0f, 0f);
        legRight.localRotation = Quaternion.Euler(-angle, 0f, 0f);
        if (armLeft != null)
            armLeft.localRotation = Quaternion.Euler(-angle * 0.8f, 0f, 0f);
        if (armRight != null)
            armRight.localRotation = Quaternion.Euler(angle * 0.8f, 0f, 0f);
    }
}
