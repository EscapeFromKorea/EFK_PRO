using System.Collections.Generic;
using UnityEngine;

/// <summary>창문 뒤 안전 바닥에 세 도형이 모두 도달했을 때만 완주를 알린다.</summary>
[RequireComponent(typeof(Collider))]
public sealed class KitchenTeamGoal : MonoBehaviour
{
    private readonly HashSet<PlayerMover> inside = new HashSet<PlayerMover>();
    private bool completed;

    private void OnTriggerEnter(Collider other)
    {
        PlayerMover player = other.GetComponentInParent<PlayerMover>();
        if (player != null) inside.Add(player);
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerMover player = other.GetComponentInParent<PlayerMover>();
        if (player == null) return;
        // 각 플레이어는 솔리드와 트리거 콜라이더를 함께 가지므로 실제 위치로 판정한다.
        Collider zone = GetComponent<Collider>();
        if (!zone.bounds.Contains(player.transform.position)) inside.Remove(player);
    }

    private void Update()
    {
        if (completed) return;
        inside.RemoveWhere(player => player == null || !GetComponent<Collider>().bounds.Contains(player.transform.position));
        if (inside.Count < 3) return;
        completed = true;
        Debug.Log("[Kitchen Map2] 세 도형이 모두 창문 뒤 안전 바닥에 도착했습니다. 레벨 완주!");
    }

    private void Reset() => GetComponent<Collider>().isTrigger = true;
}
