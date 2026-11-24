using Map2KitchenDraft;
using UnityEngine;

/// <summary>
/// One chapter 3 spray station. The visible jet and its hit capsule use the same endpoints.
/// A shape can hold the pressure plate for a partner, or cross alone while the nozzle is away.
/// </summary>
public sealed class KitchenShieldSpray : MonoBehaviour, IMpSynced
{
    [SerializeField] private Transform nozzle;
    [SerializeField] private Transform shieldTray;
    [SerializeField] private DraftWaterJet water;
    [SerializeField] private Transform pressurePlate;
    [SerializeField] private Transform pressurePlateFace;
    [SerializeField] private Transform splash;
    [SerializeField] private Transform dryRecovery;
    [SerializeField] private float travel = 1.8f;
    [SerializeField] private float period = 4.8f;
    [SerializeField] private float traySpeed = 5.5f;
    [SerializeField] private float contactRadius = 0.22f;

    private readonly Collider[] contacts = new Collider[24];
    private readonly Collider[] plateContacts = new Collider[24];
    private Vector3 nozzleCenter;
    private float trayRaisedY;
    private float trayLoweredY;
    private Vector3 faceRestPosition;
    private uint mpId;

    public bool IsShieldPressed { get; private set; }
    public bool IsWaterCut => JetWorldEnd.y > 9f;
    public int HitCount { get; private set; }
    public Vector3 JetWorldStart => water.WorldStart;
    public Vector3 JetWorldEnd => water.WorldEnd;
    public float NozzleZ => nozzle.position.z;

    public void Configure(
        Transform nozzleTransform,
        Transform trayTransform,
        DraftWaterJet effect,
        Transform plateTransform,
        Transform plateFaceTransform,
        Transform splashTransform,
        Transform recoveryTransform)
    {
        nozzle = nozzleTransform;
        shieldTray = trayTransform;
        water = effect;
        pressurePlate = plateTransform;
        pressurePlateFace = plateFaceTransform;
        splash = splashTransform;
        dryRecovery = recoveryTransform;
        nozzleCenter = nozzle.position;
        trayRaisedY = shieldTray.position.y;
        trayLoweredY = 11.65f;
        faceRestPosition = pressurePlateFace.localPosition;
        UpdateWaterEnd();
    }

    private void Awake()
    {
        if (nozzle == null || shieldTray == null || water == null ||
            pressurePlate == null || dryRecovery == null)
        {
            enabled = false;
            return;
        }

        nozzleCenter = nozzle.position;
        trayRaisedY = shieldTray.position.y;
        trayLoweredY = 11.65f;
        if (pressurePlateFace != null)
            faceRestPosition = pressurePlateFace.localPosition;
        mpId = MpSync.Register(this);
    }

    private void OnDestroy() => MpSync.Unregister(mpId, this);

    public void OnMpValue(int channel, float value)
    {
        if (channel == 0) IsShieldPressed = value > 0.5f;
        if (channel == 1 && shieldTray != null)
        {
            Vector3 position = shieldTray.position;
            position.y = value;
            shieldTray.position = position;
        }
    }

    private void FixedUpdate()
    {
        if (!MpSync.IsClient)
        {
            bool pressed = PlateHasShape();
            if (pressed != IsShieldPressed)
            {
                IsShieldPressed = pressed;
                MpSync.Publish(mpId, 0, pressed ? 1f : 0f);
            }
        }
        if (pressurePlateFace != null)
        {
            Vector3 facePosition = faceRestPosition;
            facePosition.y -= IsShieldPressed ? 0.065f : 0f;
            pressurePlateFace.localPosition = facePosition;
        }
        if (!MpSync.IsClient)
        {
            float targetY = IsShieldPressed ? trayLoweredY : trayRaisedY;
            Vector3 trayPosition = shieldTray.position;
            trayPosition.y = Mathf.MoveTowards(
                trayPosition.y, targetY, traySpeed * Time.fixedDeltaTime);
            shieldTray.position = trayPosition;
            MpSync.Publish(mpId, 1, trayPosition.y);
        }

        Vector3 nozzlePosition = nozzleCenter;
        nozzlePosition.z += travel * Mathf.Sin(
            MpSync.SharedFixedTime * (Mathf.PI * 2f / period) + 0.35f);
        nozzle.position = nozzlePosition;
        UpdateWaterEnd();
        CheckWaterContact();
    }

    private bool PlateHasShape()
    {
        Vector3 center = pressurePlate.position + Vector3.up * 0.45f;
        int count = Physics.OverlapBoxNonAlloc(
            center,
            new Vector3(0.95f, 0.72f, 1.15f),
            plateContacts,
            Quaternion.identity,
            ~0,
            QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            if (plateContacts[index] != null &&
                plateContacts[index].GetComponentInParent<PlayerMover>() != null)
                return true;
        }

        return false;
    }

    private void UpdateWaterEnd()
    {
        // The tray always spans the complete nozzle stroke. As it descends, both
        // the drawn jet and the damage capsule shorten to its upper surface.
        float bottomY = shieldTray.position.y >= water.transform.position.y - 0.35f
            ? 8.08f
            : Mathf.Max(8.08f, shieldTray.position.y + 0.17f);
        water.localEnd = Vector3.down * (water.transform.position.y - bottomY);
        if (splash != null)
            splash.position = water.WorldEnd + Vector3.up * 0.035f;
    }

    private void CheckWaterContact()
    {
        int count = Physics.OverlapCapsuleNonAlloc(
            JetWorldStart,
            JetWorldEnd,
            contactRadius,
            contacts,
            ~0,
            QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            Rigidbody body = contacts[index].attachedRigidbody;
            if (body == null || Mathf.Abs(body.position.x - nozzleCenter.x) > 2f ||
                body.GetComponent<PlayerMover>() == null || !MpSync.IsOwnBody(body)) continue;
            Transform actor = body.transform;
            if (!MpSync.Active && actor != PlayerControlSwitcher.ActiveTarget) continue;
            KitchenDraftController.MoveActor(actor, dryRecovery.position);
            PlayerFollowCamera.SnapToTarget(actor);
            HitCount++;
            return;
        }
    }
}
