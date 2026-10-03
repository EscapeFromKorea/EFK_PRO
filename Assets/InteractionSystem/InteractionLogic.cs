using UnityEngine;

public enum HoldResult { None, TapNow, TapOnRelease, HoldFired, Cancelled }

/// <summary>
/// 탭/홀드 판정 순수 로직. <c>Input</c>도 <c>Time</c>도 읽지 않고 인자만 받아서 에디터 자가검증이
/// 시간만 흉내 내 돌릴 수 있다(<c>InteractionLogicSelfTest</c>).
///
/// 규칙(키맵 통합안 §2-1): 홀드가 가능한 대상 위에서만 탭이 "뗄 때"로 밀리고, 그 외 대상의 탭은 누른
/// 즉시 나간다. 임계 시간에 홀드가 확정되면 이후 뗄 때 탭은 나가지 않는다. 홀드 도중 홀드 대상이
/// 사라지면(Tab 전환, 붙잡힘 등) 취소하고, 뗄 때까지 아무것도 나가지 않는다.
/// </summary>
public sealed class HoldTracker
{
    private enum State { Idle, Holding, Spent }

    private State state;
    private float startTime;

    public bool IsHolding => state == State.Holding;

    public float Progress(float now, float threshold) =>
        state == State.Holding ? Mathf.Clamp01((now - startTime) / Mathf.Max(0.01f, threshold)) : 0f;

    /// <param name="down">이번 프레임에 눌림(GetKeyDown)</param>
    /// <param name="up">이번 프레임에 뗌(GetKeyUp)</param>
    /// <param name="held">지금 눌려 있음(GetKey)</param>
    /// <param name="holdAvailable">지금 실행 가능한 홀드 액션이 있는가</param>
    public HoldResult Step(bool down, bool up, bool held, float now, float threshold, bool holdAvailable)
    {
        switch (state)
        {
            case State.Idle:
                if (!down) return HoldResult.None;
                if (!holdAvailable) return HoldResult.TapNow;
                if (up) return HoldResult.TapOnRelease; // 한 프레임 안에 눌렀다 뗌.
                state = State.Holding;
                startTime = now;
                return HoldResult.None;

            case State.Holding:
                if (!holdAvailable)
                {
                    state = held && !up ? State.Spent : State.Idle;
                    return HoldResult.Cancelled;
                }
                if (up || !held)
                {
                    state = State.Idle;
                    return HoldResult.TapOnRelease;
                }
                if (now - startTime >= threshold)
                {
                    state = State.Spent;
                    return HoldResult.HoldFired;
                }
                return HoldResult.None;

            default: // Spent: 뗄 때까지 대기.
                if (up || !held) state = State.Idle;
                return HoldResult.None;
        }
    }
}

public static class InteractionLogic
{
    /// <summary>같은 입력에 후보가 여럿이면 우선순위가 높은 쪽, 같으면 거리가 가까운 쪽이 이긴다.</summary>
    public static bool IsBetter(int priorityA, float distanceA, int priorityB, float distanceB) =>
        priorityA > priorityB || (priorityA == priorityB && distanceA < distanceB);
}
