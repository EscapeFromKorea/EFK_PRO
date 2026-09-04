using UnityEngine;

[RequireComponent(typeof(PlayerMover))]
[RequireComponent(typeof(Rigidbody))]
public sealed class TorqueStateController : MonoBehaviour, ITorqueStateProvider
{
    public float torqueAcceleration = 24f;
    public float maxAngularSpeed = 14f;
    public Color torqueColor = new Color(1f, 0.45f, 0.08f);

    private PlayerMover mover;
    private Rigidbody body;
    private RigidbodyConstraints slideConstraints;
    private Renderer[] visuals;
    private MaterialPropertyBlock colorBlock;
    private RespawnController respawnController;
    private int lastRespawnCount;
    private bool isTorqueEnabled;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public bool IsTorqueEnabled => isTorqueEnabled;

    private void Awake()
    {
        mover = GetComponent<PlayerMover>();
        body = GetComponent<Rigidbody>();
        slideConstraints = body.constraints;
        visuals = GetComponentsInChildren<Renderer>(true);
        colorBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        respawnController = FindObjectOfType<RespawnController>();
        if (respawnController != null) lastRespawnCount = respawnController.RespawnCount;
        SetTorqueEnabled(false);
    }

    private void Update()
    {
        if (respawnController == null) return;
        if (lastRespawnCount == respawnController.RespawnCount) return;
        lastRespawnCount = respawnController.RespawnCount;
        SetTorqueEnabled(false);
    }

    private void FixedUpdate()
    {
        if (!isTorqueEnabled || mover == null || !mover.IsControlled || mover.ExternallyDriven) return;

        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude < 0.01f) return;
        input.Normalize();

        float? viewYaw = PlayerFollowCamera.ViewYaw;
        if (viewYaw.HasValue)
            input = Quaternion.Euler(0f, viewYaw.Value, 0f) * input;

        Vector3 torqueAxis = Vector3.Cross(Vector3.up, input).normalized;
        body.AddTorque(torqueAxis * torqueAcceleration, ForceMode.Acceleration);
        if (body.angularVelocity.magnitude > maxAngularSpeed)
            body.angularVelocity = body.angularVelocity.normalized * maxAngularSpeed;
    }

    public void SetTorqueEnabled(bool enabled)
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (enabled == isTorqueEnabled && enabled) return;

        isTorqueEnabled = enabled;
        if (enabled)
        {
            slideConstraints = body.constraints;
            body.constraints = RigidbodyConstraints.None;
        }
        else
        {
            body.constraints = slideConstraints;
            body.angularVelocity = Vector3.zero;
        }
        RefreshVisuals();
    }

    public void ToggleTorque() => SetTorqueEnabled(!isTorqueEnabled);

    private void RefreshVisuals()
    {
        if (visuals == null) return;
        foreach (Renderer visual in visuals)
        {
            if (visual == null) continue;
            if (isTorqueEnabled)
            {
                visual.GetPropertyBlock(colorBlock);
                colorBlock.SetColor(ColorId, torqueColor);
                visual.SetPropertyBlock(colorBlock);
            }
            else
            {
                visual.SetPropertyBlock(null);
            }
        }
    }
}
