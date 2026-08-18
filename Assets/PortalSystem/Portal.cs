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
