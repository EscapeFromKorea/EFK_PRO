using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 딱딱 블록 하나. 다른 <see cref="SnapBlock"/>과 면을 맞대 강접합했다 해제할 수 있다.
/// 결합 판정·하이라이트·입력은 <see cref="SnapBlockController"/>가 갖고, 이 컴포넌트는
/// "내 6면이 어디인가 + 지금 누구와 결합돼 있는가 + 결합/해제를 실행"만 담당한다.
///
/// [왜 all-locked ConfigurableJoint인가 (FixedJoint 아님)]
/// FixedJoint는 단순하지만 드리프트 보정(projection) 옵션이 없어 블록 체인이 길어지면 미세 흔들림이
/// 누적된다. ConfigurableJoint의 전 축 Locked + projectionMode=PositionAndRotation은 조인트가
/// 규정 위치/자세에서 벗어나면 하드로 되돌려, "Joint 폭주" 요구를 억제하는 데 유리하다.
/// (저장소도 ConfigurableJoint를 쓴다 — DreamThreadController.)
///
/// [폭주 억제 3종] ① 결합 직전 이 블록을 상대 면에 정확히 정렬한 포즈로 스냅해 초기 침투를 없앤다
/// (조인트가 튀는 가장 큰 원인). ② solverIterations 상향 + CCD + Interpolate. ③ 결합 순간 양쪽
/// 속도를 0으로. 그래도 블록이 아주 많거나 무거운 도형이 세게 부딪히면 흔들릴 수 있다 — 완화지
/// 보장이 아니다(그레이박스 실측 튜닝 대상).
///
/// [규약] 전역 상태(Physics.*) 미변경(MP-01). PlayerSystem·씬 무수정.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
public class SnapBlock : MonoBehaviour, IMpSynced, IMpEventSink
{
    [Header("결합 안정화")]
    [Tooltip("결합된 블록의 Rigidbody.solverIterations. 높을수록 조인트 체인이 덜 흔들리지만 비용이 든다.")]
    public int solverIterations = 20;
    [Tooltip("결합 조인트가 끊어지는 힘(N). 무한(기본)이면 절대 안 끊어진다. 큰 유한값이면 과부하 시 " +
             "구조물이 분해된다 — 폭주 대신 분해로 처리하고 싶을 때만 유한값을 넣는다.")]
    public float jointBreakForce = Mathf.Infinity;
    [Tooltip("결합 조인트가 끊어지는 토크(N·m). 무한(기본)이면 절대 안 끊어진다.")]
    public float jointBreakTorque = Mathf.Infinity;

    /// <summary>블록 한 면. 결합 판정·정렬에 쓴다.</summary>
    public struct Face
    {
        public int index;            // 0..5 (+X,-X,+Y,-Y,+Z,-Z)
        public Vector3 localCenter;  // BoxCollider 로컬 면 중심
        public Vector3 center;       // 월드 면 중심
        public Vector3 normal;       // 월드 바깥 법선(정규화)
    }

    private Rigidbody body;
    private BoxCollider box;

    // 상대 블록 → 그 연결을 담당하는 ConfigurableJoint. 조인트 컴포넌트 자체는 Weld를 호출한 쪽에
    // 붙지만, 양쪽 모두 이 사전에 서로를 등록해 BFS/해제가 대칭이 되게 한다.
    private readonly Dictionary<SnapBlock, ConfigurableJoint> joints = new Dictionary<SnapBlock, ConfigurableJoint>();

    private static readonly Vector3[] LocalNormals =
    {
        Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
    };

    // 씬에 존재하는 모든 SnapBlock의 자가 등록 목록. SnapBlockController가 매 프레임
    // FindObjectsByType<SnapBlock>(FindObjectsSortMode.None)로 전체 씬을 훑던 것(블록이 많아지는 레벨일수록 비용이
    // 커짐, 2026-09-15 감사에서 발견)을 대체한다 — OnEnable/OnDisable로 등록만 하면 되므로
    // Update 비용은 리스트 길이에만 비례한다.
    private static readonly List<SnapBlock> allBlocks = new List<SnapBlock>();
    public static IReadOnlyList<SnapBlock> AllBlocks => allBlocks;

    public Rigidbody Body => body;
    public bool HasConnections => joints.Count > 0 || mpConnCount > 0;
    public IEnumerable<SnapBlock> ConnectedBlocks => joints.Keys;

    /// <summary>이 블록이 other와 이미 결합돼 있는가.</summary>
    public bool HasConnectionTo(SnapBlock other) => other != null && joints.ContainsKey(other);

    private void Reset()
    {
        // "InteractionItem" 태그 — Player 전용이던 존재/무게 게이트형 패드(DoorSystem/LiftSystem/
        // WindupAxleSystem/RainbowBridgeSystem)가 이 태그도 함께 인식하도록 확장했다. 딱딱 블록이
        // 어느 패드를 밟아도 자동으로 반응한다. Reset은 컴포넌트가 붙는 즉시(에디터 타임) 불려
        // Tools로 생성한 직후에도 바로 태그가 붙는다 — Awake는 플레이 모드 진입 전까지 안 불린다.
        gameObject.tag = "InteractionItem";
    }

    private void Awake()
    {
        MpId = MpSync.Register(this);
        MpBodySync.Ensure(this);
        mpSync = GetComponent<MpBodySync>();
        if (mpSync != null) follow = gameObject.AddComponent<SnapBlockMpFollow>();
        body = GetComponent<Rigidbody>();
        box = GetComponent<BoxCollider>();
        ApplyStabilitySettings();

        if (!gameObject.CompareTag("InteractionItem")) gameObject.tag = "InteractionItem";
    }

    // ===== [멀티 M4 B1] 블록은 서버 권한. 자세는 MpBodySync가 전하고, 이 컴포넌트는 들림(채널 0=들고 있는 몸 ID+1, 0이면 안 듦)과
    // 결합 수(채널 1, 참가자 PC의 UI용)를 올린다. 주인 PC가 보낸 요청(들기·내려놓기·결합·해제)을 서버가 실행한다.
    // 이벤트: 0=들기(data=(몸ID, 들림 높이)) 2=내려놓기(data=위치) 3=내려놓기 회전(data=오일러, 2보다 먼저) 4=결합(arg=상대 블록 ID, data=(내 면, 상대 면)) 5=결합 해제
    public uint MpId;
    private int mpConnCount;
    private MpBodySync mpSync;
    private SnapBlockMpFollow follow;
    private Quaternion mpDropRot = Quaternion.identity;
    private bool mpHasDropRot;

    // 몸 주인 PC가 블록을 미는 중(이벤트 6). 참가자 PC의 블록은 키네마틱이라 주인 몸이 밀어도 안 움직이므로,
    // 주인이 부딪힘에서 "미는 속도"를 서버에 올리고 서버가 블록의 수평 속도를 그쪽으로 끌어간다.
    private Vector3 mpPushVel;
    private float mpPushUntil;
    private float mpNextPushSend;
    private Vector3 mpPushSendVel;
    private const float PushSpeed = 2.2f; // 입력을 완전히 블록 쪽으로 눌렀을 때 서버에 올리는 밀기 속도(m/s) — 실측으로 튜닝
    private const float PushMaxAccel = 60f; // 서버가 블록을 밀리는 속도로 끌어가는 최대 가속(m/s²) — 블록이 튀지 않게 상한

    private void FixedUpdate()
    {
        if (!MpSync.IsServer) return;
        MpSync.Publish(MpId, 1, joints.Count);
        if (Time.time < mpPushUntil && !body.isKinematic)
        {
            // 결합된 구조물은 한 덩어리로 같이 민다(닿은 블록 하나의 속도만 바꾸면 조인트가 버텨 구조물이 안 움직인다)
            pushBuf.Clear();
            pushBuf.Add(this);
            for (int i = 0; i < pushBuf.Count; i++)
                foreach (SnapBlock n in pushBuf[i].ConnectedBlocks)
                    if (n != null && !pushBuf.Contains(n)) pushBuf.Add(n);
            foreach (SnapBlock b in pushBuf) b.ApplyPushVelocity(mpPushVel);
        }
    }

    private static readonly List<SnapBlock> pushBuf = new List<SnapBlock>();

    private void ApplyPushVelocity(Vector3 vel)
    {
        if (body == null || body.isKinematic) return;
        Vector3 v = body.linearVelocity;
        Vector3 h = Vector3.MoveTowards(new Vector3(v.x, 0f, v.z), vel, PushMaxAccel * Time.fixedDeltaTime);
        body.linearVelocity = new Vector3(h.x, v.y, h.z);
    }

    private void OnCollisionStay(Collision c)
    {
        if (!MpSync.IsClient || body.isKinematic == false) return; // 참가자 PC의 키네마틱 블록만 (결합·든 블록 제외는 서버가 판단)
        var no = c.gameObject.GetComponentInParent<Unity.Netcode.NetworkObject>();
        if (no == null || !no.IsOwner) return;

        Vector3 n = Vector3.zero;
        for (int i = 0; i < c.contactCount; i++) n += c.GetContact(i).normal;
        n.y = 0f;
        if (n.sqrMagnitude < 0.09f) return; // 위에 올라서 있는 접촉은 미는 게 아니다
        Vector3 dir = n.normalized;
        Vector3 away = body.position - no.transform.position;
        away.y = 0f;
        if (Vector3.Dot(dir, away) < 0f) dir = -dir; // 법선 부호 규약에 상관없이 "몸에서 블록 쪽"으로

        // 몸이 막히면 속도·상대속도가 모두 0으로 수렴하므로 "입력 의도"를 쓴다(블록 쪽 입력 성분 × 밀기 속도)
        var mover = no.GetComponentInChildren<PlayerMover>();
        float speed = mover != null ? Mathf.Max(0f, Vector3.Dot(mover.GetInputWorldDir(), dir)) * PushSpeed : Mathf.Abs(Vector3.Dot(c.relativeVelocity, dir));
        mpPushSendVel = Vector3.Lerp(mpPushSendVel, dir * speed, 0.5f);
        if (Time.time < mpNextPushSend) return;
        mpNextPushSend = Time.time + 0.02f; // 50Hz
        MpSync.SendEvent(MpId, 6, mpPushSendVel);
    }

    public void OnMpValue(int channel, float value)
    {
        if (channel == 1) { mpConnCount = (int)value; return; }
        if (channel != 0) return;
        int v = (int)value; // >0: 누가 듦(몸 ID+1), <0: 정육면체에 도킹됨(-(몸 ID+1)), 0: 자유
        enabled = v == 0; // 들리거나 도킹된 동안은 결합·들기 후보에서 빠진다(든/도킹한 쪽 PC는 이미 꺼 둠)
        if (mpSync != null)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            mpSync.Suspend = localDock || (v > 0 && po != null && po.NetworkObjectId + 1 == (ulong)v); // 내가 든 블록은 로컬이 직접 움직인다
        }
    }

    public void MpRequestPickup(ulong carrierNetId, float height) => MpSync.SendEvent(MpId, 0, new Vector3(carrierNetId, height, 0f));

    public void MpRequestDrop(Vector3 pos, Quaternion rot)
    {
        MpSync.SendEvent(MpId, 3, rot.eulerAngles);
        MpSync.SendEvent(MpId, 2, pos);
    }

    public void MpRequestWeld(SnapBlock other, int myFace, int theirFace) => MpSync.SendEvent(MpId, 4, new Vector3(myFace, theirFace, 0f), other.MpId);

    public void MpRequestDetach() => MpSync.SendEvent(MpId, 5, Vector3.zero);

    /// <summary>참가자 정육면체의 도킹 요청. data=도킹 직후 정육면체 위치, arg=정육면체 NetworkObjectId.</summary>
    public void MpRequestDock(ulong cubeNetId, Vector3 cubePos) => MpSync.SendEvent(MpId, 7, cubePos, (uint)cubeNetId);

    public void MpRequestUndock() => MpSync.SendEvent(MpId, 8, Vector3.zero);

    private const int MaxDockedBlocks = 6;   // PlayerCubeDock 기본값과 같다(서버가 한도를 다시 검사)
    private const float MaxDockedMass = 12f;
    private List<SnapBlock> dockMembers;

    // 서버: 도킹을 알려 줄 정육면체 주인. 주인 PC가 도킹 블록을 로컬에서 직접 따라오게 해야 몸과 블록 사이 한 박자 지연이 없다.
    private ulong dockOwner = ulong.MaxValue;
    private ulong dockCubeId;

    // 정육면체 주인 PC: 서버가 보낸 상대 자세로 도킹 블록을 내 몸 물리 자세에 직접 붙인다. 이 블록만 몸과 충돌을 끈다(다른 블록과는 정상 충돌).
    private bool localDock;
    private readonly List<(Collider, Collider)> localIgnored = new List<(Collider, Collider)>();

    public void MpLocalDock(bool begin, ulong cubeNetId, Vector3 relPos, Quaternion relRot)
    {
        if (!MpSync.IsClient) return;
        if (begin)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(cubeNetId, out var cube) || cube == null || follow == null) return;
            localDock = true;
            if (mpSync != null) mpSync.Suspend = true;
            follow.BeginLocal(cube, relPos, relRot);
            Collider bc = GetComponent<Collider>();
            if (bc != null)
                foreach (Collider c in cube.GetComponentsInChildren<Collider>())
                {
                    Physics.IgnoreCollision(bc, c, true);
                    localIgnored.Add((bc, c));
                }
        }
        else
        {
            localDock = false;
            if (mpSync != null) mpSync.Suspend = false;
            if (follow != null) follow.EndLocal();
            foreach (var (a, b) in localIgnored) if (a != null && b != null) Physics.IgnoreCollision(a, b, false);
            localIgnored.Clear();
        }
    }

    private void DockBegin(ulong carrierPlusOne, Vector3 relPos, Quaternion relRot, Unity.Netcode.NetworkObject cube)
    {
        body.isKinematic = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        enabled = false;
        if (follow != null) follow.BeginDock(carrierPlusOne, relPos, relRot);
        MpSync.Publish(MpId, 0, -(float)carrierPlusOne);
        dockCubeId = cube.NetworkObjectId;
        dockOwner = cube.OwnerClientId;
        if (dockOwner != Unity.Netcode.NetworkManager.ServerClientId)
            MpLobby.I.DockInfoRpc(MpId, true, dockCubeId, relPos, relRot, MpLobby.I.NetworkManager.RpcTarget.Single(dockOwner, Unity.Netcode.RpcTargetUse.Temp));
    }

    private void DockEnd()
    {
        if (dockOwner != ulong.MaxValue && dockOwner != Unity.Netcode.NetworkManager.ServerClientId
            && MpLobby.I.NetworkManager.ConnectedClients.ContainsKey(dockOwner)) // 끊긴 주인에게는 보내지 않는다
            MpLobby.I.DockInfoRpc(MpId, false, dockCubeId, Vector3.zero, Quaternion.identity, MpLobby.I.NetworkManager.RpcTarget.Single(dockOwner, Unity.Netcode.RpcTargetUse.Temp));
        dockOwner = ulong.MaxValue;
        if (follow != null) follow.End();
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        enabled = true;
        MpSync.Publish(MpId, 0, 0f);
    }

    /// <summary>서버: 블록을 들거나 도킹한 몸이 사라졌다(끊김·파괴). 해제 이벤트(2·8)가 영영 안 오므로 서버가 직접 풀어
    /// 블록이 키네마틱·비활성으로 공중에 얼어붙거나 도킹 요청이 영구히 막히는 것을 막는다.</summary>
    public void MpCarrierLost()
    {
        if (dockOwner != ulong.MaxValue)
        {
            DockEnd();
            dockMembers = null;
            return;
        }
        if (follow != null) follow.End();
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        enabled = true;
        MpSync.Publish(MpId, 0, 0f);
    }

    public void OnMpEvent(int kind, Vector3 data, uint arg)
    {
        if (!MpSync.IsServer) return;
        switch (kind)
        {
            case 0: // 들기: 서버는 블록을 키네마틱으로 만들고 들고 있는 몸을 따라가게 한다(주인 PC가 호스트면 로컬 운반 컴포넌트가 직접 움직인다)
                if (follow != null && follow.CarrierNetId != 0) return; // 이미 누가 들고 있다
                if (!MpSync.SenderOwnsBody((ulong)data.x)) return; // 남의 몸 이름으로 들 수 없다
                if (HasConnections) return;
                body.isKinematic = true;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                enabled = false;
                if (follow != null) follow.Begin((ulong)data.x + 1, data.y, PlayerBlockCarrier.SnapToAxes(body.rotation));
                MpSync.Publish(MpId, 0, data.x + 1);
                break;
            case 3:
                mpDropRot = Quaternion.Euler(data);
                mpHasDropRot = true;
                break;
            case 2: // 내려놓기: 주인 PC가 정한 자리에 놓고 다시 물리 블록으로
                if (follow != null) follow.End();
                transform.SetPositionAndRotation(data, mpHasDropRot ? mpDropRot : transform.rotation);
                mpHasDropRot = false;
                body.position = transform.position;
                body.rotation = transform.rotation;
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                enabled = true;
                MpSync.Publish(MpId, 0, 0f);
                break;
            case 4: // 결합
            {
                var other = MpSync.Find(arg) as SnapBlock;
                if (other == null || other == this) return;
                var fa = new List<Face>();
                var fb = new List<Face>();
                GetFaces(fa);
                other.GetFaces(fb);
                int mi = (int)data.x, ti = (int)data.y;
                if (mi < 0 || mi >= fa.Count || ti < 0 || ti >= fb.Count) return;
                bool ok = Weld(other, fa[mi], fb[ti]);
                break;
            }
            case 5: DetachAll(); break;
            case 7: // 도킹: 결합된 구조물 전체를 정육면체 복사본에 고정 간격으로 붙인다(참가자 PC는 물리 조인트로 못 묶는다)
            {
                if (dockMembers != null && dockMembers.Count > 0) return;
                if (!MpSync.SenderOwnsBody(arg)) return; // 남의 정육면체에 도킹시킬 수 없다
                var nm = Unity.Netcode.NetworkManager.Singleton;
                if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(arg, out var cube) || cube == null) return;
                var members = new List<SnapBlock> { this };
                var q = new Queue<SnapBlock>();
                q.Enqueue(this);
                float mass = body.mass;
                while (q.Count > 0)
                    foreach (SnapBlock n in q.Dequeue().ConnectedBlocks)
                        if (n != null && !members.Contains(n)) { members.Add(n); mass += n.body.mass; q.Enqueue(n); }
                if (members.Count > MaxDockedBlocks || mass > MaxDockedMass) return;
                Quaternion cubeRot = cube.transform.rotation;
                Quaternion inv = Quaternion.Inverse(cubeRot);
                foreach (SnapBlock m in members)
                    m.DockBegin(arg + 1UL, inv * (m.body.position - data), inv * m.body.rotation, cube);
                dockMembers = members;
                break;
            }
            case 8: // 도킹 해제
                if (dockMembers == null) return;
                foreach (SnapBlock m in dockMembers) if (m != null) m.DockEnd();
                dockMembers = null;
                break;
            case 6: // 몸 주인 PC가 미는 중
                mpPushVel = Vector3.ClampMagnitude(new Vector3(data.x, 0f, data.z), 8f);
                mpPushUntil = Time.time + 0.4f; // 접촉 끊김(왕복 지연) 동안 유지
                break;
        }
    }

    private void OnEnable()
    {
        allBlocks.Add(this);
    }

    private void OnDisable()
    {
        allBlocks.Remove(this);
    }

    private bool destroying;

    private void OnDestroy()
    {
        destroying = true;
        DetachAll();
    }

    private void OnJointBreak(float breakForce)
    {
        PurgeDeadJoints();
    }

    private void ApplyStabilitySettings()
    {
        body.solverIterations = Mathf.Max(1, solverIterations);
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    internal Bounds GetWorldBoxBounds()
    {
        // 면 중심과 같은 Transform 기준. 물리 동기화 전이나 콜라이더가 꺼져 있어도 계산할 수 있다.
        Matrix4x4 world = transform.localToWorldMatrix;
        Vector3 half = box.size * 0.5f;
        Vector3 center = world.MultiplyPoint3x4(box.center);
        Vector3 extents = new Vector3(
            Mathf.Abs(world.m00 * half.x) + Mathf.Abs(world.m01 * half.y) + Mathf.Abs(world.m02 * half.z),
            Mathf.Abs(world.m10 * half.x) + Mathf.Abs(world.m11 * half.y) + Mathf.Abs(world.m12 * half.z),
            Mathf.Abs(world.m20 * half.x) + Mathf.Abs(world.m21 * half.y) + Mathf.Abs(world.m22 * half.z));
        return new Bounds(center, extents * 2f);
    }

    /// <summary>이 블록의 6면(월드 중심 + 바깥 법선)을 채운다. BoxCollider의 현재 크기/자세를 반영.</summary>
    public void GetFaces(List<Face> outFaces)
    {
        outFaces.Clear();
        Vector3 c = box.center;
        Vector3 h = box.size * 0.5f;

        for (int i = 0; i < 6; i++)
        {
            Vector3 n = LocalNormals[i];
            Vector3 local = c + Vector3.Scale(n, h);
            outFaces.Add(new Face
            {
                index = i,
                localCenter = local,
                center = transform.TransformPoint(local),
                normal = transform.TransformDirection(n).normalized,
            });
        }
    }

    /// <summary>이 블록을 other에 강접합한다. 먼저 이 블록을 정렬 포즈로 스냅한다. 성공 시 true.</summary>
    public bool Weld(SnapBlock other, Face myFace, Face otherFace)
    {
        if (other == null || other == this) return false;
        if (joints.ContainsKey(other)) return false;

        AlignTo(other, myFace, otherFace);

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        other.body.linearVelocity = Vector3.zero;
        other.body.angularVelocity = Vector3.zero;

        ConfigurableJoint j = gameObject.AddComponent<ConfigurableJoint>();
        j.connectedBody = other.body;
        j.autoConfigureConnectedAnchor = true;
        j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
        j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;
        j.projectionMode = JointProjectionMode.PositionAndRotation;
        j.projectionDistance = 0.01f;
        j.projectionAngle = 1f;
        j.enablePreprocessing = false;
        j.breakForce = jointBreakForce;
        j.breakTorque = jointBreakTorque;

        joints[other] = j;
        other.joints[this] = j;
        return true;
    }

    /// <summary>other와의 결합만 해제한다.</summary>
    public void DetachFrom(SnapBlock other)
    {
        if (other == null) return;
        if (joints.TryGetValue(other, out ConfigurableJoint j))
        {
            if (j != null) Destroy(j);
            joints.Remove(other);
            if (!destroying && other != null && gameObject.activeInHierarchy)
                StartCoroutine(RefilterPair(other));
        }
        other.joints.Remove(this);
    }

    // 결합 중엔 두 블록 사이 충돌이 꺼져 있다. 조인트를 없애도 물리 엔진이 그 쌍의 필터를 다시 계산하지
    // 않아, 위아래로 쌓았다 해체하면 위 블록이 아래 블록을 통과해 겹친 채 정지했다(2026-10-03 Loki 실측:
    // IgnoreCollision·레이어·트리거는 모두 정상인데 dy 1.0 → 0.0). 충돌 무시를 켰다 꺼서 필터를 강제로
    // 갱신한다. 조인트 제거는 프레임 끝에 반영되므로 다음 FixedUpdate 뒤에 한다.
    private System.Collections.IEnumerator RefilterPair(SnapBlock other)
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        if (this == null || other == null || HasConnectionTo(other)) yield break;

        Collider a = GetComponent<Collider>(), b = other.GetComponent<Collider>();
        if (a != null && b != null)
        {
            Physics.IgnoreCollision(a, b, true);
            Physics.IgnoreCollision(a, b, false);
        }
        if (body != null) body.WakeUp();
        if (other.body != null) other.body.WakeUp();
    }

    /// <summary>이 블록의 모든 결합을 해제한다(구조물에서 이 블록만 떼어낸다).</summary>
    public void DetachAll()
    {
        foreach (SnapBlock other in new List<SnapBlock>(joints.Keys))
            DetachFrom(other);
    }

    // 조인트가 breakForce로 끊어지면 사전에 null 참조가 남는다 — 양쪽에서 청소한다.
    private void PurgeDeadJoints()
    {
        List<SnapBlock> dead = null;
        foreach (KeyValuePair<SnapBlock, ConfigurableJoint> kv in joints)
        {
            if (kv.Value == null)
            {
                dead ??= new List<SnapBlock>();
                dead.Add(kv.Key);
            }
        }
        if (dead == null) return;
        foreach (SnapBlock d in dead)
        {
            joints.Remove(d);
            if (d != null) d.joints.Remove(this);
        }
    }

    // 내 면이 상대 면과 정확히 맞물렸을 때의 이 블록 자세를 계산한다(적용하지 않는다). 법선을 맞춘 뒤
    // 그 축 둘레 회전(roll)을 상대 블록의 축에 90° 단위로 스냅한다 — 예전엔 roll을 현재 자세 그대로 둬
    // 대충 맞춘 블록이 모서리가 어긋난 채 결합됐다(2026-10-10 플레이 확인).
    public void ComputeAlignedPose(SnapBlock other, Face myFace, Face otherFace, out Vector3 pos, out Quaternion rot)
    {
        Vector3 n = -otherFace.normal;
        rot = Quaternion.FromToRotation(myFace.normal, n) * transform.rotation;

        // 법선과 가장 수직인 내 로컬 축 하나를 골라, 상대의 축 중 가장 가까운 것(±90° 대칭)에 맞춘다.
        int mi = 0;
        float md = 2f;
        for (int i = 0; i < 3; i++)
        {
            float d = Mathf.Abs(Vector3.Dot(AxisOf(rot, i), n));
            if (d < md) { md = d; mi = i; }
        }
        Vector3 m = Vector3.ProjectOnPlane(AxisOf(rot, mi), n).normalized;
        Quaternion oq = other.transform.rotation;
        float best = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            Vector3 o = Vector3.ProjectOnPlane(AxisOf(oq, i), n);
            if (o.sqrMagnitude < 0.25f) continue; // 법선과 거의 평행한 축은 제외.
            float a = Vector3.SignedAngle(m, o.normalized, n);
            a -= 90f * Mathf.Round(a / 90f);
            if (Mathf.Abs(a) < Mathf.Abs(best)) best = a;
        }
        if (best != float.MaxValue) rot = Quaternion.AngleAxis(best, n) * rot;

        pos = otherFace.center - rot * Vector3.Scale(myFace.localCenter, transform.lossyScale);
    }

    private static Vector3 AxisOf(Quaternion q, int i) =>
        i == 0 ? q * Vector3.right : (i == 1 ? q * Vector3.up : q * Vector3.forward);

    private void AlignTo(SnapBlock other, Face myFace, Face otherFace)
    {
        ComputeAlignedPose(other, myFace, otherFace, out Vector3 pos, out Quaternion rot);
        transform.SetPositionAndRotation(pos, rot);
    }

    private static readonly Collider[] overlapBuf = new Collider[16];
    private static readonly HashSet<SnapBlock> structureBuf = new HashSet<SnapBlock>();
    private static readonly Queue<SnapBlock> queueBuf = new Queue<SnapBlock>();

    /// <summary>other에 결합했을 때의 자세가 다른 콜라이더(플레이어·지형·다른 블록)와 겹치는가. 이 블록·상대가
    /// 속한 결합 구조물의 블록과 트리거, ignoreRoot 아래 콜라이더는 무시한다. 겹치면 그 콜라이더를 blocker로
    /// 돌려준다.</summary>
    public bool AlignedPoseBlockedBy(SnapBlock other, Face myFace, Face otherFace, out Collider blocker,
                                     Transform ignoreRoot = null)
    {
        blocker = null;
        ComputeAlignedPose(other, myFace, otherFace, out Vector3 pos, out Quaternion rot);

        structureBuf.Clear();
        queueBuf.Clear();
        queueBuf.Enqueue(this); structureBuf.Add(this);
        queueBuf.Enqueue(other); structureBuf.Add(other);
        while (queueBuf.Count > 0)
        {
            foreach (SnapBlock n in queueBuf.Dequeue().ConnectedBlocks)
                if (n != null && structureBuf.Add(n)) queueBuf.Enqueue(n);
        }

        Vector3 lossy = transform.lossyScale;
        Vector3 center = pos + rot * Vector3.Scale(box.center, lossy);
        // 5% 줄여 면이 맞닿은 이웃(상대 블록·바닥)을 겹침으로 오인하지 않게 한다.
        Vector3 half = Vector3.Scale(box.size, lossy) * 0.5f * 0.95f;
        int count = Physics.OverlapBoxNonAlloc(center, half, overlapBuf, rot, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlapBuf[i];
            if (c == null) continue;
            if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) continue;
            SnapBlock sb = c.GetComponentInParent<SnapBlock>();
            if (sb != null && structureBuf.Contains(sb)) continue;
            blocker = c;
            return true;
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        BoxCollider bc = box != null ? box : GetComponent<BoxCollider>();
        if (bc == null) return;

        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
        Vector3 c = bc.center;
        Vector3 hh = bc.size * 0.5f;
        for (int i = 0; i < 6; i++)
        {
            Vector3 w = transform.TransformPoint(c + Vector3.Scale(LocalNormals[i], hh));
            Gizmos.DrawSphere(w, 0.06f);
        }
    }
}


/// <summary>[멀티 M4 B1] 서버에서, 다른 PC의 몸이 든 블록을 그 몸의 복사본 위로 따라가게 한다(서버 권한 블록의 위치가 곧 전원에게 전파된다).
/// 들고 있는 몸이 서버(호스트)의 몸이면 PlayerBlockCarrier가 직접 움직이므로 하지 않는다.</summary>
public class SnapBlockMpFollow : MonoBehaviour
{
    public ulong CarrierNetId; // 몸 NetworkObjectId + 1, 0이면 안 듦
    float height;
    Quaternion rot;
    Rigidbody rb;

    void Awake() => rb = GetComponent<Rigidbody>();

    bool dock;
    Vector3 relPos;
    Quaternion relRot;

    public void Begin(ulong carrierPlusOne, float h, Quaternion r) { CarrierNetId = carrierPlusOne; height = h; rot = r; dock = false; }

    /// <summary>도킹: 몸 복사본의 자세를 기준으로 고정 간격(상대 위치·회전)을 유지한다.</summary>
    public void BeginDock(ulong carrierPlusOne, Vector3 p, Quaternion r) { CarrierNetId = carrierPlusOne; relPos = p; relRot = r; dock = true; }

    public void End() => CarrierNetId = 0;

    // 주인 PC 로컬 도킹: 서버가 보낸 상대 자세로 내 몸의 물리 자세를 바로 따라간다
    Unity.Netcode.NetworkObject localCube;
    Vector3 lrelPos;
    Quaternion lrelRot;
    bool local;

    public void BeginLocal(Unity.Netcode.NetworkObject cube, Vector3 p, Quaternion r) { localCube = cube; lrelPos = p; lrelRot = r; local = true; }

    public void EndLocal() { local = false; localCube = null; }

    void FixedUpdate()
    {
        if (local)
        {
            if (localCube == null || rb == null) { local = false; return; }
            var crb = localCube.GetComponent<Rigidbody>();
            Vector3 cp = crb != null ? crb.position : localCube.transform.position;
            Quaternion cr = crb != null ? crb.rotation : localCube.transform.rotation;
            rb.MovePosition(cp + cr * lrelPos);
            rb.MoveRotation(cr * lrelRot);
            return;
        }
        if (CarrierNetId == 0 || !MpSync.IsServer || rb == null) return;
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(CarrierNetId - 1, out var no) || no == null)
        {
            CarrierNetId = 0;
            var sb = GetComponent<SnapBlock>();
            if (sb != null) sb.MpCarrierLost(); // 운반자·정육면체가 사라졌다 — 해제 이벤트가 안 오므로 서버가 직접 푼다
            return;
        }
        if (no.IsOwner) return; // 서버 자신의 몸: 로컬 운반 컴포넌트가 처리
        Vector3 p = dock ? no.transform.position + no.transform.rotation * relPos : no.transform.position + Vector3.up * height;
        Quaternion r = dock ? no.transform.rotation * relRot : rot;
        rb.MovePosition(p); // 순간이동(rb.position 대입)은 겹친 동적 물체를 폭발적으로 밀어내므로 MovePosition으로
        rb.MoveRotation(r);
    }
}
