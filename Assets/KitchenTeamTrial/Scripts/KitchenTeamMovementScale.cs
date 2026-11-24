using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Map 2 only: applies the trial's horizontal movement multiplier after shape stats
/// initialize. MpLobby instantiates its own player prefabs, so scene player overrides
/// alone would leave network players at their normal speed.
/// </summary>
public sealed class KitchenTeamMovementScale : MonoBehaviour
{
    public const string ScenePath = "Assets/KitchenTeamTrial/Map2KitchenTeamTrial.unity";

    [Min(0.1f)] public float horizontalMultiplier = 1.5f;

    private readonly Dictionary<PlayerMover, (float baseline, float applied)> changed =
        new Dictionary<PlayerMover, (float baseline, float applied)>();
    private float nextNetworkScan;
    private bool scenePlayersApplied;

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void LateUpdate()
    {
        Scene scene = gameObject.scene;
        if (scene.path != ScenePath || SceneManager.GetActiveScene() != scene) return;
        if (!scenePlayersApplied)
        {
            scenePlayersApplied = true;
            // LateUpdate runs after the frame's PlayerShapeIdentity.Start calls.
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (PlayerMover mover in root.GetComponentsInChildren<PlayerMover>(true))
                    ApplyIfAtBaseSpeed(mover);
        }
        if (!MpSync.Active || Time.unscaledTime < nextNetworkScan) return;
        nextNetworkScan = Time.unscaledTime + 0.5f;

        // MpSync already keeps a half-second cache of spawned NetProtoPlayer bodies.
        // No repeated full-scene component search is needed here.
        foreach (PlayerMover mover in MpSync.Bodies)
            ApplyIfAtBaseSpeed(mover);
    }

    private void ApplyIfAtBaseSpeed(PlayerMover mover)
    {
        if (mover == null || changed.ContainsKey(mover)) return;
        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        if (identity == null || identity.stats == null) return;
        float baseline = identity.stats.moveSpeed;
        // Respect any other runtime override instead of fighting it each frame.
        if (!Mathf.Approximately(mover.moveSpeed, baseline)) return;
        float adjusted = baseline * horizontalMultiplier;
        mover.moveSpeed = adjusted;
        changed.Add(mover, (baseline, adjusted));
    }

    private void OnActiveSceneChanged(Scene previous, Scene current)
    {
        if (previous == gameObject.scene) RestoreSpeeds();
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        RestoreSpeeds();
    }

    private void RestoreSpeeds()
    {
        // Persistent multiplayer bodies must not carry Map 2 speed into Map 3.
        foreach (var entry in changed)
        {
            PlayerMover mover = entry.Key;
            if (mover == null) continue;
            if (Mathf.Approximately(mover.moveSpeed, entry.Value.applied))
                mover.moveSpeed = entry.Value.baseline;
        }
        changed.Clear();
        nextNetworkScan = 0f;
        scenePlayersApplied = false;
    }
}
