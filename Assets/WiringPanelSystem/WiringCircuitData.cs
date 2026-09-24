using UnityEngine;

/// <summary>
/// 회로 하나의 콘텐츠 데이터(회로 ID, 출력/입력 목록, 정답 쌍, 표시용 규칙 텍스트) — 코드가 아니라
/// 콘텐츠 담당이 만드는 에셋(docs/PRD/WiringPanel.md §3). 실제 CH7 값(ENTRY/POWER_1~3)은 원본
/// PRD가 "미확정 콘텐츠 구성안"으로만 남겨서 이 파일엔 예시 데이터를 넣지 않는다 — 구조만 만든다.
///
/// [WiringPanel과 분리하는 이유]
/// 패널(같은 프리팹) 하나가 W_ENTRY·W_POWER 등 여러 인스턴스로 재사용되고, 각 인스턴스는 서로
/// 다른 회로 데이터를 배정받는다(§1 "같은 프리팹, 다른 인스턴스"). 회로 데이터를 패널에
/// 하드코딩하면 인스턴스마다 스크립트를 복제해야 하므로 ScriptableObject로 분리한다.
/// </summary>
[CreateAssetMenu(menuName = "EFK/Wiring Circuit Data", fileName = "NewWiringCircuit")]
public class WiringCircuitData : ScriptableObject
{
    [System.Serializable]
    public class PortPair
    {
        public string outputId;
        public string inputId;
    }

    [Tooltip("이 회로의 식별자(로그·디버그·완료 신호에 쓰임). 예: \"ENTRY\", \"POWER_1\".")]
    public string circuitId;

    [Tooltip("이 회로에 존재하는 출력 포트 ID 목록. 예: \"A\",\"B\",\"C\".")]
    public string[] outputIds;

    [Tooltip("이 회로에 존재하는 입력 포트 ID 목록. 예: \"1\",\"2\",\"3\".")]
    public string[] inputIds;

    [Tooltip("정답 쌍. 각 outputId는 정확히 하나의 inputId와만 짝지어야 한다(중복 정답 금지).")]
    public PortPair[] pairs;

    [Tooltip("W_RULE(WiringRuleDisplay)이 표시할 규칙표 텍스트. 콘텐츠 데이터 — 이 저장소엔 아직 " +
             "UI 시스템이 없어 임시 OnGUI로만 노출된다.")]
    [TextArea]
    public string ruleText;

    /// <summary>outputId→inputId 조합이 이 회로의 정답 쌍인지 검사한다.</summary>
    public bool IsCorrectPair(string outputId, string inputId)
    {
        if (pairs == null) return false;
        foreach (PortPair p in pairs)
            if (p.outputId == outputId && p.inputId == inputId) return true;
        return false;
    }

    /// <summary>필수 필드 누락·중복 정답·존재하지 않는 포트 참조·빈 정답을 검사한다(§4 "콘텐츠 검증"
    /// — 콘텐츠 오류를 런타임에 잡는다). 문제가 있으면 시작을 거부해야 한다(WiringPanel.AssignCircuit
    /// 참고).</summary>
    public bool Validate(out string error)
    {
        if (string.IsNullOrEmpty(circuitId)) { error = "circuitId가 비어 있다."; return false; }
        if (outputIds == null || outputIds.Length == 0) { error = "outputIds가 비어 있다."; return false; }
        if (inputIds == null || inputIds.Length == 0) { error = "inputIds가 비어 있다."; return false; }
        if (pairs == null || pairs.Length == 0) { error = "pairs가 비어 있다."; return false; }

        var seenOutputs = new System.Collections.Generic.HashSet<string>();
        foreach (PortPair p in pairs)
        {
            if (string.IsNullOrEmpty(p.outputId) || string.IsNullOrEmpty(p.inputId))
            {
                error = $"'{circuitId}' — 빈 정답 쌍이 있다.";
                return false;
            }
            if (System.Array.IndexOf(outputIds, p.outputId) < 0)
            {
                error = $"'{circuitId}' — 정답 쌍이 존재하지 않는 출력 포트 '{p.outputId}'를 참조한다.";
                return false;
            }
            if (System.Array.IndexOf(inputIds, p.inputId) < 0)
            {
                error = $"'{circuitId}' — 정답 쌍이 존재하지 않는 입력 포트 '{p.inputId}'를 참조한다.";
                return false;
            }
            if (!seenOutputs.Add(p.outputId))
            {
                error = $"'{circuitId}' — 출력 포트 '{p.outputId}'에 정답이 중복 지정됐다.";
                return false;
            }
        }

        error = null;
        return true;
    }
}
