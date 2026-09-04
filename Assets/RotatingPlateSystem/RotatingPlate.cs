using UnityEngine;

/// <summary>
/// 자유 회전 장난감 판. 플레이어가 밀거나 부딪혀 임의 각도로 돌리고, 원하는 각도에서 멈춰 지형(경사로/
/// 다리/임시 벽/발사대)으로 쓰는 장치.
///
/// [왜 HingeJoint + 모터·스프링 없음인가]
/// - 스프링을 쓰면 목표 각도로 되돌아가 "임의 각도 유지"가 깨진다 → useSpring = false.
/// - 모터를 쓰면 강제 회전이라 "플레이어가 밀어서 돈다"가 죽는다 → useMotor = false.
/// - 그래서 판은 그냥 다이나믹 Rigidbody이고, 회전은 전부 PhysX가 처리한다:
///   · 플레이어 밀기/충돌 → 접촉 토크로 자연 회전 (코드 없음)
///   · 정사면체 쐐기 / 딱딱 블록 / 벨크로 스티커 → 콜라이더가 물리로 막음 (코드 없음)
///   · 회전 저항 = Rigidbody.angularDrag / 최대 각속도 = Rigidbody.maxAngularVelocity
///   · 회전 범위 = HingeJoint.limits (limit은 각도 제한일 뿐 스냅이 아니다)
///
/// [태엽 축 연동 — 이번 PR은 진입점만]
/// 태엽 동력 회전은 별도 `태엽 축` 기믹(미착수) 담당이다. 여기서는 외부 시스템이 매 FixedUpdate
/// <see cref="ApplyDriveTorque"/>로 힌지 축 토크를 주입할 수 있는 창구와, 축 방향을 알려주는
/// <see cref="HingeAxisWorld"/>만 노출한다. 태엽 축이 나오면 그쪽에서 이 두 개만 호출하면 된다.
///
/// [와이어 앵커] 자식으로 ThreadAnchor(DreamThreadSystem) 마커를 두면 실타래 스윙 지점이 된다.
/// DreamThreadController의 진자 피벗은 연결 시점의 월드 좌표 스냅샷이라, 연결 후 판이 돌아도
/// 물리가 터지지 않는다(피벗이 판을 따라 도는 건 후속 과제).
///
/// [규약] 전역 상태(Physics.*) 미변경(MP-01). 모든 수치 Inspector 노출. PlayerSystem·씬 무수정.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(HingeJoint))]
[DisallowMultipleComponent]
public class RotatingPlate : MonoBehaviour
{
    [Header("회전 축 (로컬)")]
    [Tooltip("판이 도는 축(이 오브젝트 로컬 기준). HingeJoint.axis에 그대로 넣는다. " +
             "경사로처럼 위아래로 젖히려면 (1,0,0), 회전문처럼 수평 회전이면 (0,1,0).")]
    public Vector3 rotationAxis = Vector3.right;

    [Header("회전 범위 (스냅 아님, 각도 제한만)")]
    [Tooltip("켜면 HingeJoint.limits로 회전 범위를 minAngle~maxAngle로 제한한다. 스프링을 쓰지 않으므로 " +
             "범위 안에서는 어떤 각도든 그대로 유지된다(특정 각도로 끌리지 않는다). 끄면 제한 없이 자유 회전.")]
    public bool useLimits = true;
    [Tooltip("[useLimits] 회전 하한(도). 씬에 놓인 판의 현재 방향이 0도 기준이다.")]
    public float minAngle = -75f;
    [Tooltip("[useLimits] 회전 상한(도).")]
    public float maxAngle = 75f;

    [Header("회전 물리")]
    [Tooltip("회전 저항. Rigidbody.angularDrag에 넣는다. 높을수록 밀었을 때 금방 멈춘다. 0이면 관성으로 계속 돈다.")]
    public float angularResistance = 1.5f;
    [Tooltip("최대 각속도(도/초). Rigidbody.maxAngularVelocity에 넣는다(내부적으로 라디안 변환). " +
             "Unity 기본 상한은 약 401도/초라, 크게 돌리려면 올려야 한다.")]
    public float maxAngularSpeedDeg = 360f;
    [Tooltip("판이 중력을 받는가. 끄면(기본) 밀어 놓은 각도에 그대로 머문다. 켜면 무게로 아래쪽 각도/제한까지 처진다.")]
    public bool plateUseGravity = false;

    [Header("태엽 축 연동 (외부 토크 주입 창구)")]
    [Tooltip("ApplyDriveTorque로 들어온 값에 곱하는 배율. 태엽 축 쪽 수치를 안 건드리고 여기서 세기를 맞춘다. " +
             "토크는 ForceMode.Acceleration으로 인가한다(질량/관성 무관, 저장소 관례) — 값은 각가속(rad/s²) 느낌.")]
    public float driveTorqueScale = 1f;

    private Rigidbody rb;
    private HingeJoint hinge;
    private float pendingDriveTorque;   // 이번 FixedUpdate에 인가할 외부 토크 합(인가 후 0으로 리셋).

    /// <summary>힌지 회전축의 월드 방향(정규화). 태엽 축 등 외부 시스템이 토크 방향을 잡을 때 쓴다.</summary>
    public Vector3 HingeAxisWorld =>
        transform.TransformDirection(rotationAxis.sqrMagnitude > 1e-6f ? rotationAxis.normalized : Vector3.right);

    /// <summary>외부 구동 시스템(태엽 축 등)이 매 FixedUpdate 호출해 힌지 축 토크를 주입한다.
    /// 양수 = HingeAxisWorld 방향. 이번 물리 스텝에 driveTorqueScale을 곱해 인가하고 자동으로 비운다.</summary>
    public void ApplyDriveTorque(float torqueNm)
    {
        pendingDriveTorque += torqueNm;
    }

    private void Reset()
    {
        // 인스펙터에서 직접 붙였을 때도 바로 그럴듯하게 동작하도록 기본 배선.
        Rigidbody body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.angularDrag = angularResistance;

        HingeJoint hj = GetComponent<HingeJoint>();
        hj.axis = rotationAxis;
        hj.connectedBody = null;      // 월드에 고정된 축
        hj.useMotor = false;
        hj.useSpring = false;
        hj.useLimits = useLimits;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        hinge = GetComponent<HingeJoint>();
        ApplyConfig();
    }

    private void OnValidate()
    {
        // 에디터에서 값을 바꾸면 바로 반영(플레이 중이 아니어도).
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (hinge == null) hinge = GetComponent<HingeJoint>();
        if (rb != null && hinge != null) ApplyConfig();
    }

    private void ApplyConfig()
    {
        rb.useGravity = plateUseGravity;
        rb.angularDrag = Mathf.Max(0f, angularResistance);
        rb.maxAngularVelocity = Mathf.Max(0.01f, maxAngularSpeedDeg * Mathf.Deg2Rad);

        hinge.axis = rotationAxis.sqrMagnitude > 1e-6f ? rotationAxis : Vector3.right;
        hinge.connectedBody = null;
        hinge.useMotor = false;
        hinge.useSpring = false;
        hinge.useLimits = useLimits;
        if (useLimits)
        {
            hinge.limits = new JointLimits
            {
                min = Mathf.Min(minAngle, maxAngle),
                max = Mathf.Max(minAngle, maxAngle),
                bounciness = 0f,
                bounceMinVelocity = 0f
            };
        }
    }

    private void FixedUpdate()
    {
        if (Mathf.Abs(pendingDriveTorque) > 1e-6f)
        {
            rb.AddTorque(HingeAxisWorld * (pendingDriveTorque * driveTorqueScale), ForceMode.Acceleration);
            pendingDriveTorque = 0f;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 axis = HingeAxisWorld;
        Vector3 c = transform.position;
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
        Gizmos.DrawLine(c - axis * 1.5f, c + axis * 1.5f);
        Gizmos.DrawWireSphere(c, 0.12f);
    }
}
