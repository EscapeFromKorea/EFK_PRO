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

    [Tooltip("켜면 한 번 밟는 순간부터 계속 눌린 상태로 남는다(발판에서 내려와도 유지). 끄면 밟고 있는 " +
             "동안만 눌린다(기본). 플레이 중에 끄면 그 즉시 원래 방식으로 돌아간다.")]
    public bool latchOnFirstPress = false;

    private bool latched;

    private readonly Dictionary<Rigidbody, int> overlaps = new Dictionary<Rigidbody, int>();

    /// <summary>지금 이 발판을 밟고 있는 몸이 하나 이상 있는가. <see cref="latchOnFirstPress"/>가 켜져 있으면
    /// 한 번이라도 밟힌 뒤로는 계속 true다.</summary>
    public bool IsHeld => overlaps.Count > 0 || (latchOnFirstPress && latched);

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag) && !other.CompareTag("InteractionItem")) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        overlaps.TryGetValue(rb, out int n);
        overlaps[rb] = n + 1;
        if (latchOnFirstPress) latched = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag) && !other.CompareTag("InteractionItem")) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        if (!overlaps.TryGetValue(rb, out int n)) return;
        if (n <= 1) overlaps.Remove(rb);
        else overlaps[rb] = n - 1;
    }
}
