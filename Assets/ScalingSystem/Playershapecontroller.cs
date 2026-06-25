using UnityEngine;

/// <summary>
/// 플레이어 모델의 상/하(Y축) 및 좌/우(X축) 비율을 관리합니다.
/// 구, 정사면체, 정육면체 등 기본 형태의 플레이어 오브젝트에 부착하세요.
/// </summary>
public class PlayerShapeController : MonoBehaviour
{
    [Header("스케일 설정")]
    [Tooltip("발판을 밟고 있는 동안 초당 변화하는 상/하(Y축) 스케일 속도")]
    public float verticalScaleSpeed = 0.5f;

    [Header("메시 오브젝트 연결")]
    [Tooltip("Player_Mesh 오브젝트를 여기에 드래그하세요.")]
    public Transform meshTransform;

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

    // 초기 스케일 저장
    private Vector3 initialScale;

    // 현재 목표 스케일
    private Vector3 targetScale;

    // 현재 활성화된 패드 동작 (null이면 아무 패드도 밟지 않은 상태)
    private EPadAction currentAction = EPadAction.None;

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

        if (meshTransform != null)
            meshTransform.localPosition = new Vector3(
                meshTransform.localPosition.x,
                targetScale.y * 0.5f,
                meshTransform.localPosition.z
            );
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
                // initialScale을 향해 매 프레임 천천히 이동
                targetScale = Vector3.MoveTowards(targetScale, initialScale, resetSpeed * Time.deltaTime);
                // ③⑤ initialScale에 완전히 도달하면 동작 종료 — 이후 패드를 밟고 있어도 중복 연산 방지
                if (targetScale == initialScale)
                    currentAction = EPadAction.None;
                break;

            case EPadAction.None:
                // 아무것도 하지 않음 — 발을 뗀 순간의 targetScale 유지
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
        // 현재 활성 동작이 해제 요청과 같을 때만 None으로 변경
        // (다른 패드로 바로 이동한 경우 덮어쓰기 방지)
        if (currentAction == action)
            currentAction = EPadAction.None;
    }

    /// <summary>현재 목표 스케일 반환</summary>
    public Vector3 GetTargetScale() => targetScale;
}