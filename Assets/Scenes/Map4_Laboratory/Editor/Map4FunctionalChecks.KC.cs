#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 재검증 T1 "추가 기능 검사 C" — S6-4 [팀 보고 실측: 움직이는 패널 라이더]. 기존 러너(Map4FunctionalChecks.cs)의 partial 조각이다.
/// 기존 도우미(Report·CheckItem·Detail/Pass/Fail/NA·Teleport·WaitIdle·ParkPlayers·SolidBounds·SolidOverlap·TryGround2·RunSubStep·RespawnsSince 등)를
/// 그대로 쓰고, 새 멤버는 전부 접두어 KC_ 를 단다. 메인 파일은 건드리지 않는다(등록 조각은 KC_등록.md).
///
/// [무엇을 재는가] 팀 MovablePortalPanel.ApplyTransform(MovablePortalPanel.cs:247-254)은 킨네마틱 몸에 rb.velocity·rb.angularVelocity를 대입한다.
/// 팀 주석(:242-246)의 의도는 PlayerGroundContact.GroundVelocity(= 접촉한 바디의 GetPointVelocity, PlayerGroundContact.cs:100·:122)가 이 속도를 읽어
/// PlayerMover(PlayerMover.cs:388-389·:451-453·:476)가 패널 위 라이더를 실어 나르게 하는 것이다. Unity 2022.3은 킨네마틱 몸의 속도 대입을 지원하지 않고 경고만 찍는다.
/// 그래서 ① (가) 속도 읽기 — 패널을 매 FixedUpdate ApplyDrive(+1)로 1.5초 몰며 rb.velocity·rb.angularVelocity·GetPointVelocity 읽은 값과
/// 실제 위치 변화/dt를 같이 잰다 ② (나) 라이더 실어 나르기 — 윗면에 설 수 있는 자세(윗면 법선이 위와 30° 안)가 있으면 네모를 ExternallyDriven 없이
/// 올려 정착시킨 뒤 몰고, 패널 변위 대비 도형 변위 비율과 PlayerGroundContact 공개 속도를 기록한다. 세워진 벽 패널이면 "해당 없음"이다.
///
/// [판정] 실려 감(비율 ≥ 0.8) = 통과. 실려 가지 않음 = 검사불가 + 사유 맨 앞 "[팀 보고] "(맵 결함이 아니라 팀 기믹 동작이라 실패로 세지 않는다).
/// (나)가 해당 없음이고 (가)만 있으면 검사불가 "[팀 보고] 해당 없음 …" + 속도 읽기 수치. 패널을 못 찾으면 검사불가(이름 불일치). 실패(Fail)는 쓰지 않는다.
///
/// [대조군 — 하위 결과, 판정 아님] S6의 두 패널은 둘 다 Pivot이라 수평으로 실어 나르는 팀 의도(GroundVelocity XZ)를 직접 시험할 자세가 없다.
/// 그래서 러너가 실행 중에만 팀 컴포넌트(MovablePortalPanel·PortalSurface)로 윗면이 수평인 Rail 평판 하나를 S6 방 빈 바닥 위에 만들어 같은 시험을 하고 곧 없앤다
/// (S6-3이 포탈을 실행 중에만 놓는 것과 같은 방식). 판정 줄에는 영향이 없고 하위 결과 "대조군"과 Detail에만 남는다.
///
/// [금지 준수 — 클래스 머리 주석(Map4FunctionalChecks.cs :55-59)과 같다] 팀 코드·맵 코드·씬 수정 0 · 팀 private 리플렉션 0
/// (MovablePortalPanel.ApplyDrive·Progress·IsJammed·mode·startPose·endPose·travelSpeed·pivot*·minAngle·maxAngle·rotationSpeed·stuckRecoveryDelay·riderSensor,
/// PortalSurface.Box, PlayerGroundContact.GroundVelocity·GroundNormal·IsGrounded, PlayerMover.SetControlled·IsControlled·InputLocked·useTorqueRolling,
/// Unity Rigidbody 공개 API만) · 씬 저장 0 · Time.timeScale/fixedDeltaTime/레이어 행렬 변경 0.
/// 도형의 조작권(IsControlled)을 시험 동안만 바꾸고(원래 값 기록·복원) 끝나면 되돌린다. ExternallyDriven은 건드리지 않는다.
/// </summary>
public static partial class Map4FunctionalChecks
{
    private partial class Driver : MonoBehaviour
    {
        // ═════════════════════════ KC 상수 ═════════════════════════

        private const float KC_DriveSeconds = 1.5f;        // [지시] (가) 몰기 시간
        private const float KC_CarryRatioMin = 0.8f;       // [지시] 통과 기준 = 패널 변위 대비 도형 변위 비율
        private const float KC_StandAngleMax = 30f;        // [지시] 설 수 있는 자세 = 윗면 법선이 위(+Y)와 이루는 각 ≤ 30°
        private const float KC_RideAngleMax = 25f;         // [구현 결정] 몰고 있는 동안 윗면이 이 각을 넘으면 창을 닫는다(30°는 마찰 0.6의 미끄러짐 한계 tan30°=0.58 근처라 5° 여유)
        private const float KC_RideMaxSeconds = 1.5f;      // [지시] "같은 방식" = (가)와 같은 1.5초
        private const float KC_RideSettleSeconds = 1.0f;   // [구현 결정] 올린 뒤 정착 대기
        private const float KC_RideMinExpected = 0.2f;     // [구현 결정] 패널 변위가 이보다 작으면 비율을 믿을 수 없다
        private const float KC_RiderLift = 0.12f;          // [구현 결정] 올릴 때 윗면 위 간격(내려앉는 거리)
        private const float KC_ReturnSlack = 1.5f;         // [구현 결정] 되돌리기·스윕 시간 한도에 더하는 여유
        private const float KC_HonoredRatio = 0.8f;        // [구현 결정] 속도 읽기 "반영됨" = 접촉점 속도 읽은 값/실제 ≥ 0.8
        // [S6_Wiring.cs:49·:252] 움직이는 패널 이름 = MovPanelNamePrefix "S6_MovPanel_" + 1..2
        private static readonly string[] KC_PanelNames = { "S6_MovPanel_1", "S6_MovPanel_2" };
        // 대조군 합성 Rail 평판 — 크기는 S6 패널과 같은 3.0×3.6×0.2 [S6_Wiring.cs:77], 속도는 팀 기본 travelSpeed 1 [MovablePortalPanel.cs:32], 이동 3U(진행도 1 = 3초)
        private static readonly Vector3 KC_CtrlSize = new Vector3(3.0f, 3.6f, 0.2f);
        private const float KC_CtrlTravel = 3f;
        private const float KC_CtrlLift = 1.0f;            // [구현 결정] 평판 밑면 ↔ 바닥 간격(S6 구덩이의 PitClear 1.0과 같다)
        // [구현 결정] 대조군 후보 자리(섹터 로컬 x,y=바닥,z) — S6 방 동쪽 열린 바닥(x −8.5~32, z 2~96, 천장 24). 실행 중에 바닥·빈 공간·도형 거리를 확인해 고른다.
        private static readonly Vector3[] KC_CtrlSpotsLocal =
        {
            new Vector3(18f, 0f, 50f), new Vector3(24f, 0f, 50f), new Vector3(18f, 0f, 60f),
            new Vector3(24f, 0f, 60f), new Vector3(14f, 0f, 70f), new Vector3(24f, 0f, 70f)
        };

        // ═════════════════════════ KC 자료 ═════════════════════════

        private enum KC_RideStatus { NotRun, Carried, NotCarried, NotApplicable, Inconclusive }

        /// <summary>한 번 몰기의 속도 읽기 통계(움직인 틱만 합산). "읽은" = 팀 코드 대입 뒤 rb에서 읽은 값, "실제" = 위치·회전 변화/dt.</summary>
        private sealed class KC_Vel
        {
            public int Ticks, Moving;
            public float SumRead, SumAct, SumAngRead, SumAngAct, SumPtRead, SumPtAct;
            public float MaxRead, MaxAct, MaxAngRead, MaxAngAct, MaxPtRead, MaxPtAct;
            public float Dist, Deg;
            public float Progress0 = -1f, Progress1 = -1f;
            public bool Jammed;
            public int WarnLin, WarnAng;
            public readonly List<string> Samples = new List<string>();
            public float MeanRead => Moving > 0 ? SumRead / Moving : 0f;
            public float MeanAct => Moving > 0 ? SumAct / Moving : 0f;
            public float MeanAngRead => Moving > 0 ? SumAngRead / Moving : 0f;
            public float MeanAngAct => Moving > 0 ? SumAngAct / Moving : 0f;
            public float MeanPtRead => Moving > 0 ? SumPtRead / Moving : 0f;
            public float MeanPtAct => Moving > 0 ? SumPtAct / Moving : 0f;
            public float LinRatio => SumAct > 1e-4f ? SumRead / SumAct : 0f;
            public float AngRatio => SumAngAct > 1e-4f ? SumAngRead / SumAngAct : 0f;
            public float PtRatio => SumPtAct > 1e-4f ? SumPtRead / SumPtAct : 0f;
        }

        /// <summary>윗면 법선과 위(+Y)가 이루는 각의 추적 — 서는 자세를 찾는다. 법선이 아래를 향하면 뒷면이 위라서 min(n, −n)으로 잰다.</summary>
        private sealed class KC_Pose
        {
            public float StartUp = -1f, MinUp = 180f, MinUpProgress;
            public Vector3 StartNormal, LastNormal, MinNormal;
        }

        private sealed class KC_Move
        {
            public bool Reached, Jammed;
            public float Seconds, Final;
        }

        /// <summary>라이더 시험 1회(도형 하나를 올려 몰기) 결과.</summary>
        private sealed class KC_Ride
        {
            public string Label = "";
            public bool Ran;                   // 올려서 정착시키고 창을 끝까지 쟀는가
            public string Why = "";            // Ran = false 사유
            public bool GvUsed, UseTorque, Jammed;
            public float Support, SettleHeight, StartUp, Seconds;
            public int Ticks, Grounded;
            public Vector3 Exp, Act;           // 월드 변위(패널에 붙은 점 / 도형)
            public float ExpLen, ActLen, Ratio, RatioH = -1f, MaxDev;
            public float GvMean, GvMax, GvXzMean, ExpSpeedMean, ExpXzMean;
            public int Interventions, Respawns;
        }

        private sealed class KC_PanelRep
        {
            public MovablePortalPanel Panel;
            public PortalSurface Surf;
            public int Index;
            public string Label = "";
            public bool IsControl;
            public readonly KC_Vel Vel = new KC_Vel();
            public readonly KC_Pose Pose = new KC_Pose();
            public bool StandOk;
            public float PStar, Dir = 1f;
            public KC_Ride Judged, Info;
            public KC_RideStatus Status = KC_RideStatus.NotRun;
            public string StatusWhy = "";
            public KC_Move Back;
        }

        private sealed class KC_Control
        {
            public GameObject Root, StartPose, EndPose;
            public MovablePortalPanel Panel;
            public PortalSurface Surface;
            public string How = "";
        }

        private KC_Control kcControl;
        private bool kcCtrlSaved, kcCtrl0;
        private int kcWarnLin, kcWarnAng, kcWarnOther;

        // ═════════════════════════ 진입점 ═════════════════════════

        /// <summary>S6-4. MainSequence의 S6 블록에서 S6-3 뒤·S6-정리 앞에 둔다(KC_등록.md (c)).</summary>
        private IEnumerator KC_S6_4_PanelRiders()
        {
            const string id = "S6-4";
            if (!Ready6(id)) yield break;
            Transform gen = s6.Gen;
            CheckItem item = report.Get(id);

            List<MovablePortalPanel> panels = s6.Movables.Where(x => x != null).ToList();
            if (panels.Count == 0)
            {
                NA(id, "검사불가(패널 못 찾음 — 이름 불일치 가능): MovablePortalPanel 0개(기대 S6_MovPanel_1·2 [S6_Wiring.cs:49·:252])" + NearNames(gen, "MovPanel"));
                yield break;
            }
            SpacePortal[] existing = Object.FindObjectsOfType<SpacePortal>();
            if (SpacePortal.Orange != null || SpacePortal.Blue != null || existing.Length > 0)
            {
                NA(id, $"검사불가(기존 포탈 있음): SpacePortal {existing.Length}개(Orange={Nm(SpacePortal.Orange)}, Blue={Nm(SpacePortal.Blue)}) — 포탈이 놓인 패널은 도형 충돌이 꺼져 라이더를 올릴 수 없다");
                yield break;
            }

            bool canRide = s6.Park != null && s6.Park.Length > 0;
            Detail(id, "방법: ① 패널을 시작 진행도 0에서 매 FixedUpdate ApplyDrive(+1)로 1.5초 몰며 WaitForFixedUpdate 뒤 rb.velocity·rb.angularVelocity·GetPointVelocity(면 중심)를 읽고 " +
                       "실제 위치·회전 변화/dt와 같이 기록(움직인 틱만 평균·최대) ② 윗면 법선이 위와 30° 안인 자세가 있으면 도형(네모)을 ExternallyDriven 없이 그 자세의 패널 위에 올려 1초 정착시킨 뒤 " +
                       "같은 방식으로 몰아 패널에 붙은 점의 변위 대비 도형 변위 비율 + PlayerGroundContact.GroundVelocity를 기록 ③ 끝나면 도형을 파킹점으로 보내고 ApplyDrive(−1)로 진행도 0까지 되돌림");
            if (!canRide) Detail(id, "S6 파킹점 0 — 도형을 치울 자리가 없어 라이더 시험(나)은 하지 않는다(속도 읽기 (가)만)");

            kcCtrl0 = cube.IsControlled;
            kcCtrlSaved = true;
            cleanups.Add(KC_Restore);   // 예외·워치독으로 끊겨도 Finish가 조작권·합성 패널·로그 구독을 원복한다(멱등)
            Application.logMessageReceived -= KC_OnLog;
            Application.logMessageReceived += KC_OnLog;

            yield return ParkPlayers(s6.Park, sphere, cube, tetra);
            Detail(id, $"네모 '{Nm(cube)}': IsControlled {kcCtrl0} · useTorqueRolling {cube.useTorqueRolling}(false면 PlayerMover 레거시 경로 — GroundVelocity를 쓰는 곳은 PlayerMover.cs:388뿐이고 토크 경로(:537~)는 안 쓴다) · InputLocked {cube.InputLocked}");

            // ── 실제 S6 패널 ──
            List<KC_PanelRep> reps = new List<KC_PanelRep>();
            for (int i = 0; i < panels.Count; i++)
            {
                MovablePortalPanel mp = panels[i];
                string expect = i < KC_PanelNames.Length ? KC_PanelNames[i] : "(기대 이름 없음)";
                Detail(id, $"이름 대조: 패널 {i + 1} 기대 '{expect}' / 실제 '{mp.name}' → {(mp.name == expect ? "일치" : "불일치(타입으로 찾았으므로 시험은 계속)")}");
                KC_PanelRep rep = new KC_PanelRep { Panel = mp, Surf = mp.GetComponent<PortalSurface>(), Index = i + 1, Label = mp.name };
                reps.Add(rep);
                yield return KC_RunPanel(rep, canRide);
            }

            // ── 대조군(하위 결과) ──
            KC_PanelRep ctrl = null;
            string ctrlProblem = null;
            if (!canRide) ctrlProblem = "S6 파킹점 0";
            else if (KC_BuildControl(out string why))
            {
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                Detail(id, $"대조군: 합성 Rail 평판 생성 — {kcControl.How}");
                ctrl = new KC_PanelRep { Panel = kcControl.Panel, Surf = kcControl.Surface, Index = 0, Label = "대조군(합성 Rail 평판)", IsControl = true };
                yield return KC_RunPanel(ctrl, true);
                KC_DestroyControl();
                yield return null;
            }
            else ctrlProblem = why;
            if (ctrlProblem != null) Detail(id, "대조군: 만들지 않음 — " + ctrlProblem);

            // ── 원복 ──
            KC_Restore();
            if (canRide) yield return ParkPlayers(s6.Park, sphere, cube, tetra);

            // ── 하위 결과 ──
            foreach (KC_PanelRep rep in reps)
            {
                bool honored = rep.Vel.Moving > 0 && rep.Vel.PtRatio >= KC_HonoredRatio;
                item.SetSub("vel" + rep.Index, rep.Label + " 속도 읽기", honored ? SubStatus.Pass : SubStatus.NA,
                            rep.Vel.Moving == 0 ? $"패널이 움직이지 않음(IsJammed {rep.Vel.Jammed})" : (honored ? "" : "[팀 보고] ") + KC_VelBrief(rep.Vel));
                switch (rep.Status)
                {
                    case KC_RideStatus.Carried:
                        item.SetSub("rider" + rep.Index, rep.Label + " 라이더", SubStatus.Pass, rep.StatusWhy); break;
                    case KC_RideStatus.NotCarried:
                        item.SetSub("rider" + rep.Index, rep.Label + " 라이더", SubStatus.NA, "[팀 보고] " + rep.StatusWhy); break;
                    case KC_RideStatus.NotApplicable:
                    case KC_RideStatus.Inconclusive:
                        item.SetSub("rider" + rep.Index, rep.Label + " 라이더", SubStatus.NA, rep.StatusWhy); break;
                    default:
                        item.SetSub("rider" + rep.Index, rep.Label + " 라이더", SubStatus.Skipped, "미실행"); break;
                }
            }
            if (ctrl != null)
            {
                switch (ctrl.Status)
                {
                    case KC_RideStatus.Carried:
                        item.SetSub("ctrl", "대조군(합성 Rail 평판)", SubStatus.Pass, ctrl.StatusWhy); break;
                    case KC_RideStatus.NotCarried:
                        item.SetSub("ctrl", "대조군(합성 Rail 평판)", SubStatus.NA, "[팀 보고] " + ctrl.StatusWhy); break;
                    default:
                        item.SetSub("ctrl", "대조군(합성 Rail 평판)", SubStatus.NA, ctrl.StatusWhy); break;
                }
            }
            else item.SetSub("ctrl", "대조군(합성 Rail 평판)", SubStatus.NA, "검사불가: " + ctrlProblem);

            // ── 판정 ──
            List<string> carried = new List<string>(), notCarried = new List<string>(), notApplicable = new List<string>(), inconclusive = new List<string>();
            foreach (KC_PanelRep rep in reps)
            {
                string line = $"{rep.Label}({rep.Panel.mode}) {rep.StatusWhy}";
                switch (rep.Status)
                {
                    case KC_RideStatus.Carried: carried.Add(line); break;
                    case KC_RideStatus.NotCarried: notCarried.Add(line); break;
                    case KC_RideStatus.NotApplicable: notApplicable.Add(line); break;
                    default: inconclusive.Add(line); break;
                }
            }
            string vel = string.Join(" / ", reps.Select(r => $"{r.Label}: {KC_VelBrief(r.Vel)}"));
            string ctrlTxt = ctrl == null ? $" · 대조군 없음({ctrlProblem})"
                : $" · 대조군(합성 Rail 평판, 판정 아님): {(ctrl.Status == KC_RideStatus.Carried ? "실림" : ctrl.Status == KC_RideStatus.NotCarried ? "안 실림" : "검사불가")} — {ctrl.StatusWhy}";
            string warn = $"경고 관찰('kinematic' 포함) 선속도 {reps.Sum(r => r.Vel.WarnLin)}회·각속도 {reps.Sum(r => r.Vel.WarnAng)}회";

            if (notCarried.Count > 0)
                NA(id, "[팀 보고] 라이더가 실려 가지 않음 — " + string.Join("; ", notCarried) +
                       (carried.Count > 0 ? " · 실린 패널: " + string.Join("; ", carried) : "") +
                       (notApplicable.Count > 0 ? " · 해당 없음: " + string.Join("; ", notApplicable) : "") + " · 속도 읽기: " + vel + " · " + warn + ctrlTxt);
            else if (inconclusive.Count > 0)
                NA(id, "검사불가(라이더 시험이 성립하지 않음): " + string.Join("; ", inconclusive) +
                       (carried.Count > 0 ? " · 실린 패널: " + string.Join("; ", carried) : "") + " · 속도 읽기: " + vel + " · " + warn + ctrlTxt);
            else if (carried.Count > 0)
                Pass(id, $"라이더 실림(패널 변위 대비 도형 변위 ≥ {KC_CarryRatioMin * 100f:F0}%): " + string.Join("; ", carried) +
                         (notApplicable.Count > 0 ? " · 해당 없음: " + string.Join("; ", notApplicable) : "") + " · 속도 읽기: " + vel + " · " + warn + ctrlTxt);
            else
                NA(id, "[팀 보고] 해당 없음 — 라이더를 올릴 수 있는 자세의 패널이 없다: " + string.Join("; ", notApplicable) + " · 속도 읽기: " + vel + " · " + warn + ctrlTxt);
        }

        // ═════════════════════════ 패널 하나 ═════════════════════════

        /// <summary>패널 하나의 전체 흐름. 위험한 부분은 RunSubStep으로 감싸 예외가 항목 전체를 실패시키지 않게 하고, 끝나면 반드시 조작권 복원·도형 파킹·진행도 0 되돌리기를 한다.</summary>
        private IEnumerator KC_RunPanel(KC_PanelRep rep, bool canRide)
        {
            const string id = "S6-4";
            string tag = "S6-4." + rep.Label;
            bool done = false;
            yield return RunSubStep(KC_MeasureAndRide(rep, canRide, () => done = true), tag);
            if (!done)
            {
                if (rep.Status == KC_RideStatus.NotRun) { rep.Status = KC_RideStatus.Inconclusive; rep.StatusWhy = "검사 중 예외로 끊김(메모·콘솔 참고)"; }
                Detail(id, $"{rep.Label}: 측정이 예외로 끊겼다 — 메모·콘솔의 '{tag}' 줄 참고");
            }

            // 원복(예외 여부와 무관): 도형 조작권, 도형 파킹, 패널 진행도 0
            if (cube != null) cube.SetControlled(kcCtrl0);
            if (canRide) yield return ParkPlayers(s6.Park, cube);
            rep.Back = new KC_Move();
            yield return RunSubStep(KC_DriveToProgress(rep.Panel, 0f, rep.Back), tag + ".복귀");
            Detail(id, $"{rep.Label} 되돌리기: ApplyDrive(−1)로 진행도 {rep.Back.Final:F3}까지 {rep.Back.Seconds:F2}s — {(rep.Back.Reached ? "0 도달" : "0 미도달")} · IsJammed {rep.Back.Jammed}");
        }

        private IEnumerator KC_MeasureAndRide(KC_PanelRep rep, bool canRide, Action completed)
        {
            const string id = "S6-4";
            MovablePortalPanel mp = rep.Panel;
            PortalSurface ps = rep.Surf;
            Transform gen = s6.Gen;
            float dt = Time.fixedDeltaTime;
            if (mp == null || ps == null || ps.Box == null)
            {
                rep.Status = KC_RideStatus.Inconclusive;
                rep.StatusWhy = "PortalSurface·BoxCollider 없음";
                completed();
                yield break;
            }

            // 0. 시작 진행도 0
            if (mp.Progress > 0.01f)
            {
                KC_Move pre = new KC_Move();
                yield return KC_DriveToProgress(mp, 0f, pre);
                Detail(id, $"{rep.Label}: 시작 진행도가 0이 아니어서 먼저 되돌림 → {pre.Final:F3}({pre.Seconds:F2}s)");
            }
            Detail(id, KC_DescribePanel(rep));

            // 1. (가) 속도 읽기 — 1.5초
            int w0 = kcWarnLin, a0 = kcWarnAng;
            int ticks = Mathf.CeilToInt(KC_DriveSeconds / dt - 0.001f);
            yield return KC_Drive(mp, ps, 1f, ticks, null, rep.Vel, rep.Pose);
            rep.Vel.WarnLin = kcWarnLin - w0;
            rep.Vel.WarnAng = kcWarnAng - a0;
            KC_Vel v = rep.Vel;
            Detail(id, $"{rep.Label} (가) 속도 읽기: ApplyDrive(+1) {ticks}틱({KC_DriveSeconds}s) · 진행도 {v.Progress0:F2}→{v.Progress1:F2} · 움직인 틱 {v.Moving} · 이동 {v.Dist:F2}U·회전 {v.Deg:F1}° · IsJammed {v.Jammed}");
            Detail(id, $"{rep.Label} (가) 읽은 값 ↔ 실제: " + KC_VelBrief(v) + $" · 최대(읽은/실제) 선 {v.MaxRead:F2}/{v.MaxAct:F2}U/s · 접촉점 {v.MaxPtRead:F2}/{v.MaxPtAct:F2}U/s · 각 {v.MaxAngRead:F2}/{v.MaxAngAct:F2}rad/s");
            Detail(id, $"{rep.Label} (가) 팀 경고 관찰: 몰기 {ticks}틱 동안 로그에서 'kinematic' 포함 경고 — 선속도 {v.WarnLin}회·각속도 {v.WarnAng}회(두 패널이 매 틱 대입하므로 틱당 각 2회가 예상: 팀 보고 EXE 20초 각 1,947회와 같은 종류)");
            foreach (string s in v.Samples) Detail(id, $"{rep.Label} (가) 표본 {s}");

            // 2. 서는 자세 찾기 — 시작 자세가 이미 서는 면이면 거기서, 아니면 끝까지 쓸어 윗면이 가장 평평한 진행도를 찾는다
            bool standStart = rep.Pose.StartUp <= KC_StandAngleMax;
            if (!standStart)
            {
                KC_Vel sweep = new KC_Vel();
                int maxT = Mathf.CeilToInt((KC_FullTravelSeconds(mp) + KC_ReturnSlack) / dt);
                yield return KC_Drive(mp, ps, 1f, maxT, () => mp.Progress >= 0.9995f || mp.IsJammed, sweep, rep.Pose);
                Detail(id, $"{rep.Label} 자세 훑기: 끝까지 몰아 진행도 {sweep.Progress1:F3} · 윗면과 위(+Y)의 각 시작 {rep.Pose.StartUp:F1}° · 최소 {rep.Pose.MinUp:F1}°(진행도 {rep.Pose.MinUpProgress:F3}) · " +
                           $"끝 법선 관측 local{V(gen.InverseTransformDirection(rep.Pose.LastNormal))} · IsJammed {sweep.Jammed}");
            }
            rep.StandOk = standStart || rep.Pose.MinUp <= KC_StandAngleMax;
            rep.PStar = standStart ? 0f : rep.Pose.MinUpProgress;
            rep.Dir = rep.PStar > 0.5f ? -1f : 1f;

            if (!rep.StandOk)
            {
                Bounds sb = SolidBounds(cube);
                rep.Status = KC_RideStatus.NotApplicable;
                rep.StatusWhy = $"해당 없음 — 패널이 세워져 있어 위에 설 수 없다(법선 시작 local{V(gen.InverseTransformDirection(rep.Pose.StartNormal))} → 끝 관측 local{V(gen.InverseTransformDirection(rep.Pose.LastNormal))} · " +
                                $"윗면과 위(+Y)의 각 최소 {rep.Pose.MinUp:F1}° > {KC_StandAngleMax:F0}° · 윗변은 폭 {ps.Box.size.z:F2}U 띠라 도형 폭 {Mathf.Min(sb.size.x, sb.size.z):F2}U보다 좁다)";
                Detail(id, $"{rep.Label} (나): " + rep.StatusWhy);
                completed();
                yield break;
            }
            if (!canRide)
            {
                rep.Status = KC_RideStatus.Inconclusive;
                rep.StatusWhy = "S6 파킹점 0 — 라이더를 치울 자리가 없어 올리지 않음";
                Detail(id, $"{rep.Label} (나): " + rep.StatusWhy);
                completed();
                yield break;
            }
            Detail(id, $"{rep.Label} (나): 서는 자세 있음 — {(standStart ? "시작 자세" : $"진행도 {rep.PStar:F3}")}에서 윗면과 위의 각 {(standStart ? rep.Pose.StartUp : rep.Pose.MinUp):F1}° ≤ {KC_StandAngleMax:F0}° → " +
                       $"그 자세에 도형을 올리고 ApplyDrive({(rep.Dir > 0 ? "+1" : "−1")})로 몬다(윗면 각이 {KC_RideAngleMax:F0}°를 넘거나 {KC_RideMaxSeconds}s가 지나면 창을 닫음)");

            // 3. 판정 시험(조작 중) + 참고 시험(조작권 없음)
            rep.Judged = new KC_Ride { Label = "조작 중(IsControlled=true)" };
            yield return KC_RideFull(rep, true, rep.Judged);
            rep.Info = new KC_Ride { Label = "조작권 없음(IsControlled=false)" };
            yield return KC_RideFull(rep, false, rep.Info);

            string why;
            rep.Status = KC_Classify(rep.Judged, mp, out why);
            string infoWhy;
            KC_RideStatus infoStatus = KC_Classify(rep.Info, mp, out infoWhy);
            rep.StatusWhy = rep.Status == KC_RideStatus.Carried ? KC_RideBrief(rep.Judged) : (rep.Status == KC_RideStatus.NotCarried ? KC_RideBrief(rep.Judged) + " — " + why : why);
            Detail(id, $"{rep.Label} (나) 판정 시험 [{rep.Judged.Label}] → {KC_StatusLabel(rep.Status)}: " + KC_RideText(rep.Judged));
            Detail(id, $"{rep.Label} (나) 참고 시험(판정 아님) [{rep.Info.Label}] → {KC_StatusLabel(infoStatus)}: " + KC_RideText(rep.Info) + (infoStatus == KC_RideStatus.Inconclusive ? " — " + infoWhy : ""));
            completed();
        }

        /// <summary>패널을 라이더 시험 자세(PStar)로 보내고, 도형을 올려 몬다.</summary>
        private IEnumerator KC_RideFull(KC_PanelRep rep, bool controlled, KC_Ride r)
        {
            const string id = "S6-4";
            yield return ParkPlayers(s6.Park, cube);   // 이전 시험의 도형을 치운 뒤 패널을 움직인다(위에 얹힌 채 움직이면 시험이 섞인다)
            KC_Move mv = new KC_Move();
            yield return KC_DriveToProgress(rep.Panel, rep.PStar, mv);
            if (!mv.Reached)
            {
                r.Why = $"패널을 시험 자세(진행도 {rep.PStar:F3})로 보내지 못함 — 진행도 {mv.Final:F3} · IsJammed {mv.Jammed}";
                yield break;
            }
            bool done = false;
            yield return RunSubStep(KC_RideTrial(rep.Panel, rep.Surf, cube, rep.Dir, controlled, r, () => done = true), "S6-4.라이더." + rep.Label);
            if (!done && string.IsNullOrEmpty(r.Why)) r.Why = "시험 중 예외로 끊김(메모·콘솔 참고)";
            if (cube != null) cube.SetControlled(kcCtrl0);
            Detail(id, $"{rep.Label}: 라이더 시험 [{r.Label}] 끝 — 패널 진행도 {rep.Panel.Progress:F3}");
        }

        // ═════════════════════════ 몰기·읽기 ═════════════════════════

        /// <summary>패널을 dir 방향 ApplyDrive를 매 FixedUpdate 부르며 maxTicks까지(또는 stop이 참이 될 때까지) 몬다. 매 틱 WaitForFixedUpdate 뒤 rb에서 읽은 속도와
        /// 실제 위치·회전 변화/dt를 비교해 st에 쌓고, 윗면 법선과 위의 각을 pose에 쌓는다.</summary>
        private IEnumerator KC_Drive(MovablePortalPanel mp, PortalSurface ps, float dir, int maxTicks, Func<bool> stop, KC_Vel st, KC_Pose pose)
        {
            Rigidbody rb = mp.GetComponent<Rigidbody>();
            float dt = Time.fixedDeltaTime;
            Vector3 faceLocal = ps.Box.center + new Vector3(0f, 0f, ps.Box.size.z * 0.5f);
            Vector3 prevPos = rb.position;
            Quaternion prevRot = rb.rotation;
            Vector3 prevFace = prevPos + prevRot * faceLocal;
            if (st.Progress0 < 0f) st.Progress0 = mp.Progress;
            if (pose != null && pose.StartUp < 0f)
            {
                pose.StartUp = KC_UpAngle(prevRot);
                pose.StartNormal = prevRot * Vector3.forward;
                pose.MinUp = pose.StartUp;
                pose.MinUpProgress = mp.Progress;
                pose.MinNormal = pose.StartNormal;
                pose.LastNormal = pose.StartNormal;
            }
            for (int i = 0; i < maxTicks; i++)
            {
                mp.ApplyDrive(dir);
                yield return new WaitForFixedUpdate();
                if (mp == null || rb == null) break;
                Vector3 pos = rb.position;
                Quaternion rot = rb.rotation;
                Vector3 face = pos + rot * faceLocal;
                Vector3 actV = (pos - prevPos) / dt;
                Vector3 actPt = (face - prevFace) / dt;
                Quaternion dq = rot * Quaternion.Inverse(prevRot);
                dq.ToAngleAxis(out float ang, out Vector3 _);
                if (ang > 180f) ang -= 360f;
                float actAng = Mathf.Abs(ang) * Mathf.Deg2Rad / dt;
                Vector3 readV = rb.velocity;
                Vector3 readAng = rb.angularVelocity;
                Vector3 readPt = rb.GetPointVelocity(face);

                st.Ticks++;
                st.Jammed |= mp.IsJammed;
                if (actV.magnitude > 0.02f || actAng > 0.02f)
                {
                    st.Moving++;
                    st.SumRead += readV.magnitude; st.SumAct += actV.magnitude;
                    st.SumAngRead += readAng.magnitude; st.SumAngAct += actAng;
                    st.SumPtRead += readPt.magnitude; st.SumPtAct += actPt.magnitude;
                    st.MaxRead = Mathf.Max(st.MaxRead, readV.magnitude); st.MaxAct = Mathf.Max(st.MaxAct, actV.magnitude);
                    st.MaxAngRead = Mathf.Max(st.MaxAngRead, readAng.magnitude); st.MaxAngAct = Mathf.Max(st.MaxAngAct, actAng);
                    st.MaxPtRead = Mathf.Max(st.MaxPtRead, readPt.magnitude); st.MaxPtAct = Mathf.Max(st.MaxPtAct, actPt.magnitude);
                    st.Dist += (pos - prevPos).magnitude;
                    st.Deg += Mathf.Abs(ang);
                    if (st.Samples.Count < 3)
                        st.Samples.Add($"틱{st.Ticks}: 읽은 v{V(readV)}·ω{V(readAng)}·면 중심 점속도{V(readPt)} ↔ 실제 v{V(actV)}·ω {actAng:F2}rad/s·면 중심 점속도{V(actPt)}");
                }
                st.Progress1 = mp.Progress;
                if (pose != null)
                {
                    float up = KC_UpAngle(rot);
                    pose.LastNormal = rot * Vector3.forward;
                    if (up < pose.MinUp - 1e-4f) { pose.MinUp = up; pose.MinUpProgress = mp.Progress; pose.MinNormal = pose.LastNormal; }
                }
                prevPos = pos; prevRot = rot; prevFace = face;
                if (stop != null && stop()) break;
            }
        }

        /// <summary>패널을 목표 진행도로 보낸다(ApplyDrive ±1 반복, 한도 시간 안). 0·1 목표는 팀 클램프로 정확히 닿는다.</summary>
        private IEnumerator KC_DriveToProgress(MovablePortalPanel mp, float target, KC_Move res)
        {
            float full = KC_FullTravelSeconds(mp);
            float limit = Mathf.Clamp(full * Mathf.Abs(target - mp.Progress) + mp.stuckRecoveryDelay + KC_ReturnSlack, 2f, 14f);
            float step = Time.fixedDeltaTime / Mathf.Max(0.05f, full);
            float tol = (target <= 0f || target >= 1f) ? 0.0005f : Mathf.Max(0.0005f, step * 0.75f);
            float t0 = Time.time;
            while (Time.time - t0 < limit)
            {
                float diff = target - mp.Progress;
                if (Mathf.Abs(diff) <= tol) { res.Reached = true; break; }
                mp.ApplyDrive(diff > 0f ? 1f : -1f);
                yield return new WaitForFixedUpdate();
                if (mp == null) yield break;
                res.Jammed |= mp.IsJammed;
            }
            res.Seconds = Time.time - t0;
            res.Final = mp != null ? mp.Progress : -1f;
            if (mp != null) res.Jammed |= mp.IsJammed;
        }

        /// <summary>팀 ProgressSpeedLimit(MovablePortalPanel.cs:206-215)과 같은 식을 공개 필드로만 다시 계산한 "진행도 0→1 걸리는 시간(s)". 계산 불가면 4.</summary>
        private static float KC_FullTravelSeconds(MovablePortalPanel mp)
        {
            if (mp.mode == MovablePortalPanel.PanelMode.Rail)
            {
                float d = mp.startPose != null && mp.endPose != null ? Vector3.Distance(mp.startPose.position, mp.endPose.position) : 0f;
                return d > 1e-4f && mp.travelSpeed > 1e-4f ? d / mp.travelSpeed : 4f;
            }
            float range = Mathf.Abs(mp.maxAngle - mp.minAngle);
            return range > 1e-4f && mp.rotationSpeed > 1e-4f ? range / mp.rotationSpeed : 4f;
        }

        /// <summary>윗면 법선(앞면 또는 뒷면 중 위를 보는 쪽)과 위(+Y)의 각. 앞면 법선 = 로컬 +Z(PortalSurface.cs:10-14 규약).</summary>
        private static float KC_UpAngle(Quaternion rot)
        {
            Vector3 n = rot * Vector3.forward;
            return Mathf.Min(Vector3.Angle(n, Vector3.up), Vector3.Angle(-n, Vector3.up));
        }

        /// <summary>윗면이 로컬 +Z면 +1, 뒷면(−Z)이면 −1.</summary>
        private static float KC_TopSign(Quaternion rot) => (rot * Vector3.forward).y >= 0f ? 1f : -1f;

        // ═════════════════════════ 라이더 시험 ═════════════════════════

        /// <summary>도형 m을 패널 윗면 중심에 올려(ExternallyDriven 없이) 정착시킨 뒤 dir 방향으로 몰며 패널에 붙은 점의 변위와 도형 변위를 비교한다.
        /// 위험 부분을 RunSubStep이 감싸므로 이 안의 finally는 신뢰하지 않는다 — 조작권 복원은 부르는 쪽(KC_RideFull·KC_RunPanel)과 KC_Restore가 한다.</summary>
        private IEnumerator KC_RideTrial(MovablePortalPanel mp, PortalSurface ps, PlayerMover m, float dir, bool controlled, KC_Ride r, Action completed)
        {
            Rigidbody prb = mp.GetComponent<Rigidbody>();
            Rigidbody mrb = m.GetComponent<Rigidbody>();
            PlayerGroundContact gc = m.GetComponentInChildren<PlayerGroundContact>();
            Lab_AntiStuck ast = m.GetComponent<Lab_AntiStuck>();
            BoxCollider box = ps.Box;
            float dt = Time.fixedDeltaTime;
            float since = Time.time;

            yield return WaitIdle(m, 6f);
            if (m.ExternallyDriven || mrb.isKinematic)
            {
                r.Why = "도형이 복귀 연출 중(ExternallyDriven·kinematic)이라 6초를 기다려도 풀리지 않음";
                yield break;
            }
            m.SetControlled(controlled);
            r.GvUsed = controlled && !m.InputLocked && !m.useTorqueRolling && !m.ExternallyDriven;
            r.UseTorque = m.useTorqueRolling;

            // 올릴 자리: 윗면 중심에서 법선 방향으로 (AABB 지지 거리 + 0.12)
            Quaternion rot0 = prb.rotation;
            float sign = KC_TopSign(rot0);
            Vector3 topN = rot0 * new Vector3(0f, 0f, sign);
            Vector3 topCenter = prb.position + rot0 * (box.center + new Vector3(0f, 0f, sign * box.size.z * 0.5f));
            Bounds mb = SolidBounds(m);
            Vector3 ex = mb.extents;
            float support = Mathf.Abs(topN.x) * ex.x + Mathf.Abs(topN.y) * ex.y + Mathf.Abs(topN.z) * ex.z;
            Vector3 center = topCenter + topN * (support + KC_RiderLift);
            Vector3 pivotOff = mrb.position - mb.center;
            r.Support = support;
            Collider blk = SolidOverlap(center, ex * 0.95f, Quaternion.identity);
            if (blk != null)
            {
                r.Why = $"올릴 자리 {V(center)}가 '{blk.name}'와 겹침";
                yield break;
            }
            Teleport(m, center + pivotOff);
            int c0 = ast != null ? ast.InterventionCount : 0;

            // 정착
            int settle = Mathf.RoundToInt(KC_RideSettleSeconds / dt);
            for (int i = 0; i < settle; i++) yield return new WaitForFixedUpdate();
            if (m == null || mp == null) yield break;
            Bounds nb = SolidBounds(m);
            Vector3 loc = Quaternion.Inverse(prb.rotation) * (nb.center - prb.position) - box.center;
            float height = sign * loc.z - box.size.z * 0.5f;
            r.SettleHeight = height;
            bool onPanel = Mathf.Abs(loc.x) <= box.size.x * 0.5f + 0.1f && Mathf.Abs(loc.y) <= box.size.y * 0.5f + 0.1f
                           && height >= support - 0.15f && height <= support + 0.5f;
            if (!onPanel)
            {
                r.Why = $"정착 실패 — {KC_RideSettleSeconds:F1}s 뒤 도형 중심이 패널 윗면 위 {height:F2}U(기대 ≈{support:F2}) · 패널 면 위 가로 {loc.x:F2}(반폭 {box.size.x * 0.5f:F2})/세로 {loc.y:F2}(반폭 {box.size.y * 0.5f:F2})";
                yield break;
            }

            // 몰기
            mrb.WakeUp();
            Vector3 r0 = mrb.position;
            Vector3 lr = Quaternion.Inverse(prb.rotation) * (r0 - prb.position);                          // 패널 프레임에서 본 도형 위치 — 이 점이 패널에 붙어 간다고 가정한 "기대 위치"의 기준
            Vector3 lc = new Vector3(lr.x, lr.y, box.center.z + sign * box.size.z * 0.5f);                // 같은 자리의 윗면 위 접촉점(GroundVelocity 비교용)
            r.StartUp = KC_UpAngle(prb.rotation);
            Vector3 prevC = prb.position + prb.rotation * lc;
            float lastProg = mp.Progress;
            int still = 0, n = 0, grounded = 0;
            float sumGv = 0f, sumGvXz = 0f, sumExp = 0f, sumExpXz = 0f, maxGv = 0f, maxDev = 0f;
            int maxTicks = Mathf.CeilToInt(KC_RideMaxSeconds / dt);
            while (n < maxTicks)
            {
                mp.ApplyDrive(dir);
                yield return new WaitForFixedUpdate();
                n++;
                if (m == null || mp == null) break;
                Vector3 pp = prb.position;
                Quaternion pr = prb.rotation;
                Vector3 expC = pp + pr * lc;
                Vector3 vExp = (expC - prevC) / dt;
                prevC = expC;
                Vector3 gv = gc != null ? gc.GroundVelocity : Vector3.zero;
                float gvXz = new Vector2(gv.x, gv.z).magnitude;
                sumGv += gv.magnitude; sumGvXz += gvXz; maxGv = Mathf.Max(maxGv, gv.magnitude);
                sumExp += vExp.magnitude; sumExpXz += new Vector2(vExp.x, vExp.z).magnitude;
                maxDev = Mathf.Max(maxDev, (mrb.position - (pp + pr * lr)).magnitude);
                if (gc != null && gc.IsGrounded) grounded++;
                if (KC_UpAngle(pr) > KC_RideAngleMax) break;
                float prog = mp.Progress;
                if (Mathf.Abs(prog - lastProg) < 1e-6f) { if (++still >= 3) break; } else still = 0;
                lastProg = prog;
            }
            if (m == null || mp == null) yield break;

            Vector3 expEnd = prb.position + prb.rotation * lr;
            Vector3 dExp = expEnd - r0;
            Vector3 dAct = mrb.position - r0;
            r.Exp = dExp; r.Act = dAct;
            r.ExpLen = dExp.magnitude; r.ActLen = dAct.magnitude;
            r.Ratio = dExp.sqrMagnitude > 1e-6f ? Vector3.Dot(dAct, dExp) / dExp.sqrMagnitude : 0f;
            Vector3 he = new Vector3(dExp.x, 0f, dExp.z), ha = new Vector3(dAct.x, 0f, dAct.z);
            r.RatioH = he.sqrMagnitude > 0.04f ? Vector3.Dot(ha, he) / he.sqrMagnitude : -1f;   // 수평 변위가 0.2U 미만이면 비율을 안 낸다
            r.MaxDev = maxDev;
            r.Ticks = n; r.Seconds = n * dt; r.Grounded = grounded;
            int nn = Mathf.Max(1, n);
            r.GvMean = sumGv / nn; r.GvMax = maxGv; r.GvXzMean = sumGvXz / nn;
            r.ExpSpeedMean = sumExp / nn; r.ExpXzMean = sumExpXz / nn;
            r.Interventions = (ast != null ? ast.InterventionCount : 0) - c0;
            r.Respawns = RespawnsSince(m, since);
            r.Jammed = mp.IsJammed;
            r.Ran = true;
            completed();
        }

        private KC_RideStatus KC_Classify(KC_Ride r, MovablePortalPanel mp, out string why)
        {
            if (!r.Ran) { why = string.IsNullOrEmpty(r.Why) ? "시험이 돌지 않음" : r.Why; return KC_RideStatus.Inconclusive; }
            if (r.Interventions > 0 || r.Respawns > 0) { why = $"시험 중 Lab_AntiStuck 개입 {r.Interventions}회·복귀 {r.Respawns}회 — 비율을 믿을 수 없음"; return KC_RideStatus.Inconclusive; }
            if (r.ExpLen < KC_RideMinExpected)
            {
                why = $"패널에 붙은 점의 변위 {r.ExpLen:F2}U(< {KC_RideMinExpected}U) — 비율을 믿을 수 없음" + (r.Jammed ? " · 패널 끼임(IsJammed)" : "");
                return KC_RideStatus.Inconclusive;
            }
            if (r.Ratio >= KC_CarryRatioMin) { why = ""; return KC_RideStatus.Carried; }
            why = $"변위 비율 {r.Ratio * 100f:F0}% < {KC_CarryRatioMin * 100f:F0}%";
            return KC_RideStatus.NotCarried;
        }

        // ═════════════════════════ 대조군(합성 Rail 평판) ═════════════════════════

        /// <summary>S6 방 동쪽 열린 바닥 위에 윗면이 수평인 Rail 평판 하나를 팀 컴포넌트로 만든다. 후보 자리마다 바닥(로컬 y0 ±0.3)·움직임 전 구간 빈 공간·도형 6U 이상을 확인한다.</summary>
        private bool KC_BuildControl(out string why)
        {
            why = null;
            Transform gen = s6.Gen;
            List<string> skipped = new List<string>();
            foreach (Vector3 l in KC_CtrlSpotsLocal)
            {
                Vector3 from = gen.TransformPoint(new Vector3(l.x, l.y + 4f, l.z));
                if (!TryGround2(from, 8f, null, out Vector3 g, out _)) { skipped.Add($"{V(l)} 바닥 없음"); continue; }
                float gy = Lc(gen, g).y;
                if (Mathf.Abs(gy - l.y) > 0.3f) { skipped.Add($"{V(l)} 첫 면 y{gy:F2}"); continue; }

                Vector3 start = g + gen.up * (KC_CtrlLift + KC_CtrlSize.z * 0.5f);   // 평판 중심(윗면 = 바닥 + 1.0 + 0.2)
                Vector3 end = start + gen.forward * KC_CtrlTravel;
                Vector3 mid = (start + end) * 0.5f + gen.up * 1.4f;
                Vector3 half = new Vector3(KC_CtrlSize.x * 0.5f + 0.5f, 2.2f, KC_CtrlTravel * 0.5f + KC_CtrlSize.y * 0.5f + 0.4f);   // 바닥 위 0.3~4.7 · 이동 전 구간 + 길이 반 + 0.4
                Collider blk = SolidOverlap(mid, half, gen.rotation);
                if (blk != null) { skipped.Add($"{V(l)} '{blk.name}'와 겹침"); continue; }
                PlayerMover near = players.FirstOrDefault(p => p != null && HorizDist(p.transform.position, mid) < 6f);
                if (near != null) { skipped.Add($"{V(l)} 도형 '{near.name}'이 6U 안"); continue; }

                // 윗면(로컬 +Z)이 위, 길이(로컬 Y)가 섹터 +Z. S6 바닥 패널의 규약(forward +Y, up +Z — S6_Builder.cs:178 머리말)과 같다.
                Quaternion rot = Quaternion.LookRotation(gen.up, gen.forward);
                GameObject root = new GameObject("KC_ControlPanel_FUNC");
                root.transform.SetPositionAndRotation(start, rot);
                GameObject sp = new GameObject("KC_ControlPanel_FUNC_Start");
                sp.transform.SetPositionAndRotation(start, rot);
                GameObject ep = new GameObject("KC_ControlPanel_FUNC_End");
                ep.transform.SetPositionAndRotation(end, rot);

                // 팀 SpacePortalMenuItem.BuildMovablePortalPanel(:201-217)과 같은 구성 — RequireComponent가 PortalSurface(BoxCollider)·Rigidbody를 붙인다.
                MovablePortalPanel mp = root.AddComponent<MovablePortalPanel>();
                PortalSurface surf = root.GetComponent<PortalSurface>();
                BoxCollider solid = surf.Box;
                solid.isTrigger = false;
                solid.center = Vector3.zero;
                solid.size = KC_CtrlSize;
                Rigidbody rb = root.GetComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                BoxCollider sensor = mp.riderSensor as BoxCollider;   // 에디터에서 Reset()이 돌았으면 이미 있다
                if (sensor == null) sensor = root.AddComponent<BoxCollider>();
                sensor.isTrigger = true;
                sensor.size = KC_CtrlSize + new Vector3(0f, 0f, 0.2f);
                sensor.center = new Vector3(0f, 0f, KC_CtrlSize.z * 0.5f + 0.1f);
                mp.riderSensor = sensor;
                mp.mode = MovablePortalPanel.PanelMode.Rail;
                mp.startPose = sp.transform;
                mp.endPose = ep.transform;
                mp.travelSpeed = 1f;
                mp.debugKeyboardDrive = false;

                kcControl = new KC_Control
                {
                    Root = root, StartPose = sp, EndPose = ep, Panel = mp, Surface = surf,
                    How = $"자리 local{V(Lc(gen, start))}→{V(Lc(gen, end))}(Rail 3U, travelSpeed 1U/s, 윗면 법선 local{V(gen.InverseTransformDirection(rot * Vector3.forward))}, 크기 {V(KC_CtrlSize)}, 바닥 위 {KC_CtrlLift}U)"
                };
                cleanups.Add(KC_DestroyControl);
                return true;
            }
            why = skipped.Count == 0 ? "후보 자리 없음" : "후보 자리 모두 쓸 수 없음: " + string.Join(", ", skipped);
            return false;
        }

        private void KC_DestroyControl()
        {
            KC_Control c = kcControl;
            if (c == null) return;
            kcControl = null;
            if (c.Root != null) Object.Destroy(c.Root);
            if (c.StartPose != null) Object.Destroy(c.StartPose);
            if (c.EndPose != null) Object.Destroy(c.EndPose);
        }

        /// <summary>조작권·합성 패널·로그 구독 원복(멱등). 정상 흐름에서 한 번, Finish의 cleanups에서 한 번 더 불려도 안전하다.</summary>
        private void KC_Restore()
        {
            Application.logMessageReceived -= KC_OnLog;
            if (kcCtrlSaved && cube != null) cube.SetControlled(kcCtrl0);
            KC_DestroyControl();
        }

        /// <summary>팀 킨네마틱 속도 대입 경고("Setting linear/angular velocity of a kinematic body is not supported")를 센다 — 관찰 전용.</summary>
        private void KC_OnLog(string condition, string stackTrace, LogType type)
        {
            if (condition == null) return;
            if (condition.IndexOf("kinematic", StringComparison.OrdinalIgnoreCase) < 0) return;
            if (condition.IndexOf("angular", StringComparison.OrdinalIgnoreCase) >= 0) kcWarnAng++;
            else if (condition.IndexOf("linear", StringComparison.OrdinalIgnoreCase) >= 0 || condition.IndexOf("velocity", StringComparison.OrdinalIgnoreCase) >= 0) kcWarnLin++;
            else kcWarnOther++;
        }

        // ═════════════════════════ 문장 도우미 ═════════════════════════

        private string KC_DescribePanel(KC_PanelRep rep)
        {
            MovablePortalPanel mp = rep.Panel;
            PortalSurface ps = rep.Surf;
            Transform gen = s6.Gen;
            Rigidbody rb = mp.GetComponent<Rigidbody>();
            Quaternion rot = rb.rotation;
            Vector3 n = rot * Vector3.forward;
            Vector3 face = rb.position + rot * (ps.Box.center + new Vector3(0f, 0f, ps.Box.size.z * 0.5f));
            string s = $"{rep.Label}: mode {mp.mode} · 크기 {V(ps.Box.size)} · 진행도 {mp.Progress:F2} · 면 중심 섹터로컬{V(Lc(gen, face))} · 바깥 법선(로컬 +Z) 섹터로컬{V(gen.InverseTransformDirection(n))}(월드{V(n)}) · " +
                       $"위(+Y)와 이루는 각 {KC_UpAngle(rot):F1}°(서는 쪽 면 기준) · riderSensor {(mp.riderSensor != null ? "있음" : "없음")}";
            if (mp.mode == MovablePortalPanel.PanelMode.Rail)
            {
                if (mp.startPose != null && mp.endPose != null)
                {
                    Vector3 d = mp.endPose.position - mp.startPose.position;
                    s += $" · startPose→endPose {d.magnitude:F2}U 방향 섹터로컬{V(gen.InverseTransformDirection(d.normalized))} · travelSpeed {mp.travelSpeed:F2}U/s → 전 구간 {KC_FullTravelSeconds(mp):F2}s (팀 :206-215)";
                }
                else s += " · startPose/endPose 없음(움직이지 못함)";
            }
            else
            {
                // 팀 PivotRotation(:230-235)을 공개 필드로 다시 계산 — 진행도 0에서는 현재 자세 = 초기 자세(초기위치·초기회전)다.
                Vector3 axisW = (rot * mp.pivotAxisLocal).normalized;
                Vector3 pivotW = rb.position + rot * mp.pivotPointLocal;
                Vector3 endN = Quaternion.AngleAxis(mp.maxAngle - mp.minAngle, axisW) * n;
                s += $" · 회전축 월드{V(axisW)}(섹터로컬{V(gen.InverseTransformDirection(axisW))}) · 회전 중심 섹터로컬{V(Lc(gen, pivotW))}(= 초기위치 + 초기회전×pivotPointLocal{V(mp.pivotPointLocal)}, 패널 중심까지 {mp.pivotPointLocal.magnitude:F2}U) · " +
                     $"각도 {mp.minAngle:F0}→{mp.maxAngle:F0}° · rotationSpeed {mp.rotationSpeed:F0}°/s → 전 구간 {KC_FullTravelSeconds(mp):F2}s · 끝 법선 예상(팀 식) 섹터로컬{V(gen.InverseTransformDirection(endN))}(위와의 각 {Mathf.Min(Vector3.Angle(endN, Vector3.up), Vector3.Angle(-endN, Vector3.up)):F1}°)";
            }
            return s;
        }

        private static string KC_VelBrief(KC_Vel v)
        {
            if (v.Moving == 0) return $"움직이지 않음(진행도 {v.Progress0:F2}→{v.Progress1:F2}, IsJammed {v.Jammed})";
            return $"선속도 읽은 평균 {v.MeanRead:F2}↔실제 {v.MeanAct:F2}U/s(반영 {v.LinRatio * 100f:F0}%) · 접촉점 속도 읽은 {v.MeanPtRead:F2}↔실제 {v.MeanPtAct:F2}U/s(반영 {v.PtRatio * 100f:F0}%) · " +
                   $"각속도 읽은 {v.MeanAngRead:F2}↔실제 {v.MeanAngAct:F2}rad/s(반영 {v.AngRatio * 100f:F0}%)";
        }

        private static string KC_StatusLabel(KC_RideStatus s) =>
            s == KC_RideStatus.Carried ? "실림" : s == KC_RideStatus.NotCarried ? "안 실림" : s == KC_RideStatus.NotApplicable ? "해당 없음" : s == KC_RideStatus.Inconclusive ? "검사불가" : "미실행";

        /// <summary>판정 줄용 짧은 요약.</summary>
        private static string KC_RideBrief(KC_Ride r)
        {
            string gv = r.ExpSpeedMean >= 0.3f && r.GvMean < 0.05f ? " · GroundVelocity ≈0(실림은 접촉·마찰의 몫이지 속도 대입의 몫이 아님)" : "";
            return $"[{r.Label}] 패널에 붙은 점 변위 {r.ExpLen:F2}U 중 도형 {r.ActLen:F2}U(비율 {r.Ratio * 100f:F0}%{(r.RatioH >= 0f ? $", 수평만 {r.RatioH * 100f:F0}%" : "")}) · GroundVelocity 평균 {r.GvMean:F2}U/s(실제 {r.ExpSpeedMean:F2}U/s){gv}";
        }

        private string KC_RideText(KC_Ride r)
        {
            if (!r.Ran) return r.Label + ": 시험이 돌지 않음 — " + r.Why;
            Transform gen = s6.Gen;
            return $"패널에 붙은 점 변위 섹터로컬{V(gen.InverseTransformDirection(r.Exp))}({r.ExpLen:F2}U) ↔ 도형 변위 {V(gen.InverseTransformDirection(r.Act))}({r.ActLen:F2}U) → 비율 {r.Ratio * 100f:F0}%" +
                   $"{(r.RatioH >= 0f ? $"(수평만 {r.RatioH * 100f:F0}%)" : "(수평 변위 0.2U 미만 — 수평 비율 없음)")} · 최대 이탈 {r.MaxDev:F2}U · {r.Ticks}틱({r.Seconds:F2}s) · 시작 윗면 각 {r.StartUp:F1}° · " +
                   $"정착 높이 {r.SettleHeight:F2}U(기대 ≈{r.Support:F2}) · PlayerGroundContact.GroundVelocity 평균 {r.GvMean:F2}U/s(최대 {r.GvMax:F2}, 수평 평균 {r.GvXzMean:F2}) ↔ 같이 움직이는 점의 실제 속도 평균 {r.ExpSpeedMean:F2}U/s(수평 {r.ExpXzMean:F2}) · " +
                   $"접지 {r.Grounded}/{r.Ticks}틱 · PlayerMover가 GroundVelocity를 쓰는 경로(조작 중·InputLocked 아님·useTorqueRolling 아님) {r.GvUsed} · AntiStuck 개입 {r.Interventions} · 복귀 {r.Respawns} · IsJammed {r.Jammed}";
        }
    }
}
#endif
