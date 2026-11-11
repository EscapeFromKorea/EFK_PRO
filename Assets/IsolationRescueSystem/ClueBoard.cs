using System.Text;
using UnityEngine;

/// <summary>
/// 순서판(C2_CLUE_1~3) — 격리된 안쪽 플레이어에게만 현재 단계의 정답 순서를 기호로 보여 준다.
/// 표시 전용이다(판정 로직 없음). docs/PRD/IsolationRescue.md §3 ClueBoard.
///
/// [정보 격리 — 이 퍼즐의 핵심]
/// 안쪽은 기호 순서만, 바깥은 기호-레버 대응표만 가진다. 그래서 그리는 조건이 셋이다:
/// (1) 지금 보는 사람이 격리된 참가자(컨트롤러.InsidePlayer)이고, (2) 그 사람이 이 판 앞(트리거 안)에 서 있고,
/// (3) 이 판이 맡은 단계가 현재 단계이며 진행 중일 때. 바깥 플레이어에게는 어떤 순서도 노출하지 않는다.
///
/// [임시 OnGUI]
/// 저장소에 정식 UI 시스템이 없어 BookPanel·WiringRuleDisplay와 같은 임시 OnGUI로 그린다. UI가 생기면
/// <see cref="BuildText"/>가 돌려주는 문자열을 읽어 가면 된다.
/// </summary>
public class ClueBoard : IsolationOccupancyZone
{
    [Tooltip("격리 컨트롤러.")]
    public IsolationRescueController controller;

    [Tooltip("이 판이 맡은 단계(0부터). C2_CLUE_1 = 0, C2_CLUE_2 = 1, C2_CLUE_3 = 2.")]
    public int stageIndex;

    [Tooltip("레버 번호 1~3에 대응하는 기호 이름. 콘텐츠 미확정 — 기본값은 원본 예시.")]
    public string[] symbolNames = (string[])IsolationSymbols.DefaultNames.Clone();

    /// <summary>이 판이 지금 이 참가자에게 순서를 보여 줘야 하는가.</summary>
    public bool ShouldShowTo(PlayerMover viewer)
    {
        if (controller == null || viewer == null) return false;
        if (controller.Current != IsolationRescueController.State.InProgress) return false;
        if (controller.StageIndex != stageIndex) return false;
        if (controller.InsidePlayer != viewer.gameObject) return false;
        return Contains(viewer);
    }

    /// <summary>현재 정답 순서를 "달 → 파도 → 십자" 형태로 만든다. 진행 중이 아니면 빈 문자열.</summary>
    public string BuildText()
    {
        if (controller == null || controller.Current != IsolationRescueController.State.InProgress)
            return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < controller.CurrentOrder.Count; i++)
        {
            if (i > 0) sb.Append(" → ");
            sb.Append(IsolationSymbols.NameOf(symbolNames, controller.CurrentOrder[i]));
        }
        return sb.ToString();
    }

    private void OnGUI()
    {
        PlayerMover viewer = ControlledOccupant();
        if (!ShouldShowTo(viewer)) return;

        float w = Mathf.Min(420f, Screen.width - 40f);
        Rect box = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f - 80f, w, 90f);
        GUI.Box(box, $"순서판 {stageIndex + 1}");
        GUI.Label(new Rect(box.x + 12f, box.y + 30f, box.width - 24f, 50f),
            $"{BuildText()}\n(바깥에 기호 순서를 말로 전달하세요)");
    }
}
