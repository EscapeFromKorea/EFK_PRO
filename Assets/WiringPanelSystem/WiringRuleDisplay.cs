using UnityEngine;

/// <summary>
/// W_RULE — 현재 회로의 규칙표를 표시한다(콘텐츠 데이터만 바인딩, 판정 로직 없음).
/// docs/PRD/WiringPanel.md §3.
///
/// [임시 OnGUI — 정식 UI 시스템 생기면 교체]
/// 이 저장소에는 아직 UI 시스템(Canvas/uGUI/TMP)이 없다(RespawnController.OnGUI와 같은 사정).
/// 여기서 정식 HUD를 신설하면 배선 패널 작업이 UI 시스템 작업이 돼 버리므로, 씬 세팅이 0인
/// OnGUI 텍스트 한 줄로 둔다 — 정식 HUD가 생기면 이 메서드만 지우고 panel.CurrentCircuit.ruleText를
/// 읽어 가면 된다.
/// </summary>
public class WiringRuleDisplay : MonoBehaviour
{
    [Tooltip("규칙표를 읽어올 패널.")]
    public WiringPanel panel;

    [Tooltip("화면에 그릴 영역(px).")]
    public Rect displayArea = new Rect(12f, 40f, 420f, 100f);

    private void OnGUI()
    {
        if (panel == null || panel.CurrentCircuit == null) return;
        GUI.Label(displayArea, $"[{panel.CurrentCircuit.circuitId}] {panel.CurrentCircuit.ruleText}");
    }
}
