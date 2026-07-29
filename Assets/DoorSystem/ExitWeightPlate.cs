using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무게로만 열리는 출구 판 — 무거운 도형(기본값 기준 네모)이 올라서야만 막힌 구역이 열린다.
///
/// [왜 DoorSystem에 있나]
/// 처음엔 꿈의 실타래 Phase 3(레벨 D "네모를 반드시 건너보내야 출구가 열린다")를 위해 만들었지만,
/// 하는 일 자체는 **"조건이 충족되면 장애물을 치운다"**로 DoorSystem의 레버·PadTrigger와 같은 계열이다.
/// 실타래에 종속된 로직이 하나도 없어(줄다리·앵커를 참조하지 않는다) 실타래 밖 어떤 구역에도 그대로
/// 쓸 수 있으므로 DoorSystem에 둔다. 실타래 레벨 D는 이 판의 여러 사용처 중 하나일 뿐이다.
///
/// [왜 '무게판'인가 — 도형 판별이 아니라 무게로 잰다]
/// Kind==Cube로 판별할 수도 있지만, 저장소 공통 규칙(`Assets/CLAUDE.md` 코드 컨벤션)이 "무거워서
/// 못 한다류 판정은 무게로 한다"이다. 무게로 재면 "무거운 것을 여기까지 가져와야 한다"가 그대로
/// 규칙이 되고, 나중에 무거운 운반 물체가 추가돼도 코드 변경 없이 성립한다. 기준식은 PlayerSystem의
/// `PlayerWeight.Of` 하나뿐이라(질량 × 실효 중력 배율) 무중력 버블 안의 네모(1.8)로는 못 연다
/// — 판을 버블 안에 두지 않는다.
///
/// [기본 임계 2.75의 근거] 구(1.5)+세모(1.0)=2.5로는 못 열고 네모(3.0)만 연다. 두 값 사이를 잡아,
/// 다른 도형 둘이 합쳐 대신 눌러 네모를 안 건너보내는 우회를 막는다. 협동 우회를 허용하고 싶으면
/// 2.5 아래로 낮추면 된다.
///
/// [왜 기본이 latch(한 번 열리면 유지)인가] 판을 누른 네모 자신이 그 문으로 지나가야 하므로, 밟는
/// 동안만 열리면 아무도 못 지나간다. "네모가 여기까지 왔다"는 사실 자체가 통과 조건이라 한 번 열리면
/// 유지하는 게 맞다. 밟고 있는 동안만 열리는 협동 문이 필요하면 latchOpen을 끈다.
///
/// 겹침 집계는 같은 폴더 PadTrigger의 overlapCount 패턴을 바디 단위로 확장했다 — 플레이어는 트리거
/// (Player_Mesh)와 솔리드(Player_Collider) 콜라이더를 함께 가져 한 도형당 Enter/Exit가 여러 번 불리는데,
/// 바디별로 세지 않으면 콜라이더 하나가 빠지는 순간 무게가 통째로 사라진다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ExitWeightPlate : MonoBehaviour
{
    [Header("무게 조건")]
    [Tooltip("판 위 도형들의 합산 무게(질량 × 실효 중력 배율)가 이 값 이상이면 열린다.\n" +
             "기본 2.75는 네모(3.0)만 열고 구+세모(2.5)로는 못 열게 하는 값이다 — 네모를 반드시 " +
             "건너보내게 하려는 레벨 D 의도. 협동 우회를 허용하려면 2.5 아래로 낮춘다.")]
    public float requiredWeight = 2.75f;

    [Header("열릴 때")]
    [Tooltip("열릴 때 밀어 올릴 문. 레버·패드와 **똑같은 경로**(doorPhysics.SetPadPressed)로 여닫으므로 " +
             "문이 열리는 모양·속도가 기존 문과 완전히 같다.\n" +
             "주의: 같은 문에 PadTrigger도 물려 있으면 매 물리 스텝 서로 값을 덮어써 문이 떨린다. " +
             "무게판 전용 문을 쓰거나 둘 중 하나만 연결한다.")]
    public doorPhysics targetDoor;

    [Tooltip("열리면 비활성화할 오브젝트들(다음 구역을 막고 있는 벽 등). 닫히면 다시 켜진다. " +
             "targetDoor와 함께 써도 되고, 둘 중 하나만 써도 된다.")]
    public GameObject[] exitBarriers;
    [Tooltip("켜면 한 번 열린 뒤 계속 열려 있는다(판을 누른 네모 자신이 그 문으로 지나가야 하므로 기본값). " +
             "끄면 무게가 유지되는 동안만 열린다(밟고 있어야 하는 협동 문).")]
    public bool latchOpen = true;

    public string playerTag = "Player";

    // 바디별 겹침 콜라이더 수. 값이 0이 되는 순간에만 그 도형이 판에서 완전히 내려간 것으로 본다.
    private readonly Dictionary<Rigidbody, int> overlaps = new Dictionary<Rigidbody, int>();
    private bool isOpen;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        overlaps.TryGetValue(rb, out int n);
        overlaps[rb] = n + 1;
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        if (!overlaps.TryGetValue(rb, out int n)) return;
        if (n <= 1) overlaps.Remove(rb);
        else overlaps[rb] = n - 1;
    }

    void Update()
    {
        if (isOpen && latchOpen) return;

        bool shouldOpen = TotalWeight() >= requiredWeight;
        if (shouldOpen == isOpen) return;

        isOpen = shouldOpen;
        ApplyBarriers();
        Debug.Log(isOpen
            ? $"[ExitWeightPlate] '{name}'이 눌렸습니다 (무게 {TotalWeight():0.##} ≥ {requiredWeight}). 다음 구역이 열립니다."
            : $"[ExitWeightPlate] '{name}' 무게가 모자라 출구가 다시 닫혔습니다.");
    }

    private float TotalWeight()
    {
        float sum = 0f;
        List<Rigidbody> dead = null;
        foreach (KeyValuePair<Rigidbody, int> kv in overlaps)
        {
            if (kv.Key == null) { (dead ??= new List<Rigidbody>()).Add(kv.Key); continue; }
            sum += PlayerWeight.Of(kv.Key);
        }
        if (dead != null) foreach (Rigidbody r in dead) overlaps.Remove(r);
        return sum;
    }

    private void ApplyBarriers()
    {
        // 문은 새 여닫기 로직을 만들지 않고 레버·패드가 쓰는 창구를 그대로 부른다 — "현재 문과 똑같은
        // 방식으로 열린다"가 요구사항이고, doorPhysics가 이미 목표 위치·속도·끼임 방지를 다 갖고 있다.
        if (targetDoor != null) targetDoor.SetPadPressed(isOpen);

        if (exitBarriers == null) return;
        foreach (GameObject b in exitBarriers)
            if (b != null) b.SetActive(!isOpen);
    }

    // 무게판이 어떤 장애물을 여는지 씬 뷰에서 선으로 보여준다(RainbowBridgeSwitch와 같은 배치 보조).
    void OnDrawGizmos()
    {
        if (exitBarriers == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0.35f, 1f);
        Vector3 from = transform.position + Vector3.up * 0.1f;
        foreach (GameObject b in exitBarriers)
        {
            if (b == null) continue;
            Gizmos.DrawLine(from, b.transform.position);
            Gizmos.DrawWireCube(b.transform.position, Vector3.one * 0.3f);
        }
    }
}
