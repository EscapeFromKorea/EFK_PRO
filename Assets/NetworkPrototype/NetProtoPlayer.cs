using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [멀티 M1 프로토타입] 서버 권한 입력 방식. 소유 클라이언트는 입력(축·카메라 yaw·점프)만 서버로 보내고,
/// 서버만 PlayerMover/PlayerJump로 물리를 돌린다. 클라이언트 쪽 몸은 NetworkTransform 보간으로 따라온다(예측 없음 —
/// 이게 R1 게이트가 재려는 "순수 서버 권한의 입력 지연"이다).
/// </summary>
public class NetProtoPlayer : NetworkBehaviour
{
    PlayerMover mover;
    PlayerJump jump;
    Rigidbody rb;
    bool jumpPressed;

    // 지연 측정(소유 클라이언트): 입력을 보낸 순간 → 화면의 내 몸이 움직이기 시작한 순간
    float moveInputAt = -1f, jumpInputAt = -1f;
    Vector3 moveStartPos;
    float jumpStartY;
    float firstMoveMs = -1f, firstJumpMs = -1f, spawnAt;

    public override void OnNetworkSpawn()
    {
        // 멀티플레이: 재접속자는 홈 화면에서 연결돼 기존 도형이 홈 씬에 만들어지고, 곧 맵 로드로 홈이 언로드되면 같이 사라진다 → 씬과 무관하게 유지
        if (MpLobby.I != null) DontDestroyOnLoad(gameObject);
        mover = GetComponent<PlayerMover>();
        jump = GetComponent<PlayerJump>();
        rb = GetComponent<Rigidbody>();
        spawnAt = Time.realtimeSinceStartup;
        // 맵 3: 레일 전용 트랙은 Awake 때 씬에 있던 도형에만 충돌을 껐으므로, 나중에 스폰된 도형에도 다시 적용한다(모든 피어)
        foreach (var track in FindObjectsByType<ToyWorldRailCartOnlyTrack>(FindObjectsSortMode.None)) track.IgnorePlayer(mover);
        Debug.Log($"[NetProto] 플레이어 스폰 owner={OwnerClientId} IsOwner={IsOwner} IsServer={IsServer}");

        if (NetProtoTransform.OwnerAuth)
        {
            // 오너 권한: 소유 클라이언트(호스트 자신 포함)가 자기 몸의 물리를 로컬에서 돌린다. 나머지는 받아 그리기만.
            if (IsOwner)
            {
                mover.UseRemoteInput = true; // 축은 아래 FixedUpdate에서 로컬로 직접 대입(RPC 없음)
                rb.position = SpawnPos();
            }
            else DisableBody();
        }
        else if (IsServer)
        {
            mover.UseRemoteInput = true; // 호스트 자신의 도형도 같은 경로(RPC→서버)로 굴려 비교 조건을 맞춘다
            rb.position = SpawnPos();
        }
        else DisableBody();
        if (IsOwner) NetProtoRunner.LocalPlayer = transform;
    }

    Vector3 SpawnPos() => MpGame.TrySpawnPoint(OwnerClientId, out var scenePos) ? scenePos
        : NetProtoRunner.MapMode
        ? NetProtoRunner.SpawnBase + new Vector3(OwnerClientId * 2f, 0f, 0f)
        : new Vector3(OwnerClientId * 3f, NetProtoRunner.Platform ? 4f : 1.5f, 0f);

    // 클라이언트의 몸은 서버(또는 오너) 결과를 받아 그리기만 한다
    void DisableBody()
    {
        mover.enabled = false;
        // 남의 몸(복사본)은 "조작 중이 아님"으로 표시한다. E 상호작용(InteractionController)·R 리스폰(RespawnController)은
        // 스위처 없이 IsControlled인 첫 PlayerMover를 "내 도형"으로 고르므로, 복사본이 true면 남의 몸이 선택돼 아무 일도 안 일어난다.
        mover.SetControlled(false);
        jump.enabled = false;
        var accel = GetComponent<PlayerAccelReceiver>();
        if (accel != null) accel.enabled = false;
        var wind = GetComponent<PlayerWindReceiver>();
        if (wind != null) wind.enabled = false;
    }

    // 원격 관찰 측정(오너 권한): 남의 몸이 처음 움직인 UTC 시각, 이동 중 속도의 평균/표준편차(지터)
    static double UtcMs() => (System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalMilliseconds;
    Vector3 obsPrev; bool obsInit, obsBased, obsMoved, obsDone; Vector3 obsBase; float obsBaseAt, obsMovedAt;
    float spdSum, spdSq; int spdN;

    void Observe()
    {
        var p = transform.position;
        float now = Time.realtimeSinceStartup;
        if (!obsInit) { obsInit = true; obsPrev = p; obsBaseAt = now; return; }
        float spd = (p - obsPrev).magnitude / Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
        obsPrev = p;
        if (now - obsBaseAt < 2f) return; // 스폰 직후 위치 튐·안착 구간은 무시(봇 입력은 스폰 3초 뒤)
        if (!obsBased) { obsBased = true; obsBase = p; return; }
        if (!obsMoved && (p - obsBase).sqrMagnitude > 0.05f * 0.05f)
        {
            obsMoved = true; obsMovedAt = now;
            Debug.Log($"[NetProto] 원격몸 첫이동 owner={OwnerClientId} utcMs={UtcMs():F0}");
        }
        // 지터: 이동 시작 0.5~1.8초 구간(가속 끝난 정속 구간)의 프레임별 속도 편차
        if (obsMoved && !obsDone)
        {
            float t = now - obsMovedAt;
            if (t > 0.5f && t < 1.8f) { spdSum += spd; spdSq += spd * spd; spdN++; }
            else if (t >= 1.8f)
            {
                obsDone = true;
                float m = spdSum / Mathf.Max(spdN, 1), sd = Mathf.Sqrt(Mathf.Max(0f, spdSq / Mathf.Max(spdN, 1) - m * m));
                Debug.Log($"[NetProto] 원격몸 정속구간 owner={OwnerClientId} n={spdN} mean={m:F2} sd={sd:F2} cv={sd / m:F2}");
            }
        }
    }

    // ── 탑승 흉내(-ride, 오너 권한): RailCartRider.Board와 같은 처리 — 키네마틱·ExternallyDriven·보간 끔·카트와 충돌 무시, 좌석에 위치 고정.
    // 오너는 "자기 화면의 카트(보간된 위치)"의 좌석을 따라가므로, 다른 사람 화면에서는 카트와 어긋날 수 있다(R12 측정 대상).
    NetProtoCart rideCart; Transform rideSeat; bool boarded, copyIgnored;
    Vector3 cartSpdPos; float cartSpdAt, cartSpd;
    float rideLogAt, offZSum, offZMax, offXSum, offXMax; int offN;

    bool boardRequested;
    bool IsLocalBody() => NetworkManager != null && OwnerClientId == NetworkManager.LocalClientId || boardRequested;

    // 발판 측정: 발판 대비 내 몸의 z 오프셋(미끄러지면 커진다). 1초마다 평균/최대 로그
    Transform platT; float platLogAt, platSum, platMax; int platN;
    void PlatformStats()
    {
        if (!IsOwner || Time.realtimeSinceStartup - spawnAt < 3f) return;
        if (platT == null) { var pl = FindFirstObjectByType<NetProtoPlatform>(); if (pl == null) return; platT = pl.transform; }
        float rel = transform.position.z - platT.position.z;
        platSum += rel; platN++; platMax = Mathf.Max(platMax, Mathf.Abs(rel));
        if (Time.realtimeSinceStartup > platLogAt)
        {
            platLogAt = Time.realtimeSinceStartup + 1f;
            var gc = GetComponent<PlayerGroundContact>();
            var prb = platT.GetComponent<Rigidbody>();
            Debug.Log($"[NetProto] 발판진단 owner={OwnerClientId} platY={platT.position.y:F2} platRbVel={prb.linearVelocity:F2} platPtVel={prb.GetPointVelocity(transform.position):F2} grounded={(gc != null ? gc.IsGrounded : false)} groundVel={(gc != null ? gc.GroundVelocity : Vector3.zero):F2} bodyVel={rb.linearVelocity:F2} kin={prb.isKinematic}");
            Debug.Log($"[NetProto] 발판오프셋 owner={OwnerClientId} relNow={rel:F2} relMean={platSum / platN:F2} relMaxAbs={platMax:F2} y={transform.position.y:F2} platZ={platT.position.z:F2}");
        }
    }

    // 원격 클라이언트는 직접 타지 않고 서버에 요청 → 서버가 몸의 소유권을 가져가(=위치 권한이 서버로) 좌석을 정확히 따라간다(R12 해법)
    [Rpc(SendTo.Server)]
    void BoardRpc() => NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

    // 하차: 요청 → 서버가 몸을 내려놓고 소유권을 요청자에게 되돌려준다
    bool unboarded, unboardRequested;
    float prevOwnPosT; Vector3 prevOwnPos; bool prevOwnInit;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void UnboardRpc(RpcParams p = default)
    {
        if (!boarded || rideSeat == null) return;
        boarded = false; unboarded = true;
        rb.isKinematic = false;
        mover.ExternallyDriven = false;
        Vector3 exit = rideSeat.TransformPoint(new Vector3(2f, 0f, 0f));
        rb.position = exit; transform.position = exit;
        NetworkObject.ChangeOwnership(p.Receive.SenderClientId);
        Debug.Log($"[NetProto] 하차 처리(서버) → 소유권 반환 client={p.Receive.SenderClientId}");
    }

    public override void OnGainedOwnership()
    {
        if (!NetProtoRunner.Ride || IsServer || !boardRequested) return;
        mover.enabled = true; jump.enabled = true;
        var accel = GetComponent<PlayerAccelReceiver>(); if (accel != null) accel.enabled = true;
        var wind = GetComponent<PlayerWindReceiver>(); if (wind != null) wind.enabled = true;
        mover.UseRemoteInput = true; mover.ExternallyDriven = false;
        rb.isKinematic = false;
        Debug.Log($"[NetProto] 하차 소유권 회복 pos={transform.position:F2}");
    }

    public override void OnLostOwnership()
    {
        if (NetProtoRunner.Ride && !IsServer) DisableBody(); // 소유권을 서버에 넘긴 클라이언트: 이제 받아 그리기만
    }

    void TryBoard()
    {
        if (boarded || unboarded || !NetProtoRunner.Ride || Time.realtimeSinceStartup - spawnAt < 2f) return;
        if (!IsServer)
        {
            if (!boardRequested && FindFirstObjectByType<NetProtoCart>() != null) { boardRequested = true; BoardRpc(); Debug.Log("[NetProto] 탑승 요청(서버 권위 전환)"); }
            return;
        }
        rideCart = FindFirstObjectByType<NetProtoCart>();
        if (rideCart == null) return;
        rideSeat = rideCart.transform.Find("RailCart_Seat");
        boarded = true;
        mover.ExternallyDriven = true;
        rb.interpolation = RigidbodyInterpolation.None;
        foreach (var c in GetComponentsInChildren<Collider>(true))
            foreach (var cc in rideCart.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(c, cc, true);
        rb.isKinematic = true;
        Debug.Log("[NetProto] 탑승(오너측)");
    }

    void FollowSeat()
    {
        if (!boarded || rideSeat == null) return;
        rb.position = rideSeat.position; rb.rotation = rideSeat.rotation;
        transform.SetPositionAndRotation(rideSeat.position, rideSeat.rotation);
    }

    void LateUpdate() { if (IsOwner) FollowSeat(); }

    // 관찰자(호스트): 남의 몸이 카트 좌석에서 얼마나 어긋나 보이는지. 카트 속도 5 m/s 이상일 때만 표본
    void ObserveRide()
    {
        if (!NetProtoRunner.Ride) return;
        if (rideCart == null) { rideCart = FindFirstObjectByType<NetProtoCart>(); if (rideCart == null) return; rideSeat = rideCart.transform.Find("RailCart_Seat"); }
        if (!copyIgnored)
        {
            // 탑승자의 "복사본"(남의 화면에 있는 몸)도 카트와 충돌을 무시해야 한다 — 안 그러면 키네마틱 콜라이더가 카트 안에 파묻혀 카트를 밀어낸다(R12 실측)
            copyIgnored = true;
            foreach (var c in GetComponentsInChildren<Collider>(true))
                foreach (var cc in rideCart.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(c, cc, true);
        }
        // 클라이언트의 카트는 키네마틱이라 linearVelocity가 0 → 0.25초마다 위치 변화로 속도를 구한다
        if (Time.realtimeSinceStartup - cartSpdAt > 0.25f)
        {
            cartSpd = (rideCart.transform.position - cartSpdPos).magnitude / (Time.realtimeSinceStartup - cartSpdAt);
            cartSpdPos = rideCart.transform.position; cartSpdAt = Time.realtimeSinceStartup;
        }
        if (cartSpd < 5f) return;
        Vector3 off = transform.position - rideSeat.position; // 카트는 +Z로 달린다: z가 음수면 몸이 뒤처져 보임
        float dz = off.z, dx = off.x;
        offZSum += dz; offXSum += dx; offN++;
        offZMax = Mathf.Max(offZMax, Mathf.Abs(dz)); offXMax = Mathf.Max(offXMax, Mathf.Abs(dx));
        if (Time.realtimeSinceStartup > rideLogAt)
        {
            rideLogAt = Time.realtimeSinceStartup + 1f;
            Debug.Log($"[NetProto] 탑승오프셋 n={offN} dzMean={offZSum / offN:F2} dzMaxAbs={offZMax:F2} dxMean={offXSum / offN:F2} dxMaxAbs={offXMax:F2} y={off.y:F2} cartSpeed={cartSpd:F2}");
        }
    }

    void Update()
    {
        if (NetProtoTransform.OwnerAuth && !IsOwner) { Observe(); ObserveRide(); }
        if (NetProtoRunner.Ride && !IsServer && boardRequested && !unboardRequested && !IsOwner && Time.realtimeSinceStartup - spawnAt > 12f)
        {
            unboardRequested = true; UnboardRpc(); Debug.Log("[NetProto] 하차 요청");
        }
        if (NetProtoRunner.Ride && !IsServer && IsLocalBody())
        {
            // 내 몸의 프레임 간 위치 튐(하차 전환 순간 등)
            if (prevOwnInit && (transform.position - prevOwnPos).magnitude > 0.6f)
                Debug.Log($"[NetProto] 위치튐 d={(transform.position - prevOwnPos).magnitude:F2} owner={IsOwner} pos={transform.position:F2}");
            prevOwnPos = transform.position; prevOwnInit = true;
        }
        if (NetProtoRunner.Platform) PlatformStats();
        if (IsOwner) TryBoard();
        if (!IsOwner) return;
        bool jumpNow = NetProtoRunner.Bot ? NetProtoRunner.BotJumpNow(spawnAt) : Input.GetButtonDown("Jump");
        if (jumpNow)
        {
            jumpPressed = true;
            jumpInputAt = Time.realtimeSinceStartup;
            jumpStartY = transform.position.y;
        }
        // 측정: 입력 후 화면 속 몸이 처음 움직인 시점
        if (moveInputAt >= 0f && firstMoveMs < 0f && (transform.position - moveStartPos).sqrMagnitude > 0.05f * 0.05f)
        {
            firstMoveMs = (Time.realtimeSinceStartup - moveInputAt) * 1000f;
            Debug.Log($"[NetProto] 이동 입력→화면 반응 {firstMoveMs:F0} ms");
        }
        if (jumpInputAt >= 0f && firstJumpMs < 0f && transform.position.y - jumpStartY > 0.1f)
        {
            firstJumpMs = (Time.realtimeSinceStartup - jumpInputAt) * 1000f;
            Debug.Log($"[NetProto] 점프 입력→화면 반응 {firstJumpMs:F0} ms");
        }
    }

    float nextDiag;

    void FixedUpdate()
    {
        if (IsServer && Time.realtimeSinceStartup > nextDiag)
        {
            nextDiag = Time.realtimeSinceStartup + 1f;
            Debug.Log($"[NetProto] 서버측 owner={OwnerClientId} pos={rb.position:F2} vel={rb.linearVelocity:F2} axes={mover.RemoteAxes} yaw={mover.RemoteYaw:F0} controlled={mover.IsControlled} locked={mover.InputLocked} ext={mover.ExternallyDriven} kinematic={rb.isKinematic} moverEnabled={mover.enabled} remote={mover.UseRemoteInput}");
        }
        if (!IsOwner) return;
        FollowSeat();
        Vector2 axes = NetProtoRunner.Bot
            ? NetProtoRunner.BotAxes(spawnAt)
            : new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        if (moveInputAt < 0f && axes.sqrMagnitude > 0.25f)
        {
            moveInputAt = Time.realtimeSinceStartup;
            moveStartPos = transform.position;
            Debug.Log($"[NetProto] 입력시작 utcMs={UtcMs():F0}");
        }
        if (NetProtoTransform.OwnerAuth)
        {
            mover.RemoteAxes = axes;
            mover.RemoteYaw = PlayerFollowCamera.ViewYaw ?? NetProtoRunner.CamYaw; // 정식 카메라가 있으면 그 시점 기준(마우스 궤도), 없으면 프로토 Q/E
            if (jumpPressed) { jumpPressed = false; jump.QueueJump(); }
            return;
        }
        InputRpc(axes, NetProtoRunner.CamYaw);
        if (jumpPressed) { jumpPressed = false; JumpRpc(); }
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void InputRpc(Vector2 axes, float yaw)
    {
        mover.RemoteAxes = axes;
        mover.RemoteYaw = yaw;
    }

    [Rpc(SendTo.Server)]
    void JumpRpc() => jump.QueueJump();
}

