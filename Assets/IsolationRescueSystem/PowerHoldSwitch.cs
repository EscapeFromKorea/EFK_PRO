using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 외부 유지 스위치(C2_POWER) — 바깥에서 누르고 있는 동안만 켜지는 압력판. 켜짐/꺼짐이 바뀔 때마다 컨트롤러의
/// <see cref="IsolationRescueController.SetPowerHold"/>로 현재 상태를 전달한다(최종 해제의 전제 조건 중 하나).
/// docs/PRD/IsolationRescue.md §3, §4(최종 해제 조건).
///
/// [PadTrigger와 같은 눌림 규칙]
/// Player 또는 InteractionItem 태그가 올라서 있으면 켜진다 — 문 패드(PadTrigger)와 같은 기준이라 사람이
/// 서 있어도, 상자를 올려 두어도 "유지"가 된다. 상자로 고정하면 바깥 인원 한 명이 자유로워지는데 그것을
/// 막을지는 기획 결정 사항이라(PRD에 없음) 막지 않았다.
///
/// [콜라이더 단위 집합]
/// 플레이어는 콜라이더가 여럿이라 Enter/Exit가 중복으로 온다. 겹친 콜라이더를 집합으로 들고, 파괴되었거나
/// 꺼진 콜라이더(Exit가 오지 않음)는 판정 때마다 걸러 낸다 — 눌림이 영영 풀리지 않는 사고를 막는다.
/// </summary>
public class PowerHoldSwitch : MonoBehaviour
{
    [Tooltip("격리 컨트롤러. 스위치 상태가 바뀔 때마다 SetPowerHold로 알린다.")]
    public IsolationRescueController controller;

    private readonly HashSet<Collider> pressers = new HashSet<Collider>();
    private bool lastReported;
    private bool reportedOnce;

    /// <summary>지금 켜져 있는가(누르는 콜라이더가 하나라도 있는가).</summary>
    public bool IsOn
    {
        get
        {
            Prune();
            return pressers.Count > 0;
        }
    }

    private void OnTriggerEnter(Collider other) => HandleEnter(other);
    private void OnTriggerExit(Collider other) => HandleExit(other);

    private void FixedUpdate()
    {
        // Exit 없이 사라진 콜라이더(파괴·비활성)를 반영한다.
        Refresh();
    }

    /// <summary>OnTriggerEnter가 부른다. 대상 태그가 아니면 무시한다.</summary>
    public void HandleEnter(Collider other)
    {
        if (!IsPresser(other)) return;
        pressers.Add(other);
        Refresh();
    }

    /// <summary>OnTriggerExit가 부른다.</summary>
    public void HandleExit(Collider other)
    {
        if (other == null) return;
        pressers.Remove(other);
        Refresh();
    }

    private static bool IsPresser(Collider other) =>
        other != null && (other.CompareTag("Player") || other.CompareTag("InteractionItem"));

    private void Prune()
    {
        pressers.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
    }

    /// <summary>
    /// 눌림 상태를 다시 계산해 바뀌었으면 컨트롤러에 알린다(첫 호출은 항상 알려 초기값을 맞춘다).
    /// Exit 없이 사라진 콜라이더를 반영하려고 FixedUpdate가 매 스텝 부른다.
    /// </summary>
    public void Refresh()
    {
        bool on = IsOn;
        if (reportedOnce && on == lastReported) return;

        reportedOnce = true;
        lastReported = on;
        if (controller != null) controller.SetPowerHold(on);
    }

    private void OnDisable()
    {
        // 스위치가 꺼지면(오브젝트 비활성) 눌림도 풀린 것으로 본다.
        pressers.Clear();
        if (reportedOnce && lastReported && controller != null) controller.SetPowerHold(false);
        lastReported = false;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(transform.position, transform.lossyScale);
    }
}
