using UnityEngine;

/// <summary>
/// 전력 게이지 — 표시 전용(docs/PRD/ServerRoomPower.md §3). 판정 로직은 없고
/// <see cref="PowerMaintenanceController"/>의 값을 읽어 그리기만 한다.
/// 이 저장소엔 아직 UI 시스템이 없어 임시 OnGUI로 그린다(BookPanel/QuizTerminal과 같은 사정). UI가 생기면
/// 이 파일만 교체하면 되고 컨트롤러는 건드리지 않는다.
/// </summary>
public class PowerGauge : MonoBehaviour
{
    public PowerMaintenanceController controller;

    [Tooltip("게이지 크기/위치(화면 상단 가운데 기준 오프셋).")]
    public Vector2 size = new Vector2(320f, 46f);
    public float topMargin = 12f;

    private void OnGUI()
    {
        if (controller == null) return;

        Rect box = new Rect((Screen.width - size.x) * 0.5f, topMargin, size.x, size.y);
        GUI.Box(box, GUIContent.none);

        float ratio = controller.maxPower > 0f ? Mathf.Clamp01(controller.Power / controller.maxPower) : 0f;
        Rect bar = new Rect(box.x + 8f, box.y + 24f, box.width - 16f, 12f);
        Color prev = GUI.color;
        GUI.color = controller.SubmitLocked ? new Color(0.9f, 0.3f, 0.25f) : new Color(0.3f, 0.8f, 0.4f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), Texture2D.whiteTexture);
        GUI.color = prev;

        string state;
        if (controller.Current == PowerMaintenanceController.State.Ready) state = "준비";
        else if (controller.Current == PowerMaintenanceController.State.Completed) state = "완료";
        else if (controller.Paused) state = "정지";
        else state = controller.AutoMaintain ? "자동 유지" : (controller.SubmitLocked ? "제출 잠김" : "가동");

        GUI.Label(new Rect(box.x + 8f, box.y + 4f, box.width - 16f, 20f),
            $"전력 {controller.Power:0}/{controller.maxPower:0}   [{state}]");
    }
}
