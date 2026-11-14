using UnityEngine;

/// <summary>
/// 맵4 CH5 전용: 보이지 않는 물리 트리거에 플레이어 콜라이더가 처음 들어오면 함정 하나를 켠다.
/// 접촉식 압력판의 누름/해제나 반복 발동은 구현하지 않는다. 대상은 빌더가 비활성으로 저장한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class Lab_FirstEntryTrap : MonoBehaviour
{
    public GameObject target;
    public bool Activated { get; private set; }

    private void OnTriggerEnter(Collider other)
    {
        if (Activated || target == null || other == null) return;
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null || mover.GetComponent<Rigidbody>() == null) return;

        Activated = true;
        target.SetActive(true);
        enabled = false;
        Debug.Log($"[Lab_FirstEntryTrap] '{mover.name}'가 '{name}'에 진입 — '{target.name}' 첫 활성", this);
    }
}
