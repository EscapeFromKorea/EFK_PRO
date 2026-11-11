using System;
using System.Collections.Generic;

public enum InteractionChannel { Hand, Gear }
public enum InteractionTrigger { Tap, Hold }

/// <summary>기믹 간 우선순위(키맵 통합안 §3). 같은 순위 안에서는 거리가 가까운 쪽이 이긴다.</summary>
public static class InteractionPriority
{
    public const int StateOwner = 400; // 탑승·매달림·부착·도킹 해제 등 상태를 쥔 쪽
    public const int Panel = 300;      // 슬롯·패널류
    public const int Block = 200;      // 블록(결합·들기·도킹)
    public const int EnergyBall = 100;
    public const int Pin = 0;
}

/// <summary>
/// 기믹이 "지금 이 키로 할 수 있는 것" 하나를 중앙 컨트롤러에 올리는 형식. 기믹은 <c>Input</c>을 직접
/// 읽지 않는다. <c>enabled=false</c>면 실행되지 않고, 누르면 <c>reason</c>이 화면에 뜬다(무반응 금지).
/// <c>execute</c>는 매 프레임 새 람다를 만들지 않도록 제공자가 필드로 캐시한 델리게이트를 넘긴다.
/// </summary>
public struct InteractionAction
{
    public InteractionChannel channel;
    public InteractionTrigger trigger;
    public string verb;
    public bool enabled;
    public string reason;
    public int priority;
    public float distance;
    public Action execute;
    /// <summary>붙잡힌 상태(탑승·도킹 중)에서도 올리는 해제 액션인가. 붙잡는 쪽이 자기 점유자에게만 올린다.</summary>
    public bool allowWhenGripped;
}

public interface IInteractionProvider
{
    /// <summary>지금 올릴 수 있는 액션을 <paramref name="into"/>에 추가한다(비우지 말 것).</summary>
    void CollectActions(List<InteractionAction> into);
}
