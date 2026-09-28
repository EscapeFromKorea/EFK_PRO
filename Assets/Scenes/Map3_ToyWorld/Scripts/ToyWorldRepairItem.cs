using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class ToyWorldRepairItem : MonoBehaviour
{
    public ToyWorldRepairItemType itemType;
    public ToyWorldLevelDirector director;
    public GameObject visualRoot;

    private Collider triggerCollider;
    private bool collected;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        if (!triggerCollider.isTrigger)
            Debug.LogError("[ToyWorld] Repair item collider must be a trigger.", this);
    }

    private void Start()
    {
        ResolveDirector();
        if (director != null && director.HasItem(itemType))
            ApplyCollectedState();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected || other.GetComponentInParent<PlayerMover>() == null) return;
        ResolveDirector();
        if (director == null || !director.TryCollectItem(itemType)) return;

        ApplyCollectedState();
    }

    private void ApplyCollectedState()
    {
        collected = true;
        if (triggerCollider != null) triggerCollider.enabled = false;
        if (visualRoot != null) visualRoot.SetActive(false);
    }

    private void ResolveDirector()
    {
        if (director == null) director = ToyWorldLevelDirector.Instance;
    }
}
