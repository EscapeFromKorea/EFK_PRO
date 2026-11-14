using UnityEngine;

/// <summary>맵4의 좁은 방에서 팀 카메라의 시선이 벽 밖으로 빠지면 카메라만 안쪽으로 당긴다.</summary>
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(Camera), typeof(PlayerFollowCamera))]
public sealed class Lab_CameraOcclusion : MonoBehaviour
{
    public float probeRadius = 0.2f;
    public float surfaceClearance = 0.1f;
    public float recoverySeconds = 0.12f;
    public Collider LastObstruction { get; private set; }
    public float AllowedDistance { get; private set; }

    private readonly RaycastHit[] hits = new RaycastHit[64];
    private PlayerFollowCamera follow;
    private Transform previousTarget;
    private float currentDistance, distanceVelocity;

    private void Awake() { follow = GetComponent<PlayerFollowCamera>(); }

    private void LateUpdate()
    {
        Transform target = follow != null ? follow.target : null;
        if (target == null) return;
        // 팀 카메라가 정한 광축 위로 도형 평면을 투영한다. 조준·굴리기·수직 감쇠의 시선은 바꾸지 않는다.
        Vector3 reference = target.position + Vector3.up * follow.lookHeightOffset;
        float focusDistance = Mathf.Max(0.1f, Vector3.Dot(reference - transform.position, transform.forward));
        Vector3 look = transform.position + transform.forward * focusDistance;
        Vector3 offset = transform.position - look;
        float desired = offset.magnitude;
        if (desired < 0.001f) return;
        Vector3 direction = offset / desired;
        float allowed = desired;
        LastObstruction = null;
        int count = Physics.SphereCastNonAlloc(look, probeRadius, direction, hits, desired,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null || collider.GetComponentInParent<PlayerMover>() != null) continue;
            float distance = Mathf.Max(0.05f, hits[i].distance - surfaceClearance);
            if (distance >= allowed) continue;
            allowed = distance;
            LastObstruction = collider;
        }
        AllowedDistance = allowed;
        if (target != previousTarget || currentDistance <= 0f)
        {
            currentDistance = allowed;
            distanceVelocity = 0f;
            previousTarget = target;
        }
        // 가림이 생기면 즉시 당기고, 벗어날 때만 천천히 원래 거리로 돌아간다.
        if (allowed < currentDistance) { currentDistance = allowed; distanceVelocity = 0f; }
        else currentDistance = Mathf.SmoothDamp(currentDistance, allowed, ref distanceVelocity, recoverySeconds);
        // 가림도 복귀 보간도 없으면 팀 카메라의 추적·굴리기 시선을 그대로 둔다.
        if (LastObstruction == null && currentDistance >= desired - 0.005f) return;
        transform.position = look + direction * Mathf.Min(currentDistance, allowed);
    }
}
