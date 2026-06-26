using UnityEngine;

/// <summary>
/// 플레이어 모델의 상/하(Y축) 및 좌/우(X축) 비율을 관리합니다.
/// 구, 정사면체, 정육면체 등 기본 형태의 플레이어 오브젝트에 부착하세요.
///
/// [중요] Script Execution Order 설정 필요:
/// Edit → Project Settings → Script Execution Order에서
/// PlayerMover: -100 / PlayerShapeController: -90 으로 설정하세요.
/// </summary>
public class PlayerShapeController : MonoBehaviour
{
    [Header("스케일 설정")]
    [Tooltip("발판을 밟고 있는 동안 초당 변화하는 상/하(Y축) 스케일 속도")]
    public float verticalScaleSpeed = 0.5f;

    [Tooltip("발판을 밟고 있는 동안 초당 변화하는 좌/우(X축) 스케일 속도")]
    public float horizontalScaleSpeed = 0.5f;

    [Tooltip("스케일 최솟값 (각 축)")]
    public float minScale = 0.2f;

    [Tooltip("스케일 최댓값 (각 축)")]
    public float maxScale = 5.0f;

    [Header("Reset 설정")]
    [Tooltip("Reset 발판을 밟았을 때 초기 스케일로 돌아오는 속도")]
    public float resetSpeed = 2f;

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

    [Header("접지 판단 (isGrounded)")]
    [Tooltip("바닥 감지 Raycast 거리. Player_Mesh 콜라이더 절반 높이보다 살짝 크게 설정하세요.")]
    public float groundCheckDistance = 0.15f;

    [Tooltip("바닥으로 인정할 레이어 마스크. 별도 Ground 레이어가 없으면 Everything으로 두세요.")]
    public LayerMask groundLayer = ~0; // 기본값: Everything (-1)

    // 초기 스케일 저장
    private Vector3 initialScale;

    // 현재 목표 스케일
    private Vector3 targetScale;

    // 현재 활성화된 패드 동작
    private EPadAction currentAction = EPadAction.None;

    // 접지 여부 (Raycast로 매 FixedUpdate마다 갱신)
    private bool isGrounded = false;

    public enum EPadAction
    {
        None,
        IncreaseVertical,
        DecreaseVertical,
        IncreaseHorizontal,
        DecreaseHorizontal,
        Reset
    }

    void Start()
    {
        initialScale = transform.localScale;
        targetScale = initialScale;

        if (meshTransform == null)
            Debug.LogWarning($"[PlayerShapeController] '{gameObject.name}'의 meshTransform이 연결되지 않았습니다. Y 위치 보정이 동작하지 않습니다.");

        if (colliderTransform == null)
            Debug.LogWarning($"[PlayerShapeController] '{gameObject.name}'의 colliderTransform이 연결되지 않았습니다. Collider 크기 보정이 동작하지 않습니다.");

        // 플레이어 자신의 레이어를 groundLayer에서 런타임에 자동 제외
        groundLayer &= ~(1 << gameObject.layer);

        // 레이어 제외 이후 체크 — 플레이어 레이어만 선택된 경우 제외 후 Nothing이 되는 케이스도 감지
        if (groundLayer.value == 0)
            Debug.LogWarning($"[PlayerShapeController] '{gameObject.name}'의 groundLayer가 Nothing입니다. 접지 판단이 동작하지 않습니다. 플레이어 레이어 외에 바닥 레이어도 포함되어 있는지 확인하세요.");
    }

    void Update()
    {
        // 밟고 있는 패드 동작을 매 프레임 적용
        ApplyCurrentAction();

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

        // Player_Root 스케일의 역수를 Player_Collider에 적용
        // → Collider의 월드 스케일이 항상 (1,1,1)로 유지되어 Physics 밀어올림 방지
        if (colliderTransform != null)
        {
            colliderTransform.localScale = new Vector3(
                1f / transform.localScale.x,
                1f / transform.localScale.y,
                1f / transform.localScale.z
            );

            colliderTransform.localPosition = new Vector3(
                colliderTransform.localPosition.x,
                0.5f / transform.localScale.y,
                colliderTransform.localPosition.z
            );
        }
    }

    void FixedUpdate()
    {
        // Raycast 원점: Player_Root 위치 기준 (Player_Root 자체가 바닥에 있으므로 직접 사용)
        // meshTransform.position은 스케일에 따라 올라가 있으므로 사용하지 않음
        Vector3 rayOrigin = transform.position + new Vector3(0f, 0.05f, 0f);

        // QueryTriggerInteraction.Ignore로 Trigger Collider(ScalePad 등) 제외
        // 플레이어 레이어는 Start()에서 이미 제외됨
        isGrounded = Physics.Raycast(rayOrigin, Vector3.down, groundCheckDistance,
            groundLayer, QueryTriggerInteraction.Ignore);

        // 접지 상태일 때만 Y 위치 보정 적용 — 점프 중에는 보정하지 않음
        // rb.MovePosition() 없이 meshTransform.localPosition.y만 조정
        // Player_Root Y는 Rigidbody Physics에 완전히 위임하여 누적 상승 루프 방지
        if (isGrounded && meshTransform != null)
        {
            float targetLocalY = targetScale.y * 0.5f;
            Vector3 localPos = meshTransform.localPosition;

            if (Mathf.Abs(localPos.y - targetLocalY) > 0.001f)
                meshTransform.localPosition = new Vector3(localPos.x, targetLocalY, localPos.z);
        }
    }

    /// <summary>현재 활성 동작을 매 프레임 targetScale에 반영합니다.</summary>
    private void ApplyCurrentAction()
    {
        switch (currentAction)
        {
            case EPadAction.IncreaseVertical:
                targetScale.y = Mathf.Clamp(targetScale.y + verticalScaleSpeed * Time.deltaTime, minScale, maxScale);
                break;

            case EPadAction.DecreaseVertical:
                targetScale.y = Mathf.Clamp(targetScale.y - verticalScaleSpeed * Time.deltaTime, minScale, maxScale);
                break;

            case EPadAction.IncreaseHorizontal:
                targetScale.x = Mathf.Clamp(targetScale.x + horizontalScaleSpeed * Time.deltaTime, minScale, maxScale);
                break;

            case EPadAction.DecreaseHorizontal:
                targetScale.x = Mathf.Clamp(targetScale.x - horizontalScaleSpeed * Time.deltaTime, minScale, maxScale);
                break;

            case EPadAction.Reset:
                targetScale = Vector3.MoveTowards(targetScale, initialScale, resetSpeed * Time.deltaTime);
                if (targetScale == initialScale)
                    currentAction = EPadAction.None;
                break;

            case EPadAction.None:
                break;
        }
    }

    /// <summary>패드를 밟기 시작할 때 ScalePad에서 호출합니다.</summary>
    public void SetAction(EPadAction action)
    {
        currentAction = action;
    }

    /// <summary>패드에서 발을 뗄 때 ScalePad에서 호출합니다.</summary>
    public void ClearAction(EPadAction action)
    {
        if (currentAction == action)
            currentAction = EPadAction.None;
    }

    /// <summary>현재 접지 여부 반환 (외부 참조용)</summary>
    public bool IsGrounded() => isGrounded;

    /// <summary>현재 목표 스케일 반환</summary>
    public Vector3 GetTargetScale() => targetScale;
}