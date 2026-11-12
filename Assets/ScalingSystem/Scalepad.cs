using UnityEngine;

/// <summary>
/// 플레이어가 밟으면 PlayerShapeController의 크기를 정해진 배율로 "토글"하는 패드입니다.
/// 한 번 밟으면 커지고(또는 작아지고), 같은 패드를 다시 밟으면 원래 크기로 돌아옵니다.
/// 크기는 1:1:1 균일 배율로 변합니다. 밟는 순간 한 번만 반응하며, 서 있는 동안 계속
/// 변하지 않습니다(밟고 있다가 발을 떼도 크기는 유지됨).
/// 각 패드 오브젝트에 부착하고, PadType을 설정하세요.
/// Collider는 반드시 Is Trigger = true 로 설정해야 합니다.
/// </summary>
public class ScalePad : MonoBehaviour
{
    public enum EPadType
    {
        Grow,   // 커지기 토글
        Shrink  // 작아지기 토글
    }

    [Header("패드 설정")]
    [Tooltip("이 패드의 역할을 선택하세요.")]
    public EPadType padType = EPadType.Grow;

    [Tooltip("한 번 토글한 뒤 이 시간(초) 동안은 다시 밟아도 무시합니다. " +
        "통통 튀는 물리로 트리거가 연속 재진입할 때 연달아 토글되는 것을 막습니다.")]
    public float cooldownSeconds = 1f;

    // 도형별 마지막 토글 시각. 쿨다운 판정에 사용 — 패드 하나로 묶으면 한 도형의 토글이 다른 도형의 첫 밟기를 막는다.
    private readonly System.Collections.Generic.Dictionary<PlayerShapeController, float> lastToggleTime =
        new System.Collections.Generic.Dictionary<PlayerShapeController, float>();

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

    // ③ [정정 — 2026-09-15] Player_Root는 콜라이더가 없다. 실제로 두 번씩 겹쳐 들어오는 건
    // Player_Mesh(트리거)·Player_Collider(솔리드) 둘 다 Tag: Player라서다(DoorSystem/PadTrigger·
    // ExitWeightPlate와 같은 원인). 도형(PlayerShapeController)별로 몇 개의 Collider가 겹쳐있는지
    // 추적하여 중복 토글을 막는다 — 패드 하나에 카운터 하나면 다른 도형이 이미 올라서 있을 때
    // 두 번째 도형의 첫 밟기가 토글되지 않는다.
    private readonly System.Collections.Generic.Dictionary<PlayerShapeController, int> overlapCounts =
        new System.Collections.Generic.Dictionary<PlayerShapeController, int>();

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
        // 비활성화 시 상태 초기화 및 색상 복원 (크기 토글은 플레이어 쪽에 남으므로 건드리지 않음)
        overlapCounts.Clear();
        SetPadColor(defaultColor);
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

        overlapCounts.TryGetValue(shapeController, out int count);
        overlapCounts[shapeController] = count + 1;

        // ③ Player_Mesh(트리거)·Player_Collider(솔리드)가 둘 다 Player 태그라 Enter가 여러 번
        // 호출되므로, 그 도형이 실제로 밟기 시작한 첫 번째 Enter일 때만 처리한다(중복 토글 방지).
        if (count == 0)
        {
            SetPadColor(activateColor);

            // 쿨다운 이내의 재진입(통통 튀며 반복 Enter/Exit)은 토글하지 않는다.
            if (!lastToggleTime.TryGetValue(shapeController, out float last) ||
                Time.time - last >= cooldownSeconds)
            {
                lastToggleTime[shapeController] = Time.time;
                shapeController.ToggleScale(PadTypeToState(padType));
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        PlayerShapeController shapeController = FindShapeController(other);
        if (shapeController == null) return;

        // ③ 그 도형의 카운터를 줄이고, 아무도 안 밟게 됐을 때만 색상 복원 (크기는 유지, 토글은 다음 Enter에서)
        if (!overlapCounts.TryGetValue(shapeController, out int count)) return;
        if (count <= 1) overlapCounts.Remove(shapeController);
        else overlapCounts[shapeController] = count - 1;
        if (overlapCounts.Count == 0) SetPadColor(defaultColor);
    }

    /// <summary>EPadType → PlayerShapeController.EScaleState 변환</summary>
    private PlayerShapeController.EScaleState PadTypeToState(EPadType type)
    {
        return type == EPadType.Shrink
            ? PlayerShapeController.EScaleState.Shrunk
            : PlayerShapeController.EScaleState.Grown;
    }

    /// <summary>PlayerShapeController를 Collider 기준으로 탐색합니다.
    /// GetComponentInParent가 자신+상위를 모두 훑으므로(Player_Mesh/Player_Collider → Player_Root),
    /// 그래도 없으면 하위까지 본다.</summary>
    private PlayerShapeController FindShapeController(Collider other)
    {
        return other.GetComponentInParent<PlayerShapeController>()
            ?? other.GetComponentInChildren<PlayerShapeController>();
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
            case EPadType.Grow:   Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.4f); break;
            case EPadType.Shrink: Gizmos.color = new Color(0.8f, 0.2f, 0.2f, 0.4f); break;
        }
        Gizmos.DrawCube(transform.position, transform.lossyScale);
    }
}