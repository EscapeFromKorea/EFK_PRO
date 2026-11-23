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
        int need = RequiredMask();
        if ((mask & need) != need) return;
        // RespawnZone reads this collider's bounds to find the floor and drop position.
        checkpointTrigger.enabled = true;
        if (!RespawnController.TryStoreCheckpoint(checkpoint))
        {
            checkpointTrigger.enabled = false;
            return;
        }
        MpSync.Publish(syncId, 0, 1);
    }

    // 멀티에서 나간 사람의 몸은 사라진다 — 접속 중인 도형만 모이면 저장한다. 안 그러면 이탈 중에 지나친 팀 깃발이
    // 비어 남고, 뒤 깃발이 전부 requiredCheckpoint로 잠겨 재접속 후에도 아무도 저장하지 못한다. 싱글은 세 도형 모두.
    private static int RequiredMask()
    {
        if (!MpSync.Active) return 7;
        int need = 0;
        foreach (PlayerMover body in MpSync.Bodies)
            if (body.TryGetComponent(out PlayerShapeIdentity shape)) need |= 1 << (int)shape.Kind;
        return need == 0 ? 7 : need;
    }

    public void OnMpValue(int channel, float value)
    {
        if (channel == 0 && value > 0.5f) checkpointTrigger.enabled = true;
    }
}
