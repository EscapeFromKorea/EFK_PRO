using UnityEngine;

/// <summary>Keeps the physical track solid for carts/cargo while player shapes fall through it.</summary>
public sealed class ToyWorldRailCartOnlyTrack : MonoBehaviour
{
    public Collider[] trackColliders;

    private void Awake()
    {
        foreach (PlayerMover player in FindObjectsByType<PlayerMover>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            IgnorePlayer(player);
    }

    /// <summary>이 플레이어와 트랙의 충돌을 끈다. Awake에서 씬에 있던 도형에 한 번 쓰고, 멀티플레이처럼 도형이 나중에 스폰되면 스폰한 쪽이 다시 부른다.</summary>
    public void IgnorePlayer(PlayerMover player)
    {
        if (trackColliders == null || player == null) return;
        foreach (Collider playerCollider in player.GetComponentsInChildren<Collider>(true))
        {
            if (playerCollider == null) continue;
            foreach (Collider trackCollider in trackColliders)
                if (trackCollider != null) Physics.IgnoreCollision(trackCollider, playerCollider, true);
        }
    }
}
