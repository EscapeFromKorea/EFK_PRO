using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// W_LINES — 연결된 쌍을 선으로 표시한다(순수 표시 전용, 판정 로직 없음). docs/PRD/WiringPanel.md §3.
///
/// panel.Connections(outputId→inputId)를 매 프레임 읽어 LineRenderer를 동기화한다. 연결은 최대
/// 3쌍(§ "CH7 회로 데이터" 기준)이라 매 프레임 순회해도 비용이 무시할 수준이다 — BlockCarrySystem의
/// retargetInterval 같은 간격 제한을 여기선 두지 않는다.
/// </summary>
public class WiringLineRenderer : MonoBehaviour
{
    [Tooltip("연결 목록을 읽어올 패널.")]
    public WiringPanel panel;

    [Tooltip("선 두께(Unit).")]
    public float lineWidth = 0.05f;

    [Tooltip("선 머티리얼. 비워두면 기본 스프라이트 셰이더로 생성한다.")]
    public Material lineMaterial;

    // outputId -> 그 연결을 그리는 LineRenderer. panel.Connections와 매 프레임 동기화한다.
    private readonly Dictionary<string, LineRenderer> activeLines = new Dictionary<string, LineRenderer>();
    private readonly List<string> removeBuffer = new List<string>();

    private void Update()
    {
        if (panel == null) return;

        foreach (KeyValuePair<string, string> conn in panel.Connections)
        {
            if (!activeLines.TryGetValue(conn.Key, out LineRenderer line))
            {
                line = CreateLine(conn.Key);
                activeLines[conn.Key] = line;
            }

            Transform outT = FindPort(panel.outputPorts, conn.Key);
            Transform inT = FindPort(panel.inputPorts, conn.Value);
            if (outT == null || inT == null) continue;

            line.SetPosition(0, outT.position);
            line.SetPosition(1, inT.position);
        }

        // 더 이상 연결에 없는 선은 정리한다(재배정으로 리셋된 경우 등).
        removeBuffer.Clear();
        foreach (string outputId in activeLines.Keys)
            if (!panel.Connections.ContainsKey(outputId)) removeBuffer.Add(outputId);

        foreach (string outputId in removeBuffer)
        {
            if (activeLines.TryGetValue(outputId, out LineRenderer line) && line != null)
                Destroy(line.gameObject);
            activeLines.Remove(outputId);
        }
    }

    private LineRenderer CreateLine(string outputId)
    {
        GameObject go = new GameObject($"WireLine_{outputId}");
        go.transform.SetParent(transform, false);

        LineRenderer line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.useWorldSpace = true;
        if (lineMaterial != null) line.material = lineMaterial;
        return line;
    }

    private static Transform FindPort(WiringPort[] ports, string portId)
    {
        if (ports == null) return null;
        foreach (WiringPort p in ports)
            if (p != null && p.portId == portId) return p.transform;
        return null;
    }

    private void OnDestroy()
    {
        foreach (LineRenderer line in activeLines.Values)
            if (line != null) Destroy(line.gameObject);
        activeLines.Clear();
    }
}
