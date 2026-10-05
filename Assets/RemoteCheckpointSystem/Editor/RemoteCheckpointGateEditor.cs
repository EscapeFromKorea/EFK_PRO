using UnityEditor;
using UnityEngine;

/// <summary>
/// RemoteCheckpointGate 인스펙터 — 기본 인스펙터 위에 "무엇을 어디에 연결하나" 도움말과 지금 배치의 문제점
/// 경고를 붙인다. 연결 자체는 CH2 쪽 IsolationRescueController의 UnityEvent에서 이 게이트의 함수를 고르는
/// 일이라(게이트는 컨트롤러를 모른다) 도움말만 둔다.
/// </summary>
[CustomEditor(typeof(RemoteCheckpointGate))]
public class RemoteCheckpointGateEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "【연결 방법】 CH2의 IsolationRescueController를 선택하고, 아래 이벤트의 [+]에 이 오브젝트를 끌어다 놓은 뒤 " +
            "RemoteCheckpointGate의 함수를 고른다.\n\n" +
            "  onSuccess    →  SetUnlocked  (체크 ✔ = true)\n" +
            "  onTimedOut   →  SetUnlocked  (체크 해제 = false)\n" +
            "  onAborted    →  SetUnlocked  (체크 해제 = false)\n" +
            "  onRestarted  →  RestartChapter\n\n" +
            "• 함수는 'RemoteCheckpointGate' 아래에서 고른다(Dynamic bool이 아니라 Static Parameters의 체크박스).\n" +
            "• 잠금이 기본이다. onSuccess를 연결하지 않으면 영영 저장되지 않는다.\n" +
            "• 문 뒤 'R을 누르면 CH4 시작으로 이동' 안내는 아래 OnCheckpointStored에 연결한다(저장 성공 시 한 번 발신).\n" +
            "• 이 오브젝트에는 ZoneEntryDetector(새 진입마다)가 같이 붙어 있어야 한다.",
            MessageType.Info);

        RemoteCheckpointGate gate = (RemoteCheckpointGate)target;
        if (string.IsNullOrEmpty(gate.targetCheckpointId))
            EditorGUILayout.HelpBox("Target Checkpoint Id가 비어 있다 — CH4 시작 RespawnZone의 Checkpoint Id와 같은 값을 넣어라.",
                MessageType.Warning);

        ZoneEntryDetector detector = gate.GetComponent<ZoneEntryDetector>();
        if (detector != null && detector.mode != ZoneEntryDetector.EntryMode.EveryNewEntry)
            EditorGUILayout.HelpBox("ZoneEntryDetector의 Mode가 '새 진입마다'가 아니다 — 잠긴 동안의 첫 진입이 기회를 소모해 " +
                                    "성공 뒤에는 저장되지 않을 수 있다.", MessageType.Warning);

        if (gate.startUnlocked)
            EditorGUILayout.HelpBox("Start Unlocked가 켜져 있다 — CH2 성공 없이도 저장된다(테스트용). 배포 전에 꺼라.",
                MessageType.Warning);

        DrawDefaultInspector();
    }
}
