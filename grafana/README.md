# EFK-PRO Grafana 대시보드 (로컬)

팀 전체가 로컬에서 똑같은 그라파나 대시보드(에러/이벤트 추이, 실시간 로그)를 띄워볼 수 있게
하는 설정이다. **Docker Hub에 이미지를 올리지 않는다** — 대신 이 폴더 자체를 git으로 공유하고,
각자 로컬에서 `docker-compose up`만 하면 된다. 대시보드가 바뀌면 이 폴더 안의 JSON만
고쳐서 커밋하면 되고, 이미지를 다시 빌드/푸시할 필요가 없다.

## 왜 Docker Hub에 안 올리나

이 대시보드는 우리 팀의 실제 로그(Loki)에 접속해야 의미가 있는데, 그 접속 정보
(`LOKI_URL`/`LOKI_USER`/`LOKI_TOKEN`)는 절대 이미지 안에 구워 넣으면 안 되는 민감정보다
(이미지를 받는 사람 누구나 우리 로그를 열람할 수 있게 됨). 그래서 접속 정보는 이미지가 아니라
**각자 로컬의 `.env` 파일**에서 실행 시점에 주입한다.

## 사전 준비

프로젝트 루트(`EFK_PRO/.env`)에 아래 세 값이 있어야 한다 — **Unity 쪽 텔레메트리
(`Assets/TelemetrySystem/Editor/LokiConfigSync.cs`)가 쓰는 파일과 완전히 같은 파일, 같은
키**다. 이미 세팅돼 있다면 그대로 재사용하면 되고, 없다면 팀 채널에서 값을 받아서 넣는다.

```
LOKI_URL=...
LOKI_USER=...
LOKI_TOKEN=...
```

## 실행

```bash
cd grafana
docker-compose up -d
```

브라우저에서 http://localhost:3000 접속 (기본 계정 `admin` / `admin`, 로컬 전용이라 굳이 안
바꿔도 되지만 원하면 바꿔도 됨). 좌측 메뉴에서 **EFK-PRO Telemetry** 대시보드가 자동으로
보인다.

## 끄기

```bash
docker-compose down
```

데이터(그라파나 자체 설정 등)는 도커 볼륨에 남는다. 완전히 지우려면 `docker-compose down -v`.

## 대시보드 구성

| 패널 | 내용 |
|---|---|
| 에러 발생 추이 | `type="error"` 로그(Console의 Error/Exception이 자동으로 여기 찍힘) |
| 게임 부팅(game_boot) 추이 | 세션 시작마다 1회, 실질적인 플레이 세션 수 |
| 에러 레벨 세분화 | error vs exception 나눠서 보기 |
| 패널 기믹 이상 징후 추이 | `SpacePortalSystem/MovablePortalPanel.cs`의 `panel_jam`/`panel_stuck_recover` 등 |
| 이벤트 종류별 분포 | 현재 시간 범위 안에서 어떤 이벤트가 얼마나 찍혔는지 |
| 실시간 로그 | `{job="efk-pro"}` 원본 로그 스트림 |

> 진수가 그라파나 클라우드에 만들어둔 원본 대시보드를 참고해서 최대한 비슷하게 구성했지만,
> 원본 JSON/스크린샷을 못 구해서 100% 동일하진 않을 수 있다. 팀 회의에서 다른 패널/쿼리가
> 필요하다고 나오면 이 폴더의 JSON(`provisioning/dashboards/efk-pro-telemetry.json`)만
> 고쳐서 다시 커밋하면 된다.

## 대시보드를 직접 수정하고 싶을 때

Grafana UI에서 패널을 고치고(대시보드 저장은 UI에서도 됨), 우측 상단 톱니바퀴 →
**JSON Model** → 전체 복사해서 `provisioning/dashboards/efk-pro-telemetry.json`에
덮어쓰고 커밋하면 팀 전체에 반영된다.
