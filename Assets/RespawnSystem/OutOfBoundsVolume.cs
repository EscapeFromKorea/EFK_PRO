using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 장외 판정 볼륨 — 이 안에 머무르면 킬 라인 아래에 있는 것과 똑같이 장외로 취급된다(체류 시간과
/// 타이머를 킬 라인과 공유한다). 씬에 박스를 놓고 크기로 범위를 정한다.
///
/// <b>다만 복귀 연출은 킬 라인과 다르다 — 이쪽은 페이드다</b>(2026-07-31). 킬 라인은 "떨어져서
/// 사라졌다"라 하늘에서 다시 떨어지는 게 맞지만, 이 볼륨이 덮는 것은 맵 옆으로 튕겨나가 같은 높이에
/// 뜬 경우·지형 틈에 낀 경우처럼 <b>낙하와 무관한 상황</b>이다. 그런 자리에서 낙하 연출을 쓰면
/// 사유와 화면이 어긋나므로, 그 자리에서 흐려져 사라지고 체크포인트 바닥에 나타난다.
/// 판정은 컨트롤러의 IsOutOfBounds가 어느 쪽이 잡았는지(byVolume)로 갈라 준다.
///
/// [킬 라인을 대체하지 않고 <b>더한다</b> — 중요]
/// 장외 판정의 기본은 여전히 RespawnController의 killY(높이 한 줄)다. 볼륨만 쓰면 맵이 넓어질
/// 때마다 박스를 늘려야 하고, <b>빠뜨린 빈틈에 떨어진 플레이어는 영영 리스폰되지 않는다</b> —
/// 구멍 난 안전망은 없는 것보다 나쁘다. 킬 라인이 맵 아래를 무조건 덮어 그 사고를 원천 차단하고,
/// 이 볼륨은 킬 라인이 못 잡는 곳(맵 <b>옆으로</b> 튕겨나가 같은 높이에 떠 있는 경우, 지형 틈에
/// 낀 채 안 떨어지는 경우)을 골라서 덮는다. 둘 중 하나만 걸려도 장외다.
///
/// [트리거 이벤트를 쓰지 않는다 — 컨트롤러가 좌표로 물어본다]
/// 콜라이더는 isTrigger여야 하지만(안 그러면 플레이어를 물리적으로 막는다) OnTriggerStay/Exit은
/// 쓰지 않는다. 이유가 둘이다: (1) 플레이어는 콜라이더가 둘(Player_Mesh/Player_Collider)이라
/// Enter/Exit이 중복으로 들어와, SlowZone이 ClosestPoint 가드를 따로 넣어야 했던 그 비용을 다시
/// 치른다. (2) <b>잠든 Rigidbody에는 OnTriggerStay가 오지 않는다</b> — 지형에 걸려 정지·sleep한
/// 플레이어에게 판정이 멈추는데, 그건 "끼었을 때 구해준다"는 이 시스템의 존재 이유와 정면으로
/// 충돌한다. 컨트롤러가 이미 매 프레임 플레이어 좌표를 훑고 있으므로 거기서 Contains를 물어보면
/// 두 문제가 통째로 사라지고, 체류 타이머도 킬 라인과 같은 것을 그대로 쓴다.
///
/// 콜라이더는 Box/Sphere/Capsule이나 convex MeshCollider여야 한다(ClosestPoint 제약 — 비convex
/// 메쉬는 Unity가 지원하지 않는다). 회전 배치해도 정확하다(월드 AABB인 bounds와 달리).
/// </summary>
[RequireComponent(typeof(Collider))]
public class OutOfBoundsVolume : MonoBehaviour
{
    // 씬에 볼륨이 하나도 없으면 이 목록이 비어 있어 컨트롤러 쪽 비용도 0이다.
    private static readonly List<OutOfBoundsVolume> active = new List<OutOfBoundsVolume>();

    private Collider volumeCollider;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        volumeCollider = GetComponent<Collider>();
        if (!volumeCollider.isTrigger)
            Debug.LogError("[OutOfBoundsVolume] Collider가 Trigger가 아니다 — 플레이어가 여기에 " +
                           "부딪혀 막힌다. isTrigger를 켜라.", this);
    }

    // 목록 관리를 OnEnable/OnDisable에 두면 파괴·비활성화된 볼륨이 자동으로 빠져, 컨트롤러가
    // 죽은 참조를 걸러낼 필요가 없다.
    private void OnEnable() => active.Add(this);
    private void OnDisable() => active.Remove(this);

    /// <summary>이 지점이 어떤 장외 볼륨 안에 있는가. 컨트롤러가 플레이어마다 매 프레임 물어본다.</summary>
    public static bool AnyContains(Vector3 point)
    {
        for (int i = 0; i < active.Count; i++)
            if (active[i].Contains(point)) return true;
        return false;
    }

    private bool Contains(Vector3 point)
    {
        if (volumeCollider == null) return false;
        // ClosestPoint는 안에 있는 점을 그대로 돌려준다 — 회전한 콜라이더에서도 정확하다.
        return volumeCollider.ClosestPoint(point) == point;
    }

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        // 킬 라인 평면(붉은색)과 구분되게 보라 계열로 칠한다 — 씬에 둘 다 있을 때 어느 쪽이
        // 잡은 판정인지 헷갈리지 않도록. (bounds는 월드 AABB라 회전 배치하면 기즈모가 실제 볼륨보다
        // 커 보인다. 판정은 ClosestPoint라 정확하다 — 표시만의 한계다.)
        Gizmos.color = new Color(0.7f, 0.3f, 1f, 0.12f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.color = new Color(0.7f, 0.3f, 1f, 0.7f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
