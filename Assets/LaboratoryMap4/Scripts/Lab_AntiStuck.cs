using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런타임 끼임 안전장치(F1-4). Player Variant 프리팹에 부착. 매 FixedUpdate 자기 솔리드 콜라이더와
/// 주변 "정적"(attachedRigidbody==null) 콜라이더 사이 Physics.ComputePenetration으로 관통 깊이를
/// 잰다. 관통이 profile.penetrationThreshold를 넘는 상태가 stuckFrameThreshold 프레임 이상
/// 지속되면 분리 벡터로 밀어내고, 그래도(같은 연속 관통 구간 안에서) recoveryFrameThreshold
/// 프레임 더 지속되면 최근 안전 위치(링버퍼)로 되돌린다.
///
/// [F1 재작업 판정 C5 — 카운터를 하나로 합친 이유]
/// 이전 버전은 stuckFrames(관통 지속)·pushedFrames(밀어낸 뒤 경과)를 별도 변수로 두고, 관통이
/// "한 프레임이라도" 임계 이하로 내려가면 **둘 다** 0으로 리셋했다. C5는 이를 "연속 관통
/// 프레임 수" 단일 카운터로 합쳤다.
///
/// [2차 반려 N2 — "한두 프레임 끊겨도 리셋되면 안 된다"는 그 뒤에도 안 고쳐져 있었다]
/// C5 직후 버전은 여전히 "이번 프레임이 깨끗하면(cleanThisFrame) 즉시 framesPenetrating=0"이라,
/// 관통이 간헐적으로(예: 관통→클린 1프레임→관통→...) 끊기는 경우 매 클린 프레임마다 카운터가
/// 지워져 recoveryFrameThreshold에 영원히 못 닿는 문제가 **그대로 남아 있었다**(map-reviewer
/// 2차 검문 N2 지적 — 타당함, 정확한 지적이다). #47 캐시가 "6-b 좁은 틈 실측으로 이전 설계로는
/// 도달 불가능했던 케이스를 증명했다"고 적었던 것은 **과장이었다**: 그 시나리오는 두 벽 사이
/// 폭 0.3U에 몸을 넣어 놓은 것이라 어느 프레임에도 완전한 클린(양쪽 다 관통 0)이 나오지
/// 않았을 가능성이 높고(관통 대상이 바뀌며 매 프레임 "어느 한쪽은 항상 관통"), 그렇다면 구
/// 버그가 있어도 없어도 같은 결과가 나왔을 것이다 — 그 테스트가 실제로 간헐적 클린 프레임을
/// 만들었는지 로그로 확인하지 않고 결론만 냈다. 이 문단과 캐시 #47 원문은 지우지 않고 이
/// 정정을 병기한다.
///
/// [실제 수정] cleanFrameStreak(연속 클린 프레임 수)을 별도로 세어, 이 값이
/// profile.cleanFramesToResetStuck 이상 **연속**돼야만 framesPenetrating을 0으로 되돌린다.
/// 클린 프레임이 그 미만으로 끊기면(간헐적 관통) framesPenetrating은 멈춰만 있고 지워지지
/// 않는다 — 이후 다시 관통이 잡히면 이어서 계속 올라간다. 간헐 관통 시나리오(3차 반려 A —
/// 이전 주석의 "TestAntiStuckReset IsHeld"는 오기, 실제 이름은 TestAntiStuckIntermittent,
/// Map4PlayTestRunner.cs)로 실측 확인했다(구 로직이라면 통과 못 했을 조건 — 매 다른 프레임마다
/// 확실히 클린 프레임을 최소 1회 이상 강제로 끼워 넣는다).
///
/// [개입하지 않는 경우 — 팀 공개 API로 확인 가능한 것만]
/// - PlayerMover.ExternallyDriven == true (실타래 스윙 등 — PlayerMover.cs:221, 공개 프로퍼티).
/// - Rigidbody.isKinematic == true. 이 신호로 "붙잡힘"을 판정하는 것은 RespawnController.
///   IsHeld()(RespawnController.cs:409-414)가 이미 쓰는 방식과 동일하다. [F1 재작업 판정 A5 —
///   레일카 관련 서술 정정] 레일카 탑승은 RailCartRider.cs:127-128이 isKinematic=true +
///   PlayerMover.ExternallyDriven=true를 함께 세우므로 **이미 이 두 조건으로 커버된다**(이전
///   버전은 여기 주석에 "레일카 탑승은 미확인"이라고 잘못 적어 뒀었다 — 사실이 아니었다).
/// - GetComponent&lt;ConfigurableJoint&gt;() != null. 정육면체 도킹(PlayerCubeDock.cs:331)이
///   플레이어 Root에 정확히 이 컴포넌트를 붙인다(PlayerCubeDock 자신의 `joint` 필드는 private라
///   읽을 수 없지만, 실제로 붙는 ConfigurableJoint는 Unity 엔진 공개 컴포넌트라 존재 여부를
///   리플렉션 없이 확인할 수 있다) — 조인트로 고정된 몸을 강제로 밀면 조인트 솔버와 경합한다.
/// - [판정 1 · AS-1] Physics.GetIgnoreCollision(자기 콜라이더, 상대)가 참인 쌍은 관통으로 세지 않는다 — 팀 PortalSurface.cs:49·53·65가 점유 중 통과 대상에게 충돌 무시를 거는데, 그 통과 중인 몸을 밀어내/되돌리면 진입 자체가 막힌다(검문 S6~S8_초안.md:17). F1-4 "팀 기믹이 운반 중이면 개입하지 않는다" 취지.
/// [F1 재작업 판정 A9 — 확인했지만 제외하지 않은 경우] 투석기 당김 연결(CatapultLoadController.cs)
/// 은 클래스 주석에 "실타래처럼 플레이어 몸에 조인트를 물리지 않는다"·"ExternallyDriven을 세우지
/// 않는다"·"연결된 플레이어는 평소처럼 자유롭게 걸어 다니고"라고 명시돼 있다 — 원문을 직접 읽어
/// 확인한 결과 이 시스템은 몸을 물리적으로 전혀 붙잡지 않으므로 AntiStuck과 경합할 여지가 없다
/// (제외 조건이 필요 없다는 것을 확인한 것이지, 빠뜨린 것이 아니다).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Lab_AntiStuck : MonoBehaviour
{
    public Lab_ShapeProfile profile;

    private Rigidbody rb;
    private PlayerMover mover;
    private PlayerShapeController shapeController; // 접지 판정(C5) — 없으면 접지 조건 없이 판정.
    private Collider[] solidColliders;
    private readonly Collider[] overlapBuffer = new Collider[16];

    /// <summary>연속 관통 프레임 수(합친 카운터 — 클래스 주석 "C5" 참고). 간헐적 클린 프레임
    /// 한두 번으로는 리셋되지 않는다(N2) — cleanFrameStreak이 임계에 도달해야 리셋된다.</summary>
    private int framesPenetrating;
    /// <summary>[N2] 연속으로 깨끗했던(비관통) 프레임 수. profile.cleanFramesToResetStuck에
    /// 도달해야 framesPenetrating을 0으로 되돌린다.</summary>
    private int cleanFrameStreak;

    private readonly List<Vector3> safePositions = new List<Vector3>();
    private int safeCursor;
    private float nextSampleTime;

    /// <summary>단위 검사(F1-6 항목6)가 읽는 계측값 — 이번 FixedUpdate에 밀어냈는지/되돌렸는지,
    /// 세션 누적 개입 횟수.</summary>
    public bool LastInterventionWasPush { get; private set; }
    public bool LastInterventionWasRevert { get; private set; }
    public int InterventionCount { get; private set; }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        mover = GetComponent<PlayerMover>();
        shapeController = GetComponent<PlayerShapeController>();
        RefreshSolidColliders();
    }

    /// <summary>솔리드 콜라이더 구성이 늦게 바뀌는 경우(스케일 패드 등)를 대비해 다시 모을 수 있게
    /// 공개한다.</summary>
    public void RefreshSolidColliders()
    {
        List<Collider> list = new List<Collider>();
        foreach (Collider c in GetComponentsInChildren<Collider>())
            if (!c.isTrigger) list.Add(c);
        solidColliders = list.ToArray();
    }

    private bool IsHeld()
    {
        if (mover != null && mover.ExternallyDriven) return true;
        if (rb.isKinematic) return true; // RespawnController.IsHeld()와 동일한 판정 근거(클래스 주석 참고).
        if (GetComponent<ConfigurableJoint>() != null) return true; // 정육면체 도킹(A9, 클래스 주석 참고).
        return false;
    }

    private bool IsGroundedNow()
    {
        // shapeController가 없으면(테스트용 임시 오브젝트 등) 접지 조건 없이 통과시킨다 — 안전
        // 위치를 아예 못 남기는 것보다는 비관통 조건만으로라도 남기는 편이 낫다.
        return shapeController == null || shapeController.IsGrounded();
    }

    void FixedUpdate()
    {
        LastInterventionWasPush = false;
        LastInterventionWasRevert = false;

        if (profile == null || solidColliders == null || solidColliders.Length == 0) return;
        if (IsHeld()) { framesPenetrating = 0; cleanFrameStreak = 0; return; }

        float maxDepth = ComputeMaxPenetration(out Vector3 pushDir, out Collider hitOther);

        // [C5+A 항목 — 코요테 시간 배제] 안전 위치는 "접지 + 비관통"일 때만 기록한다(명세 F1-4:
        // "최근 접지·비관통 위치"). PlayerShapeController.IsGrounded()는 팀의 유예 시간(coyote
        // time, 마지막 접지 확인 후 groundedGraceTime 동안 true 유지)을 포함하므로, 방금 낙하를
        // 시작한 프레임에도 한동안 true가 나온다 — 그 순간을 "안전"으로 저장하면 복귀 시 이미
        // 허공에서 낙하 중이던 자리로 돌아갈 위험이 있다. 수직 속도가 거의 0인(실제로 바닥에
        // 얹혀 쉬고 있는) 경우만 추가로 요구해 이 창을 좁힌다 — PlayerGroundContact의 유예
        // 시간 값 자체를 읽는 팀 공개 API는 없어(private) 속도 휴리스틱으로 근사한다(정확한
        // 해법은 아니라는 한계를 밝혀 둔다).
        bool cleanThisFrame = maxDepth <= profile.penetrationThreshold;
        bool restingOnGround = IsGroundedNow() && Mathf.Abs(rb.velocity.y) < profile.safeSampleMaxVerticalSpeed;
        if (cleanThisFrame && restingOnGround) SampleSafePosition();

        if (!cleanThisFrame)
        {
            cleanFrameStreak = 0;
            framesPenetrating++;
            if (framesPenetrating >= profile.stuckFrameThreshold + profile.recoveryFrameThreshold)
            {
                RevertToSafePosition();
            }
            else if (framesPenetrating >= profile.stuckFrameThreshold)
            {
                rb.position += pushDir * maxDepth;
                transform.position = rb.position;
                LastInterventionWasPush = true;
                InterventionCount++;
                Debug.Log($"[Lab_AntiStuck] '{name}' 밀어냄 — 깊이 {maxDepth:F3}U, 상대 " +
                          $"'{(hitOther != null ? hitOther.name : "?")}', 위치 {rb.position:F2}, " +
                          $"연속관통프레임 {framesPenetrating}");
            }
        }
        else
        {
            // [N2] 클린 프레임이 cleanFramesToResetStuck회 "연속"돼야만 리셋한다 — 간헐적으로
            // 한두 프레임만 깨끗해지는 경우(관통 대상이 매 프레임 바뀌는 좁은 틈 등) 카운터가
            // 섣불리 지워지지 않는다.
            cleanFrameStreak++;
            if (cleanFrameStreak >= profile.cleanFramesToResetStuck)
                framesPenetrating = 0;
        }
    }

    private float ComputeMaxPenetration(out Vector3 pushDir, out Collider hitOther)
    {
        float maxDepth = 0f;
        pushDir = Vector3.zero;
        hitOther = null;

        foreach (Collider mine in solidColliders)
        {
            Vector3 center = mine.bounds.center;
            float radius = profile.overlapCheckRadius + mine.bounds.extents.magnitude;
            int count = Physics.OverlapSphereNonAlloc(center, radius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = overlapBuffer[i];
                if (other == null || other.isTrigger) continue;
                if (other.attachedRigidbody != null) continue; // "정적" 콜라이더만(F1-4 범위).
                if (System.Array.IndexOf(solidColliders, other) >= 0) continue; // 자기 자신 제외.
                // [판정 1 · AS-1] 물리 충돌이 무시된 쌍 제외 — 팀 PortalSurface.RefreshOccupancy가 점유 중
                // Physics.IgnoreCollision(Box, …, occupied)를 건다(PortalSurface.cs:49 플레이어 solidCollider ·
                // :53 PortalTraversable · :65 패널과 겹치는 솔리드). 통과 중인 몸은 관통 판정 대상이 아니다.
                if (Physics.GetIgnoreCollision(mine, other)) continue;

                bool ok;
                Vector3 dir; float dist;
                try
                {
                    ok = Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation,
                        other, other.transform.position, other.transform.rotation, out dir, out dist);
                }
                catch { continue; } // 비지원 형상 조합 — 이 프레임은 건너뛴다(범위 밖).

                if (ok && dist > maxDepth)
                {
                    maxDepth = dist;
                    pushDir = dir;
                    hitOther = other;
                }
            }
        }
        return maxDepth;
    }

    private void SampleSafePosition()
    {
        if (Time.time < nextSampleTime) return;
        nextSampleTime = Time.time + profile.safePositionSampleInterval;

        if (safePositions.Count < profile.safePositionBufferSize)
        {
            safePositions.Add(rb.position);
        }
        else
        {
            safePositions[safeCursor] = rb.position;
            safeCursor = (safeCursor + 1) % Mathf.Max(1, profile.safePositionBufferSize);
        }
    }

    private void RevertToSafePosition()
    {
        framesPenetrating = 0;
        cleanFrameStreak = 0;
        if (safePositions.Count == 0)
        {
            Debug.LogWarning($"[Lab_AntiStuck] '{name}' 안전 위치가 없어 되돌리지 못한다(스폰 직후 " +
                              "끼임인 경우 발생 가능 — 검토 요청).");
            return;
        }

        int lastIndex = (safeCursor - 1 + safePositions.Count) % safePositions.Count;
        Vector3 target = safePositions[lastIndex];

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = target;
        transform.position = target;
        LastInterventionWasRevert = true;
        InterventionCount++;
        Debug.Log($"[Lab_AntiStuck] '{name}' 안전 위치로 복귀 — {target:F2}");
    }
}
