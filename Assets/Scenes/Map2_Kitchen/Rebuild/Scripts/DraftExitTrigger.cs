using UnityEngine;

namespace Map2KitchenDraft
{
    /// <summary>Signals arrival at the Map 3 handoff point after the key gate.</summary>
    public sealed class DraftExitTrigger : MonoBehaviour
    {
        public KitchenDraftController controller;

        private void OnTriggerEnter(Collider other)
        {
            if (controller == null) return;
            PlayerMover mover = other.GetComponentInParent<PlayerMover>();
            if (mover != null) controller.NotifyExit(mover.transform);
        }
    }
}
