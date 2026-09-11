using UnityEngine;

/// <summary>
/// "가변 전이 패널" 마커. 이 컴포넌트가 붙은 표면에만 <see cref="SpacePortal"/>을 설치할 수 있다
/// (Tag/Layer가 아니라 컴포넌트 존재로 판별 — 저장소 관례, PRD §4.4). 아무 표면 오브젝트에나
/// 붙일 수 있고, RailCart/Lift 같은 움직이는 발판에도 처음부터 허용된다 — <see cref="SpacePortal"/>은
/// 설치 시 자신을 이 표면의 자식 Transform으로 붙여 부모-자식 관계로 자동 추적한다(별도 속도
/// 동기화 로직 불필요).
///
/// [좌표 규약 — 반드시 지킬 것] 이 오브젝트의 <b>localScale은 항상 (1,1,1)</b>이어야 한다(RailCart
/// 루트가 이미 쓰는 규약과 동일 — 전단 왜곡 회피). 패널의 실제 가로/세로 치수는
/// <see cref="BoxCollider.size"/>로만 낸다. 바깥 법선은 로컬 +Z(transform.forward), 세로축은
/// 로컬 +Y(transform.up), 가로축은 로컬 +X(transform.right)다 — 패널 오브젝트 자체를 회전시켜
/// 바닥/벽/천장 어디에든 붙일 수 있다.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class PortalSurface : MonoBehaviour
{
    private BoxCollider box;
    public BoxCollider Box => box != null ? box : (box = GetComponent<BoxCollider>());

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
    }

    /// <summary>패널 로컬 2D(가로=X, 세로=Y) 반치수(Unit). localScale=(1,1,1) 전제 하에 월드 단위와
    /// 그대로 일치한다.</summary>
    public Vector2 HalfExtents()
    {
        Vector3 s = Box.size;
        return new Vector2(s.x, s.y) * 0.5f;
    }

    /// <summary>포탈이 이 패널을 점유 중이면 플레이어/<see cref="PortalTraversable"/> 대상과의
    /// 물리 충돌만 끈다(콜라이더 자체는 그대로 켜둔다). 패널은 솔리드 장애물이라 포탈이 설치돼도
    /// 플레이어가 그 자리를 실제로 통과하지 못하고 벽처럼 막혔다(2026-09-11 실측 확인 — 텔레포트는
    /// body가 포탈 로컬 Z=0을 실제로 넘어야 발동하는데 솔리드 패널이 그 지점까지 진입 자체를
    /// 막았다). <c>Box.enabled = false</c>로 콜라이더 자체를 끄면 조준 레이캐스트도 이 패널을
    /// 못 잡아 "이미 설치된 포탈을 다시 조준해 옮긴다"(PRD §3.2)가 깨지므로,
    /// <see cref="Physics.IgnoreCollision"/>으로 통과 대상만 선택적으로 뚫는다. 점유가 풀리면
    /// 다시 막는다.</summary>
    public void RefreshOccupancy()
    {
        bool occupied = (SpacePortal.Orange != null && SpacePortal.Orange.transform.parent == transform) ||
                         (SpacePortal.Blue != null && SpacePortal.Blue.transform.parent == transform);

        foreach (PlayerShapeIdentity id in FindObjectsOfType<PlayerShapeIdentity>())
            if (id.solidCollider != null) Physics.IgnoreCollision(Box, id.solidCollider, occupied);

        foreach (PortalTraversable t in FindObjectsOfType<PortalTraversable>())
            foreach (Collider col in t.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(Box, col, occupied);

        // 패널이 실제 벽 오브젝트(별도 GameObject) 앞/속에 겹쳐 배치된 경우, 패널 콜라이더만
        // 뚫어도 그 벽이 여전히 막아 "간헐적으로 진입이 걸리는" 느낌이 났다(2026-09-11 실측 —
        // 걸어 들어갈 때마다가 아니라 패널과 겹친 벽 콜라이더에 부딪히는 각도/타이밍에서만
        // 막혀 비결정적으로 보였다). 패널 부피와 실제로 겹치는 모든 솔리드 콜라이더를 찾아
        // 같이 뚫는다 — 위 두 루프와 겹칠 수 있지만 IgnoreCollision은 멱등이라 무해하다.
        Vector3 worldCenter = transform.TransformPoint(Box.center);
        Vector3 worldHalfExtents = Vector3.Scale(Box.size, transform.lossyScale) * 0.5f;
        foreach (Collider h in Physics.OverlapBox(worldCenter, worldHalfExtents, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h == Box) continue;
            Physics.IgnoreCollision(Box, h, occupied);
        }

        // 패널의 시각 메쉬(PortalSurface_Visual)와 포탈 자신의 쿼드가 거의 같은 평면에 겹쳐
        // z-fighting/이중 렌더로 보였다(SpacePortal_BugImage, 2026-09-11). 포탈이 점유 중이면
        // 패널 시각 메쉬를 꺼서 포탈 쿼드만 보이게 한다 — 콜라이더는 그대로라 조준 판정엔 영향 없다.
        MeshRenderer visual = GetComponentInChildren<MeshRenderer>();
        if (visual != null) visual.enabled = !occupied;
    }

    private void OnDrawGizmos()
    {
        BoxCollider b = Box;
        if (b == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.25f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
        Gizmos.DrawWireCube(b.center, b.size);

        // 바깥 법선(로컬 +Z) 화살표 — 조준 판정이 이 방향을 "패널 밖"으로 본다.
        Gizmos.color = Color.cyan;
        Vector3 from = b.center + new Vector3(0f, 0f, b.size.z * 0.5f);
        Vector3 to = from + Vector3.forward * 0.6f;
        Gizmos.DrawLine(from, to);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
