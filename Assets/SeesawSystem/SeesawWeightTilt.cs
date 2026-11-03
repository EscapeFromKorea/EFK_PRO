using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 시소 위쪽에 올라선 Rigidbody의 하중을 힌지 축 토크로 보강한다.
/// 순수 PhysX 접촉만으로는 플레이어 이동 코드·큰 angularDrag 때문에 한쪽이 충분히 내려가지 않는
/// 배치가 있어, 실제 하중의 방향은 유지하되 "한쪽에 올라서면 끝까지 기운다"는 게임 규칙을 보장한다.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(HingeJoint))]
[DisallowMultipleComponent]
public class SeesawWeightTilt : MonoBehaviour
{
    [Header("하중 기울기 보정")]
    [Tooltip("PhysX 접촉 하중에 추가할 보조 토크의 계수입니다. 0이면 추가 토크 없이 순수 물리만 사용합니다. 높을수록 더 빠르게 기웁니다.")]
    [Min(0f)] public float loadTorqueMultiplier = 24f;
    [Tooltip("이 거리보다 중심에 가까운 하중은 기울기 계산에서 제외합니다.")]
    [Min(0f)] public float centerDeadZone = 0.2f;

    [Header("안정적인 완전 기울기")]
    [Tooltip("직접 켠 경우에만 회전 저항과 힌지 제한을 보정합니다. 기본은 꺼짐으로 씬의 물리 설정을 유지합니다.")]
    public bool enforceResponsivePhysics = false;
    [Range(1f, 75f)] public float minimumTiltAngle = 45f;
    [Min(0f)] public float maximumAngularDrag = 0.75f;

    private Rigidbody body;
    private HingeJoint hinge;
    private readonly Dictionary<Rigidbody, float> supportedBodies = new Dictionary<Rigidbody, float>();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        hinge = GetComponent<HingeJoint>();
        ApplyResponsivePhysics();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        body = GetComponent<Rigidbody>();
        hinge = GetComponent<HingeJoint>();
        ApplyResponsivePhysics();
    }

    private void ApplyResponsivePhysics()
    {
        if (!enforceResponsivePhysics || body == null || hinge == null) return;

        body.angularDrag = Mathf.Min(body.angularDrag, maximumAngularDrag);
        body.maxAngularVelocity = Mathf.Max(body.maxAngularVelocity, 12f);
        hinge.useLimits = true;
        JointLimits limits = hinge.limits;
        limits.min = Mathf.Min(limits.min, -minimumTiltAngle);
        limits.max = Mathf.Max(limits.max, minimumTiltAngle);
        limits.bounciness = 0f;
        limits.bounceMinVelocity = 0f;
        hinge.limits = limits;
    }

    private void OnCollisionStay(Collision collision)
    {
        Rigidbody other = collision.rigidbody;
        if (other == null || other == body || !IsRestingOnTop(other)) return;
        supportedBodies[other] = Time.fixedTime;
    }

    // Exit 하나로 바디 전체를 지우지 않는다. 복수 콜라이더 중 하나가 떨어져도 남은 Stay가
    // 시간을 갱신한다. 모든 접촉이 끊긴 바디만 FixedUpdate의 시간 검사에서 제거한다.

    private bool IsRestingOnTop(Rigidbody other)
    {
        Vector3 pivot = transform.TransformPoint(hinge.anchor);
        // 바닥 아래에서 받친 블록·옆 충돌은 하중으로 취급하지 않는다.
        return Vector3.Dot(other.worldCenterOfMass - pivot, transform.up) > -0.05f;
    }

    private void FixedUpdate()
    {
        if (body == null || hinge == null) return;

        Vector3 pivot = transform.TransformPoint(hinge.anchor);
        Vector3 axis = transform.TransformDirection(hinge.axis).normalized;
        float moment = 0f;
        List<Rigidbody> stale = null;

        foreach (KeyValuePair<Rigidbody, float> entry in supportedBodies)
        {
            Rigidbody load = entry.Key;
            if (load == null || Time.fixedTime - entry.Value > Time.fixedDeltaTime * 1.5f)
            {
                (stale ??= new List<Rigidbody>()).Add(load);
                continue;
            }

            Vector3 lever = load.worldCenterOfMass - pivot;
            // PlayerWeight는 무중력·크기 변경 후의 실제 유효 무게를 반영하고, 일반 Rigidbody는 mass를 돌려준다.
            float weight = PlayerWeight.Of(load);
            float signedMoment = Vector3.Dot(Vector3.Cross(lever, Physics.gravity.normalized * weight), axis);
            if (Mathf.Abs(signedMoment) >= centerDeadZone * weight)
                moment += signedMoment;
        }

        if (stale != null)
            foreach (Rigidbody load in stale) supportedBodies.Remove(load);

        if (Mathf.Abs(moment) > 0.0001f)
            body.AddTorque(axis * (moment * loadTorqueMultiplier), ForceMode.Force);
    }

    // 최초 씬뿐 아니라 추가 로드된 씬에도 적용한다. 도메인 리로드를 끈 에디터에서도 중복 구독하지 않는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneUpgrade()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        UpgradeExistingSeesaws();
    }

    private static void UpgradeExistingSeesaws()
    {
        foreach (SeesawTuningNotes notes in UnityEngine.Object.FindObjectsOfType<SeesawTuningNotes>(true))
            TryUpgrade(notes);

        foreach (RotatingPlate plate in UnityEngine.Object.FindObjectsOfType<RotatingPlate>(true))
            TryUpgrade(plate);
    }

    /// <summary>기존 물리 컴포넌트를 확인한 뒤 보정기를 추가한다. 런타임 생성된 시소의 마커도 호출한다.</summary>
    public static void TryUpgrade(Component marker)
    {
        if (!Application.isPlaying || marker == null) return;
        bool identified = marker is SeesawTuningNotes ||
            (marker is RotatingPlate plate && plate.plateUseGravity &&
             plate.name.IndexOf("seesaw", StringComparison.OrdinalIgnoreCase) >= 0);
        if (!identified || marker.GetComponent<Rigidbody>() == null || marker.GetComponent<HingeJoint>() == null)
            return;
        if (marker.GetComponent<SeesawWeightTilt>() == null)
            marker.gameObject.AddComponent<SeesawWeightTilt>();
    }
}
