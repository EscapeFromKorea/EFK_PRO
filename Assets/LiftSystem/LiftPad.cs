using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 구-정사면체 협동 리프트의 발동 패드 — `DoorSystem/PadTrigger`와 같은 "존재 감지 + 홀드" 패턴이나,
/// 목표는 문이 아니라 <see cref="LiftPlatform"/>의 목표 위치다. 상세: docs/PRD/Lift.md §3·§7.
///
/// [왜 존재 판정이 아니라 무게 게이트인가] 확정 사양(PRD §7) — 가벼운 도형(구·정사면체)만 패드를
/// 누른 것으로 인정하고 정육면체는 걸러낸다. `Assets/CLAUDE.md`의 "무거워서 못 한다류 판정은 무게로"
/// 규칙을 반대 방향(가벼워야 통과)으로 적용한 것이다 — `Kind` 하드코딩 대신 `PlayerWeight.Of`를 써서
/// 무중력 버블 등으로 실효 무게가 바뀌어도 자동 반영된다.
///
/// [기본 임계 2.0의 근거] 저장소 정본 질량(구1.5/정사면체1.0/정육면체3.0) 기준으로 구·정사면체는
/// 통과, 정육면체만 차단하는 낮은 임계값이다. `WindZoneSystem.heavyWeightCutoff`(3f)·
/// `DoorSystem/ExitWeightPlate`(2.75, 반대 방향) 등 기존 선례보다 더 낮게 잡아 여유를 양쪽에 둔다.
///
/// [왜 무게를 겹침 시점이 아니라 매 프레임 다시 재는가] 겹치는 동안 무게가 바뀔 수 있다(작아지기
/// 패드, 무중력 버블 등). `ExitWeightPlate.TotalWeight()`와 같은 이유로, Enter 시점에 캐시하지 않고
/// 겹쳐 있는 바디 전체의 <b>현재</b> 무게를 매 FixedUpdate 다시 확인한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LiftPad : MonoBehaviour
{
    [Header("패드 눌림 애니메이션 (선택 — DoorSystem/PadTrigger와 같은 시각 피드백)")]
    public float padPressDepth = 0.1f;
    public float padSpeed = 5f;

    [Header("무게 게이트 (PRD §7)")]
    [Tooltip("겹쳐 있는 도형 중 이 값 미만인 것이 하나라도 있으면 눌림으로 인정한다. 기본 2.0은 " +
             "구(1.5)·정사면체(1.0)는 통과하고 정육면체(3.0)만 걸러낸다(질량 세트 A 기준). 값을 " +
             "낮출수록 '가벼움'의 기준이 빡빡해진다.")]
    public float lightWeightGate = 2.0f;

    [Header("연결")]
    [Tooltip("이 패드가 눌림 상태를 전달할 리프트. doorPhysics.SetPadPressed와 같은 직접 호출 방식이다.")]
    public LiftPlatform targetLift;

    public string playerTag = "Player";

    private Vector3 padStartPosition;
    private Vector3 padPressedPosition;
    private bool isPressed;

    // 바디별 겹침 콜라이더 수(무게 무관 — 겹침 자체를 셈). PadTrigger의 overlapCount와 같은 이유:
    // Player_Mesh(트리거)·Player_Collider(솔리드) 두 콜라이더가 한 도형당 Enter/Exit를 중복 호출한다.
    private readonly Dictionary<Rigidbody, int> overlaps = new Dictionary<Rigidbody, int>();
    private readonly List<Rigidbody> cleanupBuffer = new List<Rigidbody>();

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Start()
    {
        padStartPosition = transform.position;
        padPressedPosition = padStartPosition - new Vector3(0, padPressDepth, 0);
    }

    private void FixedUpdate()
    {
        isPressed = AnyLightBodyOverlapping();
        if (targetLift != null) targetLift.SetHeld(isPressed);

        Vector3 padTarget = isPressed ? padPressedPosition : padStartPosition;
        transform.position = Vector3.MoveTowards(transform.position, padTarget, padSpeed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;

        overlaps.TryGetValue(rb, out int n);
        overlaps[rb] = n + 1;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null || !overlaps.TryGetValue(rb, out int n)) return;

        if (n <= 1) overlaps.Remove(rb);
        else overlaps[rb] = n - 1;
    }

    private bool AnyLightBodyOverlapping()
    {
        if (overlaps.Count == 0) return false;

        cleanupBuffer.Clear();
        bool found = false;
        foreach (KeyValuePair<Rigidbody, int> kv in overlaps)
        {
            if (kv.Key == null) { cleanupBuffer.Add(kv.Key); continue; }
            if (!found && PlayerWeight.Of(kv.Key) < lightWeightGate) found = true;
        }
        foreach (Rigidbody r in cleanupBuffer) overlaps.Remove(r);
        return found;
    }
}
