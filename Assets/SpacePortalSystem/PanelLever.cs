using UnityEngine;

/// <summary>
/// <see cref="MovablePortalPanel"/>의 레버 드라이버(§13-8 중 레버만 구현 — 전력 공급은 범위 밖으로
/// 남겨둔다). `DoorSystem/LeverHead`와 코드를 공유하지 않는다 — 그쪽은 "각도 → 문 목표 위치를 매
/// 프레임 즉시 반영"이라 손을 떼도 <c>returnDelay</c>만큼 각도가 남아 문도 그만큼 열린 채 유지되지만,
/// 이 패널은 §4가 "입력이 끊기면 관성 없이 즉시 멈춘다"를 요구한다 — 복귀 지연이 있으면 레버에서
/// 손을 뗀 뒤에도 패널이 계속 움직여 요구사항을 어긴다. 그래서 접촉이 끊기는 즉시(지연 없이)
/// 구동을 멈추는 별도의 단순한 구현을 쓴다. 레버 팔의 시각적 복귀도 지연 없이 바로 중립으로
/// 돌아간다 — "밀고 있는 동안만 켜진 스위치"에 가깝다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PanelLever : MonoBehaviour
{
    [Tooltip("이 레버가 구동할 패널.")]
    public MovablePortalPanel panel;
    [Tooltip("레버가 밀린 방향으로 매 FixedUpdate 요청하는 진행도 변화량. 패널의 speedLimit이 항상 " +
             "이보다 작게 클램프하므로(MovablePortalPanel.ApplyDrive 계약) 크게 잡아도 안전하다.")]
    public float driveMagnitude = 10f;
    [Tooltip("밀리는 방향 판정을 뒤집는다 — 레버를 만든 방향에 따라 반대로 동작하면 코드 대신 이 " +
             "체크박스로 바로잡는다.")]
    public bool invertDirection = false;

    [Header("시각(선택, 비워두면 로직만 동작)")]
    [Tooltip("레버 팔. 밀리는 동안 maxArmAngle까지 돌고, 손을 떼면 지연 없이 바로 중립으로 돌아간다.")]
    public Transform leverArm;
    public float maxArmAngle = 30f;
    public float armSpeed = 360f;

    private float pushDirection; // -1, 0, 1
    private float armAngle;

    private void OnCollisionEnter(Collision collision) => UpdatePush(collision);
    private void OnCollisionStay(Collision collision) => UpdatePush(collision);

    private void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player")) pushDirection = 0f;
    }

    // 충돌 지점의 표면 노멀(어느 면에 부딪혔는지)로 방향을 판정하면, 같은 "미는 의도"라도 레버
    // 기둥의 어느 면/모서리에 닿았는지에 따라 결과가 흔들린다(2026-09-11 플레이테스트에서 실측
    // 확인 — 축도 애초에 안 맞았다: 팔은 로컬 Y 회전으로 X쪽으로 휘는데 비교는 로컬 Z를 썼다).
    // 대신 "플레이어가 지금 기둥의 어느 쪽(로컬 X 기준 좌/우)에 서 있는가"로 판정한다 — 정확히
    // 어느 면이 닿았는지와 무관하게, 어느 쪽에서 미는지만 일관되게 반영된다.
    private void UpdatePush(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;
        Vector3 playerPos = collision.rigidbody != null ? collision.rigidbody.position : collision.transform.position;
        float localX = transform.InverseTransformPoint(playerPos).x;
        pushDirection = (localX >= 0f ? 1f : -1f) * (invertDirection ? -1f : 1f);
    }

    private void FixedUpdate()
    {
        if (pushDirection != 0f && panel != null) panel.ApplyDrive(pushDirection * driveMagnitude);

        if (leverArm != null)
        {
            float target = pushDirection * maxArmAngle;
            armAngle = Mathf.MoveTowards(armAngle, target, armSpeed * Time.fixedDeltaTime);
            leverArm.localRotation = Quaternion.Euler(0f, armAngle, 0f);
        }
    }
}
