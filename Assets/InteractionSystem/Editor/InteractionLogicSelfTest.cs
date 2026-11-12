using UnityEditor;
using UnityEngine;

/// <summary>
/// 탭/홀드 판정(<see cref="HoldTracker"/>)과 후보 중재(<see cref="InteractionLogic.IsBetter"/>) 자가검증.
/// <c>Input</c>은 흉내 낼 수 없으므로 판정을 시간·키 상태만 받는 순수 함수로 뽑아 두고 여기서 시간만
/// 넣어 돌린다(저장소에 Test Framework 패키지가 없어 다른 SelfTest와 같은 메뉴 + 배치모드 방식).
///
/// 실행: 메뉴 Tools &gt; Interaction &gt; Run Logic Self-Test, 또는 배치모드
/// `-batchmode -nographic -quit -executeMethod InteractionLogicSelfTest.RunFromCommandLine`
/// (실패가 하나라도 있으면 종료 코드 1).
/// </summary>
public static class InteractionLogicSelfTest
{
    private const float T = 1f;
    private static int passed, failed;

    [MenuItem("Tools/Interaction/Run Logic Self-Test")]
    public static void RunFromMenu() => Run();

    public static void RunFromCommandLine() => EditorApplication.Exit(Run() ? 0 : 1);

    private static bool Run()
    {
        passed = failed = 0;

        // 홀드 대상이 없으면 누르는 즉시 탭.
        var tr = new HoldTracker();
        Check("홀드 불가: 누르면 즉시 탭", tr.Step(true, false, true, 0f, T, false) == HoldResult.TapNow);

        // 홀드 가능 + 임계 전에 뗌 → 뗄 때 탭 (시나리오 6).
        tr = new HoldTracker();
        Check("홀드 가능: 누른 순간엔 아무것도 안 나감", tr.Step(true, false, true, 0f, T, true) == HoldResult.None);
        Check("진행 표시 중", tr.IsHolding && tr.Progress(0.5f, T) > 0.49f && tr.Progress(0.5f, T) < 0.51f);
        Check("0.5초에 뗌 → 뗄 때 탭", tr.Step(false, true, false, 0.5f, T, true) == HoldResult.TapOnRelease);
        Check("뗀 뒤 대기 상태", !tr.IsHolding);

        // 임계 도달 → 홀드 확정, 이후 뗄 때 탭은 안 나감.
        tr = new HoldTracker();
        tr.Step(true, false, true, 0f, T, true);
        Check("0.99초: 아직 홀드 아님", tr.Step(false, false, true, 0.99f, T, true) == HoldResult.None);
        Check("1.0초: 홀드 확정", tr.Step(false, false, true, 1.0f, T, true) == HoldResult.HoldFired);
        Check("확정 후 계속 누르면 반복 안 함", tr.Step(false, false, true, 1.5f, T, true) == HoldResult.None);
        Check("확정 후 뗄 때 탭 안 나감", tr.Step(false, true, false, 1.6f, T, true) == HoldResult.None);
        Check("다음 누름은 새 판정", tr.Step(true, false, true, 2f, T, false) == HoldResult.TapNow);

        // 홀드 도중 대상 소실(Tab·붙잡힘) → 취소, 뗄 때 아무것도 안 나감 (시나리오 7).
        tr = new HoldTracker();
        tr.Step(true, false, true, 0f, T, true);
        Check("홀드 중 대상 소실 → 취소", tr.Step(false, false, true, 0.4f, T, false) == HoldResult.Cancelled);
        Check("취소 후 대상이 돌아와도 홀드 확정 안 함", tr.Step(false, false, true, 1.2f, T, true) == HoldResult.None);
        Check("취소 후 뗄 때 탭 안 나감", tr.Step(false, true, false, 1.3f, T, true) == HoldResult.None);

        // 한 프레임에 눌렀다 뗌.
        tr = new HoldTracker();
        Check("같은 프레임 누름+뗌 → 뗄 때 탭", tr.Step(true, true, false, 0f, T, true) == HoldResult.TapOnRelease);

        // 후보 중재: 우선순위 > 거리.
        Check("우선순위가 높으면 멀어도 이김", InteractionLogic.IsBetter(300, 9f, 200, 1f));
        Check("우선순위가 낮으면 가까워도 짐", !InteractionLogic.IsBetter(200, 1f, 300, 9f));
        Check("같은 순위는 가까운 쪽", InteractionLogic.IsBetter(200, 1f, 200, 2f));
        Check("완전 동률은 먼저 온 쪽 유지", !InteractionLogic.IsBetter(200, 1f, 200, 1f));

        Debug.Log($"[InteractionLogicSelfTest] 통과 {passed} / 실패 {failed}");
        return failed == 0;
    }

    private static void Check(string name, bool ok)
    {
        if (ok) passed++;
        else
        {
            failed++;
            Debug.LogError($"[InteractionLogicSelfTest] 실패: {name}");
        }
    }
}
