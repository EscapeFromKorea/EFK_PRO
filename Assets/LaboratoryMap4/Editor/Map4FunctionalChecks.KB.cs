#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 팀 develop abad97e 반영 재검증 — "추가 기능 검사 B" (K8 레이저 16 버퍼 · K9 조준 레이저 목표 · K12 S7 역할 사물 E 우선순위 · K13 ②③ S8).
/// 메인 파일(Map4FunctionalChecks.cs)을 고치지 않고 partial로만 붙는다. 새 ID: S4-5 · S4-6 · S5-5 · S7-5 · S8-5.
///
/// [금지 준수 — 메인 러너 머리 주석과 같다] 팀 코드·맵 코드·씬 수정 0 · 팀 private 리플렉션 0(공개 필드·메서드·프로퍼티·UnityEvent·C# event와
/// 공개 인터페이스 IInteractionProvider.CollectActions + InteractionAction.execute만) · 씬 저장 0 · Time.timeScale/fixedDeltaTime/레이어 행렬 변경 0.
/// 팀 UnityEvent 리스너는 관찰 전용(발화 기록)이며 끝나면 뗀다.
///
/// [중앙 입력(E) 흉내 — 규칙의 출처] 팀 InteractionController는 Update(InteractionController.cs:127-159)에서 ① 조작 중인 PlayerMover(Controlled, :41·:133-134)를 정하고
/// ② 씬 제공자(IInteractionProvider)마다 CollectActions(:140-145)로 액션을 모아 ③ 붙잡힘(IsGripped, :44-50)이면 allowWhenGripped만 남기고(:149-155)
/// ④ 채널(손/장비)별로 Channel.Offer(:82-99)가 "탭(enabled) · 탭(회색) · 홀드(enabled)" 후보 각각에 InteractionLogic.IsBetter(우선순위 높은 쪽, 같으면 거리 가까운 쪽,
/// InteractionLogic.cs:69-70)를 적용해 하나씩 고른다. 누르면(Process :161-195) 홀드가 가능한 대상이 있으면 짧게 뗄 때 탭이, 없으면 즉시 탭이 나간다.
/// 아래 KB_Collect·KB_Pick은 이 규칙을 그대로 옮긴 것이다(실행은 execute 호출 — 입력 장치는 쓰지 않는다). 조작권은 팀 PlayerControlSwitcher의 공개 정적 API
/// (RegisterPlayer·UnregisterPlayer — PlayerControlSwitcher.cs:101-122)로 넘긴다: 스위처에는 "이 도형으로 바꿔라" 공개 메서드가 없어(Tab 키만, :54-67)
/// 대상만 남기고 나머지를 뺐다가 다시 등록하는 순서를 쓴다(ApplyActive가 참조 기준으로 활성 도형을 정하므로 대상이 활성이 된다, :77-94).
/// </summary>
public static partial class Map4FunctionalChecks
{
    /// <summary>팀 LaserBeam의 NonAlloc 버퍼 크기 — LaserBeam.cs:46-47 (wallHitBuffer·beamHitBuffer = new RaycastHit[16]).</summary>
    private const int KB_BufferLimit = 16;
    private const float KB_LockAngleTol = 5f;       // [구현 결정] 록온 선과 표적 방향이 이 각 안이면 "그 표적을 잡았다"
    private const float KB_DirTol = 4f;             // [구현 결정] ≥16 표본 방향에 "도형이 설 수 있는 자리가 있다"고 보는 각 허용(조준점 방향 vs 표본 방향)
    private const int KB_MaxBehindTrials = 3, KB_MaxFrontTrials = 1;   // [구현 결정] 조준 레이저 1개당 재현된 자리 시험 상한(벽 뒤 3 + 벽 앞 1) — 시간 예산
    private const float KB_StartCaptureWait = 0.5f; // [구현 결정] 순간이동 뒤 낙하·정지·트리거 진입·컨트롤러 Update가 지나가길 기다리는 시간

    private partial class Driver : MonoBehaviour
    {
        // ═════════════════════════ 공용: 중앙 입력 흉내 ═════════════════════════

        /// <summary>한 제공자가 올린 액션 하나(제공자 이름 포함).</summary>
        private sealed class KB_Cand
        {
            public MonoBehaviour Provider;
            public string Name;
            public InteractionAction A;
        }

        private PlayerMover kbOrigControl;
        private bool kbControlSaved;

        private static PlayerMover KB_CurrentControl()
        {
            Transform t = PlayerControlSwitcher.ActiveTarget;                  // PlayerControlSwitcher.cs:32-33
            return t != null ? t.GetComponent<PlayerMover>() : null;
        }

        /// <summary>조작권을 target에게 — 공개 정적 API만. 처음 부를 때 원래 조작 도형을 기억하고 정리 목록에 되돌리기를 등록한다.</summary>
        private void KB_GiveControlNow(PlayerMover target)
        {
            if (target == null) return;
            if (!kbControlSaved)
            {
                kbControlSaved = true;
                kbOrigControl = KB_CurrentControl();
                cleanups.Add(KB_RestoreControl);
            }
            PlayerControlSwitcher.RegisterPlayer(target);                      // :101-110 (이미 있으면 무시)
            foreach (PlayerMover p in players) if (p != null && p != target) PlayerControlSwitcher.UnregisterPlayer(p);   // :112-122
            foreach (PlayerMover p in players) if (p != null && p != target) PlayerControlSwitcher.RegisterPlayer(p);
        }

        private IEnumerator KB_GiveControl(PlayerMover target)
        {
            KB_GiveControlNow(target);
            yield return null;
            yield return null;   // InteractionController.Update(:127)가 Controlled를 갱신할 시간
        }

        /// <summary>조작권을 검사 전 도형으로 되돌리고 3명 전부(발견 순서대로) 다시 등록한다. 예외가 나도 부를 수 있게 finally와 정리 목록(cleanups)에서 쓴다(멱등).</summary>
        private void KB_RestoreControl()
        {
            if (!kbControlSaved) return;
            if (kbOrigControl != null) KB_GiveControlNow(kbOrigControl);
            foreach (PlayerMover p in players) if (p != null) PlayerControlSwitcher.RegisterPlayer(p);   // 이미 등록돼 있으면 무시(:105)
        }

        /// <summary>검사 뒤 조작권 확인 — 검사 전 조작 중이던 도형이 다시 활성인지 InteractionController.Controlled(InteractionController.cs:41)와
        /// RoleAssignmentManager.ControlledPlayer()(RoleAssignmentManager.cs:179 — 스위처 활성 도형) 둘 다로 본다. 호출 전에 2프레임 이상 지나 있어야 한다.
        /// 조작권을 한 번도 안 넘겼으면(kbControlSaved 거짓) 확인할 것이 없다.</summary>
        private string KB_ControlCheck(RoleAssignmentManager mgr, out bool ok)
        {
            ok = true;
            if (!kbControlSaved) return "조작권 확인: 넘긴 적 없음";
            PlayerMover ic = InteractionController.Controlled;
            PlayerMover rm = mgr != null ? mgr.ControlledPlayer() : null;
            bool icOk = kbOrigControl == null || ic == kbOrigControl;
            bool rmOk = kbOrigControl == null || mgr == null || rm == kbOrigControl;
            ok = icOk && rmOk;
            return $"조작권 원복 확인: 검사 전 조작 도형 '{KB_ShapeName(kbOrigControl)}' → InteractionController.Controlled '{KB_ShapeName(ic)}' {(icOk ? "일치" : "불일치")} · " +
                   $"RoleAssignmentManager.ControlledPlayer() '{(mgr == null ? "(매니저 없음)" : KB_ShapeName(rm))}' {(rmOk ? "일치" : "불일치")} · 3명 재등록 호출 완료";
        }

        /// <summary>팀 InteractionController.Update(:127-159)의 "액션 수집 + 붙잡힘 거름"을 옮긴 것. 제공자는 활성·enabled인 IInteractionProvider 전부
        /// (팀은 OnEnable에서 Register한 것만 훑는다 — 등록 목록이 private라 못 읽으므로 활성 제공자 전체로 근사. (추측) 등록 안 하는 제공자가 있으면 후보가 더 많다).</summary>
        private List<KB_Cand> KB_Collect(PlayerMover holder, out bool gripped)
        {
            gripped = holder == null || InteractionController.IsGripped(holder);   // :44-50
            List<KB_Cand> list = new List<KB_Cand>();
            if (holder == null) return list;
            List<InteractionAction> buf = new List<InteractionAction>();
            foreach (MonoBehaviour mb in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (mb == null || !mb.isActiveAndEnabled) continue;
                IInteractionProvider ip = mb as IInteractionProvider;
                if (ip == null) continue;
                buf.Clear();
                try { ip.CollectActions(buf); }
                catch (Exception e)
                {
                    string key = $"KB_Collect: {mb.GetType().Name}'{mb.name}'.CollectActions 예외 {e.GetType().Name}: {e.Message}";
                    if (!report.Notes.Contains(key)) report.Notes.Add(key);
                    continue;
                }
                foreach (InteractionAction a in buf)
                    list.Add(new KB_Cand { Provider = mb, Name = $"{mb.GetType().Name}'{mb.name}'", A = a });
            }
            if (gripped) list.RemoveAll(c => !c.A.allowWhenGripped);              // :149-155
            return list;
        }

        /// <summary>팀 Channel.Offer(:82-99) — 채널 하나에서 "탭(enabled)·탭(회색)·홀드(enabled)" 후보 각각의 승자. tie = 승자와 우선순위·거리가 같은 후보가 또 있음(팀은 먼저 올린 쪽이 이김).</summary>
        private static void KB_Pick(List<KB_Cand> list, InteractionChannel ch, out KB_Cand tap, out KB_Cand blocked, out KB_Cand hold, out bool tie)
        {
            tap = null; blocked = null; hold = null; tie = false;
            foreach (KB_Cand c in list)
            {
                if (c.A.channel != ch) continue;
                if (c.A.trigger == InteractionTrigger.Hold)
                {
                    if (!c.A.enabled) continue;
                    if (hold == null || InteractionLogic.IsBetter(c.A.priority, c.A.distance, hold.A.priority, hold.A.distance)) hold = c;
                }
                else if (c.A.enabled)
                {
                    if (tap == null || InteractionLogic.IsBetter(c.A.priority, c.A.distance, tap.A.priority, tap.A.distance)) tap = c;
                }
                else if (blocked == null || InteractionLogic.IsBetter(c.A.priority, c.A.distance, blocked.A.priority, blocked.A.distance)) blocked = c;
            }
            foreach (KB_Cand c in list)
            {
                if (c.A.channel != ch || c.A.trigger != InteractionTrigger.Tap) continue;
                KB_Cand w = c.A.enabled ? tap : blocked;
                if (w != null && c != w && c.A.enabled == w.A.enabled && c.A.priority == w.A.priority && Mathf.Abs(c.A.distance - w.A.distance) < 1e-4f) tie = true;
            }
        }

        private static string KB_Desc(KB_Cand c) =>
            c == null ? "(없음)" : $"{c.Name}[{c.A.verb} · {(c.A.trigger == InteractionTrigger.Hold ? "홀드" : "탭")} · {(c.A.enabled ? "활성" : "회색: " + c.A.reason)} · 우선순위 {c.A.priority} · 거리 {c.A.distance:F2}]";

        private static string KB_DescAll(List<KB_Cand> list, InteractionChannel ch)
        {
            List<KB_Cand> hand = list.Where(c => c.A.channel == ch).ToList();
            return hand.Count == 0 ? "후보 없음" : string.Join(" ; ", hand.Select(KB_Desc));
        }

        /// <summary>목표점 둘레(반경 minR~maxR 링, 0이면 중심 포함)에서 서 있을 수 있는 바닥 점 — 위 3U에서 아래로 쏜 첫 면이 평평(법선 y ≥ 0.98)하고 목표 높이 ±1.5 안,
        /// 1.2U 상자가 고체와 안 겹치고, accept가 참인 점 중 (바닥 + pivotLift)가 목표에 가장 가까운 곳.</summary>
        private bool KB_StandNear(Transform gen, Vector3 target, float minR, float maxR, float pivotLift, Func<Vector3, bool> accept, out Vector3 ground, out string how)
        {
            ground = default;
            bool found = false;
            float best = float.MaxValue;
            int tried = 0, noGround = 0, notFlat = 0, blocked = 0, rejected = 0;
            for (float r = minR; r <= maxR + 1e-3f; r += 0.1f)
            {
                int steps = r < 1e-3f ? 1 : 36;
                for (int k = 0; k < steps; k++)
                {
                    float ang = k * 10f * Mathf.Deg2Rad;
                    Vector3 p = target + gen.right * (Mathf.Cos(ang) * r) + gen.forward * (Mathf.Sin(ang) * r);
                    tried++;
                    if (!TryGround2(p + gen.up * 2f, 6f, null, out Vector3 g, out Vector3 n)) { noGround++; continue; }
                    if (Vector3.Dot(n, gen.up) < 0.98f) { notFlat++; continue; }
                    if (Mathf.Abs(g.y - target.y) > 1.5f) { noGround++; continue; }
                    if (SolidOverlap(g + gen.up * (SpawnLift + 0.05f), new Vector3(0.6f, 0.5f, 0.6f), gen.rotation) != null) { blocked++; continue; }
                    if (accept != null && !accept(g)) { rejected++; continue; }
                    float d = Vector3.Distance(g + gen.up * pivotLift, target);
                    if (d < best) { best = d; ground = g; found = true; }
                }
            }
            how = found ? $"목표에서 3D {best:F2}U(피벗 높이 {pivotLift:F2} 가정, 후보 {tried}곳)"
                        : $"서 있을 바닥 없음(후보 {tried}곳: 바닥 없음/높이 밖 {noGround} · 경사 {notFlat} · 고체 겹침 {blocked} · 조건 탈락 {rejected})";
            return found;
        }

        private static string KB_ShapeName(PlayerMover m) => m == null ? "(없음)" : m.name;

        // ═════════════════════════ [K8] 레이저 16 버퍼 — 질의 재현·표 ═════════════════════════

        private sealed class KB_Probe
        {
            public string Laser, Sector, DirLabel, WallName, TopNames;
            public Vector3 OriginWorld, DirWorld, OriginLocal, DirLocal;
            public float Range, Radius, WallExact, WallTeam;
            public int RayAll, RayPlayer, RayWall, SphereAll, TeamSphereCount;
            public bool Over16 => RayAll >= KB_BufferLimit || SphereAll >= KB_BufferLimit;
            public bool TeamWallMissed => Mathf.Abs(WallExact - WallTeam) > 0.01f;
            public int Score => Mathf.Max(RayAll, SphereAll);
        }

        /// <summary>한 레이저 방향의 질의 수를 센다. 팀 LaserBeam.Tick(LaserBeam.cs:63-124)과 같은 질의를 "버퍼 없는 *All"로: ① Physics.RaycastAll(origin, dir, range, wallMask, 트리거 무시)
        /// — 사거리 전체 광선에 걸리는 콜라이더 수(LaserBeam.cs:84의 RaycastNonAlloc과 같은 인자) ② 가장 가까운 "도형이 아닌" 충돌까지(= 팀 dist, :88-91)
        /// Physics.SphereCastAll(origin, beamRadius, dir, dist, ~0, 트리거 무시) — 빔 굵기 안 콜라이더 수(:112와 같은 인자). 거기에 같은 질의를 크기 16 NonAlloc으로 재현해
        /// (공개 Physics API, 팀 private 버퍼는 안 건드림) 팀이 실제로 얻을 벽 거리·구 캐스트 반환 수를 나란히 남긴다.</summary>
        private KB_Probe KB_ProbeBeam(LaserBeam beam, Vector3 origin, Vector3 dir, Transform gen, string laser, string sector, string label)
        {
            Vector3 d = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;       // LaserBeam.cs:76
            KB_Probe p = new KB_Probe
            {
                Laser = laser, Sector = sector, DirLabel = label, Range = beam.range, Radius = beam.beamRadius,
                OriginWorld = origin, DirWorld = d, OriginLocal = Lc(gen, origin), DirLocal = gen.InverseTransformDirection(d)
            };
            RaycastHit[] ray = Physics.RaycastAll(origin, d, beam.range, beam.wallMask, QueryTriggerInteraction.Ignore);
            Array.Sort(ray, (a, b) => a.distance.CompareTo(b.distance));
            p.RayAll = ray.Length;
            float wall = beam.range;
            List<string> names = new List<string>();
            foreach (RaycastHit h in ray)
            {
                if (h.collider == null) continue;
                if (names.Count < 8) names.Add(h.collider.name);
                if (h.collider.CompareTag(beam.playerTag)) { p.RayPlayer++; continue; }   // LaserBeam.cs:88
                p.RayWall++;
                if (h.distance < wall) { wall = h.distance; p.WallName = h.collider.name; }
            }
            p.WallExact = wall;
            p.TopNames = string.Join(", ", names);
            p.SphereAll = Physics.SphereCastAll(origin, beam.beamRadius, d, wall, ~0, QueryTriggerInteraction.Ignore).Length;

            RaycastHit[] wb = new RaycastHit[KB_BufferLimit];
            int wc = Physics.RaycastNonAlloc(origin, d, wb, beam.range, beam.wallMask, QueryTriggerInteraction.Ignore);   // LaserBeam.cs:84
            float teamDist = beam.range;
            for (int i = 0; i < wc; i++)                                                                  // LaserBeam.cs:85-92
            {
                RaycastHit c = wb[i];
                if (c.collider != null && c.collider.CompareTag(beam.playerTag)) continue;
                if (c.distance >= teamDist) continue;
                teamDist = c.distance;
            }
            p.WallTeam = teamDist;
            RaycastHit[] bb = new RaycastHit[KB_BufferLimit];
            p.TeamSphereCount = Physics.SphereCastNonAlloc(origin, beam.beamRadius, d, bb, teamDist, ~0, QueryTriggerInteraction.Ignore);   // LaserBeam.cs:112
            return p;
        }

        private static string KB_RowText(KB_Probe p) =>
            $"{p.Laser} | S{p.Sector} | 원점 local{V(p.OriginLocal)} | {p.DirLabel} local{V(p.DirLocal)} | range {p.Range:F1}·반지름 {p.Radius:F2} | " +
            $"① {p.RayAll}(벽 후보 {p.RayWall}·도형 {p.RayPlayer}) | ② {p.SphereAll} | 벽 {p.WallExact:F2}U {(p.WallName ?? "없음(사거리 끝)")} | " +
            $"팀 16 버퍼 재현: 벽 {p.WallTeam:F2}U{(p.TeamWallMissed ? " ≠ 정확값(벽 누락)" : "")}·구 캐스트 {p.TeamSphereCount} | 16 이상 {(p.Over16 ? "예" : "아니오")}";

        private void KB_AddTable(string id, List<KB_Probe> rows)
        {
            Detail(id, "표: 레이저 | 섹터 | 원점(섹터 local) | 방향 | range·빔 반지름 | ① 사거리 광선 콜라이더 | ② 벽까지 빔 굵기 콜라이더 | 가장 가까운 벽 | 팀 16 버퍼 재현 | 16 이상 (질의 = LaserBeam.cs:84·112의 *All 판)");
            foreach (KB_Probe p in rows) Detail(id, KB_RowText(p));
        }

        /// <summary>조준 레이저는 방향이 목표에 따라 바뀐다 — 정면 + 원뿔(maxAimAngle) 안 표본(이탈각 간격 max(2.5°, 반각/8) × 방위 30°)을 훑어 ①·② 최대를 표에 올린다.
        /// worst = 표본 중 점수(max(①,②))가 가장 큰 방향.</summary>
        /// <summary>조준 레이저별 실측 계기 표본(①·② ≥ 16 또는 재현 벽 누락) 전부 — KB_ProbeAim이 채우고 KB_AimExposure가 읽는다.</summary>
        private readonly Dictionary<CharacterLockedLaser, List<KB_Probe>> kbAimTrig = new Dictionary<CharacterLockedLaser, List<KB_Probe>>();

        private void KB_ProbeAim(CharacterLockedLaser al, Transform gen, string sector, List<KB_Probe> rows, out int sampleTotal, out int sampleOver, out KB_Probe worst)
        {
            sampleTotal = 0; sampleOver = 0; worst = null;
            LaserBeam beam = al.beam;
            if (beam == null) return;
            Vector3 o = al.transform.position, f = al.transform.forward.normalized;
            KB_Probe center = KB_ProbeBeam(beam, o, f, gen, al.name, sector, "정면(transform.forward)");
            rows.Add(center);
            sampleTotal = 1;
            if (center.Over16) sampleOver++;
            List<KB_Probe> trig = new List<KB_Probe>();
            if (center.Over16 || center.TeamWallMissed) trig.Add(center);
            kbAimTrig[al] = trig;
            worst = center;
            float maxA = Mathf.Clamp(al.maxAimAngle, 1f, 180f);
            Vector3 helper = Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
            Vector3 right = Vector3.Cross(helper, f).normalized;
            float step = Mathf.Max(2.5f, maxA / 8f);
            KB_Probe max1 = center, max2 = center;
            for (float ang = step; ang <= maxA + 0.01f; ang += step)
            {
                Quaternion tilt = Quaternion.AngleAxis(ang, right);
                for (float az = 0f; az < 359.9f; az += 30f)
                {
                    Vector3 d = Quaternion.AngleAxis(az, f) * (tilt * f);
                    KB_Probe p = KB_ProbeBeam(beam, o, d, gen, al.name, sector, $"원뿔 이탈 {ang:F1}° 방위 {az:F0}°");
                    sampleTotal++;
                    if (p.Over16) sampleOver++;
                    if (p.Over16 || p.TeamWallMissed) trig.Add(p);
                    if (p.RayAll > max1.RayAll) max1 = p;
                    if (p.SphereAll > max2.SphereAll) max2 = p;
                    // 실측 대상 방향: 팀 16 버퍼 재현이 벽을 놓치는 방향을 먼저, 그다음 점수(max(①,②))가 큰 방향
                    if ((p.TeamWallMissed && !worst.TeamWallMissed) || (p.TeamWallMissed == worst.TeamWallMissed && p.Score > worst.Score)) worst = p;
                }
            }
            if (max1 != center) { max1.DirLabel += " ← 원뿔 표본 중 ① 최대"; rows.Add(max1); }
            if (max2 != center && max2 != max1) { max2.DirLabel += " ← 원뿔 표본 중 ② 최대"; rows.Add(max2); }
        }

        // ═════════════════════════ [K8] 16 이상일 때의 실측 ═════════════════════════

        private sealed class KB_Trial
        {
            public bool Released, Hit;
            public int ReleasedAttempts;
            public float HitAfter = -1f;
            public string Msg = "";
        }

        private sealed class KB_Exp
        {
            public bool FrontRan, FrontHit, BehindRan, BehindHit;
            /// <summary>그 시험 방향에서 실측 계기(①·② ≥ 16 또는 팀 16 버퍼 재현 벽 누락)가 실제로 재현됐는가. 고정 레이저는 방향이 하나라 표의 행이 곧 시험 방향(호출부에서 참으로 둔다).
            /// 조준 레이저는 시험 자리의 조준 방향을 시험 직전에 다시 재서 정한다 — 재현 안 되면 시험하지 않고 검사불가(컨트롤타워 판정, 검토 M2).</summary>
            public bool FrontTriggered, BehindTriggered;
            // 조준 레이저 방향별 실측(KB_AimExposure) 집계 — 고정 레이저는 위 Front/Behind 필드를 쓴다
            public bool IsAim;
            public int AimDirs, AimDirsNoSpot, AimCands, AimEligible, AimSkipped, AimReproduced, AimBehindRan, AimFrontRan, AimInconclusive;
            public readonly List<string> AimPierce = new List<string>(), AimMiss = new List<string>();
            public string FrontMsg = "(실측 못 함)", BehindMsg = "(실측 못 함)";
        }

        /// <summary>고정 레이저 1회 노출: 도형을 place에 붙잡아 두고 예고(얇은 선) 시작을 본 뒤 warningSeconds−0.06초에 풀어 발사 구간에 팀 물리로 맞는지 본다
        /// (메인 러너 LaserHit과 같은 방식 — 붙잡힌 몸은 팀 RespawnController가 거절하므로 맞기 직전에 푼다). 풀린 시도를 maxReleased번까지, 피격이 나오면 끝.</summary>
        private IEnumerator KB_BeamTrial(FixedPeriodicLaser lz, PlayerMover v, Vector3 place, int maxReleased, KB_Trial t)
        {
            LaserBeam beam = lz.beam;
            LineRenderer lr = beam != null ? beam.GetComponent<LineRenderer>() : null;
            if (beam == null || lr == null) { t.Msg = "beam/LineRenderer 없음"; yield break; }
            bool holding = false, hit = false;
            float hitAt = -1f;
            UnityAction<GameObject> cb = go => { if (!holding && !hit && Owner(go) == v) { hit = true; hitAt = Time.time; } };
            if (beam.OnHazardHit != null) beam.OnHazardHit.AddListener(cb);
            try
            {
                for (int attempt = 1; attempt <= maxReleased + 2 && t.ReleasedAttempts < maxReleased && !hit; attempt++)
                {
                    bool sawIdle = false;
                    float teleAt = -1f;
                    Func<bool> releaseNow = () =>
                    {
                        bool te = lr.enabled && lr.widthMultiplier < beam.activeWidth * 0.9f;
                        if (!te) { if (teleAt < 0f) sawIdle = true; return false; }
                        if (teleAt < 0f && sawIdle) teleAt = Time.time;
                        return teleAt >= 0f && Time.time >= teleAt + lz.warningSeconds - 0.06f;
                    };
                    bool released = false;
                    holding = true;
                    float since = Time.time;
                    yield return HoverShape(v, place, releaseNow, lz.warningSeconds + lz.beamSeconds + lz.restSeconds + 2f, x => { released = x; holding = false; });
                    holding = false;
                    if (!released) { t.Msg += $" {attempt}회차: 예고 시작을 못 봄;"; continue; }
                    t.Released = true;
                    t.ReleasedAttempts++;
                    float tr = Time.time;
                    while (!hit && Time.time - tr < lz.beamSeconds + 0.6f) yield return new WaitForFixedUpdate();
                    if (hit) { t.Hit = true; t.HitAfter = hitAt - since; t.Msg += $" {attempt}회차 피격(+{t.HitAfter:F2}s);"; }
                    else t.Msg += $" {attempt}회차 발사 구간에 피격 이벤트 없음;";
                    yield return WaitIdle(v, 5f);
                    yield return new WaitForSeconds(0.3f);
                }
            }
            finally
            {
                if (beam != null && beam.OnHazardHit != null) beam.OnHazardHit.RemoveListener(cb);
            }
        }

        /// <summary>벽 바로 뒤 빔 선상의 빈 자리 — 벽 거리 + 0.4U부터 0.25U 간격으로 0.45U 반폭 상자가 고체와 안 겹치는 첫 점(사거리 − 0.3 안).</summary>
        private bool KB_BehindWallSpot(Vector3 o, Vector3 d, float wall, float range, Transform gen, out Vector3 place, out string how)
        {
            place = default;
            int overlapped = 0;
            for (float s = wall + 0.4f; s <= range - 0.3f + 1e-4f; s += 0.25f)
            {
                Vector3 p = o + d * s;
                if (SolidOverlap(p, Vector3.one * 0.45f, gen.rotation) != null) { overlapped++; continue; }
                place = p;
                how = $"벽({wall:F2}U) 뒤 빔 선상 {s:F2}U(고체 겹침 표본 {overlapped}개 건너뜀)";
                return true;
            }
            how = $"벽({wall:F2}U) 뒤 사거리({range:F1}U) 안에 빈 자리 없음(고체 겹침 표본 {overlapped}개)";
            return false;
        }

        private IEnumerator KB_FixedExposure(FixedPeriodicLaser lz, PlayerMover v, Transform gen, Vector3[] park, KB_Exp r)
        {
            LaserBeam beam = lz.beam;
            LineRenderer lr = beam != null ? beam.GetComponent<LineRenderer>() : null;
            if (beam == null || lr == null) { r.FrontMsg = r.BehindMsg = $"'{lz.name}' beam/LineRenderer 없음"; yield break; }
            Vector3 o = lz.transform.position, fw = lz.transform.forward.normalized;
            KB_Probe pr = KB_ProbeBeam(beam, o, fw, gen, lz.name, "-", "정면");
            float dW = pr.WallExact;

            // (나) 벽 앞 빔 선상 도형이 맞는가 — 안 맞으면 '피격 누락'
            float limit = Mathf.Min(beam.range, dW - 0.5f);
            Vector3 front;
            string howF;
            if (limit >= 1.5f && TryBeamHoverSpot(o, fw, limit, gen, out front, out howF))
            {
                if (park != null && park.Length > 0) yield return ParkPlayers(park, sphere, cube, tetra);
                KB_Trial t = new KB_Trial();
                yield return KB_BeamTrial(lz, v, front, 2, t);
                r.FrontRan = t.Released;
                r.FrontHit = t.Hit;
                r.FrontMsg = $"{howF} local{V(Lc(gen, front))}:{t.Msg}";
            }
            else r.FrontMsg = $"벽 앞 빔 선상 자리 없음(벽 {dW:F2}U, 자리 상한 {limit:F2}U)";
            yield return WaitIdle(v, 5f);
            if (park != null && park.Length > 0) yield return ParkPlayers(park, v);

            // (가) 벽 바로 뒤 빔 선상 도형이 맞는가 — 맞으면 '빔 벽 관통'
            if (KB_BehindWallSpot(o, fw, dW, beam.range, gen, out Vector3 behind, out string howB))
            {
                KB_Trial t = new KB_Trial();
                yield return KB_BeamTrial(lz, v, behind, 2, t);
                r.BehindRan = t.Released;
                r.BehindHit = t.Hit;
                r.BehindMsg = $"{howB} local{V(Lc(gen, behind))}:{t.Msg}";
            }
            else r.BehindMsg = howB;
            yield return WaitIdle(v, 5f);
            if (park != null && park.Length > 0) yield return ParkPlayers(park, sphere, cube, tetra);
            yield return new WaitForSeconds(0.3f);
            RestoreGravityIfStuck(v, "K8");
        }

        // ═════════════════════════ [K9] 조준 레이저 — 자리·록온 관찰 ═════════════════════════

        private sealed class KB_AimResult
        {
            public bool Ran, Locked, Fired, Hit, Respawned;
            public float LockAfter = -1f, LockAngle = -1f, HitAfter = -1f;
            public string Msg = "";
        }

        private static Vector3 KB_AimPoint(PlayerMover v)
        {
            PlayerShapeIdentity id = v != null ? v.GetComponent<PlayerShapeIdentity>() : null;
            return id != null && id.solidCollider != null ? id.solidCollider.bounds.center : v.transform.position;   // CharacterLockedLaser.cs:123-124
        }

        /// <summary>조준 레이저가 표적으로 삼을 자리. mode 0 = 아무 데나(시선이 트인 곳 우선), 1 = 시선 트임(벽에 안 가림), 2 = 벽 뒤(원뿔·사거리 안인데 첫 고체가 0.6U 넘게 앞 —
        /// 광선 ① 콜라이더 수가 가장 많은 방향 우선). 록온은 시선을 보지 않는다(CharacterLockedLaser.cs:141-162 — 종류·유효·원뿔·사거리만). 자리는 서 있는 바닥(평평)에서
        /// 원뿔(maxAimAngle − 여유)·사거리(sensorRange − 0.7) 안, 안전점·고정 광선·낙석 레인을 피한다(AimLaserTrial과 같은 회피 규칙).</summary>
        private bool KB_FindAimSpot(CharacterLockedLaser al, PlayerMover v, int mode, out Vector3 ground, out string how)
        {
            ground = default;
            Transform gen = s4.Gen;
            Vector3 o = al.transform.position, f = al.transform.forward.normalized;
            float halfY = SolidBounds(v).extents.y;
            float margin = Mathf.Min(3f, al.maxAimAngle * 0.35f);
            float maxAng = al.maxAimAngle - margin;
            float minD = 1.5f, maxD = al.sensorRange - 0.7f;
            if (maxAng <= 0.5f || maxD <= minD) { how = $"원뿔 {al.maxAimAngle}°·사거리 {al.sensorRange}가 너무 좁아 자리 없음"; return false; }
            List<Vector3> avoid = new List<Vector3>();
            foreach (SectionSafePoint sp in new[] { s4.SpStairs, s4.SpBubble }) if (sp != null) avoid.AddRange(SafePoints(sp));
            List<KeyValuePair<Vector3, Vector3>> beams = s4.FixedLasers().Where(fl => fl.beam != null)
                .Select(fl => new KeyValuePair<Vector3, Vector3>(fl.transform.position, fl.transform.position + fl.transform.forward * fl.beam.range)).ToList();
            List<Vector3> lanes = new List<Vector3>();
            foreach (FallingRockSpawner rs in s4.Rocks()) if (rs.spawnPositions != null) foreach (Transform t in rs.spawnPositions) if (t != null) lanes.Add(t.position);
            float R = Mathf.Max(3f, al.sensorRange);
            Vector3 oL = Lc(gen, o);
            float bestScore = float.MinValue;
            bool found = false;
            int cols = 0, outCone = 0, avoided = 0, blocked = 0, modeSkip = 0;
            string bestInfo = "";
            for (float dx = -R; dx <= R; dx += 0.75f)
                for (float dz = -R; dz <= R; dz += 0.75f)
                {
                    if (dx * dx + dz * dz > R * R) continue;
                    cols++;
                    Vector3 top = gen.TransformPoint(new Vector3(oL.x + dx, oL.y + 1.5f, oL.z + dz));
                    RaycastHit[] hs = Physics.RaycastAll(top, -gen.up, R * 2f + 4f, ~0, QueryTriggerInteraction.Ignore);
                    Array.Sort(hs, (a, b) => a.distance.CompareTo(b.distance));
                    float lastY = float.MaxValue;
                    foreach (RaycastHit h in hs)
                    {
                        if (h.collider == null) continue;
                        if (h.collider.GetComponentInParent<PlayerMover>() != null || h.collider.GetComponentInParent<Projectile>() != null) continue;
                        if (h.collider.GetComponentInParent<FallingRock>() != null || h.collider.GetComponentInParent<FallingRockShard>() != null) continue;
                        if (Vector3.Dot(h.normal, gen.up) < 0.98f) continue;
                        if (lastY - h.point.y < 0.3f) continue;
                        lastY = h.point.y;
                        Vector3 g = h.point;
                        Vector3 aim = g + gen.up * (halfY + 0.05f);
                        Vector3 to = aim - o;
                        float dist = to.magnitude;
                        if (dist < minD || dist > maxD || Vector3.Angle(f, to) > maxAng) { outCone++; continue; }
                        if (avoid.Any(p => HorizDist(p, g) < 2.5f) || beams.Any(b => SegDist(aim, b.Key, b.Value) < 1.3f) || lanes.Any(l => HorizDist(l, g) < 1.5f)) { avoided++; continue; }
                        if (SolidOverlap(g + gen.up * (SpawnLift + 0.05f), new Vector3(0.55f, 0.5f, 0.55f), gen.rotation) != null) { blocked++; continue; }
                        float los = FirstSolidDistance(o, to, dist, al.transform);
                        bool clear = los >= dist - 0.6f;
                        if ((mode == 1 && !clear) || (mode == 2 && clear)) { modeSkip++; continue; }
                        float ang = Vector3.Angle(f, to);
                        float score = -ang + (mode == 0 && clear ? 1000f : 0f);
                        if (mode == 2 && al.beam != null) score += Physics.RaycastAll(o, to.normalized, al.beam.range, al.beam.wallMask, QueryTriggerInteraction.Ignore).Length * 1000f;
                        // 시선 트임(모드 1)에서도 ① 콜라이더 수가 16 이상인 방향을 먼저 — K8 실측 계기가 재현되는 자리를 우선한다(검토 M2).
                        if (mode == 1 && al.beam != null && Physics.RaycastAll(o, to.normalized, al.beam.range, al.beam.wallMask, QueryTriggerInteraction.Ignore).Length >= KB_BufferLimit) score += 5000f;
                        if (score > bestScore)
                        {
                            bestScore = score; ground = g; found = true;
                            bestInfo = $"거리 {dist:F1}U·정면에서 {ang:F1}°·시선 {(clear ? "트임" : $"첫 고체 {los:F1}U에서 가림")}";
                        }
                    }
                }
            how = found ? $"local{V(Lc(gen, ground))} {bestInfo}"
                        : $"자리 없음(수평 {cols}열: 원뿔/사거리 밖 {outCone} · 위험 요소 회피 {avoided} · 고체 겹침 {blocked} · 모드 탈락 {modeSkip}; 원뿔 {al.maxAimAngle}° 여유 {margin:F1}° 사거리 {al.sensorRange})";
            return found;
        }

        /// <summary>조준 레이저 1회 관찰 — v를 ground에 세워 두고(붙잡지 않음: 붙잡힌 몸은 팀 레이저가 무효 대상으로 본다, CharacterLockedLaser.cs:126-137)
        /// 매 물리 스텝 beam의 LineRenderer를 읽는다. "얇은 선(예고) 켜짐 + 방향이 그 도형 조준점과 KB_LockAngleTol° 이내"가 록온, 굵은 선이 발사, OnHazardHit(관찰 전용)이 피격.
        /// 조준 방향은 록온 순간 스냅샷이고 도형은 낙하·상승으로 조금 움직이므로 순간이동 뒤 조준점 이력 전체와 비교한다.</summary>
        private IEnumerator KB_AimLockTrial(CharacterLockedLaser al, PlayerMover v, Vector3 ground, float observeMax, KB_AimResult r)
        {
            LaserBeam beam = al.beam;
            LineRenderer lr = beam != null ? beam.GetComponent<LineRenderer>() : null;
            if (beam == null || lr == null) { r.Msg = $"'{al.name}' beam/LineRenderer 없음"; yield break; }
            Transform gen = s4.Gen;
            bool hit = false;
            float hitAt = -1f;
            UnityAction<GameObject> cb = go => { if (!hit && Owner(go) == v) { hit = true; hitAt = Time.time; } };
            if (beam.OnHazardHit != null) beam.OnHazardHit.AddListener(cb);
            try
            {
                yield return ParkPlayers(s4.Park, sphere, cube, tetra);
                yield return WaitIdle(v, 5f);
                float since = Time.time;
                List<Vector3> aims = new List<Vector3>();
                Teleport(v, ground + gen.up * SpawnLift);
                r.Ran = true;
                float t0 = Time.time, fireAt = -1f;
                float minAng = float.MaxValue;
                while (Time.time - t0 < observeMax)
                {
                    yield return new WaitForFixedUpdate();
                    if (v == null) break;
                    aims.Add(KB_AimPoint(v));
                    if (RespawnsSince(v, since) > 0) r.Respawned = true;
                    if (lr.enabled && lr.positionCount >= 2)
                    {
                        Vector3 ld = lr.GetPosition(1) - lr.GetPosition(0);
                        bool thin = lr.widthMultiplier < beam.activeWidth * 0.9f;
                        if (thin && !r.Locked && ld.sqrMagnitude > 1e-6f)
                        {
                            float best = float.MaxValue;
                            foreach (Vector3 a in aims) best = Mathf.Min(best, Vector3.Angle(ld, a - al.transform.position));
                            minAng = Mathf.Min(minAng, best);
                            if (best <= KB_LockAngleTol) { r.Locked = true; r.LockAfter = Time.time - t0; r.LockAngle = best; }
                        }
                        if (!thin && r.Locked && fireAt < 0f) { r.Fired = true; fireAt = Time.time; }
                    }
                    if (hit) { r.Hit = true; r.HitAfter = hitAt - since; }
                    if (r.Hit || (fireAt >= 0f && Time.time - fireAt > al.beamDuration + 0.8f) || r.Respawned) break;
                }
                if (!r.Locked) r.Msg = minAng < float.MaxValue ? $"예고 선은 켜졌으나 표적과 {minAng:F1}° 어긋남(허용 {KB_LockAngleTol}°)" : $"{observeMax:F1}초 안에 예고 선(록온) 없음";
                // 피격 뒤 복귀 연출 끝날 때까지 대기
                float th = Time.time;
                while (r.Hit && RespawnsSince(v, since) == 0 && Time.time - th < 3f) yield return null;
                r.Respawned = r.Respawned || RespawnsSince(v, since) > 0;
                yield return WaitIdle(v, 5f);
                yield return new WaitForSeconds(0.3f);
            }
            finally
            {
                if (beam != null && beam.OnHazardHit != null) beam.OnHazardHit.RemoveListener(cb);
            }
        }

        private string KB_AimLine(CharacterLockedLaser al, PlayerMover v, string how, KB_AimResult r)
        {
            Transform gen = s4.Gen;
            return $"'{al.name}' 발사점 local{V(Lc(gen, al.transform.position))} 대상 {(al.targetKinds != null && al.targetKinds.Length > 0 ? al.targetKinds[0].ToString() : "(없음)")} '{KB_ShapeName(v)}' " +
                   $"원뿔 {al.maxAimAngle}° 사거리 {al.sensorRange} 예고 {al.firstFireDelay}s, 자리 {how}: " +
                   (r.Ran ? $"록온 {(r.Locked ? $"예(+{r.LockAfter:F2}s, 선-표적 {r.LockAngle:F1}°)" : "아니오")} · 발사 {(r.Fired ? "예" : "아니오")} · 피격 {(r.Hit ? $"예(+{r.HitAfter:F2}s)" : "아니오")} · 복귀 {(r.Respawned ? "있음" : "없음")}" + (r.Msg.Length > 0 ? " · " + r.Msg : "") : "실측 못 함 " + r.Msg);
        }

        /// <summary>조준 레이저 시험 자리에서의 조준 방향(발사 원점 → 서 있는 표적의 조준점 추정 = 바닥 + 솔리드 반높이 + 0.05, CharacterLockedLaser.cs:123-124와 같은 기준)으로
        /// 같은 질의(①·②·팀 16 버퍼 재현 벽)를 다시 잰다 — 시험 직전에 부른다.</summary>
        private KB_Probe KB_ProbeForSpot(CharacterLockedLaser al, PlayerMover v, Vector3 ground, string label)
        {
            Transform gen = s4.Gen;
            Vector3 o = al.transform.position;
            Vector3 aim = ground + gen.up * (SolidBounds(v).extents.y + 0.05f);
            return KB_ProbeBeam(al.beam, o, aim - o, gen, al.name, "4", label);
        }

        private sealed class KB_AimCand
        {
            public Vector3 Ground, Aim;
            public float Dist, Ang, Los;
            public bool Clear;          // 발사 원점 → 조준점 사이에 0.6U 넘게 앞서는 고체가 없음(= 시선 트임)
            public KB_Probe Probe;      // 이 자리의 실제 조준 방향으로 잰 ①·②·재현 벽
        }

        /// <summary>조준 레이저가 표적으로 삼을 수 있는 모든 서 있는 자리 — 평평한 바닥(법선 y ≥ 0.98, 계단 디딤 포함, 같은 열의 여러 층 포함) 중 원뿔(maxAimAngle − 여유)·사거리(sensorRange − 0.7) 안,
        /// 안전점·고정 광선·낙석 레인을 피하고 1.2U 상자가 고체와 안 겹치는 곳. 격자 1U. 록온은 시선을 보지 않으므로(CharacterLockedLaser.cs:141-162) 벽 뒤 자리도 후보다.</summary>
        private List<KB_AimCand> KB_CollectAimCandidates(CharacterLockedLaser al, PlayerMover v, out string info)
        {
            List<KB_AimCand> list = new List<KB_AimCand>();
            Transform gen = s4.Gen;
            Vector3 o = al.transform.position, f = al.transform.forward.normalized;
            float halfY = SolidBounds(v).extents.y;
            float margin = Mathf.Min(1.5f, al.maxAimAngle * 0.2f);
            float maxAng = al.maxAimAngle - margin;
            float minD = 1.5f, maxD = al.sensorRange - 0.7f;
            if (maxAng <= 0.3f || maxD <= minD) { info = $"원뿔 {al.maxAimAngle}°·사거리 {al.sensorRange}가 너무 좁아 후보 없음"; return list; }
            List<Vector3> avoid = new List<Vector3>();
            foreach (SectionSafePoint sp in new[] { s4.SpStairs, s4.SpBubble }) if (sp != null) avoid.AddRange(SafePoints(sp));
            List<KeyValuePair<Vector3, Vector3>> beams = s4.FixedLasers().Where(fl => fl.beam != null)
                .Select(fl => new KeyValuePair<Vector3, Vector3>(fl.transform.position, fl.transform.position + fl.transform.forward * fl.beam.range)).ToList();
            List<Vector3> lanes = new List<Vector3>();
            foreach (FallingRockSpawner rs in s4.Rocks()) if (rs.spawnPositions != null) foreach (Transform t in rs.spawnPositions) if (t != null) lanes.Add(t.position);
            float R = Mathf.Max(3f, al.sensorRange);
            Vector3 oL = Lc(gen, o);
            int cols = 0, outCone = 0, avoided = 0, blocked = 0;
            for (float dx = -R; dx <= R; dx += 1f)
                for (float dz = -R; dz <= R; dz += 1f)
                {
                    if (dx * dx + dz * dz > R * R) continue;
                    cols++;
                    Vector3 top = gen.TransformPoint(new Vector3(oL.x + dx, oL.y + 1.5f, oL.z + dz));
                    RaycastHit[] hs = Physics.RaycastAll(top, -gen.up, R * 2f + 4f, ~0, QueryTriggerInteraction.Ignore);
                    Array.Sort(hs, (x, y) => x.distance.CompareTo(y.distance));
                    float lastY = float.MaxValue;
                    foreach (RaycastHit h in hs)
                    {
                        if (h.collider == null) continue;
                        if (h.collider.GetComponentInParent<PlayerMover>() != null || h.collider.GetComponentInParent<Projectile>() != null) continue;
                        if (h.collider.GetComponentInParent<FallingRock>() != null || h.collider.GetComponentInParent<FallingRockShard>() != null) continue;
                        if (Vector3.Dot(h.normal, gen.up) < 0.98f) continue;
                        if (lastY - h.point.y < 0.3f) continue;
                        lastY = h.point.y;
                        Vector3 g = h.point;
                        Vector3 aim = g + gen.up * (halfY + 0.05f);
                        Vector3 to = aim - o;
                        float dist = to.magnitude;
                        float ang = Vector3.Angle(f, to);
                        if (dist < minD || dist > maxD || ang > maxAng) { outCone++; continue; }
                        if (avoid.Any(q => HorizDist(q, g) < 2.5f) || beams.Any(bm => SegDist(aim, bm.Key, bm.Value) < 1.3f) || lanes.Any(l => HorizDist(l, g) < 1.5f)) { avoided++; continue; }
                        if (SolidOverlap(g + gen.up * (SpawnLift + 0.05f), new Vector3(0.55f, 0.5f, 0.55f), gen.rotation) != null) { blocked++; continue; }
                        float los = FirstSolidDistance(o, to, dist, al.transform);
                        list.Add(new KB_AimCand { Ground = g, Aim = aim, Dist = dist, Ang = ang, Los = los, Clear = los >= dist - 0.6f });
                    }
                }
            info = $"수평 {cols}열 → 서 있는 자리 {list.Count}곳(원뿔/사거리 밖 {outCone} · 위험 요소 회피 {avoided} · 고체 겹침 {blocked}; 원뿔 {al.maxAimAngle}° 여유 {margin:F1}° 사거리 {al.sensorRange}, 시선 트임 {list.Count(c => c.Clear)}·벽 뒤 {list.Count(c => !c.Clear)})";
            return list;
        }

        /// <summary>재현된 자리 중 시험할 것 n개 — 팀 16 버퍼 재현이 벽을 놓친 방향 → ①·② 점수 큰 순, 이미 고른 자리와 1.5U 안이거나 조준 방향이 4° 안이면 건너뛰어 방향을 흩는다.</summary>
        private static List<KB_AimCand> KB_PickCands(List<KB_AimCand> list, int n, Vector3 origin)
        {
            List<KB_AimCand> chosen = new List<KB_AimCand>();
            foreach (KB_AimCand c in list.OrderByDescending(x => x.Probe.TeamWallMissed ? 1 : 0).ThenByDescending(x => x.Probe.Score).ThenBy(x => x.Ang))
            {
                if (chosen.Count >= n) break;
                if (chosen.Any(k => Vector3.Distance(k.Ground, c.Ground) < 1.5f || Vector3.Angle(k.Aim - origin, c.Aim - origin) < 4f)) continue;
                chosen.Add(c);
            }
            return chosen;
        }

        /// <summary>K8 조준 레이저 직접 시험 (컨트롤타워 지시 — 계단 조준 레이저 ≥16 방향).
        /// 팀 레이저는 록온·발사에 시선 질의가 없고(CharacterLockedLaser.cs 전체에 Physics 호출 0, 후보 선정 :141-162) 벽에 끊기는지는 LaserBeam.Tick의 벽 질의 하나(LaserBeam.cs:84-92)가 정한다 —
        /// 그래서 도형이 벽 뒤에 서도 록온되며, 그때 빔이 벽에서 끊기는지가 16 버퍼 질의의 실제 영향이다.
        /// ① 이 레이저가 표적으로 삼을 수 있는 모든 서 있는 자리(KB_CollectAimCandidates)에서 그 자리의 실제 조준 방향으로 ①·②·재현 벽을 재고, ② ≥16 또는 재현 벽 누락인 자리만 "재현"으로 본다.
        /// ③ 재현된 자리 중 벽 뒤(가장 가까운 막는 벽 너머) 최대 KB_MaxBehindTrials곳 + 벽 앞 최대 KB_MaxFrontTrials곳에 대상 도형을 Hover 없이 세워 두고 록온·발사·피격을 본다
        /// (붙잡힌 몸은 팀 레이저가 무효 대상으로 본다). 벽 뒤 도형이 맞으면 '빔 벽 관통', 벽 앞 도형이 안 맞으면 '피격 누락'.
        /// ④ 표본 중 ≥16 방향마다 KB_DirTol° 안에 서 있는 자리가 하나도 없으면 "플레이 중 생길 수 없는 방향"으로 센다.</summary>
        private IEnumerator KB_AimExposure(CharacterLockedLaser al, KB_Exp r)
        {
            const string id = "S4-5";
            r.IsAim = true;
            if (al.targetKinds == null || al.targetKinds.Length == 0) { r.FrontMsg = r.BehindMsg = $"'{al.name}' targetKinds 비어 있음"; yield break; }
            PlayerMover v = ShapeOf(al.targetKinds[0]);
            Transform gen = s4.Gen;
            Vector3 o = al.transform.position;
            List<KB_Probe> samples;
            if (!kbAimTrig.TryGetValue(al, out samples)) samples = new List<KB_Probe>();
            r.AimDirs = samples.Count;

            List<KB_AimCand> cands = KB_CollectAimCandidates(al, v, out string scanInfo);
            r.AimCands = cands.Count;
            foreach (KB_AimCand c in cands) c.Probe = KB_ProbeBeam(al.beam, o, c.Aim - o, gen, al.name, "4", "후보 자리 조준 방향");
            List<KB_AimCand> behind = cands.Where(c => !c.Clear && (c.Probe.Over16 || c.Probe.TeamWallMissed)).ToList();
            List<KB_AimCand> front = cands.Where(c => c.Clear && (c.Probe.Over16 || c.Probe.TeamWallMissed)).ToList();
            r.AimEligible = behind.Count + front.Count;
            List<string> noSpot = new List<string>();
            foreach (KB_Probe sp in samples)
                if (!cands.Any(c => Vector3.Angle(c.Aim - o, sp.DirWorld) <= KB_DirTol)) noSpot.Add($"{sp.DirLabel}(① {sp.RayAll}·② {sp.SphereAll})");
            r.AimDirsNoSpot = noSpot.Count;
            Detail(id, $"  '{al.name}' ≥16 방향 직접 시험: 계기 표본 {samples.Count}개 · {scanInfo} · 실제 조준 방향에서 계기가 재현되는 자리 벽 뒤 {behind.Count}곳·벽 앞 {front.Count}곳 · 표본 중 {KB_DirTol:F0}° 안에 서 있는 자리가 없는(= 플레이 중 생길 수 없는) 방향 {noSpot.Count}개");
            foreach (string ns in noSpot) Detail(id, $"    설 자리 없는 ≥16 방향: {ns}");

            List<KB_AimCand> tests = KB_PickCands(behind, KB_MaxBehindTrials, o);
            tests.AddRange(KB_PickCands(front, KB_MaxFrontTrials, o));
            r.AimSkipped = r.AimEligible - tests.Count;
            if (r.AimSkipped > 0) Detail(id, $"    시간 예산으로 시험하지 않은 재현 자리 {r.AimSkipped}곳(레이저당 벽 뒤 {KB_MaxBehindTrials}·벽 앞 {KB_MaxFrontTrials} 상한, 방향 4° 이상 떨어진 순)");
            float observe = al.firstFireDelay + al.beamDuration + 2f;
            foreach (KB_AimCand c in tests)
            {
                bool isBehind = !c.Clear;
                string kind = isBehind ? "(가) 벽 뒤" : "(나) 벽 앞";
                string how = $"local{V(Lc(gen, c.Ground))} 거리 {c.Dist:F1}U·정면에서 {c.Ang:F1}°·{(c.Clear ? "시선 트임" : $"첫 고체 {c.Los:F1}U에서 가림")}";
                Detail(id, $"  {kind} 시험 직전 조준 방향 재측정: {how} · {KB_RowText(c.Probe)} → 실측 계기 재현");
                KB_AimResult a = new KB_AimResult();
                yield return KB_AimLockTrial(al, v, c.Ground, observe, a);
                bool ran = a.Locked && a.Fired;
                Detail(id, $"    {kind} 시험 결과: {KB_AimLine(al, v, how, a)}");
                if (!ran) r.AimInconclusive++;
                else
                {
                    r.AimReproduced++;
                    if (isBehind)
                    {
                        r.AimBehindRan++;
                        if (a.Hit) r.AimPierce.Add($"'{al.name}' 벽 뒤 자리 {how} 도형이 맞음(빔 벽 관통, ① {c.Probe.RayAll}·② {c.Probe.SphereAll}, 재현 벽 {c.Probe.WallTeam:F2}U/정확 {c.Probe.WallExact:F2}U)");
                    }
                    else
                    {
                        r.AimFrontRan++;
                        if (!a.Hit) r.AimMiss.Add($"'{al.name}' 벽 앞 자리 {how} 도형이 안 맞음(피격 누락, ① {c.Probe.RayAll}·② {c.Probe.SphereAll})");
                    }
                }
                foreach (PlayerMover p in players) RestoreGravityIfStuck(p, "S4-5 조준");
            }
            r.FrontMsg = r.BehindMsg = $"직접 시험 {tests.Count}건(재현 {r.AimReproduced}·불확정 {r.AimInconclusive})";
        }

        // ═════════════════════════ [K8] S4-5 · S5-5 ═════════════════════════

        private IEnumerator KB_S4_5_BeamBuffer()
        {
            const string id = "S4-5";
            if (!Ready4(id)) yield break;
            List<FixedPeriodicLaser> fl = s4.FixedLasers().ToList();
            List<CharacterLockedLaser> al = s4.AimStair.Concat(s4.AimBubble).ToList();
            yield return KB_BeamBufferCommon(id, "4", s4.Scene, s4.Gen, fl, al, s4.Park);
        }

        private IEnumerator KB_S5_5_BeamBuffer()
        {
            const string id = "S5-5";
            if (!Ready5(id)) yield break;
            yield return KB_BeamBufferCommon(id, "5", s5.Scene, s5.Gen, s5.Lasers.ToList(), new List<CharacterLockedLaser>(), s5.Park);
        }

        private IEnumerator KB_BeamBufferCommon(string id, string sector, Scene scene, Transform gen, List<FixedPeriodicLaser> fixedLasers, List<CharacterLockedLaser> aimLasers, Vector3[] park)
        {
            if (fixedLasers.Count + aimLasers.Count == 0) { NA(id, $"검사불가(이름 불일치): S{sector} 레이저(FixedPeriodicLaser·CharacterLockedLaser) 0개"); yield break; }
            Detail(id, "질의 확인(LaserBeam.cs): 벽 = Physics.RaycastNonAlloc(origin, dir, wallHitBuffer[16], range, wallMask, 트리거 무시) — 반지름 0인 '광선'(구 캐스트 아님), 결과 정렬 안 함(:84-92 최솟값 직접 계산); " +
                          "빔 = Physics.SphereCastNonAlloc(origin, beamRadius, dir, beamHitBuffer[16], dist(=가장 가까운 도형 아닌 충돌), ~0(레이어 전체), 트리거 무시)(:112), Player 태그만 피격(:116-118). " +
                          "버퍼가 차면 어느 16개가 남는지는 PhysX 순회 순서라 가장 가까운 벽이 빠질 수 있다(추측 — 아래 '팀 16 버퍼 재현' 열이 이 엔진에서 실제로 얻는 값).");
            if (aimLasers.Count > 0)
                Detail(id, "조준 레이저 시선(LOS) 확인(CharacterLockedLaser.cs): 목표를 잡을 때도 쏠 때도 Physics 질의가 한 번도 없다(파일 전체에 Physics·Raycast·Linecast 호출 0). 목표 후보 = Start에서 한 번 캐시한 FindObjectsOfType<PlayerShapeIdentity>(:65), " +
                              "FindNearestValidTarget(:141-162)은 targetKinds 일치 · IsCandidateValid(:126-137: activeInHierarchy·ExternallyDriven·isKinematic) · WithinAimAngle(:115-116, maxAimAngle) · sensorRange 거리만 본다. 발사는 FixedUpdate에서 beam.Tick(transform.position, lockedDirection, …)만 부른다(:69-101). " +
                              "즉 '벽에 가리면 안 쏜다'는 구조가 아니다 — 도형이 벽 뒤에 있어도 록온되고, 빔이 벽에서 끊기는지는 LaserBeam.Tick의 벽 질의 하나(LaserBeam.cs:84-92, RaycastNonAlloc 버퍼 16)가 전부 정한다. " +
                              "그 질의가 곧 시선 질의이고 이미 16 버퍼 재현 대상이다(표의 '팀 16 버퍼 재현' 열). 벽 뒤 도형이 맞는 경우는 그 질의가 벽을 놓칠 때만 생긴다.");
            yield return ParkPlayers(park, sphere, cube, tetra);
            List<KB_Probe> rows = new List<KB_Probe>();
            Dictionary<Component, KB_Probe> worstOf = new Dictionary<Component, KB_Probe>();
            Dictionary<Component, string> fanInfo = new Dictionary<Component, string>();
            List<string> bad = new List<string>(), na = new List<string>();

            foreach (FixedPeriodicLaser lz in fixedLasers)
            {
                if (lz.beam == null) { na.Add($"'{lz.name}' beam 비어 있음"); continue; }
                KB_Probe p = KB_ProbeBeam(lz.beam, lz.transform.position, lz.transform.forward, gen, lz.name, sector, "정면(고정 방향 transform.forward)");
                rows.Add(p);
                worstOf[lz] = p;
                // 팀 16 버퍼 재현이 벽을 놓쳐도(정확 벽 거리와 0.01U 초과로 다름) 그것만으로 실패로 치지 않는다 — ①·② ≥ 16과 같은 "실측 계기"일 뿐이고,
                // 판정은 도형 실측(벽 뒤 맞음 = 관통, 벽 앞 안 맞음 = 누락)으로만 낸다(컨트롤타워 판정). 재현 수치는 표에 그대로 남는다.
                if (p.TeamWallMissed) Detail(id, $"실측 계기: '{lz.name}' 실제 발사 방향에서 팀 16 버퍼 재현이 가장 가까운 벽을 놓침(정확 {p.WallExact:F2}U '{p.WallName}' ≠ 재현 {p.WallTeam:F2}U) — 도형 실측으로 판정");
            }
            int sumTotal = 0, sumOver = 0;
            foreach (CharacterLockedLaser al in aimLasers)
            {
                if (al.beam == null) { na.Add($"'{al.name}' beam 비어 있음"); continue; }
                KB_ProbeAim(al, gen, sector, rows, out int total, out int over, out KB_Probe worst);
                worstOf[al] = worst;
                sumTotal += total; sumOver += over;
                fanInfo[al] = $"'{al.name}' 원뿔 {al.maxAimAngle}° 표본 {total}개 중 16 이상 {over}개";
            }
            if (rows.Count == 0) { NA(id, "검사불가: 측정할 레이저 행 0(beam 비어 있음: " + string.Join(" / ", na) + ")"); yield break; }
            KB_AddTable(id, rows);
            foreach (string s in fanInfo.Values) Detail(id, "조준 레이저 방향 표본: " + s);
            List<LaserBeam> allBeams = InScene<LaserBeam>(scene);
            HashSet<LaserBeam> covered = new HashSet<LaserBeam>();
            foreach (FixedPeriodicLaser f0 in fixedLasers) if (f0.beam != null) covered.Add(f0.beam);
            foreach (CharacterLockedLaser a0 in aimLasers) if (a0.beam != null) covered.Add(a0.beam);
            List<LaserBeam> orphan = allBeams.Where(b => !covered.Contains(b)).ToList();
            Detail(id, $"LaserBeam 컴포넌트 {allBeams.Count}개 = 고정 레이저 소속 {fixedLasers.Count(f1 => f1.beam != null)} + 조준 레이저 소속 {aimLasers.Count(a1 => a1.beam != null)} + 소속 기믹 없음 {orphan.Count}" +
                          (orphan.Count > 0 ? $" [{string.Join(", ", orphan.Select(b => b.name))}] — Tick을 부르는 주체가 없어 발사하지 않음" : ""));

            // 요약
            KB_Probe top1 = rows.OrderByDescending(x => x.RayAll).First(), top2 = rows.OrderByDescending(x => x.SphereAll).First();
            int playersOnBeam = rows.Sum(x => x.RayPlayer);
            int rowsOver = rows.Count(x => x.Over16), rowsMissed = rows.Count(x => x.TeamWallMissed);
            string summary = $"레이저 {fixedLasers.Count + aimLasers.Count}개(고정 {fixedLasers.Count}·조준 {aimLasers.Count}) 표 {rows.Count}행: ① 최대 {top1.RayAll}('{top1.Laser}' {top1.DirLabel}) · ② 최대 {top2.SphereAll}('{top2.Laser}' {top2.DirLabel}) · " +
                             $"16 이상 행 {rowsOver} · 팀 16 버퍼 재현 벽 누락 행 {rowsMissed}(실측 계기일 뿐 판정은 도형 실측)" + (aimLasers.Count > 0 ? $" · 조준 원뿔 표본 {sumTotal}개 중 16 이상 {sumOver}" : "") + $" · 빔 선상 도형 {playersOnBeam}(0이어야 파킹 정상)";
            if (top1.RayAll >= 8) Detail(id, $"① 최대 행 콜라이더(가까운 순 최대 8): {top1.TopNames}");
            if (top2.SphereAll >= 8 && top2 != top1) Detail(id, $"② 최대 행 벽({top2.WallExact:F2}U)까지 빔 굵기 안 콜라이더는 SphereCast 결과 {top2.SphereAll}개(이름은 ① 목록 참고: {top2.TopNames})");

            // 16 이상인 레이저의 실측
            List<Component> overList = worstOf.Where(kv => kv.Value.Over16 || kv.Value.TeamWallMissed).Select(kv => kv.Key).ToList();
            List<string> pierce = new List<string>(), miss = new List<string>();
            int reproduced = 0, notReproduced = 0;   // 계기가 재현돼 실제로 시험한 건수 / 계기 미재현(또는 자리 없음)으로 시험 못 한 방향 수 — 통과 문구는 앞의 것만 센다
            int aimLasersTested = 0, aimDirs = 0, aimNoSpot = 0, aimReproduced = 0, aimBehindRan = 0, aimFrontRan = 0, aimInconclusive = 0, aimSkipped = 0;
            foreach (Component c in overList)
            {
                KB_Exp ex = new KB_Exp();
                if (c is FixedPeriodicLaser) ex.FrontTriggered = ex.BehindTriggered = true;   // 고정 레이저는 방향이 하나 — 표의 행이 곧 시험 방향이고 overList는 그 행이 계기(≥16 또는 재현 벽 누락)일 때만 들어온다
                KB_Probe wp = worstOf[c];
                Detail(id, $"실측 대상(16 이상 또는 재현 벽 누락) '{c.name}' — 최악 방향 {wp.DirLabel}: ① {wp.RayAll} ② {wp.SphereAll} (재현 벽 {wp.WallTeam:F2}U / 정확 {wp.WallExact:F2}U)");
                if (c is FixedPeriodicLaser lz) yield return KB_FixedExposure(lz, cube, gen, park, ex);
                else if (c is CharacterLockedLaser al && s4 != null) yield return KB_AimExposure(al, ex);
                if (ex.IsAim)
                {
                    aimLasersTested++;
                    aimDirs += ex.AimDirs; aimNoSpot += ex.AimDirsNoSpot; aimReproduced += ex.AimReproduced;
                    aimBehindRan += ex.AimBehindRan; aimFrontRan += ex.AimFrontRan; aimInconclusive += ex.AimInconclusive; aimSkipped += ex.AimSkipped;
                    reproduced += ex.AimReproduced;
                    pierce.AddRange(ex.AimPierce);
                    miss.AddRange(ex.AimMiss);
                    yield return ParkPlayers(park, sphere, cube, tetra);
                    continue;
                }
                Detail(id, $"  (나) 벽 앞 도형 피격: {(ex.FrontRan ? (ex.FrontHit ? "맞음(정상)" : "안 맞음 — 피격 누락") : "실측 못 함")} — {ex.FrontMsg}");
                Detail(id, $"  (가) 벽 뒤 도형 피격: {(ex.BehindRan ? (ex.BehindHit ? "맞음 — 빔 벽 관통" : "안 맞음(정상)") : "실측 못 함")} — {ex.BehindMsg}");
                if (ex.BehindRan && ex.BehindHit) pierce.Add($"'{c.name}' 벽 뒤 도형이 맞음(빔 벽 관통)");
                if (ex.FrontRan && !ex.FrontHit) miss.Add($"'{c.name}' 벽 앞 도형이 안 맞음(피격 누락)");
                if (ex.FrontRan && ex.FrontTriggered) reproduced++;
                if (ex.BehindRan && ex.BehindTriggered) reproduced++;
                if (!ex.FrontTriggered) notReproduced++;
                if (!ex.BehindTriggered) notReproduced++;
                if (!ex.FrontRan || !ex.BehindRan)
                    na.Add($"'{c.name}' 실측 불완전(앞 {(ex.FrontRan ? "함" : ex.FrontTriggered ? "못 함" : "실측 계기 미재현")}·뒤 {(ex.BehindRan ? "함" : ex.BehindTriggered ? "못 함" : "실측 계기 미재현")})");
                yield return ParkPlayers(park, sphere, cube, tetra);
            }

            bad.AddRange(pierce);
            bad.AddRange(miss);
            if (overList.Count > 0) summary += $" · 실측 계기 재현 시험 {reproduced}건·미재현/불가 방향 {notReproduced}개";
            // 조준 레이저: 재현된 시험이 1건 이상이고 관통·누락 0이면 통과 / 재현 0이고 ≥16 방향이 전부 '설 자리 없음'이면 통과(플레이 중 생길 수 없음) / 그 외 검사불가 (컨트롤타워 판정)
            bool aimNoStand = false;
            if (aimLasersTested > 0)
            {
                summary += $" · 조준 레이저 {aimLasersTested}개 ≥16 방향: 계기 표본 {aimDirs}개 중 설 자리 없음 {aimNoSpot}개 · 직접 시험 {aimReproduced}건(벽 뒤 {aimBehindRan}·벽 앞 {aimFrontRan}) · 결과 불확정 {aimInconclusive}건 · 시간 예산으로 미시험 {aimSkipped}곳";
                if (bad.Count == 0 && reproduced == 0)
                {
                    if (aimDirs > 0 && aimNoSpot == aimDirs) aimNoStand = true;
                    else na.Add($"조준 레이저 ≥16 방향 {aimDirs}개 중 설 자리 없음 {aimNoSpot}개뿐이고 재현된 직접 시험 0건(불확정 {aimInconclusive}건) — 결론을 낼 수 없음");
                }
            }
            if (bad.Count > 0) Fail(id, summary + " — 실패: " + string.Join(" / ", bad) + " — 기믹은 고치지 않는다: 팀 보고·맵 대안(콜라이더 합치기 등)은 사용자 결정");
            else if (na.Count > 0) NA(id, summary + " — 검사불가: " + string.Join(" / ", na));
            else if (aimNoStand) Pass(id, summary + " — 조준 레이저의 ≥16 방향 전부 도형이 설 수 있는 자리가 없다: 실제 플레이에서 16 이상 방향에 설 수 없어 벽 관통·피격 누락이 생길 수 없음");
            else if (overList.Count > 0) Pass(id, summary + " — 실측 계기가 재현된 방향 " + reproduced + "건(레이저 " + overList.Count + "개)을 시험해 벽 관통·피격 누락 없음");
            else Pass(id, summary + " — 16 이상 없음(버퍼 여유 최소 " + (KB_BufferLimit - Mathf.Max(top1.RayAll, top2.SphereAll)) + ")");
        }

        // ═════════════════════════ [K9] S4-6 ═════════════════════════

        private static int KB_SceneIndex(Scene s)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).handle == s.handle) return i;
            return -1;
        }

        private IEnumerator KB_S4_6_AimLockAll()
        {
            const string id = "S4-6";
            if (!Ready4(id)) yield break;
            Transform gen = s4.Gen;
            List<CharacterLockedLaser> all = s4.AimStair.Concat(s4.AimBubble).ToList();
            if (all.Count == 0) { NA(id, $"검사불가(이름 불일치): 'S4_AL_Stair_*'/'S4_AL_Bubble_*' 없음(CharacterLockedLaser 전체 {s4.AllAim})"); yield break; }

            // Start 시점 증거 — 팀 CharacterLockedLaser는 플레이어 목록을 Start에서 한 번만 모은다(CharacterLockedLaser.cs:59-67, FindObjectsOfType<PlayerShapeIdentity>).
            int idCount = Object.FindObjectsOfType<PlayerShapeIdentity>().Length;
            string playerScenes = string.Join(", ", players.Select(p => $"{p.gameObject.scene.name}(로드 순번 {KB_SceneIndex(p.gameObject.scene)})").Distinct());
            string laserScenes = string.Join(", ", all.Select(a => $"{a.gameObject.scene.name}(로드 순번 {KB_SceneIndex(a.gameObject.scene)})").Distinct());
            int pIdx = players.Count > 0 ? KB_SceneIndex(players[0].gameObject.scene) : -1;
            int lIdx = KB_SceneIndex(all[0].gameObject.scene);
            bool playersFirst = pIdx >= 0 && lIdx >= 0 && pIdx < lIdx;
            Detail(id, $"[레이저 Start 전 플레이어 존재] 플레이어 {idCount}명(PlayerShapeIdentity — 팀 레이저가 Start에서 모으는 같은 타입) 소속 씬 [{playerScenes}] / 조준 레이저 {all.Count}개 소속 씬 [{laserScenes}] → " +
                       (playersFirst ? "플레이어 씬이 레이저 씬보다 먼저 로드됨 — S4 씬이 추가 로드되어 레이저 Start가 돌 때 플레이어 3명은 이미 있었다" : "플레이어 씬 순번이 레이저 씬보다 앞서지 않음 — 확인 필요") +
                       $" · 레이저 활성 {all.Count(a => a.isActiveAndEnabled)}/{all.Count}");
            Detail(id, "[로드 순서 코드 근거] 게임: Master 씬(빌드 목록 첫 번째, EditorBuildSettings.asset)의 플레이어 3명(씬 상주, Map4Director.cs:76-87 Awake에서 중력 끔) → Map4Director.Start(:89-92) → RunStartup(:94-106) → LoadAllSectors(:108-)가 " +
                       "LoadSceneAsync(Additive)(:121)로 Map4_S1~S8을 차례로 로드 → 각 섹터 오브젝트의 Awake/OnEnable/Start는 그 씬 로드 뒤. 러너: Run()이 Master 씬을 열고 플레이 모드 진입(Map4FunctionalChecks.cs:307-313) → 같은 Director 경로 → " +
                       "Setup()이 director.LoadedSectorCount ≥ 8까지 기다린 뒤 진행(:1186-1189). 즉 러너 순서 = 게임 순서(Master 플레이어 → 섹터 추가 로드).");
            Detail(id, "참고: 레이저가 비활성 루트 안에 있어 나중에 켜져도 Start는 그때 돈다 — 플레이어는 그보다 먼저 존재하므로 같은 결론.");

            CheckItem it42 = report.Get("S4-2");
            CheckItem it46 = report.Get(id);   // 레이저별 하위 결과(키 = 레이저 이름) — 자리 없음은 그 레이저만 하위 결과 검사불가, 항목 판정은 나머지로 낸다(컨트롤타워 판정 7)
            List<string> bad = new List<string>(), na = new List<string>();
            int locked = 0;
            foreach (CharacterLockedLaser al in all)
            {
                bool stair = s4.AimStair.Contains(al);
                // S4-2 하위 결과 재사용 — S4-2는 종류별 첫 레이저만 시도한다(조준 시도가 통과 = 피격 + 복귀 + 목적지 → 록온 포함).
                SubResult sr = it42.Subs.FirstOrDefault(x => x.Key == (stair ? "aimStair" : "aimBubble"));
                bool firstOfGroup = (stair ? s4.AimStair : s4.AimBubble).FirstOrDefault() == al;
                if (firstOfGroup && sr != null && sr.Status == SubStatus.Pass)
                {
                    locked++;
                    Detail(id, $"'{al.name}': S4-2 하위 결과 '{sr.Name}' 통과 재사용(피격 + 복귀 + 목적지 확인 → 목표 잡음). 새 시도 생략.");
                    it46.SetSub(al.name, al.name, SubStatus.Pass, "S4-2 하위 결과 통과 재사용(피격 + 복귀 + 목적지 확인 → 목표 잡음)");
                    continue;
                }
                if (al.targetKinds == null || al.targetKinds.Length == 0) { bad.Add($"'{al.name}' targetKinds 비어 있음"); it46.SetSub(al.name, al.name, SubStatus.Fail, "targetKinds 비어 있음"); continue; }
                PlayerMover v = ShapeOf(al.targetKinds[0]);
                Vector3 ground = default;
                string how = null;
                bool ok = false;
                bool rise = false;
                if (!stair && s4.BubbleBox != null && TryAimRiseSpot(al, out Vector3 rg, out string riseWhy)) { ok = true; rise = true; ground = rg; how = "버블 안에서 떠오르는 도형 — " + riseWhy; }
                if (!ok) ok = KB_FindAimSpot(al, v, 0, out ground, out how);
                if (!ok)
                {
                    string why = $"검사불가(자리 없음): 대상 {al.targetKinds[0]}, 원뿔 {al.maxAimAngle}° 사거리 {al.sensorRange}, 발사점 local{V(Lc(gen, al.transform.position))} — 자리 탐색 결과: {how}";
                    na.Add($"'{al.name}' {why}");
                    Detail(id, $"'{al.name}': {why}");
                    it46.SetSub(al.name, al.name, SubStatus.NA, why);
                    continue;
                }
                KB_AimResult r = new KB_AimResult();
                float observe = rise ? al.firstFireDelay + al.beamDuration + 2.5f : al.firstFireDelay + al.beamDuration + 2f;
                yield return KB_AimLockTrial(al, v, ground, observe, r);
                Detail(id, KB_AimLine(al, v, (rise ? "(떠오름) " : "") + how, r));
                if (r.Locked) locked++;
                else bad.Add($"'{al.name}' 록온 못 함({r.Msg})");
                it46.SetSub(al.name, al.name, r.Locked ? SubStatus.Pass : SubStatus.Fail, KB_AimLine(al, v, (rise ? "(떠오름) " : "") + how, r));
                yield return ParkPlayers(s4.Park, sphere, cube, tetra);
                yield return new WaitForSeconds(0.3f);
                foreach (PlayerMover p in players) RestoreGravityIfStuck(p, "S4-6");
            }
            string head = $"조준 레이저 {all.Count}개 중 목표 잡음 {locked}개(계단 {s4.AimStair.Count}·버블 {s4.AimBubble.Count}, 플레이어 {idCount}명이 레이저 Start 전부터 존재: {(playersFirst ? "예" : "확인 필요")})";
            // 항목 판정: 못 잡은 레이저가 있으면 실패, 모두 못 시험했으면 검사불가, 그 밖에는 시험한 레이저 전부 잡았으면 통과(자리 없는 레이저는 하위 결과 검사불가로만 남긴다).
            if (bad.Count > 0) Fail(id, head + " — 실패: " + string.Join(" / ", bad) + (na.Count > 0 ? " / 하위 검사불가: " + string.Join(" / ", na) : ""));
            else if (locked == 0) NA(id, head + " — 검사불가: " + string.Join(" / ", na));
            else Pass(id, head + (na.Count > 0 ? $" — 자리 없어 하위 결과 검사불가 {na.Count}개(항목 판정은 나머지 {locked}개로): " + string.Join(" / ", na) : ""));
        }

        // ═════════════════════════ [K12] S7-5 ═════════════════════════

        private IEnumerator KB_S7_5_RoleSlotPriority()
        {
            const string id = "S7-5";
            if (!Ready7(id)) yield break;
            Transform gen = s7.Gen;
            List<RoleSlot> slots = InScene<RoleSlot>(s7.Scene);
            RoleSlot book = slots.FirstOrDefault(x => x.roleId == "Book") ?? slots.FirstOrDefault(x => x.name == "S7_Slot_Book");
            RoleSlot comp = slots.FirstOrDefault(x => x.roleId == "Computer") ?? slots.FirstOrDefault(x => x.name == "S7_Slot_Computer");
            if (book == null || comp == null) { NA(id, "검사불가(이름 불일치): 책/컴퓨터 RoleSlot(roleId 'Book'·'Computer' 또는 이름 S7_Slot_Book·_Computer)을 못 찾음"); yield break; }
            if (Object.FindObjectOfType<InteractionController>() == null) { NA(id, "검사불가: 씬에 InteractionController 없음(팀 중앙 입력이 아직 안 만들어짐)"); yield break; }
            if (Object.FindObjectOfType<PlayerControlSwitcher>() == null) { NA(id, "검사불가: 씬에 PlayerControlSwitcher 없음 — 조작권을 넘길 방법이 없다"); yield break; }

            // S7_GimmickRoot는 도형이 전실 활성기에 들어와야 켜진다([판정 13]) — S7-1 주행이 켜 두었는지 보고, 꺼져 있으면 활성기 구역에 도형을 한 번 넣어 본다.
            if (!book.isActiveAndEnabled || !comp.isActiveAndEnabled)
            {
                Lab_ActivateWhenPlayersReady act = InScene<Lab_ActivateWhenPlayersReady>(s7.Scene).FirstOrDefault();
                if (act != null && act.zone != null && s7.GimmickRoot != null)
                {
                    Vector3 zc = act.zone.bounds.center;
                    yield return WaitIdle(cube, 5f);
                    Teleport(cube, GroundedSpot(zc + Vector3.up * 2f, 8f, null, zc));
                    float ta = Time.time;
                    while (!s7.GimmickRoot.gameObject.activeSelf && Time.time - ta < 3f) yield return null;
                    Detail(id, $"기믹 루트가 꺼져 있어 전실 활성기 구역 중앙에 네모를 넣음 → 루트 activeSelf {s7.GimmickRoot.gameObject.activeSelf}");
                    yield return new WaitForSeconds(0.3f);
                }
                if (!book.isActiveAndEnabled || !comp.isActiveAndEnabled) { NA(id, $"검사불가: RoleSlot이 비활성(책 {book.isActiveAndEnabled}·컴퓨터 {comp.isActiveAndEnabled}) — S7_GimmickRoot 미활성"); yield break; }
            }

            BoxCollider bt = book.GetComponent<BoxCollider>(), ct = comp.GetComponent<BoxCollider>();
            string trigTxt;
            if (bt != null && ct != null)
            {
                Bounds b1 = bt.bounds, b2 = ct.bounds;
                float gx = Mathf.Max(0f, Mathf.Abs(b1.center.x - b2.center.x) - (b1.extents.x + b2.extents.x));
                float gz = Mathf.Max(0f, Mathf.Abs(b1.center.z - b2.center.z) - (b1.extents.z + b2.extents.z));
                trigTxt = b1.Intersects(b2) ? "슬롯 트리거 겹침" : $"슬롯 트리거 비겹침(틈 {Mathf.Sqrt(gx * gx + gz * gz):F2}U)";
            }
            else trigTxt = "슬롯 트리거 정보 없음";
            Detail(id, $"RoleSlot 책 '{book.name}' local{V(Lc(gen, book.transform.position))} 트리거 {(bt != null ? BL(LocalBoxBounds(bt, gen)) : "없음")} · 컴퓨터 '{comp.name}' local{V(Lc(gen, comp.transform.position))} 트리거 {(ct != null ? BL(LocalBoxBounds(ct, gen)) : "없음")} · " +
                       $"수평 간격 {HorizDist(book.transform.position, comp.transform.position):F2}U · 트리거 겹침 {(bt != null && ct != null && bt.bounds.Intersects(ct.bounds) ? "있음" : "없음")}");
            Detail(id, "RoleSlot 액션: 손(E) 탭 · 우선순위 Panel(300) · 거리 = 조작 도형 피벗 ↔ 슬롯 위치 3D(RoleSlot.cs:109-125), 도형이 슬롯 트리거 안에 있을 때만 올림(overlaps, :111-112)");

            Dictionary<PlayerMover, Vector3> origPos = players.Where(p => p != null).ToDictionary(p => p, p => p.transform.position);
            List<string> bad = new List<string>(), na = new List<string>();
            PlayerMover[] shapes = { sphere, cube, tetra };
            string[] shapeNames = { "구", "네모", "세모" };
            Vector3 midPoint = (book.transform.position + comp.transform.position) * 0.5f;
            // 원복(도형 위치 + 조작권)은 멱등 클로저 하나로 — 아래 finally와 러너 정리 목록(cleanups) 양쪽이 부른다.
            // 메인 RunSubStep은 중첩 도우미(WaitIdle·KB_GiveControl·ParkPlayers 등)에서 난 예외를 잡으면 스택의 바깥 반복자를 Dispose하지 않아 그 finally가 안 돈다(검토 L8)
            // — 그 경로에서는 cleanups(러너 Finish)가 복원한다. 그때까지(= S8 블록 동안) 후속 검사는 바뀐 조작권·도형 위치로 돈다.
            bool restored7 = false;
            Action restore7 = () =>
            {
                if (restored7) return;
                restored7 = true;
                foreach (KeyValuePair<PlayerMover, Vector3> kv in origPos) if (kv.Key != null) Teleport(kv.Key, kv.Value);
                KB_RestoreControl();
            };
            cleanups.Add(restore7);
            try
            {
                for (int s = 0; s < shapes.Length; s++)
                {
                    PlayerMover v = shapes[s];
                    float lift = Mathf.Max(0.3f, SolidBounds(v).extents.y);
                    // 서는 자리 3곳 — 컴퓨터 바로 앞(컴퓨터 트리거 안 · 컴퓨터가 책보다 0.3U 이상 가까움) · 책 바로 앞 · 둘의 가운데(수평 중점)
                    RoleSlot[] tgt = { comp, book, null };
                    string[] spotName = { "컴퓨터 바로 앞", "책 바로 앞", "둘의 가운데" };
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 ground;
                        string how;
                        bool found;
                        if (k < 2)
                        {
                            RoleSlot t = tgt[k], other = k == 0 ? book : comp;
                            BoxCollider tc = k == 0 ? ct : bt;
                            Func<Vector3, bool> accept = g => HorizDist(g, t.transform.position) + 0.3f < HorizDist(g, other.transform.position) && (tc == null || tc.bounds.Contains(g + gen.up * lift));
                            found = KB_StandNear(gen, t.transform.position, 0.4f, 2.4f, lift, accept, out ground, out how);
                        }
                        else found = KB_StandNear(gen, midPoint, 0f, 1.5f, lift, null, out ground, out how);
                        if (!found) { na.Add($"{shapeNames[s]}·{spotName[k]}: 서 있을 자리 없음({how})"); Detail(id, $"{shapeNames[s]} '{v.name}' · {spotName[k]}: 자리 없음 — {how}"); continue; }

                        yield return KB_GiveControl(v);
                        yield return WaitIdle(v, 5f);
                        Teleport(v, ground + gen.up * SpawnLift);
                        yield return new WaitForSeconds(KB_StartCaptureWait);
                        bool controlled = InteractionController.Controlled == v;                 // InteractionController.cs:41
                        List<KB_Cand> list = KB_Collect(v, out bool gripped);
                        KB_Pick(list, InteractionChannel.Hand, out KB_Cand tap, out KB_Cand blocked, out KB_Cand hold, out bool tie);
                        bool inBook = bt != null && bt.bounds.Contains(v.transform.position), inComp = ct != null && ct.bounds.Contains(v.transform.position);
                        string line = $"{shapeNames[s]} '{v.name}' · {spotName[k]} local{V(Lc(gen, v.transform.position))}(자리 {how}) · 조작 확인 {(controlled ? "예" : "아니오")} · 붙잡힘 {gripped} · 책 트리거 안 {inBook}·컴퓨터 트리거 안 {inComp} · " +
                                      $"손(E) 후보: {KB_DescAll(list, InteractionChannel.Hand)} => 탭 승자 {KB_Desc(tap)}{(tie ? " (동률 — 팀은 먼저 올린 쪽)" : "")} · 회색 승자 {KB_Desc(blocked)} · 홀드 승자 {KB_Desc(hold)}";
                        Detail(id, line);
                        if (!controlled) { na.Add($"{shapeNames[s]}·{spotName[k]}: 조작권 확인 실패(Controlled='{KB_ShapeName(InteractionController.Controlled)}')"); continue; }
                        if (k == 2) continue;   // 가운데는 정보(승자 기록)
                        RoleSlot expect = k == 0 ? comp : book;
                        bool expectOffered = list.Any(c => c.Provider == expect);
                        if (tap == null && blocked == null && hold == null) na.Add($"{shapeNames[s]}·{spotName[k]}: 후보 0(트리거 진입 미등록 의심 — 검사 도구 한계 가능, {(k == 0 ? ct : bt) != null && (k == 0 ? inComp : inBook)})");
                        else if (tap != null && tap.Provider == expect) { /* 정상 */ }
                        else if (!expectOffered) na.Add($"{shapeNames[s]}·{spotName[k]}: 기대한 '{expect.name}'이 후보에 없음(트리거 안 {(k == 0 ? inComp : inBook)}) — 승자 {KB_Desc(tap)}");
                        else bad.Add($"{shapeNames[s]}·{spotName[k]}: 기대 '{expect.name}' 대신 {KB_Desc(tap)}가 이김");
                    }
                    Teleport(v, origPos[v]);
                    yield return new WaitForSeconds(0.2f);
                }
            }
            finally
            {
                restore7();
            }
            yield return new WaitForSeconds(0.3f);   // 복원 뒤 InteractionController.Update가 Controlled를 갱신할 시간
            string ctlNote = KB_ControlCheck(book.manager, out bool ctlOk);
            Detail(id, ctlNote + " · 도형 3개를 시작 때 자리로 되돌림(execute는 한 번도 호출하지 않음 — 조회만)");
            if (!ctlOk) bad.Add("조작권 원복 실패 — " + ctlNote);
            string tail = " — " + trigTxt + " · " + ctlNote;
            if (bad.Count > 0) Fail(id, string.Join(" / ", bad.Concat(na)) + tail);
            else if (na.Count > 0) NA(id, string.Join(" / ", na) + tail);
            else Pass(id, "도형 3종 × 자리 3곳: 컴퓨터 바로 앞에서는 컴퓨터, 책 바로 앞에서는 책이 손(E) 탭 승자(가운데는 승자 기록)" + tail);
        }

        // ═════════════════════════ [K13 ②③] S8-5 ═════════════════════════

        private IEnumerator KB_S8_5_StartZoneAndKeyE()
        {
            const string id = "S8-5";
            if (!Ready8(id)) yield break;
            Transform gen = s8.Gen;
            List<string> bad = new List<string>(), na = new List<string>(), tail = new List<string>();   // tail = 사유 꼬리(조작권 확인·Reveal 공개 호출 명시)
            List<ManagerChapterController> ctrls = InScene<ManagerChapterController>(s8.Scene);
            ManagerChapterController ctrl = ctrls.Count == 1 ? ctrls[0] : null;
            AddName("S8 챕터 컨트롤러(ManagerChapterController)", "CH8_ManagerChapterController ×1", ctrls.Count == 0 ? "(없음)" : string.Join("|", ctrls.Select(x => PathUnder(x.transform, gen))), ctrls.Count == 1 && ctrls[0].name == "CH8_ManagerChapterController");

            // ② 시작 구역 밑면 = 서고 바닥 윗면 −1 (허용 ±0.05)
            if (ctrl == null || ctrl.startZone == null) na.Add($"② 시작 구역 비교 못 함: ManagerChapterController {ctrls.Count}개 / startZone {(ctrl == null ? "-" : Nm(ctrl.startZone))}");
            else
            {
                Vector3 bottomW = ctrl.startZone.position + ctrl.startZone.rotation * new Vector3(0f, -ctrl.startZoneSize.y * 0.5f, 0f);
                Vector3 topW = ctrl.startZone.position + ctrl.startZone.rotation * new Vector3(0f, ctrl.startZoneSize.y * 0.5f, 0f);
                float bottomL = Lc(gen, bottomW).y, topL = Lc(gen, topW).y;
                Transform floorT = FindInScene(s8.Scene, "GEO_S8_Floor_Main");
                Collider fc = floorT != null ? floorT.GetComponent<Collider>() : null;
                if (fc == null && floorT != null) fc = FirstSolid(floorT.gameObject);
                float floorTop = float.NaN;
                string src = "";
                if (fc != null) { floorTop = LocalBoxBounds(fc, gen).max.y; src = "GEO_S8_Floor_Main 콜라이더 윗면"; }
                // 실측 보조: 구역 안 5곳에서 아래로 쏜 첫 면(도형·탄 제외)의 섹터 로컬 y
                List<string> rayInfo = new List<string>();
                Vector3 cW = ctrl.startZone.position;
                List<float> rayYs = new List<float>();
                foreach (Vector2 off in new[] { new Vector2(0f, 0f), new Vector2(-0.25f, 0f), new Vector2(0.25f, 0f), new Vector2(0f, -0.25f), new Vector2(0f, 0.25f) })
                {
                    Vector3 from = cW + ctrl.startZone.right * (off.x * ctrl.startZoneSize.x) + ctrl.startZone.forward * (off.y * ctrl.startZoneSize.z);
                    if (TryGround2(from, ctrl.startZoneSize.y + 8f, null, out Vector3 gp, out _)) { float y = Lc(gen, gp).y; rayYs.Add(y); rayInfo.Add($"{y:F2}"); }
                    else rayInfo.Add("없음");
                }
                if (float.IsNaN(floorTop) && rayYs.Count > 0) { floorTop = rayYs.OrderBy(y => y).ElementAt(rayYs.Count / 2); src = "구역 안 5곳 레이 중앙값(바닥 콜라이더 이름 못 찾음)"; }
                if (float.IsNaN(floorTop)) na.Add("② 서고 바닥 윗면을 못 구함(GEO_S8_Floor_Main도 레이도 실패)");
                else
                {
                    float expect = floorTop - 1f;
                    bool okBottom = Mathf.Abs(bottomL - expect) <= 0.05f;
                    Detail(id, $"② 시작 구역 '{ctrl.startZone.name}' 중심 local{V(Lc(gen, ctrl.startZone.position))} 크기 {V(ctrl.startZoneSize)} → 밑면 local y {bottomL:F3} · 윗면 y {topL:F3} / 서고 바닥 윗면 y {floorTop:F3}({src}; 구역 안 레이 5곳 y [{string.Join(", ", rayInfo)}]) → 기대 밑면 {expect:F3}(허용 ±0.05) {(okBottom ? "일치" : "다름")}");
                    Vector3 pin = gen.TransformPoint(new Vector3(Lc(gen, cW).x, floorTop - 0.9f, Lc(gen, cW).z)), pout = gen.TransformPoint(new Vector3(Lc(gen, cW).x, floorTop - 1.1f, Lc(gen, cW).z));
                    bool inOk = ctrl.InStartZone(pin), outOk = !ctrl.InStartZone(pout);
                    Detail(id, $"② InStartZone 확인: 바닥 −0.9 지점 {(inOk ? "안(정상)" : "밖(이상)")} · 바닥 −1.1 지점 {(outOk ? "밖(정상)" : "안(이상)")}  [세모 피벗이 바닥 아래여도 서 있으면 구역 안이어야 한다 — ManagerChapterController.cs:59-60]");
                    if (!okBottom) bad.Add($"② 시작 구역 밑면 y {bottomL:F3} ≠ 서고 바닥 윗면 {floorTop:F3} − 1 = {expect:F3}");
                    if (!inOk || !outOk) bad.Add($"② InStartZone 경계 이상(−0.9 지점 {(inOk ? "안" : "밖")}, −1.1 지점 {(outOk ? "밖" : "안")})");
                }
            }

            // ③ 열쇠 줍기·문 열기·완성물 사용이 E 중앙 입력 규칙으로 되는가
            ManagerKeyPoint kp = InScene<ManagerKeyPoint>(s8.Scene).FirstOrDefault();
            ManagerKeyDoor kd = InScene<ManagerKeyDoor>(s8.Scene).FirstOrDefault();
            ManagerUsePoint up = InScene<ManagerUsePoint>(s8.Scene).FirstOrDefault();
            if (kp == null || kd == null || kd.door == null || kp.manager == null) na.Add($"③ 열쇠 흐름 검사불가: ManagerKeyPoint {Nm(kp)} · ManagerKeyDoor {Nm(kd)} · door {(kd != null ? Nm(kd.door) : "-")} · manager {(kp != null ? Nm(kp.manager) : "-")}");
            else if (ctrls.Count > 1) na.Add($"③ 검사불가: ManagerChapterController {ctrls.Count}개 — 자동 시작 차단(SetParticipantPaused)을 어느 쪽에 걸지 정할 수 없음");
            else if (Object.FindObjectOfType<InteractionController>() == null) na.Add("③ 검사불가: 씬에 InteractionController 없음");
            else if (Object.FindObjectOfType<PlayerControlSwitcher>() == null) na.Add("③ 검사불가: 씬에 PlayerControlSwitcher 없음 — 열쇠·문은 manager.ControlledPlayer()(스위처 활성 도형)를 쓴다");
            else if (!kp.isActiveAndEnabled || !kd.isActiveAndEnabled) na.Add($"③ 검사불가: 열쇠 자리 활성 {kp.isActiveAndEnabled}·열쇠 문 활성 {kd.isActiveAndEnabled}");
            else yield return KB_S8KeyFlow(kp, kd, up, ctrl, bad, na, tail);

            string tailTxt = tail.Count > 0 ? " — " + string.Join(" · ", tail) : "";
            if (bad.Count > 0) Fail(id, string.Join(" / ", bad.Concat(na)) + tailTxt);
            else if (na.Count > 0) NA(id, string.Join(" / ", na) + tailTxt);
            else Pass(id, "② 시작 구역 밑면 = 서고 바닥 윗면 −1 (±0.05) · ③ 열쇠 줍기·문 열기가 손(E) 탭 중앙 입력 규칙으로 실행되어 문이 열림, 완성물 사용 지점도 E 탭 액션으로 올라옴(회색: 완성물 없음) — 검사 뒤 열쇠·문·컨트롤러·조작권 원복" + tailTxt);
        }

        /// <summary>S8 열쇠 흐름. 관리자 자동 시작(ManagerChapterController.Update — 구역 안 도형이 있으면 시작, ManagerChapterController.cs:151-157)을 막으려 검사 동안만 팀 공개
        /// SetParticipantPaused(true)(:342 — Update가 Paused에서 멈춘다 :156)를 건다. 컴포넌트 enabled는 건드리지 않는다(OnDisable→Unbind가 usePoint 리스너·ChapterReset 구독을 푼다, :104-139 — 컨트롤타워 판정).
        /// 되돌림은 전부 팀 공개 메서드: SetParticipantPaused(원래 값) + ResetAll()(:330) + ManagerKeyPoint.ResetKey · ManagerKeyDoor.Close · 조작권 복원 · 도형 자리 복원.
        /// ResetAll→ResetToStart가 PathChaserAgent.speed를 patrolSpeed로 덮으므로 원래 값으로 다시 돌린다(S8-1 정리와 같은 처리). 예외·워치독에 대비해 정리 목록(cleanups)에도 같은 동작을 등록한다.</summary>
        private IEnumerator KB_S8KeyFlow(ManagerKeyPoint kp, ManagerKeyDoor kd, ManagerUsePoint up, ManagerChapterController ctrl, List<string> bad, List<string> na, List<string> tail)
        {
            Transform gen = s8.Gen;
            PlayerMover v = cube;
            Dictionary<PlayerMover, Vector3> origPos = players.Where(p => p != null).ToDictionary(p => p, p => p.transform.position);
            bool pausedWas = ctrl != null && ctrl.Paused;
            PathChaserAgent agent0 = ctrl != null && ctrl.managerAgent != null ? ctrl.managerAgent.agent : null;
            float agentSpeed0 = agent0 != null ? agent0.speed : 0f;
            Vector3 agentPos0 = agent0 != null ? agent0.transform.position : Vector3.zero;
            bool revealed0 = kp.Revealed, hasKey0 = kp.HasKey, open0 = kd.IsOpen;
            Vector3 doorPos0 = kd.door.transform.position;
            Detail("S8-5", $"③ 시작 상태: 열쇠 Revealed {revealed0}·HasKey {hasKey0} · 문 IsOpen {open0} · 출구 문 local{V(Lc(gen, doorPos0))} · 컨트롤러 Paused {pausedWas}" + (ctrl != null ? $" 상태 {ctrl.Current}" : "") + (agent0 != null ? $" · 관리자 에이전트 speed {agentSpeed0} 위치 local{V(Lc(gen, agentPos0))}" : ""));
            if (revealed0 || hasKey0 || open0) Detail("S8-5", "주의: 시작 상태가 이미 '열쇠 공개/획득/문 열림' — 원복(ResetKey·Close)은 그 값이 아니라 초기값(미공개·미획득·닫힘)으로 돌린다");
            // 원복은 멱등 클로저 하나 — 아래 finally와 러너 정리 목록(cleanups) 양쪽이 부른다. 메인 RunSubStep은 중첩 도우미 예외 때 바깥 반복자를 Dispose하지 않아 finally가 안 돌 수 있다(검토 L8)
            // — 그 경로에서는 cleanups(러너 Finish)가 복원한다.
            bool restored = false;
            Action restore = () =>
            {
                if (restored) return;
                restored = true;
                foreach (KeyValuePair<PlayerMover, Vector3> kv in origPos) if (kv.Key != null) Teleport(kv.Key, kv.Value);
                if (kd != null) kd.Close();               // ManagerKeyDoor.cs:94-100
                if (kp != null) kp.ResetKey();            // ManagerKeyPoint.cs:60-66
                if (ctrl != null)
                {
                    ctrl.SetParticipantPaused(pausedWas);   // ManagerChapterController.cs:342
                    ctrl.ResetAll();                        // :330 — Ready + 관리자 경로 처음·준비 상태
                    if (agent0 != null) agent0.speed = agentSpeed0;   // ResetToStart가 patrolSpeed로 덮은 PathChaserAgent.speed 원래 값
                }
                KB_RestoreControl();
            };
            cleanups.Add(restore);
            bool keyTaken = false, doorOpened = false;
            float doorRise = 0f;
            try
            {
                if (ctrl != null) ctrl.SetParticipantPaused(true);   // 자동 시작 차단(Update가 Paused에서 멈춤, :156)
                yield return WaitIdle(v, 5f);
                yield return KB_GiveControl(v);
                kp.Reveal();                                                                       // ManagerKeyPoint.cs:52 — 팀이 수면 확정 때 부르는 같은 공개 메서드
                Detail("S8-5", "열쇠 노출: ManagerKeyPoint.Reveal() 직접 호출(팀 컨트롤러는 완성물 사용으로 수면이 확정될 때 부른다 — 완성물 흐름은 팀 콘텐츠 없어 검사불가라 이 호출로 대신)");
                tail.Add("완성물 흐름 대신 ManagerKeyPoint.Reveal() 공개 호출(목적은 E 줍기 확인)");
                if (ctrl != null) tail.Add("자동 시작 차단: SetParticipantPaused(true) → 끝에 SetParticipantPaused(원래 값) + ResetAll()");

                // (1) 열쇠 자리 앞 — E 탭으로 열쇠 줍기
                if (!KB_StandNear(gen, kp.transform.position, 0.4f, Mathf.Max(0.6f, kp.pickupRadius - 0.3f), 0.5f, null, out Vector3 gKey, out string howKey))
                    na.Add("③ 열쇠 자리 근처 서 있을 바닥 없음: " + howKey);
                else
                {
                    Teleport(v, gKey + gen.up * SpawnLift);
                    yield return new WaitForSeconds(KB_StartCaptureWait);
                    List<KB_Cand> list = KB_Collect(v, out bool gripped);
                    KB_Pick(list, InteractionChannel.Hand, out KB_Cand tap, out KB_Cand blocked, out KB_Cand hold, out bool tie);
                    bool controlled = InteractionController.Controlled == v;
                    Detail("S8-5", $"(1) 열쇠 자리 앞 '{v.name}' local{V(Lc(gen, v.transform.position))}(열쇠까지 3D {Vector3.Distance(v.transform.position, kp.transform.position):F2}U, 줍기 반경 {kp.pickupRadius}; {howKey}) · 조작 확인 {(controlled ? "예" : "아니오")}(manager.ControlledPlayer()='{KB_ShapeName(kp.manager.ControlledPlayer())}') · 붙잡힘 {gripped} · 손(E) 후보: {KB_DescAll(list, InteractionChannel.Hand)} => 탭 승자 {KB_Desc(tap)}{(tie ? " (동률)" : "")}");
                    if (!controlled) na.Add($"③ 열쇠: 조작권 확인 실패(Controlled='{KB_ShapeName(InteractionController.Controlled)}')");
                    else if (tap == null || tap.Provider != kp) bad.Add($"③ 열쇠 자리 앞 E 탭 승자가 열쇠 줍기가 아님: {KB_Desc(tap)} / 회색 {KB_Desc(blocked)}");
                    else
                    {
                        tap.A.execute?.Invoke();                                                       // 중앙 입력이 탭을 실행하는 것과 같은 호출(InteractionController.cs:185)
                        yield return null;
                        keyTaken = kp.HasKey;
                        Detail("S8-5", $"(1) 열쇠 줍기 execute → ManagerKeyPoint.HasKey {kp.HasKey}");
                        if (!keyTaken) bad.Add("③ 열쇠 줍기 execute 뒤에도 HasKey = false");
                    }
                }

                // (2) 열쇠 문 앞 — E 탭으로 문 열기
                if (keyTaken)
                {
                    if (!KB_StandNear(gen, kd.transform.position, 0.6f, Mathf.Max(0.8f, kd.useRadius - 0.6f), 0.5f, null, out Vector3 gDoor, out string howDoor))
                        na.Add("③ 열쇠 문 근처 서 있을 바닥 없음: " + howDoor);
                    else
                    {
                        Teleport(v, gDoor + gen.up * SpawnLift);
                        yield return new WaitForSeconds(KB_StartCaptureWait);
                        List<KB_Cand> list = KB_Collect(v, out bool gripped);
                        KB_Pick(list, InteractionChannel.Hand, out KB_Cand tap, out KB_Cand blocked, out KB_Cand hold, out bool tie);
                        bool controlled = InteractionController.Controlled == v;
                        Detail("S8-5", $"(2) 열쇠 문 앞 '{v.name}' local{V(Lc(gen, v.transform.position))}(문 사용 지점까지 3D {Vector3.Distance(v.transform.position, kd.transform.position):F2}U, 반경 {kd.useRadius}; {howDoor}) · 조작 확인 {(controlled ? "예" : "아니오")}(manager.ControlledPlayer()='{KB_ShapeName(kp.manager.ControlledPlayer())}') · 붙잡힘 {gripped} · 손(E) 후보: {KB_DescAll(list, InteractionChannel.Hand)} => 탭 승자 {KB_Desc(tap)}{(tie ? " (동률)" : "")}");
                        if (!controlled) na.Add($"③ 문: 조작권 확인 실패(Controlled='{KB_ShapeName(InteractionController.Controlled)}')");
                        else if (tap == null || tap.Provider != kd) bad.Add($"③ 열쇠 문 앞 E 탭 승자가 문 열기가 아님: {KB_Desc(tap)} / 회색 {KB_Desc(blocked)}");
                        else
                        {
                            tap.A.execute?.Invoke();
                            float t0 = Time.time;
                            while (Time.time - t0 < 3f && kd.door.transform.position.y - doorPos0.y < 1.0f) yield return new WaitForFixedUpdate();
                            doorRise = kd.door.transform.position.y - doorPos0.y;
                            doorOpened = kd.IsOpen;
                            Detail("S8-5", $"(2) 문 열기 execute → ManagerKeyDoor.IsOpen {kd.IsOpen} · 출구 문 {Time.time - t0:F1}s 동안 위로 {doorRise:F2}U 이동(doorSpeed {kd.door.doorSpeed}, 목표 +{kd.door.doorTargetYOffset})");
                            if (!doorOpened) bad.Add("③ 문 열기 execute 뒤에도 IsOpen = false");
                            else if (doorRise < 0.5f) bad.Add($"③ IsOpen = true이나 출구 문이 {doorRise:F2}U만 움직임(열림 아님)");
                        }
                    }
                }
                else Detail("S8-5", "(2) 열쇠를 못 얻어 문 열기 단계는 건너뜀");

                // (3) 관리자 사용 지점 — 완성물은 팀 콘텐츠 없이 못 만든다(MixingStation 공개 API에 완성물을 넣는 창구 없음): 실행 대신 E 탭 액션이 올라오는지만 본다.
                if (up == null) Detail("S8-5", "(3) ManagerUsePoint 없음 — 완성물 사용 지점 생략");
                else if (!up.isActiveAndEnabled) na.Add("③ ManagerUsePoint 비활성");
                else if (!KB_StandNear(gen, up.transform.position, 0.8f, Mathf.Max(1.2f, up.promptRadius - 0.8f), 0.5f, null, out Vector3 gUse, out string howUse))
                    na.Add("③ 관리자 사용 지점 근처 서 있을 바닥 없음: " + howUse);
                else
                {
                    Teleport(v, gUse + gen.up * SpawnLift);
                    yield return new WaitForSeconds(KB_StartCaptureWait);
                    List<KB_Cand> list = KB_Collect(v, out bool gripped);
                    KB_Pick(list, InteractionChannel.Hand, out KB_Cand tap, out KB_Cand blocked, out KB_Cand hold, out bool tie);
                    KB_Cand mine = list.FirstOrDefault(c => c.Provider == up);
                    Detail("S8-5", $"(3) 관리자 사용 지점 근처 '{v.name}' local{V(Lc(gen, v.transform.position))}(사용 지점까지 3D {Vector3.Distance(v.transform.position, up.transform.position):F2}U, 안내 반경 {up.promptRadius}; {howUse}) · 손(E) 후보: {KB_DescAll(list, InteractionChannel.Hand)} => 탭 승자 {KB_Desc(tap)} · 회색 승자 {KB_Desc(blocked)} · 완성물 보유 {(up.station != null && up.station.HasToken)}");
                    if (mine == null) bad.Add("③ 관리자 사용 지점이 E 중앙 입력에 액션을 올리지 않음(범위 안인데 후보 없음)");
                    else if (mine.A.channel != InteractionChannel.Hand || mine.A.trigger != InteractionTrigger.Tap) bad.Add($"③ 관리자 사용 지점 액션이 손(E) 탭이 아님: {KB_Desc(mine)}");
                    else Detail("S8-5", "(3) 완성물 사용 지점: 손(E) 탭 액션을 올림(완성물이 없어 회색 — 실행은 하지 않음. 완성물 흐름은 팀 콘텐츠 없음으로 검사불가)");
                }
            }
            finally
            {
                restore();
            }

            // 원복 확인 — 문이 닫힘 위치로 돌아올 때까지(최대 6초)
            float tw = Time.time;
            while (kd != null && kd.door != null && !kd.door.IsAtClosedPosition && Time.time - tw < 6f) yield return new WaitForFixedUpdate();
            Detail("S8-5", $"원복: 열쇠 Revealed {kp.Revealed}·HasKey {kp.HasKey} · 문 IsOpen {kd.IsOpen}·닫힘 위치 {kd.door.IsAtClosedPosition}({Time.time - tw:F1}s, 출구 문 시작 위치와 {Vector3.Distance(kd.door.transform.position, doorPos0):F3}U) · 컨트롤러 Paused {(ctrl != null ? ctrl.Paused.ToString() : "-")}" +
                                (ctrl != null ? $" 상태 {ctrl.Current}" : "") + (agent0 != null ? $" · 관리자 에이전트 speed {agent0.speed}(시작 {agentSpeed0}) 시작점과 {Vector3.Distance(agent0.transform.position, agentPos0):F3}U" : ""));
            yield return null;
            yield return null;   // InteractionController.Update가 복원된 Controlled를 갱신할 시간
            string ctlNote = KB_ControlCheck(kp.manager, out bool ctlOk);
            Detail("S8-5", ctlNote);
            tail.Add(ctlNote);
            if (!ctlOk) bad.Add("③ 조작권 원복 실패 — " + ctlNote);
            if (kp.Revealed || kp.HasKey || kd.IsOpen || !kd.door.IsAtClosedPosition) bad.Add("③ 원복 실패: 열쇠/문 상태가 초기값으로 안 돌아옴");
            if (ctrl != null && ctrl.Current != ManagerChapterController.State.Ready) bad.Add($"③ 컨트롤러 상태가 {ctrl.Current}(Ready여야 함) — 검사 중 자동 시작이 걸렸을 수 있음");
            if (ctrl != null && ctrl.Paused != pausedWas) bad.Add($"③ 원복 실패: 컨트롤러 Paused {ctrl.Paused}(시작 {pausedWas})");
            if (agent0 != null && (Mathf.Abs(agent0.speed - agentSpeed0) > 1e-4f || Vector3.Distance(agent0.transform.position, agentPos0) > 0.05f)) bad.Add($"③ 원복 실패: 관리자 에이전트 speed {agent0.speed}(시작 {agentSpeed0}) / 위치 차 {Vector3.Distance(agent0.transform.position, agentPos0):F3}U");
        }
    }
}
#endif
