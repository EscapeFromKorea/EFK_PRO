using UnityEngine;

/// <summary>Seeds a visible steam preview when the review scene is opened in Edit Mode.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class KitchenReviewSteamPreview : MonoBehaviour
{
    [SerializeField] private ParticleSystem[] systems;

    public void SetSystems(ParticleSystem[] steamSystems)
    {
        systems = steamSystems;
        ShowPreview();
    }

    private void OnEnable() => ShowPreview();

    private void ShowPreview()
    {
        if (Application.IsPlaying(gameObject) || systems == null) return;

        foreach (ParticleSystem system in systems)
        {
            if (system == null) continue;
            // Simulate leaves the emitters paused so Edit Mode previews are reproducible.
            system.Simulate(3f, false, true, true);
        }
    }
}
