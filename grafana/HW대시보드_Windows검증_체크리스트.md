# HW 부하 대시보드 — Windows 실측 체크리스트 (진수용)

macOS 쪽은 만삣삐 맥에서 만들었고, **Windows 실제 PC에서 값이 제대로 들어오는지**는 아직 못 봤다.
이 문서는 그걸 확인하려고 만든 체크리스트다. 위에서부터 순서대로, 막히면 막힌 단계 번호만 알려주면 된다.

> 먼저 알아둘 것: 이 대시보드가 보여주는 건 CPU/GPU **사용률(%)이 아니라 프레임타임(ms)** 이다.
> Unity가 크로스플랫폼 사용률을 안 줘서 그렇다. 16.7ms = 60FPS 한계, 33.4ms = 30FPS 한계.

---

## 0. 준비물

- Windows PC (가능하면 GPU가 NVIDIA/AMD/Intel 중 뭔지 아는 PC)
- Docker Desktop (그라파나/Loki용 — `팀원_설치_가이드.md` 참고)
- 이 브랜치(`feat/mnppi-hw-dashboard`)를 받은 EFK_PRO 프로젝트, Unity 2022.3.62f3

## 1. Loki/Grafana 띄우기

```
cd grafana
docker-compose up -d
```

브라우저에서 http://localhost:3000 → 로그인(`admin`/`admin`) → 대시보드 목록에
**EFK-PRO HW 부하 (Windows)** 가 보이는지 확인.

- [ ] 대시보드가 목록에 보인다

## 2. Frame Timing Stats 켜기 (중요)

Unity → **Edit > Project Settings > Player > Other Settings** 에서 **Frame Timing Stats** 체크.

> 이게 꺼져 있으면 CPU/GPU 프레임타임이 아예 안 나오고 FPS와 메모리만 나온다.
> 이 설정은 `ProjectSettings` 공유 파일을 바꾸는 거라, **팀 전체에 반영할지는 만삣삐와 먼저 상의**할 것.
> (로컬에서 테스트만 하고 커밋하지 않아도 된다.)

- [ ] 체크했다 (또는 만삣삐가 이미 반영해뒀다)

## 3. 텔레메트리 설정

`EFK_PRO/.env` 에 아래 세 줄이 있는지 확인(없으면 `README.md`의 "실제 게임 텔레메트리를…" 절 참고):

```
LOKI_URL=http://localhost:3100
LOKI_USER=local
LOKI_TOKEN=local
```

Unity에서 **Tools > Telemetry > Sync Loki Config from .env** 실행.

- [ ] Sync 실행했다

## 4. 플레이 (두 가지 모두)

| 구분 | 방법 | 대시보드 상단 **빌드 종류** 변수 |
|---|---|---|
| A. 에디터 | Unity Play 1~2분 | `editor` 또는 `All` |
| B. 플레이어 빌드 | Windows 빌드 후 exe 실행 1~2분 | `player` (기본값) |

**B가 진짜 부하 측정값**이다(에디터는 에디터 오버헤드가 섞임). A는 파이프라인이 도는지 보는 용도.

## 5. 확인할 것

대시보드 **EFK-PRO HW 부하 (Windows)**, 시간 범위 Last 60 minutes.

| # | 확인 항목 | 정상이면 | 결과 |
|---|---|---|---|
| 1 | "수집된 세션 수" | 플레이한 횟수만큼 올라감 | [ ] |
| 2 | "세션별 hw_info 원본" 로그 | `gpu`, `vramMb`, `gfxApi`, `ramMb` 가 **내 PC 사양과 일치** | [ ] |
| 3 | "평균 FPS" / "FPS 추이" | 값이 나오고 체감과 비슷 | [ ] |
| 4 | "CPU 프레임타임" | cpu_frame / main / render 3개 선이 보임 | [ ] |
| 5 | "GPU 프레임타임" | **값이 나온다** (아래 주의 참고) | [ ] |
| 6 | "진단: GPU 타이밍 미지원 보고" | `0` | [ ] |
| 7 | "메모리 사용량" | sys_mem, gfx_mem 둘 다 나옴 | [ ] |
| 8 | "GPU 분포"/"VRAM 분포"/"그래픽 API 분포" | 내 GPU·VRAM·API(`Direct3D11` 등)가 보임 | [ ] |

### 5번/6번이 안 나오면 (GPU 프레임타임 없음)

- "진단" 패널이 `1` 이상이고 GPU 프레임타임이 비어 있으면 → **이 환경에서는 GPU 타이밍이 안 잡힌다**는 뜻.
  버그가 아니라 알아야 할 정보다. 아래를 같이 알려줘:
  - "세션별 hw_info 원본"의 `gfxApi` 값 (Direct3D11 / Direct3D12 / Vulkan)
  - GPU 모델명
  - 2번(Frame Timing Stats) 체크 여부
- 이 경우 Project Settings > Player > Other Settings 의 **Graphics API**를 바꿔서(예: D3D11 ↔ D3D12)
  다시 플레이해보고 달라지는지도 알려주면 큰 도움이 된다.

### 값이 이상할 때 빠르게 보는 법

Grafana 왼쪽 메뉴 **Explore** → 데이터소스 Loki → 아래를 붙여넣고 실행:

```
{job="efk-pro", os="windows"}
```

- 결과가 아예 없으면: 3단계(.env / Sync)와 Loki 컨테이너가 떠 있는지(`docker ps`) 확인.
- `os="windows"` 대신 `{job="efk-pro"}` 만 넣었을 때는 나오는데 위는 안 나오면: `os` 라벨이 안 붙은 것 →
  `LokiClient.cs` 변경이 안 들어온 브랜치일 수 있다. 브랜치를 확인.

## 6. 알려줄 것 (결과 보고)

1. 8개 항목 체크 결과 (되는 것/안 되는 것)
2. PC 사양: CPU, GPU 모델, RAM, VRAM
3. 대시보드 스크린샷 1장 (위쪽 요약+그래프가 보이게)
4. 5번이 안 나왔다면 위 "5번/6번이 안 나오면"에 적은 3가지

수고!
