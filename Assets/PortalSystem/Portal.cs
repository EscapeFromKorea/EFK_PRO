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

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
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

        PlayerRollModeReceiver receiver = identity.GetComponent<PlayerRollModeReceiver>();
        if (receiver == null) receiver = identity.gameObject.AddComponent<PlayerRollModeReceiver>();

        // 모드 전환보다 먼저 복사한다 — exitRollModeOnFall은 켜지는 그 순간부터 유효해야 한다.
        receiver.tumbleDurationScale = tumbleDurationScale;
        receiver.holdRepeatInterval = holdRepeatInterval;
        receiver.inputSnapTolerance = inputSnapTolerance;
        receiver.exitRollModeOnFall = exitRollModeOnFall;
        receiver.fallExitGrace = fallExitGrace;
        receiver.logBlocking = logBlocking;

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
