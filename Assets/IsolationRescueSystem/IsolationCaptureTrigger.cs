using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 격리 진입점 트리거(C2_CAPTURE) — 진입점을 안쪽으로 "완전히 통과한" 첫 참가자를 격리 후보로 컨트롤러에 요청하고,
/// 준비 중 그 후보가 바깥으로 되돌아 나오면 격리를 취소시킨다. docs/PRD/IsolationRescue.md §2.1~2.2, §3.
///
/// [완전히 통과 = 안쪽 방향으로 트리거를 벗어남]
/// 트리거 볼륨을 출입구에 걸쳐 놓고, 참가자의 마지막 콜라이더가 볼륨을 벗어나는 순간 어느 쪽으로 나갔는지를
/// 본다. 안쪽(insideDirection 방향, 중심 기준 minDepth 이상)으로 나갔으면 통과, 바깥쪽으로 나갔으면
/// 되돌아간 것이다. 문턱에 걸치기만 한 참가자는 후보가 되지 않는다.
///
/// [동시 진입 — 판정 순서를 결정적으로]
/// 같은 물리 스텝에 여러 명이 통과해도 컨트롤러는 먼저 요청한 한 명만 받는다(RequestCapture의 첫 유효
/// 요청 규칙). 그런데 OnTriggerExit 호출 순서는 비결정적이라 "첫 번째"가 매번 달라질 수 있다. 그래서 이번
/// 스텝에 통과한 후보를 모았다가 다음 FixedUpdate에서 안쪽으로 가장 깊이 들어간 순서(같으면 인스턴스 ID
/// 순)로 정렬해 차례로 요청한다 — 한 명만 선정되고 나머지는 바깥에 남는다(두 명이 함께 갇히는 연출 금지).
///
/// [안전검사는 여기서 하지 않는다]
/// 문이 닫히며 끼는지 보는 일은 IsolationEntryDoor가 한다. 이 컴포넌트는 후보 선정과 취소 요청만 한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class IsolationCaptureTrigger : MonoBehaviour
{
    [Tooltip("격리 컨트롤러.")]
    public IsolationRescueController controller;

    [Tooltip("안쪽을 가리키는 방향의 기준. 이 Transform의 forward가 안쪽이다. 비워 두면 이 오브젝트의 forward.")]
    public Transform insideDirection;

    [Tooltip("중심에서 안쪽으로 이 거리(Unit) 이상 벗어나서 나가야 통과로 본다. 0이면 중심을 넘기만 하면 된다.")]
    public float minDepth = 0f;

    private readonly Dictionary<PlayerMover, int> overlaps = new Dictionary<PlayerMover, int>();
    private readonly List<PlayerMover> pending = new List<PlayerMover>();

    private Vector3 InsideForward =>
        (insideDirection != null ? insideDirection : transform).forward;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"[IsolationCaptureTrigger] '{name}'의 Collider가 Trigger가 아니다 — " +
                             "참가자를 물리적으로 막는다. isTrigger를 켜라.", this);
    }

    private void OnTriggerEnter(Collider other) => HandleEnter(other);
    private void OnTriggerExit(Collider other) => HandleExit(other);

    private void FixedUpdate()
    {
        ResolvePending();
    }

    public void HandleEnter(Collider other)
    {
        PlayerMover mover = other != null ? other.GetComponentInParent<PlayerMover>() : null;
        if (mover == null) return;
        overlaps.TryGetValue(mover, out int n);
        overlaps[mover] = n + 1;
    }

    public void HandleExit(Collider other)
    {
        PlayerMover mover = other != null ? other.GetComponentInParent<PlayerMover>() : null;
        if (mover == null || !overlaps.TryGetValue(mover, out int n)) return;

        if (n > 1)
        {
            overlaps[mover] = n - 1;
            return;
        }

        overlaps.Remove(mover);
        OnLeftTrigger(mover);
    }

    // 참가자가 트리거를 완전히 벗어났다. 나간 방향으로 통과/되돌아감을 가른다.
    private void OnLeftTrigger(PlayerMover mover)
    {
        if (controller == null || mover == null) return;

        float depth = DepthOf(mover);

        if (controller.Current == IsolationRescueController.State.Idle)
        {
            if (depth > minDepth && !pending.Contains(mover)) pending.Add(mover);
            return;
        }

        // 준비 중 격리된 참가자가 바깥으로 되돌아 나왔다 → 격리 취소(§2.2). 진행 중에는 컨트롤러가 취소를
        // 받지 않으므로(준비 상태에서만 유효) 따로 가리지 않아도 안전하지만, 의도를 코드로 남긴다.
        if (controller.Current == IsolationRescueController.State.Preparing &&
            controller.InsidePlayer == mover.gameObject &&
            depth < 0f)
        {
            Debug.Log($"[IsolationCaptureTrigger] 격리 후보 '{mover.name}'가 바깥으로 되돌아 나와 격리를 취소한다.", this);
            controller.CancelCapture();
        }
    }

    /// <summary>중심에서 안쪽 방향으로 얼마나 떨어져 있는가(음수 = 바깥쪽).</summary>
    public float DepthOf(PlayerMover mover)
    {
        return Vector3.Dot(mover.transform.position - transform.position, InsideForward);
    }

    /// <summary>
    /// 모아 둔 후보를 판정 순서(안쪽으로 깊은 순, 같으면 인스턴스 ID 순)로 요청한다. 컨트롤러가 첫 유효
    /// 요청만 받으므로 한 명만 선정된다. 그 사이 다시 트리거로 돌아온 참가자는 통과가 아니므로 거른다.
    /// 요청 결과로 선정된 참가자를 돌려준다(없으면 null).
    /// </summary>
    public PlayerMover ResolvePending()
    {
        if (pending.Count == 0) return null;

        // 정렬 키가 위치라서 해결 시점에 다시 계산한다.
        pending.RemoveAll(m => m == null || overlaps.ContainsKey(m));
        pending.Sort((a, b) =>
        {
            int byDepth = DepthOf(b).CompareTo(DepthOf(a));
            return byDepth != 0 ? byDepth : a.GetInstanceID().CompareTo(b.GetInstanceID());
        });

        PlayerMover chosen = null;
        foreach (PlayerMover candidate in pending)
        {
            if (controller != null && controller.RequestCapture(candidate.gameObject))
            {
                chosen = candidate;
                break;
            }
        }

        // 선정이 끝났든 거부됐든 이번 스텝 후보는 비운다 — 다음 통과는 새 후보로 다시 모인다.
        pending.Clear();
        return chosen;
    }

    private void OnDrawGizmos()
    {
        Vector3 forward = InsideForward;
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
        Gizmos.DrawLine(transform.position, transform.position + forward * 2f);
        Gizmos.DrawSphere(transform.position + forward * 2f, 0.2f);
    }
}
