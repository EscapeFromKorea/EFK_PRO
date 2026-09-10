using UnityEngine;

/// <summary>
/// T0RS 보조 리스폰 깃발 12곳(2026-09-03 #6, S1 식탁 CP 삭제 후 재배열 — 도입 당시엔 13곳)의
/// 순차 활성화 게이트 — 컨트롤타워 결정(2026-09-03 #3):
/// "이전 깃발을 밟아야 다음 깃발이 활성화" / 저장소 사본(RespawnSystem) 무수정 / 우리 컴포넌트로
/// 감싸는 방식.
///
/// [2026-09-06 전면 재설계 — 왜 바뀌었나, 근거: FROM_코워크 09-06 #3 1-② + 컨트롤타워 확정
/// TO_코워크 09-06 #8]
/// 이전 버전(2026-09-03~09-05)은 "다음 깃발을 잠근다" = 다음 깃발 RespawnZone.enabled=false로
/// 표현했다. 그 전제("MonoBehaviour가 disabled면 물리 콜백(OnTriggerEnter 포함)을 아예 못
/// 받는다")는 Update/FixedUpdate 같은 일반 콜백에는 맞지만 <b>물리 트리거 콜백에는 틀렸다</b> —
/// Unity는 GameObject.activeInHierarchy와 Collider.enabled만 보고 그 GameObject에 붙은 모든
/// MonoBehaviour(자신의 enabled 여부와 무관하게)에게 OnTriggerEnter를 통지한다(외부 검토
/// codex [확인함] 등급, 이 명제는 컨트롤타워 확정(TO_코워크 09-06 #8)이며 8e는 이를 측정하지
/// 않는다 — RespawnZone.OnTriggerEnter()에
/// enabled 검사가 없다는 사실 그대로). 즉 잠긴(RespawnZone.enabled=false)
/// 상태에서도 RespawnZone.OnTriggerEnter가 불려 RespawnController.SetCheckpoint가 호출됐다 —
/// 잠금 자체가 무효였다.
///
/// [새 잠금 = BoxCollider.enabled, 게이트는 부모와 분리된 자식 트리거로 이동]
/// Collider.enabled=false는 PhysX가 그 콜라이더를 물리 씬에서 완전히 빼는 것이라 "그
/// GameObject에 붙은 어떤 스크립트도" 트리거 콜백을 못 받는다(스크립트 enabled 플래그와 달리
/// PhysX가 직접 아는 상태라 예외가 없다([정정 2026-09-09, 검문27차 (나)] 09-09 12:13 8e 실측으로
/// 확정 — G1·G3·G2 기대값 일치, 검문27차 B 통과) — 새 콜라이더 잠금(BoxCollider.enabled)에서는
/// 그 기전이 적용되지 않는다. 8e(09-09 12:13) G1·G3·G2 기대값 일치는 콜라이더 잠금의 유효성만
/// 지지한다). 그래서
/// "다음 깃발을 잠근다"는 이제 다음 깃발 RespawnZone의 <b>자기 BoxCollider</b>를 끄는 것으로
/// 표현한다(RespawnZone MonoBehaviour 자신은 Place()가 항상 enabled=true로 둔다 — 스크립트
/// 잠금은 더 이상 안 쓴다). 문제는 그 콜라이더를 끄면 이 게이트도 같이 죽어 "밟았는지"를 감지
/// 못 하게 된다는 것 — 그래서 이 게이트는 부모(RespawnZone) GameObject가 아니라, 같은 크기의
/// <b>전용 자식 GameObject</b>(이름 접미 `_Gate`, V3_Checkpoints.Place()가 생성)의 트리거
/// 콜라이더에 붙는다. 이 자식 콜라이더는 부모가 잠겨도 항상 켜져 있다(Place()가 절대 안 끔) —
/// 그래서 게이트는 잠금 상태와 무관하게 계속 이벤트를 받는다.
///
/// 필드 넷의 역할:
/// - `zone`/`zoneCollider` = 이 게이트가 붙어 있는 부모 깃발 자신(RespawnZone·그 BoxCollider).
/// - `next`/`nextCollider` = 이 깃발을 밟으면 풀어줄 다음 깃발(RespawnZone·그 BoxCollider).
/// - 잠금 = `nextCollider.enabled = false`, 해제 = `true`.
///
/// [순서 보증 — 잠긴 채로 우회 진입해도 사슬이 안 풀린다]
/// 자식 트리거는 부모가 잠겨 있어도 항상 켜져 있으므로, 우회 경로로 아직 잠긴 깃발의 트리거
/// 볼륨을 그냥 지나가기만 해도 이 게이트의 OnTriggerEnter는 불린다 — 그것만으로 next를 풀면
/// "순차"가 이름뿐이게 된다. 그래서 next를 풀기 전에 <b>zoneCollider.enabled(내가 지금 유효한
/// 체크포인트인가)</b>를 확인한다: 내 자신의 콜라이더가 꺼져 있다(=나 자신이 아직 잠김)는 정상
/// 순서로 도달한 진입이 아니라는 뜻이므로 next를 풀지 않는다 — 구 버전의 "self.enabled 검사"와
/// 결과는 같고 표현 대상만 콜라이더로 바뀌었다.
///
/// [Q1 완화 — 순차 잠금 해제 토글, 컨트롤타워 결정 2026-09-03 #7, 2026-09-06 콜라이더 기준으로
/// 갱신]
/// 실패 모드: 볼륨 1곳을 어떤 이유로든(지형 버그·통행 문제 등) 못 밟으면 그 뒤 전부가 영영
/// 잠긴다. T0 측정 편의를 위해 `V3_Checkpoints.ToggleSequentialLock()`(메뉴 "8c")이 씬의
/// T0RS_SequentialGate 전부를 순회하며 각자의 `zoneCollider.enabled=true`로 강제 해제하고 이
/// `bypass` 플래그를 세운다. bypass는 논리를 바꾸지 않는다(OnTriggerEnter의 next 언락은 원래도
/// 멱등이라 이미 풀린 것을 다시 안 잠근다) — 순수하게 "지금 수동 해제 상태다"를 기록해 토글
/// 메뉴가 다음 클릭에 무엇을 할지(더 풀 것인지, Place() 초기 상태로 되돌릴 것인지) 판정하는
/// 상태값이다. 되돌릴 때는 next 사슬을 따라가며 첫 깃발만 남기고 다시 잠그고 bypass를 false로
/// 내린다.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class T0RS_SequentialGate : MonoBehaviour
{
    [Tooltip("이 게이트가 붙어 있는 부모 깃발 자신(RespawnZone). 참조용 — 잠금 판정 자체는 zoneCollider를 쓴다.")]
    public RespawnZone zone;

    [Tooltip("부모 깃발(zone)의 BoxCollider. enabled==false면 '나 자신이 아직 잠긴 상태' — 순서 보증(우회 진입 시 사슬 안 풀림) 판정에 쓴다.")]
    public BoxCollider zoneCollider;

    [Tooltip("이 깃발을 밟으면 풀어줄 다음 깃발(RespawnZone, 참조용). 마지막 깃발(12번)은 비워 둔다.")]
    public RespawnZone next;

    [Tooltip("다음 깃발의 BoxCollider — 실제 잠금/해제 대상. enabled=false가 잠금, true가 해제. 마지막 깃발은 비워 둔다.")]
    public BoxCollider nextCollider;

    [Tooltip("[Q1 완화] 메뉴 '8c. Toggle T0RS Sequential Lock'이 전부 해제할 때 세우는 상태 플래그 — " +
             "논리에는 관여하지 않는다(순수 기록용). 기본 false.")]
    public bool bypass;

    private void Reset()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (next == null || nextCollider == null) return;
        if (!other.CompareTag("Player")) return;
        // 내 존 자체가 지금 잠겨 있다(zoneCollider.enabled==false) = 정상 순서로 도달한 진입이
        // 아니다(우회 진입) — next를 풀지 않는다. 플레이어는 콜라이더가 둘(Player_Mesh/
        // Player_Collider)이라 진입마다 두 번 불릴 수 있다 — nextCollider.enabled 대입은
        // 멱등이라 중복 호출이 안전하다.
        if (zoneCollider == null || !zoneCollider.enabled) return;
        if (!nextCollider.enabled) nextCollider.enabled = true;
    }
}
