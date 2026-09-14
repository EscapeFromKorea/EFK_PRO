using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// <see cref="WindupActivationMode.HoldPad"/>용 발판. 밟고 있는 동안만 <see cref="IsHeld"/>가
/// true다. 겹침 집계는 DoorSystem/ExitWeightPlate와 같은 바디별 카운트 패턴을 그대로 따른다 —
/// 플레이어가 트리거(Player_Mesh)와 솔리드(Player_Collider) 콜라이더를 함께 가져 한 도형당
/// Enter/Exit가 여러 번 불리므로, 바디별로 세지 않으면 콜라이더 하나만 빠져도 눌림이 사라진다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WindupActivationPad : MonoBehaviour
{
    public string playerTag = "Player";

    private readonly Dictionary<Rigidbody, int> overlaps = new Dictionary<Rigidbody, int>();

    /// <summary>지금 이 발판을 밟고 있는 몸이 하나 이상 있는가.</summary>
    public bool IsHeld => overlaps.Count > 0;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        overlaps.TryGetValue(rb, out int n);
        overlaps[rb] = n + 1;
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        if (!overlaps.TryGetValue(rb, out int n)) return;
        if (n <= 1) overlaps.Remove(rb);
        else overlaps[rb] = n - 1;
    }
}
