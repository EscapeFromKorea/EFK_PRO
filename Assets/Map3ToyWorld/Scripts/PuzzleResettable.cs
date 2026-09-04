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

    public bool NeedsAutomaticReset => captured && transform.position.y < autoResetBelowY;

    private void Awake() => CaptureResetState();

    public void CaptureResetState()
    {
        body = GetComponent<Rigidbody>();
        resetPosition = transform.position;
        resetRotation = transform.rotation;
        resetLocalScale = transform.localScale;
        captured = true;
    }

    public void ResetPuzzleObject()
    {
        if (!captured) CaptureResetState();

        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = resetPosition;
            body.rotation = resetRotation;
            body.Sleep();
        }
        else
        {
            transform.SetPositionAndRotation(resetPosition, resetRotation);
        }

        if (resetScale) transform.localScale = resetLocalScale;
        gameObject.SendMessage("OnPuzzleReset", SendMessageOptions.DontRequireReceiver);
    }
}
