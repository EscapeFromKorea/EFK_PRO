using UnityEngine;

/// <summary>
/// 플레이어가 밟으면 패드가 바라보는 방향(forward)으로
/// 즉시 고정 속도만큼 가속시키는 부스터 패드.
/// 실제 velocity 제어는 PlayerAccelReceiver가 담당한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AccelPad : MonoBehaviour
{
    [Header("가속 설정")]
    [Tooltip("패드가 플레이어에게 부여하는 고정 속도 (m/s)")]
    public float boostSpeed = 20f;

    [Tooltip("가속된 속도가 그대로 유지되는 시간 (초)")]
    public float holdDuration = 1f;

    [Tooltip("유지 시간 이후 0으로 감속되는 데 걸리는 시간 (초)")]
    public float decelDuration = 1.5f;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerAccelReceiver receiver = other.GetComponentInParent<PlayerAccelReceiver>();
        if (receiver == null) return;

        Vector3 boostDir = transform.forward;
        boostDir.y = 0f;
        boostDir.Normalize();

        receiver.ApplyBoost(boostDir * boostSpeed, holdDuration, decelDuration);
    }

    private void OnDrawGizmos()
    {
        Vector3 origin = transform.position + Vector3.up * 0.3f;
        Vector3 dir = transform.forward;
        dir.y = 0f;
        dir.Normalize();

        float length = 2f;
        Vector3 tip = origin + dir * length;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, tip);

        // 화살촉
        Vector3 right = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 150f, 0) * Vector3.forward;
        Vector3 left = Quaternion.LookRotation(dir) * Quaternion.Euler(0, -150f, 0) * Vector3.forward;
        Gizmos.DrawLine(tip, tip + right * 0.5f);
        Gizmos.DrawLine(tip, tip + left * 0.5f);
    }
}