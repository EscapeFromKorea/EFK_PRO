using System.Collections;
using UnityEngine;

/// <summary>동적 퍼즐 오브젝트의 최초 안전 상태를 보관한다.</summary>
public sealed class PuzzleResettable : MonoBehaviour, IPuzzleResettable
{
    public float autoResetBelowY = -8f;
    public bool resetScale;

    private Rigidbody body;
    private Vector3 resetPosition;
    private Quaternion resetRotation;
    private Vector3 resetLocalScale;
    private bool captured;
    private bool resetPending;

    public bool NeedsAutomaticReset => captured && !resetPending && transform.position.y < autoResetBelowY;

    private void Awake() => CaptureResetState();

    public void CaptureResetState()
    {
        body = GetComponent<Rigidbody>();
        resetPosition = body != null ? body.position : transform.position;
        resetRotation = body != null ? body.rotation : transform.rotation;
        resetLocalScale = transform.localScale;
        captured = true;
    }

    public void ResetPuzzleObject()
    {
        if (resetPending) return;
        if (!captured) CaptureResetState();
        // Release through the existing SnapBlock API before teleporting either joint body.
        SnapBlock block = GetComponent<SnapBlock>();
        if (block != null && isActiveAndEnabled)
        {
            block.DetachAll();
            resetPending = true;
            StartCoroutine(ResetAfterJointDestruction());
            return;
        }
        ApplyResetPose();
    }

    private IEnumerator ResetAfterJointDestruction()
    {
        // SnapBlock.DetachAll uses Destroy(joint), whose native constraint survives until frame end.
        // Wait before teleporting, otherwise the still-live constraint projects the body back.
        yield return null;
        ApplyResetPose();
        resetPending = false;
    }

    private void ApplyResetPose()
    {
        transform.SetPositionAndRotation(resetPosition, resetRotation);
        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.position = resetPosition;
            body.rotation = resetRotation;
            body.Sleep();
        }
        if (resetScale) transform.localScale = resetLocalScale;
        Physics.SyncTransforms();
    }

    private void OnDisable() => resetPending = false;
}
