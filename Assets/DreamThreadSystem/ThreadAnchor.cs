using UnityEngine;

/// <summary>
/// 실타래를 걸 수 있는 고정 고리(앵커) 표식이다. 이 컴포넌트는 물리를 갖지 않는 순수 위치 마커로,
/// DreamThreadController가 F 입력 시 씬의 모든 ThreadAnchor 중 조작 중인 플레이어로부터
/// connectRange 안에 있는 "가장 가까운" 앵커를 골라 그 위치를 진자의 회전축으로 삼는다.
///
/// 왜 컴포넌트 하나로 분리했나: 앵커는 레벨 디자이너가 인스펙터에서 자유롭게 배치/이동해야 하고
/// (Phase 2의 세모 핀이 만드는 런타임 앵커도 같은 타입을 재사용할 수 있도록), 연결 판정 로직은
/// 전적으로 컨트롤러가 갖는다. 앵커 자신은 "여기 걸 수 있다 + 이만큼 거리까지 잡힌다"만 안다.
/// 콜라이더/트리거가 없는 이유: 연결은 거리 판정이라 물리 접촉이 필요 없고, 공중에 뜬 고리에
/// 플레이어가 물리적으로 부딪히면 안 되기 때문이다.
/// </summary>
public class ThreadAnchor : MonoBehaviour
{
    [Header("연결 범위")]
    [Tooltip("이 앵커에 F로 연결할 수 있는 최대 거리(플레이어 Root 중심 기준, Unit). " +
             "컨트롤러는 범위 안에 든 앵커들 중 가장 가까운 것을 고른다.")]
    public float connectRange = 4f;

    // 씬 뷰에서 앵커 위치(작은 실구)와 연결 가능 범위(와이어 구)를 보여준다. 레벨 배치 시
    // 플레이어가 어디까지 다가가야 걸리는지 눈으로 확인하기 위함.
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 1f);
        Gizmos.DrawSphere(transform.position, 0.15f);
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, connectRange);
    }
}
