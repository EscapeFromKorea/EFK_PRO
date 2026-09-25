using UnityEngine;

/// <summary>
/// 팀 전체 도착 판정 볼륨 — 이 안에 씬의 모든 PlayerMover가 들어와 있는지를 좌표로 물어보는 범용
/// 재사용 컴포넌트다(특정 기믹 전용이 아니다). "조건이 충족되면 장애물을 치운다/통과를 인정한다"
/// 계열을 담당하는 DoorSystem에 둔 것도 같은 이유 — 이 컴포넌트 자체는 아무것도 치우지 않고, 호출자
/// (예: PathChaserController)가 <see cref="AllPlayersInside"/> 결과를 팀 종료 조건의 한 축으로 쓴다.
///
/// [트리거 이벤트를 쓰지 않는다 — 호출자가 좌표로 물어본다, RespawnSystem/OutOfBoundsVolume과 동일
/// 패턴] OnTriggerEnter/Stay로 "누가 들어왔는지"를 추적하면 (1) 플레이어 콜라이더가 둘
/// (Player_Mesh/Player_Collider)이라 중복 카운트를 따로 걸러야 하고, (2) 잠든 Rigidbody에는
/// OnTriggerStay가 오지 않아 판정이 멈춘다. 매 프레임 ClosestPoint로 직접 물어보면 두 문제가 모두
/// 사라진다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TeamExitZone : MonoBehaviour
{
    private Collider zoneCollider;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        zoneCollider = GetComponent<Collider>();
        if (!zoneCollider.isTrigger)
            Debug.LogError("[TeamExitZone] Collider가 Trigger가 아니다 — 플레이어가 여기에 부딪혀 " +
                           "막힌다. isTrigger를 켜라.", this);
    }

    /// <summary>씬에 있는 모든 PlayerMover가 이 구역 안에 있는가. PlayerMover가 하나도 없으면
    /// false다(아직 아무도 없는데 "전원 도착"은 성립하지 않는다).</summary>
    public bool AllPlayersInside()
    {
        PlayerMover[] movers = Object.FindObjectsOfType<PlayerMover>();
        if (movers.Length == 0) return false;

        foreach (PlayerMover mover in movers)
            if (!Contains(mover.transform.position)) return false;
        return true;
    }

    private bool Contains(Vector3 point)
    {
        if (zoneCollider == null) return false;
        return zoneCollider.ClosestPoint(point) == point;
    }

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.12f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.7f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
