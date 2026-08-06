using UnityEngine;

/// <summary>
/// 투석기의 당김 줄을 걸 수 있는 고정 고리(앵커) 표식이다. `DreamThreadSystem/ThreadAnchor`와 같은
/// 성격의 물리 없는 순수 위치 마커로, `CatapultLoadController`가 C 입력 시 자신의 connectRange 안에
/// 조작 중인 플레이어가 있는지만 거리로 판정한다.
///
/// 콜라이더가 없는 이유: 연결은 거리 판정이라 물리 접촉이 필요 없고, 공중에 뜬 고리에 플레이어가
/// 물리적으로 부딪히면 안 되기 때문이다(ThreadAnchor와 동일한 이유).
/// `ThreadAnchor`와 달리 스윙 물리가 없어 `lockToSidePlane` 같은 필드는 없다 — 이 앵커는
/// "여기서 C를 누르면 걸린다"는 사실 하나만 안다. **9차 개편(2026-08-05)까지는 당김 진행(거리 비례
/// 팔 당김)도 `CatapultLoadController`가 매 프레임 이 앵커와의 거리로 직접 계산했지만, 9차 개편으로
/// 그 계산이 마우스 휠 누적값으로 완전히 대체됐다** — 이 앵커가 아는 건 여전히 "연결을 처음 걸
/// 수 있는 거리"(`connectRange`)뿐이고, 그 이후의 당김 진행에는 더 이상 관여하지 않는다
/// (`CatapultLoadController.cs` 상단 "9차 개편" 주석 참고).
/// </summary>
public class CatapultPullAnchor : MonoBehaviour
{
    [Header("연결 범위")]
    [Tooltip("이 앵커에 C로 연결할 수 있는 최대 거리(플레이어 Root 중심 기준, Unit) — '처음 걸 수 " +
             "있는' 거리일 뿐, 9차 개편(2026-08-05)으로 연결 이후의 당김 진행에는 관여하지 않는다 " +
             "(그건 이제 마우스 휠 누적값이 전담한다, CatapultLoadController.cs 참고). " +
             "6차 개편(투석기 3배 확대)에서 3→9로 스케일했다 — 이건 구조물 위의 공간적 거리이기 " +
             "때문이다(발사 속도/피치각과는 다른 성격, CatapultArm.cs 참고).")]
    public float connectRange = 9f;

    // 씬 뷰에서 앵커 위치(작은 마름모 대용 구)와 연결 가능 범위를 보여준다. ThreadAnchor(청록)와
    // 구분되는 색으로, 어느 기믹의 앵커인지 씬에서 한눈에 구분한다.
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 1f);
        Gizmos.DrawSphere(transform.position, 0.15f);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, connectRange);
    }
}
