using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class ToyWorldExitTrigger : MonoBehaviour
{
    public ToyWorldLevelDirector director;

    private void Awake()
    {
        if (!GetComponent<Collider>().isTrigger)
            Debug.LogError("[ToyWorld] Exit collider must be a trigger.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerMover>() == null) return;
        if (director == null) director = ToyWorldLevelDirector.Instance;
        if (director != null) director.TryCompleteLevel();
    }
}
