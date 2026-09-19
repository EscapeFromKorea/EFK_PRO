using System;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public sealed class ToyWorldLevelDirector : MonoBehaviour
{
    public static ToyWorldLevelDirector Instance { get; private set; }

    [Header("Scene references")]
    [Tooltip("Existing DoorSystem door opened when all three parts have been collected.")]
    public doorPhysics finalGate;
    [Tooltip("Existing DoorSystem door opened after all three parts have been installed.")]
    public doorPhysics installationGate;
    public MusicBoxRepairController musicBox;
    public HubProgressDisplay hubProgress;

    [Header("Debug")]
    public bool verboseLogging = true;

    private readonly ToyWorldProgressState state = new ToyWorldProgressState();

    public event Action ProgressChanged;

    public int CollectedCount => state.CollectedCount;
    public int InstalledCount => state.InstalledCount;
    public bool AllItemsCollected => state.AllCollected;
    public bool AllItemsInstalled => state.AllInstalled;
    public bool IsMusicBoxActivated => state.MusicBoxActivated;
    public bool IsLevelCompleted => state.LevelCompleted;
    public bool CanUseExit => state.CanExit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("[ToyWorld] ToyWorldLevelDirector must be unique.", this);
            enabled = false;
            return;
        }

        Instance = this;
        ApplyProgressToScene();
    }

    private void Start() => ApplyProgressToScene();

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool HasItem(ToyWorldRepairItemType type) => state.IsCollected(type);
    public bool IsInstalled(ToyWorldRepairItemType type) => state.IsInstalled(type);

    public bool TryCollectItem(ToyWorldRepairItemType type)
    {
        if (!state.TryCollect(type)) return false;

        if (verboseLogging)
            Debug.Log($"[ToyWorld] Collected {type}. Progress {state.CollectedCount}/3.", this);

        ApplyProgressToScene();
        return true;
    }

    public bool TryInstallItem(ToyWorldRepairItemType type)
    {
        if (!state.TryInstall(type))
        {
            if (verboseLogging)
                Debug.Log($"[ToyWorld] Cannot install {type}. Collect all parts and use the order Spring > Gear > Cylinder.", this);
            return false;
        }

        if (verboseLogging)
            Debug.Log($"[ToyWorld] Installed {type}. Installed {state.InstalledCount}/3.", this);

        ApplyProgressToScene();
        return true;
    }

    public bool TryActivateMusicBox()
    {
        if (!state.TryActivateMusicBox()) return false;

        if (verboseLogging)
            Debug.Log("[ToyWorld] Broken Music Box activated. The exit route is now valid.", this);

        ApplyProgressToScene();
        return true;
    }

    public bool TryCompleteLevel()
    {
        if (!state.TryCompleteLevel())
        {
            if (verboseLogging)
                Debug.Log("[ToyWorld] Exit rejected: collect, install, and activate all three repair parts first.", this);
            return false;
        }

        Debug.Log("[ToyWorld] MAP 3 COMPLETE", this);
        ApplyProgressToScene();
        return true;
    }

    private void ApplyProgressToScene()
    {
        if (finalGate != null)
            finalGate.SetPadPressed(state.AllCollected);
        if (installationGate != null)
            installationGate.SetPadPressed(state.AllInstalled);
        if (hubProgress != null)
            hubProgress.Refresh(this);
        if (musicBox != null)
            musicBox.RefreshInstallVisuals();

        ProgressChanged?.Invoke();
    }
}
