using UnityEngine;

/// <summary>
/// 플레이어 오브젝트의 수평 이동을 담당한다.
/// IsControlled가 false인 동안(Tab으로 다른 오브젝트를 조작 중일 때)에는
/// 입력을 무시하고 수평 velocity를 0으로 만들어 제자리에 멈춘다.
/// 중력/외력(점프, 부스트 등)은 그대로 물리 시뮬레이션에 맡긴다.
///
/// [구르는 연출: 구/정육면체/정사면체 모두 같은 공식을 쓴다]
/// 접지 중 매 프레임 각속도 = Cross(Vector3.up, 이동 velocity) / rollRadius를 무게중심 기준으로
/// 하드 설정해 "구르는 것처럼" 보이게 한다(v=ωr, 매끄럽게 굴러갈 때 성립하는 공식). 예전에는
/// 정육면체/정사면체만 별도의 "모서리 피벗 텀블링(RollMode.EdgeTumble)" 방식을 썼었다 — 다면체는
/// 바닥에 "면"으로 완전히 밀착하기 때문에, 이 공식을 무게중심 기준으로 그대로 적용하면 그 면의
/// 양 끝(코너)이 동시에 반대 방향(하나는 파고들고 하나는 뜨려)으로 움직이려 해 PhysX 접촉 솔버와
/// 매 스텝 충돌해 회전이 거의 안 보이거나 씹혔었다.
///
/// 하지만 사용자 피드백으로 "점프대 위에서만이 아니라 평소 이동 중에도 항상 매끄럽게 계속
/// 회전"하는 연출이 필요하다는 게 확인되어, 스텝형 텀블링을 걷어내고 다시 세 도형 모두 이
/// 연속 회전 공식 하나로 통일했다. 대신 "완전히 평평한 면 접촉 + 순간 각속도 강제"가 만드는
/// 접촉 솔버 충돌 자체는 이 스크립트의 로직이 아니라, 접촉 지오메트리와 Rigidbody 물리 설정
/// 쪽에서 완화한다(구체적인 내용은 PlayerObjectMenuItem.cs 상단 주석 참고):
/// - 정육면체: BoxCollider(정확한 모양) 자체는 그대로 두되, 저마찰 PhysicMaterial로 접지 중
///   접촉면을 따라 생기는 마찰력을 줄여 충돌 강도를 낮춘다.
/// - 정사면체: Unity에 대응하는 기본 Primitive Collider가 없다. 꼭짓점 4개 각각에 작은
///   SphereCollider를 배치한 컴파운드로 근사해, 뾰족한 점 대신 둥근 표면으로 접촉하게 한다
///   (구는 어느 방향으로 회전해도 중심-표면 거리가 일정해 침투가 완만해진다).
/// - 공통: Rigidbody.maxAngularVelocity를 기본 캡(7 rad/s)보다 넉넉히 올리고(v=ωr로 나온
///   각속도가 잘리지 않도록), interpolation과 solver iteration을 높여 시각적 떨림/솔버 오차를
///   줄인다.
///
/// [솔직한 한계] 이 조치들은 "다면체가 평평한 면으로 접지한 채 순간 각속도를 강제하면 다중
/// 접촉점이 서로 충돌한다"는 구조적 문제 자체를 없애지는 못한다(구가 아닌 이상 "완벽하게
/// 매끄러운 회전"은 물리적으로 보장할 수 없다) — 접촉을 부드럽게 만들고 솔버가 덜 튀도록
/// 완화할 뿐이다. 실제로 씬에서 확인해가며 rollRadius/PhysicMaterial/solver 값을 조정할 것.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerMover : MonoBehaviour
{
    public float moveSpeed = 5f;

    [Header("구르는 회전 연출 (접지 중에만 적용)")]
    [Tooltip("굴렀을 때의 반경으로 취급할 값(대략 오브젝트 반지름/절반 크기). " +
             "매 프레임 각속도 = Cross(Vector3.up, 이동 velocity) / rollRadius로 하드 설정한다. " +
             "구/정육면체/정사면체 모두 이 공식 하나를 공유한다(자세한 이유는 클래스 상단 주석 참고).")]
    public float rollRadius = 0.5f;

    [Header("자체 접지 판정 (PlayerShapeController가 없을 때만 사용)")]
    [Tooltip("바닥 감지 Raycast 거리")]
    public float groundCheckDistance = 0.15f;
    [Tooltip("바닥으로 인정할 레이어 마스크")]
    public LayerMask groundLayer = ~0;

    /// <summary>PlayerControlSwitcher가 세팅. 스위처가 씬에 없으면 기본값 true로 항상 조작 가능.</summary>
    [HideInInspector] public bool IsControlled = true;

    private Rigidbody rb;
    private PlayerShapeController shapeController;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        shapeController = GetComponent<PlayerShapeController>();

        if (shapeController == null)
            groundLayer &= ~(1 << gameObject.layer);
    }

    void OnEnable()
    {
        PlayerControlSwitcher.RegisterPlayer(this);
    }

    void OnDisable()
    {
        PlayerControlSwitcher.UnregisterPlayer(this);
    }

    void FixedUpdate()
    {
        if (!IsControlled)
        {
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
            return;
        }

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 move = new Vector3(h, 0f, v) * moveSpeed;

        if (!IsGrounded())
        {
            rb.velocity = new Vector3(move.x, rb.velocity.y, move.z);
            // 공중에서는 회전을 건드리지 않는다 — 점프대 등에서 자유 회전(통통 튀는 불규칙함)을
            // 그대로 보존한다.
            return;
        }

        rb.velocity = new Vector3(move.x, rb.velocity.y, move.z);
        if (rollRadius > 0.0001f)
            rb.angularVelocity = Vector3.Cross(Vector3.up, move) / rollRadius;
    }

    private bool IsGrounded()
    {
        if (shapeController != null)
            return shapeController.IsGrounded();

        Vector3 rayOrigin = transform.position + Vector3.up * 0.05f;
        return Physics.Raycast(rayOrigin, Vector3.down, groundCheckDistance, groundLayer, QueryTriggerInteraction.Ignore);
    }
}
