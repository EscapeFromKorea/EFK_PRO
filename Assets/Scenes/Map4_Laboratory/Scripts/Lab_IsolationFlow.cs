using System.Collections.Generic;
using UnityEngine;

/// <summary>맵4 CH2의 씬 간 연결. 팀 격리 기믹은 그대로 사용하고, 성공 후 S4 통과만 처리한다.</summary>
[RequireComponent(typeof(Collider))]
public sealed class Lab_IsolationFlow : MonoBehaviour
{
    public IsolationRescueController controller;
    public IsolationRoleReturn roleReturn;
    public IsolationRescueHud hud;
    [Tooltip("S4 문을 통과하면 세 도형을 함께 CH4에 배치한다.")]
    public bool moveWholeTeam = true;

    public bool HasTransferred { get; private set; }
    private bool transferInProgress;
    private readonly Dictionary<PlayerMover, float> returningOutside = new Dictionary<PlayerMover, float>();

    private void Start()
    {
        if (roleReturn != null) roleReturn.respawn = FindObjectOfType<RespawnController>();
        if (controller != null) controller.onRestarted.AddListener(ResetTransfer);
        Collider trigger = GetComponent<Collider>();
        if (trigger == null || !trigger.isTrigger)
            Debug.LogError("[Lab_IsolationFlow] S4 통과 감지 콜라이더가 Trigger가 아니다.", this);
    }

    private void OnDestroy()
    {
        if (controller != null) controller.onRestarted.RemoveListener(ResetTransfer);
    }

    private void ResetTransfer()
    {
        HasTransferred = false;
        transferInProgress = false;
        returningOutside.Clear();
    }

    private void Update()
    {
        if (controller == null || roleReturn == null || roleReturn.respawn == null) return;
        IsolationRescueController.State state = controller.Current;
        if (state != IsolationRescueController.State.Preparing &&
            state != IsolationRescueController.State.InProgress &&
            state != IsolationRescueController.State.AwaitFinalRelease) return;
        if (controller.InsidePlayer == null) return;

        Transform localFrame = roleReturn.transform.parent;
        foreach (PlayerMover mover in FindObjectsOfType<PlayerMover>())
        {
            if (mover == null || mover.gameObject == controller.InsidePlayer) continue;
            Vector3 p = localFrame.InverseTransformPoint(mover.transform.position);
            bool inRoom = p.x < -14.1f && p.x > -38f && p.z > 30.5f && p.z < 53.5f;
            if (!inRoom) { returningOutside.Remove(mover); continue; }
            // 바깥 안전점 점유/다른 복귀 처리로 거절돼도 실내에 남은 도형은 재시도한다.
            if (!returningOutside.TryGetValue(mover, out float lastRequest) || Time.time - lastRequest >= 1.5f)
            {
                returningOutside[mover] = Time.time;
                Debug.Log($"[Lab_IsolationFlow] 비선정 도형 {mover.name}의 격리실 동시 진입을 바깥 안전점으로 복구한다.", mover);
                roleReturn.Return(mover.gameObject);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerMover mover = other != null ? other.GetComponentInParent<PlayerMover>() : null;
        if (mover != null) TryTransfer(mover.gameObject);
    }

    /// <summary>성공 후 S4 문 뒤에서 처음 감지한 도형만 통과를 확정한다. 테스트에서도 이 공개 API를 사용한다.</summary>
    public bool TryTransfer(GameObject enteringPlayer)
    {
        if (HasTransferred || transferInProgress || controller == null || controller.Current != IsolationRescueController.State.Success)
            return false;
        if (enteringPlayer == null || enteringPlayer.GetComponent<PlayerMover>() == null)
            return false;
        Collider passage = GetComponent<Collider>();
        bool inPassage = false;
        foreach (Collider c in enteringPlayer.GetComponentsInChildren<Collider>())
            if (c.enabled && passage.bounds.Intersects(c.bounds)) { inPassage = true; break; }
        if (!inPassage) return false;

        Map4Director director = FindObjectOfType<Map4Director>();
        if (director == null || !director.TryGetSector(4, out SectorController sector) || sector == null)
        {
            Debug.LogError("[Lab_IsolationFlow] CH4가 아직 로드되지 않아 이동을 보류한다.", this);
            if (hud != null) hud.Say("CH4가 준비되지 않았습니다. 다시 통과해 주세요.");
            return false;
        }
        if (sector.spawnSlots == null || sector.spawnSlots.Length < 3 ||
            sector.spawnSlots[0] == null || sector.spawnSlots[1] == null || sector.spawnSlots[2] == null)
        {
            Debug.LogError("[Lab_IsolationFlow] CH4의 세 도형 스폰 슬롯이 준비되지 않았다.", sector);
            return false;
        }
        RespawnZone checkpoint = null;
        foreach (RespawnZone zone in sector.GetComponentsInChildren<RespawnZone>(true))
            if (zone.name == "CP_S4_Start") { checkpoint = zone; break; }
        if (checkpoint == null)
        {
            Debug.LogError("[Lab_IsolationFlow] CH4 시작 체크포인트 CP_S4_Start가 없다.", sector);
            return false;
        }
        if (FindObjectOfType<RespawnController>() == null)
        {
            Debug.LogError("[Lab_IsolationFlow] 공용 복귀 컨트롤러가 없어 CH4 이동을 보류한다.", this);
            return false;
        }

        if (moveWholeTeam)
        {
            PlayerMover[] movers = FindObjectsOfType<PlayerMover>();
            if (movers.Length != 3)
            {
                Debug.LogError($"[Lab_IsolationFlow] 도형 {movers.Length}개 — 3개여야 함께 이동할 수 있다.", this);
                return false;
            }
            // 슬롯 순서는 게임플레이 차이가 없지만, 재현 가능한 결과를 위해 이름순으로 고정한다.
            System.Array.Sort(movers, (a, b) => string.CompareOrdinal(a.name, b.name));
            var bodies = new List<Rigidbody>(3);
            var positions = new List<Vector3>(3);
            var velocities = new List<Vector3>(3);
            var angularVelocities = new List<Vector3>(3);
            foreach (PlayerMover mover in movers)
            {
                Rigidbody rb = mover.GetComponent<Rigidbody>();
                if (rb == null) { Debug.LogError($"[Lab_IsolationFlow] {mover.name}에 Rigidbody가 없다.", mover); return false; }
                bodies.Add(rb);
                positions.Add(rb.position);
                velocities.Add(rb.velocity);
                angularVelocities.Add(rb.angularVelocity);
            }
            transferInProgress = true;
            bool placed = director.SpawnPlayersAt(4, bodies, out int overlaps);
            if (!placed)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    bodies[i].position = positions[i];
                    bodies[i].transform.position = positions[i];
                    bodies[i].velocity = velocities[i];
                    bodies[i].angularVelocity = angularVelocities[i];
                }
                Physics.SyncTransforms();
                transferInProgress = false;
                Debug.LogError($"[Lab_IsolationFlow] CH4 스폰 겹침 {overlaps}건. 원위치로 복원했다.", this);
                if (hud != null) hud.Say("CH4 이동에 실패했습니다. 문을 다시 통과해 주세요.");
                return false;
            }
        }
        else
        {
            Rigidbody rb = enteringPlayer.GetComponent<Rigidbody>();
            if (rb == null || !director.TeleportToSectorSpawn(rb, 4, 0)) return false;
        }

        HasTransferred = true;
        transferInProgress = false;
        RespawnController.SetCheckpoint(checkpoint);
        PlayerFollowCamera.SnapToTarget();
        if (hud != null) hud.Say("CH4로 이동했습니다.");
        return true;
    }

    private void OnGUI()
    {
        if (controller == null || HasTransferred || !Lab_MapPresentation.IsCurrentSector(this)) return;
        string help;
        switch (controller.Current)
        {
            case IsolationRescueController.State.Idle:
                help = "CH2: 도형 하나로 격리실에 진입하세요. Tab으로 도형을 전환합니다."; break;
            case IsolationRescueController.State.Preparing:
                help = "문에서 물러나세요. 문이 닫히면 안팎 준비 버튼을 각각 E로 누르세요."; break;
            case IsolationRescueController.State.InProgress:
                help = "안쪽 순서판과 바깥 대응표를 Tab으로 확인하고, 바깥 도형으로 레버를 순서대로 당기세요."; break;
            case IsolationRescueController.State.AwaitFinalRelease:
                help = "바깥 도형을 전원판 위에 두고 Tab으로 안쪽 도형을 선택해 해제 버튼을 E로 누르세요."; break;
            case IsolationRescueController.State.Success:
                help = "S4 출구문을 통과하면 세 도형이 함께 CH4로 이동합니다."; break;
            case IsolationRescueController.State.TimedOut:
                help = "시간 초과: 격리문이 열렸습니다. 열린 S3 출구로 진행하세요."; break;
            default:
                help = "격리가 해제됐습니다."; break;
        }
        GUI.Box(new Rect(12f, Screen.height - 48f, Mathf.Min(Screen.width - 24f, 900f), 36f), help);
    }
}
