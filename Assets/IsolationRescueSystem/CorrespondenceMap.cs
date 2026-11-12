using System.Text;
using UnityEngine;

/// <summary>
/// 대응표(C2_MAP) — 바깥 플레이어에게만 기호-레버 번호 대응을 보여 준다. 고정 데이터라 재도전에서도 바뀌지
/// 않는다(PRD §4 "재도전 시 변경 범위": 정답 순서만 순환, 대응은 절대 불변). 표시 전용.
/// docs/PRD/IsolationRescue.md §3 CorrespondenceMap.
///
/// [정보 격리]
/// 격리된 안쪽 참가자에게는 그리지 않는다 — 안쪽이 대응표까지 보면 대화가 필요 없어진다. 격리 전(대기)에는
/// 안쪽이 정해지지 않았으므로 이 표 앞에 선 누구에게나 보인다.
/// </summary>
public class CorrespondenceMap : IsolationOccupancyZone
{
    [Tooltip("격리 컨트롤러. 안쪽 참가자를 가려내는 데만 쓴다. 비워 두면 항상 보인다.")]
    public IsolationRescueController controller;

    [Tooltip("레버 번호 1~3에 대응하는 기호 이름. 콘텐츠 미확정 — 기본값은 원본 예시.")]
    public string[] symbolNames = (string[])IsolationSymbols.DefaultNames.Clone();

    /// <summary>이 표가 지금 이 참가자에게 보여야 하는가.</summary>
    public bool ShouldShowTo(PlayerMover viewer)
    {
        if (viewer == null || !Contains(viewer)) return false;
        return controller == null || controller.InsidePlayer != viewer.gameObject;
    }

    /// <summary>"파도 = 레버 1 / 달 = 레버 2 / 십자 = 레버 3" 형태의 대응표 문자열.</summary>
    public string BuildText()
    {
        var sb = new StringBuilder();
        for (int lever = 1; lever <= IsolationRescueController.LeverCount; lever++)
        {
            if (lever > 1) sb.Append('\n');
            sb.Append(IsolationSymbols.NameOf(symbolNames, lever)).Append(" = 레버 ").Append(lever);
        }
        return sb.ToString();
    }

    private void OnGUI()
    {
        PlayerMover viewer = ControlledOccupant();
        if (!ShouldShowTo(viewer)) return;

        float w = Mathf.Min(300f, Screen.width - 40f);
        Rect box = new Rect(20f, Screen.height * 0.5f - 70f, w, 110f);
        GUI.Box(box, "기호 대응표");
        GUI.Label(new Rect(box.x + 12f, box.y + 28f, box.width - 24f, box.height - 36f), BuildText());
    }
}
