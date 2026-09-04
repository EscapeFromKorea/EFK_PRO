using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(HingeJoint))]
public sealed class WindUpAxis : MonoBehaviour
{
    [Header("Winding")]
    public Vector3 localAxis = Vector3.up;
    public float maxTurns = 1.5f;
    public float windingAcceleration = 28f;
    public float maxAngularSpeed = 12f;

    [Header("Stored energy")]
    [Range(-1f, 1f)] public float storedEnergy;
    public float releasePerSecond = 0.12f;
    public float outputScale = 8f;
    public MonoBehaviour[] receivers;

    [Header("Feedback")]
    public Renderer energyRenderer;
    public Color emptyColor = new Color(0.3f, 0.22f, 0.05f);
    public Color fullColor = new Color(1f, 0.85f, 0.08f);

    private readonly Dictionary<TorqueStateController, int> overlappingPlayers =
        new Dictionary<TorqueStateController, int>();
    private readonly List<TorqueStateController> deadPlayers = new List<TorqueStateController>();
    private Rigidbody body;
    private HingeJoint hinge;
    private float previousAngle;
    private MaterialPropertyBlock colorBlock;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public float SignedStoredEnergy => storedEnergy;
    public Vector3 AxisWorld => transform.TransformDirection(
        localAxis.sqrMagnitude > 0.0001f ? localAxis.normalized : Vector3.up);

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        hinge = GetComponent<HingeJoint>();
        body.useGravity = false;
        body.maxAngularVelocity = maxAngularSpeed;
        hinge.axis = localAxis;
        hinge.connectedBody = null;
        hinge.useMotor = false;
        hinge.useSpring = false;
        hinge.useLimits = false;
        previousAngle = hinge.angle;
        colorBlock = new MaterialPropertyBlock();
        RefreshColor();
    }

    private void FixedUpdate()
    {
        TorqueStateController user = FindActiveUser();
        float input = 0f;
        if (user != null)
        {
            float vertical = Input.GetAxisRaw("Vertical");
            float horizontal = Input.GetAxisRaw("Horizontal");
            input = Mathf.Abs(vertical) >= Mathf.Abs(horizontal) ? vertical : horizontal;
            if (Mathf.Abs(input) > 0.05f)
                body.AddTorque(AxisWorld * (input * windingAcceleration), ForceMode.Acceleration);
        }

        float angle = hinge.angle;
        float delta = Mathf.DeltaAngle(previousAngle, angle);
        previousAngle = angle;

        if (user != null && Mathf.Abs(input) > 0.05f && maxTurns > 0.01f)
            storedEnergy = Mathf.Clamp(storedEnergy + delta / (360f * maxTurns), -1f, 1f);

        if (Mathf.Abs(storedEnergy) > 0.0001f)
        {
            float power = storedEnergy * outputScale;
            if (receivers != null)
            {
                foreach (MonoBehaviour receiver in receivers)
                {
                    IWindUpReceiver windUpReceiver = receiver as IWindUpReceiver;
                    if (windUpReceiver != null)
                        windUpReceiver.ApplyWindUpPower(power, Time.fixedDeltaTime);
                }
            }
            storedEnergy = Mathf.MoveTowards(storedEnergy, 0f, releasePerSecond * Time.fixedDeltaTime);
        }

        RefreshColor();
    }

    private TorqueStateController FindActiveUser()
    {
        deadPlayers.Clear();
        TorqueStateController result = null;
        foreach (KeyValuePair<TorqueStateController, int> entry in overlappingPlayers)
        {
            if (entry.Key == null) deadPlayers.Add(entry.Key);
            else if (entry.Key.IsTorqueEnabled && entry.Key.GetComponent<PlayerMover>().IsControlled)
                result = entry.Key;
        }
        foreach (TorqueStateController dead in deadPlayers) overlappingPlayers.Remove(dead);
        return result;
    }

    private void OnTriggerEnter(Collider other)
    {
        TorqueStateController state = other.GetComponentInParent<TorqueStateController>();
        if (state == null) return;
        int count;
        overlappingPlayers.TryGetValue(state, out count);
        overlappingPlayers[state] = count + 1;
    }

    private void OnTriggerExit(Collider other)
    {
        TorqueStateController state = other.GetComponentInParent<TorqueStateController>();
        if (state == null) return;
        int count;
        if (!overlappingPlayers.TryGetValue(state, out count)) return;
        if (count <= 1) overlappingPlayers.Remove(state);
        else overlappingPlayers[state] = count - 1;
    }

    public void SetStoredEnergyForValidation(float value)
    {
        storedEnergy = Mathf.Clamp(value, -1f, 1f);
        RefreshColor();
    }

    private void RefreshColor()
    {
        if (energyRenderer == null || colorBlock == null) return;
        energyRenderer.GetPropertyBlock(colorBlock);
        colorBlock.SetColor(ColorId, Color.Lerp(emptyColor, fullColor, Mathf.Abs(storedEnergy)));
        energyRenderer.SetPropertyBlock(colorBlock);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 axis = AxisWorld;
        Gizmos.DrawLine(transform.position - axis * 2f, transform.position + axis * 2f);
    }
}
