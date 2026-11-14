using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class LevelGoal : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerMover>() != null && LevelManager.Instance != null)
            LevelManager.Instance.CompleteLevel();
    }
}
