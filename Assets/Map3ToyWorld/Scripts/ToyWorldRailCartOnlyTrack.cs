using UnityEngine;

/// <summary>Keeps the physical track solid for carts/cargo while player shapes fall through it.</summary>
public sealed class ToyWorldRailCartOnlyTrack : MonoBehaviour
{
    public Collider[] trackColliders;

    private void Awake()
    {
        if (trackColliders == null) return;
        foreach (PlayerMover player in FindObjectsOfType<PlayerMover>(true))
        {
            foreach (Collider playerCollider in player.GetComponentsInChildren<Collider>(true))
            {
                if (playerCollider == null) continue;
                foreach (Collider trackCollider in trackColliders)
                    if (trackCollider != null) Physics.IgnoreCollision(trackCollider, playerCollider, true);
            }
        }
    }
}
