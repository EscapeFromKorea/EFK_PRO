#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// KitchenMapV3 — T0 블록아웃 빌더 (회색 박스) · 실좌표판 (2026-09-01).
/// 목적: 물리 감사 4종 + T0 측정 4항(P1 착지 폭 1a/1b · V1 5분 예산 · P4 반복 체감 · S1 성능).
///
/// 수치 출처 규약:
///   [실좌표] DATA_KitchenMap_v2.json v3.2 (2026-08-27, 360obj) — 이번 판에서 [추정]을 전부 교체.
///   [검증]   릴레이 확정·검산보고서 실측 — 바꾸지 말 것. 릴레이 재설계분(IN/OUT 선반 2단 Z10.2 ·
///            P5 동반구 배치 · 싱크/덕트/팬트리 중간 턱 · D-a 몰딩)은 실좌표 위에 얹는다.
///   근거 문서: 맵2_V3_릴레이설계/V2실측_검산보고서_8건.md (반영 목록 9항).
/// 좌표: 문서(X=좌우 0~98, Y=앞뒤 0~86 실내·86~110 뒷마당, Z=높이 0~30) → V3.Doc()가 Unity로 변환.
/// </summary>
public static class V3Build
{
    // ── [검증]/[실좌표] 전역 상수 ────────────────────────────────
    const float RoomX = 98f, RoomY = 86f, RoomZ = 30f, YardY = 110f;
    const float CounterTop = 9.6f;                       // 조리대·아일랜드 상판 [실좌표=검증 일치]
    const float TrolleyX0 = 45.8f, TrolleyX1 = 52.2f;    // 카트 상판 [검산 N-2 실측 = JSON Trolley_Top 일치]
    const float WinX0 = 10.2f, WinX1 = 29.8f;            // 창 유리 [실좌표] (개구 벽면은 x10~30)
    const float WinZ0 = 12f, WinZ1 = 20f;                // 창 개구부 [검증=실좌표 일치]
    const float ShelfZ = 10.2f, ShelfDepth = 3.5f;       // IN/OUT 선반 (M-확인-1 확정)
    const float FridgeTop = 20f;                         // 냉장고 상면 [실좌표=검증 일치]
    const float RidgeTop = 22.2f;                        // 상부장 능선 [실좌표=검증 일치]
    const float DuctPad = 24f;                           // 덕트 바닥 상면 [실좌표=검증 일치]

    public static void Clear()
    {
        GameObject root = GameObject.Find(V3.RootName);
        if (root != null) Object.DestroyImmediate(root);
        V3.Log("블록아웃 삭제 완료.");
    }

    public static void BuildAll()
    {
        Clear();
        Shell();
        S1_Dining();
        S2_CounterSink();
        S2_BoxSteps();
        WK_IslandP1();
        S3_RangeHood();
        S4_FridgeWall();
        S6_UpperRidge();
        S7_Duct();
        WindowShelves();
        S5_Pantry();
        S8_Backyard();
        // 질감 + 자연화 + 깃발 체크포인트 자동 체인 — 콜라이더·측정 수치에는 영향 없음.
        V3Materials.Apply();
        V3Dress.Apply();
        V3Checkpoints.Place();
        V3Gimmicks.Wire();          // R2 기믹 배선(DoorSystem+DreamThread 사본) — 상세는 V3_Gimmicks.cs
        V3Dress.SetMarkers(false);   // 측정 마커는 기본 숨김 — 메뉴 9로 토글
        V3.Log("Build All(실좌표판+질감+자연화+체크포인트) 완료 — 다음: 3. Setup Play → ▶ / 4. Audit & T0 Measure.");
    }

    // ── 헬퍼: 콜라이더 없는 시각용 박스 ──────────────────────────
    static GameObject NoColl(GameObject go)
    {
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    /// <summary>중간 턱(🔒N4 표준 — 지형을 깎지 않고 더한다).</summary>
    static void Ledge(GameObject g, string name, float x0, float y0, float x1, float y1, float baseZ, float topZ)
    {
        V3.Box(g, name, x0, y0, baseZ, x1, y1, topZ, "Grain");
    }

    // ── 셸: 바닥·벽(창/덕트구멍/발코니문)·뒷마당 지면 [실좌표 S0] ──
    static void Shell()
    {
        GameObject g = V3.Group("S0_Shell");
        V3.Box(g, "Floor_Indoor", 0, 0, -0.5f, RoomX, RoomY, 0, "GrayLo");
        V3.Box(g, "Floor_Yard", 0, RoomY, -0.5f, RoomX, YardY, 0, "GrayLo");   // 베란다 바닥 z0 [실좌표]
        V3.Box(g, "Wall_S", 0, -1, 0, RoomX, 0, RoomZ, "GrayLo");
        V3.Box(g, "Wall_W", -1, -1, 0, 0, YardY, RoomZ, "GrayLo");
        V3.Box(g, "Wall_E", RoomX, -1, 0, RoomX + 1, YardY, RoomZ, "GrayLo");
        // 북벽 [실좌표 정정 09-06: JSON y85~86, 구 86~87 — FROM_코워크 2026-09-06 결정 E]:
        // 덕트 구멍 x2~8·z24~27 / 창 개구 x10~30·z12~20 / 발코니 개구 x66~82(유리문 잠김)
        V3.Box(g, "Wall_N_A", 0, RoomY - 1, 0, 2, RoomY, RoomZ, "GrayLo");
        V3.Box(g, "Wall_N_B1", 2, RoomY - 1, 0, 8, RoomY, 24, "GrayLo");
        V3.Box(g, "Wall_N_B2", 2, RoomY - 1, 27, 8, RoomY, RoomZ, "GrayLo");
        V3.Box(g, "Wall_N_C", 8, RoomY - 1, 0, 10, RoomY, RoomZ, "GrayLo");
        V3.Box(g, "Wall_N_D1", 10, RoomY - 1, 0, 30, RoomY, WinZ0, "GrayLo");
        V3.Box(g, "Wall_N_D2", 10, RoomY - 1, WinZ1, 30, RoomY, RoomZ, "GrayLo");
        V3.Box(g, "Wall_N_E", 30, RoomY - 1, 0, 66, RoomY, RoomZ, "GrayLo");
        V3.Box(g, "Wall_N_F", 66, RoomY - 1, 21, 82, RoomY, RoomZ, "GrayLo");
        V3.Box(g, "발코니유리문_잠김(통행불가)", 66, RoomY - 1, 0, 82, RoomY, 21, "Smooth");  // S8 진입은 덕트뿐 [실좌표 정정 09-06: JSON y85~86, 구 86~87]
        V3.Box(g, "Wall_N_G", 82, RoomY - 1, 0, RoomX, RoomY, RoomZ, "GrayLo");
        // 창턱 [실좌표: x9.6~30.4 · y84.2~85 · z11.5~12] — "넘는 턱"(통행로 아님)
        V3.Box(g, "WindowSill_창턱12.0(넘는턱)", 9.6f, 84.2f, 11.5f, 30.4f, 85f, WinZ0, "Gray");
        V3.Marker(g, "Ceiling_Z30_마커", 0, 0, RoomZ, 4, 4, RoomZ + 0.2f);
    }

    // ── S1 다이닝(V1) [실좌표] ───────────────────────────────────
    static void S1_Dining()
    {
        GameObject g = V3.Group("S1_Dining_V1");
        // START 매트 = 무게판 Teach(B2) [실좌표 EntryMat x58~70·y3~9 — 스폰 (62~65,6)과 일치]
        V3.Box(g, "Start_Mat_무게판2.75", 58, 3, 0, 70, 9, 0.3f, "Orange");
        NoColl(V3.Box(g, "Rug_러그존(R3 직물×1.8)", 26, 10, 0, 74, 40, 0.12f, "Grain"));  // [실좌표 — 콜라이더 없음]
        // 8인 롱테이블 [실좌표: x30~70 · y18~34 · 상판 7~7.5]
        V3.Box(g, "Table_Top_7.5", 30, 18, 7f, 70, 34, 7.5f, "GrayHi");
        V3.Box(g, "Table_Leg_A", 31, 19, 0, 32.2f, 20.2f, 7f, "Gray");
        V3.Box(g, "Table_Leg_B", 67.8f, 19, 0, 69, 20.2f, 7f, "Gray");
        V3.Box(g, "Table_Leg_C", 31, 31.8f, 0, 32.2f, 33, 7f, "Gray");
        V3.Box(g, "Table_Leg_D", 67.8f, 31.8f, 0, 69, 33, 7f, "Gray");
        Chair(g, "Chair_N1", 38, 44, 34.5f, 40.5f, false);   // 북측 — B3 실 앵커 후보 (구 앞치마 계단이 붙어있던 자리 — 계단은 2026-09-03 #6 삭제, 의자는 유지)
        Chair(g, "Chair_N2", 54, 60, 34.5f, 40.5f, false);
        Chair(g, "Chair_E1", 70.5f, 76.5f, 23, 29, true);
        Chair(g, "Chair_W1", 23.5f, 29.5f, 23, 29, true);
        V3.Box(g, "Sideboard_수납장", 18, 0.5f, 0, 46, 8.5f, 8f, "Gray");     // [실좌표 top 8]
    }

    static void Chair(GameObject g, string name, float x0, float x1, float y0, float y1, bool backEast)
    {
        V3.Box(g, name + "_Seat_4.7", x0, y0, 4.2f, x1, y1, 4.7f, "Grain");
        V3.Box(g, name + "_LegF", x0, y0, 0, x1, y0 + 0.6f, 4.2f, "Gray");
        V3.Box(g, name + "_LegB", x0, y1 - 0.6f, 0, x1, y1, 4.2f, "Gray");
        if (backEast) V3.Box(g, name + "_Back", x1 - 0.6f, y0, 4.7f, x1, y1, 9f, "Gray");
        else V3.Box(g, name + "_Back", x0, y1 - 0.6f, 4.7f, x1, y1, 9f, "Gray");
    }

    // ── S2 북벽(서측) 조리대 + 싱크볼 (V3 무대) [실좌표] ──────────
    static void S2_CounterSink()
    {
        GameObject g = V3.Group("S2_CounterSink");
        V3.Box(g, "Cab_Sink_W", 0.05f, 76, 0, 14, 84.95f, 9f, "Gray");
        V3.Box(g, "Cab_Sink_Mid_7.2", 14, 76, 0, 26, 84.95f, 7.2f, "Gray");   // 볼 바닥 받침 [실좌표]
        V3.Box(g, "Cab_Sink_E", 26, 76, 0, 40, 84.95f, 9f, "Gray");
        V3.Box(g, "Counter_W_L", 0.05f, 74.8f, 9f, 14, 84.95f, CounterTop, "GrayHi");
        V3.Box(g, "Counter_W_R", 26, 74.8f, 9f, 40, 84.95f, CounterTop, "GrayHi");
        V3.Box(g, "Counter_W_Front_싱크앞턱", 14, 74.8f, 9f, 26, 77.5f, CounterTop, "GrayHi");
        V3.Box(g, "Counter_W_Back", 14, 83.5f, 9f, 26, 84.95f, CounterTop, "GrayHi");
        // 싱크볼 [실좌표: x14~26 · y77.5~83.5 · 바닥 7.2~7.4 · 벽 top 9]
        V3.Box(g, "SinkBowl_Floor_7.4", 14, 77.5f, 7.2f, 26, 83.5f, 7.4f, "GrayLo");
        V3.Box(g, "SinkBowl_WallW", 14, 77.5f, 7.4f, 14.4f, 83.5f, 9f, "Gray");
        V3.Box(g, "SinkBowl_WallE", 25.6f, 77.5f, 7.4f, 26, 83.5f, 9f, "Gray");
        V3.Box(g, "SinkBowl_WallS", 14.4f, 77.5f, 7.4f, 25.6f, 77.9f, 9f, "Gray");
        V3.Box(g, "SinkBowl_WallN", 14.4f, 83.1f, 7.4f, 25.6f, 83.5f, 9f, "Gray");
        // 실재 탈출 스텝 [실좌표: 수세미 8.3 · 세제통 9.3] + 중간 턱 2 (🔒N4 · 놀이터 §30-2①)
        V3.Box(g, "Sink_Step_수세미_8.3", 23.6f, 78, 7.4f, 25.4f, 80, 8.3f, "Grain");
        V3.Box(g, "Sink_Step_세제통_9.3", 23.7f, 80.4f, 7.4f, 25.3f, 82.4f, 9.3f, "Grain");
        Ledge(g, "Sink_중간턱1_7.85", 22.2f, 78.2f, 23.6f, 79.8f, 7.4f, 7.85f);
        Ledge(g, "Sink_중간턱2_8.8", 23.7f, 79.9f, 25.3f, 80.5f, 7.4f, 8.8f);
        // 수도꼭지 [실좌표] — ⚠️ IN 선반과의 교차는 WindowShelves() 노치로 처리(신규 확인 V4-폭)
        V3.Box(g, "Faucet_Body", 19, 83.2f, 9.6f, 21, 84.1f, 12.2f, "Gray");
        V3.Box(g, "Faucet_Neck", 19.4f, 80.5f, 12.2f, 20.6f, 84, 12.8f, "Gray");
        V3.Box(g, "Cup_A_머그", 11.2f, 79.2f, 9.6f, 12.4f, 80.4f, 10.6f, "Gray");
        V3.Marker(g, "U3b_배수구마개_무게판2.75", 18.5f, 79.5f, 7.4f, 20.1f, 81.1f, 7.6f);
    }

    // ── S2 수납상자 계단 10단 (V1 복귀·조리대行) [실좌표 — N-16 검산 축] ──
    static void S2_BoxSteps()
    {
        GameObject g = V3.Group("S2_BoxSteps");
        // Box_Step 1~10: x10.4~34.4 · y70.5~74.4 · 단높이 0.96 (단차 ≤1.0 — 필수 경로 기준 통과)
        for (int i = 0; i < 10; i++)
            V3.Box(g, $"Box_Step_{i + 1}", 10.4f + 2.4f * i, 70.5f, 0, 12.8f + 2.4f * i, 74.4f, 0.96f * (i + 1), "Grain");
    }

    // ── WK 아일랜드 + P1 관문 [실좌표 + 릴레이 D-a 몰딩] ──────────
    static void WK_IslandP1()
    {
        GameObject g = V3.Group("WK_Island_P1");
        // 아일랜드 [실좌표: 하부장 x34~62·y58.5~69.5·z0.8~9 (캐스터 위) / 상판 x33.5~62.5·y58~70·z9~9.6]
        V3.Box(g, "Island_Base", 34, 58.5f, 0.8f, 62, 69.5f, 9f, "Gray");
        V3.Box(g, "Island_Caster_1", 34.7f, 59.2f, 0, 36.3f, 60.8f, 0.8f, "Gray");
        V3.Box(g, "Island_Caster_2", 59.7f, 59.2f, 0, 61.3f, 60.8f, 0.8f, "Gray");
        V3.Box(g, "Island_Caster_3", 34.7f, 67.2f, 0, 36.3f, 68.8f, 0.8f, "Gray");
        V3.Box(g, "Island_Caster_4", 59.7f, 67.2f, 0, 61.3f, 68.8f, 0.8f, "Gray");
        V3.Box(g, "Island_Top", 33.5f, 58, 9f, 62.5f, 70, CounterTop, "GrayHi");
        // D-a 몰딩(채널③ 매끈 — "설 자리 없음") [릴레이 확정]: 북쪽 가장자리, 도킹 폭(x45.8~52.2) 밖만
        MoldingEdge(g, "Molding_W", 33.5f, TrolleyX0, 70f);
        MoldingEdge(g, "Molding_E", TrolleyX1, 62.5f, 70f);
        // Trolley [실좌표: 몸체 x46~52·y70~74.7 / 상판 top 8.6] — 갭(y70~74.8)의 다리. T0에선 kinematic 토글
        GameObject trolley = V3.RigidBox(g, "Trolley_카트", 46f, 70f, 0, 52f, 74.7f, 8.6f, 12f); // 질량 [TBD — 기믹 배선 시]
        trolley.GetComponent<Rigidbody>().isKinematic = true;
        V3.Marker(g, "Brake_브레이크판2.75_마커", 56, 60, CounterTop, 59, 63, CounterTop + 0.3f);
        // 하-19 도구 [실좌표]: 아일랜드 도마 — P1 제2 수단(지렛대) 후보. 출발 측 실재 ✅
        GameObject board = V3.RigidBox(g, "IS_Board_도마(하-19·질량TBD)", 36, 60, CounterTop, 43, 66.6f, 10.1f, 1f);
        board.GetComponent<Rigidbody>().isKinematic = true;   // T0에서는 정적 — 기믹 배선 시 해제
        // 아일랜드行 수납상자 계단 10단 [실좌표: x38~46 · y48~58 · 단높이 0.96] — 바닥→아일랜드 직접 재등반
        for (int i = 0; i < 10; i++)
            V3.Box(g, $"Crate_Step_{i + 1}", 38, 48 + i, 0, 46, 49 + i, 0.96f * (i + 1), "Grain");
        // 주변 실재물 [실좌표] — N-16 서측 회랑(x13.5~34)은 비워 둔다(검산 확정)
        V3.Box(g, "Stool_1", 50, 52.6f, 0, 54, 56.6f, 6f, "Gray");
        V3.Box(g, "Stool_2", 56, 52.6f, 0, 60, 56.6f, 6f, "Gray");
        V3.Box(g, "Wagon_왜건", 74, 64, 0, 80, 70, 5f, "Gray");
        V3.Box(g, "Humidifier", 80, 56, 0, 83, 59, 3f, "Gray");
        V3.Box(g, "Plant2_Pot_고정(질량3.0→static)", 76, 46, 0, 82, 52, 4f, "Smooth");  // [실좌표 z0~4]
    }

    static void MoldingEdge(GameObject g, string name, float x0, float x1, float yEdge)
    {
        if (x1 - x0 < 0.1f) return;
        GameObject go = V3.Box(g, name + "_몰딩(매끈)", x0, yEdge - 1.75f, 9.1f, x1, yEdge, CounterTop - 0.05f, "Smooth");
        go.transform.rotation = Quaternion.Euler(-35f, 0, 0);   // 북쪽으로 굴러떨어지는 경사 [블록아웃 근사]
    }

    // ── S3 북벽(동측) 레인지·후드 (P2) [실좌표] ───────────────────
    static void S3_RangeHood()
    {
        GameObject g = V3.Group("S3_RangeHood_P2");
        V3.Box(g, "Cab_Stove_Base", 40, 76, 0, 83.95f, 84.95f, 9f, "Gray");
        V3.Box(g, "Counter_E", 40, 74.8f, 9f, 83.95f, 84.95f, CounterTop, "GrayHi");
        V3.Box(g, "Stove_Frame", 56, 75.5f, CounterTop, 76, 84.5f, 10f, "Gray");
        V3.Box(g, "Burner_1", 57.8f, 76.2f, 10f, 62.2f, 79.8f, 10.12f, "Orange");
        V3.Box(g, "Burner_2", 69.8f, 76.2f, 10f, 74.2f, 79.8f, 10.12f, "Orange");
        V3.Box(g, "Burner_3", 57.8f, 80.2f, 10f, 62.2f, 83.8f, 10.12f, "Orange");
        V3.Box(g, "Burner_4", 69.8f, 80.2f, 10f, 74.2f, 83.8f, 10.12f, "Orange");
        V3.Marker(g, "VOL_화력피해_마커", 56, 75.5f, 10.1f, 76, 84.5f, 13f);
        V3.Box(g, "Pot_Big_큰냄비", 58.4f, 76.6f, 10.12f, 63.6f, 81.8f, 13.32f, "Gray");
        V3.Box(g, "Pan_Fry", 70, 76.4f, 10.12f, 74.4f, 80.8f, 11.32f, "Gray");
        // 동측 코너 등반 체인 [실좌표: 접시 10.3 → 트레이 11.2 → 커피머신 11.9 → 선반/전자레인지 14.9]
        V3.Box(g, "PlateStack_C_10.3", 77.6f, 75.6f, CounterTop, 80.8f, 78.8f, 10.3f, "Grain");
        V3.Box(g, "Tray_Corner_11.2", 79.6f, 75.4f, 10.3f, 83.4f, 79.6f, 11.2f, "Grain");
        V3.Box(g, "CoffeeMachine_11.9", 79.8f, 80, CounterTop, 83.6f, 84.4f, 11.9f, "Gray");
        V3.Box(g, "Shelf_MW_벽선반", 73, 80.5f, 11.5f, 79.6f, 84.95f, 11.9f, "Grain");
        V3.Box(g, "Microwave_14.9", 73.5f, 80.7f, 11.9f, 79.5f, 84.9f, 14.9f, "Gray");
        // 후드 [실좌표: 캐노피 x58.05~74 · z17~19 (상단 19 = 검증 일치) · 연통 19~23.6]
        V3.Box(g, "Hood_Canopy_19", 58.05f, 76.5f, 17f, 74, 84.95f, 19f, "Smooth");
        V3.Box(g, "Hood_Chimney", 62, 78.5f, 19f, 70, 84.5f, 23.6f, "Smooth");
        // 후드 위 상자 계단 [실좌표: 19→19.9→20.9→21.9 → 상부장 C 22.2]
        V3.Box(g, "HoodBox_1_19.9", 70.2f, 79, 19f, 74, 84, 19.9f, "Grain");
        V3.Box(g, "HoodBox_2_20.9", 70.4f, 79.5f, 19.9f, 74, 83.5f, 20.9f, "Grain");
        V3.Box(g, "HoodBox_3_21.9", 70.6f, 80, 20.9f, 74, 83.5f, 21.9f, "Grain");
        V3.Box(g, "Cab_Upper_C_22.2", 74, 79, 16f, 84, 84.95f, RidgeTop, "Gray");
        // P2 릴레이 장치 마커: 실 게이트(3.0 — 네모 거부) = 후드行 진입부 / 물컵 선반판(무게판 2.75)
        V3.Marker(g, "P2_실게이트3.0_마커(후드 진입부)", 74.5f, 79.5f, 14.9f, 77.5f, 80.5f, 16.4f);
        V3.Marker(g, "WaterCup_선반판2.75_마커", 51, 76, CounterTop, 54, 79, CounterTop + 0.3f);
    }

    // ── S4 동벽 냉장고·키큰장 + 덕트行 계단 [실좌표 + 🔒N4 중간 턱 5] ──
    static void S4_FridgeWall()
    {
        GameObject g = V3.Group("S4_FridgeWall");
        V3.Box(g, "Broom_Cabinet", 89.5f, 2.2f, 0, 97.95f, 6f, 19.6f, "Gray");
        V3.Box(g, "Tall_Oven_Tower", 89.5f, 6f, 0, 97.95f, 26f, 19.6f, "Gray");
        V3.Box(g, "Filler_OvenKimchi", 89.5f, 26f, 0, 97.95f, 28f, 19.6f, "Gray");
        V3.Box(g, "Kimchi_Fridge_16", 89.5f, 28f, 0, 97.95f, 46f, 16f, "Gray");
        V3.Box(g, "Fridge_Panel_S", 89.5f, 46f, 0, 97.95f, 48f, FridgeTop, "Gray");
        V3.Box(g, "Fridge_Main_20", 89.5f, 48f, 0, 97.95f, 62f, FridgeTop, "Gray");    // 협곡 착지점 [실좌표=검증]
        V3.Box(g, "Fridge_Panel_N", 89.5f, 62f, 0, 97.95f, 63f, FridgeTop, "Gray");
        V3.Box(g, "Cab_FridgeSide", 89.5f, 63f, 0, 97.95f, 74.75f, FridgeTop, "Gray"); // 위가 덕트行 계단 시작
        // 덕트行 계단 [실좌표: 20→21→22→23, 단높이 1.0] + 중간 턱 5 (🔒N4 표준 승격 — 덕트·싱크볼·팬트리)
        Ledge(g, "Duct_중간턱0_20.5", 91, 62.2f, 97, 63, FridgeTop, 20.5f);
        V3.Box(g, "Duct_Step_1_21", 91, 63, FridgeTop, 97, 66.2f, 21f, "Grain");
        Ledge(g, "Duct_중간턱1_21.5", 91, 66.2f, 97, 67.2f, FridgeTop, 21.5f);
        V3.Box(g, "Duct_Step_2_22", 91, 67.2f, FridgeTop, 97, 70.4f, 22f, "Grain");
        Ledge(g, "Duct_중간턱2_22.5", 91, 70.4f, 97, 71.4f, FridgeTop, 22.5f);
        V3.Box(g, "Duct_Step_3_23", 91, 71.4f, FridgeTop, 97, 74.6f, 23f, "Grain");
        Ledge(g, "Duct_중간턱3_23.5", 91, 74.75f, 96, 76f, 23f, 23.5f);   // 계단3 → 동측 패드(24)
        // 코너 상판·선반 체인 [실좌표: 코너 상판 9.6 → 바구니 10.6 → 선반1 12.9 → 선반2 13.9(CP40)]
        V3.Box(g, "Cab_Corner", 84, 74.8f, 0, 97.95f, 84.95f, 9f, "Gray");
        V3.Box(g, "Counter_Corner", 84, 76, 9f, 97.95f, 84.95f, CounterTop, "GrayHi");
        V3.Box(g, "Basket_Corner_10.6", 85, 75.2f, CounterTop, 89, 79.4f, 10.6f, "Grain");
        V3.Box(g, "Shelf_C1_12.9", 84.5f, 84, 12.6f, 96, 84.95f, 12.9f, "Grain");
        V3.Box(g, "Shelf_C2_13.9", 88, 84, 13.6f, 96, 84.95f, 13.9f, "Grain");         // CP40 — C1 서쪽 끝에서 점프
        V3.Box(g, "Cab_Upper_E", 89.5f, 74.8f, 16f, 97.95f, 84.95f, RidgeTop, "Gray");
        V3.Box(g, "Recycle_분리수거함3", 85.9f, 8f, 0, 89.3f, 19.4f, 4.6f, "Gray");
        V3.Marker(g, "CP40_코너선반_마커", 89.5f, 82.8f, 13.9f, 92.5f, 85.8f, 16.9f);
    }

    // ── S6 상부장 능선 (P4 출발) [실좌표] ─────────────────────────
    static void S6_UpperRidge()
    {
        GameObject g = V3.Group("S6_Ridge_P4");
        V3.Box(g, "Cab_Upper_A_22.2", 0.05f, 74.8f, 16f, 9f, 84.95f, RidgeTop, "Gray");
        V3.Box(g, "Cab_Upper_B_22.2", 31f, 79f, 16f, 58f, 84.95f, RidgeTop, "Gray");   // 동쪽 끝 x58 = 스윙 시작
        V3.Box(g, "CurtainBox_21.2", 9.5f, 84f, 20.2f, 30.5f, 84.95f, 21.2f, "Gray");  // A 22.2 ↓ 21.2 ↑ B 22.2
        GameObject basket = V3.RigidBox(g, "Ridge_Basket", 40, 80, RidgeTop, 46, 84.6f, 23.3f, 0.7f);
        basket.GetComponent<Rigidbody>().isKinematic = false;
        GameObject box = V3.RigidBox(g, "Ridge_Box", 2, 76, RidgeTop, 7, 82, 23.4f, 0.6f);
        box.GetComponent<Rigidbody>().isKinematic = false;
        V3.Marker(g, "P4_출발_능선동단(CP60)", 54.5f, 80.5f, RidgeTop, 57.5f, 83.5f, RidgeTop + 3f);
        V3.Marker(g, "P4_도착_냉장고위(CP62)", 92, 53.5f, FridgeTop, 95, 56.5f, FridgeTop + 3f);
    }

    // ── S7 천장 스윙 고리 + 덕트 [실좌표] ─────────────────────────
    static void S7_Duct()
    {
        GameObject g = V3.Group("S7_SwingDuct");
        // 스윙 고리 6개 [실좌표: 능선 동단(59,77) → 냉장고(84,53) 대각 · z23.8~24.6] — 주황 앵커(콜라이더 없음)
        float[,] rings = {
            { 59.2f, 60.8f, 76.7f, 78.3f }, { 63.7f, 65.3f, 73.7f, 75.3f }, { 68.2f, 69.8f, 67.7f, 69.3f },
            { 72.7f, 74.3f, 61.7f, 63.3f }, { 77.2f, 78.8f, 56.2f, 57.8f }, { 82.7f, 84.3f, 52.7f, 54.3f } };
        for (int i = 0; i < 6; i++)
            NoColl(V3.Box(g, $"SwingRing_{i + 1}_z24", rings[i, 0], rings[i, 2], 23.8f, rings[i, 1], rings[i, 3], 24.6f, "Orange"));
        // 덕트 [실좌표]: 동측 패드(진입·그릴) → 주 덕트 바닥(top 24) → 배출 패드 → 벽 구멍(x2~8·z24~27) → S8
        V3.Box(g, "Duct_East_Pad", 88, 76, 23.6f, 96, 84.5f, DuctPad, "Smooth");
        V3.Box(g, "Duct_Floor", 8, 79.5f, 23.6f, 88, 84.5f, DuctPad, "Smooth");
        V3.Box(g, "Duct_Exit_Pad", 2, 79.5f, 23.6f, 8, 85, DuctPad, "Smooth");
        V3.Box(g, "Duct_Rail_N", 8, 84.5f, DuctPad, 96, 85, 25.6f, "Smooth");
        V3.Box(g, "Duct_Rail_S", 8, 79.5f, DuctPad, 86, 80, 25.6f, "Smooth");
        V3.Marker(g, "Duct_그릴_래치(주황·안팎걸쇠·패드24)", 90, 80, DuctPad, 94, 84, 25.5f);
        // 편도성 [🔒L2]: 배출구(z24)에서 뒷마당으로 낙하 — 마당에서 되오를 수 없다(기하로 성립)
    }

    // ── 창 IN/OUT 선반 (V4 · M-확인-1 + N-19·20 반영) ─────────────
    static void WindowShelves()
    {
        GameObject g = V3.Group("V4_WindowShelves");
        // IN 선반(실내) z10.2 · 깊이 3.5 [릴레이 확정] — 수도꼭지(x19~21) 노치 분할
        V3.Box(g, "Shelf_IN_전면부", WinX0, 81.2f, ShelfZ - 0.3f, WinX1, 83.0f, ShelfZ, "Grain");
        V3.Box(g, "Shelf_IN_후면W", WinX0, 83.0f, ShelfZ - 0.3f, 18.6f, 84.7f, ShelfZ, "Grain");
        V3.Box(g, "Shelf_IN_후면E", 21.4f, 83.0f, ShelfZ - 0.3f, WinX1, 84.7f, ShelfZ, "Grain");
        V3.Marker(g, "V4폭_신규확인_수도꼭지구간(전면 발판 1.8<3.0)", 18.6f, 81.2f, ShelfZ, 21.4f, 83f, ShelfZ + 1f);
        // IN 선반 → 창턱(11.5~12): 1.8 = 3단 × 0.6 [검증]
        V3.Stairs(g, "Shelf_IN_창턱계단", 'y', 24f, 82.4f, ShelfZ, WinZ0, 3, 0.6f, 2.5f);
        // OUT 선반(실외) z10.2 · 깊이 3.5 — N-19·N-20 검산 반영: x18~29.8 (세탁기·투석 궤도 회피)
        V3.Box(g, "Shelf_OUT_낙하받이(x18~29.8)", 18f, RoomY, ShelfZ - 0.3f, WinX1, RoomY + ShelfDepth, ShelfZ, "Grain");
        V3.Box(g, "Shelf_OUT_난간(매끈)", 18f, RoomY + ShelfDepth, ShelfZ, WinX1, RoomY + ShelfDepth + 0.4f, ShelfZ + 1.2f, "Smooth");
        // OUT → 창턱 복귀: 1.8 = 3단 × 0.6 [검증] — 벽 쪽(-y)으로 오른다
        V3.Stairs(g, "Shelf_OUT_복귀계단", 'y', 21f, RoomY + 1.9f, ShelfZ, WinZ0, 3, -0.6f, 2.5f);
        // 화분 3종(IN 선반 위 — 상면 차 ≤0.8 · 서측 세그먼트) [검증 규칙]
        V3.Box(g, "Pot_A", WinX0 + 0.6f, 83.1f, ShelfZ, WinX0 + 3.6f, 84.6f, ShelfZ + 0.7f, "Grain");
        V3.Box(g, "Pot_B", WinX0 + 4.4f, 83.1f, ShelfZ, WinX0 + 7.4f, 84.6f, ShelfZ + 1.3f, "Grain");
        V3.Marker(g, "CP5_IN선반_마커", 14f, 81.6f, ShelfZ, 17f, 84.4f, ShelfZ + 3f);
    }

    // ── S5 서벽 팬트리 (🔒N7: 관문 0 — 개방 선반이라 자연 충족) [실좌표 + 🔒N4 중간 턱 15 + 잔여 4 플래그] ──
    static void S5_Pantry()
    {
        GameObject g = V3.Group("S5_Pantry");
        V3.Box(g, "Pantry_Back", 0.05f, 6f, 0, 0.7f, 66f, 19.2f, "Gray");
        // 측판 6 [실좌표: 베이 A(y6.6~25.4) · B(26.6~45.4) · C(46.6~65.4)]
        float[,] sides = { { 6f, 6.6f }, { 25.4f, 26f }, { 26f, 26.6f }, { 45.4f, 46f }, { 46f, 46.6f }, { 65.4f, 66f } };
        for (int i = 0; i < 6; i++)
            V3.Box(g, $"Pantry_Side_{i + 1}", 0.7f, sides[i, 0], 0, 13.5f, sides[i, 1], 19.2f, "Gray");
        // 선반 4단 × 3베이 [실좌표: top 4.8 / 9.6 / 14.4 / 19.2]
        float[,] bays = { { 6.6f, 25.4f }, { 26.6f, 45.4f }, { 46.6f, 65.4f } };
        float[] shelfTops = { 4.8f, 9.6f, 14.4f, 19.2f };
        for (int b = 0; b < 3; b++)
            for (int s = 0; s < 4; s++)
                V3.Box(g, $"Pantry_{(char)('A' + b)}_Shelf_{s + 1}", 0.7f, bays[b, 0], shelfTops[s] - 0.5f, 13.5f, bays[b, 1], shelfTops[s], "Grain");
        // 식료품 계단 G0~G3 [실좌표 — 베이 B] + 중간 턱(N-18 처리: 배치 가능 15개소)
        // G0 (바닥→선반1): 쌀1 1.0 / 쌀2 2.0 / 김치통 3.0 / 생수 3.8
        V3.Box(g, "G0_Rice1", 1.2f, 27.2f, 0, 4.6f, 31.2f, 1f, "Grain");
        V3.Box(g, "G0_Rice2", 1.2f, 31.6f, 0, 4.4f, 35.4f, 2f, "Grain");
        V3.Box(g, "G0_Box1", 1.4f, 35.8f, 0, 4.2f, 39.4f, 3f, "Grain");
        V3.Box(g, "G0_Water", 1.4f, 39.8f, 0, 4.4f, 43.6f, 3.8f, "Grain");
        Ledge(g, "G0_턱_진입_0.5", 4.6f, 27.4f, 5.2f, 31f, 0, 0.5f);
        Ledge(g, "G0_턱_1.5", 1.5f, 31.1f, 4.4f, 31.7f, 0, 1.5f);
        Ledge(g, "G0_턱_2.5", 1.5f, 35.3f, 4.2f, 35.9f, 0, 2.5f);
        // G1 (선반1 위): 캔 5.8 / 잼병 6.8 / 시리얼 7.8 / 밀가루 8.7
        V3.Box(g, "G1_CanBox", 2, 28, 4.8f, 5.2f, 32, 5.8f, "Grain");
        V3.Box(g, "G1_Jars", 2, 32.4f, 4.8f, 5, 36.2f, 6.8f, "Grain");
        V3.Box(g, "G1_Cereal", 2.2f, 36.6f, 4.8f, 5, 40.2f, 7.8f, "Grain");
        V3.Box(g, "G1_FlourBag", 2.2f, 40.6f, 4.8f, 5.2f, 44.4f, 8.7f, "Grain");
        Ledge(g, "G1_턱_진입_5.3", 5.2f, 28.2f, 5.8f, 31.8f, 4.8f, 5.3f);
        Ledge(g, "G1_턱_6.3", 2, 32.05f, 5, 32.55f, 4.8f, 6.3f);
        Ledge(g, "G1_턱_7.3", 2.2f, 36.25f, 5, 36.75f, 4.8f, 7.3f);
        Ledge(g, "G1_턱_8.25", 2.2f, 40.25f, 5, 40.75f, 4.8f, 8.25f);
        // G2 (선반2 위): 파스타 10.6 / 기름궤짝 11.6 / 통조림탑 12.6 / 과자 13.5
        V3.Box(g, "G2_PastaBox", 2, 28, 9.6f, 5.2f, 31.8f, 10.6f, "Grain");
        V3.Box(g, "G2_OilCrate", 2, 32.2f, 9.6f, 5, 36, 11.6f, "Grain");
        V3.Box(g, "G2_TinStack", 2.2f, 36.4f, 9.6f, 5, 40, 12.6f, "Grain");
        V3.Box(g, "G2_SnackBox", 2.2f, 40.4f, 9.6f, 5.2f, 44.2f, 13.5f, "Grain");
        Ledge(g, "G2_턱_진입_10.1", 5.2f, 28.2f, 5.8f, 31.6f, 9.6f, 10.1f);
        Ledge(g, "G2_턱_11.1", 2, 31.85f, 5, 32.35f, 9.6f, 11.1f);
        Ledge(g, "G2_턱_12.1", 2.2f, 36.05f, 5, 36.55f, 9.6f, 12.1f);
        Ledge(g, "G2_턱_13.05", 2.2f, 40.05f, 5, 40.55f, 9.6f, 13.05f);
        // G3 (선반3 위): 믹서 15.4 / 냄비세트 16.4 / 찜기 17.4 / 대형박스 18.3
        V3.Box(g, "G3_Blender", 2, 28, 14.4f, 5, 31.6f, 15.4f, "Grain");
        V3.Box(g, "G3_PotSet", 2, 32, 14.4f, 5.2f, 35.8f, 16.4f, "Grain");
        V3.Box(g, "G3_Cooker", 2.2f, 36.2f, 14.4f, 5.2f, 39.8f, 17.4f, "Grain");
        V3.Box(g, "G3_BigBox", 2.2f, 40.2f, 14.4f, 5.4f, 44.2f, 18.3f, "Grain");
        Ledge(g, "G3_턱_진입_14.9", 5f, 28.2f, 5.6f, 31.4f, 14.4f, 14.9f);
        Ledge(g, "G3_턱_15.9", 2, 31.75f, 5, 32.25f, 14.4f, 15.9f);
        Ledge(g, "G3_턱_16.9", 2.2f, 35.85f, 5, 36.35f, 14.4f, 16.9f);
        Ledge(g, "G3_턱_17.85", 2.2f, 40.05f, 5, 40.55f, 14.4f, 17.85f);
        // ⚠️ 신규 확인 N-21: 각 층 마지막 아이템 → 상부 선반 이행부 4곳은 선반 슬래브가 머리 위(헤드룸 0.4~0.5).
        //    중간 턱을 놓을 수 없다 — 도형 통과 가능 여부는 T0 실측 몫(불가 시 릴레이 판정 필요).
        V3.Marker(g, "N21_이행부4곳_T0실측_마커", 1.2f, 42f, 3.8f, 5.4f, 44.4f, 19.2f);
        // 팬트리 꼭대기·코너 [실좌표]
        V3.Box(g, "G4_TopBox_19.9", 2, 61.5f, 19.2f, 5, 65.4f, 19.9f, "Grain");
        V3.Box(g, "Tall_Corner_Cabinet_20.7", 0.05f, 66, 0, 10, 74.75f, 20.7f, "Gray");
        V3.Box(g, "G5_TopBox2_21.4", 1, 70, 20.7f, 4, 74, 21.4f, "Grain");
        V3.Box(g, "BigPlant_Pot_고정(질량3.0→static)", 4, 2.2f, 0, 7.6f, 5.8f, 4f, "Smooth");
        V3.Marker(g, "CP50_팬트리꼭대기_마커", 5.5f, 34.5f, 19.2f, 8.5f, 37.5f, 22.2f);
    }

    // ── S8 뒷마당 (P3 무대 · P5 GOAL) [실좌표 + 하-15′ 동반구 배치안] ──
    static void S8_Backyard()
    {
        GameObject g = V3.Group("S8_Backyard_P5");
        // 난간·게이트 [실좌표: 게이트 x44~54 · h12 · 열림 0/1 (하-15′c ✅)]
        V3.Box(g, "Rail_Side_L", 0, 86.02f, 0, 1, 109.98f, 4f, "Smooth");
        V3.Box(g, "Rail_Side_R", 97, 86.02f, 0, 98, 109.98f, 4f, "Smooth");
        V3.Box(g, "Rail_Out_L", 1, 108, 0, 44, YardY, 4f, "Smooth");
        V3.Box(g, "Rail_Out_R", 54, 108, 0, 97, YardY, 4f, "Smooth");
        V3.Box(g, "GOAL_Gate_닫힘(0/1·무변화)", 44, 108, 0, 54, YardY, 12f, "Smooth");
        // P5 판 3 [하-15′ 검산 배치안 — 6쌍 비가시·게이트 3방 가시 통과 / 하제 확인 1줄 대기]
        V3.Box(g, "PlateA_무게판2.75_네모(실좌표)", 46, 104, 0, 52, 108, 0.4f, "Orange");
        V3.Box(g, "PlateB_실게이트너머(배치안)", 64.5f, 103.5f, 0, 68.5f, 107.5f, 0.4f, "Orange");
        V3.Box(g, "PlateC_실게이트너머(배치안)", 80.5f, 95.5f, 0, 83.5f, 99f, 0.4f, "Orange");
        // 마커 이동(coord-auditor 09-05, 하18_P5카메라_산출보고서 말미 회신): 구 x64.5~68.5 y99.5~100.5 → 신 x68~71 y99.5~100.5(Jangdok_3 동단67·4 서단72 자연갭5.0의 중앙, 폭 4.0→3.0(공통규칙 §3 실 게이트 3.0 부합) — 구위치는 62.5%가 Jangdok_3 솔리드 내부)
        V3.Marker(g, "PlateB_실게이트3.0_마커(배치안·이동 09-05)", 68f, 99.5f, 0, 71f, 100.5f, 3f);
        // 마커 이동(coord-auditor 09-05): 구 x80.5~83.5 y92.5~93.5 → 신 x80.5~83.5 y100~101(판C x80.5~83.5 y95.5~99 북측 진입부 — 구위치는 100% AirCon_실외기(x80~94 y88~95) 내부, 남측은 실외기·Jangdok_4가 (80,94~95)에서 맞닿아 지상 통행 불가)
        V3.Marker(g, "PlateC_실게이트3.0_마커(배치안·이동 09-05)", 80.5f, 100f, 0, 83.5f, 101f, 3f);
        // 차폐물 = 실재 오브젝트 [하-16 ✅ — 신설 0 · 🔒H6 (a) 고정 · 채널③ 매끈]
        V3.Box(g, "Bench_평상_4.3", 26, 103, 0, 44, 107.5f, 4.3f, "Smooth");
        V3.Box(g, "YardBox_1_2.0", 56, 104, 0, 59.7f, 108, 2f, "Smooth");
        V3.Box(g, "YardBox_2_3.5", 60, 104, 0, 63.7f, 108, 3.5f, "Smooth");
        V3.Box(g, "YardBox_3_2.5", 41.7f, 94.5f, 0, 45.4f, 98.5f, 2.5f, "Smooth");
        V3.Box(g, "YardBox_4_4.5", 54.6f, 95.17f, 0, 58.3f, 99.17f, 4.5f, "Smooth");
        for (int i = 0; i < 5; i++)   // 장독 5 [실좌표: y94~102 · h7.4(뚜껑 포함) · 간격 5]
            V3.Box(g, $"Jangdok_{i}_7.4", 20 + 13 * i, 94, 0, 28 + 13 * i, 102, 7.4f, "Smooth");
        V3.Box(g, "Jangdok_FoldPlanks_판(기믹:펼침)", 20, 103, 4.6f, 76, 106, 5f, "Gray");
        V3.Box(g, "Storage_Shed_창고_10", 84, 98, 0, 96, 108, 10f, "Smooth");
        V3.Box(g, "Shed_Roof", 83.5f, 97.5f, 10f, 96.5f, 108.5f, 10.5f, "Smooth");
        V3.Box(g, "WaterTank_물탱크_8", 40, 86.6f, 0, 47, 93.6f, 8f, "Smooth");
        V3.Box(g, "WashingMachine_세탁기_9.3", 6, 88, 0, 18, 95, 9.3f, "Smooth");
        V3.Box(g, "AirCon_실외기_7.3", 80, 88, 0, 94, 95, 7.3f, "Smooth");
        V3.Box(g, "YardStep_1", 49, 88, 0, 55, 92, 2f, "Grain");
        V3.Box(g, "YardStep_2", 55, 88, 0, 61, 92, 3f, "Grain");
        // P3 무대 [실좌표 — 차폐물로 계수 금지(하-16 제외 규칙)]
        V3.Box(g, "DryRack_건조대받침", 6, 100, 0, 20, 108, 0.4f, "Gray");
        for (int i = 0; i < 4; i++)
            V3.Box(g, $"DryRack_봉_{i + 1}", 6, 101 + 1.8f * i, 0.4f, 20, 101.6f + 1.8f * i, 7f, "Gray");
        V3.Box(g, "Line_Pole_L", 3.4f, 86.6f, 0, 5.4f, 88.6f, 12.3f, "Smooth");
        V3.Box(g, "Line_Pole_R", 87, 95.5f, 0, 89, 97.5f, 12.3f, "Smooth");
        NoColl(V3.Box(g, "Clothesline_빨랫줄(기믹:네모시 1.65 처짐)", 10, 96, 12f, 86.9f, 97, 12.3f, "Gray"));
        V3.Marker(g, "Catapult_건조대_자리(방향고정:창틀·서측)", 6, 100, 0, 20, 108, 6f);
        // 수도·물통 [실좌표 하제 #7 ✅ + 검증 질량]
        V3.Box(g, "Faucet_Out_수도", 2, 90, 0, 4, 92, 5f, "Gray");
        // x4.4~6.2 → 4.2~6.0 (0.2U 서쪽 이동, FROM_코워크 2026-09-06 결정 4 — 세탁기 겹침 0.2 보정, JSON에 물통 없음이라 [실좌표] 아님)
        GameObject bucket = V3.RigidBox(g, "Bucket_물통(빈2.70)[하제#7 배치·세탁기 겹침 0.2 보정]", 4.2f, 90, 0, 6.0f, 91.8f, 2.2f, 2.70f);
        bucket.GetComponent<Rigidbody>().isKinematic = false;
        // 소품 [실좌표]
        V3.Box(g, "SmallPot_1", 22, 88, 0, 26, 92, 3f, "Smooth");
        V3.Box(g, "SmallPot_2", 35, 88, 0, 39, 92, 3f, "Smooth");
        V3.Box(g, "SmallPot_3", 76.69f, 102.92f, 0, 80.69f, 106.92f, 3f, "Smooth");
        V3.Box(g, "SmallPot_4", 61, 88, 0, 65, 92, 3f, "Smooth");
        V3.Box(g, "SmallPot_5", 74, 88, 0, 78, 92, 3f, "Smooth");
        V3.Box(g, "YardPlant_1_Pot", 66.6f, 87.4f, 0, 71.8f, 92.6f, 3.6f, "Smooth");
        V3.Box(g, "YardPlant_2_Pot", 1.5f, 95.9f, 0, 5.9f, 100.3f, 3.15f, "Smooth");
        V3.Box(g, "FarSide_LeverPad", 52.1f, 103.28f, 0, 55.9f, 107.28f, 0.35f, "Orange");
    }
}
#endif
