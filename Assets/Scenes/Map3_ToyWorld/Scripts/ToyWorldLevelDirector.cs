using System;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public sealed class ToyWorldLevelDirector : MonoBehaviour, IMpSynced
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
        mpId = MpSync.Register(this);
    }

    // [멀티] 수집·설치·활성화·완료는 팀 공유 상태로 서버만 바꾼다(아이템·소켓·출구 트리거는 서버에서 몸 복사본으로도 발생한다).
    // 채널 0=수집 비트, 1=설치 비트, 2=플래그. 참가자 PC의 Try*는 아무 일도 하지 않는다.
    private uint mpId;

    public void OnMpValue(int channel, float value)
    {
        int bits = (int)value;
        if (channel == 0) state.SetCollectedMask(bits);
        else if (channel == 1) state.SetInstalledMask(bits);
        else if (channel == 2) state.SetFlagBits(bits);
        else return;
        ApplyProgressToScene();
    }

    private void Start() => ApplyProgressToScene();

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        MpSync.Unregister(mpId, this);
    }

    public bool HasItem(ToyWorldRepairItemType type) => state.IsCollected(type);
    public bool IsInstalled(ToyWorldRepairItemType type) => state.IsInstalled(type);

    public bool TryCollectItem(ToyWorldRepairItemType type)
    {
        if (MpSync.IsClient || !state.TryCollect(type)) return false;

        if (verboseLogging)
            Debug.Log($"[ToyWorld] Collected {type}. Progress {state.CollectedCount}/3.", this);

        ApplyProgressToScene();
        return true;
    }

    public bool TryInstallItem(ToyWorldRepairItemType type)
    {
        if (MpSync.IsClient) return false;
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

    public void SetInstallPadPressed(ToyWorldRepairItemType type, bool pressed)
    {
        if (MpSync.IsClient || !state.SetInstallPadPressed(type, pressed)) return;

        if (verboseLogging)
            Debug.Log($"[ToyWorld] Install pad {type}: {(pressed ? "pressed" : "released")}. Active {state.InstalledCount}/3.", this);

        ApplyProgressToScene();
    }

    public bool TryActivateMusicBox()
    {
        if (MpSync.IsClient || !state.TryActivateMusicBox()) return false;

        if (verboseLogging)
            Debug.Log("[ToyWorld] Broken Music Box activated. The exit route is now valid.", this);

        ApplyProgressToScene();
        return true;
    }

    public bool TryCompleteLevel()
    {
        if (MpSync.IsClient) return false;
        if (!state.TryCompleteLevel())
        {
            if (verboseLogging)
                Debug.Log("[ToyWorld] Exit already completed.", this);
            return false;
        }

        Debug.Log("[ToyWorld] MAP 3 COMPLETE", this);
        if (LevelManager.Instance != null) LevelManager.Instance.CompleteLevel();
        ApplyProgressToScene();
        return true;
    }

    private void ApplyProgressToScene()
    {
        if (mpId != 0) // Awake의 첫 반영은 등록 전이다
        {
            MpSync.Publish(mpId, 0, state.CollectedMask);
            MpSync.Publish(mpId, 1, state.InstalledMask);
            MpSync.Publish(mpId, 2, state.FlagBits);
        }
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
