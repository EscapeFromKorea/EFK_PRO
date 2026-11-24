using UnityEngine;

public sealed class Map1TeamCheckpoint : MonoBehaviour, IMpSynced
{
    public RespawnZone checkpoint;
    public BoxCollider checkpointTrigger;
    public Vector3 arrivalSize;

    private readonly Collider[] arrivals = new Collider[64];
    private uint syncId;

    private void Awake() => syncId = MpSync.Register(this);
    private void OnDestroy() => MpSync.Unregister(syncId, this);

    // 회의 결정(2026-11-24): 스위치는 섹터 진행용 기믹일 뿐이고, 깃발은 세 도형 중 하나만 도착해도 켜진다.
    private void FixedUpdate()
    {
        if (MpSync.IsClient || checkpointTrigger.enabled) return;
        int count = Physics.OverlapBoxNonAlloc(transform.position + Vector3.up * arrivalSize.y * 0.5f,
            arrivalSize * 0.5f, arrivals, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        bool arrived = false;
        for (int i = 0; i < count && !arrived; i++)
            arrived = arrivals[i].GetComponentInParent<PlayerShapeIdentity>() != null;
        if (!arrived) return;
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
