using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// E 키로 누르는 격리실 버튼류의 공통 베이스(준비 확인·최종 해제). 트리거 안에서 "조작 중인" 참가자가
/// 상호작용 키를 눌렀을 때만 <see cref="Press"/>를 부른다. docs/PRD/IsolationRescue.md §3.
///
/// [키는 직접 읽지 않는다]
/// 2026-10-03 키맵 통합으로 E는 InteractionController가 한 번만 읽는다. 이 버튼은 "지금 이 키로 할 수
/// 있는 것"을 <see cref="IInteractionProvider"/>로 올리고, 같은 자리의 다른 E 기믹과 동시에 반응하지
/// 않게 컨트롤러가 후보 하나만 실행한다. 붙잡힌 몸(탑승·도킹·매달림·벽 부착)은 컨트롤러가 걸러 준다.
///
/// [무반응 금지]
/// 같은 통합안의 규칙 2("불가능할 땐 이유를 표시한다")에 따라, 눌렀는데 안 되는 경우는 <see cref="Say"/>로
/// 이유를 HUD에 띄운다. HUD가 없으면 로그만 남긴다.
/// </summary>
public abstract class IsolationInteractZone : IsolationOccupancyZone, IInteractionProvider
{
    [Tooltip("거부 사유를 띄울 HUD. 비워 두면 로그만 남긴다.")]
    public IsolationRescueHud hud;

    /// <summary>상호작용 안내에 표시할 동사. 파생 클래스가 버튼에 맞게 바꾼다.</summary>
    protected virtual string Verb => "누르기";

    private System.Action press;

    private void OnEnable() => InteractionController.Register(this);
    private void OnDisable() => InteractionController.Unregister(this);

    public void CollectActions(List<InteractionAction> into)
    {
        PlayerMover p = InteractionController.Controlled;
        if (p == null || !Contains(p)) return;
        press ??= Press;
        into.Add(new InteractionAction
        {
            channel = InteractionChannel.Hand,
            trigger = InteractionTrigger.Tap,
            verb = Verb,
            enabled = true,
            priority = InteractionPriority.Panel,
            distance = Vector3.Distance(p.transform.position, transform.position),
            execute = press,
        });
    }

    /// <summary>버튼을 한 번 누른다. 입력 처리와 분리해 둔 이유: 자가검증이 키 입력 없이 호출한다.</summary>
    public abstract void Press();

    protected void Say(string message)
    {
        Debug.Log($"[{GetType().Name}] {message}", this);
        if (hud != null) hud.Say(message);
    }
}
