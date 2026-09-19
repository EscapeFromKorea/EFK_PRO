# EFK-PRO Grafana 대시보드 (로컬 자체 완결형)

팀 전체가 로컬에서 EFK-PRO 텔레메트리 대시보드(에러/이벤트 추이, 실시간 로그)를 볼 수 있게
하는 설정이다. **Loki(로그 저장소)까지 이 안에 같이 띄운다** — 외부 Grafana Cloud 계정이나
API 토큰이 전혀 필요 없다. `git pull` + `docker-compose up`만으로 누구나 바로 재현된다.

**Docker Hub에도 안 올린다** — 이 폴더 자체를 git으로 공유하고 로컬에서 직접 빌드 없이
그대로 띄우는 방식이라, 이미지를 새로 굽고 푸시할 필요가 없다. 대시보드가 바뀌면 이 폴더
안의 JSON만 고쳐서 커밋하면 된다.

> **참고**: 이 폴더는 원래 진수가 Grafana Cloud에 만들어둔 대시보드를 참고해서 시작했지만,
> 2026-09-20부로 완전히 로컬 자체 호스팅(Loki도 같이 띄움)으로 방향을 바꿨다 — 외부
> 계정/토큰 없이 팀 누구나 바로 쓸 수 있게 하기 위함이다. 팀 회의에서 진수 것과 합칠지,
> 이 방향을 계속 갈지 논의 필요.

## 실행

```bash
cd grafana
docker-compose up -d
```

브라우저에서 http://localhost:3000 접속 (계정 `admin` / `admin`, 로컬 전용이라 안 바꿔도 됨).
좌측 메뉴에서 **EFK-PRO Telemetry** 대시보드가 자동으로 보인다.

## 실제 게임 텔레메트리를 이 로컬 Loki로 보내고 싶다면

`EFK_PRO/.env`에 아래 세 줄을 넣는다(Unity의 `LokiConfigSync.cs`가 이 파일을 읽는다).
로컬 Loki는 인증이 없어서 USER/TOKEN 값은 아무 문자열이나 넣어도 된다(빈 값만 아니면 됨) —
그래도 형식은 지키자:

```
LOKI_URL=http://localhost:3100
LOKI_USER=local
LOKI_TOKEN=local
```

Unity 에디터에서 `Tools > Telemetry > Sync Loki Config from .env` 실행 후 Play하면, 실제
플레이 중 발생하는 이벤트(`game_boot`, 에러 등)가 이 로컬 Loki로 들어와 대시보드에 뜬다.

## 끄기

```bash
docker-compose down
```

로그/대시보드 설정 데이터는 도커 볼륨에 남는다. 완전히 지우려면 `docker-compose down -v`
(그러면 저장된 로그도 같이 사라진다).

## 대시보드 구성

| 패널 | 내용 |
|---|---|
| 에러 발생 추이 | `type="error"` 로그(Console의 Error/Exception이 자동으로 여기 찍힘) |
| 게임 부팅(game_boot) 추이 | 세션 시작마다 1회, 실질적인 플레이 세션 수 |
| 에러 레벨 세분화 | error vs exception 나눠서 보기 |
| 패널 기믹 이상 징후 추이 | `SpacePortalSystem/MovablePortalPanel.cs`의 `panel_jam`/`panel_stuck_recover` 등 |
| 이벤트 종류별 분포 | 현재 시간 범위 안에서 어떤 이벤트가 얼마나 찍혔는지 |
| 실시간 로그 | `{job="efk-pro"}` 원본 로그 스트림 |

> 진수가 만든 원본 대시보드 스크린샷/JSON을 못 구해서, 코드(`LokiTelemetry.cs`의 라벨 구조)
> 기준으로 재구성했다. 100% 동일하진 않을 수 있다 — 팀 회의에서 다른 패널/쿼리가 필요하다고
> 나오면 `provisioning/dashboards/efk-pro-telemetry.json`만 고쳐서 다시 커밋하면 된다.

## 팀원끼리 로그를 공유하고 싶다면 (지금은 미적용, 참고용)

지금 이 구성은 **각자 로컬에만** 로그가 쌓인다(내가 플레이한 것만 내 컴퓨터의 대시보드에
보임). 팀 전체가 같은 로그를 보려면 한 명의 컴퓨터가 Loki를 계속 켜두고 나머지가 그 IP로
접속하는 방식(같은 네트워크 한정)이나, 다시 클라우드 Loki(Grafana Cloud 등)를 쓰는 방식이
필요하다 — 이건 이번에 범위 밖으로 남겨두고 회의에서 정하기로 함.

## 대시보드를 직접 수정하고 싶을 때

Grafana UI에서 패널을 고치고, 우측 상단 톱니바퀴 → **JSON Model** → 전체 복사해서
`provisioning/dashboards/efk-pro-telemetry.json`에 덮어쓰고 커밋하면 팀 전체(이 폴더를
받는 모두)에 반영된다.
