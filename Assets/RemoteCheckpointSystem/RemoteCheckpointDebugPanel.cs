using UnityEngine;

/// <summary>
/// 테스트 전용 — IsolationRescueController 없이 원격 체크포인트 게이트를 시험하는 패널. CH2 성공/실패/재시작
/// 신호를 F1/F2/F3 키(또는 화면 버튼)로 흉내 내고, 게이트 상태와 "R 안내"를 화면에 보여 준다.
/// 실제 맵에서는 쓰지 않는다(그쪽은 컨트롤러 이벤트를 게이트에 인스펙터로 연결한다).
/// 내장 GUI 폰트에 한글 글리프가 없어 영문이다.
/// </summary>
public class RemoteCheckpointDebugPanel : MonoBehaviour
{
    public RemoteCheckpointGate gate;

    private bool unlocked;
    private bool stored;

    private void Awake()
    {
        if (gate != null) gate.OnCheckpointStored.AddListener(() => stored = true);
    }

    private void Update()
    {
        if (gate == null) return;
        if (Input.GetKeyDown(KeyCode.F1)) Success();
        if (Input.GetKeyDown(KeyCode.F2)) Fail();
        if (Input.GetKeyDown(KeyCode.F3)) Restart();
    }

    private void Success() { unlocked = true; gate.SetUnlocked(true); }
    private void Fail() { unlocked = false; gate.SetUnlocked(false); }
    private void Restart() { unlocked = false; stored = false; gate.RestartChapter(); }

    private void OnGUI()
    {
        if (gate == null) return;
        GUILayout.BeginArea(new Rect(12f, 40f, 360f, 220f));
        GUILayout.Label($"Gate: {(unlocked ? "UNLOCKED" : "LOCKED")}   Stored: {stored}");
        if (GUILayout.Button("F1  CH2 success (unlock A)")) Success();
        if (GUILayout.Button("F2  CH2 timeout/abort (lock A)")) Fail();
        if (GUILayout.Button("F3  chapter restart")) Restart();
        if (stored) GUILayout.Label(">> Press R to return to CH4 start");
        GUILayout.EndArea();
    }
}
