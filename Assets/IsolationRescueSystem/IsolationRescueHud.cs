using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 격리 퍼즐 상태 표시 — 상태·남은 시간·(바깥에게만) 진행도와 거부 사유 메시지. 표시 전용.
/// docs/PRD/IsolationRescue.md §2.4 "진행도(예: 1/3)만 바깥에 노출".
///
/// [보는 사람에 따라 달라진다]
/// 상태와 남은 시간은 모두에게 보인다. 단계·입력 진행도는 격리된 안쪽 참가자에게는 그리지 않는다 — 안쪽은
/// 순서판만 보고, 맞았는지는 바깥의 말로 알아야 하는 퍼즐이기 때문이다. 보는 사람을 알 수 없으면(조작 전환
/// 컴포넌트가 없는 씬) 진행도는 그리지 않는다.
///
/// [임시 OnGUI]
/// 저장소에 정식 UI 시스템이 없어 PowerGauge·WiringRuleDisplay와 같은 임시 OnGUI다. 문자열 조립은
/// <see cref="BuildLines"/>에 모아 둬서 UI가 생기면 그 결과를 읽어 가면 된다.
/// </summary>
public class IsolationRescueHud : MonoBehaviour
{
    [Tooltip("격리 컨트롤러.")]
    public IsolationRescueController controller;

    [Tooltip("메시지가 화면에 머무는 시간(초).")]
    public float messageSeconds = 3f;

    [Tooltip("표시 영역(px).")]
    public Rect area = new Rect(12f, 12f, 360f, 140f);

    private string message = string.Empty;
    private float messageUntil;

    /// <summary>거부 사유 같은 한 줄 메시지를 잠깐 띄운다.</summary>
    public void Say(string text)
    {
        message = text ?? string.Empty;
        messageUntil = Time.unscaledTime + messageSeconds;
    }

    /// <summary>현재 메시지(만료되었으면 빈 문자열).</summary>
    public string CurrentMessage => Time.unscaledTime < messageUntil ? message : string.Empty;

    /// <summary>표시할 줄들. viewerIsInside = 보는 사람이 격리된 참가자인가, viewerKnown = 보는 사람을 아는가.</summary>
    public List<string> BuildLines(bool viewerIsInside, bool viewerKnown)
    {
        var lines = new List<string>();
        if (controller == null) return lines;

        lines.Add($"격리 구출 — {StateLabel(controller.Current)}");

        IsolationRescueController.State s = controller.Current;
        bool timed = s == IsolationRescueController.State.InProgress ||
                     s == IsolationRescueController.State.AwaitFinalRelease;

        if (timed && controller.Timer != null)
            lines.Add($"남은 시간 {Mathf.CeilToInt(controller.Timer.Remaining)}초");

        if (s == IsolationRescueController.State.InProgress && viewerKnown && !viewerIsInside)
        {
            lines.Add($"단계 {controller.StageIndex + 1}/{controller.StageCount}   " +
                      $"입력 {controller.InputCount}/{IsolationRescueController.LeverCount}");
        }

        if (s == IsolationRescueController.State.AwaitFinalRelease)
            lines.Add(controller.PowerOn ? "전원 ON — 안쪽에서 해제 버튼" : "전원 OFF — 바깥에서 전원 스위치 유지");

        string msg = CurrentMessage;
        if (!string.IsNullOrEmpty(msg)) lines.Add(msg);
        return lines;
    }

    public static string StateLabel(IsolationRescueController.State state)
    {
        switch (state)
        {
            case IsolationRescueController.State.Idle: return "대기";
            case IsolationRescueController.State.Preparing: return "준비";
            case IsolationRescueController.State.InProgress: return "진행";
            case IsolationRescueController.State.AwaitFinalRelease: return "최종 해제 대기";
            case IsolationRescueController.State.Success: return "성공 (CH4 숏컷)";
            case IsolationRescueController.State.TimedOut: return "시간 초과 (CH3 경로)";
            case IsolationRescueController.State.Aborted: return "중단";
            default: return state.ToString();
        }
    }

    private void OnGUI()
    {
        if (controller == null) return;

        PlayerMover viewer = IsolationOccupancyZone.ControlledViewer();
        bool known = viewer != null;
        bool inside = known && controller.InsidePlayer == viewer.gameObject;

        List<string> lines = BuildLines(inside, known);
        if (lines.Count == 0) return;

        GUI.Box(area, GUIContent.none);
        GUI.Label(new Rect(area.x + 10f, area.y + 6f, area.width - 20f, area.height - 12f),
            string.Join("\n", lines));
    }
}
