using UnityEngine;

/// <summary>
/// 플레이어가 밟고 있는 동안 PlayerShapeController의 스케일을 지속 변경하는 패드입니다.
/// 발을 떼면 그 시점의 스케일에서 멈춥니다. Reset 패드는 밟는 동안 천천히 초기값으로 복귀합니다.
/// 각 패드 오브젝트에 부착하고, PadType을 설정하세요.
/// Collider는 반드시 Is Trigger = true 로 설정해야 합니다.
/// </summary>
public class ScalePad : MonoBehaviour
{
    public enum EPadType
    {
        IncreaseVertical,    // 상/하 비율 증가
        DecreaseVertical,    // 상/하 비율 감소
        IncreaseHorizontal,  // 좌/우 비율 증가
        DecreaseHorizontal,  // 좌/우 비율 감소
        Reset                // 초기 상태로 서서히 복귀
    }

    [Header("패드 설정")]
    [Tooltip("이 패드의 역할을 선택하세요.")]
    public EPadType padType = EPadType.IncreaseVertical;

    [Tooltip("플레이어 오브젝트의 태그 (기본값: Player)")]
    public string playerTag = "Player";

    [Header("시각적 피드백 (선택)")]
    [Tooltip("패드를 밟고 있는 동안 표시할 색상 (MeshRenderer 필요)")]
    public Color activateColor = Color.yellow;
    [Tooltip("패드 기본 색상")]
    public Color defaultColor = Color.white;

    private MeshRenderer meshRenderer;

    // 머티리얼 누수 방지용 — Start에서 인스턴스를 하나만 생성해 재사용
    private Material padMaterialInstance;

    // 현재 이 패드를 밟고 있는 PlayerShapeController (null이면 아무도 없음)
    private PlayerShapeController currentPlayer = null;

    // ③ Player_Root + Player_Mesh 둘 다 Tag: Player이므로 Enter/Exit가 두 번씩 호출됨
    // 카운터로 실제로 몇 개의 Collider가 겹쳐있는지 추적하여 중복 해제 방지
    private int overlapCount = 0;

    void Start()
    {
        meshRenderer = GetComponent<MeshRenderer>();

        if (meshRenderer != null)
        {
            padMaterialInstance = new Material(meshRenderer.sharedMaterial);
            padMaterialInstance.color = defaultColor;
            meshRenderer.material = padMaterialInstance;
        }

        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[ScalePad] '{gameObject.name}'의 Collider가 Is Trigger = false입니다. " +
                             "패드가 동작하려면 Is Trigger를 true로 설정하세요.");
        }
    }

    private void OnDestroy()
    {
        if (padMaterialInstance != null)
            Destroy(padMaterialInstance);
    }

    private void OnDisable()
    {
        // 비활성화 시 현재 밟고 있던 플레이어의 동작 해제 및 색상 복원
        overlapCount = 0;
        ReleaseCurrentPlayer();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        PlayerShapeController shapeController = FindShapeController(other);
        if (shapeController == null)
        {
            Debug.LogWarning($"[ScalePad] '{other.gameObject.name}'에서 PlayerShapeController를 찾을 수 없습니다.");
            return;
        }

        overlapCount++;

        // ③ 첫 번째 Enter일 때만 동작 시작 및 색상 활성화 (중복 SetAction 방지)
        if (overlapCount == 1)
        {
            currentPlayer = shapeController;
            shapeController.SetAction(PadTypeToAction(padType));
            SetPadColor(activateColor);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        PlayerShapeController shapeController = FindShapeController(other);
        if (shapeController == null) return;

        // ③ 카운터를 줄이고, 0이 됐을 때만 동작 중단 및 색상 복원
        overlapCount = Mathf.Max(0, overlapCount - 1);
        if (overlapCount == 0)
        {
            shapeController.ClearAction(PadTypeToAction(padType));
            if (currentPlayer == shapeController)
            {
                currentPlayer = null;
                SetPadColor(defaultColor);
            }
        }
    }

    /// <summary>비활성화 시 현재 플레이어의 동작을 안전하게 해제합니다.</summary>
    private void ReleaseCurrentPlayer()
    {
        if (currentPlayer != null)
        {
            currentPlayer.ClearAction(PadTypeToAction(padType));
            currentPlayer = null;
        }
        SetPadColor(defaultColor);
    }

    /// <summary>EPadType → EPadAction 변환</summary>
    private PlayerShapeController.EPadAction PadTypeToAction(EPadType type)
    {
        switch (type)
        {
            case EPadType.IncreaseVertical:   return PlayerShapeController.EPadAction.IncreaseVertical;
            case EPadType.DecreaseVertical:   return PlayerShapeController.EPadAction.DecreaseVertical;
            case EPadType.IncreaseHorizontal: return PlayerShapeController.EPadAction.IncreaseHorizontal;
            case EPadType.DecreaseHorizontal: return PlayerShapeController.EPadAction.DecreaseHorizontal;
            case EPadType.Reset:              return PlayerShapeController.EPadAction.Reset;
            default:                          return PlayerShapeController.EPadAction.None;
        }
    }

    /// <summary>PlayerShapeController를 Collider 기준으로 탐색합니다.</summary>
    private PlayerShapeController FindShapeController(Collider other)
    {
        // 1) 자기 자신
        PlayerShapeController sc = other.GetComponent<PlayerShapeController>();
        if (sc != null) return sc;

        // 2) 부모 방향 탐색 (자신 제외)
        Transform p = other.transform.parent;
        while (p != null)
        {
            sc = p.GetComponent<PlayerShapeController>();
            if (sc != null) return sc;
            p = p.parent;
        }

        // 3) 자식 탐색
        return other.GetComponentInChildren<PlayerShapeController>();
    }

    /// <summary>머티리얼 인스턴스 색상 변경 (null 안전)</summary>
    private void SetPadColor(Color color)
    {
        if (padMaterialInstance != null)
            padMaterialInstance.color = color;
    }

    // 에디터에서 패드 영역을 시각적으로 확인하기 위한 기즈모
    private void OnDrawGizmos()
    {
        switch (padType)
        {
            case EPadType.IncreaseVertical:   Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.4f); break;
            case EPadType.DecreaseVertical:   Gizmos.color = new Color(0.8f, 0.2f, 0.2f, 0.4f); break;
            case EPadType.IncreaseHorizontal: Gizmos.color = new Color(0.2f, 0.4f, 1.0f, 0.4f); break;
            case EPadType.DecreaseHorizontal: Gizmos.color = new Color(1.0f, 0.6f, 0.0f, 0.4f); break;
            case EPadType.Reset:              Gizmos.color = new Color(0.8f, 0.8f, 0.0f, 0.4f); break;
        }
        Gizmos.DrawCube(transform.position, transform.lossyScale);
    }
}