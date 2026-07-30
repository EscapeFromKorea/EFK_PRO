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
///
/// [감속 = 중력↓ + 낙하 속도 상한 (낙석 사양 3장)]
/// 중력만 0.5배로 낮추면 낙하 속도는 0.5배가 아니라 √0.5≈0.71배밖에 안 줄고, 게다가 떨어지는
/// 내내 계속 가속하므로 낙하 거리가 길어질수록 감속 체감이 옅어진다. 낙하 속도에 상한이 있어야
/// "낙석 사이를 언제 지나갈 수 있는지"가 계산 가능한 타이밍 퍼즐이 된다.
///
/// 사양 3장은 이를 drag↑로 표현하지만, PlayerGravityOverride의 drag는 "대상의 기본 drag에 곱하는
/// 배율"이다. 씬의 낙석 파편은 기본 drag가 0이어서(플레이어 3종도 0) 배율을 얼마로 줘도 아무
/// 효과가 없었다 - 코드에는 감속이 있는데 실제로는 안 걸리는, 조용히 무효화되는 구조였다.
/// 그래서 drag 대신 maxFallSpeed로 하강 속도를 직접 자른다: 대상이 authored drag를 올바르게
/// 갖고 있어야 한다는 전제가 사라지고, 종단속도를 근사하는 대신 정확한 값을 얻는다.
///
/// [진입 램프 — 상한을 즉시 걸지 않고 transitionTime에 걸쳐 내린다]
/// 하강 상한을 진입 첫 프레임에 그대로 걸면 구역 밖 자유낙하(약 10 U/s)가 한 스텝(0.02초) 만에
/// 2 U/s로 꺾여, 감속이 아니라 <b>보이지 않는 벽에 부딪혀 급정지</b>한 것처럼 보인다. 요구는
/// "순간적으로 느려지는 것이 보인 뒤 느려진 속도로 내려간다"이므로, 상한 자체를 진입 시 하강 속도
/// → maxFallSpeed로 transitionTime 동안 선형 보간한다(바디별로 진입 시각·진입 속도를 기록).
/// 중력이 매 스텝 속도를 상한까지 밀어붙이므로 실제 낙하 속도는 이 상한을 그대로 따라 내려간다 -
/// 즉 상한을 보간하는 것만으로 "눈에 보이는 감속 과정"이 생긴다. PlayerGravityOverride의
/// 중력 배율 전환도 같은 transitionTime을 쓰므로 두 감속이 같은 리듬으로 맞물린다.
/// 진입 1회 속도 감쇠는 <b>수평 성분에만</b> 남겼다. 세로까지 곱하면 램프와 이중으로 겹쳐 진입
/// 순간 10 → 5 U/s로 한 번 더 꺾이고(없애려던 그 급정지다) 램프 초반이 무의미해진다. 수평 감쇠는
/// "구역에 들어서면 관성이 눌린다"는 원래 의도라 유지한다 - 하강은 램프가, 수평은 이 1회 감쇠가
/// 담당하는 식으로 역할을 갈라 두었다.
///
/// [대상 추적은 폴링이 아니라 트리거 이벤트]
/// 예전에는 매 FixedUpdate Physics.OverlapBox로 구역을 다시 스캔했다. (1) Collider.bounds는 이미
/// 월드 AABB인데 거기에 transform.rotation을 또 먹여서, 구역을 회전 배치하면 감속 볼륨이 실제
/// 콜라이더와 어긋났고 (2) 초당 50회 결과 배열과 컬렉션을 새로 할당했다. 콜라이더가 어차피
/// isTrigger 필수라 Unity가 이미 겹침을 추적하고 있으므로 OnTriggerStay/OnTriggerExit로 옮겨,
/// 두 문제를 폴링 코드 삭제와 함께 없앴다. OnTriggerStay는 매 물리 스텝 들어오므로 "구역 안에
/// 이미 있던 대상이 나중에 Activate되면 그때부터 감속" 요구도 별도 코드 없이 성립한다.
/// (잠든 Rigidbody에는 Stay가 오지 않는다 - 정지한 물체는 감속할 속도 자체가 없고, 밀려서 깨어나는
/// 순간부터 적용되므로 실질 차이가 없다.)
///
/// [플레이어 이동속도(moveSpeed)는 건드리지 않는다 — 되살리지 말 것]
/// 예전에는 includePlayer=true면 PlayerMover.moveSpeed를 깎았다가 복원했다. 사양은 낙석만
/// 감속하면 되고(includePlayer 기본 false라 이 경로는 기본 설정에서 죽어 있었다), 무엇보다 구역이
/// 두 개 겹치면 두 번째 구역이 "이미 깎인" moveSpeed를 원본으로 저장해, 복원 순서에 따라 플레이어
/// 이속이 영구히 절반으로 남는 버그가 있었다. 플레이어에게 남기는 것은 중력 배율·drag와 진입 시
/// 1회 속도 감쇠뿐이다 — 낙하·관성은 느려지지만 걷는 속도는 그대로다(조작감을 빼앗지 않는 편이
/// 이 기믹의 의도에 맞다).
/// </summary>
[RequireComponent(typeof(Collider))]
public class SlowZone : MonoBehaviour
{
    [Header("감속 파라미터")]
    public float slowMultiplier = 0.5f;
    [Tooltip("구역 안에서 허용할 최대 하강 속도(U/s). 이 값이 낙석 타이밍 퍼즐의 핵심 수치다 - " +
             "6칸 구역을 지나가는 시간이 (구역 높이 / 이 값)으로 바로 나온다. 기본 2는 6칸을 3초에 " +
             "통과시켜, 이속 7 U/s인 구가 낙석 사이를 뚫을 창을 만든다(구역 밖 자유낙하는 약 10 U/s). " +
             "0으로 두면 상한 없음 - 중력 배율만 적용된다. 진입 순간 이 값으로 바로 꺾이지 않고 " +
             "아래 transitionTime에 걸쳐 진입 속도에서 이 값까지 내려온다(급정지처럼 보이지 않게).")]
    public float maxFallSpeed = 2f;
    [Tooltip("0이면 자동 원복이 없다 - 한 번 켜지면 영구 지속(Activate는 토글이 아니라 재타격해도 안 꺼진다).")]
    public float duration = 10f;
    [Tooltip("감속이 걸리고/풀리는 데 걸리는 시간(초). 중력 배율 전환과 하강 상한 램프가 같이 이 " +
             "값을 쓴다. 0으로 두면 진입 즉시 상한이 걸려 급정지처럼 보인다 - '느려지는 것이 보여야 " +
             "한다'는 요구를 지키려면 0으로 두지 마라. 크게 두면 감속이 늦게 완성되므로 낙석 통과 창 " +
             "계산(FallingRockSpawner 클래스 주석)에 오차로 들어온다.")]
    public float transitionTime = 0.5f;
    [Tooltip("감속 대상 레이어. 기본값은 전체 레이어이므로, 낙석만 감속하려면 여기서 좁혀라. " +
             "플레이어 포함 여부는 레이어와 별개로 아래 includePlayer가 판단한다.")]
    public LayerMask targetMask = ~0;
    [Tooltip("플레이어도 감속 대상에 포함할지 (기본 제외 - 낙석만). 포함해도 걷는 속도는 " +
             "느려지지 않는다(중력/저항/진입 속도만).")]
    public bool includePlayer = false;
    public string playerTag = "Player";

    [Header("이벤트 (연출/사운드 연동)")]
    public UnityEvent OnActivated;
    public UnityEvent OnDeactivated;

    private Collider zoneCollider;
    // 감속 중인 바디 + 바디별 램프 상태(진입 시각·진입 하강 속도). 상한 램프는 바디마다 진입
    // 시점이 다르므로 구역 단위 값 하나로는 표현할 수 없다.
    private readonly Dictionary<PlayerGravityOverride, FallRamp> affected =
        new Dictionary<PlayerGravityOverride, FallRamp>();
    private float deactivateAt;

    private struct FallRamp
    {
        public float startTime;
        public float startFallSpeed; // 진입 순간의 하강 속도(양수). 램프의 시작 상한이 된다.
    }

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

    // 구역이 꺼지거나 사라져도 대상이 감속된 채 남지 않게 한다(PlayerGravityOverride가 자기
    // OnDisable에서 스스로 복원하는 것과 같은 취지 - 영구 감속 사고 방지).
    private void OnDisable()
    {
        if (IsActive) Deactivate();
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsActive) return;
        if ((targetMask.value & (1 << other.gameObject.layer)) == 0) return;
        if (!includePlayer && other.CompareTag(playerTag)) return;

        // 플레이어는 트리거 자식(Player_Mesh)과 솔리드 자식(Player_Collider)이 각각 이벤트를 쏘고
        // Rigidbody는 Root에 하나뿐이다 - attachedRigidbody 기준으로 중복을 제거한다.
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        PlayerGravityOverride body = rb.GetComponent<PlayerGravityOverride>();
        if (body == null) body = rb.gameObject.AddComponent<PlayerGravityOverride>();

        // 두 번째 1f는 drag 배율(건드리지 않는다 - 위 클래스 주석의 maxFallSpeed 사유), 세 번째 0f는
        // 추가 상승 가속(감속 구역은 밀어 올리지 않는다), 마지막 1f는 점프 높이 배율 — 감속 구역은
        // 점프를 강화하지 않는다(무중력 버블과 다른 축).
        body.SetGravityScale(slowMultiplier, 1f, 0f, transitionTime, 1f);

        if (!affected.TryGetValue(body, out FallRamp ramp))
        {
            ramp = new FallRamp
            {
                startTime = Time.time,
                startFallSpeed = Mathf.Max(0f, -rb.velocity.y)
            };
            affected[body] = ramp;

            // 진입 순간에만 수평 속도를 한 번 깎는다(매 프레임 곱하면 기하급수적으로 감쇠해 멈춘다).
            // 세로는 일부러 건드리지 않는다 - 하강은 아래 램프가 담당하고, 여기서 같이 곱하면
            // 진입 프레임에 하강 속도가 한 번 더 꺾여 없애려던 급정지가 되살아난다(클래스 주석 참고).
            rb.velocity = new Vector3(rb.velocity.x * slowMultiplier, rb.velocity.y,
                                      rb.velocity.z * slowMultiplier);
            Debug.Log($"[SlowZone] '{rb.name}' 진입 - 감속 시작 (중력 x{slowMultiplier}, 하강 " +
                      $"{ramp.startFallSpeed:F1} → {maxFallSpeed} U/s를 {transitionTime}초에 걸쳐)");
        }

        // 상한은 매 스텝 적용해야 의미가 있다(중력이 계속 가속시키므로). 곱셈이 아니라 clamp라서
        // 매 프레임 걸어도 기하급수적으로 감쇠해 멈춰버리는 문제가 없다.
        if (maxFallSpeed <= 0f) return; // 0 = 상한 없음(중력 배율만).

        float ceiling = FallSpeedCeiling(ramp);
        if (rb.velocity.y < -ceiling)
            rb.velocity = new Vector3(rb.velocity.x, -ceiling, rb.velocity.z);
    }

    /// <summary>지금 이 순간 허용할 하강 속도 상한(양수). 진입 속도에서 maxFallSpeed까지
    /// transitionTime 동안 선형으로 내려온다 - 진입 첫 프레임에 상한을 그대로 걸면 급정지로 보인다.
    /// 진입 속도가 이미 상한보다 느리면 램프 없이 상한을 그대로 쓴다(느린 물체를 가속시키지 않는다).</summary>
    private float FallSpeedCeiling(FallRamp ramp)
    {
        float from = Mathf.Max(ramp.startFallSpeed, maxFallSpeed);
        if (transitionTime <= 0f || from <= maxFallSpeed) return maxFallSpeed;

        float t = Mathf.Clamp01((Time.time - ramp.startTime) / transitionTime);
        return Mathf.Lerp(from, maxFallSpeed, t);
    }

    private void OnTriggerExit(Collider other)
    {
        // 대상이 파괴되며 나가는 경로에서는 other가 이미 죽은 참조로 들어온다(프로퍼티 접근 시 예외).
        // 이 경우 복원은 PlayerGravityOverride가 자기 OnDestroy에서 알아서 한다.
        if (other == null) return;

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        PlayerGravityOverride body = rb.GetComponent<PlayerGravityOverride>();
        if (body == null || !affected.ContainsKey(body)) return;

        // 같은 Rigidbody에 자식 콜라이더가 둘(Player_Mesh/Player_Collider) 붙어 있어, 한쪽만 경계를
        // 벗어나도 Exit이 온다. 그때 복원해버리면 아직 안에 있는 쪽의 다음 OnTriggerStay가 다시
        // "신규 진입"으로 잡아 1회성 속도 감쇠를 반복 적용해, 플레이어가 경계에서 갇힌다.
        // 바디 무게중심이 아직 구역 안이면 이탈이 아니다. ClosestPoint는 회전한 콜라이더에서도
        // 정확하다(월드 AABB인 bounds와 달리) — 단 구역 콜라이더는 Box/Sphere/Capsule이나 convex
        // MeshCollider여야 한다(비convex 메쉬는 Unity가 지원하지 않는다).
        Vector3 center = rb.worldCenterOfMass;
        if (zoneCollider.ClosestPoint(center) == center) return;

        RestoreOne(body);
        affected.Remove(body);
    }

    private void Deactivate()
    {
        IsActive = false;
        foreach (PlayerGravityOverride body in affected.Keys)
            RestoreOne(body);
        affected.Clear();
        OnDeactivated?.Invoke();
    }

    private void RestoreOne(PlayerGravityOverride body)
    {
        if (body == null) return;
        body.RestoreDefault(transitionTime);
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
