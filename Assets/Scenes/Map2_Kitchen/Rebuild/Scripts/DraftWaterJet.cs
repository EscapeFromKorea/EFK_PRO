using UnityEngine;

namespace Map2KitchenDraft
{
    /// <summary>Fixed nozzle visual. It never reads or tracks a player and does not inflict damage.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class DraftWaterJet : MonoBehaviour
    {
        public Vector3 localEnd = new Vector3(0f, -4f, 0f);
        public float width = 0.18f;
        public Material waterMaterial;

        public Vector3 WorldStart => transform.position;
        public Vector3 WorldEnd => transform.TransformPoint(localEnd);
        public bool TracksPlayer => false;

        private LineRenderer line;

        private void OnEnable() => RefreshLine();
        private void OnValidate() => RefreshLine();
        private void Update() => RefreshLine();

        private void RefreshLine()
        {
            if (line == null) line = GetComponent<LineRenderer>();
            if (line == null) return;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, WorldStart);
            line.SetPosition(1, WorldEnd);
            line.startWidth = Mathf.Max(0.01f, width);
            float pulse = Application.isPlaying ? 0.88f + 0.12f * Mathf.Sin(Time.time * 8f) : 1f;
            line.endWidth = Mathf.Max(0.01f, width * pulse * 0.7f);
            line.startColor = new Color(0.18f, 0.94f, 1f, 0.92f);
            line.endColor = new Color(0.42f, 0.98f, 1f, 0.72f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (waterMaterial != null && line.sharedMaterial != waterMaterial)
                line.sharedMaterial = waterMaterial;
        }
    }
}
