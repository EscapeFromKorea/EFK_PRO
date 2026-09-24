using UnityEngine;

/// <summary>
/// 격리 제한시간(C2_TIMER) — 마감 시각(deadline) 하나만 들고 있는 단순 타이머.
/// docs/PRD/IsolationRescue.md §3, §4.
///
/// [왜 "남은 시간"이 아니라 "마감 시각"인가]
/// 매 프레임 남은 시간을 깎으면 프레임 순서에 따라 "입력 시각 == 마감" 경계(PRD C2-06)가 흔들린다.
/// 마감 시각을 고정해 두고 "지금 &gt;= 마감"으로만 판정하면 같은 시각은 항상 시간 초과로 일관되게
/// 떨어진다. 개인 추락 복귀 등으로 시간이 멈추지 않는 것도 같은 구조에서 자연스럽게 성립한다
/// (PRD §2.8: 복귀해도 남은 시간 유지).
///
/// [양수 필수]
/// 타이머 없는 격리 시작은 거부한다(§3). duration이 0 이하이면 TryBegin이 false를 돌려주고,
/// 컨트롤러는 격리 자체를 시작하지 않는다.
///
/// [Clock 주입]
/// 기본은 Time.time이다. Editor 자가검증(IsolationRescueSelfTest)이 시각을 직접 움직여 경계
/// 조건(C2-05/06)을 결정적으로 재현하도록 열어 뒀다 — 런타임 코드가 바꿀 일은 없다.
/// </summary>
public class IsolationTimer : MonoBehaviour
{
    [Tooltip("격리 시작~마감까지의 제한시간(초). 양수 필수. 실제 값은 미정 — 플레이테스트로 조정한다 " +
             "(docs/PRD/IsolationRescue.md §4/§5).")]
    public float duration = 60f;

    /// <summary>현재 시각 공급자. 테스트용 주입 지점이며 기본은 Time.time.</summary>
    public System.Func<float> Clock = () => Time.time;

    /// <summary>타이머가 돌고 있는가.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>마감 시각(Clock 기준). IsRunning일 때만 의미가 있다.</summary>
    public float Deadline { get; private set; }

    public float Now => Clock();

    /// <summary>돌고 있고 마감 시각에 도달했는가. 정확히 마감 시각이어도 true(C2-06).</summary>
    public bool HasExpired => IsRunning && Now >= Deadline;

    /// <summary>남은 시간(초). 멈춰 있으면 0.</summary>
    public float Remaining => IsRunning ? Mathf.Max(0f, Deadline - Now) : 0f;

    /// <summary>타이머를 시작한다. duration이 양수가 아니면 시작하지 않고 false.</summary>
    public bool TryBegin()
    {
        if (duration <= 0f)
        {
            Debug.LogError($"[IsolationTimer] '{name}'의 duration({duration})이 양수가 아니어서 시작을 거부한다.", this);
            return false;
        }

        Deadline = Now + duration;
        IsRunning = true;
        return true;
    }

    /// <summary>타이머를 멈춘다. 이미 멈춰 있어도 안전하다.</summary>
    public void Stop()
    {
        IsRunning = false;
    }
}
