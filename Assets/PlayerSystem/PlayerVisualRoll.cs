using UnityEngine;

/// <summary>
/// 물리 회전을 고정(RigidbodyConstraints.FreezeRotation)한 플레이어(정육면체/정사면체)의
/// 시각 메쉬(Player_MeshVisual)만 살짝 기울여 점프에 "약간의 회전감"을 준다. 콜라이더와
/// 무게중심은 축이 고정돼 점프 도달 궤적·착지가 결정론적으로 유지되므로(레벨 도달 규격표의 전제),
/// 회전은 순수 시각 효과로만 존재한다.
///
/// [연속 회전을 쓰지 않는 이유] 예전엔 공중에서 v=ωr로 계속 굴렸는데, 체공이 길면 한 바퀴 넘게
/// 누적돼 착지 시 똑바로 되감는 동작이 "확 돌아가는" 스냅으로 보였다. 그래서 누적하지 않고,
/// 이동 방향으로 각도가 제한된 목표 기울기를 향해 매 프레임 수렴만 시킨다(공중=기울임, 접지=직립).
/// 되감을 양이 작고 항상 부드럽게 수렴하므로 착지 스냅이 없다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerVisualRoll : MonoBehaviour
{
    [Tooltip("기울일 시각 메쉬(Player_MeshVisual). 물리에는 영향을 주지 않는 순수 시각 오브젝트.")]
    public Transform visual;

    [Tooltip("접지 판정(PlayerGroundContact). 공중에서만 기울이기 위해 참조한다. 비면 항상 직립.")]
    public PlayerGroundContact groundContact;

    [Tooltip("공중에서 이동 방향으로 기울이는 최대 각도(도). 점프 시 '약간 회전'하는 연출. 0이면 회전 없음.")]
    public float maxTumbleAngle = 30f;

    [Tooltip("이 수평 속력(이상)에서 최대 각도에 도달한다. 느리게 움직이면 덜 기운다.")]
    public float speedForMaxTumble = 4f;

    [Tooltip("목표 기울기/직립으로 수렴하는 속도. 높을수록 빠릿하다. 착지 복귀도 이 속도라 자연스럽게 펴진다.")]
    public float responsiveness = 10f;

    [Tooltip("이 수직 속력(|velocity.y|)을 넘을 때만 '진짜 공중'으로 보고 기울인다. 접지 판정이 " +
             "순간 false로 튀어도(정사면체는 접촉 패치가 얕아 흔히 발생) 평지에서는 y속도가 ~0이라 " +
             "기울임이 새지 않는다. 점프/낙하는 y속도가 뚜렷해 연출이 그대로 나온다.")]
    public float airborneVerticalSpeed = 0.5f;

    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (visual == null) return;

        bool grounded = groundContact != null && groundContact.IsGrounded;
        // 접지 플래그만으로 판단하면, 정사면체처럼 접촉 패치가 얕아 IsGrounded가 순간 false로
        // 튀는 도형은 평지 이동 중에도 아래 기울임 연출이 새어 나와 "면이 안 붙고 살짝 롤이 든"
        // 것처럼 보인다. 진짜 체공은 수직 속도가 뚜렷하다는 점을 함께 걸어(FreezeRotation이라
        // 물리 자체는 절대 안 기울므로 화면 기울임의 유일한 원인이 이 스크립트다), 평지에서는
        // y속도≈0이라 접지 판정이 깜빡여도 기울임이 나오지 않게 한다.
        bool airborne = !grounded && Mathf.Abs(rb.velocity.y) > airborneVerticalSpeed;

        // 목표 자세: 접지/정지면 직립(identity), 공중 이동 중이면 이동 방향으로 제한된 기울임.
        // Root/Player_Mesh 회전이 identity라 월드축=로컬축이므로 AngleAxis(월드축)를 localRotation에
        // 그대로 대입해도 정합한다.
        Quaternion target = Quaternion.identity;
        if (airborne && maxTumbleAngle > 0.01f)
        {
            Vector3 horiz = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
            float speed = horiz.magnitude;
            if (speed > 0.01f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, horiz.normalized);
                float t = speedForMaxTumble > 0.01f ? Mathf.Clamp01(speed / speedForMaxTumble) : 1f;
                // ScalingSystem으로 커지면 같은 각도라도 메쉬가 커진 만큼 모서리가 더 크게 휘둘려
                // 기울임이 과장돼 보인다. 스케일이 1보다 클 때 각도를 스케일로 나눠, 모서리의 실제
                // 처짐량(각도×크기)을 대략 일정하게 유지한다. 작아졌을 때(스케일<1)는 건드리지 않는다.
                float scale = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);
                float effectiveAngle = scale > 1f ? maxTumbleAngle / scale : maxTumbleAngle;
                target = Quaternion.AngleAxis(effectiveAngle * t, axis);
            }
        }

        // 프레임레이트 무관 지수 수렴. 누적이 아니라 목표를 향한 수렴이라 착지 시 되감을 양이 작다.
        float k = 1f - Mathf.Exp(-responsiveness * Time.deltaTime);
        visual.localRotation = Quaternion.Slerp(visual.localRotation, target, k);
    }
}
