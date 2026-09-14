#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// KitchenMapV3 — T0 측정 보조 리스폰 깃발 (저장소 RespawnSystem 정식 사용).
///
/// ⚠️ 이 12곳(2026-09-03 컨트롤타워 결정 — S1 식탁은 조형물로 남기고 루트에서 제외, 구 T0RS_02
/// 삭제 · 나머지 12곳은 🔒L1 주 경로 확정표(1of4:455~475)+🔒K3 순서로 재배열·연번 재부여)은
/// 릴레이 잠금 🔒K3가 고정한 정식 체크포인트 CP0~CP8이 아니다 — T0 블록아웃 측정
/// 기간에 팀이 구간을 오가며 재는 것을 돕는 보조 리스폰이다(검문_Editor3파일_반려표_2026-09-02.md
/// §E-1 L3 판정으로 재분류). 이름 접두는 반드시 T0RS_nn(T0 Respawn) — CP 접두 사용 금지. 정식
/// CP0~CP8(트리거·페널티·적용대상 명시, 도형별 리스폰 등 🔒M3 표 그대로)은 기믹 배선 단계에서
/// 별도 구현한다. 이 그룹은 그때 통째로 삭제되는 것을 전제로 한다.
///
/// 저장소의 "Tools/Respawn/Create Checkpoint Pole"(트리거 구역 + 막대 + 3단계 깃발 게양)을
/// 그대로 쓴다 — 직접 조립하지 않는 이유 = 정식 생성기 원칙(계층·콜라이더 제거·깃발 배선이
/// 메뉴에 들어 있다).
///
/// [R1 반려 해소 — 볼륨 규격]
/// 저장소 기본값(RespawnMenuItem.ZoneHeight=18)은 지점 위로 18U짜리 기둥을 세워, 그 발 밑
/// (x,y) 위에 있는 무관한 실재물(상판·창턱·선반 등)을 층수와 무관하게 통째로 삼킨다 — 🔒M3
/// "관문 무효화" 위험(검문 R1). 생성 직후 BoxCollider를 size=(6,H,6)/center=(0,H/2,0)로
/// 재설정해 판정 범위를 "지점 상면 ~ 지점 상면+H"로 제한한다. 기본 H=3, T0RS_08은 예외로 0.6
/// (아래 R5 설명 참고). 검산 결과 원 반려표가 지목한 대상(Counter_W_L·Shelf_IN_후면W·Pot_A·
/// WindowSill·Shelf_OUT)은 T0RS_11·12에서 더 이상 겹치지 않는다. 일부 지점은 여전히 "같은
/// 표면 위 인접 소품"과 미세하게 겹치는데, 실측(파이썬, 260개 콜라이더 전수 대조)으로 특정하면
/// T0RS_04↔IS_Board_도마(관문 안쪽 소품, 겹침 6.0×1.6U) · T0RS_06↔WindowSill_창턱(통행
/// 지형 자체, 겹침 6.0×0.8U — 지형이라 무해) · T0RS_13↔YardBox_1(3.7×1.0U)·FarSide_LeverPad
/// 4건이다(위 이름·번호는 이 문단이 쓰인 시점의 구번호 — 2026-09-03 재배열 후 대응: 구
/// T0RS_04→신 T0RS_03·구 T0RS_06→신 T0RS_06·구 T0RS_13→신 T0RS_12, 좌표는 전부 불변이라
/// 겹침 판정 자체는 그대로 유효하다). 전부 그 지점이 담당하는 관문의 안쪽·전방(플레이어가 그 관문을 이미 통과했거나
/// 통과 중에만 닿는 자리)이라 🔒M3가 금지하는 "관문 건너뛰기"로 이어지지 않는다(map-reviewer
/// 2026-09-03 재검 확인). 지면 판정 레이(구역 중앙 1점)도 이 소품들의 x,y 열 밖이라 리스폰
/// 오작동은 없다.
///
/// [R5 반려 해소 — 낙하 스폰 ≠ 덕트 지붕, 2026-09-03 재검 반영]
/// RespawnController.dropExtraHeight(컨트롤러 하나뿐이라 전체(당시 13곳, 2026-09-03 재배열
/// 후 12곳) 전부에 적용)가 크면 T0RS_08(구번호 — 신 T0RS_07, 상부장 능선, z22.2)의 낙하
/// 스폰점이 Duct_Floor 밑면(z23.6) 위로 밀려 올라가 덕트 지붕
/// 속에서 스폰된다. 1차 수정에서 H=1.0·dropExtraHeight=0.5로 "스폰점(피벗) 23.2 < 23.6"까지만
/// 확인했는데, map-reviewer 재검에서 **피벗이 아니라 플레이어 몸(반경 0.5)의 최상단**으로 재야
/// 한다는 지적을 받았다(피벗 기준 여유 0.4는 몸 반경을 더하면 −0.1로 뒤집힌다). 그래서 T0RS_08
/// H를 0.6으로 더 낮춘다(dropExtraHeight는 0.5 그대로 — 전역값이라 나머지 12곳까지 흔들 이유가
/// 없다):
///   피벗 z = 22.2(baseZ) + 0.6(H) − 0.5(dropSpawnInset) + 0.5(dropExtraHeight) = 22.8
///   몸 상단 = 22.8 + 0.5(플레이어 반경) = 23.3 < 23.6(Duct_Floor 밑면) — 0.3U 여유로 통과.
/// 트리거 자체는 여전히 성립한다 — 실제 서 있는 몸(z22.2~23.2, 반경 기준 직경 1.0)과 볼륨
/// (z22.2~22.8)의 겹침이 0.6U라 밟으면 정상 발동한다. 13곳(당시 개수 — 2026-09-03 재배열 후
/// 12곳, S1 식탁 CP 삭제분 제외) 전부를 "피벗+반경 0.5=몸 상단"
/// 기준으로 재검산했고 전부 clear다(T0RS_08 23.3 < 23.6 포함, 나머지 12곳은 최소 여유 8U대).
///
/// [R2 반려 해소 — 생성 검증, 2026-09-10 인스턴스ID 기준으로 강화]
/// Selection.activeGameObject만 믿으면 메뉴 실행이 지연/실패해도 직전 오브젝트를 계속 개명해
/// "12/12 완료"(당시는 13/13)를 거짓으로 찍을 수 있다(V3_Play.cs가 GameObject.Find 재조회로 막은 것과 같은
/// 구멍). 여기서는 호출 전후 씬의 RespawnZone 인스턴스ID 집합을 비교해 "새로 생긴 것"을 직접
/// 찾는다 — Selection에 전혀 의존하지 않는다. 신규 개수가 정확히 1이 아니면(0=실패, 2+=중복
/// 생성 등 이상 상태) 그 자리에서 경고 후, 이 한 번의 동기 호출이 만든 것이 확실한 신규
/// 인스턴스(들)만 되돌리고(destroy) 중단한다. made 카운트는 이 검증을 통과한 것만 센다.
///
/// [2026-09-10, 컨트롤타워 판정 — 씬 전체 스캔형 "고아 Checkpoint 정리" 완전 폐기]
/// 이전 버전은 Place() 시작부에서 Object.FindObjectsOfType&lt;RespawnZone&gt;() 전체를 훑어
/// 이름="Checkpoint"(+부모없음+BoxCollider 크기가 생성기 기본값과 일치, 안전선 2개)인 것을
/// 찾아 지웠다 — 그런데 이 필터는 아무리 좁혀도 "팀원이 같은 저장소 메뉴로 방금 만들고 아직
/// 개명 전인 체크포인트"와 씬 상태만으로 구분할 수 없었다(소유권 태그가 없다 — #41 검토 요청).
/// 컨트롤타워 판정: <b>V3는 자기 루트(V3.Root()) 밖의 오브젝트를 어떤 경우에도 지우지
/// 않는다</b> — 이 스윕은 통째로 제거했다(Place() 안 어디에도 이제 이런 스캔이 없다). 대신
/// 위 문단의 인스턴스ID 비교가 "생성 직후, 아직 우리 손을 벗어나지 않은 바로 그 한 개"만
/// 정확히 특정하고, 그 뒤 개명·재부모화·자식 _Gate 생성·컴포넌트 설정 전체를 try/catch로
/// 감싸 실패하면 <b>방금 특정한 그 인스턴스(참조 자체, 이름 재검색 아님)</b>만 DestroyImmediate
/// 한다 — 씬을 훑어 이름으로 찾지 않으므로 위 철칙과 충돌하지 않는다(그 인스턴스는 이 메서드
/// 자신이 이번 호출에서 만든 것이 인스턴스ID로 확정돼 있어, 다른 누구의 것일 수 없다).
///
/// [T0RS_11 좌표 이동 — 2차(컨트롤타워) → 3차(map-reviewer, Rail 누락) → 4차/N6(map-reviewer,
/// 착지 주머니 소프트락) 누적, 전부 2026-09-03. 아래 "T0RS_11"은 전부 이 문단이 쓰인 시점의
/// 구번호 — 2026-09-03 재배열 후 신 T0RS_10. 좌표(19.5,93.0,0)·Y180·이름은 그대로 승계했다.]
/// 1차 검토 요청: (9,87,0)이 Wall_N_C 외측면 y87과 맞물림 → 컨트롤타워가 (1.5,88.8,0)로 승인
/// 이동(2차). 2차 재검산 거리표에 Rail_Side_L(x0~1)이 빠져 있어 중심 x1.5까지 거리 0.50 = 플레이어
/// 반경과 정확히 같아 여유 0 반려(3차) → x를 2.0으로 이동. 그런데 3차 통과 뒤 **N6**: (2.0,88.8)이
/// 있는 자리 자체가 Wall_W·Rail_Side_L·북벽·Line_Pole_L·Faucet_Out·YardPlant_2·세탁기로 둘러싸인
/// "서쪽 주머니"이고, 그 주머니의 모든 출구가 ≤1.0U(기둥↔세탁기 대각 0.849 · 수도↔레일 1.0 ·
/// 화분↔레일 0.5)라 리스폰이 이 안이면 몸(반경 0.5)이 주머니를 못 빠져나가는 소프트락 위험이다
/// (🔒H6). 개별 거리들은 그동안 전부 통과였지만 "주머니 자체가 닫혀 있다"는 형태 문제라 거리
/// 검산만으로는 못 잡는다 — map-reviewer가 3차 통과 후 별도로 짚었다.
/// 4차: 통로를 완전히 벗어난 첫 개활지 **(19.5,93.0,0)**로 재배치, 이름도 위치에 맞게
/// `T0RS_11_뒷마당_세탁기동편`으로 변경. 깃대는 Y180 유지(로컬 x−2.6 → 월드 +x쪽 22.1,93.0 —
/// 이번엔 벽이 아니라 세탁기·화분 쪽을 피하는 용도로도 맞는 방향이라 그대로 둔다).
/// 재검산(파이썬, 260개 콜라이더 전수 스캔, 최근접 8개 — 바닥면은 발밑이라 표에서 제외):
///   Jangdok_0 1.12(코너 대각) · WashingMachine 1.50 · SmallPot_1 2.69 · Shelf_OUT_난간 3.10 ·
///   Shelf_OUT_낙하받이 3.50 · Shelf_OUT_복귀계단_Step1/2 5.3~5.9 · Wall_N_D1 6.00 (전부 U).
///   최소값 1.12(Jangdok_0) ≥ 플레이어 반경 0.5 — 여유 0.62U. 이번엔 개별 거리뿐 아니라
///   "사방이 열려 있는가"도 확인: 남쪽(y 증가, 게이트 방향)·서쪽(x 감소, 세탁기 남쪽 회랑)·
///   북쪽(x18 세탁기 동면 넘어) 전부 다음 최근접 장애물까지 1.1U 이상이라 폐주머니가 아니다.
///   깃대+깃발 조립 월드 AABB(x[21.20,22.15]·y[92.95,93.05]·z[0,4])는 SmallPot_1(y≤92)·
///   Jangdok_0(x≥20·y≥94)·WashingMachine(x≤18)과 겹침 0.
///   낙하 스폰(19.5,93,3.0) 직상 천장 없음(clear) — Shelf_OUT_낙하받이(z9.9~10.2)는 y범위
///   86~89.5라 이 점(y93)의 x,y 발자국 밖이라 애초에 안 걸린다. 지면 레이 Floor_Yard z0 정상 히트.
///   볼륨(x16.5~22.5·y90~96·z0~3) 겹침 3건: Jangdok_0(상면 z7.4)·WashingMachine(상면 z9.3)은
///   상면이 볼륨 천장(z3) 위라 아래쪽 몸통만 스쳐 무해. SmallPot_1(상면 z3, 정적 소품)은 상면이
///   볼륨과 정확히 같은 높이까지라 원칙상 "겹침"이지만 x축 0.5U 코너 슬리버뿐이고 지면 판정
///   레이(중심 x=19.5)가 SmallPot_1의 x범위(22~26) 밖이라 리스폰 오작동은 없다 — 조치 없이 기록.
///
/// [순차 활성화 — 컨트롤타워 결정 2026-09-03 #5]
/// "이전 깃발을 밟아야 다음 깃발이 활성화"(사용자 결정 3번). 저장소 사본(RespawnSystem 6개 폴더)은
/// 한 바이트도 안 건드린다 — 같은 GameObject에 T0RS_SequentialGate(신규,
/// Assets/KitchenMapV3/Scripts/T0RS_SequentialGate.cs, 런타임)를 나란히 붙여 같은 트리거 이벤트를
/// 우리 쪽에서도 받는다(Unity는 트리거 콜백을 그 오브젝트의 enabled 컴포넌트마다 각각 보낸다).
/// 잠금은 다음 깃발의 RespawnZone.enabled=false로 표현(GameObject·BoxCollider는 항상 살아 있어야
/// 우리 게이트도 계속 트리거를 받는다 — 그래서 이 둘은 절대 안 끈다). T0RS_01만 시작부터
/// RespawnZone.enabled=true(=언락), 02~12는 false(=잠금)로 시작해 Place() 안에서 순서대로
/// gate.next를 연결한다. 이유·안전장치(우회 진입 방지 등) 전문은 T0RS_SequentialGate.cs 자체
/// 주석 참고 — Place() 재실행 시 그룹 전체를 지우고 새로 만들므로(위 R2/R1 로직 그대로) 중복
/// 컴포넌트·잔여 잠금 상태가 남지 않는다.
///
/// [2026-09-06 재설계 — 위 문단의 "RespawnZone.enabled=false" 전제 무효, 근거: FROM_코워크
/// 09-06 #3 1-② + 컨트롤타워 확정 TO_코워크 09-06 #8]
/// Unity는 GameObject에 붙은 MonoBehaviour의 enabled 여부와 무관하게 OnTriggerEnter를 보낸다
/// (RespawnZone.OnTriggerEnter()에 enabled 검사가 없다) — 그래서 위 방식으로는 잠긴 RespawnZone도
/// 여전히 RespawnController.SetCheckpoint를 호출해 버렸다(재현 예정: V3_GateTestRunner.cs G1
/// 케이스, 8e 미실행 — map-reviewer 23차 R3, "재현"이라는 확정 서술은 실제 플레이모드 실행
/// 결과 없이는 과장이라 "재현 예정"으로 정정).
/// 새 표현: 잠금 = 다음 깃발 <b>자신의 BoxCollider.enabled</b>(RespawnZone MonoBehaviour는
/// 이제 항상 enabled=true — Place()가 명시적으로 그렇게 둔다). Collider.enabled=false는
/// PhysX가 그 콜라이더를 물리 씬에서 완전히 빼 "그 GameObject의 어떤 스크립트도" 트리거를 못
/// 받게 하므로 예외가 없다(미검증 — 8e 실측으로 확정). 다만 그러면 게이트 자신도 같이 죽으므로, 각 T0RS_nn 아래에 부모와
/// 동일 center/size/회전의 <b>전용 자식 GameObject `T0RS_nn_Gate`</b>(콜라이더 외 컴포넌트
/// 없음)를 새로 만들고 그 자식의 트리거 콜라이더에 T0RS_SequentialGate를 붙인다 — 이 자식
/// 콜라이더는 부모 잠금 상태와 무관하게 항상 켜져 있다. 게이트 필드는 `zone`(부모 RespawnZone)·
/// `zoneCollider`(부모 BoxCollider, "나 자신이 유효한 체크포인트인가" 순서 보증용)·`next`(다음
/// 깃발 RespawnZone)·`nextCollider`(다음 깃발 BoxCollider, 실제 잠금/해제 대상) 넷이다. 01만
/// 시작부터 `box.enabled=true`(언락), 02~12는 `false`(잠금)로 시작 — 세부는
/// T0RS_SequentialGate.cs 클래스 주석의 "[2026-09-06 전면 재설계]" 참고.
///
/// [깃발 렌더러 — 콜라이더만 꺼도 "미방문=깃발 없음" 유지된다]
/// [R3 정정, map-reviewer 21차 반려] 이전 문구 "GameObject 활성 여부와 무관하게 컴포넌트가
/// enabled인 한 항상 실행되고"는 부정확했다 — 정확히는: Awake는 <b>컴포넌트 자신의 enabled
/// 여부와 무관하게</b> 실행되지만(비활성 컴포넌트도 Awake는 받는다), <b>GameObject 자체가
/// 비활성이면 호출되지 않는다</b>(활성화되는 순간 그때 호출된다). 이 프로젝트에서는 T0RS_nn
/// GameObject 자체를 SetActive로 끄지 않고(잠금은 자기 BoxCollider.enabled만 대입, Place()
/// 참고) 항상 활성 상태로 두므로 결론 자체(Awake가 1회 정상 실행된다)는 바뀌지 않는다.
/// RespawnZone.Awake()는 그렇게 정상 실행되고, 그 안에서
/// flagRenderer.enabled=false로 깃발을 무조건 숨긴다 — 콜라이더 상태와 무관한 별개 처리다.
/// "미방문=깃발 없음"이 유지되는 근거를 게양 경로 하나로 재작성한다: 깃발을 다시 켜는(=
/// 게양하는) 경로는 RespawnZone.SetFlagState()뿐이고, SetFlagState를 실제로
/// 호출하는 곳은 RespawnController.StoreCheckpoint(내부 "if
/// (currentZone != null) currentZone.SetFlagState(...)"·"zone.SetFlagState(...)") 한 곳뿐이며,
/// StoreCheckpoint를 부르는 정적 진입점 RespawnController.SetCheckpoint의 호출처는 이 프로젝트
/// 전체(KitchenMapV3)에서 RespawnZone.OnTriggerEnter()뿐이다(grep 전수 확인
/// — R3, 2026-09-06). 즉 게양으로 이어지는 유일한 경로가 OnTriggerEnter이므로, 잠긴 존은 자기
/// BoxCollider가 꺼져 있어 그 경로 자체가 안 불리고, 그래서 SetFlagState도 절대 호출되지
/// 않는다 — Awake가 숨긴 상태(깃발 없음) 그대로 유지된다는 결론은 유효하다(경로 추적 근거는
/// 강화됐으나, 실플레이 관찰로 최종 확인은 여전히 필요 — 미검증).
///
/// [S1 식탁 CP 삭제 + 주 경로 순서 재배열 — 컨트롤타워 판정, 2026-09-03 #6]
/// 사용자 결정: "식탁(S1)은 조형물로만 남긴다. 루트에서 뺀다 — CP2(식탁 위 깃발) 삭제. 앞치마
/// 계단(Apron_앞치마)은 잠금에 묶여 있으면 남기고 아니면 삭제." 컨트롤타워 판정: S1 식탁은
/// 릴레이 우회로 U2-c(비잠금, 1of4:985·1065) — 🔒L1/🔒L3 V1 무대는 "식탁 아래 러그 존"
/// (1of4:469·505)이고 B3 실 앵커는 의자 다리(4of4:641)라 식탁·의자·러그 자체(조형물)는 그대로
/// 두되, 구 T0RS_02_식탁(식탁 위 깃발)은 🔒K3 CP 세트(3of4:1163~1171)에 대응 항목이 없어 삭제.
/// `Apron_앞치마` 계단은 잠금 수치가 없는 U2-c 소품이라 삭제(V3_Build.cs, map-builder 처리 —
/// Table_Top·의자·러그는 그대로 유지). 나머지 12곳은 🔒L1 주 경로 확정표(1of4:455~475)+🔒K3
/// 순서(스폰→계단 발치→아일랜드[P1]→동쪽 화력 징검다리[P2]→코너 상판→서쪽 복귀→싱크
/// 옆[V3]→IN선반[V4·CP5]→상부장 능선[P4 출발]→[P4 스윙] 냉장고→덕트→뒷마당→마당 중앙→
/// 게이트 앞)로 재배열하고 T0RS_01~12로 연번을 재부여했다(좌표·H·yRot은 전부 불변 — 이미 4차
/// 검문에서 이격·지면 검산이 끝난 값이라 손대지 않았다). 구번호는 각 항목 인라인 주석의
/// "(구 T0RS_nn)"으로 남긴다 — 위 R1/R5/N6/T0RS_11 이력 문단들은 전부 그 문단이 작성된
/// 시점의 구번호를 그대로 쓴다(반려표·검산 기록의 원문 대조를 위해 재타이핑하지 않음).
/// </summary>
public static class V3Checkpoints
{
    const float DefaultH = 3.0f;
    const float DropExtraHeight = 0.5f;   // [R5] 컨트롤러 전역값 — 근거는 클래스 주석 참고.

    // (이름, 문서 x, 문서 y, 바닥 z, 볼륨 높이 H, Y축 회전도) — 루트 순서
    // [2026-09-03 #6, 컨트롤타워] S1 식탁 CP(구 T0RS_02) 삭제 + 🔒L1 주 경로 확정표(1of4:455~475)
    // +🔒K3 순서로 재배열, T0RS_01~12 연번 재부여. 좌표·H·yRot은 전부 이전 값 그대로(무변경) —
    // 각 항목 인라인 주석의 "(구 T0RS_nn)"이 재배열 전 번호다. 근거·판정 전문은 클래스 주석
    // "[S1 식탁 CP 삭제 + 주 경로 순서 재배열]" 참고.
    static readonly (string name, float x, float y, float z, float h, float yRot)[] Defs =
    {
        // 촘촘 배치 원칙(2026-09-01 사용자 피드백): 높은 상판보다 "다음에 갈 곳"의 진입부·바닥에
        // 우선 배치 — 깃발이 걷는 눈높이에서 보여 길잡이를 겸한다.
        ("T0RS_01_스폰_러그초입",       63.0f, 13.0f,  0.0f,  DefaultH, 0f),   // (구 T0RS_01) START 매트 바로 앞
        ("T0RS_02_계단_발치",           42.0f, 45.0f,  0.0f,  DefaultH, 0f),   // (구 T0RS_03) 아일랜드行 수납상자 계단 앞 바닥
        ("T0RS_03_아일랜드",            39.5f, 68.0f,  9.6f,  DefaultH, 0f),   // (구 T0RS_04) P1 출발
        ("T0RS_04_코너_상판",           86.5f, 81.0f,  9.6f,  DefaultH, 0f),   // (구 T0RS_07) P2 진입(커피머신 동쪽) — 동쪽 화력 징검다리 도착
        ("T0RS_05_조리대_싱크옆",       31.0f, 80.0f,  9.6f,  DefaultH, 0f),   // (구 T0RS_05) V3 싱크 옆 — 서쪽 복귀 후
        ("T0RS_06_IN선반",              14.5f, 82.6f, 10.2f,  DefaultH, 0f),   // (구 T0RS_06) V4 (M-확인-1의 CP5 확정 지점)
        ("T0RS_07_상부장_능선",         55.0f, 82.0f, 22.2f,  0.6f,     0f),   // (구 T0RS_08) [R5 3차] 몸 상단(피벗+반경0.5) 기준 재계산 — H 1.0→0.6, P4 출발
        ("T0RS_08_냉장고_위",           93.5f, 55.0f, 20.0f,  DefaultH, 0f),   // (구 T0RS_09) P4 스윙 도착·V5
        ("T0RS_09_덕트_진입",           92.0f, 80.0f, 24.0f,  DefaultH, 0f),   // (구 T0RS_10) 덕트 동측 패드
        // [좌표 이동 누적, 구 T0RS_11 시절] 9,87,0 → 1.5,88.8,0(2차, 컨트롤타워) → 2.0,88.8,0
        // (3차, map-reviewer, Rail_Side_L 누락) → 19.5,93.0,0(4차/N6, map-reviewer — 벽·기둥·
        // 수도·화분·세탁기로 둘러싸인 서쪽 주머니 자체가 출구 전부 ≤1.0U인 소프트락 위험지대라
        // 통로를 완전히 벗어난 첫 개활지로 재배치). 클래스 주석 "[T0RS_11 좌표 이동]" 참고.
        // Y180 회전은 유지 — 깃대(로컬 x−2.6)가 벽이 아니라 세탁기 쪽을 피하려는 용도로 +x쪽
        // (월드 22.1,93.0)으로 보낸다.
        ("T0RS_10_뒷마당_세탁기동편",   19.5f, 93.0f,  0.0f,  DefaultH, 180f), // (구 T0RS_11) N6: 통로 통과 후 첫 개활지
        ("T0RS_11_마당_중앙",           30.0f, 91.5f,  0.0f,  DefaultH, 0f),   // (구 T0RS_12) 착지→게이트 사이 지면(화분·호스 회피)
        ("T0RS_12_게이트_앞",           57.0f, 102.0f, 0.0f,  DefaultH, 0f),   // (구 T0RS_13) P5 — 장독 열 북쪽·평상 동쪽, GOAL
    };

    [MenuItem("Tools/KitchenMapV3/8. Place T0RS Flags (T0 보조 리스폰 깃발)", false, 32)]
    public static void Place()
    {
        GameObject root = V3.Root();
        // 재실행 대비: 기존 그룹 삭제 후 재배치
        Transform old = root.transform.Find("V3_Checkpoints");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // [2026-09-10, 컨트롤타워 판정] 씬 전체를 훑어 이름="Checkpoint"인 오브젝트를 찾아 지우던
        // 이전 스윕은 여기 있었다 — 완전히 제거했다(근거는 클래스 주석 "[2026-09-10, 컨트롤타워
        // 판정 — 씬 전체 스캔형 '고아 Checkpoint 정리' 완전 폐기]" 참고). V3는 자기 루트 밖의
        // 오브젝트를 어떤 경우에도 지우지 않는다 — 대신 아래 루프의 인스턴스ID 비교 + try/catch가
        // "이번 호출이 방금 만든 그 하나"만 실패 시 되돌린다.

        GameObject group = new GameObject("V3_Checkpoints");
        group.transform.SetParent(root.transform, false);
        // T0 기간 기본값 = 표시 ON(활성). T1 이후에는 메뉴 "8b. Toggle T0RS Flags"로 끈다(🔒L3,
        // 검문 §E-1 L4 — 상시 표시는 불허, 토글 가능 + 기본 OFF 조건).

        // 컨트롤러(씬에 하나) — 정식 메뉴 경유. Clear()가 지우는 루트 밖에 있어 재빌드에도 유지된다.
        if (Object.FindObjectOfType<RespawnController>() == null)
            EditorApplication.ExecuteMenuItem("Tools/Respawn/Create Respawn Controller");

        RespawnController controller = Object.FindObjectOfType<RespawnController>();
        if (controller != null)
        {
            controller.dropExtraHeight = DropExtraHeight;   // [R5] — 저장소 코드는 무수정, 인스턴스 값만 설정
            EditorUtility.SetDirty(controller);              // [경미 반려] 인스펙터 값 변경을 씬에 확실히 반영
        }
        else
            V3.Warn("RespawnController를 찾지 못해 dropExtraHeight를 설정하지 못했다.");

        int made = 0;
        RespawnZone previousZone = null;               // [순차 활성화] 직전 깃발의 RespawnZone
        T0RS_SequentialGate previousGate = null;        // 직전 깃발의 게이트 — next를 여기에 연결
        foreach (var d in Defs)
        {
            // [R2, 2026-09-10 인스턴스ID 기준으로 강화] Selection에 의존하지 않는다 — 호출 전후
            // 씬의 RespawnZone 인스턴스ID 집합을 비교해 "방금 이 호출이 만든 것"을 찾는다
            // (참조를 담은 HashSet<RespawnZone> 대신 GetInstanceID() 정수 비교 — Unity Object의
            // 오버로드된 ==/Equals가 "파괴된 객체는 null과 같다"는 특수 규칙을 갖고 있어 의도가
            // 더 분명하다).
            var beforeIds = new HashSet<int>();
            foreach (RespawnZone z in Object.FindObjectsOfType<RespawnZone>())
                beforeIds.Add(z.GetInstanceID());

            if (!EditorApplication.ExecuteMenuItem("Tools/Respawn/Create Checkpoint Pole"))
            {
                V3.Warn($"{d.name}: 체크포인트 정식 생성기 메뉴 실행 실패 — RespawnSystem이 프로젝트에 있는지 확인. 중단.");
                return;
            }

            var newOnes = new List<RespawnZone>();
            foreach (RespawnZone z in Object.FindObjectsOfType<RespawnZone>())
                if (!beforeIds.Contains(z.GetInstanceID())) newOnes.Add(z);

            if (newOnes.Count != 1)
            {
                // [되돌림, 2026-09-10] 0개=조용한 실패(지울 것 없음), 2개 이상=이 한 번의 동기
                // ExecuteMenuItem 호출이 만든 것이 확실하므로(같은 메서드 호출 안이라 다른 주체가
                // 끼어들 수 없다) 전부 되돌린다 — 이건 "V3 루트 밖 스캔 삭제"가 아니라 방금 자기
                // 호출의 반환값을 정리하는 것이다(클래스 주석 "[2026-09-10, 컨트롤타워 판정]" 참고).
                Debug.LogError($"[V3_Checkpoints] {d.name}: 생성 검증 실패(신규 RespawnZone {newOnes.Count}개, " +
                                "기대 1개) — made 카운트를 거짓으로 올리지 않기 위해 중단하고, 방금 생성된 " +
                                "것으로 확인된 항목만 되돌린다.");
                // [정정 2026-09-10, 컨트롤타워 지시] "Tools/Respawn/Create Checkpoint Pole"이
                // Undo.RegisterCreatedObjectUndo로 등록한 오브젝트를 plain DestroyImmediate로
                // 지우면 Undo 스택에 파괴된 오브젝트 참조가 남는다 — 등록한 쪽과 짝이 맞는
                // Undo.DestroyObjectImmediate로 되돌린다(Undo 스택에서도 등록을 해제).
                foreach (RespawnZone z in newOnes)
                    if (z != null) Undo.DestroyObjectImmediate(z.gameObject);
                return;
            }

            RespawnZone created = newOnes[0];
            GameObject cp = created.gameObject;

            // [2026-09-10, 컨트롤타워 지시] 개명·재부모화·자식 _Gate 생성·컴포넌트 설정 전체를
            // try/catch로 감싼다 — 실패하면 방금 특정한 그 인스턴스(cp, 위에서 인스턴스ID로 특정한
            // 참조 그 자체 — 이름이나 부모로 다시 찾지 않는다)만 Undo.DestroyObjectImmediate하고
            // 중단한다.
            // [정정 2026-09-10, 컨트롤타워 지시 A3] 아래 try 안에서 previousGate.next/nextCollider를
            // 이번 반복의 created/box로 연결한 뒤(지역변수 previousGate는 그 직후 이번 반복의 gate로
            // 재대입된다) 남은 대입 3줄(previousZone·previousGate·made++) 중 하나가 예외를 내면,
            // "직전(=이전 반복까지 정상 배치된) 게이트"가 지금 파괴할 created/box를 가리킨 채 남는다
            // — linkedGate에 그 직전 게이트 참조를 따로 잡아 둬, catch에서 previousGate 지역변수가
            // 이미 재대입돼 있어도 무엇을 되돌려야 하는지 잃지 않는다.
            T0RS_SequentialGate linkedGate = null;
            try
            {
                cp.name = d.name;
                cp.transform.SetParent(group.transform, true);
                cp.transform.position = V3.Doc(d.x, d.y, d.z); // 구역 원점 = 바닥 (메뉴 규약)
                if (d.yRot != 0f)
                    cp.transform.rotation = Quaternion.Euler(0f, d.yRot, 0f); // [구 T0RS_11=신 T0RS_10] 깃대를 벽 반대쪽으로 — 볼륨은 대칭이라 판정 범위 불변

                // [R1] 볼륨 규격 재설정 — "지점 상면 ~ +H"로 제한(저장소 기본 18U 기둥 대신).
                BoxCollider box = cp.GetComponent<BoxCollider>();
                if (box != null)
                {
                    box.size = new Vector3(6f, d.h, 6f);
                    box.center = new Vector3(0f, d.h * 0.5f, 0f);
                }
                else
                {
                    V3.Warn($"{d.name}: BoxCollider를 찾지 못해 볼륨 규격을 재설정하지 못했다.");
                }

                // [2026-09-06 재설계] RespawnZone MonoBehaviour는 항상 true — 잠금은 더 이상
                // 스크립트 enabled로 표현하지 않는다(무효였음, 클래스 주석 "[2026-09-06 재설계]"
                // 참고). 실제 잠금 = 자기 BoxCollider.enabled. T0RS_01만 시작부터 언락(true),
                // 02~12는 잠금(false)으로 시작한다.
                created.enabled = true;
                if (box != null) box.enabled = (previousZone == null);

                // [R2, map-reviewer 23차 — 이 분기는 "예외"가 아니라 이미 승인된 명시적 완화
                // 처리라 아래 catch의 "실패 시 되돌림" 대상이 아니다] 부모 BoxCollider가 없으면
                // (구조 이상) 게이트 자식(_Gate) 생성 자체를 중단한다 — 이전에는 이 경우에도
                // AddComponent<BoxCollider>() 기본값(size 1×1×1, center 0)이 그대로 남아 의미
                // 없는 박스가 감사 모집단에 섞여 들어갈 위험이 있었다. Debug.LogError를 직접 써서
                // V3_Batch.cs의 errorCount(logMessageReceived가 LogType.Error를 집계)에 잡히게
                // 한다. previousGate는 갱신하지 않고 넘어가 — 다음 정상 항목이 그 이전(마지막으로
                // 유효했던) 게이트에 바로 이어진다. 항목 자체(이름 변경·그룹 소속)는 유지된다 —
                // 순차 잠금 사슬에서만 빠진다.
                if (box == null)
                {
                    Debug.LogError($"[V3_Checkpoints] {d.name}: 부모 BoxCollider가 없어 게이트 자식(_Gate) " +
                                    "생성을 중단했다 — 구조 이상(생성기 결과물에 BoxCollider 없음). 이 " +
                                    "체크포인트는 순차 잠금 사슬에서 빠진다(Place() 재실행 권장).");
                    previousZone = created;
                    made++;
                    continue;
                }

                // [순차 활성화] 게이트 전용 자식 트리거 — 부모 콜라이더가 꺼져도(잠금) 항상 살아
                // 있어야 이 게이트가 계속 이벤트를 받는다. 부모와 동일 center/size(부모 회전은
                // SetParent(false)+localRotation 기본값으로 그대로 상속), 콜라이더 외 컴포넌트 없음.
                GameObject gateGo = new GameObject(d.name + "_Gate");
                gateGo.transform.SetParent(cp.transform, false);
                BoxCollider gateBox = gateGo.AddComponent<BoxCollider>();
                gateBox.isTrigger = true;
                gateBox.center = box.center;
                gateBox.size = box.size;
                T0RS_SequentialGate gate = gateGo.AddComponent<T0RS_SequentialGate>();
                gate.zone = created;
                gate.zoneCollider = box;

                if (previousGate != null)
                {
                    previousGate.next = created;
                    previousGate.nextCollider = box;
                    linkedGate = previousGate; // [A3] 롤백 대상 — 아래 previousGate 재대입과 무관하게 유지된다.
                }
                previousZone = created;
                previousGate = gate;

                made++;
            }
            catch (System.Exception e)
            {
                // [2026-09-10, 컨트롤타워 지시] "방금 특정한 그 인스턴스만" — cp는 위에서 인스턴스ID
                // 비교로 특정한 참조 그 자체다. 이 시점엔 이미 개명·SetParent가 끝났을 수 있어
                // 이름이나 부모로 다시 찾으면 틀릴 위험이 있다 — 그래서 처음부터 들고 있던 참조를
                // 그대로 파괴 대상으로 쓴다(인스턴스ID 기준, 이름 재검색 아님).
                Debug.LogError($"[V3_Checkpoints] {d.name}: 배치 중 예외 발생 — 방금 생성된 인스턴스" +
                                $"(instanceID={cp.GetInstanceID()})만 되돌리고 중단한다. 예외: {e}");
                // [정정 2026-09-10, 컨트롤타워 지시 A3] cp(=created가 속한 GameObject, box도 그
                // 자식이다)를 파괴하기 전에 linkedGate의 연결부터 되돌린다 — 순서를 바꿔 cp를 먼저
                // 파괴하면 linkedGate.next/nextCollider가 이미 파괴된 오브젝트를 계속 가리킨 채
                // 남는다(8c 토글의 next 사슬 순회가 그 잔여 참조를 만난다).
                if (linkedGate != null)
                {
                    linkedGate.next = null;
                    linkedGate.nextCollider = null;
                    EditorUtility.SetDirty(linkedGate);
                }
                if (cp != null) Undo.DestroyObjectImmediate(cp);
                return;
            }
        }
        V3.Log($"T0RS 보조 리스폰 {made}/{Defs.Length}개 배치 완료 — 정식 CP0~CP8(🔒K3)이 아님. " +
               "미방문=깃발 없음, 밟으면 게양(활성=초록 1개뿐). 순차 활성화: T0RS_01만 시작부터 " +
               "언락(자기 BoxCollider ON), 02~12는 직전 깃발을 밟기 전까지 잠금(자기 " +
               "BoxCollider OFF — 체크포인트 등록 자체가 안 됨). 게이트 전용 자식(_Gate)의 " +
               "트리거는 잠금과 무관하게 항상 살아 있다.");
    }

    /// <summary>[L4] T0 보조 리스폰 그룹 전체 표시 토글 — 검문 §E-1 L4: "메뉴 토글로 끌 수 있어야
    /// 하고 T1 이후 기본 OFF". Place()가 만드는 기본 상태는 T0 기간이므로 ON — 이 메뉴로 끈다.
    /// 그룹 GameObject를 통째로 비활성화하므로 트리거·깃발 모두 함께 꺼진다(측정 끝나면 완전히
    /// 없는 것처럼 취급하는 것이 목적 — 부분적으로 트리거만 살려두면 🔒L3 취지와 다시 어긋난다).</summary>
    // [경미 반려 ①] 우선순위 33은 V3_Dress.cs "9. 마커 표시 토글"과 충돌(둘 다 파일을 확인한 값).
    // V3_Dress.cs가 30~36을 이미 다 쓰고 있어(6~12번 메뉴) 그 사이엔 빈 정수가 없다 — 37로 이동.
    [MenuItem("Tools/KitchenMapV3/8b. Toggle T0RS Flags (T0 보조 리스폰 표시)", false, 37)]
    public static void ToggleVisible()
    {
        GameObject root = GameObject.Find(V3.RootName);
        Transform t = root != null ? root.transform.Find("V3_Checkpoints") : null;
        if (t == null)
        {
            V3.Warn("V3_Checkpoints 그룹이 없다 — 먼저 Build All(또는 8. Place T0RS Flags)을 실행해라.");
            return;
        }
        bool next = !t.gameObject.activeSelf;
        t.gameObject.SetActive(next);
        V3.Log($"T0RS 보조 리스폰 표시 {(next ? "켜짐" : "꺼짐")} — T1 이후에는 기본 꺼짐 권장(🔒L3, 검문 §E-1 L4).");
    }

    /// <summary>[Q1 완화 — 컨트롤타워 결정 2026-09-03 #7, 2026-09-06 콜라이더 기준으로 갱신]
    /// 순차 잠금 실패 모드(볼륨 1곳을 못 밟으면 그 뒤 전부 영영 잠김) 완화용 T0 측정 토글. 기본은
    /// 순차 ON. 누르면 씬의 T0RS_SequentialGate 전부에 대해 각자의 <b>zoneCollider.enabled=true</b>로
    /// 강제 해제 + gate.bypass=true(상태 기록, 근거는 T0RS_SequentialGate.cs 클래스 주석 "[Q1
    /// 완화]" 참고) — 저장소 RespawnZone 자체는 건드리지 않는다(RespawnZone.enabled는 이제
    /// 항상 true, 이 토글도 그 값을 안 만진다 — 잠금 표현이 콜라이더로 옮겨간 뒤 이 메서드도
    /// 그 대상을 따라갔다). 다시 누르면 next 사슬을 따라가며 첫 깃발(사슬 시작 — 아무 게이트의
    /// next로도 참조되지 않는 것)만 남기고 재잠금해 Place() 직후와 같은 초기 상태로 복원한다.
    /// 게이트는 이제 부모(RespawnZone) GameObject가 아니라 자식(`T0RS_nn_Gate`)에 있으므로
    /// next 사슬을 따라갈 때 GetComponent 대신 GetComponentInChildren로 찾는다.</summary>
    [MenuItem("Tools/KitchenMapV3/8c. Toggle T0RS Sequential Lock (순차 잠금 해제/복원)", false, 38)]
    public static void ToggleSequentialLock()
    {
        GameObject root = GameObject.Find(V3.RootName);
        Transform t = root != null ? root.transform.Find("V3_Checkpoints") : null;
        if (t == null)
        {
            V3.Warn("V3_Checkpoints 그룹이 없다 — 먼저 Build All(또는 8. Place T0RS Flags)을 실행해라.");
            return;
        }

        T0RS_SequentialGate[] gates = t.GetComponentsInChildren<T0RS_SequentialGate>(true);
        if (gates.Length == 0)
        {
            V3.Warn("T0RS_SequentialGate를 찾지 못했다 — Place()가 정상 실행됐는지 확인해라.");
            return;
        }

        // 전부 같은 상태로만 토글하므로(아래 두 분기가 항상 전체를 함께 바꾼다) 대표 1개로 현재
        // 상태를 판정한다.
        bool goingToBypass = !gates[0].bypass;

        if (goingToBypass)
        {
            foreach (T0RS_SequentialGate gate in gates)
            {
                if (gate.zoneCollider != null) gate.zoneCollider.enabled = true;   // [2026-09-06] 잠금 = 자기 BoxCollider
                gate.bypass = true;
                EditorUtility.SetDirty(gate);
                // [R6, map-reviewer 21차] enabled를 바꾼 것은 gate가 아니라 gate.zoneCollider
                // (부모 GameObject의 별도 컴포넌트)다 — gate만 SetDirty하면 zoneCollider 쪽 변경이
                // 씬 저장에 반영 안 될 위험이 있어 병행한다.
                if (gate.zoneCollider != null) EditorUtility.SetDirty(gate.zoneCollider);
            }
            V3.Log($"T0RS 순차 잠금 해제 — {gates.Length}개 깃발 전부 활성(T0 측정용). " +
                   "다시 이 메뉴를 누르면 Place() 초기 상태(첫 깃발만 활성)로 복원한다.");
        }
        else
        {
            // 복원: next 사슬을 따라간다(배열 순서가 Defs 순서와 같다는 보장이 없어 순회로 시작점을 찾는다).
            // [2026-09-06] 게이트는 이제 RespawnZone과 같은 GameObject가 아니라 그 자식(`_Gate`)에
            // 있으므로 next(부모 RespawnZone)의 자식에서 게이트를 찾는다.
            var referencedByOthers = new HashSet<T0RS_SequentialGate>();
            foreach (T0RS_SequentialGate gate in gates)
                if (gate.next != null)
                {
                    T0RS_SequentialGate nextGate = gate.next.GetComponentInChildren<T0RS_SequentialGate>(true);
                    if (nextGate != null) referencedByOthers.Add(nextGate);
                }
            T0RS_SequentialGate chainStart = null;
            foreach (T0RS_SequentialGate gate in gates)
                if (!referencedByOthers.Contains(gate)) { chainStart = gate; break; }
            if (chainStart == null) chainStart = gates[0]; // 이론상 도달 불가 — 사슬은 항상 시작점이 1개

            var visited = new HashSet<T0RS_SequentialGate>();
            T0RS_SequentialGate cur = chainStart;
            bool isFirst = true;
            while (cur != null && visited.Add(cur))
            {
                if (cur.zoneCollider != null) cur.zoneCollider.enabled = isFirst;   // [2026-09-06] 잠금 = 자기 BoxCollider
                cur.bypass = false;
                EditorUtility.SetDirty(cur);
                // [R6, map-reviewer 21차] 위와 동일 이유 — cur.zoneCollider의 enabled 변경도 병행 SetDirty.
                if (cur.zoneCollider != null) EditorUtility.SetDirty(cur.zoneCollider);
                isFirst = false;
                cur = cur.next != null ? cur.next.GetComponentInChildren<T0RS_SequentialGate>(true) : null;
            }
            V3.Log("T0RS 순차 잠금 복원 — 첫 깃발만 활성, 나머지는 다시 순서대로 밟아야 풀린다.");
        }
    }
}
#endif
