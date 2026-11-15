using System.Collections.Generic;
using UnityEngine;

public sealed class PuzzleResetManager : MonoBehaviour
{
    public KeyCode resetKey = KeyCode.Backspace;
    public float holdSeconds = 1.5f;
    public bool verboseLogging = true;

    private readonly List<PuzzleResettable> resettables = new List<PuzzleResettable>();
    private float heldFor;

    public float HeldFraction => holdSeconds <= 0f ? 0f : Mathf.Clamp01(heldFor / holdSeconds);

    private void Start() => RebuildCache();

    private void Update()
    {
        for (int i = 0; i < resettables.Count; i++)
        {
            PuzzleResettable resettable = resettables[i];
            if (resettable != null && resettable.NeedsAutomaticReset)
                resettable.ResetPuzzleObject();
        }

        if (!Input.GetKey(resetKey))
        {
            heldFor = 0f;
            return;
        }

        heldFor += Time.unscaledDeltaTime;
        if (heldFor < holdSeconds) return;
        heldFor = 0f;
        ResetAllPuzzleObjects();
    }

    public void RebuildCache()
    {
        resettables.Clear();
        resettables.AddRange(FindObjectsOfType<PuzzleResettable>());
    }

    public void ResetAllPuzzleObjects()
    {
        for (int i = 0; i < resettables.Count; i++)
            if (resettables[i] != null) resettables[i].ResetPuzzleObject();
        if (verboseLogging)
            Debug.Log($"[ToyWorld] Reset {resettables.Count} puzzle objects. Repair progress was preserved.", this);
    }
}
