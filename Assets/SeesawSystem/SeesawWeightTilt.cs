using System;
using System.Collections.Generic;
using UnityEngine;

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
    [Tooltip("하중 모멘트에 곱할 보정 토크. 높을수록 한쪽에 올라섰을 때 더 빠르게 제한 각도까지 기웁니다.")]
    [Min(0f)] public float loadTorqueMultiplier = 24f;
    [Tooltip("이 거리보다 중심에 가까운 하중은 기울기 계산에서 제외합니다.")]
    [Min(0f)] public float centerDeadZone = 0.2f;

    [Header("안정적인 완전 기울기")]
    [Tooltip("켜면 회전 저항을 낮추고 힌지 제한을 이 각도 이상으로 확보합니다. 한쪽 하중이 끝까지 내려갈 수 있게 하는 기본값입니다.")]
    public bool enforceResponsivePhysics = true;
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

    private void OnCollisionExit(Collision collision)
    {
        if (collision.rigidbody != null) supportedBodies.Remove(collision.rigidbody);
    }

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

    // 기존 메뉴로 생성했거나 Map3 생성기로 배치된 시소도 씬을 다시 만들지 않아도 런타임에 보정한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void UpgradeExistingSeesaws()
    {
        foreach (SeesawTuningNotes notes in UnityEngine.Object.FindObjectsOfType<SeesawTuningNotes>(true))
            if (notes.GetComponent<SeesawWeightTilt>() == null)
                notes.gameObject.AddComponent<SeesawWeightTilt>();

        foreach (RotatingPlate plate in UnityEngine.Object.FindObjectsOfType<RotatingPlate>(true))
            if (plate.name.IndexOf("seesaw", StringComparison.OrdinalIgnoreCase) >= 0 &&
                plate.GetComponent<SeesawWeightTilt>() == null)
                plate.gameObject.AddComponent<SeesawWeightTilt>();
    }
}
