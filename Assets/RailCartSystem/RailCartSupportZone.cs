using UnityEngine;

/// <summary>
/// 카트 전용 트리거 교량 — 이 볼륨 안에서 레일 위(OnRail)에 있는 카트만 중력을 상쇄해, 받침 없는
/// 구간(절벽)을 레일 복원력만으로 건너게 한다. 플레이어는 트리거라 그대로 떨어진다(카트로만 건넌다).
///
/// [왜 RailCart를 고치지 않고 별도 볼륨인가] 레일은 콜라이더 없는 그림이고 카트는 레일 복원력
/// (railRestoreForce 80N/m)으로만 붙어 있어, 질량 40kg이면 받침 없는 구간에서 5m 가까이 처진다.
/// 중력 상쇄를 RailCart에 넣으면 모든 레일이 공중 부양 레일이 되므로, "여기서만"을 볼륨으로 표현한다.
/// 탈선(Derailed)한 카트는 상쇄하지 않는다 — 탈선하면 절벽으로 떨어지는 게 맞다.
///
/// [트리거 이벤트를 쓰지 않는다 — 좌표로 물어본다, TeamExitZone/OutOfBoundsVolume과 같은 패턴]
/// 카트는 바닥판+4벽+바퀴 컴파운드 콜라이더라 OnTriggerStay가 콜라이더 수만큼 중복 호출된다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RailCartSupportZone : MonoBehaviour
{
    private Collider zone;
    private RailCart[] carts;
    private Rigidbody[] bodies;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Start()
    {
        zone = GetComponent<Collider>();
        carts = FindObjectsOfType<RailCart>();
        bodies = new Rigidbody[carts.Length];
        for (int i = 0; i < carts.Length; i++) bodies[i] = carts[i].GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        for (int i = 0; i < carts.Length; i++)
        {
            if (carts[i] == null || !carts[i].IsOnRail || !bodies[i].useGravity) continue;
            Vector3 p = bodies[i].position;
            if ((zone.ClosestPoint(p) - p).sqrMagnitude > 1e-6f) continue;
            bodies[i].AddForce(-Physics.gravity, ForceMode.Acceleration);
        }
    }
}
