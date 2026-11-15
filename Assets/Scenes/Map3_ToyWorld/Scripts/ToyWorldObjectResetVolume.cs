using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class ToyWorldObjectResetVolume : MonoBehaviour
{
    private void Awake()
    {
        if (!GetComponent<Collider>().isTrigger)
            Debug.LogError("[ToyWorld] Object reset volume must be a trigger.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        PuzzleResettable resettable = other.GetComponentInParent<PuzzleResettable>();
        if (resettable != null) resettable.ResetPuzzleObject();
    }
}
