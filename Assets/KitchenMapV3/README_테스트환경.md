# KitchenMapV3 — 테스트 환경 안내 (초안)

> 대상: 김민섭 · 박진수 · 이경준 (팀원, 처음 열어보는 전제)
> 작성: map-builder(소넷) · 2026-09-10(36차 반영판) · 이 문서는 **초안**이다 — 챕터별 테스트 콘솔은 다음 판에 들어간다.
> 문의처: 맵2(부엌맵 V3 블록아웃) 담당 — 송원석.

## 0. 열기 전에

- **Unity 2022.3.62f3 고정.** 다른 버전으로 열지 마라(`ProjectSettings/ProjectVersion.txt` 참고).
- `Assets/KitchenMapV3`는 **신규 추가분**이다. 팀 코드(PlayerSystem·RespawnSystem 등 기존 폴더)는
  이 작업에서 수정하지 않는다 — 전부 읽기 전용 사본으로 다룬다.
- **예외 1건: `Build All`을 누르면 DoorSystem·DreamThreadSystem의 `.mat`이 다시 저장될 수 있다**
  (`DreamThreadMenuItem.LoadOrCreateMaterial`이 기존 에셋에도 무조건 `EditorUtility.SetDirty`+
  `AssetDatabase.SaveAssets()`를 호출한다 — map-reviewer 37차 R4 실측, 우리 클론에서
  `DreamThread_Anchor_Mat.mat`의 셰이더가 실제로 바뀐 사례 확인됨) — **그 변경은 커밋하지 마라.**
- **이 사본의 검증 기준은 2026-08-10 이전판이다(map-reviewer 36차-b 실측).** develop의 최신판
  (origin/develop, PR #82 반영분)과는 PlayerMover·PlayerFollowCamera·PlayerGroundContact 등
  12개 파일이 다르다 — 컴파일 호환은 확인됐지만(같은 실측), 그 위에서 한 T0 물리 측정은
  **평지(바닥 법선 y=1, 축정렬)·기본 스케일(ScalePad로 확대·축소하지 않은 상태)·정적 지형(동적
  Rigidbody 발판 위가 아닌 상태)**에 한정된다. 경사면(구·세모만 해당, 네모는 무게 임계로 제외)·
  확대축소·동적 발판 위에서의 동작은 이 사본으로도, develop 최신판으로도 별도 검증이 안 됐다.
- **진수의 `SampleScene.unity`는 저장하지 말 것.** 테스트는 자기 씬을 새로 만들어 거기서 진행해라
  (씬 전용 강제는 다음 판에 들어간다 — 지금은 각자 주의). 아래 §2에 씬 저장 관련 사실을 더 적었다.

## 1. 메뉴 절차 (`Tools/KitchenMapV3/...`, 메뉴 문자열은 코드 grep으로 대조)

1. **`1. Clear Blockout`** — 이전 블록아웃 삭제.
2. **`2. Build All (T0 블록아웃+질감+자연화)`** — 회색 박스 전체 생성. 내부에서 자동으로 체인된다:
   질감(`6. Apply Kitchen Materials (질감)`) → 자연화(`7. Naturalize (자연화)`) → T0 보조 리스폰
   깃발(`8. Place T0RS Flags (T0 보조 리스폰 깃발)`) → 기믹 배선(`2b. Wire Gimmicks
   (DoorSystem+DreamThread)`, 아래 §3 참고). 투석기(`2c`)는 이 체인 밖이다(아래 참고).
3. **`3. Setup Play (도형 3종 + 카메라)`** — 정식 생성기로 구/세모/네모 캐릭터 + 카메라 배치.
4. **▶ 플레이** — WASD/Space, Tab으로 도형 전환. 우리 카메라(V3ThirdPersonCamera) 조작은 우클릭
   드래그(시점 회전)·Q/E(좌우 회전)·Z/C(상하 회전)·V(시점 리셋)·휠(줌)이다.
5. **`4. Audit + T0 Measure (콘솔 리포트)`** — 물리 겹침 감사 + T0 측정 리포트를 콘솔에 출력.
6. **`8. Place T0RS Flags`**는 위 2번 안에 이미 포함되어 있다 — 이것은 **T0 측정용 보조 리스폰**이지
   정식 체크포인트(CP0~CP8)가 아니다. 팀이 구간을 오가며 재는 용도로만 쓴다.

**실행 순서 주의 — 물리실험은 `3. Setup Play`보다 먼저 돌려라.** `물리실험 (V3PhysLab)` 메뉴는
씬 루트에 `Player_Sphere`/`Player_Cube`/`Player_Tetrahedron`이 이미 있으면 시작하지 않고
중단한다(`1. Clear`로는 이 오브젝트들이 안 지워진다 — 직접 삭제해야 한다). `3. Setup Play`를
먼저 눌러 캐릭터를 스폰해 버리면, 물리실험을 다시 돌리기 전에 그 세 오브젝트를 씬에서 수동으로
지워야 한다.

그 밖의 번호 메뉴(5·8b·8c·8d·8e·9~12)는 각자 인라인 주석에 용도가 적혀 있다 — 필요할 때만 눌러라.

## 2. 현재 상태 (사실만 — 실측 없는 항목은 자리표시자로 남긴다)

- **컴파일·물리 관통(Audit ① 정적 관통) 결과: 2026-09-10 09:15 실측**(`V3_Reports/관문실행_2026-09-10_0915.txt`,
  이 폴더의 최종 소스 상태로 컴파일) — 컴파일 통과(4.47s, `error CS` 0), Build All 중 `[Error]` 0줄
  (06:31 실행에 있던 `renderer.material` 에러 2줄은 V3_PlateSensor의 MaterialPropertyBlock 전환으로
  소멸), `[Warning]` 1줄(B2 시작 매트의 찬장 문 연동 대상 미확인 — 알려진 미해결). Audit: ① 정적 관통
  0건 · ② 가동체 겹침 0건 · Rigidbody 8/40 · 질량 초과 0건. 같은 그룹 8건·트리거 13건·kinematic 1건은
  참고 항목(물리 관통 아님).
- **순차 잠금(T0RS 8~8c) 회귀 테스트(`8e. Test Sequential Gate (플레이모드 회귀)`) 결과:**
  이번 관문(06:31)에서는 미실행 — 최신 실행분은 2026-09-09 14:20(`게이트테스트_2026-09-09_1420.md`,
  통과)이며 측정 범위는 T0RS_01~05만이다(G1=03 직행·G3=04→05 직행 시 05 잠금 유지·G2=01→02→03
  정상경로 — T0RS_06~12는 미측정, map-reviewer 37차 A3). 그 뒤 러너는 출력 경로·주석만
  변경됐다(로직 변경 없음 — 빌더 진술, 버전관리 없어 재현 불가, map-reviewer 37차 A4).
- **기믹 배선**(시작 매트 무게판 · P5 판 3개→GOAL · 스윙 고리 · 빨랫줄 · P2 실 게이트)은
  `2. Build All`에 전부 포함되어 있다 — 별도로 누를 필요 없다.
- **투석기(`2c. Wire Catapult (실험 — 기본 OFF, Audit 깨짐)`)는 기본 OFF다.** Build All 체인에서
  의도적으로 분리돼 있다 — 겹침·질량 150·뒷마당 Rigidbody 예산 등 판정 전이라, 켜면 Audit가
  깨진다. 확인 목적이 아니면 누르지 마라. 투석기의 3역할(네모 탑승·세모 장전·구 조향)은 릴레이
  설계와 다르게 하드코딩돼 있고, 컨트롤타워가 "코드 정본" 쪽으로 확정했다(V3_Gimmicks.cs
  WireCatapult() 클래스 주석 참고).
- **`V3_WallSlip` 래퍼는 실험 전용이다.** 실제 플레이어에는 붙지 않는다(`3. Setup Play`가 만드는
  Player_* 오브젝트와 무관 — V3_Play.cs가 이 컴포넌트를 추가하지 않는다). 서랍계단 앞에서 점프가
  안 되는 현상의 원인은 벽 마찰이고, 이 래퍼가 물리실험 하네스 안에서는 그 마찰을 없애 효과를
  확인했지만, 실제 플레이어에 적용할지는 아직 판정 전이다 — 지금은 "원인 확인"까지만 끝났다.
- **물리실험 메뉴(`물리실험 (V3PhysLab)`)는 씬 루트에 `Player_Sphere`/`Player_Cube`/
  `Player_Tetrahedron`이 있으면 시작하지 않는다.** 위 §1 "실행 순서 주의" 참고.
- **물리실험 5회차(06:31, 각속도 재현 포함): 네모 래퍼 ON 8/8 ✓ · 세모 ON 0.8~1.0 6/6 ✓, 구
  20건 비행통과 — 4회차와 동일 패턴.** 근거: `V3_Reports/물리실험_2026-09-10_0631.md`.
- **리포트는 이제 이 유니티 프로젝트 폴더 안 `V3_Reports/`(= `KitchenMapV3/V3_Reports/`,
  `Assets` 밖이라 유니티가 임포트하지 않는다)에 생긴다.** 물리실험·무인검증(`V3Batch.RunAll`)·
  8e 게이트 테스트 리포트 전부 같은 규칙을 쓴다. 이전 판(axiom 루트 밖 "맵2_V3_릴레이설계/
  무인검증" 폴더)은 팀원 클론에 그 폴더 자체가 없을 수 있어 바꿨다. 컨트롤타워가 무인검증용으로
  다른 폴더를 지정하고 싶으면 환경변수 `V3_REPORT_DIR`를 설정한다 — 설정돼 있으면 그 폴더가
  최우선이다(무인검증 배치(`V3Batch.RunAll`)의 기존 `-v3out` 명령행 인자는 그대로 남아 있고,
  환경변수보다 우선순위가 낮다).
- **`.mat` 머티리얼 에셋 10개는 `Build All`(내부 `6. Apply Kitchen Materials (질감)`)이 파이프라인에
  맞춰 자동으로 만들거나(없으면 생성) 고쳐 쓴다(있는데 셰이더가 안 맞으면 재대입) — URP가 없는
  클론에서도 그냥 눌러도 된다.** `GraphicsSettings.currentRenderPipeline`이 URP를 가리키면 URP
  Lit 셰이더로, 아니면 Standard 셰이더로 만든다(CatapultSystem이 이미 쓰는 것과 같은 패턴).
  `.mat` 파일 자체는 이번 업로드 대상에서 빠져 있으니(캐시 #43 경로 목록 참고), 처음 여는
  사람은 반드시 `2. Build All`(또는 `6. Apply Kitchen Materials`)을 한 번 눌러야 질감이 보인다 —
  안 누르면 렌더러에 머티리얼이 아예 없다.
- **`Build All`·리포트 실행 시 `Assets/KitchenMapV3/Materials/*.mat`(+.meta)와 `V3_Reports/`가
  untracked로 생긴다 — 커밋 대상 아니다.** `.gitignore`에 둘 다 없어(map-reviewer 37차 A7 실독)
  `git status`에 떠도 놀라지 마라 — 추가하지 말고 그대로 둬라.
- **develop의 FrictionStickerSystem이 Play 진입 시 자동으로 붙는다(map-reviewer 36차-b 실측 —
  이 프로젝트 자체엔 그 시스템 코드가 없어 지금 이 클론에서는 발동하지 않는다. 팀 develop 전체를
  갖춘 환경에서 이 맵을 열면 발동한다는 뜻).** `[RuntimeInitializeOnLoadMethod]`로 씬에 스스로
  생성되고, 모든 `PlayerMover`에 `PlayerStickerCarrier`를 붙이며 기본 키가 **부착=V · 종류전환=Q**
  다 — 우리 카메라(V3ThirdPersonCamera)의 **Q(좌우 회전)·V(시점 리셋)**와 키가 겹친다. 이 맵에는
  부착 대상(StickerSurface)이 0개라 부착 자체는 항상 실패하지만, Q를 누르면 그 시스템이 먼저
  입력을 소비한다 — 카메라 Q 회전이 간헐적으로 안 먹는 것처럼 보이면 이 겹침을 의심해라.

## 3. 알려진 미해결 (상신 중 — 판정 대기)

- P5 판 B·C 배치.
- 빨랫줄 탑승 단차.
- SwingRing(스윙 고리) 간격.
- 카트(Trolley) 도킹 미구현.
- 서랍 슬라이드 보류.

## 4. 문제 생기면

콘솔 에러/경고 전문을 캡처해서 맵2 담당(송원석)에게 전달해라. 이 README는 초안이라 절차·현재
상태가 자주 바뀔 수 있다 — 최신 여부가 의심되면 먼저 확인해라. §2의 컴파일·관통·8e 항목은
2026-09-10 06:31 실행분(`V3_Reports/관문실행_2026-09-10_0631.txt`)으로 채워졌다 — 이후 다시
실행하는 사람은 그 최신 결과로 이 자리를 덮어써라(추정으로 채우지 마라).
