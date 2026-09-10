#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 물리 배치 실험 — "세모가 서랍 계단(Box_Step 0.96단) 앞에 붙으면 점프가 안 되고 뒤로 빼야
/// 풀린다"는 사용자 플레이 보고의 원인을 배치모드 물리로 확정하고, 수정안 ①(V3_WallSlip 래퍼)의
/// 효과를 같은 실험으로 측정한다(2026-09-06, 검문25차 조건부 채택분 2026-09-09 반영).
///
/// [저장소·기존 코드 무수정] 이 스크립트는 신규 실험 공간(문서 y&lt;-50)만 만들고 끝에 전부
/// 파괴한다. 기존 블록아웃(V3Menu.BuildAll 결과)·저장소 사본(PlayerSystem 등)은 읽기만 한다.
///
/// [Edit Mode 재현 한계] PlayerMover/PlayerJump/PlayerShapeController/PlayerGroundContact는
/// 전부 일반 MonoBehaviour(ExecuteInEditMode 없음)라 Edit Mode에서 Update/FixedUpdate/
/// OnCollisionStay가 전혀 호출되지 않는다(에디터가 스크립트 실행 루프를 안 돌림 — Play Mode
/// 전용). 그래서 이 스크립트는 그 컴포넌트들의 로직을 파일:줄 단위로 읽어 아래 상수·메서드로
/// "그대로" 재현한 뒤, Physics.simulationMode = SimulationMode.Script + Physics.Simulate(dt)로
/// 직접 물리를 구동한다. 재현한 원본 줄은 각 상수·메서드 주석에 명시했다.
/// PlayerShapeIdentity.Start()(이속/점프높이/질량/마찰 적용)도 같은 이유로 Edit Mode에서
/// 자동 실행되지 않으므로 ApplyIdentityStats()에서 동일 로직을 수동으로 1회 적용한다.
///
/// [2026-09-09, 검문25차 조건부 채택분] (1) V3_WallSlip.Initialize()를 AddComponent 직후
/// 명시 호출(래퍼 무동작 의심 해소 시도, (라) 항목) + CaseResult에 swapCount·firstSwapSimTime
/// 계측 추가. (2) ProbeWallContact 사거리를 ownColliders 실측 bounds 기반으로 도형별 자동
/// 계산(하드코딩 0.60 제거, (다) 항목 — 세모 실측 최대 수평 반경 0.694 대응). (3) 통과 판정을
/// "마지막 프레임 4조건"으로 재정의 + z 측정 기준을 콜라이더 하단(발밑)으로 통일(A1~A3 항목).
/// [정정 27차 R1 결정] 어느 프레임이든 4조건 동시 만족 1회 이상 = 통과(landedSimTime).
/// (4) FixedDt 0.02(50Hz, ProjectSettings/TimeManager 실측)·TotalSteps 150(동일 3초, A4 항목).
/// (5) Setup Play 오브젝트를 인스턴스ID 기준으로만 정리하고, 실행 전 동명 오브젝트가 있으면
/// 삭제 대신 중단(A5 항목).
/// [2026-09-09, 컨트롤타워 31차 판정 반영] 래퍼 ON 열의 ✗가 V3_WallSlip이 아니라 하네스 대체
/// 벽감지(콜라이더 중심 1점)가 상승 중 벽을 놓쳐 마찰을 복원시킨 결과였다는 지적(예측식 8/8
/// 일치, map-reviewer.md §1 #31)에 대응해 (1) ProbeWallContact를 solid 콜라이더 bounds의 세
/// 높이(발밑/중심/상단) 8방향으로 확장, (2) CaseResult에 wallLostWhileRising·
/// restoreSwapWhileRising 계측 추가, (3) 리포트 "60케이스" 산술 오류 정정(OFF 3모드+ON
/// 2모드=5조합, ON-(c) 미측정 고지) + 한계 고지 문단 신설. 래퍼 성패 자체는 이 보완판 재측정
/// 전까지 미판정.
/// [2026-09-09, 컨트롤타워 결정 — 검문32차 D1 확정 반영] 3회차 실측(물리실험_2026-09-09_1805.md)의
/// D1 기전(벽 상실 후에도 코요테 유예 0.1초 동안 접지가 true라 V3_WallSlip.Tick이 마찰을 원본으로
/// 복원, 그 마찰이 남은 상승을 흡수) 가능성에 대응해 V3_WallSlip.cs Tick()의 복원 조건에 "상승
/// 중이 아닐 것"을 추가했다(이 파일은 무수정, Scripts/V3_WallSlip.cs만 변경). [정정 2026-09-09
/// #37, map-reviewer 33차 R1] 위 결정의 근거로 인용한 "복원스왑(상승중)=1인 케이스는 전부 ✗ →
/// 실측으로 확정"은 역방향 오류였다 — 1805.json 전수 재현: ✗ 케이스는 전부 rsr≥1이지만, rsr≥1
/// 20건 중 7건은 통과(네모 ON 통과 전부)라 rsr≥1은 핀닝의 필요조건일 수 있어도 충분조건은 아니다.
/// 실제 핀닝 3중 시그니처(maxZ 단 상단 근처 정지 + 최종x 벽면 고정 + rsr≥1)는 [정정 2026-09-09,
/// 검문34차 정정 — 33차 자신의 목록이 누락이었다] 5건이 아니라 6건이다 — 세모
/// 0.8 ON a(0.802/2.33)·세모 0.8 ON b(0.801/2.33)·세모 0.96 ON a(1.056/2.33)·세모 1.0 ON a
/// (1.050/2.33)·네모 0.5 ON a(0.500/2.70)·세모 0.5 ON b(0.461/2.35, 33차 목록에서 빠져 있었다).
/// 이 회차의 변경: (1) CaseResult.firstRestoreBottomZ
/// 신설(최초 복원 시점의 발밑 문서 z, 없으면 -1f — restoreOccurred=false로 판별
/// [정정 #37, 검문33차 A2. 센티널은 [정정 2026-09-09, map-reviewer 34차 R3] float.NaN이
/// JsonUtility 직렬화 시 JSON 스펙을 위반하는 bare NaN 토큰이 됨이 34차 실측으로 확인돼 다시
/// -1f로 되돌렸다]) [정정 2026-09-09, map-reviewer 35차 A2] "상세표 열 1개"는 이 문단이 기술한
/// 시점(#36) 기준값이었다 — 이후 #38(검문34차 R2)이 restoreTotal·firstRestoreSimTime 2열을 더해
/// 실제 신설 열은 3개(복원총횟수·최초복원시각·최초복원z)다. (2) 검문32차 A2~A6 정정
/// (RunAll의 RunCase 5회 호출부, RunCase 지상 분기의 rb.velocity 대입(원본 PlayerMover.LegacyVelocityFixedUpdate()),
/// 리포트 제목 "검문25차"→"검문32차", OFF 열 like-for-like 비교 불가 고지, 세모 프로브 원점
/// +0.074 오프셋으로 (b) 시작 시 이미 벽 접촉 고지).
/// [2026-09-09 #40, GPT검토패키지_2026-09-09 지적1·지적2 반영] (1) 지상 이동에서 도형 회전
/// 연출용으로만 여겨졌던 rb.angularVelocity 하드 대입(PlayerMover.LegacyVelocityFixedUpdate()의 rollRadius 기반 각속도 대입)이
/// 실제로는 PhysX 접촉 솔버·마찰 결합에 영향하는 물리 대입임을 재확인해 재현에 넣었다(지적1) —
/// "각속도는 시각 전용이라 재현 생략" 이전 주석이 틀렸다. (2) flewOver를 "최고z≥h"만 보던 정의에서
/// "최고z≥h + 루프 중 어느 프레임이든 x가 상판 끝(6.0)을 넘은 적 있음(crossedTopEdgeX)"으로
/// 재정의하고, 통과도 비행통과도 아니면서 최종x가 계단 앞면(3.0) 미만으로 남은 경우를
/// stalledAtWall로 새로 분리했다(지적2) — 재정의 이후 재측정 전까지, 이전 판(1805·2057) 기준
/// "비행통과 20건" 집계는 구 정의값임을 리포트 본문에도 명시한다(BuildMarkdownReport 참고).
/// </summary>
public static class V3PhysLab
{
    // ── 물리 스텝 ────────────────────────────────────────────────
    // [정정 2026-09-09, 검문25차 A4] ProjectSettings/TimeManager 실측 Fixed Timestep = 0.02
    // (50Hz) — 이전 1/60f(60Hz)는 프로젝트 실제 설정과 불일치했다. 150스텝×0.02 = 3.0초로
    // 이전 180스텝×(1/60) = 3.0초와 지속시간은 동일하다.
    private const float FixedDt = 0.02f;
    private const int TotalSteps = 150; // 3초(지시 3.항) @ 50Hz
    // [신규 2026-09-09, map-reviewer 35차 A3] 리포트 제목의 검문 회차 표기를 상수 1곳으로 뺀다 —
    // 이전엔 BuildMarkdownReport 안 리터럴 문자열("검문32차 반영")을 갱신하는 것을 매 회차 잊어
    // 32차 이후(33·34차 반영분)에도 계속 "검문32차"로 인쇄되는 일이 반복됐다(34차 A3 지적).
    // 앞으로는 이 상수만 바꾸면 리포트 제목이 따라간다.
    private const string ReviewRound = "36차";

    // ── 실험 공간 배치 (지시 1.항 — 문서 y<-50, 단마다 독립 레인) ──
    // 진행축 = 문서 X(=Unity X). PlayerMover.inputYawOffset 필드 기본값 90°가
    // Vertical 입력(0,0,1)을 Quaternion.AngleAxis(90,Up)로 (1,0,0)=Unity+X로 돌리기 때문에
    // "전진 유지" 입력이 실제로 미는 방향은 문서 X다 — 그래서 레인의 진행 방향도 문서 X로
    // 잡았다(폭 6.0은 문서 Y, 안전지대 y<-50은 레인 중심의 문서 Y로 만족).
    private const float LaneWidth = 6.0f;          // 폭 6.0(지시)
    private const float LandingDepth = 3.0f;       // 착지 평면 깊이 3.0(지시)
    private const float StepTopDepth = 3.0f;       // 계단 상판 깊이 3.0(지시)
    private const float LandingStartDocX = 0.3f;   // 스폰 X(착지 평면 안, 벽까지 2.7U 조주거리)
    private const float StepFrontDocX = LandingDepth; // 계단 앞면(리저) X = 3.0
    private static readonly float[] StepHeights = { 0.5f, 0.8f, 0.96f, 1.0f };

    // ── PlayerGroundContact.cs 재현 (아래 SphereCast 접지 판정) ────
    private const float GroundCheckRadius = 0.25f;     // PlayerGroundContact.groundCheckRadius 필드
    private const float GroundCheckDistance = 0.6f;    // PlayerGroundContact.groundCheckDistance 필드
    private const float GroundNormalThreshold = 0.5f;  // PlayerGroundContact.groundNormalThreshold 필드
    private const float GroundedGraceTime = 0.1f;      // PlayerGroundContact.groundedGraceTime 필드 (코요테 유예)
    // PlayerShapeController.FixedUpdate()가 groundContact!=null이면 그 IsGrounded를
    // 그대로 씀 — 정식 생성기가 만든 플레이어는 항상 groundContact가 배정되므로(PlayerObjectMenuItem.
    // CreatePlayer()) 이 경로가 실제 게임에서 쓰이는 유일한 경로다. PlayerMover.IsGrounded()·
    // PlayerJump.IsGrounded()의 "자체 Raycast" 폴백은 shapeController==null일 때만 타는 죽은
    // 경로라 재현하지 않았다.

    // ── PlayerMover.cs 재현 (LegacyVelocityFixedUpdate(), useTorqueRolling은 생성기가 항상
    // false로 두므로 — PlayerObjectMenuItem.CreatePlayer() 주석 — 이 경로만 존재한다) ──
    private const float InputYawOffsetDeg = 90f;       // PlayerMover.inputYawOffset 필드
    private const float AirControlMultiplier = 0.6f;   // PlayerMover.airControlMultiplier 필드
    private const float AirWallCheckDistance = 0.7f;   // PlayerMover.airWallCheckDistance 필드
    private const float WallNormalYThreshold = 0.5f;   // PlayerMover.DeflectAirMoveFromWall() 내부 임계

    // ── 프로브 사거리(2026-09-09, 검문25차 (다) — 도형별 자동 계산) ──
    // 이전엔 GroundCheckRadius+0.35=0.60U 고정이었다. 검문25차는 정사면체 solid 콜라이더 중간
    // 링(y=-0.1155)의 손계산 반경 0.694U를 근거로 들었으나, [정정 2026-09-09, 검문27차 R3]
    // ComputeProbeDistance는 ownColliders 전체(트리거 포함 — Player_Mesh도 대상)를 순회하므로
    // 실제로 쓰이는 값은 solid 콜라이더 AABB extents(실측 0.5964)가 아니라 그보다 큰 트리거
    // (Player_Mesh) AABB extents(실측 0.6830)다 — maxExtent=0.6830, +ProbeMargin 0.10=0.783이
    // 실제 반환값이다. 09-06 리포트 실측(정지 x=2.3296, 벽 x=3.0)에서 역산한 실제 필요 사거리
    // 0.6704(=3.0−2.3296)보다 0.783이 커 충분하다. 구·네모는 solid extents 0.5+0.10=0.60로
    // 이전과 동일(트리거가 solid보다 크지 않은 도형). 아래 여유값만 상수로 남기고, 실제
    // 사거리는 ownColliders의 world bounds extents 최대값으로 매 호출 계산한다
    // (ComputeProbeDistance) — 도형이 바뀌어도 하드코딩을 다시 맞출 필요가 없다.
    private const float ProbeMargin = 0.10f; // bounds 최대 수평 반경 + 여유

    // ── 시나리오 타이밍(초 단위 — 프레임 수 아님, 검문25차 지시 유지) ──
    private const float JumpDelayAfterWallContact = 0.5f; // (a)(c): 벽 접촉 후 0.5초 뒤 점프(지시)
    private const float SettleTimeB = 0.2f;                // (b): 정지 정착 대기 후 점프

    // ── 통과 판정 임계(2026-09-09, 검문25차 A1~A3) ──
    private const float PassZTolerance = 0.05f;  // 콜라이더 하단 z가 단높이 h와 이 이내면 "안착"
    private const float PassVyTolerance = 0.1f;  // |수직속도| 이 미만이면 "정지"

    private static readonly (string menu, string goName)[] Shapes =
    {
        ("Sphere", "Player_Sphere"),
        ("Cube", "Player_Cube"),
        ("Tetrahedron", "Player_Tetrahedron"),
    };

    [Serializable]
    private class CaseResult
    {
        public string shape;
        public float stepHeight;
        public bool wrapperOn;
        public string inputMode; // "a" | "b" | "c"
        public bool passed;
        // [정정 2026-09-09, GPT검토패키지_2026-09-09 지적2·컨트롤타워 재현됨] 이전 정의
        // "!passed && maxDocZ>=stepHeight"는 높이만 보고 장애물을 실제로 넘어 반대편(상판 위)으로
        // 갔는지는 확인하지 않았다 — 예컨대 벽 앞에서 튀어올랐다가 그대로 떨어져도 maxDocZ가 h를
        // 넘으면 "비행통과"로 오분류될 수 있었다. 이제는 루프 중 어느 프레임이든 x가 상판 끝
        // (StepFrontDocX+StepTopDepth=6.0)을 넘은 적이 있어야만 flewOver로 인정한다(아래
        // crossedTopEdgeX). "높이는 넘었으나 x 미달로 벽 앞에 남음" 상태는 flewOver가 아니라
        // stalledAtWall로 별도 분류한다(아래).
        public bool flewOver;    // [신규 A3] 최고z는 h 이상까지 올랐고 상판 끝(x=6.0)도 넘었지만 어떤 프레임에서도 4조건 미충족
        // [신규 2026-09-09, GPT검토패키지_2026-09-09 지적2] "높이는 h 이상 넘었으나 x가 3.0(계단
        // 앞면) 미만으로 남아 벽 앞에 그대로 핀닝된" 상태를 flewOver와 구분해 명시한다 —
        // !passed && !flewOver && 최종x < StepFrontDocX(3.0). passed나 flewOver인 케이스는 정의상
        // false(택일).
        public bool stalledAtWall;
        public float maxDocZ;    // [정정 A3] 콜라이더 하단(발밑) 기준 최고값(루트 위치 아님)
        public int wallContactFrames;
        public int groundedFrames;
        public bool jumpTriggered;
        public bool jumpAtGroundedTrue;
        // [정정 2026-09-09, map-reviewer 34차 A2] 이름과 달리 "종료 후 1회 계산값"이 아니다 — RunCase의
        // 시뮬레이션 스텝 루프 안에서 매 프레임 `root.transform.position.x`로 갱신되는 "현재 x"이고,
        // 루프가 끝난 시점의 마지막 대입값이 곧 "최종"이 되는 구조다(필드명은 유지, 의미만 명시).
        public float finalDocX;
        public int swapCount;          // [신규 (라)] V3_WallSlip.IsFrictionlessNow가 실제로 바뀐 횟수
        public float firstSwapSimTime; // [신규 (라)] 최초 스왑 시각(초) — 없으면 -1
        public int trackedColliders;   // [신규 검문27차 R4] 래퍼 초기화 시점 V3_WallSlip.TrackedCount(케이스당 1회 기록). wrapperOn=false면 -1(해당 없음)
        // [신규 검문27차 R1(컨트롤타워 결정)] 시뮬 중 (i)~(iv) 4조건을 동시에 만족한 최초 프레임의
        // simTime(초). 끝까지 못 채우면 -1. passed = landedSimTime>=0f(아래 RunCase 참고).
        public float landedSimTime;
        // [신규 2026-09-09, 컨트롤타워 31차 판정] rb.velocity.y>0(상승 중)인 프레임에서 벽 접촉이
        // true→false로 바뀐 횟수. 31차가 지목한 "구 하네스(중심 1점)가 상승 중 벽을 놓쳐 래퍼가
        // 마찰을 복원시킨다"는 기전이 이번 3높이 보완판에서도 재현되는지 보는 계측이다.
        public int wallLostWhileRising;
        // [정정 2026-09-09, map-reviewer 34차 R2] 검문33차 A1(V3_PhysLab.cs risingNow와
        // V3_WallSlip.cs Tick()의 risingFast 임계 통일, 둘 다 RisingVyThreshold=0.05)이 반영된
        // 뒤로 이 열은 구조적으로 항상 0이다 — risingNow와 risingFast가 같은 프레임·같은
        // Rigidbody·같은 임계를 읽어(측정 지점과 Tick 호출 사이에 velocity 대입이 없다)
        // risingNow=true인 프레임은 항상 wantFrictionless=true가 되므로, 이 필드가 세려는
        // "상승 중 무마찰→원본" 전이 자체가 도달 불가 분기가 된다(34차 코드 재현). D1 잔존을
        // 탐지하는 역할은 아래 restoreTotal로 옮겼다 — 이 필드는 회귀 감시용(임계가 다시
        // 어긋나면 0이 아닌 값이 나온다)으로만 유지한다.
        public int restoreSwapWhileRising;
        // [신규 2026-09-09, map-reviewer 34차 R2] 위 restoreSwapWhileRising이 임계 통일로
        // tautology(구조적 항상 0)가 되어 D1 잔존 복원을 더는 탐지하지 못하게 된 것의 대체 —
        // risingNow 게이트 밖에서 "무마찰→원본" 복원 스왑을 상승 여부와 무관하게 전수 센다.
        public int restoreTotal;
        // [신규 2026-09-09, map-reviewer 34차 R2] restoreTotal이 최초로 1이 된(=최초 원본 복원)
        // simTime(초). 한 번도 복원이 없으면 -1(판별은 restoreOccurred 또는 restoreTotal>0).
        public float firstRestoreSimTime;
        // [신규 2026-09-09, 컨트롤타워 결정 — 검문32차 D1 제안] 최초 복원 시점의 콜라이더 하단
        // (발밑) 문서 z. [정정 2026-09-09, map-reviewer 34차 R2] 이전엔 risingNow 게이트 안에서만
        // 기록했는데, 그 게이트가 위 restoreSwapWhileRising과 똑같이 도달 불가라 사실상 한 번도
        // 안 채워졌다 — 이번 회차부터 restoreTotal·firstRestoreSimTime과 같은 조건(risingNow와
        // 무관한 "첫 원본 복원")으로 기록한다. [정정 2026-09-09 #37, map-reviewer 33차 A2]
        // "복원스왑(상승중)=1인 케이스는 전부 ✗"라던 근거 자체가 역방향 오류였음이 33차에서
        // 드러났다(클래스 상단 주석 참고, 실제 핀닝 시그니처는 [정정 2026-09-09, 검문34차 정정]
        // 5건이 아니라 6건). [정정 2026-09-09, map-reviewer 34차 R3] 센티널은 float.NaN이 아니라
        // -1f로 되돌린다 — JsonUtility가 float.NaN을 bare NaN 토큰으로 직렬화해 JSON 스펙(엄격
        // 파서: JS JSON.parse·System.Text.Json·jq 등)을 위반함이 34차 실측으로 확인됐다(md 출력
        // "-"는 restoreOccurred 기준이라 이 되돌림과 무관하게 그대로 정합). 판별은 여전히
        // restoreOccurred(또는 restoreTotal>0)이지 이 값의 부호가 아니다.
        public float firstRestoreBottomZ;
        // [신규 2026-09-09 #37, map-reviewer 33차 A2] firstRestoreBottomZ·firstRestoreSimTime이
        // 실제로 기록됐는지의 판별 기준(=restoreTotal>0과 동치) — 출력·가드 모두 이 플래그로
        // 판정한다.
        public bool restoreOccurred;

        public CaseResult()
        {
            // [정정 2026-09-09, map-reviewer 35차 A5] float.NegativeInfinity 센티널을 폐기 —
            // JsonUtility가 이를 bare "-Infinity" 토큰으로 직렬화하면 34차 R3이 지적한 NaN과
            // 같은 계열의 JSON 스펙 위반이 된다(150스텝 루프가 매 실행 이를 실측값으로 덮어써
            // 현재는 도달 불가지만, 물리가 NaN 위치를 내면 `NaN > -Infinity`가 false라 그대로
            // 직렬화될 여지가 남는다 — 추측, 그 경로는 미관측). -999f는 이 실험의 실측 발밑
            // z 범위(대략 0~3U)보다 한참 아래인 유한값 센티널이라 JSON 비스펙 토큰이 될 가능성이
            // 없다. "가장 낮은 값"이 아니라 "관측 없음"을 뜻하는 것은 동일.
            maxDocZ = -999f;
            firstSwapSimTime = -1f;
            trackedColliders = -1;
            landedSimTime = -1f;
            firstRestoreSimTime = -1f; // [신규 map-reviewer 34차 R2]
            firstRestoreBottomZ = -1f; // [정정 map-reviewer 34차 R3] float.NaN → -1f(NaN은 JSON 스펙 위반, 판별은 restoreOccurred로)
            restoreOccurred = false;
        }
    }

    [Serializable]
    private class ResultSet
    {
        public List<CaseResult> cases = new List<CaseResult>();
    }

    [MenuItem("Tools/KitchenMapV3/물리실험 (V3PhysLab)", false, 50)]
    public static void RunAll()
    {
        SimulationMode prevMode = Physics.simulationMode;
        bool switcherPreexisted = UnityEngine.Object.FindObjectOfType<PlayerControlSwitcher>() != null;
        // [신규 2026-09-09, 검문27차 A5] 정식 생성기(PlayerObjectMenuItem.EnsureFollowCamera())는 씬에
        // PlayerFollowCamera가 하나도 없을 때만 Main Camera에 그것을 새로 붙인다 — 이 실험이
        // 처음 스폰할 때 그 부착이 일어났다면(=실행 전 부재), 실행 끝에 우리가 걷어내야
        // 사용자의 기존 카메라 설정을 오염시키지 않는다(PlayerControlSwitcher와 동일 패턴).
        bool followCameraPreexisted = UnityEngine.Object.FindObjectOfType<PlayerFollowCamera>() != null;
        GameObject expRoot = null;
        StringBuilder consoleLog = new StringBuilder();
        Application.LogCallback handler = (condition, stacktrace, type) =>
        {
            consoleLog.AppendLine($"[{type}] {condition}");
        };
        Application.logMessageReceived += handler;

        var results = new ResultSet();
        int exitCode = 0;

        // [정정 2026-09-09, 검문25차 A5] 이번 실행이 만든 오브젝트/재질만 정리 대상으로 추적한다
        // (이름 기준 GameObject.Find 삭제는 하지 않는다 — 사용자의 실제 Setup Play 결과물을
        // 실수로 지울 수 있어서다). RunCase가 생성 즉시 이 리스트들에 등록한다.
        var createdInstanceIds = new List<int>();
        var createdMaterials = new List<PhysicMaterial>();
        // [신규 2026-09-09, 검문27차 A3] RunCase 시작부의 잔존 오브젝트 정리를 이름 기반
        // GameObject.Find 대신 인스턴스ID 기준으로 통일한다(A5와 같은 이유 — 사용자의 동명
        // 실제 오브젝트를 잘못 지우지 않기 위함). 도형별로 "직전에 이 케이스 루프가 만든"
        // 인스턴스ID만 기억해 그것만 대상으로 삼는다.
        var lastInstanceIdByName = new Dictionary<string, int>();

        try
        {
            // [정정 2026-09-09, A5] 실행 전 동명 오브젝트가 이미 있으면 삭제하지 않고 중단한다.
            // 이전엔 CleanupStaleObjects()가 이름으로 찾아 조용히 지우고 진행했다 — 사용자가
            // 이미 만들어 둔 Setup Play 결과물(같은 이름)을 실험이 삼킬 위험이 있었다.
            if (HasPreexistingObjects())
            {
                // [정정 2026-09-09, 검문27차 R2] "1 Clear 후 실행"은 잘못된 안내였다 — V3Menu.Clear()는
                // 블록아웃(V3_Build 산출물)만 지우고 이 실험의 Player_* 오브젝트는 건드리지 않는다.
                Debug.LogError("[V3PhysLab] Setup Play 오브젝트 존재 — 씬 루트의 Player_Sphere/Player_Cube/" +
                    "Player_Tetrahedron을 직접 삭제한 뒤 실행(1 Clear로는 안 지워짐)");
                exitCode = 1;
            }
            else
            {
                expRoot = BuildExperimentSpace();
                createdInstanceIds.Add(expRoot.GetInstanceID());

                Physics.simulationMode = SimulationMode.Script;

                foreach (var shape in Shapes)
                {
                    for (int hi = 0; hi < StepHeights.Length; hi++)
                    {
                        float stepHeight = StepHeights[hi];
                        float laneDocY = LaneDocY(hi);

                        // 래퍼 OFF: (a)(b)(c) 3종
                        results.cases.Add(RunCase(shape.menu, shape.goName, stepHeight, laneDocY, false, "a", createdInstanceIds, createdMaterials, lastInstanceIdByName));
                        results.cases.Add(RunCase(shape.menu, shape.goName, stepHeight, laneDocY, false, "b", createdInstanceIds, createdMaterials, lastInstanceIdByName));
                        results.cases.Add(RunCase(shape.menu, shape.goName, stepHeight, laneDocY, false, "c", createdInstanceIds, createdMaterials, lastInstanceIdByName));
                        // 래퍼 ON: (a)(b) 2종
                        results.cases.Add(RunCase(shape.menu, shape.goName, stepHeight, laneDocY, true, "a", createdInstanceIds, createdMaterials, lastInstanceIdByName));
                        results.cases.Add(RunCase(shape.menu, shape.goName, stepHeight, laneDocY, true, "b", createdInstanceIds, createdMaterials, lastInstanceIdByName));
                    }
                }

                WriteReports(results, consoleLog.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[V3PhysLab] 실행 중 예외 — 삼키지 않고 exit 1: " + e);
            exitCode = 1;
        }
        finally
        {
            Application.logMessageReceived -= handler;

            // [정정 2026-09-09, A5] 인스턴스ID 기준 정리 — 이름으로 GameObject.Find하지 않는다.
            // 정상 경로에서는 RunCase가 이미 각 플레이어 root를 스스로 파괴했으므로(아래),
            // InstanceIDToObject는 대부분 null을 돌려주고 조용히 스킵된다. 예외로 중간에 끊긴
            // 케이스가 남긴 인스턴스만 여기서 실제로 정리된다.
            foreach (int id in createdInstanceIds)
            {
                var obj = EditorUtility.InstanceIDToObject(id) as GameObject;
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            }

            // 케이스마다 만든 PhysicMaterial(도형별 마찰 재질·(c) 상시무마찰 재질·래퍼 무마찰
            // 재질) 정리 — PhysicMaterial은 GameObject의 자식 에셋이 아니라 컬라이더가 참조만
            // 하는 별도 오브젝트라, 소유 GameObject를 파괴해도 자동으로 없어지지 않는다.
            foreach (var mat in createdMaterials)
            {
                if (mat != null) UnityEngine.Object.DestroyImmediate(mat);
            }

            // 정식 생성기가 최초 스폰 시 부가 생성한 PlayerControlSwitcher는, 실행 전에 없었을
            // 때만 우리가 만든 것이므로 그때만 걷어낸다(사전에 있던 것은 건드리지 않는다).
            if (!switcherPreexisted)
            {
                var switcher = UnityEngine.Object.FindObjectOfType<PlayerControlSwitcher>();
                if (switcher != null) UnityEngine.Object.DestroyImmediate(switcher.gameObject);
            }

            // [신규 2026-09-09, 검문27차 A5] PlayerFollowCamera는 PlayerControlSwitcher와 달리
            // 새 GameObject가 아니라 기존 Main Camera에 붙는 컴포넌트라 — 회수 시 카메라
            // GameObject 자체가 아니라 컴포넌트 인스턴스만 파괴한다(카메라는 우리가 만든 게
            // 아니므로 절대 지우지 않는다).
            if (!followCameraPreexisted)
            {
                var followCam = UnityEngine.Object.FindObjectOfType<PlayerFollowCamera>();
                if (followCam != null) UnityEngine.Object.DestroyImmediate(followCam);
            }

            Physics.simulationMode = prevMode;
        }

        // [2026-09-09 게이트 추가] 배치모드(무인검증 -executeMethod 경로)에서만 에디터를 끈다.
        // 종전엔 대화형으로 메뉴를 직접 눌러도 무조건 EditorApplication.Exit이 불려 에디터가
        // 꺼졌다(2026-09-09 12:15 실측 — 사용자가 "Tools/KitchenMapV3/물리실험 (V3PhysLab)"을
        // 메뉴로 실행 → 60케이스 완주 → 리포트 3종 저장까지 정상 진행된 뒤 레이아웃 저장·
        // quitting 콜백까지 깔끔하게 타며 종료됨 — 크래시가 아니라 배치모드 전용 설계가 메뉴
        // 경로에 그대로 노출된 것). 예외로 exitCode=1이 된 경로도 동일하게 이 분기 하나로 처리된다.
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(exitCode);
        }
        else
        {
            Debug.Log($"[V3PhysLab] 대화형 실행 — 에디터를 끄지 않습니다. exitCode={exitCode} · 리포트 폴더: {ResolveOutDir()}");
        }
    }

    // ── 레인 배치 ────────────────────────────────────────────────
    private static float LaneDocY(int index) => -100f + index * 10f; // 전부 y<-50(지시 1. 안전지대)

    /// <summary>[정정 2026-09-09, A5] 이름 기준으로 이전 실행 잔존물을 지우던 CleanupStaleObjects()를
    /// 대체한다 — 삭제하지 않고 "있는지"만 본다. 있으면 호출부가 실행을 중단하고 사용자에게
    /// 씬 루트의 Player_Sphere/Cube/Tetrahedron 직접 삭제를 요청한다(1 Clear로는 안 지워짐,
    /// 동명의 실제 Setup Play 결과물을 실수로 삼키지 않기 위함).</summary>
    private static bool HasPreexistingObjects()
    {
        if (GameObject.Find("V3PhysLab_Experiment") != null) return true;
        foreach (var shape in Shapes)
            if (GameObject.Find(shape.goName) != null) return true;
        return false;
    }

    /// <summary>바닥(V3.Box 스타일 BoxCollider, 기본 재질 — PhysicMaterial 미지정) + 단 4종
    /// (높이 0.5·0.8·0.96·1.0, 깊이 3.0·폭 6.0) + 각 단 앞 3.0U 착지 평면. V3.Box를 그대로
    /// 재사용해 콜라이더 생성 방식을 기존 블록아웃과 동일하게 맞춘다(V3_Core.cs 64~74행).</summary>
    private static GameObject BuildExperimentSpace()
    {
        GameObject root = new GameObject("V3PhysLab_Experiment");
        for (int i = 0; i < StepHeights.Length; i++)
        {
            float h = StepHeights[i];
            float laneY = LaneDocY(i);
            float yMin = laneY - LaneWidth / 2f;
            float yMax = laneY + LaneWidth / 2f;
            string tag = $"H{h:0.00}";

            // 착지 평면(상면 z=0, 깊이 3.0).
            V3.Box(root, $"Lane_{tag}_Landing", 0f, yMin, -1f, StepFrontDocX, yMax, 0f, "Gray");
            // 계단 1단(상면 z=stepHeight, 깊이 3.0) — V3.Stairs가 아니라 단일 리저 박스다.
            // 실재 지형 검증용 신설 실험체라 🔒N4(계단 턱≤0.8) 대상이 아니다(1.0단도 포함해야
            // 하는 실험 목적상 의도적으로 초과 높이를 포함한다).
            V3.Box(root, $"Lane_{tag}_Step", StepFrontDocX, yMin, -1f, StepFrontDocX + StepTopDepth, yMax, h, "Gray");
        }
        return root;
    }

    // ── 케이스 1회 실행 ──────────────────────────────────────────
    private static CaseResult RunCase(string shapeMenu, string goName, float stepHeight, float laneDocY,
        bool wrapperOn, string inputMode, List<int> createdInstanceIds, List<PhysicMaterial> createdMaterials,
        Dictionary<string, int> lastInstanceIdByName)
    {
        // [정정 2026-09-09, 검문27차 A3] 이름 기반 GameObject.Find+DestroyImmediate를 인스턴스ID
        // 기준으로 교체 — 게이트(HasPreexistingObjects) 뒤라 원래도 무해했지만, A5와 같은 원칙
        // (이름으로 찾아 지우지 않는다)으로 통일한다. 직전 케이스가 이 이름으로 만든 인스턴스가
        // 남아 있을 때만(정상 경로면 RunCase 끝에서 이미 파괴됨 — 예외로 중간에 끊긴 경우만 해당) 정리한다.
        if (lastInstanceIdByName.TryGetValue(goName, out int staleId))
        {
            GameObject stale = EditorUtility.InstanceIDToObject(staleId) as GameObject;
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale);
        }

        // 정식 생성기 호출(공통규칙 철칙 3 — 직접 생성 절대 금지). PlayerObjectMenuItem.cs가
        // 3단 계층(Root/Mesh/Collider)·접지 마스크·PlayerShapeIdentity까지 전부 구성한다.
        if (!EditorApplication.ExecuteMenuItem("Tools/PlayerSystem/Create Player/" + shapeMenu))
            throw new Exception("정식 생성기 메뉴 실행 실패: " + shapeMenu);

        GameObject root = GameObject.Find(goName);
        if (root == null) throw new Exception(goName + " 스폰 후 오브젝트를 찾지 못함");
        // [정정 2026-09-09, A5] 예외로 이 케이스가 중간에 끊겨도 바깥 finally가 인스턴스ID로
        // 찾아 정리할 수 있도록 생성 직후 등록해둔다.
        createdInstanceIds.Add(root.GetInstanceID());
        // [신규 2026-09-09, 검문27차 A3] 다음 케이스(같은 goName)가 시작될 때 이 인스턴스를
        // 인스턴스ID로 찾아 정리할 수 있도록 갱신해둔다.
        lastInstanceIdByName[goName] = root.GetInstanceID();

        FixColliderOffsets(root);   // PlayerShapeController.FixedUpdate() 1회 재현
        PhysicMaterial identityMaterial = ApplyIdentityStats(root); // PlayerShapeIdentity.Start() 1회 재현(Start()가 안 돔)
        if (identityMaterial != null) createdMaterials.Add(identityMaterial);

        Transform colliderT = root.transform.Find("Player_Collider");
        Rigidbody rb = root.GetComponent<Rigidbody>();
        PlayerShapeIdentity identity = root.GetComponent<PlayerShapeIdentity>();
        PlayerShapeStats stats = identity.stats;
        Collider[] ownColliders = root.GetComponentsInChildren<Collider>(true);
        Collider solidCollider = identity.solidCollider; // [A3] z 측정 기준 = 콜라이더 하단(발밑)
        // [신규 2026-09-09, GPT검토패키지_2026-09-09 지적1] rollRadius 취득원 — 원본(PlayerMover.cs)이
        // 읽는 곳과 동일하게 PlayerMover 컴포넌트의 공개 필드에서 그대로 읽는다(PlayerMover.rollRadius 필드,
        // 기본값 0.5f). PlayerObjectMenuItem.cs 전수 grep으로 도형별 rollRadius 덮어쓰기가 0건임을
        // 확인했다(ApplyIdentityStats가 moveSpeed/jumpHeight/mass/friction만 PlayerShapeStats에서
        // 덮어쓰고 rollRadius는 손대지 않는다 — PlayerShapeStats/PlayerShapeIdentity 어디에도 이
        // 필드가 없다) — 그래서 세 도형 모두 필드 기본값 0.5f 그대로 쓰인다.
        PlayerMover mover = root.GetComponent<PlayerMover>();
        float rollRadius = mover != null ? mover.rollRadius : 0f;

        float startDocX = (inputMode == "b") ? (StepFrontDocX - 0.8f) : LandingStartDocX;
        root.transform.position = V3.Doc(startDocX, laneDocY, 0f);
        root.transform.rotation = Quaternion.identity;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        V3_WallSlip wrapper = null;
        int trackedAtInit = -1; // [신규 검문27차 R4] wrapperOn일 때만 실제 값으로 갱신된다.
        if (wrapperOn)
        {
            wrapper = root.AddComponent<V3_WallSlip>();
            wrapper.enabledSlip = true;
            // [정정 2026-09-09, 검문25차 (라)] Edit Mode에서 AddComponent가 Awake를 그 프레임에
            // 동기 호출한다는 보장이 없다(V3_WallSlip.cs 클래스 상단 주석 참고, 유력 가설·미확정)
            // — CacheColliders·BuildFrictionlessMaterial이 안 돌면 tracked가 비어 SwapMaterials가
            // 사실상 무동작(대상 콜라이더 0개)이 된다. 하네스가 직접 Initialize()를 호출해 이
            // 불확실성을 없앤다(멱등 — Awake가 나중에 불려도 다시 캐싱할 뿐).
            wrapper.Initialize();
            if (wrapper.FrictionlessMaterial != null) createdMaterials.Add(wrapper.FrictionlessMaterial);
            // [신규 검문27차 R4] tracked 개수를 케이스당 1회 기록 — swapCount는 tracked가 0개여도
            // SwapMaterials 호출 자체(대상 없이)로 증가할 수 있어 "래퍼가 실제 콜라이더에 작용했다"
            // 는 증거가 못 된다(V3_WallSlip.cs Tick() 참고). 이 값과 나란히 봐야 유효성을 알 수 있다.
            trackedAtInit = wrapper.TrackedCount;
        }
        else if (inputMode == "c")
        {
            // (c) 래퍼 대신 플레이어 콜라이더에 무마찰 재질을 상시 부착(지시 3.항 (c)).
            PhysicMaterial always0 = new PhysicMaterial(goName + "_AlwaysFrictionless")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicMaterialCombine.Minimum
            };
            if (identity.solidCollider != null) identity.solidCollider.material = always0;
            createdMaterials.Add(always0);
        }

        var result = new CaseResult
        {
            shape = goName,
            stepHeight = stepHeight,
            wrapperOn = wrapperOn,
            inputMode = inputMode,
            trackedColliders = trackedAtInit,
        };

        float simTime = 0f;
        float lastGroundedSimTime = -999f;
        bool jumped = false;
        float wallFirstContactTime = -1f;
        bool forwardForB = false; // (b)는 점프 전까지 정지, 점프 후 전진(지시)
        bool prevWallNow = false; // [신규 31차 보완] wallLostWhileRising 계측용 — 직전 프레임 벽 접촉 상태
        // [신규 2026-09-09, GPT검토패키지_2026-09-09 지적2] 루프 중 어느 프레임이든(post-simulate,
        // finalDocX와 동일 시점) 콜라이더(=root, FixColliderOffsets는 y만 오프셋해 x는 root와
        // 동일)의 x가 상판 끝을 넘은 적이 있는지 — flewOver 재정의(아래 루프 끝)에 쓴다.
        bool crossedTopEdgeX = false;

        for (int step = 0; step < TotalSteps; step++)
        {
            Vector3 colliderPos = colliderT.position;
            bool grounded = ComputeGrounded(colliderPos, ownColliders, ref lastGroundedSimTime, simTime);
            // [정정 2026-09-09, 컨트롤타워 31차 판정] solid 콜라이더 bounds가 없으면(이론상 발생
            // 안 함, 방어적 처리만) 중심점 주변 임의 소형 bounds로 대체한다.
            Bounds wallProbeBounds = solidCollider != null ? solidCollider.bounds : new Bounds(colliderPos, Vector3.one * 0.1f);
            bool wallNow = ProbeWallContact(wallProbeBounds, ownColliders);
            // [신규 31차 보완] 상승 중 벽 접촉이 이번 프레임에 끊겼는지 계측 — 아래 rb.velocity는
            // 이 시점까지 이전 스텝 결과 그대로다(이번 스텝의 이동/점프 대입은 아직 안 일어남).
            // [정정 2026-09-09 #37, map-reviewer 33차 A1] 임계를 하드코딩 >0f에서 Tick()의
            // risingFast와 동일한 V3_WallSlip.RisingVyThreshold(0.05)로 통일 — 이전엔 0<vy≤0.05
            // 구간(정상 착지 복원)까지 이 계측이 "상승중 복원"으로 잘못 셌다.
            bool risingNow = rb.velocity.y > V3_WallSlip.RisingVyThreshold;
            if (risingNow && prevWallNow && !wallNow) result.wallLostWhileRising++;
            prevWallNow = wallNow;

            if (grounded) result.groundedFrames++;
            if (wallNow) result.wallContactFrames++;
            if (wallNow && wallFirstContactTime < 0f) wallFirstContactTime = simTime;

            if (wrapper != null)
            {
                // [신규 2026-09-09, 검문25차 (라)] IsFrictionlessNow가 실제로 바뀐 시점만 "스왑"
                // 으로 센다 — Tick() 자체는 매 프레임 불리지만 SwapMaterials는 상태 전환 때만
                // 실행되므로, 이 두 값이 "래퍼가 실제로 동작했다"는 증거가 된다(ON 열의 유효성).
                bool beforeFrictionless = wrapper.IsFrictionlessNow;
                wrapper.Tick(wallNow, grounded);
                if (wrapper.IsFrictionlessNow != beforeFrictionless)
                {
                    result.swapCount++;
                    if (result.firstSwapSimTime < 0f) result.firstSwapSimTime = simTime;

                    bool isRestore = beforeFrictionless && !wrapper.IsFrictionlessNow; // 무마찰→원본
                    if (isRestore)
                    {
                        // [신규 2026-09-09, map-reviewer 34차 R2] risingNow 게이트 밖에서 원본 복원을
                        // 상승 여부와 무관하게 전수 센다 — restoreSwapWhileRising이 임계 통일(검문
                        // 33차 A1) 이후 구조적으로 항상 0인 tautology가 되어 D1 잔존을 못 잡게 된
                        // 것의 대체(클래스 상단 필드 주석 참고).
                        result.restoreTotal++;
                        if (!result.restoreOccurred)
                        {
                            // [정정 2026-09-09, map-reviewer 34차 R2] 최초 복원 시점의 발밑(콜라이더
                            // 하단) 문서 z·simTime을 risingNow와 무관하게 기록한다(이전엔 risingNow
                            // 게이트 안에서만 기록해 사실상 한 번도 안 채워졌다) — 이 스텝의
                            // 이동/점프 대입은 아직 안 일어났으므로(Physics.Simulate는 루프 뒤쪽)
                            // colliderPos가 이 프레임 시작 시점의 실제 위치다.
                            result.firstRestoreSimTime = simTime;
                            result.firstRestoreBottomZ = solidCollider != null ? solidCollider.bounds.min.y : colliderPos.y;
                            result.restoreOccurred = true;
                        }
                        // [신규 31차 보완, 정정 2026-09-09 map-reviewer 34차 R2] 상승 중(risingNow)
                        // 복원만 별도로도 센다 — 임계 통일 후 구조적으로 항상 0이지만(클래스 상단
                        // restoreSwapWhileRising 주석 참고) 회귀 감시용으로 필드·계측을 유지한다.
                        if (risingNow)
                        {
                            result.restoreSwapWhileRising++;
                        }
                    }
                }
            }

            // 입력 결정.
            float h = 0f, v;
            if (inputMode == "a" || inputMode == "c") v = 1f;      // 전진 유지(지시 (a)(c))
            else v = forwardForB ? 1f : 0f;                        // (b) 정지 → 점프 후 전진

            // 점프 트리거(1회성) 판정 — 실제 입력 이벤트(Space 1회)를 흉내낸다.
            bool triggerJumpNow = false;
            if (!jumped)
            {
                if (inputMode == "a" || inputMode == "c")
                {
                    if (wallFirstContactTime >= 0f && simTime >= wallFirstContactTime + JumpDelayAfterWallContact)
                        triggerJumpNow = true;
                }
                else
                {
                    if (simTime >= SettleTimeB) triggerJumpNow = true;
                }
            }

            // PlayerMover.LegacyVelocityFixedUpdate() 재현.
            Vector3 moveRaw = new Vector3(h, 0f, v) * stats.moveSpeed;
            Vector3 move = Quaternion.AngleAxis(InputYawOffsetDeg, Vector3.up) * moveRaw; // inputYawOffset 회전 적용부
            if (grounded)
            {
                rb.velocity = new Vector3(move.x, rb.velocity.y, move.z); // 대입(가속 아님)
                // [정정 2026-09-09, GPT검토패키지_2026-09-09 지적1·컨트롤타워 재현됨] 이전 주석
                // "rollRadius 각속도 연출은 시각용이라 재현 생략"은 틀렸다 — 동적 Rigidbody의
                // angularVelocity 하드 대입은 PhysX 접촉 솔버·마찰 결합에 실제로 영향하는 물리
                // 대입이지 시각 전용 연출이 아니다(velocity 대입과 같은 성격의 하드 대입). 원본
                // 그대로 재현한다(PlayerMover.LegacyVelocityFixedUpdate()의 rollRadius 대입).
                // rollRadius가 0에 가까우면(<=0.0001f) 이 대입 자체가 없어 네모·세모(현재 기본값
                // 0.5f로 실제로는 항상 적용됨, 위 rollRadius 취득 주석 참고)도 그 경우엔 무영향이다
                // — 가드 조건은 원본과 동일하게 유지한다.
                if (rollRadius > 0.0001f)
                    rb.angularVelocity = Vector3.Cross(Vector3.up, move) / rollRadius; // 각속도 대입
            }
            else
            {
                Vector3 airMove = DeflectAirMoveFromWall(move, rb.position, ownColliders); // PlayerMover.DeflectAirMoveFromWall() 호출부·본체 재현
                rb.velocity = new Vector3(airMove.x * AirControlMultiplier, rb.velocity.y, airMove.z * AirControlMultiplier); // 공중 이동 대입
            }

            // PlayerJump.LaunchToHeight() 재현. PlayerGravityOverride 없음 → 전역 중력 사용.
            if (triggerJumpNow)
            {
                jumped = true;
                result.jumpTriggered = true;
                result.jumpAtGroundedTrue = grounded; // PlayerJump.FixedUpdate()의 `if (!IsGrounded()) return;` 소비 시점 기록
                if (grounded)
                {
                    float g = Mathf.Abs(Physics.gravity.y);
                    float vy = Mathf.Sqrt(2f * g * stats.jumpHeight); // PlayerJump.LaunchToHeight()
                    rb.velocity = new Vector3(rb.velocity.x, vy, rb.velocity.z); // 수평 보존
                }
                if (inputMode == "b") forwardForB = true;
            }

            Physics.Simulate(FixedDt);
            simTime += FixedDt;

            // [정정 2026-09-09, A3] z 측정 기준을 루트 위치에서 콜라이더 하단(발밑)으로 통일 —
            // 세모는 로컬 y=+0.5 강제와 실제 형상(밑면 -0.2887)이 안 맞아 루트 기준으로는
            // +0.211U 뜬 값이 섞였다. bounds.min.y(Unity y = 문서 Z)가 실제 발밑이다.
            float colliderBottomDocZ = solidCollider != null ? solidCollider.bounds.min.y : root.transform.position.y;
            if (colliderBottomDocZ > result.maxDocZ) result.maxDocZ = colliderBottomDocZ;
            result.finalDocX = root.transform.position.x;
            // [신규 2026-09-09, GPT검토패키지_2026-09-09 지적2] flewOver 재정의용 — 상판 끝
            // (x=6.0) 통과 여부를 매 프레임 누적(한 번 true면 계속 true).
            if (result.finalDocX >= (StepFrontDocX + StepTopDepth)) crossedTopEdgeX = true;

            // [신규 2026-09-09, 검문27차 R1(컨트롤타워 결정)] 이 프레임이 (i)~(iv)를 동시에
            // 만족하는지 검사 — 최초 만족 프레임만 기록한다(landedSimTime>=0 가드로 그 뒤는 스킵).
            if (result.landedSimTime < 0f)
            {
                bool stepGroundedStrict = IsGroundedStrict(colliderT.position, ownColliders); // (iii)
                bool stepNearTopZ = Mathf.Abs(colliderBottomDocZ - stepHeight) <= PassZTolerance; // (i)
                bool stepOnTopX = result.finalDocX >= StepFrontDocX && result.finalDocX <= (StepFrontDocX + StepTopDepth); // (ii) = [3.0,6.0]
                bool stepVyOk = Mathf.Abs(rb.velocity.y) < PassVyTolerance; // (iv)
                if (stepNearTopZ && stepOnTopX && stepGroundedStrict && stepVyOk)
                    result.landedSimTime = simTime;
            }
        }

        // [정정 2026-09-09, 검문27차 R1(컨트롤타워 결정)] 통과 판정을 "마지막 프레임 4조건"에서
        // "시뮬 도중 어느 프레임에서든 (i)~(iv)를 동시에 만족한 프레임이 1개 이상"으로 바꾼다.
        // 근거(검문27차 §1 (가)): BuildExperimentSpace가 레인당 착지판+계단 1단만 두어 상판 끝
        // (x=6) 뒤에 정지 지형이 없고, 접지 중 rb.velocity 대입([정정 2026-09-09, 검문32차 A3]
        // ":404행 부근"은 부정확했다 — [정정 #37, 검문33차 R2] 줄번호 대신 함수명 앵커로: 실제
        // 위치는 RunCase 지상 분기의 rb.velocity 대입, 원본 PlayerMover.LegacyVelocityFixedUpdate() 재현)이 계속 moveSpeed로
        // 전진시키는 탓에 마지막 프레임엔 거의 항상 상판을 벗어나 있었다(09-06 리포트 실측
        // 60/60 케이스 전부 구간 밖). 시뮬 길이·입력 대입·지형은 그대로 둔다(정지벽 추가는
        // 하지 않는다 — ProbeWallContact가 새 벽에 오염된다). 판정은 위 루프 안에서 매 프레임
        // 계산해 result.landedSimTime에 최초 만족 시각을 기록해 뒀다(없으면 -1).
        result.passed = result.landedSimTime >= 0f;
        // [정정 2026-09-09, GPT검토패키지_2026-09-09 지적2·컨트롤타워 재현됨] "비행 통과" = 최고z가
        // 단높이 이상까지 올랐고, 루프 중 어느 프레임이든 x가 상판 끝(6.0)을 넘은 적이 있지만,
        // 어떤 프레임에서도 4조건을 동시에 못 채운 경우 — 이전엔 x 조건이 없어 "높이만 넘고 벽
        // 앞에 그대로 남은" 경우까지 비행통과로 오분류될 수 있었다.
        result.flewOver = !result.passed && result.maxDocZ >= stepHeight && crossedTopEdgeX;
        // [신규 2026-09-09, GPT검토패키지_2026-09-09 지적2] 높이는 넘었으나(또는 못 넘었으나) x가
        // 계단 앞면(3.0) 미만으로 남아 벽 앞에 핀닝된 상태를 flewOver와 구분해 별도로 표시한다.
        result.stalledAtWall = !result.passed && !result.flewOver && result.finalDocX < StepFrontDocX;

        UnityEngine.Object.DestroyImmediate(root);
        return result;
    }

    // ── PlayerShapeController.FixedUpdate() 재현 ────────────
    // (Update()·FixedUpdate()가 매 프레임 이 오프셋을 강제하지만, Edit Mode에선 그 자체가 안
    // 돌므로 1회만 맞춰둔다 — 값 자체는 상수라 매 스텝 재적용이 필요 없다.)
    private static void FixColliderOffsets(GameObject root)
    {
        Transform colliderT = root.transform.Find("Player_Collider");
        Transform meshT = root.transform.Find("Player_Mesh");
        if (colliderT != null)
            colliderT.localPosition = new Vector3(colliderT.localPosition.x, 0.5f, colliderT.localPosition.z);
        if (meshT != null)
            meshT.localPosition = new Vector3(meshT.localPosition.x, 0.5f, meshT.localPosition.z);
    }

    // ── PlayerShapeIdentity.Start() 재현 ───────────────
    // [정정 2026-09-09, A5] 반환값을 PhysicMaterial로 바꿔 호출부가 정리 목록에 등록할 수 있게
    // 했다(이전엔 void — 이 재질이 어디서도 안 지워지고 남았다).
    private static PhysicMaterial ApplyIdentityStats(GameObject root)
    {
        PlayerShapeIdentity identity = root.GetComponent<PlayerShapeIdentity>();
        PlayerShapeStats stats = identity.stats;
        if (stats == null) throw new Exception(root.name + ": PlayerShapeStats 에셋이 없음");

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb != null) rb.mass = stats.mass; // Start()의 질량 대입

        PlayerMover mover = root.GetComponent<PlayerMover>();
        if (mover != null) mover.moveSpeed = stats.moveSpeed; // Start()의 이속 대입

        PlayerJump jump = root.GetComponent<PlayerJump>();
        if (jump != null) jump.jumpHeight = stats.jumpHeight; // Start()의 점프높이 대입

        if (identity.solidCollider == null) return null;

        PhysicMaterial mat = new PhysicMaterial($"{root.name}_Physic")
        {
            staticFriction = stats.friction,
            dynamicFriction = stats.friction,
            frictionCombine = PhysicMaterialCombine.Average,
            bounciness = stats.bounciness,
            bounceCombine = PhysicMaterialCombine.Minimum
        }; // Start()의 물리 머티리얼 구성
        identity.solidCollider.material = mat;
        return mat;
    }

    // ── PlayerGroundContact.FixedUpdate() 아래 SphereCast 판정 + IsGrounded 프로퍼티(코요테 유예)
    // 재현. OnCollisionStay()는 Edit Mode 콜백 미발생으로 재현하지
    // 않는다(재현 불가 — 회신에 명시) — SphereCast 경로만으로 충분히 접지를 잡는지가 이 실험의
    // 관찰 대상 중 하나이기도 하다.
    private static bool ComputeGrounded(Vector3 colliderWorldPos, Collider[] ownColliders,
        ref float lastGroundedSimTime, float simTime)
    {
        if (IsGroundedStrict(colliderWorldPos, ownColliders)) lastGroundedSimTime = simTime;
        return (simTime - lastGroundedSimTime) <= GroundedGraceTime;
    }

    /// <summary>[신규 2026-09-09, A1~A3] 코요테 유예 없이 이 순간 실제로 바닥을 딛고 있는지만
    /// 본다 — ComputeGrounded의 SphereCast 판정 본체를 그대로 재사용(중복 로직 없음), 유예
    /// 상태(lastGroundedSimTime)는 건드리지 않는다. 매 프레임 착지 판정(조건 iii)에
    /// 쓴다 — 유예를 섞으면 이미 공중에 뜬 채로도 "접지"로 잡혀 판정이 느슨해진다.</summary>
    private static bool IsGroundedStrict(Vector3 colliderWorldPos, Collider[] ownColliders)
    {
        RaycastHit[] hits = Physics.SphereCastAll(colliderWorldPos, GroundCheckRadius, Vector3.down,
            GroundCheckDistance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            if (Array.IndexOf(ownColliders, hits[i].collider) >= 0) continue;
            if (hits[i].normal.y > GroundNormalThreshold) return true;
        }
        return false;
    }

    /// <summary>V3_WallSlip이 실제로 쓰는 OnCollisionStay 접촉 콜백은 Edit Mode에서 호출되지
    /// 않으므로(클래스 상단 주석 참고), 하네스 전용 대체 판정으로 수평 8방향 단거리
    /// 레이캐스트를 쏴 |normal.y|&lt;wallNormalYThreshold인 면이 있는지 검사한다. 판정 임계값
    /// 자체는 PlayerMover.DeflectAirMoveFromWall() 내부 임계와 동일하게 맞췄다 —
    /// "OnCollisionStay 접촉 법선"의 완전한 재현은 아니라는 점을 회신의 재현 불가 항목에 남긴다.
    /// [정정 2026-09-09, 검문25차 (다)] 사거리를 도형 무관 상수(0.60) 대신 ownColliders의 실측
    /// world bounds extents 최대값 + 여유(ProbeMargin)로 매 호출 계산한다.
    /// [정정 2026-09-09, 검문27차 R3] 세모의 실제 계산 기준은 solid 콜라이더 AABB extents
    /// (실측 0.5964)가 아니라 더 큰 트리거(Player_Mesh) AABB extents(실측 0.6830)다 —
    /// ownColliders는 트리거도 포함해 순회한다. 실제 ComputeProbeDistance 반환값은
    /// 0.6830+0.10=0.783이고, 09-06 리포트 실측으로 역산한 필요 사거리 0.6704(정지 x 2.3296 =
    /// 3.0−0.6704)보다 커 충분하다. 이 계산은 bounds.extents.x·extents.z 중 큰 값을 쓰므로
    /// 세모(0.783)·네모/구(0.5+0.10=0.60, 이전 상수와 동일)를 자동으로 만족한다.
    /// [정정 2026-09-09, 컨트롤타워 31차 판정] 이전 판은 콜라이더 "중심 1점"에서만 쐈다 — 상승
    /// 중 그 원점이 단 상단(z=h)을 넘는 순간 wall=false로 떨어지고, 그때 접지는 SphereCast
    /// 0.85U 도달 + 코요테 0.1s 때문에 여전히 true라 V3_WallSlip.Tick의
    /// `wallContactNow || !grounded`가 false가 되어 원본 마찰이 복원되고 벽 마찰이 그 높이에서
    /// 상승을 죽인다(예측식 maxZ≈(h−프로브원점 오프셋)+오버슈트가 네모·세모 8/8 실측과 일치,
    /// map-reviewer.md §1 #31). 대응 — 벽 감지 = 발밑/중심/상단 3높이 8방향(31차 보완): solid
    /// 콜라이더 bounds의 세 높이(min.y+0.05·center.y·max.y−0.05)에서 각각 수평 8방향을 쏘고
    /// 하나라도 벽에 맞으면 접촉으로 판정한다 — 상승 중 원점 하나가 단을 넘어도 나머지 두
    /// 높이가 계속 벽면(수직면)을 잡는 한 벽 접촉이 유지된다. [대안 검토] Physics.OverlapBox
    /// (bounds를 수평 +0.05 확장, 자기 제외, 법선 y&lt;0.5 벽면만)도 가능했으나, 기존
    /// ComputeProbeDistance·WallNormalYThreshold·자기 콜라이더 제외 로직을 그대로 재사용할 수
    /// 있고 RaycastHit.normal이 그대로 나와 판정식이 더 단순한 레이 방식을 택했다.</summary>
    private static bool ProbeWallContact(Bounds solidBounds, Collider[] ownColliders)
    {
        float probeDist = ComputeProbeDistance(ownColliders);
        Vector3[] dirs =
        {
            Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
            (Vector3.forward + Vector3.right).normalized, (Vector3.forward + Vector3.left).normalized,
            (Vector3.back + Vector3.right).normalized, (Vector3.back + Vector3.left).normalized,
        };
        // [신규 31차 보완] 세 높이 — 하단(발밑쪽 0.05 여유)·중심·상단(0.05 여유). bounds가
        // 아주 얇아 min+0.05 > max-0.05가 되는 극단값이면 중심 높이가 그 사이를 대표한다.
        float[] probeYs =
        {
            solidBounds.min.y + 0.05f,
            solidBounds.center.y,
            solidBounds.max.y - 0.05f,
        };
        for (int y = 0; y < probeYs.Length; y++)
        {
            Vector3 origin = new Vector3(solidBounds.center.x, probeYs[y], solidBounds.center.z);
            for (int d = 0; d < dirs.Length; d++)
            {
                RaycastHit[] hits = Physics.RaycastAll(origin, dirs[d], probeDist, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits.Length; i++)
                {
                    if (Array.IndexOf(ownColliders, hits[i].collider) >= 0) continue;
                    if (Mathf.Abs(hits[i].normal.y) < WallNormalYThreshold) return true;
                }
            }
        }
        return false;
    }

    /// <summary>[신규 2026-09-09, (다)] ownColliders의 world bounds(Collider.bounds, AABB)
    /// extents.x·extents.z 중 최대값에 ProbeMargin(0.10)을 더한다 — "콜라이더 실제 수평 반경 +
    /// 여유"를 도형 구분 없이 계산하는 최소 구현.</summary>
    private static float ComputeProbeDistance(Collider[] ownColliders)
    {
        float maxExtent = 0f;
        for (int i = 0; i < ownColliders.Length; i++)
        {
            Bounds b = ownColliders[i].bounds;
            if (b.extents.x > maxExtent) maxExtent = b.extents.x;
            if (b.extents.z > maxExtent) maxExtent = b.extents.z;
        }
        return maxExtent + ProbeMargin;
    }

    /// <summary>PlayerMover.DeflectAirMoveFromWall() 재현 — 자기 자신 제외를
    /// GetComponentInParent&lt;PlayerMover&gt; 대신 ownColliders 배열 대조로 치환한 것 외에는
    /// 동일하다.</summary>
    private static Vector3 DeflectAirMoveFromWall(Vector3 horizontalMove, Vector3 originWorld, Collider[] ownColliders)
    {
        if (AirWallCheckDistance <= 0f || horizontalMove.sqrMagnitude < 1e-4f) return horizontalMove; // 사거리 0 가드

        Vector3 dir = horizontalMove.normalized;
        RaycastHit[] hits = Physics.RaycastAll(originWorld, dir, AirWallCheckDistance, ~0, QueryTriggerInteraction.Ignore); // 벽 탐색 레이캐스트
        float bestDist = float.PositiveInfinity;
        Vector3 wallNormal = Vector3.zero;
        for (int i = 0; i < hits.Length; i++)
        {
            if (Array.IndexOf(ownColliders, hits[i].collider) >= 0) continue;       // 자기 자신 제외
            if (Mathf.Abs(hits[i].normal.y) >= 0.5f) continue;                      // 바닥/천장 제외
            if (hits[i].distance < bestDist) { bestDist = hits[i].distance; wallNormal = hits[i].normal; } // 최근접 벽 갱신
        }
        if (wallNormal == Vector3.zero) return horizontalMove; // 벽 없음

        float into = Vector3.Dot(horizontalMove, wallNormal);   // 벽으로 미는 성분
        if (into < 0f) horizontalMove -= into * wallNormal;     // 그 성분만 제거
        return horizontalMove;
    }

    // ── 출력 ────────────────────────────────────────────────────
    /// <summary>[정정 2026-09-10, 컨트롤타워 지시 R4] 기본 출력 폴더를 axiom 루트 밖(클론마다
    /// 존재 여부가 다른 "맵2_V3_릴레이설계" 폴더) 대신 이 유니티 프로젝트 안
    /// "&lt;프로젝트 루트&gt;/V3_Reports"로 통일한다(Assets 밖이라 유니티가 임포트하지 않는다 —
    /// 36차 R4 지적: 팀원 클론에는 저장소 밖 한글 폴더가 없다). V3_REPORT_DIR 환경변수가 있으면
    /// 그것을 최우선으로 쓴다(컨트롤타워가 무인검증 폴더로 지정 — V3_Batch.cs·
    /// Scripts/V3_GateTestRunner.cs와 동일 규칙, -v3out과 달리 axiom 루트 제한 없이 그대로 채택).</summary>
    private static string ResolveOutDir()
    {
        string envOverride = Environment.GetEnvironmentVariable("V3_REPORT_DIR");
        if (!string.IsNullOrEmpty(envOverride))
            return Path.GetFullPath(envOverride);

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectRoot, "V3_Reports"));
    }

    /// <summary>[정정 2026-09-09] 파일명을 실행 날짜로 고정한다(이전엔 "2026-09-06" 리터럴이
    /// 박혀 있어 재실행마다 기존 09-06 리포트를 덮어썼다). 기존 09-06 파일은 이 변경으로도
    /// 건드리지 않는다(다른 파일명이라 자동으로 보존됨).
    /// [정정 2026-09-09, 검문27차 A2] 날짜만으로는 하루 여러 번 재실행 시 여전히 같은 파일명이
    /// 된다(검문27차 §1 (나) 지적 — 09-09 12:15 실행분이 파일명·제목엔 "09-06"으로 남아 사실과
    /// 어긋났다). 분 단위 시각까지 포함해 실행마다 별도 파일을 남긴다.</summary>
    private static string ReportBaseName()
    {
        return "물리실험_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm");
    }

    private static void WriteReports(ResultSet results, string consoleLogText)
    {
        string outDir = ResolveOutDir();
        Directory.CreateDirectory(outDir);
        string baseName = ReportBaseName();

        string json = JsonUtility.ToJson(results, true);
        File.WriteAllText(Path.Combine(outDir, baseName + ".json"), json, new UTF8Encoding(true));

        string md = BuildMarkdownReport(results);
        File.WriteAllText(Path.Combine(outDir, baseName + ".md"), md, new UTF8Encoding(true));

        File.WriteAllText(Path.Combine(outDir, baseName + "_콘솔.txt"), consoleLogText, new UTF8Encoding(true));

        Debug.Log("[V3PhysLab] 리포트 저장 완료: " + outDir);
    }

    private static CaseResult Find(ResultSet r, string shape, float h, bool wrapperOn, string mode)
    {
        foreach (var c in r.cases)
            if (c.shape == shape && Mathf.Approximately(c.stepHeight, h) && c.wrapperOn == wrapperOn && c.inputMode == mode)
                return c;
        return null;
    }

    private static string BuildMarkdownReport(ResultSet results)
    {
        StringBuilder sb = new StringBuilder();

        // 첫 줄: 사용자 재현 사례(세모·0.96단·(a)) 판정 요약.
        CaseResult keyOff = Find(results, "Player_Tetrahedron", 0.96f, false, "a");
        CaseResult keyOn = Find(results, "Player_Tetrahedron", 0.96f, true, "a");
        // [정정 2026-09-09, 컨트롤타워 31차 판정] "(하네스 한계 참조)" 병기 — ON 열은 이번
        // 회차까지도 하네스 대체 벽감지 위의 값이라 100% 원본 재현이 아니다(아래 한계 고지 참고).
        string keyLine = "판정 요약: 래퍼 OFF 0.96단 세모 (a) " + (keyOff != null && keyOff.passed ? "✓" : "✗")
            + " / 래퍼 ON 0.96단 세모 (a) " + (keyOn != null && keyOn.passed ? "✓" : "✗")
            + " (하네스 한계 참조)";
        sb.AppendLine(keyLine);
        // [신규 2026-09-09, map-reviewer 34차 A3] 위 판정 요약 한 줄만 보면 ✗가 전부 "핀닝"으로
        // 오독될 수 있다 — 구 20건(래퍼 OFF 12(a·b·c)·ON 8(a·b), [정정 2026-09-09, map-reviewer
        // 35차 R2] 이전 "OFF 8·ON 12"는 빌더 자체 오기였다 — 1805.json 전수 재현: Sphere OFF
        // 12/12·ON 8/8이 flewOver)은 전부 비행통과(flewOver, 날아서 지나감)이지 핀닝이 아니고,
        // 세모 0.5는 ON-(a)가 rsr=1인데도 maxZ 1.937(OFF-(c)와 동일 = 완전 무마찰 상승)이라 핀닝
        // 사례가 아니다(map-reviewer.md §1 33차 핵심). [정정 2026-09-09, map-reviewer 35차 R1]
        // 세모 0.5는 ON-(b)가 오히려 핀닝 6건 목록에 들어 있어(34차 R1), 아래 인쇄문의 "세모 0.5"는
        // ON-(a) 1건에만 한정한다 — ON-(b)까지 포함해 읽으면 거짓이 된다.
        // [정정 2026-09-10, map-reviewer 37차 A5] 구 괄호 "(구 정의 집계 — 이번 판 재정의 후
        // 재측정 전)"을 삭제한다 — 이 문장은 재측정으로 생성되는 리포트 자신에 박혀 있어, 남겨
        // 두면 "아직 재측정 전"이라는 거짓을 리포트가 스스로 주장하게 된다(27차 (나)·35차 R1과
        // 같은 재발 유형). 같은 실측(37차)으로 세모 0.5 ON의 실제 비행통과는 (a) 1건이 아니라
        // (a)·(b) 2건임도 확인돼 아래 문구를 정정한다.
        sb.AppendLine("구 20건·세모 0.5 ON-(a)·(b)는 비행통과(핀닝 아님) — 1805·2057 실측 기준, 표 참조");
        // [신규 2026-09-09, GPT검토패키지_2026-09-09 지적2] 위 "구 20건" 집계는 flewOver의 이전
        // 정의(x 조건 없이 maxDocZ만으로 판정)로 측정된 값이다 — 이번 재정의(상판 끝 x=6.0 통과
        // 조건 추가 + stalledAtWall 분리) 이후 재실행하면 이 집계가 달라질 수 있다(원문 미삭제,
        // 재측정 전까지는 이 문구를 "구" 표기로만 참조할 것).
        sb.AppendLine();
        sb.AppendLine("# 물리실험 — 세모 계단 앞 점프 불가 재현 및 수정안 ① 측정 (검문" + ReviewRound + " 반영, " + DateTime.Now.ToString("yyyy-MM-dd") + ")");
        sb.AppendLine();
        // [정정 2026-09-09, 컨트롤타워 31차 판정 — map-reviewer.md §1 #31 반려 R1] 이전 문구
        // "3종×4종×{OFF,ON}×(a)(b)(c)=60"은 산술 오류였다(3×4×2×3=72≠60). 실제 설계는
        // OFF (a)(b)(c) 3모드 + ON (a)(b) 2모드 = 5조합([정정 2026-09-09, 검문32차 A2] ":199~204"는
        // 부정확했다 — [정정 #37, 검문33차 R2] 줄번호는 편집마다 어긋난다(A2가 고친 :215~220도
        // 이미 다시 틀렸다는 것이 33차에서 재확인됨) — 실제 위치는 RunAll의 RunCase 5회 호출부)이고, ON-(c)
        // (래퍼+상시무마찰 동시 적용)는 설계에 없어 미측정이다 — 아래 상세표에도 그 열이 없다.
        sb.AppendLine("도형 3종 × 단 높이 4종 × [OFF (a)(b)(c) + ON (a)(b) = 5조합] = 60케이스(3×4×5), 각 3초(150스텝 @ 50Hz, FixedDt=0.02) 시뮬. ON-(c)는 미측정.");
        sb.AppendLine();
        // [정정 2026-09-09, 컨트롤타워 31차 판정 — R2] 한계 고지(재현 불가 항목) 리포트 본문 명시.
        sb.AppendLine("**한계 고지(31차)**: ON 열은 하네스 대체 벽감지 위의 값이다 — 이전 판(콜라이더 중심 1점에서 수평 8방향)은 상승 중 프로브 원점이 단 상단(z=h)을 넘는 순간 벽 접촉을 놓쳐 V3_WallSlip이 마찰을 원본으로 복원시켰다(예측식 maxZ≈(h−프로브원점 오프셋)+오버슈트가 네모·세모 8/8 실측과 일치). 이번 판은 벽 감지를 발밑/중심/상단 3높이 8방향(31차 보완)으로 넓혔지만, V3_WallSlip이 실제로 쓰는 OnCollisionStay(면 전체 접촉)의 완전한 재현은 여전히 아니다(코드 xmldoc ProbeWallContact 참고). ON 열의 성패는 이 근사 위에서 읽어야 한다.");
        sb.AppendLine("**한계 고지(32차 A5)**: 3높이화는 ON 열뿐 아니라 OFF 열의 점프 트리거 시점(wallFirstContactTime)도 바꾼다 — 09-09 1647판(중심 1점) 네모 0.5 OFF는 wallContactFrames가 27(중심 광선이 마침 h=0.5와 같은 높이라 스치는 접촉)이었으나 이번 3높이판은 118 안팎으로 크게 달라진다. 그래서 OFF 열도 이번 판과 직전 판(09-09 1647) 사이에서 like-for-like 비교가 안 된다 — 판마다의 절대값만 읽어야 한다.");
        sb.AppendLine("**한계 고지(32차 A6)**: 세모(Tetrahedron)만 프로브 원점이 콜라이더 원점 대비 x·z로 +0.0740 이동한다(챔퍼 메쉬 AABB 중심 오프셋, ComputeProbeDistance가 참조하는 트리거 Player_Mesh 기준). 그 결과 세모 (b) 모드는 시작 위치부터 이미 벽까지 실거리 0.726U < 사거리 0.783U라 애초부터 벽 접촉 상태로 시작한다(네모·구는 이 오프셋이 0이라 해당 없음) — (b) 열의 '벽접촉프레임'이 이미 크다는 것은 이 시작 상태 때문일 수 있다.");
        sb.AppendLine();
        sb.AppendLine("**통과 기준(검문27차 R1(컨트롤타워 결정) — 시뮬 도중 어느 프레임에서든 아래 4조건을 동시에 만족한 프레임이 1개 이상이면 통과. 마지막 프레임만 보던 이전 방식은 상판 끝 뒤 정지 지형 부재로 60/60이 항상 구간을 벗어나 있어 폐기):**");
        sb.AppendLine("(i) 콜라이더 하단(발밑, bounds.min.y) 문서 z가 단 높이 h ± " + PassZTolerance.ToString("0.00"));
        sb.AppendLine("(ii) 문서 x ∈ [" + StepFrontDocX.ToString("0.00") + ", " + (StepFrontDocX + StepTopDepth).ToString("0.00") + "](단 상판 위)");
        sb.AppendLine("(iii) 접지(SphereCast, 코요테 유예 미적용 — 그 순간 실제 접지) true");
        sb.AppendLine("(iv) |수직속도| < " + PassVyTolerance.ToString("0.00"));
        sb.AppendLine("최고z 열은 시뮬레이션 전 구간 중 콜라이더 하단(발밑)의 최고값이다(루트 위치 아님 — 세모는 로컬 y=+0.5 강제와");
        sb.AppendLine("실제 형상(밑면 −0.2887)이 안 맞아 루트 기준이면 +0.211U 뜬 값이 섞였다, 검문25차 실측으로 제거).");
        sb.AppendLine("착지시각(landedSimTime) 열 = 위 4조건을 최초로 동시에 만족한 simTime(초), 못 채우면 '-'.");
        sb.AppendLine("'비행통과'(flewOver) 열 = [정정 2026-09-09, GPT검토패키지_2026-09-09 지적2] 최고z가 h 이상까지");
        sb.AppendLine("올랐고 루프 중 어느 프레임이든 x가 상판 끝(" + (StepFrontDocX + StepTopDepth).ToString("0.0") + ")을 넘은 적이 있지만,");
        sb.AppendLine("어떤 프레임에서도 위 4조건을 동시에 못 채운 경우(날아서 지나가 착지에 실패한 경우를 '통과'로");
        sb.AppendLine("오판하지 않기 위한 열) — 이전 정의는 x 조건이 없어 '높이만 넘고 벽 앞에 남은' 경우까지");
        sb.AppendLine("비행통과로 오판할 수 있었다. '벽앞정체'(stalledAtWall) 열 = [신규, 같은 지적] 통과도 비행통과도");
        sb.AppendLine("아니면서 최종x가 계단 앞면(" + StepFrontDocX.ToString("0.0") + ") 미만으로 남은 경우 — 높이를 넘었든 못 넘었든 상판 쪽으로");
        sb.AppendLine("전혀 나아가지 못하고 벽 앞에 핀닝됐음을 flewOver와 구분해 보여준다.");
        sb.AppendLine("[신규, 검문36차 R3] 표에서 통과·비행통과·벽앞정체 어디에도 안 걸리는 네 번째 상태도 있다 — 실패(passed=N)");
        sb.AppendLine("이면서 flewOver=N·stalledAtWall=N인 행: 최종x가 [" + StepFrontDocX.ToString("0.0") + ", " + (StepFrontDocX + StepTopDepth).ToString("0.0") + "](상판 위 구간)에 남았지만 4조건을 동시에");
        sb.AppendLine("못 채운 경우다 — 전용 열이 없으니 flewOver·stalledAtWall 두 열이 모두 N인 행으로 식별한다.");
        sb.AppendLine("'점프시접지'(jumpAtGroundedTrue) 열 N = 점프 입력이 소비된 시점(트리거 프레임)에 비접지 상태라");
        sb.AppendLine("PlayerJump.FixedUpdate()의 `if (!IsGrounded()) return;`에 걸려 점프가 거절됐다는 뜻이다(원본 그대로 재현) —");
        sb.AppendLine("점프 자체가 안 나갔으니 이후 착지 실패는 핀닝이 아니라 이 거절이 원인이다(구 실험 2건: 1647·2057");
        sb.AppendLine("리포트 각 1건, 0.5단 OFF-(a), GPT검토패키지_2026-09-09 지적4).");
        sb.AppendLine("스왑횟수·최초스왑시각 열은 V3_WallSlip.IsFrictionlessNow가 실제로 바뀐 토글 횟수/시각이다 — SwapMaterials는");
        sb.AppendLine("tracked가 0개여도 호출될 수 있어 이 값만으로는 '래퍼가 실제 콜라이더에 작용했다'는 증거가 되지 않는다");
        sb.AppendLine("(검문27차 R4) — tracked열(V3_WallSlip.TrackedCount, 래퍼 초기화 시점 1회 기록)과 함께 봐야 한다.");
        sb.AppendLine("벽이탈(상승중)·복원스왑(상승중) 열은 [신규 31차 보완] rb.velocity.y>0.05(V3_WallSlip.RisingVyThreshold,");
        sb.AppendLine("Tick의 risingFast와 동일 임계 — [정정 #37, 검문33차 A1] 이전엔 >0f 하드코딩이라 어긋났었다)인 프레임에서 벽 접촉이 true→false로");
        sb.AppendLine("바뀐 횟수 / 그 동안 V3_WallSlip이 마찰을 원본으로 복원한 횟수다 — [정정 2026-09-09, map-reviewer");
        sb.AppendLine("35차 A1] 벽상실(상승중)=0인데도 해당 케이스의 ON이 ✗면, 이번 3높이 벽감지 기준으로도 상승 중");
        sb.AppendLine("벽을 놓치지 않았다는 뜻이라 그 실패는 하네스가 아니라 래퍼 자체(또는 원본 재현부)의 문제로 볼");
        sb.AppendLine("수 있다(map-reviewer.md §1 #31 지시). 복원스왑(상승중)은 이 연언지에서 뺐다 — 검문33차 A1의");
        sb.AppendLine("임계 통일 이후 구조적으로 항상 0인 tautology 열이라(상세 근거는 아래 \"[정정 … 34차 R2]\" 문단 참고) 무정보이고,");
        sb.AppendLine("벽상실(상승중)=0 단독만으로 위 결론이 선다.");
        sb.AppendLine("[정정 2026-09-09 #37, map-reviewer 33차 R1] \"복원스왑(상승중)=1인 케이스는 전부 ✗ → D1 기전이");
        sb.AppendLine("실측으로 확정됐다\"던 이전 서술은 역방향 오류였다 — 1805.json 전수 재현: ✗ 케이스는 전부 rsr≥1이지만");
        sb.AppendLine("rsr≥1 20건 중 7건은 통과(네모 ON 통과 전부)라 rsr≥1은 필요조건일 수 있어도 충분조건은 아니다. 실제 핀닝");
        sb.AppendLine("3중 시그니처(maxZ 단 상단 근처 정지+최종x 벽면 고정+rsr≥1)는 [정정 검문34차 정정 — 33차 자신의 목록이");
        sb.AppendLine("누락이었다] 5건이 아니라 6건(세모 0.8 ON a 0.802/2.33·0.8 ON b 0.801/2.33·0.96 ON a 1.056/2.33·");
        sb.AppendLine("1.0 ON a 1.050/2.33·네모 0.5 ON a 0.500/2.70·세모 0.5 ON b 0.461/2.35, rsr 1, 33차 목록에서");
        sb.AppendLine("빠져 있었다). 그럼에도 벽 상실 직후 코요테 유예(0.1초) 때문에 접지가 true라 원본 마찰이 복원되고");
        sb.AppendLine("그 마찰이 남은 상승을 흡수하는 기전 자체는 가능성으로 남아 있어(위 6건이 그 사례), 아래 수정은 유지한다.");
        sb.AppendLine("V3_WallSlip.Tick()에 \"상승 중(Rigidbody 수직속도>0.05)이면 복원하지 않는다\" 조건을 추가했다 —");
        sb.AppendLine("[정정 2026-09-09, map-reviewer 34차 R2] 그런데 검문33차 A1(임계 통일)로 인해 복원스왑(상승중) 열은");
        sb.AppendLine("이제 구조적으로 항상 0이다 — risingNow와 Tick()의 risingFast가 같은 프레임·같은 Rigidbody·같은");
        sb.AppendLine("0.05 임계를 읽어(측정과 Tick 호출 사이 velocity 대입 없음) risingNow=true인 프레임은 항상");
        sb.AppendLine("wantFrictionless=true가 되므로 \"상승 중 무마찰→원본\" 전이 자체가 도달 불가 분기가 됐다. 그래서 이 열");
        sb.AppendLine("만으로는 이 수정 이후에도 D1이 잔존하는지 더는 판정할 수 없다(회귀 감시용으로만 남긴다). 대신");
        sb.AppendLine("복원총횟수(restoreTotal)·최초복원시각(firstRestoreSimTime)·최초복원z(발밑) 3열을 risingNow와");
        sb.AppendLine("무관하게 원본 복원 전수(무마찰→원본으로 바뀐 모든 스왑)로 새로 계측한다 — 이 수정 이후에도 원본");
        sb.AppendLine("복원이 있는지, 있다면 언제·어떤 발밑 높이에서인지를 이 3열로 읽는다");
        sb.AppendLine("(restoreOccurred=false, 즉 restoreTotal=0이면 셋 다 '-'/-1 — [정정 #37, 검문33차 A2] 판별은 이");
        sb.AppendLine("bool 플래그다. [정정 2026-09-09, map-reviewer 34차 R3] 센티널은 float.NaN이 아니라 -1f다 —");
        sb.AppendLine("JsonUtility가 float.NaN을 bare NaN 토큰으로 직렬화해 JSON 스펙을 위반함이 34차 실측으로 확인됐다).");
        // [신규 2026-09-09, map-reviewer 35차 A4] 시각 기준 고지 — 최초복원시각·최초스왑시각은
        // 그 스텝의 Physics.Simulate 이전(스텝 시작) simTime을 기록하고, 착지시각·최고z는
        // Physics.Simulate 이후(스텝 종료) 값을 기록한다(RunCase 루프 구조 확인). 나란히 비교하면
        // 0.02s(FixedDt 1스텝) 계통 차가 있을 수 있다.
        sb.AppendLine("시각 기준: 최초복원시각·최초스왑시각 = 스텝 시작(Simulate 이전) simTime, 착지시각·최고z = 스텝 종료(Simulate 이후) — 나란히 비교 시 0.02s 계통 차.");
        sb.AppendLine();
        sb.AppendLine("| 도형 | 단높이 | OFF-(a) | OFF-(b) | OFF-(c) | ON-(a) | ON-(b) |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var shape in Shapes)
        {
            foreach (float h in StepHeights)
            {
                CaseResult offA = Find(results, shape.goName, h, false, "a");
                CaseResult offB = Find(results, shape.goName, h, false, "b");
                CaseResult offC = Find(results, shape.goName, h, false, "c");
                CaseResult onA = Find(results, shape.goName, h, true, "a");
                CaseResult onB = Find(results, shape.goName, h, true, "b");
                sb.AppendLine($"| {shape.goName} | {h:0.00} | {Cell(offA)} | {Cell(offB)} | {Cell(offC)} | {Cell(onA)} | {Cell(onB)} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 상세 (벽 접촉 프레임 · 접지 프레임 · 점프 시점 접지 여부 · 래퍼 스왑 계측)");
        sb.AppendLine();
        // [신규 2026-09-09, GPT검토패키지_2026-09-09 지적2] "벽앞정체" 열 신설 — 비행통과 바로
        // 옆에 둔다(둘 다 !passed 세분류라 나란히 읽기 위함).
        sb.AppendLine("| 도형 | 단높이 | 래퍼 | 입력 | 통과 | 착지시각 | 비행통과 | 벽앞정체 | 최고z(발밑) | 벽접촉프레임 | 접지프레임 | 점프트리거 | 점프시접지 | 최종x | 스왑횟수 | 최초스왑시각 | tracked | 벽이탈(상승중) | 복원스왑(상승중) | 복원총횟수 | 최초복원시각 | 최초복원z(발밑) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var c in results.cases)
        {
            sb.AppendLine($"| {c.shape} | {c.stepHeight:0.00} | {(c.wrapperOn ? "ON" : "OFF")} | {c.inputMode} | " +
                $"{(c.passed ? "✓" : "✗")} | {(c.landedSimTime < 0f ? "-" : c.landedSimTime.ToString("0.000"))} | " +
                $"{(c.flewOver ? "Y" : "N")} | {(c.stalledAtWall ? "Y" : "N")} | {c.maxDocZ:0.000} | {c.wallContactFrames} | {c.groundedFrames} | " +
                $"{(c.jumpTriggered ? "Y" : "N")} | {(c.jumpAtGroundedTrue ? "Y" : "N")} | {c.finalDocX:0.00} | " +
                $"{c.swapCount} | {(c.firstSwapSimTime < 0f ? "-" : c.firstSwapSimTime.ToString("0.000"))} | " +
                $"{(c.trackedColliders < 0 ? "-" : c.trackedColliders.ToString())} | " +
                // [정정 2026-09-09, map-reviewer 34차 R2] 벽이탈(상승중)·복원스왑(상승중) 두 열은
                // 검문33차 A1 임계 통일 이후 구조적으로 항상 0이다(클래스 상단 restoreSwapWhileRising
                // 필드 주석 참고) — 회귀 감시용으로만 유지, D1 잔존 판정은 아래 3열로 대체한다.
                $"{c.wallLostWhileRising} | {c.restoreSwapWhileRising} | " +
                // [신규 2026-09-09, map-reviewer 34차 R2] risingNow 게이트 밖 전수 계측 — 판별은
                // restoreOccurred(=restoreTotal>0)로 통일한다.
                $"{c.restoreTotal} | " +
                $"{(c.restoreOccurred ? c.firstRestoreSimTime.ToString("0.000") : "-")} | " +
                $"{(c.restoreOccurred ? c.firstRestoreBottomZ.ToString("0.000") : "-")} |");
        }

        return sb.ToString();
    }

    private static string Cell(CaseResult c)
    {
        if (c == null) return "-";
        return (c.passed ? "✓" : "✗") + " z=" + c.maxDocZ.ToString("0.00");
    }
}
#endif
