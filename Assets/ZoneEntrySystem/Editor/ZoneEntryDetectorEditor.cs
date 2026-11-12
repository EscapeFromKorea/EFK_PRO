using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ZoneEntryDetector 인스펙터 — 기본 인스펙터 아래에 "함정 연결 도우미"를 붙인다. UnityEvent 기본 드롭다운은
/// 끌어다 놓은 오브젝트의 모든 컴포넌트·함수를 늘어놓아 엉뚱한 함수를 고르기 쉽다. 이 도우미는 슬롯이
/// <see cref="PeriodicTrapBase"/>(도끼·망치·가시)만 받고, 버튼이 정확한 <c>Activate()</c>/<c>Deactivate()</c>를
/// <c>OnValidEntry</c>에 영속 리스너로 꽂아 준다. 런타임 컴포넌트는 함정의 존재를 계속 모른다(범용 감지기
/// 유지) — 이 에디터 스크립트만 안다. 같은 방식의 선례: <c>PeriodicTrapRespawnWiring</c>.
///
/// 발사구·레이저는 같은 <c>Activate()</c>를 가졌지만 이 도우미의 대상이 아니다(요청 범위 밖) — 기본
/// 드롭다운으로 연결하면 된다. 구역을 나갈 때 자동 정지는 없으므로(PRD §6) Deactivate()는 다른 이벤트에
/// 연결해야 하는 경우가 많다 — 이 도우미는 OnValidEntry에만 꽂는다.
/// </summary>
[CustomEditor(typeof(ZoneEntryDetector))]
public class ZoneEntryDetectorEditor : Editor
{
    private PeriodicTrapBase trap;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("함정 연결 도우미", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "도끼·망치·가시만 고를 수 있다. 버튼을 누르면 OnValidEntry에 해당 함수가 추가된다. " +
            "이미 같은 연결이 있으면 건너뛴다.", MessageType.None);

        trap = (PeriodicTrapBase)EditorGUILayout.ObjectField("함정", trap, typeof(PeriodicTrapBase), true);

        using (new EditorGUI.DisabledScope(trap == null))
        {
            if (GUILayout.Button("OnValidEntry에 Activate() 추가")) Wire(trap.Activate, nameof(PeriodicTrapBase.Activate));
            if (GUILayout.Button("OnValidEntry에 Deactivate() 추가")) Wire(trap.Deactivate, nameof(PeriodicTrapBase.Deactivate));
        }
    }

    private void Wire(UnityAction action, string methodName)
    {
        ZoneEntryDetector detector = (ZoneEntryDetector)target;
        UnityEvent evt = detector.OnValidEntry;

        for (int i = 0; i < evt.GetPersistentEventCount(); i++)
        {
            if (evt.GetPersistentTarget(i) == trap && evt.GetPersistentMethodName(i) == methodName)
            {
                Debug.Log($"[ZoneEntrySystem] '{trap.name}'.{methodName}()는 이미 연결돼 있어 건너뛴다.", detector);
                return;
            }
        }

        Undo.RecordObject(detector, $"Wire {trap.name}.{methodName}");
        UnityEventTools.AddVoidPersistentListener(evt, action);
        EditorUtility.SetDirty(detector);
        Debug.Log($"[ZoneEntrySystem] OnValidEntry에 '{trap.name}'.{methodName}()를 연결했다.", detector);
    }
}
