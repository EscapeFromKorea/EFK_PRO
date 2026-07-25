using UnityEngine;

/// <summary>
/// 플레이어 모델의 크기를 1:1:1 균일 배율로 관리합니다.
/// 구, 정사면체, 정육면체 등 기본 형태의 플레이어 오브젝트에 부착하세요.
///
/// 커지기/작아지기 패드를 밟을 때마다 해당 배율로 "토글"됩니다 —
/// 한 번 밟으면 정해진 배율로 커지고(또는 작아지고), 같은 종류를 다시 밟으면 원래 크기로 복귀.
/// 커진 상태에서 작아지기 패드를 밟으면 곧바로 작아진 상태로 전환됩니다.
///
/// PlayerJump(10), PlayerGroundContact(20)가 이 컴포넌트(기본 순서 0)보다 나중에
/// 실행되도록 각자 [DefaultExecutionOrder]로 코드 차원에서 순서를 보장하므로,
/// Project Settings의 Script Execution Order를 수동으로 설정할 필요는 없습니다.
/// </summary>
public class PlayerShapeController : MonoBehaviour
{
    [Header("스케일 설정")]
    [Tooltip("커지기 패드를 밟았을 때 초기 크기에 곱할 배율 (예: 2 → 2배)")]
    public float growMultiplier = 2f;

    [Tooltip("작아지기 패드를 밟았을 때 초기 크기에 곱할 배율 (예: 0.5 → 절반)")]
    public float shrinkMultiplier = 0.5f;

    [Tooltip("스케일 최솟값 (각 축, 배율 적용 후 클램프)")]
    public float minScale = 0.2f;

    [Tooltip("스케일 최댓값 (각 축, 배율 적용 후 클램프)")]
    public float maxScale = 5.0f;

    [Header("스케일 변환 부드럽게 처리")]
    [Tooltip("스케일이 목표값으로 변하는 속도 (0이면 즉시 변경)")]
    public float lerpSpeed = 8f;

    [Tooltip("Lerp 수렴 판정 허용 오차 (이 값 이하면 목표값으로 스냅)")]
    public float lerpSnapEpsilon = 0.001f;

    [Header("메시 오브젝트 연결")]
    [Tooltip("Player_Mesh 오브젝트를 여기에 드래그하세요.")]
    public Transform meshTransform;

    [Tooltip("Player_Collider 오브젝트를 여기에 드래그하세요.")]
    public Transform colliderTransform;  //

    [Tooltip("SphereCollider처럼 비균일(X/Y 개별) 스케일에서 축별로 반영되지 못하는 콜라이더를 " +
        "쓸 때 true로 설정하세요. Unity SphereCollider는 반지름을 두 축 중 큰 쪽 기준으로만 " +
        "키우므로, X/Y 평균으로 world scale을 강제 균일화해 이 왜곡을 상쇄합니다. " +
        "BoxCollider(정육면체/정사면체)는 각 축을 독립적으로 반영하므로 false로 둡니다.")]
    public bool useAverageColliderScale = false;

    [Header("접지 판단 (isGrounded)")]
    [Tooltip("Player_Collider에 부착된 PlayerGroundContact를 연결하세요. 연결되어 있으면 " +
        "실제 충돌 접촉 기반으로 접지를 판단하고(회전 중에도 정확), 비어있으면 아래 Raycast " +
        "방식으로 대체 동작합니다(Root가 회전하지 않는 오브젝트에서만 신뢰할 수 있음).")]
    public PlayerGroundContact groundContact;

    [Tooltip("[groundContact가 비어있을 때만 사용] 바닥 감지 Raycast 거리. Player_Mesh 콜라이더 " +
        "절반 높이보다 살짝 크게 설정하세요.")]
    public float groundCheckDistance = 0.15f;

    [Tooltip("[groundContact가 비어있을 때만 사용] 바닥으로 인정할 레이어 마스크. 별도 Ground " +
        "레이어가 없으면 Everything으로 두세요.")]
    public LayerMask groundLayer = ~0; // 기본값: Everything (-1)

    // 초기 스케일 저장
    private Vector3 initialScale;

    // 현재 목표 스케일
    private Vector3 targetScale;

    // 현재 크기 상태 (Normal이면 초기 크기)
    private EScaleState currentState = EScaleState.Normal;

    // 접지 여부 (Raycast로 매 FixedUpdate마다 갱신)
    private bool isGrounded = false;

    // 접지 판단용 레이캐스트가 자기 자신의 콜라이더(Player_Mesh/Player_Collider 등)에
    // 맞는 것을 걸러내기 위한 캐시. 레이어로 자신을 제외하면 바닥이 플레이어와 같은
    // 레이어(예: 둘 다 Default)일 때 바닥까지 함께 제외되어버리는 문제가 있어 이 방식으로 대체.
    private Collider[] ownColliders;
    private readonly RaycastHit[] groundHitBuffer = new RaycastHit[8];

    public enum EScaleState
    {
        Normal,  // 초기 크기
        Grown,   // 커진 상태 (growMultiplier)
        Shrunk   // 작아진 상태 (shrinkMultiplier)
    }

    void Start()
    {
        initialScale = transform.localScale;
        targetScale = initialScale;

        if (meshTransform == null)
            Debug.LogWarning($"[PlayerShapeController] '{gameObject.name}'의 meshTransform이 연결되지 않았습니다. Y 위치 보정이 동작하지 않습니다.");

        if (colliderTransform == null)
            Debug.LogWarning($"[PlayerShapeController] '{gameObject.name}'의 colliderTransform이 연결되지 않았습니다. Collider 크기 보정이 동작하지 않습니다.");

        // 자기 자신(Player_Root/Player_Mesh/Player_Collider 등)의 콜라이더를 캐시해두고,
        // 접지 판단 레이캐스트에서 이 목록에 있는 콜라이더는 무시한다.
        ownColliders = GetComponentsInChildren<Collider>(true);

        if (groundLayer.value == 0)
            Debug.LogWarning($"[PlayerShapeController] '{gameObject.name}'의 groundLayer가 Nothing입니다. 접지 판단이 동작하지 않습니다.");
    }

    void Update()
    {
        // targetScale은 패드를 밟는 순간(ToggleScale)에만 바뀌므로 여기서는 갱신하지 않는다.

        // 현재 localScale을 목표 스케일로 부드럽게 이동
        if (lerpSpeed > 0f)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * lerpSpeed);

            // 목표값에 충분히 가까워지면 즉시 스냅 (무한 수렴 방지)
            if (Vector3.Distance(transform.localScale, targetScale) < lerpSnapEpsilon)
                transform.localScale = targetScale;
        }
        else
        {
            transform.localScale = targetScale;
        }

        // Player_Collider의 로컬 스케일은 (1,1,1)로 고정해, Root의 스케일을 그대로
        // 물려받아 월드 스케일이 실시간 크기와 항상 일치하도록 한다(부모-자식 계층에서
        // 자식의 로컬 위치·스케일은 부모 스케일만큼 자동으로 곱해져 반영된다).
        // 스케일이 1:1:1 균일 배율로만 변하도록 바뀐 뒤로는 SphereCollider의 비균일
        // 왜곡(반지름이 두 축 중 큰 쪽 기준으로만 커지는 문제)이 발생하지 않으므로,
        // 예전의 useAverageColliderScale X/Y 평균 보정 분기는 더 이상 필요 없다.
        // (해당 public 필드는 PlayerSystem/Editor의 생성기가 참조하고 있어 남겨두지만
        //  런타임 동작에는 관여하지 않는다.)
        if (colliderTransform != null)
        {
            colliderTransform.localScale = Vector3.one;

            colliderTransform.localPosition = new Vector3(
                colliderTransform.localPosition.x,
                0.5f,
                colliderTransform.localPosition.z
            );
        }
    }

    void FixedUpdate()
    {
        if (groundContact != null)
        {
            // Rigidbody 회전이 자유로워진 뒤로는(정육면체/정사면체가 실제로 통통 튀며 구름),
            // Root 원점 기준 고정 방향 Raycast로는 접지를 신뢰할 수 없다 — 물체가 회전하면
            // Root 원점과 실제 접지면 사이의 상대 방향이 매 프레임 달라지기 때문이다.
            // 실제 충돌 접촉(항상 월드 스페이스 법선 기준이라 회전과 무관) 기반으로 판단한다.
            isGrounded = groundContact.IsGrounded;
        }
        else
        {
            // Raycast 원점: Player_Root 위치 기준 (Player_Root 자체가 바닥에 있으므로 직접 사용)
            // meshTransform.position은 스케일에 따라 올라가 있으므로 사용하지 않음
            // [주의] Root가 회전하지 않는 오브젝트에서만 신뢰할 수 있는 대체 경로.
            Vector3 rayOrigin = transform.position + new Vector3(0f, 0.05f, 0f);

            // QueryTriggerInteraction.Ignore로 Trigger Collider(ScalePad 등) 제외.
            // 맞은 콜라이더가 자기 자신(ownColliders)이면 무시하고, 그 외의 콜라이더에
            // 맞아야만 접지로 인정한다.
            isGrounded = false;
            int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, groundHitBuffer,
                groundCheckDistance, groundLayer, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                if (System.Array.IndexOf(ownColliders, groundHitBuffer[i].collider) < 0)
                {
                    isGrounded = true;
                    break;
                }
            }
        }

        // Y 위치 보정은 접지 여부와 무관하게 항상 적용한다(Player_Collider와 동일하게 대칭 처리).
        // 로컬 Y는 상수 0.5로 고정한다 — meshTransform은 Root의 자식이므로 부모의
        // localScale이 자동으로 곱해져 월드 오프셋이 이미 실시간 크기를 반영한다.
        // (예전엔 여기서 targetScale.y를 다시 곱해, 부모 스케일과 중복 적용되어
        // 스케일이 1이 아닐 때 오프셋이 제곱으로 어긋나는 버그가 있었다.)
        // rb.MovePosition() 없이 meshTransform.localPosition.y만 조정
        // Player_Root Y는 Rigidbody Physics에 완전히 위임하여 누적 상승 루프 방지
        if (meshTransform != null)
        {
            float targetLocalY = 0.5f;
            Vector3 localPos = meshTransform.localPosition;

            if (Mathf.Abs(localPos.y - targetLocalY) > 0.001f)
                meshTransform.localPosition = new Vector3(localPos.x, targetLocalY, localPos.z);
        }
    }

    /// <summary>현재 활성 동작을 매 프레임 targetScale에 반영합니다.</summary>
    /// <summary>currentState에 맞춰 targetScale을 초기 크기 × 배율로 다시 계산합니다.
    /// X/Y/Z 모두 같은 배율을 곱하므로 형태 비율(1:1:1)이 유지됩니다.</summary>
    private void RecomputeTargetScale()
    {
        float m;
        switch (currentState)
        {
            case EScaleState.Grown:  m = growMultiplier;   break;
            case EScaleState.Shrunk: m = shrinkMultiplier; break;
            default:                 m = 1f;               break;
        }

        targetScale = new Vector3(
            Mathf.Clamp(initialScale.x * m, minScale, maxScale),
            Mathf.Clamp(initialScale.y * m, minScale, maxScale),
            Mathf.Clamp(initialScale.z * m, minScale, maxScale)
        );
    }

    /// <summary>패드를 밟을 때 ScalePad에서 호출합니다. 같은 상태를 다시 밟으면
    /// 초기 크기(Normal)로 돌아가고, 다른 상태면 그 상태로 전환합니다(토글).</summary>
    public void ToggleScale(EScaleState state)
    {
        currentState = (currentState == state) ? EScaleState.Normal : state;
        RecomputeTargetScale();
    }

    /// <summary>현재 접지 여부 반환 (외부 참조용)</summary>
    public bool IsGrounded() => isGrounded;

    /// <summary>현재 목표 스케일 반환</summary>
    public Vector3 GetTargetScale() => targetScale;
}