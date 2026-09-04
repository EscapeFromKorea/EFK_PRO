# Map 3 ToyWorld 검증 기록

검증일: 2026-09-05  
Unity: 2022.3.62f3  
환경: 원본 프로젝트가 Unity Editor에서 열려 있어, 사용자 세션을 종료하지 않고 별도 임시 복제본에서 batchmode 검증

## 결과

- C# 런타임/Editor 어셈블리 컴파일: PASS
- Builder 최초 실행: PASS
- Builder 반복 실행 및 `Generated` 재생성: PASS
- 씬 구조 검사: PASS
- Missing Script: 0
- Missing Reference: 0
- Collider/VisualMesh 동일 오브젝트 혼합: 0
- 필수 바닥/벽/경사/플랫폼/레일/문 Collider 검사: PASS
- Play Mode 스모크: `RUNTIME_SMOKE_PASS`

## 자동 진행 검증

- 세 분기의 가능한 여섯 가지 완료 순서: PASS
- 같은 수리 아이템 중복 획득 방지: PASS
- 2/3 Final Gate 잠금: PASS
- 3/3 Final Gate 개방: PASS
- `Spring → Gear → Cylinder` 설치 순서: PASS
- 설치 완료 전 Exit 차단: PASS
- Music Box 활성화 전 Exit 차단: PASS
- 활성화 후 최종 완료: PASS
- Torque 상태 Enable/Disable: PASS
- 레일카 탈선 후 스폰 상태 복귀: PASS
- 레일 분기판 기본 우회/패드 정렬 상태: PASS
- SnapBlock 결합/분리: PASS

## 수동 플레이에서 확인할 항목

- 각 도형별 체감 난이도와 경사/점프 거리 미세 조정
- 실제 입력으로 WindUpAxis를 감는 속도와 리프트/카트 체감 시간
- 레일 단절부 통과 최고 속도 및 와이어 해제 타이밍
- Doll House 회전판의 질량/Angular Drag 체감
- 최종 아트 교체 후 시각 Mesh와 gameplay Collider 정합
