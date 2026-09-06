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
public class SnapBlock : MonoBehaviour
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

    public Rigidbody Body => body;
    public bool HasConnections => joints.Count > 0;
    public IEnumerable<SnapBlock> ConnectedBlocks => joints.Keys;

    /// <summary>이 블록이 other와 이미 결합돼 있는가.</summary>
    public bool HasConnectionTo(SnapBlock other) => other != null && joints.ContainsKey(other);

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        box = GetComponent<BoxCollider>();
        ApplyStabilitySettings();
    }

    private void OnDestroy()
    {
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

        AlignTo(myFace, otherFace);

        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        other.body.velocity = Vector3.zero;
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
        }
        other.joints.Remove(this);
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

    // 내 면이 상대 면과 정확히 맞물리도록 이 블록을 회전 + 이동시킨다. FromToRotation은 법선 축만
    // 맞추고 그 축 둘레 회전(roll)은 현재 자세를 최대한 보존한다 — 플레이어가 대충 맞춰 민 상태라
    // 그 정도면 그레이박스엔 충분하다(정밀 정렬은 후속).
    private void AlignTo(Face myFace, Face otherFace)
    {
        Quaternion delta = Quaternion.FromToRotation(myFace.normal, -otherFace.normal);
        transform.rotation = delta * transform.rotation;

        Vector3 newMyCenter = transform.TransformPoint(myFace.localCenter);
        transform.position += otherFace.center - newMyCenter;
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
