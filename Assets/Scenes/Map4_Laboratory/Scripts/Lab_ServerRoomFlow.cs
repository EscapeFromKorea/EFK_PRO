using UnityEngine;

/// <summary>
/// CH7 맵 연결만 담당한다. 팀 기믹의 공개 API로 입구 회로를 배정하고,
/// 실험 시작 전에는 문답 제출을 막는다. 전력 감소·회복·문답 판정은 팀 컴포넌트가 맡는다.
/// </summary>
[DefaultExecutionOrder(200)] // 팀 컴포넌트(기본 0)의 Bind/ResetAll 뒤에 구독·게이트 차단
public sealed class Lab_ServerRoomFlow : MonoBehaviour
{
    public WiringPanel entryPanel;
    public WiringCircuitData entryCircuit;
    public RoleAssignmentManager roles;
    public QuizTerminal quiz;
    public PowerMaintenanceController power;
    public doorPhysics entryDoor;
    public doorPhysics exitDoor;

    private bool subscribed;

    private void OnEnable()
    {
        InitializeAndSubscribe();
    }

    private void Start()
    {
        // 생성기 AddComponent 시에는 필드가 아직 비어 있다. 활성화 후 한 번 더 연결한다.
        InitializeAndSubscribe();
    }

    private void OnDisable()
    {
        if (!subscribed) return;
        if (roles != null) roles.ChapterReset -= ResetChapter;
        if (power != null) power.onStarted.RemoveListener(AllowRunningSubmission);
        subscribed = false;
    }

    private void InitializeAndSubscribe()
    {
        if (entryPanel == null || entryCircuit == null || roles == null || quiz == null || power == null)
        {
            // AddComponent 직후의 OnEnable에서는 아직 필드가 대입되지 않았을 수 있다.
            return;
        }

        if (!subscribed)
        {
            roles.ChapterReset += ResetChapter;
            power.onStarted.AddListener(AllowRunningSubmission);
            subscribed = true;
            AssignEntry();
        }

        if (power.Current == PowerMaintenanceController.State.Ready)
            quiz.SetSubmitGate(false);
    }

    private void Update()
    {
        // PowerMaintenanceController.ResetAll()은 기본 게이트를 다시 연다. Ready 동안 계속 닫아 둔다.
        if (quiz != null && power != null && power.Current == PowerMaintenanceController.State.Ready && quiz.SubmitGateOpen)
            quiz.SetSubmitGate(false);
    }

    private void AssignEntry()
    {
        if (!entryPanel.AssignCircuit(entryCircuit, 1))
            Debug.LogError("[S7] ENTRY 회로 배정 실패 — 입구 문과 실험 시작이 잠긴다.", this);
    }

    private void AllowRunningSubmission()
    {
        if (quiz != null && power != null && power.Current == PowerMaintenanceController.State.Running)
            quiz.SetSubmitGate(!power.SubmitLocked);
    }

    private void ResetChapter()
    {
        if (quiz != null) quiz.SetSubmitGate(false);
        if (entryDoor != null) entryDoor.SetPadPressed(false);
        if (exitDoor != null) exitDoor.SetPadPressed(false);
        if (entryPanel != null && entryCircuit != null) AssignEntry();
    }
}
