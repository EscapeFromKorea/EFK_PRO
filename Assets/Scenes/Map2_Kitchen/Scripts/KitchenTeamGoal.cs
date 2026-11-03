using System.Collections.Generic;
using UnityEngine;

/// <summary>창문 뒤 안전 바닥에 세 도형이 모두 도달했을 때만 완주를 알린다.</summary>
[RequireComponent(typeof(Collider))]
public sealed class KitchenTeamGoal : MonoBehaviour
{
    [Tooltip("완주에 필요한 인원입니다. 기본은 3명이며 솔로 테스트 시 1로 설정할 수 있습니다.")]
    [Min(1)] public int requiredPlayerCount = 3;
    private readonly HashSet<PlayerMover> inside = new HashSet<PlayerMover>();
    private bool completed;
    private Collider zone;

    private void Awake() => zone = GetComponent<Collider>();

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
        if (!zone.bounds.Contains(player.transform.position)) inside.Remove(player);
    }

    private void Update()
    {
        if (completed) return;
        Bounds goalBounds = zone.bounds;
        inside.RemoveWhere(player => player == null || !player.isActiveAndEnabled || !goalBounds.Contains(player.transform.position));
        if (inside.Count < Mathf.Max(1, requiredPlayerCount)) return;
        completed = true;
        Debug.Log($"[Kitchen Map2] 목표 인원 {requiredPlayerCount}명이 창문 뒤 안전 바닥에 도착했습니다. 레벨 완주!");
    }

    private void Reset() => GetComponent<Collider>().isTrigger = true;
}
