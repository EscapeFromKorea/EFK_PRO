using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class ToyRailSwitchPad : MonoBehaviour
{
    public ToyRailSwitch railSwitch;
    public bool latch = true;
    private int overlapCount;

    private void Awake()
    {
        if (!GetComponent<Collider>().isTrigger)
            Debug.LogError("[ToyWorld] Rail switch pad must be a trigger.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerMover>() == null || railSwitch == null) return;
        overlapCount++;
        railSwitch.SetAligned(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<PlayerMover>() == null || railSwitch == null) return;
        overlapCount = Mathf.Max(0, overlapCount - 1);
        if (!latch && overlapCount == 0) railSwitch.SetAligned(false);
    }
}
