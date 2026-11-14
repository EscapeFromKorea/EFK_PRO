using UnityEngine;

namespace Map2KitchenDraft
{
    /// <summary>Three-dimensional review region and entry/exit references for one chapter.</summary>
    public sealed class DraftChapterRegion : MonoBehaviour
    {
        public int index;
        public string chapterName;
        public string objective;
        public BoxCollider volume;
        public Transform start;
        public Transform end;
    }
}
