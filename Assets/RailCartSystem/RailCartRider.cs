using UnityEngine;

/// <summary>
/// 레일카 탑승/하차 — 2026-09-05 확정: 걸어서 올라타는 방식(트리거) 대신 `CatapultBucket`의 C키
/// 탑승 패턴을 재사용한다(거리 게이트 + 즉시 텔레포트 + 부모화 + isKinematic). 카트가 곡선 레일을
/// 따라 위치뿐 아니라 방향(회전)도 계속 바뀌므로, 이전의 "velocity만 얹어 따라가게 한다"는
/// 방식(직선에서만 잘 맞음)보다 완전 종속(부모화)이 곡선에서도 정확하다 — 회전까지 자동으로
/// 따라간다.
///
/// [왜 카트 루트에 직접 부모화해도 되는가] `CatapultBucket`은 비균일 스케일을 가진
/// `Catapult_BucketInner`가 아니라 순수 회전(스케일 (1,1,1)) `armPivot`에 부모화한다 — 비균일
/// 스케일에 회전이 겹치면 `SetParent(..., true)`의 스케일 역산이 전단(shear)을 만들어 탑승자가
/// 찌그러지기 때문이다(`CatapultSystem/CLAUDE.md` "10차 개편" 참고). 레일카는 이 함정을 설계
/// 단계에서 피한다 — 루트 Transform의 `localScale`을 항상 `(1,1,1)`로 고정하고, 외형(U자형
/// 광산차 몸체)은 전부 자식 오브젝트의 로컬 스케일로만 표현한다(`RailCartMenuItem` 참고). 그래서
/// 탑승자를 카트 루트에 직접 부모화해도 전단이 생기지 않는다.
/// </summary>
public class RailCartRider : MonoBehaviour, IInteractionProvider, IMpSynced, IMpEventSink
{
    [Tooltip("탑승/하차 대상 카트. 비워두면 이 컴포넌트가 붙은 오브젝트의 RailCart를 찾는다.")]
    public RailCart cart;
    [Tooltip("이 거리 안에서 C를 누르면 탑승(또는 하차)한다.")]
    public float boardRange = 2.5f;
    [Tooltip("탑승 시 플레이어를 놓을 좌석 지점(카트 자식 Transform). 비워두면 이 오브젝트의 " +
             "위치를 쓴다.")]
    public Transform seat;
    [Tooltip("하차 시 플레이어를 내려놓을 지점 — 카트 로컬 좌표계 기준 오프셋(옆으로 비켜서 내림).")]
    public Vector3 exitLocalOffset = new Vector3(0.9f, 0f, 0f);

    private Rigidbody occupantBody;
    private PlayerMover occupantMover;
    private Transform occupantOriginalParent;
    private RigidbodyInterpolation occupantOriginalInterpolation;
    private Collider[] occupantColliders;
    private Collider[] cartColliders;

    void Awake()
    {
        if (cart == null) cart = GetComponent<RailCart>();
        mpId = MpSync.Register(this);
    }

    void OnDestroy() => MpSync.Unregister(mpId, this);

    // ── [멀티] 좌석은 서버가 배정한다(동시에 타려는 두 사람 방지). 이벤트 0=탑승 요청, 1=하차(arg=몸 ID), 채널 0=탑승한 몸 ID+1.
    // 배정을 받은 몸의 주인 PC가 자기 몸을 좌석에 붙이고, 모든 PC가 그 몸과 카트의 충돌을 끈다(안 끄면 서버의 카트가 떠오른다).
    private uint mpId;
    private float mpOccupant;
    private Collider[] mpIgnoredBody;

    private bool RemoteOccupied => occupantBody == null && mpOccupant > 0f;

    private static Unity.Netcode.NetworkObject BodyById(float idPlusOne)
    {
        var nm = Unity.Netcode.NetworkManager.Singleton;
        return nm != null && nm.SpawnManager.SpawnedObjects.TryGetValue((ulong)(idPlusOne - 1f), out var no) ? no : null;
    }

    public void OnMpValue(int channel, float value) { if (channel == 0) ApplyOccupant(value); }

    public void OnMpEvent(int kind, Vector3 data, uint arg)
    {
        if (!MpSync.IsServer || !MpSync.SenderOwnsBody(arg)) return;
        float body = arg + 1f;
        if (kind == 0 && mpOccupant <= 0f) SetOccupant(body);
        else if (kind == 1 && Mathf.Approximately(mpOccupant, body)) SetOccupant(0f);
    }

    private void SetOccupant(float value)
    {
        MpSync.Publish(mpId, 0, value);
        ApplyOccupant(value);
    }

    private void ApplyOccupant(float value)
    {
        if (Mathf.Approximately(value, mpOccupant)) return;
        SetRemoteBodyCollisionIgnored(false);
        mpOccupant = value;
        if (value <= 0f) return;
        var no = BodyById(value);
        if (no == null) return;
        if (!no.IsOwner)
        {
            mpIgnoredBody = no.GetComponentsInChildren<Collider>(true);
            SetRemoteBodyCollisionIgnored(true);
            return;
        }
        if (occupantBody != null) return;
        var mover = no.GetComponent<PlayerMover>();
        var body = no.GetComponent<Rigidbody>();
        // 배정을 기다리는 사이 다른 기믹에 붙잡혔으면 자리를 돌려준다
        if (mover == null || body == null || mover.ExternallyDriven || body.isKinematic)
            MpSync.SendEvent(mpId, 1, Vector3.zero, (uint)no.NetworkObjectId);
        else Board(mover, body);
    }

    private void SetRemoteBodyCollisionIgnored(bool ignored)
    {
        if (mpIgnoredBody == null) return;
        Collider[] carts = cart != null ? cart.GetComponentsInChildren<Collider>(true) : GetComponentsInChildren<Collider>(true);
        foreach (Collider b in mpIgnoredBody)
            foreach (Collider c in carts)
                if (b != null && c != null) Physics.IgnoreCollision(b, c, ignored);
        if (!ignored) mpIgnoredBody = null;
    }

    // 탑승자가 복귀 대상이 되면(추격자·레이저 피격, 장외, R) 먼저 내려놓는다 — 붙잡힌 채로는
    // RespawnController가 복귀를 거절해 영영 돌아가지 못한다.
    private System.Action onInteract;

    void OnEnable()
    {
        RespawnController.ReleaseHoldRequested += HandleReleaseHold;
        InteractionController.Register(this);
    }

    void OnDisable()
    {
        RespawnController.ReleaseHoldRequested -= HandleReleaseHold;
        InteractionController.Unregister(this);
        // 탑승 중에 꺼지면 탑승자가 부모화·키네마틱·ExternallyDriven 상태로 영구히 남는다.
        if (occupantBody != null && occupantMover != null) Unboard();
    }

    // 키를 직접 읽지 않는다 — 탑승 중인 플레이어에게는 하차(붙잡힌 상태에서도 허용), 그 외엔 탑승 범위
    // 안일 때 탑승을 E 탭 액션으로 올린다(키맵 통합안 §2-1). 도형 제한 없음.
    public void CollectActions(System.Collections.Generic.List<InteractionAction> into)
    {
        PlayerMover mover = InteractionController.Controlled;
        if (mover == null) return;
        onInteract ??= OnInteract;

        if (occupantBody != null)
        {
            if (occupantMover != mover) return; // 다른 사람이 탄 카트는 건드리지 못한다.
            into.Add(new InteractionAction
            {
                channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
                verb = "하차", enabled = true,
                priority = InteractionPriority.StateOwner, distance = 0f, execute = onInteract,
                allowWhenGripped = true,
            });
            return;
        }

        if (RemoteOccupied) return; // 다른 플레이어가 타고 있거나 배정 중이다
        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;
        float d = Vector3.Distance(body.position, transform.position);
        if (d > boardRange) return;
        into.Add(new InteractionAction
        {
            channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
            verb = "레일카 탑승", enabled = true,
            priority = InteractionPriority.Panel, distance = d, execute = onInteract,
        });
    }

    private void HandleReleaseHold(PlayerMover mover)
    {
        if (occupantMover != null && occupantMover == mover) Unboard();
    }

    void Update()
    {
        // 탑승 중엔 다른 기믹(DreamThreadSystem 등)이 같은 프레임에 ExternallyDriven을 꺼도
        // 자기 몫만큼은 자가 치유한다 — RailCartRider의 이전 버전이 겪은 것과 같은 소유권 경합
        // 방지(`RailCartRider.cs` 이력 참고).
        if (occupantMover != null) occupantMover.ExternallyDriven = true;
    }

    private void OnInteract()
    {
        if (occupantBody != null)
        {
            // occupantMover가 지금 조작 대상이 아니면(Tab으로 다른 도형으로 넘어갔거나, 다른
            // 플레이어가 무관한 목적으로 C를 눌렀거나) 무시한다 — CatapultSteerHandle이 같은
            // 문제를 dockedMover.IsControlled로 막은 것과 동일한 게이트. 이게 없으면 Board()의
            // 거리+최근접 게이트와 달리 Unboard()엔 아무 방어가 없어, 씬 어디서든 C를 누르면
            // 카트에 탄 다른 플레이어가 강제로 내려진다(2026-09-15 감사에서 발견).
            if (occupantMover.IsControlled) Unboard();
            return;
        }

        TryBoard();
    }

    private void FixedUpdate()
    {
        // [멀티] 탄 채 접속이 끊기면 하차 이벤트가 오지 않는다 — 서버가 자리를 비운다
        if (MpSync.IsServer && mpOccupant > 0f && BodyById(mpOccupant) == null) SetOccupant(0f);
        SyncOccupantToSeat();
    }
    private void LateUpdate() => SyncOccupantToSeat();

    private void SyncOccupantToSeat()
    {
        if (occupantBody == null) return;
        Transform target = seat != null ? seat : transform;
        // A child kinematic Rigidbody does not reliably inherit a moving dynamic parent's pose in PhysX.
        // Keep both physics and visual transforms on the explicit seat while cart collisions are ignored.
        occupantBody.position = target.position;
        occupantBody.rotation = target.rotation;
    }

    private void TryBoard()
    {
        PlayerMover mover = FindControlledPlayer();
        if (mover == null) return;
        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;

        // 이미 다른 기믹이 붙잡고 있는 몸이면 탑승시키지 않는다(2026-09-29 점검에서 발견 — 예:
        // 벽에 부착 중인 세모를 억지로 태우면 부모화가 두 소유자 사이에서 꼬인다).
        if (mover.ExternallyDriven || body.isKinematic)
        {
            Debug.Log("[RailCart] 이미 다른 기믹이 이 도형을 붙잡고 있어 탑승할 수 없습니다.");
            return;
        }

        float myDistance = Vector3.Distance(body.position, transform.position);
        if (myDistance > boardRange) return;
        if (!IsNearestRider(body.position, myDistance)) return;

        if (MpSync.Active)
        {
            var no = mover.GetComponent<Unity.Netcode.NetworkObject>();
            if (no != null && mpOccupant <= 0f) MpSync.SendEvent(mpId, 0, Vector3.zero, (uint)no.NetworkObjectId);
            return;
        }

        Board(mover, body);
    }

    private static PlayerMover FindControlledPlayer()
    {
        foreach (PlayerMover m in Object.FindObjectsByType<PlayerMover>(FindObjectsSortMode.None))
            if (m.IsControlled) return m;
        return null;
    }

    // 여러 레일카의 탑승 범위가 겹치는 위치에서 C를 누르면 가장 가까운 카트에만 탑승한다 —
    // CatapultBucket.IsNearestBucket과 같은 이유(C는 전역 키).
    private bool IsNearestRider(Vector3 playerPosition, float myDistance)
    {
        foreach (RailCartRider other in Object.FindObjectsByType<RailCartRider>(FindObjectsSortMode.None))
        {
            if (other == this) continue;
            float otherDistance = Vector3.Distance(playerPosition, other.transform.position);
            if (otherDistance > other.boardRange) continue;
            if (otherDistance < myDistance) return false;
            if (otherDistance == myDistance && other.GetInstanceID() < GetInstanceID()) return false;
        }
        return true;
    }

    private void Board(PlayerMover mover, Rigidbody body)
    {
        // 포탈 굴리기 모드인 채로 타면, 텀블 도중(붙잡힘 상태)엔 모드가 스스로 꺼지지 않아 카트 안에서
        // 텀블·Release(isKinematic 해제)를 이어갈 수 있다. 아래에서 키네마틱을 걸기 전에 먼저 끈다(끄는 쪽이
        // isKinematic/ExternallyDriven을 되돌리므로 순서 중요). ※ 2026-09-25 "정사면체가 떠서 카트가 못
        // 움직인다"의 실제 원인은 이게 아니라 아래 보간이었다(Loki 실측) — 이 줄은 예방용으로 남긴다.
        PlayerRollModeReceiver rollMode = mover.GetComponent<PlayerRollModeReceiver>();
        if (rollMode != null) rollMode.SetRollMode(false);

        occupantBody = body;
        occupantMover = mover;
        occupantOriginalParent = body.transform.parent;
        // 탑승 중엔 보간을 끈다 — CatapultBucket.Board와 같은 이유: 키네마틱 바디가 부모(카트) Transform에
        // 간접적으로 끌려갈 때 Interpolate는 옛 물리 자세로 Transform을 되써 몸이 월드에 묶일 수 있다.
        // 하차 시 원래 값으로 되돌린다.
        occupantOriginalInterpolation = body.interpolation;
        body.interpolation = RigidbodyInterpolation.None;

        occupantColliders = body.GetComponentsInChildren<Collider>(true);
        cartColliders = cart != null ? cart.GetComponentsInChildren<Collider>(true) : GetComponentsInChildren<Collider>(true);
        SetOccupantCartCollisionsIgnored(true);

        body.isKinematic = true;
        mover.ExternallyDriven = true;

        Transform mountParent = cart != null ? cart.transform : transform;
        body.transform.SetParent(mountParent, true);
        body.transform.position = seat != null ? seat.position : transform.position;
        body.transform.localRotation = Quaternion.identity;
    }

    private void Unboard()
    {
        Rigidbody body = occupantBody;
        PlayerMover mover = occupantMover;

        Transform mountParent = cart != null ? cart.transform : transform;
        Vector3 exitPos = mountParent.TransformPoint(exitLocalOffset);

        SetOccupantCartCollisionsIgnored(false);

        body.transform.SetParent(occupantOriginalParent, true);
        MpSync.RestoreBodyRoot(body.gameObject); // [멀티] 좌석 자식이 되며 맵 씬으로 옮겨진 몸을 DDOL로 되돌린다(맵 전환 때 파괴 방지)
        body.transform.position = exitPos;
        body.isKinematic = false;
        body.interpolation = occupantOriginalInterpolation;
        mover.ExternallyDriven = false;
        if (MpSync.Active && body.TryGetComponent(out Unity.Netcode.NetworkObject bodyNo))
            MpSync.SendEvent(mpId, 1, Vector3.zero, (uint)bodyNo.NetworkObjectId);

        occupantBody = null;
        occupantMover = null;
        occupantOriginalParent = null;
        occupantColliders = null;
        cartColliders = null;
    }

    private void SetOccupantCartCollisionsIgnored(bool ignored)
    {
        if (occupantColliders == null || cartColliders == null) return;
        foreach (Collider occupant in occupantColliders)
        {
            if (occupant == null) continue;
            foreach (Collider cartCollider in cartColliders)
            {
                if (cartCollider == null || cartCollider == occupant) continue;
                Physics.IgnoreCollision(occupant, cartCollider, ignored);
            }
        }
    }
}
