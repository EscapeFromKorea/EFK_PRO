using UnityEngine;

public sealed class Map1TeamCheckpoint : MonoBehaviour, IMpSynced
{
    public RespawnZone checkpoint;
    public BoxCollider checkpointTrigger;
    public Vector3 arrivalSize;
    public Map1CoopSwitch[] requires;

    private readonly Collider[] arrivals = new Collider[64];
    private uint syncId;

    private void Awake() => syncId = MpSync.Register(this);
    private void OnDestroy() => MpSync.Unregister(syncId, this);

    private void FixedUpdate()
    {
        if (MpSync.IsClient || checkpointTrigger.enabled || !Map1CoopSwitch.AllSet(requires)) return;
        int mask = 0;
        int count = Physics.OverlapBoxNonAlloc(transform.position + Vector3.up * arrivalSize.y * 0.5f,
            arrivalSize * 0.5f, arrivals, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var shape = arrivals[i].GetComponentInParent<PlayerShapeIdentity>();
            if (shape != null) mask |= 1 << (int)shape.Kind;
        }
        if (mask != 7) return;
        // RespawnZone reads this collider's bounds to find the floor and drop position.
        checkpointTrigger.enabled = true;
        if (!RespawnController.TryStoreCheckpoint(checkpoint))
        {
            checkpointTrigger.enabled = false;
            return;
        }
        MpSync.Publish(syncId, 0, 1);
    }

    public void OnMpValue(int channel, float value)
    {
        if (channel == 0 && value > 0.5f) checkpointTrigger.enabled = true;
    }
}
