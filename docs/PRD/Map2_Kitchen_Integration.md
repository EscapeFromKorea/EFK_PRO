# [FEAT] map2-kitchen 통합

## 개요 (Overview)
> 이 PR이 무엇을 하는지 한두 문장으로 설명해주세요.
- map-integraion 브랜치에 map2 병합


## 관련 이슈 (Related Issues)
> 관련된 이슈 번호를 연결해주세요.

- Closes #
- Related to #

---

## 변경 사항 (Changes)
> 구체적으로 무엇을 추가/수정/삭제했는지 설명해주세요.
- map-integraion 브랜치에 map2 병합 진행
- develop 브랜치에 머지되어있는 map2
- 
- 

---

## 스크린샷 / 영상 (Screenshots / Video)
> UI 변경, 게임플레이 변화 등 시각적인 결과물이 있으면 첨부해주세요.
> 없으면 이 섹션은 삭제해도 됩니다.
<img width="1801" height="841" alt="image" src="https://github.com/user-attachments/assets/df0fae76-cef1-4bb4-bff8-78cbf58c5a5c" />



---

## 테스트 방법 (How to Test)
> 리뷰어가 직접 테스트해볼 수 있도록 재현 방법을 적어주세요.

1. Assets/KitchenMapV3/Scene/TeamKitchen 씬 실행
2. 
3. 

---

## 리뷰 포인트 (Review Focus)
> 리뷰어가 특히 집중해서 봐줬으면 하는 부분이 있으면 알려주세요.


---

## PR 체크리스트 (Checklist)

### 제출자 (Author)
- [x] 로컬에서 실행 및 기능 테스트 완료
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
> 리뷰어나 팀원에게 전달하고 싶은 사항이 있으면 자유롭게 적어주세요.
개선안 제안
- 맵이 사각형 공간안에 갇혀있는 구조다 보니 카메라를 돌릴 때마다 벽이나 지형지물에 카메라가 가려져 플레이어가 보이지 않는 문제가 있음. 카메라 관련하여 명세서 다시 작성할 예정
- 플레이어 입장에서 어디로 가야 클리어를 할 수 있는지 직관적이지 않음. 바닥에 포인트가 있긴 하지만 기믹을 자연스럽게 발견하고 이를 해결했을 경우 다음 섹터로 넘어가도록 유도하는 구조로 수정하면 좋을 듯 함.
- 충돌 사항 해결[Map2_Kitchen_Integration_Conflict_Resolution.md]