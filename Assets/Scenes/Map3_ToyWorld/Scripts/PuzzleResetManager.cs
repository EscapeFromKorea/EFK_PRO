using System.Collections.Generic;
using UnityEngine;

public sealed class PuzzleResetManager : MonoBehaviour, IMpEventSink
{
    public KeyCode resetKey = KeyCode.Backspace;
    public float holdSeconds = 1.5f;
    public bool verboseLogging = true;

    private readonly List<PuzzleResettable> resettables = new List<PuzzleResettable>();
    private float heldFor;

    public float HeldFraction => holdSeconds <= 0f ? 0f : Mathf.Clamp01(heldFor / holdSeconds);

    private uint mpId;

    private void Awake() => mpId = MpSync.Register(this);

    private void OnDestroy() => MpSync.Unregister(mpId, this);

    private void Start() => RebuildCache();

    public void OnMpEvent(int kind, Vector3 data, uint arg)
    {
        if (kind == 0 && MpSync.IsServer) ResetAllPuzzleObjects();
    }

    /// <summary>퍼즐 초기화 요청. 멀티에서는 누가 눌러도 서버가 팀 전체의 퍼즐을 되돌린다.</summary>
    public void RequestReset()
    {
        if (MpSync.Active) MpSync.SendEvent(mpId, 0, Vector3.zero);
        else ResetAllPuzzleObjects();
    }

    private void Update()
    {
        for (int i = 0; i < resettables.Count; i++)
        {
            PuzzleResettable resettable = resettables[i];
            if (resettable != null && resettable.NeedsAutomaticReset)
                resettable.ResetPuzzleObject();
        }

        if (!Input.GetKey(resetKey) || (MpSync.Active && InputGate.Blocked))
        {
            heldFor = 0f;
            return;
        }

        heldFor += Time.unscaledDeltaTime;
        if (heldFor < holdSeconds) return;
        heldFor = 0f;
        RequestReset();
    }

    public void RebuildCache()
    {
        resettables.Clear();
        resettables.AddRange(FindObjectsByType<PuzzleResettable>(FindObjectsSortMode.None));
    }

    public void ResetAllPuzzleObjects()
    {
        for (int i = 0; i < resettables.Count; i++)
            if (resettables[i] != null) resettables[i].ResetPuzzleObject();
        if (verboseLogging)
            Debug.Log($"[ToyWorld] Reset {resettables.Count} puzzle objects. Repair progress was preserved.", this);
    }
}
