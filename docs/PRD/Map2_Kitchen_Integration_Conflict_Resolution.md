# [FIX] PR #96 map2-kitchen 통합 충돌 해소

> **대상**: PR #96 `[FEAT] map2-kitchen 통합`의 `map-integration` 병합 충돌
> **브랜치**: `feat/lkj-map2-integration` · 충돌 해결 커밋 `e2649f2`
> **작성일**: 2026-09-19
> **성격**: 확인된 충돌 4건을 모두 해결했으며, 미해결 충돌은 0건

## 개요 (Overview)

PR #96의 head와 base 양쪽에서 `SampleScene` 및 동일한 Door Material의 `.meta` 파일을 서로 다르게 변경해 GitHub가 자동 병합하지 못했다. `SampleScene`은 PR head의 최신본을 그대로 유지하고, 재질은 기존 canonical GUID로 통일한 뒤 반대쪽 씬 참조를 함께 교정해 씬 데이터 손실과 Missing Material을 방지했다.

## 관련 이슈 (Related Issues)

- Related to [PR #96 — `[FEAT] map2-kitchen 통합`](https://github.com/EscapeFromKorea/EFK_PRO/pull/96)

---

## 변경 사항 (Changes)

### 1. `SampleScene` 양쪽 수정 충돌 (심각도: 상) — 수정 완료

PR head와 `map-integration`이 공통 조상 이후 동일한 Unity YAML 씬을 각각 대규모로 수정했다. Unity 씬은 오브젝트 단위가 아니라 직렬화된 텍스트 단위로 병합되므로, 서로 다른 위치의 작업이어도 fileID 배열과 오브젝트 블록 변화가 겹치면 자동 병합이 불가능하다.

문제 상태는 다음과 같았다.

```text
// Assets/Scenes/SampleScene.unity
CONFLICT (content): Merge conflict in Assets/Scenes/SampleScene.unity
```

적용한 해결은 충돌 블록을 부분적으로 조합하지 않고 PR head의 최신 씬을 파일 전체 기준으로 유지하는 것이다.

```text
// 병합 후 blob 검증
working tree SampleScene: fdba61b87acd379272269aa716b28b337c28de27
PR head SampleScene:     fdba61b87acd379272269aa716b28b337c28de27
```

- **해결책 A — PR head의 최신 `SampleScene` 전체 유지 (권장, 적용됨)**: 사용자가 최신 씬으로 덮어쓸 예정이라는 작업 방향과 일치하며, Unity YAML을 수동 조합하면서 생길 수 있는 Missing Reference를 피한다.
- **해결책 B — `map-integration`에서 `SampleScene` 삭제 (기각)**: 이후 병합에서 충돌이 없어지는 것이 아니라 `modify/delete` 충돌로 바뀔 수 있고, 병합 결과에서 씬 삭제로 해석될 위험이 있다.
- **해결책 C — 양쪽 YAML 수동 병합 (기각)**: 변경량이 크고 Unity fileID 관계를 깨뜨릴 가능성이 있어, 최신 씬을 기준으로 삼는 현재 운영 방식에 비해 위험이 크다.

정적 비교로 두 파일의 변경 이력과 blob hash를 확인했다. Unity Editor에서 실제 씬을 열어 플레이하는 검증은 로컬에 요구 버전 실행 파일이 없어 수행하지 못했다.

### 2. Door Material `.meta` add/add 충돌 (심각도: 상) — 수정 완료

세 재질 파일의 본문은 양쪽 브랜치에서 동일했지만, `.meta`가 독립적으로 생성되어 서로 다른 GUID를 가졌다. 한쪽 `.meta`만 선택하면 반대쪽 씬이 존재하지 않는 GUID를 계속 참조해 Missing Material이 발생한다.

```yaml
# Assets/DoorSystem/Materials/Door_LeverHead_Mat.mat.meta
# map-integration에서 2026-08-22부터 사용한 원본
guid: 376629d2bf7d1d347b84ad4cd39c5866

# PR head에서 2026-09-14에 재생성된 값
guid: 4f8c04d472ff14d53a9056a91fd4ff26
```

세 재질 모두 먼저 생성되어 Map1이 사용 중이던 `map-integration` GUID를 canonical 값으로 유지했다.

```yaml
# 병합 후 canonical GUID
Door_LeverHead_Mat: 376629d2bf7d1d347b84ad4cd39c5866
Door_Lever_Mat:     3d8a32aac3c30f44ab402db19d01259b
Door_Pad_Mat:       be25f6a7861a1fc46a4d17cda0d5fdc5
```

- **해결책 A — 최초 생성 GUID 유지 및 신규 참조 치환 (권장, 적용됨)**: 기존 Map1 참조를 보존하고, 뒤에 생성된 데모 씬의 제한된 참조만 교정한다.
- **해결책 B — PR head GUID 유지 (기각)**: 오래된 Map1 씬의 참조까지 바꿔야 하며 canonical 자산의 식별자가 불필요하게 교체된다.
- **해결책 C — 양쪽 재질을 중복 보존 (기각)**: 내용이 같은 자산에 두 GUID가 계속 존재해 이후 씬마다 참조가 갈리고 동일 충돌이 반복될 수 있다.

재질 본문 세 파일의 Git blob hash가 양쪽 브랜치에서 각각 동일함을 정적으로 확인했다.

### 3. `GimmickDemo_mnppi`의 폐기 GUID 참조 (심각도: 상) — 수정 완료

canonical `.meta`만 선택하면 PR 쪽 데모 씬에 남은 재생성 GUID 네 곳이 끊어진다. 따라서 `.meta` 선택과 함께 씬의 재질 참조를 원자적으로 변경했다.

```diff
# Assets/Scenes/GimmickDemo_mnppi.unity
- guid: 4f8c04d472ff14d53a9056a91fd4ff26
+ guid: 376629d2bf7d1d347b84ad4cd39c5866

- guid: ab840111322b24f48b52c8333d4ae92e
+ guid: 3d8a32aac3c30f44ab402db19d01259b

- guid: de3a27f151010468fa913c40b810a020
+ guid: be25f6a7861a1fc46a4d17cda0d5fdc5
```

`Door_Lever_Mat`은 씬에서 두 번 참조되어 총 네 곳을 치환했다. 병합 후 폐기된 세 GUID 검색 결과는 0건이고, `Map1_old`와 `GimmickDemo_mnppi` 모두 같은 canonical GUID를 참조한다.

### 4. GitHub 병합 가능 상태 복구 (심각도: 중) — 수정 완료

현재 feature 브랜치에 `origin/map-integration`을 merge commit으로 반영하고 네 충돌을 해결한 뒤 원격 브랜치에 푸시했다.

```text
// 원격 확인 결과
e2649f267bc04ec45e4efd4512b060769537303a  refs/pull/96/head
8047548eb2003ba5babc26bb9a66e78d0292ab97  refs/pull/96/merge
```

GitHub가 `refs/pull/96/merge`를 다시 생성했으므로 서버 기준으로도 자동 병합 가능한 상태임을 확인했다. 이는 Git 원격 상태에 대한 실측 결과이며 Unity 런타임 검증을 의미하지는 않는다.

---

## 스크린샷 / 영상 (Screenshots / Video)

해당 없음. 충돌 전 상태는 PR #96의 GitHub conflict 목록에서 확인할 수 있으며, 이번 변경에는 UI 또는 게임플레이 외형 변경이 없다.

---

## 테스트 방법 (How to Test)

1. `git fetch origin`을 실행하고 `feat/lkj-map2-integration`을 checkout한다.
2. `git merge-tree $(git merge-base origin/map-integration origin/feat/lkj-map2-integration) origin/map-integration origin/feat/lkj-map2-integration` 결과에 충돌 항목이 없는지 확인한다.
3. `Assets` 아래에서 `<<<<<<<`, `>>>>>>>` 충돌 마커와 폐기 GUID 3개를 검색한다.
4. Unity 2022.3.62f3에서 프로젝트를 열어 컴파일 완료 후 `SampleScene`, `Map1_old`, `GimmickDemo_mnppi`를 각각 연다.
5. 세 씬에서 Door Lever Head, Lever, Pad 재질에 Missing Material이 없는지 확인한다.

**예상 결과:** GitHub PR #96이 충돌 없이 merge 가능하며, `SampleScene`은 PR head 최신 상태를 유지한다. Door Material 세 개는 모든 관련 씬에서 정상 연결되고 Missing Material이 발생하지 않는다.

---

## 리뷰 포인트 (Review Focus)

- `SampleScene`을 부분 병합하지 않고 PR head 버전으로 유지한 결정이 현재 씬 운영 방식과 맞는지
- Door Material의 canonical GUID를 최초 생성본으로 통일한 것이 적절한지
- `GimmickDemo_mnppi`의 네 참조 치환 외에 폐기 GUID가 남지 않았는지
- 향후 feature 브랜치가 공용 `SampleScene`을 직접 수정하지 않고 맵별 전용 씬을 사용하도록 운영 규칙을 정할지

---

## PR 체크리스트 (Checklist)

### 제출자 (Author)
- [ ] 로컬에서 실행 및 기능 테스트 완료 (Unity 2022.3.62f3 실행 파일이 로컬에 없어 미실행)
- [x] 관련 이슈 번호 연결
- [x] 커밋 메시지 컨벤션 준수
- [x] 불필요한 디버그 로그 / 주석 제거
- [x] 충돌(Conflict) 없음 확인

### 리뷰어 (Reviewer)
- [ ] 변경 사항 이해 및 의도 파악
- [ ] 코드 로직 검토
- [ ] 직접 빌드하여 테스트
- [ ] 네이밍 / 컨벤션 준수 확인

---

## 추가 메모 (Additional Notes)

- 충돌 해결 과정에서 맵 오브젝트 배치나 사용자가 수정한 `SampleScene` 내용은 재생성하지 않았다. 병합 전 PR head의 씬 파일을 그대로 보존했다.
- `map-integration`의 `SampleScene`을 삭제하는 방식은 권장하지 않는다. 공용 씬 충돌을 줄이려면 삭제보다 맵별 전용 씬 분리와 `SampleScene` 수정 주체 단일화가 필요하다.
- 자동 병합된 C# 파일은 충돌 마커가 없음을 확인했지만, Unity Editor 컴파일과 플레이테스트는 별도 확인이 필요하다.

---

## 5줄 요약

1. (상/완료) 양쪽에서 수정된 `SampleScene`은 PR head 최신본을 그대로 유지했다 (`e2649f2`).
2. (상/완료) 동일 재질에 중복 생성된 `.meta` GUID 3개를 최초 canonical GUID로 통일했다 (`e2649f2`).
3. (상/완료) `GimmickDemo_mnppi`의 폐기 GUID 참조 4곳을 치환해 Missing Material 가능성을 제거했다 (`e2649f2`).
4. (중/완료) 충돌 마커·중복 GUID·폐기 GUID가 0건이고 GitHub PR merge ref가 다시 생성됨을 확인했다 (`e2649f2`).
5. 전체 진행 상황: 충돌 4건 중 4건 해결, Git 검증 완료, Unity 2022.3.62f3 에디터 컴파일/플레이테스트만 미실행.
