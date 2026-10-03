using UnityEngine;

/// <summary>
/// 격리 중 개인 추락 복귀 — 안쪽 참가자는 안쪽 안전점(C2_IN_SAFE)으로, 바깥 참가자는 바깥 안전점(C2_OUT_SAFE)으로
/// 돌려보낸다. 단계·입력·남은 시간은 건드리지 않는다. docs/PRD/IsolationRescue.md §2.8.
///
/// [복귀 경로는 재사용]
/// 실제 이동·연출·점유 검사는 RespawnController.RespawnPlayer(GameObject, SectionSafePoint)가 한다
/// (docs/PRD/SectionRespawn.md의 지정 목적지 복귀). 이 컴포넌트는 "누구를 어느 안전점으로"만 고른다.
///
/// [격리 중이 아니면 공용 복귀]
/// 격리가 진행되지 않는 동안(대기·성공·시간 초과·중단)에는 역할이 없으므로 목적지를 비우고 공용 체크포인트
/// 복귀(RespawnPlayer(GameObject))로 맡긴다.
///
/// [사용]
/// 격리실 아래 추락 구간에 Trigger 볼륨으로 두면 들어온 참가자를 즉시 돌려보낸다. 다른 장치에서 부르려면
/// <see cref="Return"/>을 UnityEvent에 배선한다.
/// </summary>
public class IsolationRoleReturn : MonoBehaviour
{
    [Tooltip("격리 컨트롤러. 누가 안쪽인지 여기서 읽는다.")]
    public IsolationRescueController controller;

    [Tooltip("복귀를 수행할 컨트롤러.")]
    public RespawnController respawn;

    [Tooltip("안쪽 참가자의 복귀 지점(C2_IN_SAFE).")]
    public SectionSafePoint insideSafePoint;

    [Tooltip("바깥 참가자의 복귀 지점(C2_OUT_SAFE).")]
    public SectionSafePoint outsideSafePoint;

    private void OnTriggerEnter(Collider other)
    {
        PlayerMover mover = other != null ? other.GetComponentInParent<PlayerMover>() : null;
        if (mover != null) Return(mover.gameObject);
    }

    /// <summary>이 참가자를 역할에 맞는 안전점으로 복귀시킨다. 복귀를 요청했으면 true.</summary>
    public bool Return(GameObject playerRoot)
    {
        if (playerRoot == null || respawn == null)
        {
            Debug.LogWarning($"[IsolationRoleReturn] '{name}'에 RespawnController가 없거나 대상이 없어 복귀하지 못한다.", this);
            return false;
        }

        SectionSafePoint destination = PickDestination(playerRoot);
        respawn.RespawnPlayer(playerRoot, destination);
        return true;
    }

    /// <summary>
    /// 역할에 맞는 안전점을 고른다. 격리가 진행 중(준비·진행·최종 해제 대기)일 때만 역할이 있고, 그 밖에는
    /// null(= 공용 복귀)이다. 안전점이 비어 있어도 null이라 공용 복귀로 떨어진다.
    /// </summary>
    public SectionSafePoint PickDestination(GameObject playerRoot)
    {
        if (controller == null) return null;

        IsolationRescueController.State s = controller.Current;
        bool isolated = s == IsolationRescueController.State.Preparing ||
                        s == IsolationRescueController.State.InProgress ||
                        s == IsolationRescueController.State.AwaitFinalRelease;
        if (!isolated) return null;

        return controller.InsidePlayer == playerRoot ? insideSafePoint : outsideSafePoint;
    }
}
