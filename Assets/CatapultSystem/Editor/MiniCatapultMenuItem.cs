// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.
//    Editor 폴더 밖에 두면 런타임 빌드 시 UnityEditor 참조로 컴파일 에러가 난다.

using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Catapult > Create Mini Catapult — 정상 크기 플레이어는 물리적으로 잘 못 쓰고, ScalePad로
/// 축소(`PlayerShapeController.EScaleState.Shrunk`)된 정육면체만 실질적으로 탑승할 수 있는 축소
/// 투석기다(2026-08-31, `docs/PRD/Catapult.md` §8.3 안 B). 코드 게이트가 아니라 **물리 크기 자체가
/// 게이트**다 — `PlayerShapeController.RecomputeTargetScale()`이 Root 전체(트리거·솔리드 콜라이더
/// 포함)에 배율을 곱하므로, 축소된 정육면체(기본 0.5배, 즉 0.5×0.5×0.5)만 이 캐비티에 맞는다
/// (`PortalSystem`이 이미 쓰는 "기하 제약 자체가 레벨 디자인 게이트" 철학과 같은 방향,
/// `Assets/CLAUDE.md`의 PortalSystem 항목 참고).
///
/// 형태는 `SlingCatapultMenuItem`의 Y자 슬링 프레임을 그대로 재사용한다(`BuildSlingCatapult`가
/// `float scale` 하나로 전체 형태를 결정하므로) — 손수레형이 아니라 슬링형을 고른 이유는 순수
/// 실용적 선택이다: 슬링 프레임이 바퀴·트레슬·균형추 없이 부품이 적어 축소했을 때 형태가 덜
/// 지저분하다.
///
/// **`MiniScale`을 이렇게 정한 이유(재검산 없이 안전한 이유) — `CatapultMenuItem.cs` 클래스 상단
/// "파묻힘 검산 공식" 참고.** `CreateBucket`이 만드는 캐비티 절반 치수는
/// `raw_half * scale * BucketGroupLocalScale`이고(`BucketGroupLocalScale`(0.43)은 scale과 무관한
/// 별도 계수 — "고정 배율 정육면체가 scale-비례 캐비티에 맞도록 축소하는" 상수), 파묻힘 검산 공식
/// `y = ApexY + (BucketGroupLocalY + BucketGroupLocalScale·Δz)·cosθ + BucketGroupLocalScale·Δy·sinθ`
/// 도 `ApexY`/`BucketGroupLocalY`/`Δy`/`Δz`가 전부 `scale`에 선형이라 **`scale`을 그대로 어떤
/// 양수배로 줄여도 좌변 `y`와 우변 `BaseTopY`(=0.2·scale)가 똑같은 비율로 줄어든다** — 즉 원본에서
/// 이미 검증된 여유(margin = y − BaseTopY)의 **부호**는 scale을 줄여도 절대 바뀌지 않는다(선형
/// 스케일링이 부호를 보존한다). `scale`을 `CatapultMenuItem.Scale`(3f, 정상 정육면체 1×1×1 기준으로
/// 검증된 값) × `ShrinkMultiplier`(0.5f)로 낮추면, 캐비티 절반 치수도 정확히 같은 비율로 줄어들고
/// — 축소된 정육면체(0.5×0.5×0.5)도 정상 정육면체(1×1×1)와 정확히 같은 비율로 작아지므로, **캐비티와
/// 정육면체의 상대적 여유(margin) 비율이 원본과 동일하게 보존된다.** 버킷 캐비티를 새로 재설계·
/// 재검산할 필요가 없는 이유가 이것이다.
///
/// **`ShrinkMultiplier=0.5f`는 `ScalingSystem/Playershapecontroller.cs`의 `shrinkMultiplier` 필드
/// 기본값(0.5f)을 그대로 미러링한 것이다 — 직접 참조하지 않은 이유는 그 필드가 MonoBehaviour
/// 인스턴스 필드(씬 배치 후 인스펙터에서 바뀔 수 있는 값)라 Editor 생성기가 컴파일 타임에 참조할
/// 방법이 없기 때문이다.** 씬의 플레이어 프리팹에서 이 기본값을 바꾸면 이 상수도 함께 재조정해야
/// 한다 — 교차 폴더(ScalingSystem) 값에 의존하는 숫자라는 것을 기록해 둔다(`CatapultSteerHandle`이
/// 이미 `PlayerShapeController.growMultiplier`를 직접 참조하는 선례가 있어, 값을 "읽는" 의존
/// 자체는 이 저장소에서 새로운 패턴이 아니다).
///
/// **탑승(정육면체) 외에 조향(구 도킹)·장전(정사면체 당김줄)은 크기와 무관하게 항상 접근 가능하다
/// (`docs/PRD/Catapult.md` §8.3에서 이미 명시한 대로) — 정상 크기 구·정사면체도 미니 투석기를
/// 조향·장전할 수 있다.** 이 라운드는 그 이상의 게이트(예: 조향도 축소 상태를 강제)를 추가하지
/// 않았다(§8.5 미확정 항목 — 사용자가 아직 답하지 않은 질문을 임의로 좁히지 않는다, YAGNI).
///
/// **탑승도 이제 코드 게이트로 확실히 막는다(2026-08-31 씬 테스트 피드백 반영, §8.5 미확정 항목을
/// "강제한다" 쪽으로 확정).** 물리 크기만으로는 정상 크기 정육면체가 억지로 다가가면 `Catapult_
/// BucketInner` 트리거와의 겹침만으로 `Board()`가 발동해 벽을 뚫고 부자연스럽게 낄 수 있었다 —
/// `CatapultBucket.requireShrunkOccupant`(신규 필드)를 이 투석기의 버킷에서만 `true`로 세팅해,
/// `rb.mass / stats.mass` 비율이 `shrunkBoardMaxScaleRatio`(기본 0.75, Shrunk 0.5는 통과·Normal
/// 1.0은 차단) 이하일 때만(=Shrunk 상태) 탑승을 허용한다 — 이미 있던 "커지면 탑승 차단"
/// (`heavyBoardBlockScaleRatio`) 게이트와 정확히 같은 스케일-비율 판정 패턴을 재사용했다(새
/// 판정 방식을 만들지 않았다). 기존 투석기(손수레형·Sling)의 버킷은 이 필드 기본값(`false`)이라
/// 전혀 영향받지 않는다.
///
/// **조향(구 도킹)·장전(정사면체 당김줄)은 여전히 크기와 무관하게 항상 접근 가능하다**
/// (`docs/PRD/Catapult.md` §8.3에서 이미 명시한 대로, §8.5의 나머지 미확정 항목은 이번 라운드에서
/// 건드리지 않았다 — 사용자가 아직 답하지 않은 질문을 임의로 좁히지 않는다, YAGNI).
///
/// **[TBD, 미검증] `Rigidbody.mass`는 배율(scale)에 비례해 줄이지 않는다.**
/// `SlingCatapultMenuItem.BuildSlingCatapult`가 호출하는 `CatapultMenuItem.ConfigureRootRigidbody`는
/// `mass=150`을 항상 그대로 쓴다(6차 개편이 "선형 3배" 기준으로 잡은 값, 배율과 무관한 고정값) —
/// 미니 투석기(`MiniScale=1.5`, 기본 `Scale=3f`의 절반)는 부피 기준으로 훨씬 작은데도 같은
/// 질량이라, 물리적으로는 원본보다 밀도가 훨씬 높은(무거운) 구조물이 된다. 조향(구 도킹)이 낼 수
/// 있는 회전 토크나 충돌 반응 등에 이 불일치가 체감상 영향을 줄 수 있다 — 실측 전이라 이번
/// 라운드는 건드리지 않았다(요청받지 않은 튜닝을 미리 하지 않는다, YAGNI). 문제가 확인되면
/// `MiniCatapultMenuItem`이 `root.GetComponent&lt;Rigidbody&gt;().mass`를 직접 낮추는 것을 검토한다.
/// </summary>
public static class MiniCatapultMenuItem
{
    // `PlayerShapeController.shrinkMultiplier`의 기본값(0.5f)을 미러링 — 클래스 상단 주석 참고.
    private const float ShrinkMultiplier = 0.5f;

    // = 1.5f. `CatapultMenuItem.Scale`이 internal const라 같은 어셈블리에서 상수 표현식으로
    // 참조할 수 있다 — 값을 다시 베끼지 않는다.
    internal const float MiniScale = CatapultMenuItem.Scale * ShrinkMultiplier;

    [MenuItem("Tools/Catapult/Create Mini Catapult")]
    private static void CreateMiniCatapult()
    {
        Vector3 origin = SlingCatapultMenuItem.ResolveSpawnOrigin();
        GameObject root = SlingCatapultMenuItem.BuildSlingCatapult(origin, MiniScale, "MiniCatapult");

        // 이 투석기의 버킷에서만 축소 상태 게이트를 켠다(클래스 상단 주석 참고) — 기존
        // 투석기/Sling의 버킷 컴포넌트는 각자 생성될 때 기본값(false) 그대로 남는다.
        CatapultBucket bucket = root.GetComponentInChildren<CatapultBucket>();
        if (bucket != null) bucket.requireShrunkOccupant = true;

        // 2026-08-31 신규(사용자 요청) — 미니 투석기는 링 자체가 작게 지어져 있어, 구가 도킹할 때
        // "링에 맞게" 커지는 기존 투석기의 연출이 어색하다. CatapultSteerHandle.growOnDock을 이
        // 투석기에서만 꺼서 도킹해도 크기가 그대로 유지되게 한다(기존 투석기/Sling은 계속 커진다).
        CatapultSteerHandle steerHandle = root.GetComponent<CatapultSteerHandle>();
        if (steerHandle != null) steerHandle.growOnDock = false;

        Debug.Log("[Catapult] 미니 투석기 생성 완료 — ScalePad로 축소(Shrunk)된 정육면체만 " +
                   "버킷에 탑승할 수 있습니다(정상 크기는 물리적으로도 안 맞고, 코드 게이트로도 " +
                   "거부됩니다). 조향(구 도킹)·장전(정사면체 당김줄)은 크기와 무관하게 정상 " +
                   "크기로도 가능합니다(구는 도킹해도 커지지 않습니다).");
    }
}
