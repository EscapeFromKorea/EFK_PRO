using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 태엽 축의 실제 입력 감지 — 기둥 위에 얹힌 막대 하나를 미는 방식(2026-09-04 재설계). 막대 전체가
/// 이 트리거 하나다(양 끝에 방향이 고정된 별도 패들이 없다). PortalSystem 굴리기 모드로 이동 방식이
/// 바뀐 플레이어("토크 상태", 원 기획 요구사항 §1)가 텀블로 지나가면 그때마다
/// <see cref="WindupAxle.ApplyRotation"/>을 한 번 호출한다 — 카타풀트 조준 손잡이처럼 접촉 자체가
/// 신호이지 연속 조작이 아니다.
///
/// <b>회전 방향은 "친 방향"으로 동적으로 정한다.</b> 어느 쪽 끝을 쳤는지가 아니라, 막대 피벗
/// (`axle.crank`)에서 접촉 지점(플레이어 위치)으로의 반경 벡터와 플레이어의 진행 방향을 외적해
/// 부호(시계/반시계)를 구한다 — 막대를 미는 실제 방향에 물리적으로 대응한다.
///
/// <b>진행 방향은 velocity가 아니라 `PlayerRollModeReceiver.CurrentTumbleDirection`에서 온다
/// (2026-09-05, 코드 리뷰로 발견).</b> 처음엔 `body.velocity`로 밀린 방향을 쟀는데, 굴리기 모드는
/// 텀블 중엔 isKinematic + `Grab()`/`Release()`가 velocity를 명시적으로 0으로 찍고, 텀블 사이에도
/// 매 스텝 수평 velocity를 직접 지운다(PortalSystem/CLAUDE.md "수평 이동 봉쇄" 절) — 즉
/// `RollModeActive`가 켜져 있는 한 velocity는 항상 0이라 방향이 절대 안 나왔다(어느 쪽을 쳐도
/// `Mathf.Sign(0f)`가 항상 +1이라 고정 방향으로만 감겼다). PortalSystem에 읽기전용 프로퍼티 하나를
/// 추가해(교차 폴더 하드 룰에 따라 사용자 허가 하에 진행) 실제 텀블 이동 방향을 그대로 받는다.
///
/// <b>굴리기 모드가 아니면 반응하지 않는다.</b> 걸어서 지나가는 것만으로는 축이 감기지 않는다 —
/// `RollModeActive`와 `CurrentTumbleDirection`(텀블 중이 아니면 zero) 둘 다 게이트로 쓴다.
///
/// <b>같은 통과 안의 재발화를 무시한다.</b> 텀블 중엔 플레이어가 isKinematic을 켰다 끄는데, 이
/// 트리거에 겹쳐 있는 동안 그 플립이 일어나면 PhysX가 OnTriggerExit+Enter를 다시 쏠 수 있다
/// (PortalSystem이 Toggle 재발화로 실측한 것과 같은 함정 — PortalSystem/CLAUDE.md "포탈 위에서만
/// 굴리기가 새어 나간다" 절 참고). ApplyRotation은 멱등이 아니라(호출마다 델타가 누적된다) Toggle과
/// 달리 값을 세는 문제라, 같은 통과에서 두 번 세면 안 감은 만큼 더 감긴 것으로 잘못 기록된다.
/// 그래서 여기도 Portal과 같은 "겹침 도장" 패턴으로 한 통과당 한 번만 센다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WindupPaddleInput : MonoBehaviour
{
    [Tooltip("신호를 보낼 태엽 축.")]
    public WindupAxle axle;

    [Tooltip("한 번 지나갈 때 가할 signedDelta의 크기(부호 제외). 부호(회전 방향)는 접촉 시점에 " +
             "동적으로 계산한다.")]
    public float deltaPerHit = 1f;

    // Portal.cs의 "같은 통과 안 재발화 무시"와 동일한 패턴 — 상세 근거는 위 클래스 주석 참고.
    private readonly Dictionary<Rigidbody, float> lastOverlapTime = new Dictionary<Rigidbody, float>();
    private readonly List<Rigidbody> staleBuffer = new List<Rigidbody>(); // 정리 스윕용 재사용 버퍼
    private float nextSweepTime;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerStay(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body != null) lastOverlapTime[body] = Time.time;

        // 겹침 도장은 "같은 통과 재발화" 판정에 최근 값(수 프레임 이내)만 쓴다 — 5초 넘게 안 만진
        // 기록은 그 판정과 무관하니 정리한다. OnTriggerExit에서 즉시 지우면 안 된다: 재발화가 바로
        // 그 Exit 직후에 오므로(위 클래스 주석) 지운 순간 재발화를 새 통과로 오판한다.
        if (Time.time >= nextSweepTime)
        {
            nextSweepTime = Time.time + 5f;
            staleBuffer.Clear();
            foreach (var kv in lastOverlapTime)
                if (Time.time - kv.Value > 5f) staleBuffer.Add(kv.Key);
            for (int i = 0; i < staleBuffer.Count; i++) lastOverlapTime.Remove(staleBuffer[i]);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (axle == null || axle.crank == null) return;

        PlayerShapeIdentity identity = other.GetComponentInParent<PlayerShapeIdentity>();
        if (identity == null) return;

        // 플레이어는 트리거(Player_Mesh)와 솔리드(Player_Collider) 콜라이더가 둘 다 이 트리거에
        // 들어온다 — 솔리드 하나만 받는다(AccelPad/Portal과 같은 이중 발화 방지 관용구).
        bool isSolid = identity.solidCollider != null ? identity.solidCollider == other : !other.isTrigger;
        if (!isSolid) return;

        PlayerRollModeReceiver rollMode = identity.GetComponent<PlayerRollModeReceiver>();
        if (rollMode == null || !rollMode.RollModeActive) return;

        Vector3 push = rollMode.CurrentTumbleDirection;
        if (push.sqrMagnitude < 0.0001f) return; // 텀블 이동 중이 아니면(호 사이 대기) 방향을 모른다

        Rigidbody body = other.attachedRigidbody;
        if (body == null) return;

        float seen;
        bool samePass = lastOverlapTime.TryGetValue(body, out seen)
                        && Time.time - seen <= Time.fixedDeltaTime * 2.5f;
        lastOverlapTime[body] = Time.time;
        if (samePass) return;

        // "친 방향"으로 회전 방향을 정한다 — 피벗→접촉지점 반경 벡터와 텀블 진행 방향의 외적 부호가
        // 시계/반시계를 가른다(반경과 진행 방향이 이루는 평면 회전 방향, Vector3.Cross(...).y로 판정).
        Vector3 radial = other.transform.position - axle.crank.position;
        radial.y = 0f;
        float turnSign = Mathf.Sign(Vector3.Cross(radial, push).y);

        axle.ApplyRotation(turnSign * deltaPerHit);
    }
}
