using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 레일카 화물 적재 트리거. "딱딱 블록"은 다른 담당자가 구현할 별도 기믹(docs/PRD/RailCart.md
/// §3.3, 이 저장소에 아직 없음)이라 구체 타입을 몰라야 한다 — 그래서 트리거 안의 아무 Rigidbody나
/// 질량 합산 대상으로 삼는다(`DestructionSystem`이 순수 `Rigidbody.mass`로 재는 것과 같은 이유).
/// 그 블록 시스템이 나중에 들어와도 이 파일은 고칠 필요가 없다. 플레이어(탑승자)는 화물이
/// 아니므로 태그로 제외한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RailCartCargoBay : MonoBehaviour
{
    private readonly HashSet<Rigidbody> cargo = new HashSet<Rigidbody>();

    public float TotalCargoMass()
    {
        float total = 0f;
        foreach (Rigidbody rb in cargo)
            if (rb != null) total += rb.mass;
        return total;
    }

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) return; // 탑승자는 화물이 아니다.
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null) cargo.Add(rb);
    }

    void OnTriggerExit(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null) cargo.Remove(rb);
    }
}
