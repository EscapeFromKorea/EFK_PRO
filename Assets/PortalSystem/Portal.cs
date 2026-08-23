using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 문틀처럼 지나가는 트리거. 통과한 도형의 <b>이동 방식</b>을 굴리기 모드로 켜거나 끈다.
///
/// <b>포탈은 순간이동을 하지 않는다.</b> 문틀을 지나는 것뿐이고 위치는 그대로다 — 바뀌는 것은
/// 조작 규칙이다. 입구 포탈과 출구 포탈이 한 쌍이고, 그 사이 구간에서만 굴리기가 유효하다.
/// 굴리기 구간은 <b>선택 경로(지름길·숨겨진 길·보상)</b> 전용이다 — 필수 경로에 두면 네모 단독
/// 완주(LD-01)가 그 구간을 굴리기로 지나가야 해서 머지 조건이 무너진다(PRD §11).
///
/// 실제 상태 기계는 플레이어 쪽 <see cref="PlayerRollModeReceiver"/>가 전담한다(발신자-수신자
/// 분리). 리시버는 이 포탈이 최초 접촉 시 런타임으로 붙이므로 PlayerSystem 폴더도, 씬의
/// 플레이어 3종도 건드릴 필요가 없다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Portal : MonoBehaviour
{
    public enum PortalAction
    {
        Toggle,  // 켜져 있으면 끄고, 꺼져 있으면 켠다 (기본 — 같은 문으로 되돌아 나올 수 있다)
        Enable,  // 항상 켠다 (입구 전용)
        Disable, // 항상 끈다 (출구 전용)
    }

    [Tooltip("이 포탈을 통과했을 때 굴리기 모드를 어떻게 할지. 기본 Toggle — 편도(맵 끝까지 유지)를 " +
             "기본으로 두지 않는 이유는, 모드가 맵 나머지 전체로 새어 나가면 '구간을 예외로 선언'이 " +
             "아니라 '그 이후 전부를 예외로 선언'이 되어 선택 경로라는 전제가 무너지기 때문이다.")]
    public PortalAction action = PortalAction.Toggle;

    // [왜 리시버 노브가 여기 있나]
    // 리시버는 이 포탈이 런타임에 AddComponent하므로 씬에 미리 존재하지 않는다 — 즉 리시버 쪽
    // public 필드는 아무도 인스펙터에서 조정할 수 없었다(PRD §12가 "인스펙터 3노브"라 적어 둔 것과
    // 어긋난 지점이다). 그래서 같은 값을 문 쪽에 두고 리시버를 얻는 즉시 그대로 복사한다.
    // 기본값은 리시버 기본값과 일치시켜 둔다. 한 몸이 값이 다른 문 여럿을 지나면 마지막 문이 이긴다.
    [Header("굴리기 노브")]
    [Tooltip("텀블 1회 소요 시간 배율. 기본 소요 시간은 '한 칸 ÷ 그 도형의 이속'으로 자동 계산된다. " +
             "⚠ 배율의 대상은 '속도'가 아니라 '소요 시간'이다 — 1보다 크면 느려지고(1.25 = 25% 느림), " +
             "1보다 작으면 빨라진다(0.8 = 25% 빠름). 회전을 늦추고 싶으면 1보다 큰 값을 넣어라.")]
    public float tumbleDurationScale = 1f;

    [Tooltip("방향키를 누르고 있을 때 다음 텀블까지의 간격(초). 0으로 두지 마라 — 그 구간에서 R 리스폰이 거절된다.")]
    public float holdRepeatInterval = 0.03f;

    [Tooltip("입력 방향을 유효 방향으로 스냅할 때 허용하는 최대 각도(도). 벗어나면 제자리 무반응이다. " +
             "⚠ 30° 미만 금지 — 정사면체 직진 입력이 항상 45° 벗어난다(PlayerRollModeReceiver 참고).")]
    public float inputSnapTolerance = 46f;

    [Tooltip("굴리기 중 발밑이 비면 굴리기 모드를 끄고 기존 이동 방식으로 돌아간다(기본). " +
             "끄면 굴리기 모드를 유지한 채 몸만 놓아 수직으로 떨어진다 — 포탈 뒤에 리스폰 깃발을 둔 맵용.")]
    public bool exitRollModeOnFall = true;

    [Tooltip("발밑이 빈 채로 이 시간(초) 안에 다시 착지하면 exitRollModeOnFall과 무관하게 모드를 " +
             "유지한다 — 턱·계단을 내려갈 때 순간적으로 뜨는 것과 진짜 낙하를 구분한다. 경사면에는 " +
             "안 듣는다(텀블 모델은 완전 수평 전용).")]
    public float fallExitGrace = 0.35f;

    [Tooltip("텀블이 막혀 되감기로 돌아설 때 무엇이 막았는지 경고 로그를 찍는다(텀블 하나당 1회). " +
             "조사용이며, 끝나면 꺼라.")]
    public bool logBlocking = true;

    [Tooltip("텀블로 올라갈 수 있는 최대 단차(Unit, 스케일 1 기준). 도착 칸 바닥이 지금 칸보다 이 " +
             "값 이하로 높으면 그 높이에 맞춰 올라가고, 넘으면 제자리 무반응이다. 0 = 기능 끔. " +
             "⚠ 내려가는 쪽 허용치(0.25)와 일부러 대칭이 아니다 — 크게 잡으면 턱을 타고 기어오른다. " +
             "천장이 낮은 정사면체 전용 게이트(1.30U)를 쓰는 구간에서는 0.0753U를 넘기지 마라 " +
             "(호 전체가 이 값만큼 들려 천장 여유를 그만큼 잠식한다). 상세: PlayerRollModeReceiver 참고.")]
    public float stepUpCap = 0.05f;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    // [같은 통과 안에서의 재발화를 무시한다 — Toggle이 멱등이 아니라서 필수]
    // 리시버는 텀블을 시작할 때 isKinematic을 켜고(Grab) 끝날 때 끈다(Release). 이 몸이 트리거에
    // 겹쳐 있는 동안 그 플립이 일어나면 **PhysX가 액터를 다시 등록하면서 OnTriggerExit와
    // OnTriggerEnter를 새로 쏜다.** action이 Toggle이면 그 재발화가 그대로 모드를 꺼 버리고,
    // 꺼지면 Release가 또 플립을 만들어 ON↔OFF가 물리 스텝마다 무한 반복된다. 그동안 몸은
    // 모드가 꺼진 스텝에 평범하게 걸으므로, 화면에는 "포탈 위에서만 굴리기 없이 미끄러지다가
    // 다 지나면 굴러간다"로 보인다(2026-08-23 실측: 0.020초 간격 OFF/ON 반복).
    // 위 OnTriggerEnter의 이중 발화 방지(솔리드 콜라이더 하나만 받기)는 **한 스텝 안의 콜라이더
    // 중복**만 막는다 — 스텝을 건너뛰며 오는 이 재발화는 못 막는다. 그래서 겹침이 이어지는 동안
    // OnTriggerStay가 매 스텝 도장을 찍어 두고, 도장이 아직 살아 있는 Enter는 흘려보낸다.
    // 진짜로 문을 나갔다 다시 들어오면 그사이 Stay가 끊겨 도장이 상해 있으므로 정상 동작한다.
    private readonly Dictionary<Rigidbody, float> lastOverlapTime = new Dictionary<Rigidbody, float>();

    // 도장은 이 문에 닿아 본 플레이어 몸 수만큼만 늘어난다(씬 전체 3개) — 별도 청소가 필요 없다.
    private void OnTriggerStay(Collider other)
    {
        // Enter와 달리 솔리드/트리거를 가리지 않는다 — 도장은 "이 몸이 아직 겹쳐 있다"는 사실만
        // 남기면 되고, 어느 콜라이더가 찍든 같은 Rigidbody다.
        Rigidbody body = other.attachedRigidbody;
        if (body != null) lastOverlapTime[body] = Time.time;
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerShapeIdentity identity = other.GetComponentInParent<PlayerShapeIdentity>();
        if (identity == null) return;

        // [이중 발화 방지] 플레이어는 tag Player 콜라이더를 두 개 갖는다 — Player_Mesh(트리거)와
        // Player_Collider(솔리드). 둘 다 이 트리거에 들어오므로 OnTriggerEnter가 콜라이더당 한 번,
        // 총 두 번 불린다. 기본이 Toggle이라 켰다가 곧바로 꺼서 아무 일도 안 일어나는데, 증상은
        // "포탈이 가끔 안 먹는다"로만 보인다. AccelPad가 이 문제를 겪지 않은 건 ApplyBoost가
        // 멱등이라서다 — 토글은 멱등이 아니다.
        // 솔리드 콜라이더 하나만 받는다. 상태를 들고 다니는 중복 제거(HashSet·쿨다운)보다 조건
        // 하나가 결정론적이고 짧다.
        bool isSolid = identity.solidCollider != null ? identity.solidCollider == other : !other.isTrigger;
        if (!isSolid) return;

        // 구는 이미 자유회전으로 굴러간다. 격자 텀블을 씌워도 시각적 변화가 없고 자유도만 잃으므로
        // 아무 일도 일어나지 않는다 — 막지 않고 그냥 지나간다(PRD §4.3).
        if (identity.Kind == PlayerShapeStats.ShapeKind.Sphere) return;

        // 도장이 직전 스텝 것이면 같은 통과의 재발화다(위 주석). 여유 2.5스텝은 재발화가 Exit와
        // 같은 스텝에 오든 다음 스텝에 오든 덮으면서, 진짜 재통과(문을 나갔다 오는 데 최소 수백 ms)
        // 와는 자릿수가 달라 절대 겹치지 않는다. 흘려보내기 전에 도장을 갱신해야 연속 재발화가
        // 도장을 상하게 두지 않는다.
        Rigidbody body = other.attachedRigidbody;
        if (body != null)
        {
            float seen;
            bool samePass = lastOverlapTime.TryGetValue(body, out seen)
                            && Time.time - seen <= Time.fixedDeltaTime * 2.5f;
            lastOverlapTime[body] = Time.time;
            if (samePass) return;
        }

        PlayerRollModeReceiver receiver = identity.GetComponent<PlayerRollModeReceiver>();
        if (receiver == null) receiver = identity.gameObject.AddComponent<PlayerRollModeReceiver>();

        // 모드 전환보다 먼저 복사한다 — exitRollModeOnFall은 켜지는 그 순간부터 유효해야 한다.
        receiver.tumbleDurationScale = tumbleDurationScale;
        receiver.holdRepeatInterval = holdRepeatInterval;
        receiver.inputSnapTolerance = inputSnapTolerance;
        receiver.exitRollModeOnFall = exitRollModeOnFall;
        receiver.fallExitGrace = fallExitGrace;
        receiver.logBlocking = logBlocking;
        receiver.stepUpCap = stepUpCap;

        // [포탈 중앙 정렬] 굴리기 모드로 켜지거나 유지될 때만, 진행 방향과 수직인 폭 축(로컬 X)으로
        // 위치를 맞춘다 — 문을 치우쳐 지나가면 그 좌우 오차가 이후 몇 칸을 굴러도 그대로 보존되어
        // 빠듯한 통로에서 벽에 스치는 원인이 된다(PlayerRollModeReceiver.AlignAcrossPortal 참고).
        // 모드가 꺼지는 통과(Disable/Toggle-off)는 그 뒤 이동 방식이 기존 방식으로 돌아가므로
        // 정렬할 이유가 없다 — 평범한 보행 중 위치를 임의로 스냅하면 오히려 튄다.
        bool willBeActive = action == PortalAction.Enable ? true
                           : action == PortalAction.Disable ? false
                           : !receiver.RollModeActive;
        if (willBeActive) receiver.AlignAcrossPortal(transform.position, transform.right);

        switch (action)
        {
            case PortalAction.Enable: receiver.SetRollMode(true); break;
            case PortalAction.Disable: receiver.SetRollMode(false); break;
            default: receiver.ToggleRollMode(); break;
        }
    }

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        // 켜는 문은 초록, 끄는 문은 빨강, 토글은 노랑.
        Gizmos.color = action == PortalAction.Enable ? new Color(0.2f, 0.8f, 0.3f, 0.35f)
                     : action == PortalAction.Disable ? new Color(0.85f, 0.25f, 0.2f, 0.35f)
                     : new Color(0.9f, 0.8f, 0.2f, 0.35f);

        Bounds b = col.bounds;
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.9f);
        Gizmos.DrawWireCube(b.center, b.size);
    }
}
