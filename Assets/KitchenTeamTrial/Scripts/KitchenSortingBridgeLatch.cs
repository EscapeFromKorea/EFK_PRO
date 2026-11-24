using UnityEngine;

/// <summary>
/// The second shape reaches the far plate while the first holds the temporary
/// bridge. The permanent bridge then stays present so the holder can follow.
/// The server owns the latch and publishes it to every player.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class KitchenSortingBridgeLatch : MonoBehaviour, IMpSynced
{
    public GameObject permanentBridge;
    public GameObject temporaryBridge;
    public BoxCollider holdPlate;

    private uint mpId;
    private bool latched;
    private Renderer[] bridgeRenderers;
    private Collider[] bridgeColliders;
    private Collider temporaryDeck;

    public bool IsLatched => latched;

    private void Awake()
    {
        if (permanentBridge != null)
        {
            bridgeRenderers = permanentBridge.GetComponentsInChildren<Renderer>(true);
            bridgeColliders = permanentBridge.GetComponentsInChildren<Collider>(true);
        }
        if (temporaryBridge != null)
            temporaryDeck = temporaryBridge.GetComponentInChildren<Collider>(true);
        if (holdPlate == null && transform.parent != null)
        {
            var hold = transform.parent.GetComponentInChildren<RainbowBridgeSwitch>(true);
            if (hold != null) holdPlate = hold.GetComponent<BoxCollider>();
        }
        mpId = MpSync.Register(this);
        Apply();
    }

    private void OnDestroy() => MpSync.Unregister(mpId, this);

    public void OnMpValue(int channel, float value)
    {
        if (channel != 0) return;
        latched = value > .5f;
        Apply();
    }

    private void OnTriggerEnter(Collider other) => TryLatch(other);
    private void OnTriggerStay(Collider other) => TryLatch(other);

    private void TryLatch(Collider other)
    {
        if (MpSync.IsClient || latched || !other.CompareTag("Player")) return;
        if (!temporaryDeck || !temporaryDeck.enabled || !temporaryDeck.gameObject.activeInHierarchy ||
            !holdPlate || !holdPlate.enabled || !holdPlate.gameObject.activeInHierarchy) return;

        // The switch updates its cached visibility each frame. Check physical
        // occupancy too, so releasing the west plate in this tick cannot latch it.
        var t = holdPlate.transform;
        foreach (var holder in Physics.OverlapBox(t.TransformPoint(holdPlate.center),
            Vector3.Scale(t.lossyScale, holdPlate.size) * .5f, t.rotation, ~0, QueryTriggerInteraction.Collide))
        {
            if (!holder.CompareTag("Player") && !holder.CompareTag("InteractionItem")) continue;
            Latch();
            break;
        }
    }

    private void Latch()
    {
        latched = true;
        Apply();
        MpSync.Publish(mpId, 0, 1f);
    }

    private void Update()
    {
        if (!MpSync.IsClient) MpSync.Publish(mpId, 0, latched ? 1f : 0f);
    }

    private void Apply()
    {
        if (bridgeRenderers != null)
            foreach (Renderer r in bridgeRenderers)
                if (r != null) r.enabled = latched;
        if (bridgeColliders != null)
            foreach (Collider c in bridgeColliders)
                if (c != null) c.enabled = latched;
        // The permanent deck takes over as one surface. This prevents two
        // coincident colliders and renderers from fighting after the relay.
        if (temporaryBridge != null && temporaryBridge.activeSelf == latched)
            temporaryBridge.SetActive(!latched);
    }
}
