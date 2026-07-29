using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 몽환의 모래시계 감속 구역 — 지정 볼륨 내 대상만 감속한다. Time.timeScale 전역 사용 금지.
/// (사유: 한 명이 발동하면 다른 플레이어 조작감까지 느려지고, 멀티 물리 동기화가 깨진다)
///
/// 스스로 트리거 진입으로 켜지지 않는다. FallingRockFlip 같은 외부 판정이 Activate()를
/// 호출해야 켜지고, duration 경과 시 자동으로 원복된다(0이면 재타격 전까지 무한 지속).
///
/// 실제 감속은 무중력 버블(ZeroGravityBubbleSystem)과 같은 PlayerGravityOverride를 재사용한다 -
/// "개별 대상 중력/속도 조정"이 필요한 기믹은 전부 이 창구를 거친다(전역 상태 변경 금지 규약).
/// </summary>
[RequireComponent(typeof(Collider))]
public class SlowZone : MonoBehaviour
{
    [Header("감속 파라미터")]
    public float slowMultiplier = 0.5f;
    [Tooltip("0이면 자동 원복이 없다 - 한 번 켜지면 영구 지속(Activate는 토글이 아니라 재타격해도 안 꺼진다).")]
    public float duration = 10f;
    public float transitionTime = 0.5f;
    [Tooltip("감속 대상 레이어. 기본값은 전체 레이어이므로, 낙석만 감속하려면 여기서 좁혀라. " +
             "플레이어 포함 여부는 레이어와 별개로 아래 includePlayer가 판단한다.")]
    public LayerMask targetMask = ~0;
    [Tooltip("플레이어도 감속 대상에 포함할지 (기본 제외 - 낙석만).")]
    public bool includePlayer = false;
    public string playerTag = "Player";

    [Header("이벤트 (연출/사운드 연동)")]
    public UnityEvent OnActivated;
    public UnityEvent OnDeactivated;

    private Collider zoneCollider;
    private readonly HashSet<PlayerGravityOverride> affected = new HashSet<PlayerGravityOverride>();
    // PlayerMover는 매 FixedUpdate 자기 moveSpeed 기준으로 수평 velocity를 직접 대입해버려서,
    // Rigidbody.velocity를 한 번 깎는 것만으로는 걷는 속도에 아무 영향이 없다(바로 다음 프레임에
    // 덮어써짐). "속도 0.5배"를 실제로 체감시키려면 moveSpeed 자체를 낮췄다가 복원해야 한다.
    private readonly Dictionary<PlayerMover, float> originalMoveSpeed = new Dictionary<PlayerMover, float>();
    private float deactivateAt;

    public bool IsActive { get; private set; }

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        zoneCollider = GetComponent<Collider>();
        if (!zoneCollider.isTrigger)
            Debug.LogError("[SlowZone] Collider가 Trigger가 아니다. isTrigger를 켜라.", this);
    }

    /// <summary>외부(FallingRockFlip 등)가 호출: 감속 구역을 켠다. 이미 켜진 상태면 지속시간만 갱신.</summary>
    public void Activate()
    {
        bool wasActive = IsActive;
        IsActive = true;
        deactivateAt = Time.time + duration;
        if (!wasActive)
            OnActivated?.Invoke();
    }

    private void Update()
    {
        if (!IsActive) return;
        if (duration <= 0f) return; // 0 = 자동 원복 없음, 재타격까지 무한 지속.
        if (Time.time >= deactivateAt)
            Deactivate();
    }

    private void FixedUpdate()
    {
        if (!IsActive) return;

        Bounds b = zoneCollider.bounds;
        Collider[] hits = Physics.OverlapBox(b.center, b.extents, transform.rotation, targetMask, QueryTriggerInteraction.Ignore);

        var stillInside = new HashSet<PlayerGravityOverride>();
        foreach (Collider hit in hits)
        {
            if (!includePlayer && hit.CompareTag(playerTag))
                continue;

            Rigidbody rb = hit.attachedRigidbody;
            if (rb == null) continue;

            PlayerGravityOverride body = rb.GetComponent<PlayerGravityOverride>();
            if (body == null) body = rb.gameObject.AddComponent<PlayerGravityOverride>();

            bool isNewlyAffected = affected.Add(body);
            stillInside.Add(body);

            // 마지막 1f는 점프 높이 배율 — 감속 구역은 점프를 강화하지 않는다(무중력 버블과 다른 축).
            body.SetGravityScale(slowMultiplier, 1f, 0f, transitionTime, 1f);

            if (isNewlyAffected)
            {
                // 진입 순간에만 속도를 한 번 깎는다. 매 프레임 곱하면 기하급수적으로 감쇠해 멈춰버린다.
                rb.velocity *= slowMultiplier;

                // 플레이어라면 걷는 속도 자체도 낮춘다(PlayerMover가 매 프레임 velocity를 moveSpeed
                // 기준으로 재대입하므로, 위 velocity 감쇠만으론 걷기에 아무 효과가 없다).
                PlayerMover mover = rb.GetComponent<PlayerMover>();
                if (mover != null)
                {
                    originalMoveSpeed[mover] = mover.moveSpeed;
                    mover.moveSpeed *= slowMultiplier;
                }

                Debug.Log($"[SlowZone] '{rb.name}' 진입 - 감속 적용 (배율 {slowMultiplier})");
            }
        }

        foreach (PlayerGravityOverride body in new List<PlayerGravityOverride>(affected))
        {
            if (body == null || stillInside.Contains(body)) continue;
            RestoreOne(body);
            affected.Remove(body);
        }
    }

    private void Deactivate()
    {
        IsActive = false;
        foreach (PlayerGravityOverride body in affected)
            RestoreOne(body);
        affected.Clear();
        OnDeactivated?.Invoke();
    }

    private void RestoreOne(PlayerGravityOverride body)
    {
        if (body == null) return;

        body.RestoreDefault(transitionTime);

        PlayerMover mover = body.GetComponent<PlayerMover>();
        if (mover != null && originalMoveSpeed.TryGetValue(mover, out float orig))
        {
            mover.moveSpeed = orig;
            originalMoveSpeed.Remove(mover);
        }

        Debug.Log($"[SlowZone] '{body.name}' 이탈/원복 - 정상 속도로 복귀");
    }

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = IsActive ? new Color(1f, 0.5f, 0.2f, 0.35f) : new Color(1f, 0.5f, 0.2f, 0.12f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
    }
}
