using UnityEngine;

namespace Map2KitchenDraft
{
    public enum CreatureKind { Rat, Cockroach }

    /// <summary>Creature position and intended activity space; AI and capture are pending.</summary>
    public sealed class DraftCreatureMarker : MonoBehaviour
    {
        public CreatureKind kind;
        public int chapterIndex;
        public BoxCollider activityBounds;
        public bool visualOnly = true;

        public bool IsInsideActivityBounds(Vector3 point)
        {
            return activityBounds != null && activityBounds.bounds.Contains(point);
        }
    }
}
