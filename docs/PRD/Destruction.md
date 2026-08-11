# 물체 파괴 시스템 설계 문서 (Unity)

> 이 문서는 구현 착수 전 참고용 스펙 문서입니다. 실제 코드 작성/수정은 사용자 승인(diff 확인) 후 진행합니다.

**대상 엔진 버전: Unity 2022.3.62f3 (LTS)**

- 본 문서에서 다루는 API(`Rigidbody`, `Collision.relativeVelocity`, `Rigidbody.AddExplosionForce`, `EditorWindow`/`MenuItem`, `List<T>` 등)는 모두 2022.3 LTS에서 정상 지원되며 버전 관련 이슈 없음.
- 만약 프로젝트에서 새 Input System 패키지를 사용 중이라면(가속 발판/실타래 기믹의 입력 처리 관련), 해당 패키지 버전 호환성은 별도 확인 필요.

---

## 1. 개요

가속 발판, 실타래 기믹 등으로 플레이어(또는 오브젝트)의 이동 속도가 임계값 이상으로 붙었을 때,
**질량(무게) 조건**과 **속도 조건**을 동시에 만족하면 `Breakable` 태그/컴포넌트가 붙은 오브젝트가 파괴되는 시스템.

파괴는 "사전 제작된 파편 프리팹으로 스왑" 방식을 1차 구현 대상으로 하며,
실시간 메쉬 컷(Voronoi 등)은 성능/난이도 이슈로 **추후 확장 옵션**으로만 남겨둔다.

### 1.1 왜 이 기믹이 필요한가 (레벨 디자인 목적)

**새 경로를 여는 장애물이다.** 기존에도 "조건 충족 시 장애물을 치운다"는 계열(레버/발판으로 여는
`DoorSystem`, 무거운 도형이 올라서야 열리는 `ExitWeightPlate`)이 있지만, 이 기믹은 그 계열과
트리거 방식이 다르다 — 발판을 밟거나 위에 서 있는 게 아니라 **무게+속도 조건을 만족한 충돌**
자체가 트리거다. 가속 발판으로 가속한 도형이나 실타래로 스윙 붙은 도형이 벽/장애물에 부딪혀
깨뜨리면 그 자리에 새 통로가 열리는 그림을 상정한다 — 즉 "얼마나 세게 부딪혔는가"가 퍼즐의 해법이
된다.

`FallingRockSystem`(낙석)이 이미 비슷한 "충돌 시 파편으로 부서짐" 로직을 갖고 있지만, 이 시스템은
그와 독립된 별도 기믹으로 간다(낙석의 부서짐은 낙석 자신의 소멸 연출이지, 플레이어가 유발하는
레벨 기믹이 아니다) — 다만 파편 처리 방식(스케일 축소로 사라지기, 질량을 낮춰 플레이어를 안
밀기 등, 아래 3.2 참고)은 필요하면 그대로 참고한다.

---

## 2. 핵심 판별 로직

### 2.1 파괴 조건 (AND 조건)

```
IF (PlayerWeight.Of(충돌 상대 Rigidbody) >= requiredWeight)
   AND (collision.relativeVelocity.magnitude >= breakThreshold)
THEN 파괴 트리거
```

- 기존 설계안은 속도만 체크했으나, "무게를 활용한 기믹"이라는 컨셉과 맞지 않아 무게 조건을 추가함.
- **무게 판정은 raw `Rigidbody.mass`가 아니라 `PlayerSystem/PlayerWeight.Of(rb)`(질량 × 그 바디의
  실효 중력 배율)를 쓴다.** 이 저장소는 "무거워서 못 한다"류 판정 전부(`DreamThreadController`
  매달림 게이트, `ThreadBridge` 처짐, `DoorSystem/ExitWeightPlate`, `CloudTrampoline` 튕김·붕괴)를
  이 창구 하나로 통일해 뒀다 — raw mass로 재면 무중력 버블 등으로 실효 무게가 낮아진 도형이 여전히
  원래 무게로 판정되는 모순이 생긴다(공식이 갈라지면 "가벼워서 매달렸는데 무거워서는 못 부순다"
  같은 반대 모순도 날 수 있다). `PlayerWeight.Of`는 `body`가 플레이어가 아닌 임의의 Rigidbody여도
  안전하다(중력 오버라이드가 없으면 그냥 `mass`를 그대로 반환).
- `requiredWeight`는 오브젝트별 필드로 노출한다. 기본값은 "정육면체 하나로는 못 깨고 최소 두 도형의
  협동(또는 정육면체+가속/스윙 등 속도 보정)이 필요"인지, "정육면체 하나면 항상 깨짐"인지에 따라
  달라진다 — 이 저장소의 확정 도형 무게는 구 1.5 / 세모 1.0 / 네모 3.0(`PlayerShapeStats`)이므로,
  **"1kg" 같은 임의 기본값 대신 이 세 수치 중 어디를 기준으로 할지로 정한다**(예: 네모 단독 통과를
  의도하면 `requiredWeight ≈ 3.0`, 구+세모 협동을 의도하면 더 높게).

### 2.2 파괴 대상 판별 기준

- **1차 기준(정확한 판별): 컴포넌트 부착 여부** — `BreakableObject` 스크립트가 붙어 있어야만 파괴 로직이 동작.
  - 태그(문자열) 기반보다 오타/중복 위험이 없고, 오브젝트별 파라미터(파편 프리팹, 임계값 등)를 함께 들고 있을 수 있음.
- **2차 기준(에디터 편의용): `Breakable` 태그** — Tools에서 생성 시 자동으로 부여, 씬 뷰에서 필터링/식별용으로만 사용. 파괴 판별 로직 자체는 태그가 아니라 컴포넌트로 수행.

### 2.3 충돌 소스(가해자) 조건

- 가해자는 반드시 `Rigidbody`를 보유해야 `relativeVelocity`가 정확히 계산됨.
- **플레이어는 `CharacterController`가 아니라 `Rigidbody` 기반이다(코드로 확인 완료, 결정 불필요).**
  `PlayerSystem/Editor/PlayerObjectMenuItem.cs`가 세 도형(구/정육면체/정사면체) 전부에 논카인매틱
  `Rigidbody`를 붙인다 — 정육면체/정사면체는 `FreezeRotation`(회전 고정, 시각 회전은
  `PlayerVisualRoll`이 별도로 낸다), 구는 자유 회전이다. `OnCollisionEnter`는 지면/기믹 충돌을
  전담하는 `Player_Collider`(솔리드 콜라이더 자식)에서 정상 발동한다 — `DoorSystem`/`JumpSystem`
  등 기존 `OnCollisionEnter` 기반 기믹과 같은 대상이라 별도 처리가 필요 없다.

---

## 3. 스크립트 명세 (설계 초안, 의사코드)

### 3.1 `BreakableObject.cs` (파괴 대상 오브젝트에 부착)

```csharp
// 의사코드 - 실제 구현 시 사용자 승인 후 작성
// 이 저장소 컨벤션: 기획자가 Inspector에서 조정할 값은 public 필드 + [Header]/[Tooltip]으로 노출한다
// (SerializeField+private는 이 저장소에 전례가 없다 — Assets/CLAUDE.md 코드 컨벤션 참고).
public class BreakableObject : MonoBehaviour
{
    [Header("파괴 조건")]
    [Tooltip("PlayerWeight.Of(rb) 기준 임계 무게. 정육면체 단독 통과를 의도하면 ~3.0, 협동을 " +
             "의도하면 더 높게(PlayerShapeStats: 구 1.5/세모 1.0/네모 3.0).")]
    public float requiredWeight = 3f;
    [Tooltip("충돌 속도 임계값(relativeVelocity.magnitude).")]
    public float breakThreshold = 5f;

    [Header("파편")]
    public List<GameObject> fragmentPrefabs; // 2~3세트, 랜덤 선택
    public float fragmentLifetime = 5f;      // 파편 자동 정리 시간

    [Header("폭발력")]
    public float explosionForce = 10f;
    public float explosionRadius = 3f;

    private void OnCollisionEnter(Collision collision)
    {
        Rigidbody otherRb = collision.rigidbody;
        if (otherRb == null) return; // Rigidbody 없는 오브젝트는 판별 불가

        float speed = collision.relativeVelocity.magnitude;
        // PlayerWeight.Of를 쓴다 — raw mass 비교 금지(2.1 참고, 무중력 버블 등 실효 무게 변화를
        // 반영하려면 저장소 공통 창구를 통해야 한다).
        bool weightOk = PlayerWeight.Of(otherRb) >= requiredWeight;
        bool speedOk = speed >= breakThreshold;

        if (weightOk && speedOk)
        {
            Break(collision.GetContact(0).point);
        }
    }

    private void Break(Vector3 hitPoint)
    {
        // 1. 원본 비활성화 + 콜라이더/리지드바디 정지 (이중 충돌 방지)
        // 2. fragmentPrefabs 중 랜덤 하나 Instantiate
        // 3. 파편들에 AddExplosionForce(explosionForce, hitPoint, explosionRadius) 적용
        // 4. 각 파편에 fragmentLifetime 후 자동 Destroy 예약
    }
}
```

> 위 코드는 구조 이해용 의사코드이며, 실제 파일 생성/수정은 별도 승인 절차를 거쳐 진행합니다.

### 3.2 파편 프리팹 요구사항

- 각 Breakable 오브젝트 종류당 2~3세트 (완전 동일할 필요 없음, `Random.Range`로 선택).
- 각 파편 프리팹은 미리 다음을 포함해야 함:
  - `Collider` (단순 형태 권장, 성능 고려)
  - `Rigidbody`
  - (선택) 일정 시간 후 자동 삭제되는 별도 컴포넌트 또는 `BreakableObject`가 `Destroy(fragment, fragmentLifetime)` 호출로 처리

---

## 4. 에디터 툴 (Breakable 오브젝트 생성)

### 4.1 요구사항

- Unity Tools 메뉴에서 사용자가 원하는 형태(큐브, 구, 원기둥 등)의 Breakable 오브젝트를 생성 가능해야 함.
- 생성 시 자동으로:
  - 기본 Mesh + Collider 부여
  - `BreakableObject` 컴포넌트 부착 (기본 파라미터 세팅)
  - `Breakable` 태그 자동 할당 (에디터 식별용)
  - 파편 프리팹 슬롯은 비워둔 채 생성 → 사용자가 직접 할당

### 4.2 구현 방향 (설계 초안)

- `EditorWindow` 또는 `MenuItem` 기반 커스텀 툴로 구현.
- 형태 선택 UI(드롭다운 등) → 해당 Primitive 생성 → 컴포넌트 자동 부착.
- **[결정 필요 항목]** — 형태별 프리셋을 미리 준비된 프리팹으로 둘지, PrimitiveType으로 즉석 생성할지 확인 필요.

---

## 5. 실시간 메쉬 컷 (Voronoi 등) — 확장 옵션, 1차 구현 제외

| 항목 | 내용 |
|---|---|
| 방식 | 충돌 지점 기준 실시간 메쉬 계산/분할 |
| 장점 | 매번 다른 모양으로 랜덤하게 파괴됨 |
| 단점 | 계산 비용 큼(메쉬 재계산 + 콜라이더 재생성), 구현 난이도 높음(RayFire, Mesh Cutter 등 에셋 필요), 다수 동시 파괴 시 프레임 드랍 위험 |
| 결론 | 1차 구현에서는 제외. 프리팹 스왑 방식이 안정적이므로 우선 적용하고, 필요 시 후속 스프린트에서 별도 검토 |

---

## 6. 결정 필요 항목 (구현 착수 전 확인 필요)

1. `requiredWeight` 기준값 — 어떤 도형(조합)까지 통과시킬지의 레벨 디자인 판단(2.1 참고, 값 자체는
   `PlayerShapeStats`로 이미 알려져 있어 "수치를 모른다"는 문제가 아니라 "어디를 기준선으로 삼을지"
   설계 판단이다).
2. Breakable 생성 툴에서 형태 프리셋을 프리팹으로 관리할지, PrimitiveType으로 즉석 생성할지
3. 파편 오브젝트 풀링(Object Pooling) 적용 여부 — 동시 다발 파괴가 잦다면 성능상 고려 필요
   (참고: `FallingRockSystem`은 풀링 없이 런타임 Instantiate/Destroy만 쓴다 — 이 저장소에서
   풀링이 필수 관례는 아니다).

---

## 7. 원본 설계안 대비 변경/보강 사항 요약

- 파괴 조건에 **무게(mass) 체크 추가** (기존엔 속도만 체크)
- 파괴 판별을 **컴포넌트 기준**으로 통일 (태그는 보조 식별용)
- **파편 정리(lifetime) 로직 명시**
- 원본 오브젝트 비활성화 시 **콜라이더/리지드바디까지 함께 정지**하도록 명시
- 실시간 메쉬 컷은 **1차 구현 범위에서 명시적으로 제외**
- (전문가 검토 반영) 무게 판정을 raw `Rigidbody.mass`에서 저장소 공통 창구 **`PlayerWeight.Of`**
  로 교체 — 무중력 버블 등 실효 무게 변화 반영, 다른 무게 게이트 기믹과 판정 기준 통일
- (전문가 검토 반영) 필드 선언을 이 저장소 컨벤션(`public` + `[Header]`/`[Tooltip]`)에 맞춤
- (전문가 검토 반영) "CharacterController 여부" 결정 항목 제거 — 코드로 이미 Rigidbody 기반임을 확인
- (전문가 검토 반영) 1장에 레벨 디자인 목적("새 경로를 여는 장애물") 명시, `FallingRockSystem`과의
  관계(독립 기믹, 파편 처리 방식은 참고 가능) 명시
