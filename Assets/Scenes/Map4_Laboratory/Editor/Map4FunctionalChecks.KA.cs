#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 재검증 T1 "추가 기능 검사 A" (팀 develop abad97e 반영분) — S1-7·S1-8·S1-9 / S2-5 / S3-5 / G-1.
/// 기존 러너(Map4FunctionalChecks.cs)의 partial 조각이다. 기존 도우미(Report·CheckItem·Detail/Pass/Fail/NA·Teleport·WaitIdle·
/// ParkPlayers·Snapshot/CheckUnchanged·InScene·PickByName·TryGround2·SolidOverlap·PredictMoverHoriz 등)를 그대로 쓰고,
/// 새 멤버는 전부 접두어 KA_ 를 단다. 메인 파일은 건드리지 않는다(등록 조각은 KA_등록.md).
///
/// [금지 준수 — 클래스 머리 주석(Map4FunctionalChecks.cs :55-59)과 같다] 팀 코드·맵 코드·씬 수정 0 · 팀 private 리플렉션 0
/// (공개 필드/메서드/프로퍼티/UnityEvent·C# event와 공개 인터페이스 IInteractionProvider.CollectActions + InteractionAction.execute만) ·
/// 씬 저장 0 · Time.timeScale/fixedDeltaTime/레이어 행렬 변경 0.
///
/// [중앙 입력(E/V) 흉내 — KA_TakeControl·KA_Press]
/// 팀 InteractionController(InteractionController.cs)는 Update(:127-159)에서 지금 조작 중인 PlayerMover를 InteractionController.Controlled(:41)로
/// 갱신하고, 각 제공자는 CollectActions에서 Controlled를 읽는다(예: DreamThreadController.cs :151-153, CatapultLoadController.cs :230-232).
/// 그래서 ① PlayerMover.SetControlled(공개, PlayerMover.cs :205 — 팀 PlayerControlSwitcher.ApplyActive(:77-94)가 쓰는 같은 창구. 스위처 자체에는 활성 도형을 지정하는 공개 메서드가 없다 — 공개는 ActiveTarget·RegisterPlayer·UnregisterPlayer뿐)로 대상에게 조작권을 넘기고
/// ② 한 프레임 이상 기다려 Controlled == 대상을 확인한 뒤 ③ 씬의 IInteractionProvider에서 CollectActions로 액션을 모아
/// 컨트롤러와 같은 규칙(붙잡힘이면 allowWhenGripped만 — InteractionController.IsGripped(:44)·Update :149-155, 채널·trigger·enabled — Channel.Offer :82-99,
/// 후보 중재 — InteractionLogic.IsBetter)으로 하나를 골라 execute()를 부른다. 탭은 tap.execute, 홀드는 hold.execute(시간 경과는 판정 대상 아님).
/// </summary>
public static partial class Map4FunctionalChecks
{
    private partial class Driver : MonoBehaviour
    {
        // ═════════════════════════ KA 상수 ═════════════════════════

        private const float KA_ScaleTol = 0.02f;          // [구현 결정] 크기 복원 허용 — PlayerShapeController 수렴 기준(lerpSnapEpsilon 0.001)보다 넉넉히
        private const float KA_TetherTol = 0.6f;          // [구현 결정] 구속 초과 허용 — 조인트 contactDistance 0.02 + 몸 중심 떨림
        private const float KA_PushWindow = 7f;           // [구현 결정] S1-9 바깥으로 미는 최대 시간
        private const float KA_LeverPulled = 30f;         // [구현 결정] 레버 "당김" = 닫힘 각도에서 30° 이상 벗어남(전체 범위 90°의 1/3)
        private const float KA_LeverReturnTol = 2f;       // [구현 결정] 레버 "복귀" = 팀 복귀 목표각(-45°) ±2° 이내(시작 각도는 인정 안 함 — L3)
        private const float KA_LeverClosedAngle = -45f;   // [팀] LeverHead.cs :82(MoveTowardsAngle 목표 -45f)·:90-95(-45 도달 시 -45로 스냅) 복귀 목표 각도(코드에 -45 고정)

        // ═════════════════════════ KA 공용: 조작권·중앙 입력 흉내 ═════════════════════════

        private sealed class KA_PressResult
        {
            public bool Executed;
            public MonoBehaviour Provider;
            public string Verb = "";
            public bool BlockedOnly;      // 실행 가능한 탭이 없고 회색(enabled=false) 후보뿐 — 실제 게임에선 토스트만 뜬다
            public string Reason = "";
            public string Candidates = "";
            public string Note = "";
        }

        private sealed class KA_ControlState
        {
            public readonly Dictionary<PlayerMover, bool> Saved = new Dictionary<PlayerMover, bool>();
            public bool Taken;
            public PlayerMover Target;
            public string Error = "";
        }

        /// <summary>PlayerMover.IsControlled 원래 값으로 되돌린다(여러 번 불러도 안전).</summary>
        private void KA_RestoreControl(KA_ControlState st)
        {
            if (st == null || st.Saved.Count == 0) return;
            foreach (KeyValuePair<PlayerMover, bool> kv in st.Saved)
                if (kv.Key != null) kv.Key.SetControlled(kv.Value);
            st.Saved.Clear();
            st.Taken = false;
        }

        /// <summary>대상 도형에만 조작권을 주고 InteractionController.Controlled == 대상이 될 때까지 기다린다(최대 40프레임).</summary>
        private IEnumerator KA_TakeControl(PlayerMover target, KA_ControlState st)
        {
            st.Target = target;
            st.Taken = false;
            st.Error = "";
            if (target == null) { st.Error = "대상 도형 없음"; yield break; }
            if (Object.FindObjectOfType<InteractionController>() == null)
            {
                st.Error = "씬에 InteractionController 인스턴스가 없음 — 중앙 입력(E/V) 흉내 불가";
                yield break;
            }
            if (st.Saved.Count == 0)
                foreach (PlayerMover p in players)
                    if (p != null) st.Saved[p] = p.IsControlled;
            foreach (PlayerMover p in players)
                if (p != null) p.SetControlled(p == target);

            int frames = 0;
            while (InteractionController.Controlled != target && frames < 40) { frames++; yield return null; }
            if (InteractionController.Controlled != target)
            {
                st.Error = $"조작권을 넘기고 {frames}프레임 뒤에도 InteractionController.Controlled = {Nm(InteractionController.Controlled)}(기대 {Nm(target)})";
                KA_RestoreControl(st);
                yield break;
            }
            st.Taken = true;
        }

        /// <summary>손(E)/장비(V) 한 번 누름을 흉내 낸다. InteractionController.Update·Process·Channel.Offer와 같은 규칙으로 후보를 고르고
        /// execute가 true면 고른 액션의 execute()를 부른다. Controlled가 holder가 아니면 아무것도 하지 않는다(Note에 사유).</summary>
        private KA_PressResult KA_Press(PlayerMover holder, InteractionChannel channel, InteractionTrigger trigger, bool execute)
        {
            KA_PressResult r = new KA_PressResult();
            if (holder == null) { r.Note = "대상 도형 없음"; return r; }
            if (InteractionController.Controlled != holder)
            {
                r.Note = $"InteractionController.Controlled = {Nm(InteractionController.Controlled)} ≠ {Nm(holder)}";
                return r;
            }

            bool gripped = InteractionController.IsGripped(holder);
            List<KeyValuePair<MonoBehaviour, InteractionAction>> cands = new List<KeyValuePair<MonoBehaviour, InteractionAction>>();
            List<InteractionAction> buf = new List<InteractionAction>();
            int providers = 0;
            foreach (MonoBehaviour mb in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (mb == null || !mb.isActiveAndEnabled) continue;      // 제공자는 OnEnable에서 Register — 활성+enabled인 것만 컨트롤러에 올라 있다
                IInteractionProvider ip = mb as IInteractionProvider;
                if (ip == null) continue;
                providers++;
                buf.Clear();
                try { ip.CollectActions(buf); }
                catch (Exception e) { r.Note += $"[{mb.GetType().Name} '{mb.name}' CollectActions 예외: {e.Message}] "; continue; }
                for (int i = 0; i < buf.Count; i++)
                    if (!gripped || buf[i].allowWhenGripped)             // InteractionController.Update :149-155
                        cands.Add(new KeyValuePair<MonoBehaviour, InteractionAction>(mb, buf[i]));
            }

            bool hasTap = false, hasBlocked = false, hasHold = false;
            KeyValuePair<MonoBehaviour, InteractionAction> tap = default, blocked = default, hold = default;
            List<string> names = new List<string>();
            foreach (KeyValuePair<MonoBehaviour, InteractionAction> kv in cands)
            {
                InteractionAction a = kv.Value;
                if (a.channel != channel) continue;
                names.Add($"{kv.Key.GetType().Name}'{kv.Key.name}' {a.trigger} '{a.verb}'{(a.enabled ? "" : "(회색: " + a.reason + ")")} 우선순위 {a.priority} 거리 {a.distance:F2}");
                if (a.trigger == InteractionTrigger.Hold)
                {
                    if (!a.enabled) continue;                            // Channel.Offer :86
                    if (!hasHold || InteractionLogic.IsBetter(a.priority, a.distance, hold.Value.priority, hold.Value.distance)) { hold = kv; hasHold = true; }
                }
                else if (a.enabled)
                {
                    if (!hasTap || InteractionLogic.IsBetter(a.priority, a.distance, tap.Value.priority, tap.Value.distance)) { tap = kv; hasTap = true; }
                }
                else if (!hasBlocked || InteractionLogic.IsBetter(a.priority, a.distance, blocked.Value.priority, blocked.Value.distance)) { blocked = kv; hasBlocked = true; }
            }
            r.Candidates = $"제공자 {providers}개 · {channel} 채널 후보 {names.Count}개" + (names.Count == 0 ? "" : ": " + string.Join(" / ", names)) + (gripped ? " (붙잡힘 → 해제 액션만)" : "");

            KeyValuePair<MonoBehaviour, InteractionAction> chosen = default;
            bool has = false;
            if (trigger == InteractionTrigger.Tap)
            {
                if (hasTap) { chosen = tap; has = true; }
                else if (hasBlocked)
                {
                    r.BlockedOnly = true;
                    r.Reason = blocked.Value.reason ?? "";
                    r.Verb = blocked.Value.verb ?? "";
                    r.Provider = blocked.Key;
                    return r;
                }
            }
            else if (hasHold) { chosen = hold; has = true; }
            if (!has) { r.Note += "해당 입력으로 실행 가능한 액션이 올라오지 않음"; return r; }

            r.Provider = chosen.Key;
            r.Verb = chosen.Value.verb ?? "";
            if (execute)
            {
                if (chosen.Value.execute == null) { r.Note += "고른 액션의 execute가 null"; return r; }
                chosen.Value.execute();
                r.Executed = true;
            }
            return r;
        }

        /// <summary>예외·중단으로 붙잡힌 채 끝난 도형을 풀어 준다(동기 처리 — 같은 프레임에 E 탭 흉내 한 번). 조작권이 그 도형에게 있고 도킹·매달림(붙잡힘) 또는 당김 줄 연결 중일 때만.
        /// 그다음 조작권 원복. 정상 종료 때는 이미 풀려 있어 아무 일도 하지 않는다.</summary>
        private void KA_ExitCleanup(PlayerMover m, KA_ControlState st)
        {
            try
            {
                if (m != null && st != null && st.Taken && InteractionController.Controlled == m &&
                    (InteractionController.IsGripped(m) || CatapultLoadController.IsConnectedTo(m)))
                {
                    KA_PressResult r = KA_Press(m, InteractionChannel.Hand, InteractionTrigger.Tap, true);
                    report.Notes.Add($"KA 정리: '{m.name}' 붙잡힌 채 끝나 해제 입력 흉내 → 실행 {r.Executed} '{r.Verb}'");
                }
            }
            catch (Exception e) { report.Notes.Add("KA 정리 중 예외: " + e.Message); }
            KA_RestoreControl(st);
        }

        /// <summary>하위 단계를 자기 try/catch 안에서 펼쳐(RunSubStep과 같은 방식) 예외가 나도 onExit(정리)이 반드시 돌게 한다.
        /// 예외는 OnStepException으로 id 항목에 실패로 기록한다.</summary>
        private IEnumerator KA_Guard(IEnumerator body, Action onExit, string id)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(body);
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator top = stack.Peek();
                    bool moved;
                    object current = null;
                    try
                    {
                        moved = top.MoveNext();
                        if (moved) current = top.Current;
                    }
                    catch (Exception e)
                    {
                        OnStepException(id, e);
                        break;
                    }
                    if (!moved) { stack.Pop(); continue; }
                    if (current is IEnumerator nested) { stack.Push(nested); continue; }
                    yield return current;
                }
            }
            finally
            {
                if (onExit != null)
                {
                    try { onExit(); }
                    catch (Exception e) { report.Notes.Add($"{id} 정리 중 예외: {e.Message}"); }
                }
            }
        }

        /// <summary>PlayerShapeController가 localScale을 목표 크기에 맞출 때까지(최대 timeout초) 기다린다. 목표 = 팀 공개 GetTargetScale().</summary>
        private IEnumerator KA_WaitScale(PlayerShapeController psc, float timeout)
        {
            float t0 = Time.time;
            yield return null;
            while (psc != null && Vector3.Distance(psc.transform.localScale, psc.GetTargetScale()) > 0.005f && Time.time - t0 < timeout) yield return null;
        }

        private static string KA_S(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

        // ═════════════════════════ S1-7 [K2] 조향석 도킹 → 해제 뒤 크기 복원 ═════════════════════════

        private sealed class KA_DockTrial
        {
            public string Label = "";
            public bool Started, Docked, Undocked, NaProblem;
            public string Problem = "";
            public Vector3 Scale0, ScaleDock, ScaleEnd;
            public PlayerShapeController.EScaleState State0, StateDock, StateEnd;
            public float Grow0, GrowDock, GrowEnd;
            public string DockInfo = "", UndockInfo = "";

            /// <summary>해제 뒤 기대 localScale. 도킹 전이 Grown이면 도킹이 growMultiplier를 바꿔 남기므로(팀 방침 — CatapultSteerHandle.cs :52-56, 해제 때 되돌리지 않음)
            /// 시작 scale에 growMultiplier 비(끝/시작)를 곱한다. Normal·Shrunk는 growMultiplier와 무관하니 시작 scale 그대로.</summary>
            public Vector3 ExpectedEnd => (State0 == PlayerShapeController.EScaleState.Grown && Grow0 > 1e-4f) ? Scale0 * (GrowEnd / Grow0) : Scale0;
            public bool ScaleSame => Vector3.Distance(ScaleEnd, ExpectedEnd) <= KA_ScaleTol;
            public bool Same => Started && Docked && Undocked && ScaleSame && StateEnd == State0;

            public string Text() =>
                $"{Label}: 시작 scale{KA_S(Scale0)}·{State0}·growMultiplier {Grow0:F2} → 도킹 중 scale{KA_S(ScaleDock)}·{StateDock}·growMultiplier {GrowDock:F2} → " +
                $"해제 뒤 scale{KA_S(ScaleEnd)}·{StateEnd}·growMultiplier {GrowEnd:F2} (기대 {KA_S(ExpectedEnd)}, 차이 {Vector3.Distance(ScaleEnd, ExpectedEnd):F4}) / 도킹={Docked} 해제={Undocked}" +
                (Problem.Length > 0 ? " / 문제: " + Problem : "");
        }

        /// <summary>구의 크기 상태·growMultiplier를 기록해 둔 값으로 되돌린다(여러 번 불러도 안전). 팀 공개 SetGrowMultiplier/SetScaleState.</summary>
        private static void KA_RestoreScale(PlayerShapeController psc, PlayerShapeController.EScaleState state, float grow)
        {
            if (psc == null) return;
            psc.SetGrowMultiplier(grow);
            if (psc.CurrentState != state) psc.SetScaleState(state);
        }

        private IEnumerator KA_S1_7_SteerDockScale()
        {
            KA_ControlState st = new KA_ControlState();
            // L1: 예외·중단 경로에서도 구의 크기 상태·growMultiplier가 남지 않게 시작 값을 기록해 러너 cleanups(Finish)와 Guard 정리에 모두 등록한다(정상 경로 원복은 KA_DockTrialRun에서 그대로).
            PlayerShapeController psc0 = sphere != null ? sphere.GetComponent<PlayerShapeController>() : null;
            PlayerShapeController.EScaleState state0 = psc0 != null ? psc0.CurrentState : PlayerShapeController.EScaleState.Normal;
            float grow0 = psc0 != null ? psc0.growMultiplier : 1f;
            cleanups.Add(() => KA_RestoreControl(st));
            cleanups.Add(() => KA_RestoreScale(psc0, state0, grow0));
            yield return KA_Guard(KA_S1_7_Body(st), () => { KA_ExitCleanup(sphere, st); KA_RestoreScale(psc0, state0, grow0); }, "S1-7");
        }

        private IEnumerator KA_S1_7_Body(KA_ControlState st)
        {
            const string id = "S1-7";
            if (!Ready1(id)) yield break;
            List<CatapultSteerHandle> hs = InScene<CatapultSteerHandle>(s1.Scene);
            CatapultSteerHandle h = PickByName(hs, "S1_SlingCatapult", "S1 조향석(CatapultSteerHandle) 소유 투석기 루트", out string problem);
            if (h == null) { NA(id, NameNA(problem)); yield break; }
            if (h.dockAnchor == null || h.rootBody == null) { NA(id, $"CatapultSteerHandle '{h.name}'의 dockAnchor/rootBody 비어 있음"); yield break; }
            PlayerShapeController psc = sphere.GetComponent<PlayerShapeController>();
            PlayerShapeIdentity idn = sphere.GetComponent<PlayerShapeIdentity>();
            if (psc == null) { NA(id, $"구 '{sphere.name}'에 PlayerShapeController 없음 — 크기 상태 비교 불가"); yield break; }
            if (idn == null || idn.Kind != PlayerShapeStats.ShapeKind.Sphere) { NA(id, $"구 '{sphere.name}'의 PlayerShapeIdentity.Kind가 Sphere가 아님({(idn == null ? "컴포넌트 없음" : idn.Kind.ToString())}) — 조향석은 구 전용"); yield break; }
            Detail(id, $"조향석 '{h.name}' dockAnchor '{h.dockAnchor.name}' local{V(L1(h.dockAnchor.position))} dockRange {h.dockRange} growOnDock {h.growOnDock} dockedScaleMultiplier {h.dockedScaleMultiplier} " +
                       $"/ 구 '{sphere.name}' CurrentState {psc.CurrentState} growMultiplier {psc.growMultiplier:F2} localScale{KA_S(psc.transform.localScale)}");
            if (!h.growOnDock) Detail(id, "주의: growOnDock=false — 이 조향석은 도킹 때 크기를 바꾸지 않는 설정(복원 검사 의미가 약함)");

            yield return KA_TakeControl(sphere, st);
            if (!st.Taken) { NA(id, "검사불가(중앙 입력 흉내 불가): " + st.Error); yield break; }

            KA_DockTrial main = new KA_DockTrial { Label = "기본(검사 시작 때의 크기 상태에서)" };
            yield return KA_DockTrialRun(h, psc, st, false, PlayerShapeController.EScaleState.Normal, main);
            Detail(id, main.Text());
            if (main.DockInfo.Length > 0) Detail(id, "도킹 입력: " + main.DockInfo);
            if (main.UndockInfo.Length > 0) Detail(id, "해제 입력: " + main.UndockInfo);

            // 변형 시작(이미 Shrunk·Grown인 채 도킹) 대조 — 2026-09-29 결함 수정(ToggleScale 두 번 → 도킹 전 상태 저장·SetScaleState 복원)의 핵심 경우. 판정에는 합산하지 않는 하위 결과.
            KA_DockTrial shr = new KA_DockTrial { Label = "변형 시작 Shrunk" };
            KA_DockTrial grn = new KA_DockTrial { Label = "변형 시작 Grown" };
            if (main.Docked)
            {
                yield return KA_DockTrialRun(h, psc, st, true, PlayerShapeController.EScaleState.Shrunk, shr);
                Detail(id, shr.Text());
                yield return KA_DockTrialRun(h, psc, st, true, PlayerShapeController.EScaleState.Grown, grn);
                Detail(id, grn.Text());
                bool anyRan = shr.Started || grn.Started;
                bool anyBad = (shr.Started && !shr.Same) || (grn.Started && !grn.Same);
                SubStatus vs = !anyRan ? SubStatus.NA : (anyBad ? SubStatus.Fail : SubStatus.Pass);
                report.Get(id).SetSub("variant", "변형 시작 복원(Shrunk·Grown에서 도킹→해제)", vs, shr.Text() + " ‖ " + grn.Text());
            }
            else report.Get(id).SetSub("variant", "변형 시작 복원(Shrunk·Grown에서 도킹→해제)", SubStatus.Skipped, "기본 도킹이 안 돼 돌리지 않음");

            yield return WaitIdle(sphere, 4f);
            yield return ParkPlayers(s1.Park, sphere);

            if (!main.Started)
            {
                if (main.NaProblem) NA(id, "검사불가: " + main.Problem);
                else Fail(id, main.Problem);
                yield break;
            }
            if (!main.Docked) { Fail(id, "중앙 입력(E 탭)으로 조향석 도킹이 안 됨 — " + main.Problem); yield break; }
            if (!main.Undocked) { Fail(id, "도킹은 됐으나 E 탭으로 해제가 안 됨 — " + main.Problem); yield break; }
            List<string> bad = new List<string>();
            if (!main.ScaleSame) bad.Add($"해제 뒤 localScale {KA_S(main.ScaleEnd)} ≠ 도킹 전 기대 {KA_S(main.ExpectedEnd)} (차이 {Vector3.Distance(main.ScaleEnd, main.ExpectedEnd):F4} > {KA_ScaleTol})");
            if (main.StateEnd != main.State0) bad.Add($"해제 뒤 CurrentState {main.StateEnd} ≠ 도킹 전 {main.State0}");
            if (bad.Count == 0)
                Pass(id, $"도킹(E 탭) → 해제(E 탭) 뒤 크기 상태 복원: localScale {KA_S(main.Scale0)} → 도킹 중 {KA_S(main.ScaleDock)}({main.StateDock}) → {KA_S(main.ScaleEnd)}, CurrentState {main.State0} → {main.StateEnd}" +
                         $" (변형 시작 대조: {report.Get(id).Subs.FirstOrDefault(s => s.Key == "variant")?.Status})");
            else Fail(id, string.Join(" / ", bad));
        }

        /// <summary>도킹→해제 한 번(필요하면 시작 크기 상태를 먼저 강제). 끝나면 크기 상태·growMultiplier를 시작 전 값으로 되돌린다.</summary>
        private IEnumerator KA_DockTrialRun(CatapultSteerHandle h, PlayerShapeController psc, KA_ControlState st, bool force, PlayerShapeController.EScaleState forced, KA_DockTrial t)
        {
            PlayerShapeController.EScaleState origState = psc.CurrentState;
            float origGrow = psc.growMultiplier;
            yield return KA_DockTrialInner(h, psc, st, force, forced, t);
            // 되돌리기 — 팀 공개 SetGrowMultiplier/SetScaleState(PlayerShapeController.cs :264-290). growMultiplier는 도킹이 대입한 채 남기 때문에(팀 방침) 검사가 원래 값으로 복구한다.
            psc.SetGrowMultiplier(origGrow);
            if (psc.CurrentState != origState) psc.SetScaleState(origState);
            yield return KA_WaitScale(psc, 3f);
        }

        private IEnumerator KA_DockTrialInner(CatapultSteerHandle h, PlayerShapeController psc, KA_ControlState st, bool force, PlayerShapeController.EScaleState forced, KA_DockTrial t)
        {
            Rigidbody rb = sphere.GetComponent<Rigidbody>();
            Transform dockAnchor = h.dockAnchor;
            Transform origParent = sphere.transform.parent;

            yield return WaitIdle(sphere, 6f);
            if (IsBusy(sphere)) { t.Problem = "구가 6초 안에 풀리지 않음(ExternallyDriven/kinematic)"; t.NaProblem = true; yield break; }
            if (force && psc.CurrentState != forced) psc.SetScaleState(forced);
            yield return KA_WaitScale(psc, 3f);
            t.Scale0 = psc.transform.localScale; t.State0 = psc.CurrentState; t.Grow0 = psc.growMultiplier;
            t.Started = true;

            // 도킹: 구를 조향석 도킹 지점에 놓고 곧바로(같은 프레임, 물리가 밀기 전에) E 탭을 흉내 낸다.
            Teleport(sphere, dockAnchor.position);
            KA_PressResult p1 = KA_Press(sphere, InteractionChannel.Hand, InteractionTrigger.Tap, true);
            t.DockInfo = $"{p1.Candidates}{(p1.Note.Length > 0 ? " · " + p1.Note : "")} → 고른 것 {(p1.Provider != null ? p1.Provider.GetType().Name + " '" + p1.Verb + "'" : "없음")}{(p1.BlockedOnly ? " (회색: " + p1.Reason + ")" : "")}";
            if (!p1.Executed)
            {
                t.NaProblem = p1.Note.StartsWith("InteractionController.Controlled", StringComparison.Ordinal);
                t.Problem = $"E 탭으로 실행된 액션 없음(구↔도킹 지점 {Vector3.Distance(rb.position, dockAnchor.position):F2}U, dockRange {h.dockRange})";
                yield break;
            }
            if (p1.Provider != h) { t.Problem = $"E 탭이 조향석이 아닌 {p1.Provider.GetType().Name} '{p1.Verb}'를 실행함(엉뚱한 쪽)"; }
            yield return new WaitForFixedUpdate();
            t.Docked = sphere.ExternallyDriven && rb.isKinematic && sphere.transform.parent == dockAnchor;
            yield return KA_WaitScale(psc, 3f);
            t.ScaleDock = psc.transform.localScale; t.StateDock = psc.CurrentState; t.GrowDock = psc.growMultiplier;
            if (!t.Docked)
            {
                t.Problem += $" 도킹 상태 아님(ExternallyDriven {sphere.ExternallyDriven}, kinematic {rb.isKinematic}, 부모 '{(sphere.transform.parent != null ? sphere.transform.parent.name : "없음")}')";
                yield break;
            }

            // 해제: 도킹된 몸은 붙잡힘이라 allowWhenGripped 해제 액션만 올라온다.
            KA_PressResult p2 = KA_Press(sphere, InteractionChannel.Hand, InteractionTrigger.Tap, true);
            t.UndockInfo = $"{p2.Candidates}{(p2.Note.Length > 0 ? " · " + p2.Note : "")} → 고른 것 {(p2.Provider != null ? p2.Provider.GetType().Name + " '" + p2.Verb + "'" : "없음")}";
            if (!p2.Executed) { t.Problem += " 해제 액션이 올라오지 않음(붙잡힘 상태 해제 키 없음)"; yield break; }
            yield return new WaitForFixedUpdate();
            t.Undocked = !sphere.ExternallyDriven && !rb.isKinematic && sphere.transform.parent == origParent;
            yield return KA_WaitScale(psc, 3f);
            yield return new WaitForSeconds(0.2f);
            t.ScaleEnd = psc.transform.localScale; t.StateEnd = psc.CurrentState; t.GrowEnd = psc.growMultiplier;
            if (!t.Undocked) t.Problem += $" 해제 뒤 상태 이상(ExternallyDriven {sphere.ExternallyDriven}, kinematic {rb.isKinematic}, 부모 '{(sphere.transform.parent != null ? sphere.transform.parent.name : "없음")}')";
        }

        // ═════════════════════════ S1-8 [K5] 잡기 구역 위로 떨어진 도형도 잡힘 ═════════════════════════

        private IEnumerator KA_S1_8_CatchZoneDrop()
        {
            const string id = "S1-8";
            if (!Ready1(id)) yield break;
            PathChaserController ch = s1.Chaser;
            if (!ch.gameObject.activeInHierarchy || !ch.enabled || ch.agent == null) { NA(id, "추격 컨트롤러가 동작 중이 아님(S1-1 참고)"); yield break; }
            if (ch.wall == null) { NA(id, "chaser.wall 비어 있음 — 벽 파괴 전/후 구분 불가"); yield break; }
            if (!ch.wall.gameObject.activeInHierarchy) { NA(id, "파괴벽이 이미 없음 — SAFE_PRE 기대 조건('벽 파괴 전')을 만들 수 없음"); yield break; }
            SectionHitCounter counter = ch.safePreCounter;
            SectionSafePoint dest = counter != null ? counter.destination : null;
            if (dest == null) { Fail(id, "safePreCounter 또는 그 destination(SectionSafePoint)이 비어 있음"); yield break; }

            PathChaserCatchZone zone = ch.agent.GetComponent<PathChaserCatchZone>();
            if (zone == null) zone = InScene<PathChaserCatchZone>(s1.Scene).FirstOrDefault();
            AddName("S1 추격자 잡기 구역(PathChaserCatchZone)", "추격자(PathChaserAgent)와 같은 GameObject",
                    zone != null ? $"{zone.name}(agent '{ch.agent.name}')" : "(없음)", zone != null && zone.gameObject == ch.agent.gameObject);
            if (zone == null) { NA(id, "검사불가(이름 불일치): PathChaserCatchZone을 찾지 못함(추격자 오브젝트에 없음, S1 씬 전체에도 0개)"); yield break; }
            Collider zc = zone.GetComponent<Collider>();
            if (zc == null) { NA(id, $"검사불가: PathChaserCatchZone '{zone.name}'에 Collider 없음"); yield break; }

            PlayerMover victim = tetra;
            yield return WaitIdle(victim, 5f);
            Bounds zb = zc.bounds;
            float off = -1f;
            Vector3 drop = default;
            foreach (float o in new[] { 2f, 1.5f, 1f, 0.5f })
            {
                Vector3 p = new Vector3(zb.center.x, zb.max.y + o, zb.center.z);
                if (SolidOverlap(p + Vector3.up * 0.5f, new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity) == null) { off = o; drop = p; break; }
            }
            if (off < 0f) { NA(id, $"검사불가: 잡기 구역 윗면 +0.5~2 어디에도 몸이 들어갈 빈 자리가 없음(구역 local{V(L1(zb.center))}, 윗면 y{zb.max.y:F2})"); yield break; }

            Detail(id, $"잡기 구역 '{zone.name}' {zc.GetType().Name}(trigger={zc.isTrigger}) bounds local 중심{V(L1(zb.center))} 크기{V(zb.size)} · retryInterval {zone.retryInterval}s · controller 연결 {(zone.controller == ch ? "일치" : zone.controller == null ? "비어 있음" : "다른 컨트롤러")}");
            Detail(id, $"SAFE_PRE '{dest.name}' local{V(L1(dest.transform.position))} · 카운터 임계 {counter.hitsBeforeRespawn} · 쿨다운 {counter.hitCooldown}s — 임계 ≥2면 낙하 한 번(Enter)으로는 모자라고 구역 안에 머무는 동안 재판정(Stay)이 있어야 복귀한다");
            Detail(id, $"낙하 시작점 local{V(L1(drop))} = 구역 윗면 +{off:F1}(수평은 구역 중심) · 도형 '{victim.name}'(ExternallyDriven 없이 놓음) · 추격자 local{V(L1(ch.agent.transform.position))}");

            Dictionary<PlayerMover, Vector3> snap = Snapshot(Others(victim));
            float since = Time.time;
            Teleport(victim, drop);
            float tEnter = -1f;
            Collider vcol = FirstSolid(victim.gameObject);
            float deadline = Time.time + 8f;
            while (Time.time < deadline && RespawnsSince(victim, since) == 0)
            {
                yield return new WaitForFixedUpdate();
                if (tEnter < 0f && vcol != null && zc.bounds.Intersects(vcol.bounds)) tEnter = Time.time - since;
            }
            int resp = RespawnsSince(victim, since);
            if (resp == 0)
            {
                Vector3 endPos = victim.transform.position;
                bool inside = vcol != null && zc.bounds.Intersects(vcol.bounds);
                Fail(id, $"구역 위(+{off:F1})에서 놓은 도형이 8초 안에 잡히지 않음 — 끝 위치 local{V(L1(endPos))}, 구역 bounds와 {(inside ? "겹침(구역 안에 있는데 재판정으로도 안 잡힘)" : "안 겹침")}, 구역 진입 {(tEnter >= 0f ? $"+{tEnter:F2}s" : "기록 없음")}");
                yield return WaitIdle(victim, 3f);
                yield return ParkPlayers(s1.Park, victim);
                yield break;
            }
            float respAt = Time.time - since;
            yield return WaitIdle(victim, 4f);
            yield return new WaitForSeconds(0.3f);
            Vector3 pos = victim.transform.position;
            bool atDest = NearAny(pos, SafePoints(dest), 0.9f, 1.6f, out float bestD);
            string others = CheckUnchanged(snap, since, 0.5f);
            bool wallStill = ch.wall.gameObject.activeInHierarchy;
            Detail(id, $"'{victim.name}' 구역 진입 {(tEnter >= 0f ? $"+{tEnter:F2}s" : "기록 없음")} → 복귀 +{respAt:F2}s → local{V(L1(pos))}, 가장 가까운 SAFE_PRE 지점까지 수평 {bestD:F2}U, 벽 상태={(wallStill ? "파괴 전" : "파괴됨")}");
            Detail(id, "다른 도형: " + (others ?? "위치 유지(0.5U 이내)·복귀 없음"));
            List<string> bad = new List<string>();
            if (!atDest) bad.Add($"복귀 위치가 SAFE_PRE(및 보조) 아님(수평 {bestD:F2}U)");
            if (others != null) bad.Add("다른 도형 영향: " + others);
            if (!wallStill) bad.Add("검사 중 벽이 파괴돼 SAFE_PRE 기대 조건이 깨짐");
            if (bad.Count == 0) Pass(id, $"구역 윗면 +{off:F1}에서 놓은 도형이 +{respAt:F2}s에 잡혀 SAFE_PRE로 복귀(임계 {counter.hitsBeforeRespawn}), 다른 도형 유지");
            else Fail(id, string.Join(" / ", bad));
            yield return WaitIdle(victim, 3f);
            yield return ParkPlayers(s1.Park, victim);
        }

        // ═════════════════════════ S1-9 [K3①] 투석기 당김 줄 — 앵커 수평 12 제한 ═════════════════════════

        /// <summary>start(지면 접점)에서 dir(수평 단위)로 0.5U씩, 바닥(±0.35U)이 이어지고 몸 상자가 비어 있는 거리(≤ maxLen).</summary>
        private static float KA_FreeRun(Vector3 start, Vector3 dir, float maxLen)
        {
            float ok = 0f;
            for (float s = 0.5f; s <= maxLen + 1e-4f; s += 0.5f)
            {
                Vector3 q = start + dir * s;
                if (!TryGround2(q + Vector3.up * 1.5f, 4f, null, out Vector3 g, out _)) break;
                if (Mathf.Abs(g.y - start.y) > 0.35f) break;
                if (SolidOverlap(g + Vector3.up * 0.6f, new Vector3(0.45f, 0.45f, 0.45f), Quaternion.identity) != null) break;
                ok = s;
            }
            return ok;
        }

        /// <summary>앵커 둘레(수평 3·4.5·6U × 16방향)에서 세모를 놓고 바깥으로 밀 자리를 고른다. 점수 = 바깥으로 막힘 없이 걸을 수 있는 거리(≤14), 추격자가 켜져 있으면 가까운 쪽 감점.</summary>
        private bool KA_PickTetherSpot(Vector3 anchorPos, float connectRange, Vector3 chaserPos, bool chaserActive, out Vector3 spot, out Vector3 dir, out float freeRun, out string how)
        {
            spot = default; dir = Vector3.forward; freeRun = 0f;
            Transform gen = s1.Gen;
            float bestScore = float.MinValue;
            int noGround = 0, notFloor = 0, overlapped = 0, tooFar = 0, tried = 0;
            foreach (float r in new[] { 3f, 4.5f, 6f })
            {
                for (int k = 0; k < 16; k++)
                {
                    float ang = k * 22.5f * Mathf.Deg2Rad;
                    Vector3 d = gen.right * Mathf.Cos(ang) + gen.forward * Mathf.Sin(ang);
                    d.y = 0f;
                    d.Normalize();
                    Vector3 p = new Vector3(anchorPos.x, anchorPos.y, anchorPos.z) + d * r;
                    if (!TryGround2(new Vector3(p.x, anchorPos.y + 1f, p.z), anchorPos.y + 8f, null, out Vector3 g, out _)) { noGround++; continue; }
                    if (Mathf.Abs(Lc(gen, g).y - CheckpointFloorLocalY) > 0.3f) { notFloor++; continue; }
                    Vector3 sp = g + Vector3.up * SpawnLift;
                    if (Vector3.Distance(sp, anchorPos) > connectRange * 0.9f) { tooFar++; continue; }
                    if (SolidOverlap(sp + Vector3.up * 0.5f, new Vector3(0.6f, 0.6f, 0.6f), Quaternion.identity) != null) { overlapped++; continue; }
                    tried++;
                    float run = KA_FreeRun(g, d, 14f);
                    float score = run - r * 0.01f;
                    if (chaserActive)
                    {
                        float md = Mathf.Min(HorizDist(chaserPos, g), HorizDist(chaserPos, g + d * 14f), SegDist(new Vector3(chaserPos.x, g.y, chaserPos.z), g, g + d * 14f));
                        if (md < 9f) score -= 20f;
                    }
                    if (score > bestScore) { bestScore = score; spot = sp; dir = d; freeRun = run; }
                }
            }
            how = $"후보 {tried}곳 평가(바닥 없음 {noGround}·바닥 높이 다름 {notFloor}·연결 범위 밖 {tooFar}·겹침 {overlapped})";
            return bestScore > float.MinValue;
        }

        /// <summary>당김 줄 컨트롤러가 쓰는 한계(CatapultLoadController.cs UpdateLeash: limit = Max(maxTetherDistance, anchor.connectRange))와 위치 표.</summary>
        private void KA_TetherTable(CatapultLoadController lc, CatapultSteerHandle steer, float limit, List<string> rows, List<string> overRequired)
        {
            Vector3 ap = lc.anchor.transform.position;
            Transform gen = s1.Gen;
            float floorY = gen.TransformPoint(Vector3.zero).y;
            Action<string, Vector3, bool, string> add = (name, w, required, note) =>
            {
                float d = HorizDist(w, ap);
                bool over = d > limit;
                rows.Add($"{name}: local{V(L1(w))} → 앵커 수평 {d:F2}U {(over ? ">" : "≤")} {limit:F1} [{(required ? "필수" : "참고")}]{(note.Length > 0 ? " " + note : "")}");
                if (required && over) overRequired.Add($"{name} {d:F2}U");
            };
            rows.Add($"앵커 '{lc.anchor.name}' local{V(L1(ap))}(바닥 위 {ap.y - floorY:F2}U) · connectRange {lc.anchor.connectRange}(3D) · maxTetherDistance {lc.maxTetherDistance}");
            // 연결 가능 영역의 수평 반경 — 서 있는 몸 중심(바닥 +0.5)에서 3D 거리 connectRange 안
            float dy = ap.y - (floorY + 0.5f);
            float hmax = Mathf.Sqrt(Mathf.Max(0f, lc.anchor.connectRange * lc.anchor.connectRange - dy * dy));
            bool hOver = hmax > limit;
            rows.Add($"연결 가능 영역 수평 반경(바닥에 선 몸, dy {dy:F2}): {hmax:F2}U {(hOver ? ">" : "≤")} {limit:F1} [필수] 연결 후 제자리에서 장전·발사 가능");
            if (hOver) overRequired.Add($"연결 가능 영역 {hmax:F2}U");
            if (lc.arm != null && lc.arm.bucket != null)
            {
                Collider bt = FirstTrigger(lc.arm.bucket.gameObject);
                add("버킷 탑승 지점(네모가 타는 자리)", bt != null ? bt.bounds.center : lc.arm.bucket.transform.position, true, "— 앵커가 버킷 그룹에 붙어 있어 가깝다");
            }
            if (steer != null && steer.dockAnchor != null) add("조향 고리 도킹 지점(구가 서는 자리)", steer.dockAnchor.position, false, "— 세모가 이쪽에서 조율할 때");
            PathChaserController ch = s1.Chaser;
            if (ch != null && ch.wall != null)
            {
                Collider wc = ch.wall.GetComponent<Collider>();
                Vector3 wp = wc != null ? new Vector3(wc.bounds.center.x, floorY, wc.bounds.min.z) : ch.wall.transform.position;
                add("파괴벽 앞면 중앙(발사 목표)", wp, false, "— 연결 중 세모가 벽까지 갈 필요는 없다");
            }
            if (ch != null && ch.safePreCounter != null && ch.safePreCounter.destination != null)
                add("SAFE_PRE(잡힘 복귀 지점)", ch.safePreCounter.destination.transform.position, false, "— 팀 UpdateLeash는 복귀 직후 범위 밖이면 안으로 들어올 때까지 줄을 잇지 않는다(CatapultLoadController.cs :180-183)");
            if (ch != null && ch.agent != null && ch.agent.waypoints != null)
            {
                int inside = 0, total = 0;
                foreach (Transform w in ch.agent.waypoints)
                {
                    if (w == null) continue;
                    total++;
                    if (HorizDist(w.position, ap) <= limit) inside++;
                }
                rows.Add($"추격자 경로 지점 {total}개 중 앵커 수평 {limit:F1} 안 {inside}개 [참고] — 연결 중 세모가 추격 경로 가까이 서면 잡힐 수 있다(팀 의도)");
            }
        }

        private IEnumerator KA_S1_9_TetherLimit()
        {
            KA_ControlState st = new KA_ControlState();
            cleanups.Add(() => KA_RestoreControl(st));
            yield return KA_Guard(KA_S1_9_Body(st), () => KA_ExitCleanup(tetra, st), "S1-9");
        }

        private IEnumerator KA_S1_9_Body(KA_ControlState st)
        {
            const string id = "S1-9";
            if (!Ready1(id)) yield break;
            List<CatapultLoadController> lcs = InScene<CatapultLoadController>(s1.Scene);
            CatapultLoadController lc = PickByName(lcs, "S1_SlingCatapult", "S1 당김 줄 컨트롤러(CatapultLoadController) 소유 투석기 루트", out string problem);
            if (lc == null) { NA(id, NameNA(problem)); yield break; }
            if (lc.anchor == null || lc.arm == null || lc.arm.bucket == null) { NA(id, $"CatapultLoadController '{lc.name}'의 anchor/arm/bucket 비어 있음"); yield break; }
            PlayerShapeIdentity idn = tetra.GetComponent<PlayerShapeIdentity>();
            if (idn == null || idn.Kind != PlayerShapeStats.ShapeKind.Tetrahedron) { NA(id, $"세모 '{tetra.name}'의 PlayerShapeIdentity.Kind가 Tetrahedron이 아님({(idn == null ? "컴포넌트 없음" : idn.Kind.ToString())}) — 당김 줄 연결은 세모 전용"); yield break; }
            if (lc.maxTetherDistance <= 0f) { NA(id, $"maxTetherDistance={lc.maxTetherDistance} ≤ 0 — 이동 제한이 꺼져 있어 검사할 대상 없음"); yield break; }

            CatapultArm arm = lc.arm;
            Rigidbody trb = tetra.GetComponent<Rigidbody>();
            float limit = Mathf.Max(lc.maxTetherDistance, lc.anchor.connectRange);
            CatapultSteerHandle steer = lc.GetComponent<CatapultSteerHandle>();
            Detail(id, $"당김 줄 컨트롤러 '{lc.name}' · maxTetherDistance {lc.maxTetherDistance} · 앵커 connectRange {lc.anchor.connectRange} → 실제 한계 {limit:F1}(Max, 팀 UpdateLeash) · 팔 rest {arm.restAngle}° pulled {arm.pulledAngle}° 노치 {arm.pullNotchCount} · 세모 '{tetra.name}' moveSpeed {tetra.moveSpeed}");

            // (1) 필요한 자리 표 — 팀 공개 값과 S1 오브젝트 좌표로 계산(실측 전).
            List<string> rows = new List<string>();
            List<string> overRequired = new List<string>();
            KA_TetherTable(lc, steer, limit, rows, overRequired);
            foreach (string row in rows) Detail(id, "자리 표 · " + row);

            // (2) 준비 — 팔이 쉬는 상태, 버킷이 비어 있음.
            float tw = Time.time;
            while ((arm.State != CatapultArm.ArmState.Idle || Mathf.Abs(arm.CurrentAngle - arm.restAngle) > 0.5f) && Time.time - tw < 6f)
            {
                arm.BeginPull(0f);
                yield return null;
            }
            if (arm.bucket.HasOccupant) { NA(id, "버킷에 탑승자가 남아 있음(S1-3 잔류) — 당김 줄 검사 전제 불성립"); yield break; }
            yield return WaitIdle(tetra, 6f);
            if (IsBusy(tetra)) { NA(id, "세모가 6초 안에 풀리지 않음(ExternallyDriven/kinematic)"); yield break; }

            Vector3 ap0 = lc.anchor.transform.position;
            bool chaserActive = s1.Chaser.agent != null && s1.Chaser.agent.gameObject.activeInHierarchy;
            Vector3 chaserPos = s1.Chaser.agent != null ? s1.Chaser.agent.transform.position : Vector3.zero;
            if (!KA_PickTetherSpot(ap0, lc.anchor.connectRange, chaserPos, chaserActive, out Vector3 spot, out Vector3 dir, out float freeRun, out string how))
            {
                NA(id, "검사불가: 앵커 둘레에 세모를 놓을 자리가 없음 — " + how);
                yield break;
            }
            Detail(id, $"시험 자리 local{V(L1(spot))}(앵커 수평 {HorizDist(spot, ap0):F2}U, 3D {Vector3.Distance(spot, ap0):F2}U) · 바깥 방향 local{V(s1.Gen.InverseTransformDirection(dir))} · 막힘 없이 걸을 수 있는 거리 {freeRun:F1}U · {how} · 추격자 {(chaserActive ? "켜져 있음 local" + V(L1(chaserPos)) : "꺼짐")}");

            // (3) 연결 — 중앙 입력(E 탭) 흉내.
            yield return KA_TakeControl(tetra, st);
            if (!st.Taken) { NA(id, "검사불가(중앙 입력 흉내 불가): " + st.Error); yield break; }
            Teleport(tetra, spot);
            KA_PressResult pc = KA_Press(tetra, InteractionChannel.Hand, InteractionTrigger.Tap, true);
            Detail(id, $"연결 입력: {pc.Candidates}{(pc.Note.Length > 0 ? " · " + pc.Note : "")} → 고른 것 {(pc.Provider != null ? pc.Provider.GetType().Name + " '" + pc.Verb + "'" : "없음")}{(pc.BlockedOnly ? " (회색: " + pc.Reason + ")" : "")}");
            if (!pc.Executed)
            {
                if (pc.Note.StartsWith("InteractionController.Controlled", StringComparison.Ordinal)) NA(id, "검사불가(중앙 입력 흉내 불가): " + pc.Note);
                else Fail(id, $"세모를 당김 앵커 범위(3D {Vector3.Distance(trb.position, ap0):F2}U ≤ connectRange {lc.anchor.connectRange})에 놓았는데 E 탭으로 실행된 액션이 없음");
                yield return ParkPlayers(s1.Park, tetra);
                yield break;
            }
            if (pc.Provider != lc)
            {
                Fail(id, $"E 탭이 당김 줄이 아닌 {pc.Provider.GetType().Name} '{pc.Verb}'를 실행함(엉뚱한 쪽) — 후보: {pc.Candidates}");
                yield return ParkPlayers(s1.Park, tetra);
                yield break;
            }
            bool connected = CatapultLoadController.IsConnectedTo(tetra);
            bool drivenAtConnect = tetra.ExternallyDriven;
            // 구속 조인트는 연결 직후에만 관측하고 그 값(leashSeen·leashLimit·leashInfo)만 판정·문구에 쓴다.
            // 이유(FUNC_20261004_035513 S1-9 도구 결함): 해제하면 팀이 조인트를 Destroy해 Unity null이 되므로, 해제 뒤에 `leash` 참조를 다시 보면
            // 연결 중에는 있었던 조인트가 "생기지 않음"으로 잘못 판정되고 `leash.linearLimit`도 못 읽는다.
            ConfigurableJoint leash = null;
            bool leashSeen = false;
            float leashLimit = float.NaN;
            string leashInfo = "";
            for (int i = 0; i < 10 && !leashSeen; i++)
            {
                yield return new WaitForFixedUpdate();
                leash = tetra.GetComponent<ConfigurableJoint>();
                if (leash != null)
                {
                    leashSeen = true;
                    leashLimit = leash.linearLimit.limit;
                    leashInfo = $"linearLimit {leashLimit:F2} x/y/z={leash.xMotion}/{leash.yMotion}/{leash.zMotion}";
                }
            }
            Detail(id, $"연결 직후: IsConnectedTo={connected} · ExternallyDriven={tetra.ExternallyDriven}(팀 설계상 false여야 함) · 구속 조인트(ConfigurableJoint) {(leashSeen ? "있음 — " + leashInfo : "없음")} · 팔 각도 {arm.CurrentAngle:F1}° 상태 {arm.State}");

            // (4) 앵커에서 수평으로 멀어지는 쪽으로 계속 민다 — ExternallyDriven·kinematic 금지(걸면 팀이 조인트를 푼다). AddForce(속도 변화)만.
            float speed = tetra.moveSpeed > 0.5f ? tetra.moveSpeed : 5f;
            float maxFlat = HorizDist(trb.position, lc.anchor.transform.position);
            float tPush = Time.time, lastImprove = Time.time;
            string stopWhy = $"{KA_PushWindow:F0}초 끝";
            bool grabbed = false;
            while (Time.time - tPush < KA_PushWindow)
            {
                Vector3 apNow = lc.anchor.transform.position;
                Vector3 radial = new Vector3(trb.position.x - apNow.x, 0f, trb.position.z - apNow.z);
                Vector3 pushDir = radial.sqrMagnitude > 0.01f ? radial.normalized : dir;
                Vector3 v = trb.velocity;
                trb.AddForce(pushDir * speed - PredictMoverHoriz(tetra, new Vector3(v.x, 0f, v.z)), ForceMode.VelocityChange);
                yield return new WaitForFixedUpdate();
                if (tetra == null || trb == null) { stopWhy = "몸이 사라짐"; break; }
                if (IsBusy(tetra)) { stopWhy = "몸이 붙잡힘(복귀 연출/외부 기믹)"; grabbed = true; break; }
                float flat = HorizDist(trb.position, lc.anchor.transform.position);
                if (flat > maxFlat + 0.01f) { maxFlat = flat; lastImprove = Time.time; }
                if (Time.time - tPush > 2f && Time.time - lastImprove > 1.5f) { stopWhy = "1.5초 동안 최대 거리 갱신 없음(한계에 막힘)"; break; }
            }
            float pushEnd = HorizDist(trb.position, lc.anchor.transform.position);
            // 밀기를 멈추고 0.6초 더 본다(관성으로 한계를 넘는지).
            float tCoast = Time.time;
            while (Time.time - tCoast < 0.6f && trb != null)
            {
                yield return new WaitForFixedUpdate();
                if (trb == null) break;
                float flat = HorizDist(trb.position, lc.anchor.transform.position);
                if (flat > maxFlat) maxFlat = flat;
            }
            float ratio = Mathf.InverseLerp(arm.restAngle, arm.pulledAngle, arm.CurrentAngle);
            bool leashAtEnd = tetra != null && tetra.GetComponent<ConfigurableJoint>() != null;
            Detail(id, $"바깥으로 밀기({speed:F1}U/s, AddForce): 도달 최대 수평 거리 {maxFlat:F2}U(한계 {limit:F1}) · 밀기 끝 {pushEnd:F2}U · 중단 사유: {stopWhy} · 그때 팔 각도 {arm.CurrentAngle:F1}° → 당김 비율 {ratio:F2}(팀 공개 CurrentAngle로 환산, 휠 입력 없음) · 팔 상태 {arm.State} · 구속 조인트 {(leashAtEnd ? "유지" : "없음")}");

            // (5) 해제 — E 탭(발사). 연결자가 조작 중이라 해제가 올라온다.
            KA_PressResult pr = KA_Press(tetra, InteractionChannel.Hand, InteractionTrigger.Tap, true);
            Detail(id, $"해제 입력: {pr.Candidates}{(pr.Note.Length > 0 ? " · " + pr.Note : "")} → 고른 것 {(pr.Provider != null ? pr.Provider.GetType().Name + " '" + pr.Verb + "'" : "없음")}");
            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;
            bool released = !CatapultLoadController.IsConnectedTo(tetra);
            bool leashGone = tetra.GetComponent<ConfigurableJoint>() == null;
            Detail(id, $"해제 뒤: IsConnectedTo={!released} · 구속 조인트 {(leashGone ? "없음" : "남아 있음")}");
            if (!released)
            {
                // 연결이 안 풀리면 한 번 더 시도(Tab 게이트 등 확인용 기록) — 그래도 안 풀리면 정리 불가라 Fail.
                KA_PressResult pr2 = KA_Press(tetra, InteractionChannel.Hand, InteractionTrigger.Tap, true);
                yield return null;
                released = !CatapultLoadController.IsConnectedTo(tetra);
                Detail(id, $"해제 재시도: 실행 {pr2.Executed} → IsConnectedTo={!released}");
            }

            // (6) 최대 장전(비율 1.0)이 가능한가 — 휠 입력은 주입할 수 없으므로 팀 공개 BeginPull(1.0)로 팔이 pulledAngle에 닿는지만 본다(연결이 풀린 뒤라 컨트롤러가 덮어쓰지 않는다).
            float twait = Time.time;
            while (arm.State != CatapultArm.ArmState.Idle && arm.State != CatapultArm.ArmState.Loaded && arm.State != CatapultArm.ArmState.Pulling && Time.time - twait < 5f) yield return null;
            float hold = Mathf.Max(0f, arm.pullTransitionDuration) + 0.35f;
            float th = Time.time;
            while (Time.time - th < hold) { arm.BeginPull(1f); yield return null; }
            float fullAngle = arm.CurrentAngle;
            CatapultArm.ArmState fullState = arm.State;
            bool fullOk = Mathf.Abs(fullAngle - arm.pulledAngle) <= 1f && fullState == CatapultArm.ArmState.Loaded;
            float tr = Time.time;
            while ((Mathf.Abs(arm.CurrentAngle - arm.restAngle) > 0.5f || arm.State != CatapultArm.ArmState.Idle) && Time.time - tr < 4f) { arm.BeginPull(0f); yield return null; }
            Detail(id, $"최대 장전(팀 공개 BeginPull(1.0), 연결 해제 상태): 팔 {fullAngle:F1}°(pulledAngle {arm.pulledAngle}°) 상태 {fullState} → {(fullOk ? "도달" : "미도달")} · 되돌림 {arm.CurrentAngle:F1}°/{arm.State}");
            Detail(id, "당김 비율 근거: CatapultLoadController.UpdatePull은 wheelRatio(마우스 휠 누적)만으로 BeginPull(비율)을 부른다 — 앵커↔세모 거리는 비율에 관여하지 않는다(CatapultLoadController.cs 9차 개편 주석·UpdatePull). 그래서 연결 가능한 어느 자리(≤ 한계)에서도 비율 1.0이 성립한다. 휠 입력은 주입 불가라 실측은 위 API 확인으로 갈음.");

            yield return WaitIdle(tetra, 3f);
            yield return ParkPlayers(s1.Park, tetra);

            // (7) 판정 — Fail = 실제로 어긋난 것(연결·조인트·한계 초과·해제·최대 장전). 구속 실측이 한계까지 못 가거나 도구가 중단된 것은 Fail이 아니라 검사불가(M1).
            List<string> bad = new List<string>();
            if (!connected) bad.Add("E 탭 실행 뒤 IsConnectedTo=false(연결 안 됨)");
            if (!leashSeen) bad.Add("연결 뒤 10물리 스텝 안에 구속 조인트(ConfigurableJoint)가 생기지 않음(연결 직후 관측 기준)");
            if (drivenAtConnect && connected) bad.Add("연결 중 ExternallyDriven이 세워짐(팀 설계는 세우지 않음 — 이동 자체가 장전 입력)");
            if (maxFlat > limit + KA_TetherTol) bad.Add($"앵커 수평 {maxFlat:F2}U까지 나감 > 한계 {limit:F1}+{KA_TetherTol}(구속 실패)");
            if (!released) bad.Add("E 탭으로 연결이 해제되지 않음");
            if (!leashGone && released) bad.Add("해제 뒤에도 구속 조인트가 남음");
            if (!fullOk) bad.Add($"BeginPull(1.0)으로 팔이 최대 장전에 못 닿음({fullAngle:F1}°/{arm.pulledAngle}°, {fullState})");
            if (bad.Count > 0) { Fail(id, string.Join(" / ", bad)); yield break; }

            List<string> naWhy = new List<string>();
            if (grabbed) naWhy.Add($"도구 중단: 밀기 도중 몸이 붙잡혀({stopWhy}) 구속 실측이 끝나지 않음 — 도달 {maxFlat:F2}U / 한계 {limit:F1}");
            else if (maxFlat < limit - 1.0f) naWhy.Add($"구속 실측 미도달: 도달 {maxFlat:F2}U / 한계 {limit:F1} (한계 −1U 미만) · 중단 사유: {stopWhy} · 막힘 없이 걸을 수 있는 거리 {freeRun:F1}U");
            if (overRequired.Count > 0) naWhy.Add($"사용자 결정 필요: 필요한 자리가 앵커 수평 {limit:F1} 밖 — {string.Join(", ", overRequired)}");
            if (naWhy.Count > 0)
            {
                NA(id, string.Join(" / ", naWhy) + $" (구속 한계 초과는 없음: 최대 {maxFlat:F2}U ≤ {limit:F1}+{KA_TetherTol})");
                yield break;
            }
            Pass(id, $"E 탭 연결 → 앵커 수평 최대 {maxFlat:F2}U(한계 {limit:F1}의 1U 안까지 도달, 한계+{KA_TetherTol} 이내)에서 막힘, 구속 조인트 {(leashSeen ? leashLimit.ToString("F1") : "-")}(연결 직후 관측값), 당김 비율은 휠 전용(위치 무관)·BeginPull(1.0)로 팔 {fullAngle:F1}° 도달, 필수 자리(연결 가능 영역·버킷 탑승 지점) 모두 앵커 수평 {limit:F1} 안, E 탭 해제·조인트 제거");
        }

        // ═════════════════════════ S2-5 [K10] 레버 3개 당김 → 복귀 ═════════════════════════

        private sealed class KA_LeverTrial
        {
            public string Name = "";
            public LeverHead Head;
            public Collider HeadCol;
            public Transform LeverRoot;
            public bool Resolved;
            public string Problem = "";          // 이름·접근 문제(검사불가 사유)
            public float Rest = float.NaN;       // 시작 각도
            public float MaxAngle = float.NaN;   // 밀기 중 가장 +쪽 각도
            public float Delta;                  // MaxAngle - Rest
            public bool Pulled;
            public int Sign;                     // 성공한(또는 마지막으로 시도한) 접근 방향(+1 = pivot.forward)
            public string Attempts = "";
            public float ReleasedAt = -1f;
            public float ReturnedAfter = -1f;    // 놓은 뒤 복귀까지(초)
            public float AngleAtEnd = float.NaN;
            public bool Returned;
            public float Expect;                 // 예상 복귀 시간(초)
        }

        /// <summary>메인 ParkPlayers와 같은 규칙(구 0·네모 1·세모 2)으로 그 도형 자기 파킹 자리 — 다른 도형 자리와 겹치지 않게(L2).</summary>
        private Vector3 KA_OwnPark(PlayerMover m, Vector3[] park) => park[(m == sphere ? 0 : m == cube ? 1 : 2) % park.Length];

        private IEnumerator KA_S2_5_Levers()
        {
            yield return KA_Guard(KA_S2_5_Body(), () => { if (s2 != null && s2.Park != null && s2.Park.Length > 0) Teleport(cube, KA_OwnPark(cube, s2.Park)); }, "S2-5");
        }

        private IEnumerator KA_S2_5_Body()
        {
            const string id = "S2-5";
            if (!Ready2(id)) yield break;
            string[] names = { "S2_Lever1_Trap", "S2_Lever2", "S2_Lever3" };
            List<LeverHead> all = InScene<LeverHead>(s2.Scene);
            List<KA_LeverTrial> trials = new List<KA_LeverTrial>();
            foreach (string pn in names)
            {
                KA_LeverTrial t = new KA_LeverTrial { Name = pn };
                t.Head = all.FirstOrDefault(l => HasAncestor(l.transform, pn));
                if (t.Head == null) { t.Problem = $"검사불가(이름 불일치): 부모 '{pn}' 아래 LeverHead 없음(LeverHead 전체 {all.Count}: {string.Join(", ", all.Select(l => l.name).Take(6))})"; }
                else
                {
                    for (Transform p = t.Head.transform; p != null; p = p.parent) if (p.name == pn) { t.LeverRoot = p; break; }
                    t.HeadCol = t.Head.GetComponent<Collider>();
                    if (t.Head.leverPivot == null) t.Problem = $"'{t.Head.name}'.leverPivot 비어 있음";
                    else if (t.HeadCol == null || t.HeadCol.isTrigger) t.Problem = $"'{t.Head.name}'에 솔리드 Collider 없음(밀 수 없음)";
                    else t.Resolved = true;
                }
                trials.Add(t);
            }
            if (s2.Park == null || s2.Park.Length == 0) { NA(id, "S2 파킹점 없음 — 도형을 치울 곳이 없어 검사 불가"); yield break; }
            PlayerMover pusher = cube;
            float speed = pusher.moveSpeed > 0.5f ? pusher.moveSpeed : 3.5f;
            Detail(id, $"레버 {trials.Count(t => t.Resolved)}/3 확인 · LeverHead 전체 {all.Count} · 미는 도형 '{pusher.name}'(ExternallyDriven 없이 AddForce 속도 변화, 목표 {speed:F1}U/s) · 각도는 팀 공개 LeverHead.GetCurrentAngle()");

            // (1) 당김 — 한 개씩. 놓은 뒤(도형을 파킹점으로 순간이동) 바로 다음 레버로 가서 복귀는 나중에 한꺼번에 본다.
            foreach (KA_LeverTrial t in trials)
            {
                if (!t.Resolved) continue;
                yield return KA_LeverPull(t, pusher, speed);
                Detail(id, $"{t.Name}: 시작 {t.Rest:F1}° · 밀기 중 최대 {t.MaxAngle:F1}°(변화 {t.Delta:F1}°, 기준 ≥{KA_LeverPulled:F0}°) → {(t.Pulled ? "당김" : "당김 실패")} · 접근 시도: {t.Attempts}");
                yield return WaitIdle(pusher, 3f);
                Teleport(pusher, KA_OwnPark(pusher, s2.Park));
                t.ReleasedAt = Time.time;
                yield return new WaitForFixedUpdate();
            }

            // (2) 복귀 — 모두 놓은 상태에서 동시에 본다. 팀 LeverHead는 returnDelay 뒤 returnSpeed×20 °/s로 -45°를 향해 돌아온다.
            float maxExpect = 0f;
            foreach (KA_LeverTrial t in trials)
            {
                if (t.Resolved && Mathf.Abs(t.Rest - KA_LeverClosedAngle) > KA_LeverReturnTol)
                    Detail(id, $"{t.Name}: 경고 — 시작 각도 {t.Rest:F1}°가 팀 복귀 목표 {KA_LeverClosedAngle}°와 {KA_LeverReturnTol}° 넘게 다름(복귀 판정은 {KA_LeverClosedAngle}°만 인정)");
                if (!t.Resolved || !t.Pulled) continue;
                float exc = t.MaxAngle - KA_LeverClosedAngle;
                t.Expect = Mathf.Max(0f, t.Head.returnDelay) + Mathf.Max(0f, t.Head.pushExitGrace) + exc / Mathf.Max(0.01f, t.Head.returnSpeed * 20f);
                maxExpect = Mathf.Max(maxExpect, t.Expect);
                Detail(id, $"{t.Name}: 복귀 예상 {t.Expect:F1}s(returnDelay {t.Head.returnDelay} + 유예 {t.Head.pushExitGrace} + {exc:F0}°/{t.Head.returnSpeed * 20f:F1}°/s)");
            }
            float cap = Mathf.Clamp(maxExpect + 8f, 12f, 70f);
            float tRet0 = Time.time;
            while (Time.time - tRet0 < cap)
            {
                bool pending = false;
                foreach (KA_LeverTrial t in trials)
                {
                    if (!t.Resolved || !t.Pulled || t.Returned) continue;
                    float a = t.Head.GetCurrentAngle();
                    t.AngleAtEnd = a;
                    if (Mathf.Abs(a - KA_LeverClosedAngle) <= KA_LeverReturnTol) { t.Returned = true; t.ReturnedAfter = Time.time - t.ReleasedAt; }   // L3: 팀 복귀 목표각 −45°만 인정(시작 각도 불인정)
                    else pending = true;
                }
                if (!pending) break;
                yield return new WaitForFixedUpdate();
            }
            foreach (KA_LeverTrial t in trials)
                if (t.Resolved && t.Pulled) t.AngleAtEnd = t.Head.GetCurrentAngle();
            foreach (KA_LeverTrial t in trials)
                if (t.Resolved && t.Pulled)
                    Detail(id, $"{t.Name}: 복귀 {(t.Returned ? $"확인 — 놓은 뒤 {t.ReturnedAfter:F1}s에 {t.AngleAtEnd:F1}°" : $"미확인 — {cap:F0}s 관찰 끝 각도 {t.AngleAtEnd:F1}°(닫힘 {KA_LeverClosedAngle}°)")}");

            // 도형 정리
            yield return WaitIdle(pusher, 3f);
            yield return ParkPlayers(s2.Park, cube);

            // (3) 판정 — 실패가 하나라도 있으면 실패, 아니면 검사불가가 하나라도 있으면 검사불가, 전부 당김+복귀면 통과.
            List<string> bad = new List<string>(), na = new List<string>(), good = new List<string>();
            foreach (KA_LeverTrial t in trials)
            {
                if (!t.Resolved) { na.Add($"{t.Name}: {t.Problem}"); continue; }
                if (!t.Pulled)
                {
                    if (t.Attempts.StartsWith("접근 자리 없음", StringComparison.Ordinal)) na.Add($"{t.Name}: 검사불가 — {t.Attempts}");
                    else bad.Add($"{t.Name}: 당김 실패(최대 변화 {t.Delta:F1}° < {KA_LeverPulled:F0}°; {t.Attempts})");
                    continue;
                }
                if (!t.Returned) { bad.Add($"{t.Name}: 당김은 됐으나 {cap:F0}s 안에 복귀 안 함(끝 각도 {t.AngleAtEnd:F1}°, 예상 {t.Expect:F1}s)"); continue; }
                good.Add($"{t.Name} 당김 {t.Delta:F0}°→복귀 {t.ReturnedAfter:F1}s(예상 {t.Expect:F1}s)");
            }
            if (bad.Count > 0) Fail(id, string.Join(" / ", bad) + (good.Count > 0 ? " · 통과분: " + string.Join(", ", good) : ""));
            else if (na.Count > 0) NA(id, string.Join(" / ", na) + (good.Count > 0 ? " · 통과분: " + string.Join(", ", good) : ""));
            else Pass(id, "레버 3개 모두 당김 → 복귀(팀 복귀 목표각 -45° ±" + KA_LeverReturnTol + "°, 근거 LeverHead.cs :82·:90-95): " + string.Join(", ", good));
        }

        /// <summary>레버 하나를 도형으로 민다. 팀 LeverHead는 Player 태그 몸이 부딪힐 때(OnCollisionEnter/Stay) 접촉 법선의 pivot 로컬 z 부호로 목표 각도(±maxAngle)를 정한다
        /// (LeverHead.cs :103-125 OnCollisionEnter/Stay · :152-163 UpdatePushDirection). 닫힘 -45°에서 +쪽으로 당기려면 pivot 로컬 +z 방향으로 막대 넓은 면을 밀어야 하므로 +z 쪽 접근을 먼저, 안 되면 -z 쪽을 시도한다.</summary>
        private IEnumerator KA_LeverPull(KA_LeverTrial t, PlayerMover pusher, float speed)
        {
            LeverHead lh = t.Head;
            Rigidbody srb = pusher.GetComponent<Rigidbody>();
            t.Rest = lh.GetCurrentAngle();
            t.MaxAngle = t.Rest;
            List<string> attempts = new List<string>();
            bool anySpot = false;
            foreach (int sign in new[] { 1, -1 })
            {
                if (!KA_LeverApproachSpot(t, pusher, sign, out Vector3 spot, out string how)) { attempts.Add($"{(sign > 0 ? "+z" : "-z")} 쪽: {how}"); continue; }
                Detail("S2-5", $"{t.Name}: {(sign > 0 ? "+z" : "-z")} 쪽 접근 자리 선정 — {how}");
                anySpot = true;
                yield return WaitIdle(pusher, 5f);
                Teleport(pusher, spot);
                yield return new WaitForSeconds(0.35f);
                // 경사판(GEO_S2_Wedge_*) 위 자리라 정착·미끄러짐이 있을 수 있다 — 밀기 전에 도형(솔리드 합 bounds)과 막대의 높이 겹침을 한 번 더 확인해 남긴다.
                Bounds pb = SolidBounds(pusher);
                Bounds hb = t.HeadCol.bounds;
                bool yOverlap = pb.max.y > hb.min.y + 0.1f && pb.min.y < hb.max.y - 0.1f;
                Detail("S2-5", $"{t.Name}: {(sign > 0 ? "+z" : "-z")} 쪽 밀기 전 확인 — 도형 y {pb.min.y:F2}~{pb.max.y:F2} / 막대 y {hb.min.y:F2}~{hb.max.y:F2} → 높이 {(yOverlap ? "겹침" : "안 겹침")} · 놓은 자리에서 수평 {HorizDist(srb.position, spot):F2}U 이동");
                if (!yOverlap)
                {
                    attempts.Add($"{(sign > 0 ? "+z" : "-z")} 쪽 접근: 정착 뒤 도형 높이(y {pb.min.y:F2}~{pb.max.y:F2})가 막대(y {hb.min.y:F2}~{hb.max.y:F2})와 안 겹쳐 밀지 않음");
                    Teleport(pusher, KA_OwnPark(pusher, s2.Park));
                    yield return new WaitForFixedUpdate();
                    continue;
                }
                float a0 = lh.GetCurrentAngle();
                float t0 = Time.time, lastMove = Time.time;
                float localMax = a0;
                string why = "시간 끝";
                while (Time.time - t0 < 4f)
                {
                    Transform pv = lh.leverPivot;
                    Vector3 f = Vector3.ProjectOnPlane(pv.forward, Vector3.up).normalized * sign;
                    Vector3 rt = Vector3.ProjectOnPlane(pv.right, Vector3.up).normalized;
                    Vector3 rel = srb.position - t.HeadCol.bounds.center;
                    rel.y = 0f;
                    float lat = Vector3.Dot(rel, rt);
                    Vector3 want = f * speed - rt * Mathf.Clamp(lat * 2f, -1.5f, 1.5f);
                    Vector3 v = srb.velocity;
                    srb.AddForce(want - PredictMoverHoriz(pusher, new Vector3(v.x, 0f, v.z)), ForceMode.VelocityChange);
                    yield return new WaitForFixedUpdate();
                    if (pusher == null || srb == null) { why = "도형 사라짐"; break; }
                    float ang = lh.GetCurrentAngle();
                    if (ang > localMax + 0.2f) { localMax = ang; lastMove = Time.time; }
                    if (localMax - a0 >= KA_LeverPulled) { why = "당김 기준 도달"; break; }
                    if (Time.time - t0 > 1.5f && Time.time - lastMove > 1.2f) { why = "각도가 안 늘어남"; break; }
                }
                attempts.Add($"{(sign > 0 ? "+z" : "-z")} 쪽 접근(자리 local{V(Lc(s2.Gen, spot))}): {a0:F1}° → 최대 {localMax:F1}°({why})");
                t.Sign = sign;
                if (localMax > t.MaxAngle) t.MaxAngle = localMax;
                if (localMax - a0 >= KA_LeverPulled) { t.Pulled = true; break; }
                // 실패한 접근 — 도형을 치우고 레버가 쉬는 각도로 돌아올 때까지 짧게 기다리지 않고(복귀가 느리다) 다음 접근으로 간다.
                Teleport(pusher, KA_OwnPark(pusher, s2.Park));
                yield return new WaitForFixedUpdate();
            }
            t.Delta = t.MaxAngle - t.Rest;
            t.Attempts = anySpot ? string.Join(" / ", attempts) : "접근 자리 없음 — " + string.Join(" / ", attempts);
        }

        /// <summary>바닥 레이(TryGround2와 같은 거름 — 도형·탄·낙석·ignore 계층 제외)로 첫 바닥의 충돌체까지 돌려준다(바닥 이름을 Detail에 남기려고).</summary>
        private static bool KA_GroundHit(Vector3 from, float maxDist, Transform ignore, out RaycastHit hit)
        {
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, maxDist, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.collider.GetComponentInParent<PlayerMover>() != null || h.collider.GetComponentInParent<Projectile>() != null) continue;
                if (h.collider.GetComponentInParent<FallingRock>() != null || h.collider.GetComponentInParent<FallingRockShard>() != null) continue;
                hit = h;
                return true;
            }
            hit = default;
            return false;
        }

        /// <summary>막대 중심에서 pivot 로컬 ±z 반대쪽으로 gap만큼 떨어진 바닥 자리를 고른다. 채택 조건은 "도형이 막대에 닿을 수 있는 높이"라는 물리 기준이다:
        /// ① 바닥 g + 도형 높이 &gt; 막대 아랫면 + 0.1 ② g &lt; 막대 윗면 − 0.1 ③ 도형 윗면이 머리 위 고체(레버 칸 뚜껑 GEO_S2_Ceil_Bay*_Lid 아랫면) 아래 ④ 몸 윗부분 상자가 비어 있음 ⑤ 막대 쪽 광선이 막대에 먼저 닿음.
        /// 바닥은 막대 윗면 바로 위(bounds.max.y + 0.6)에서 아래로 쏜다(뚜껑 윗면을 바닥으로 잡지 않게 — FUNC_20261004_035513 S2-5 도구 결함).
        /// 레버 칸 바닥에는 경사판 GEO_S2_Wedge_Bay*_{A,B,C}(높이 0.3204~0.4, S2_Builder.cs :243-259)가 깔려 있어 레버 앞 바닥이 y 0.2~0.4다 — 예전 "기준 바닥 ±0.2" 거름은
        /// 이 경사판 자리를 모두 버려 검사불가를 만들었으므로(FUNC_20261004_040534) 높이 기준 거름을 뺐다. 경사판 위라 몸을 낮게(lift = 뚜껑 아랫면 − 0.05 − 도형 높이 − 바닥, 최대 SpawnLift) 놓는다.</summary>
        private bool KA_LeverApproachSpot(KA_LeverTrial t, PlayerMover pusher, int sign, out Vector3 spot, out string how)
        {
            spot = default;
            Transform pv = t.Head.leverPivot;
            Vector3 f = Vector3.ProjectOnPlane(pv.forward, Vector3.up);
            if (f.sqrMagnitude < 0.1f) { how = "pivot.forward가 수평이 아님"; return false; }
            Vector3 d = f.normalized * sign;
            Vector3 hc = t.HeadCol.bounds.center;
            float barBottom = t.HeadCol.bounds.min.y, barTop = t.HeadCol.bounds.max.y;
            float shapeH = Mathf.Max(0.5f, SolidBounds(pusher).size.y);
            List<string> why = new List<string>();
            foreach (float gap in new[] { 2.4f, 1.8f, 1.3f })
            {
                Vector3 flat = new Vector3(hc.x, hc.y, hc.z) - d * gap;
                if (!KA_GroundHit(new Vector3(flat.x, barTop + 0.6f, flat.z), 8f, t.LeverRoot, out RaycastHit gh)) { why.Add($"간격 {gap:F1}: 바닥 없음"); continue; }
                Vector3 g = gh.point;
                string gName = gh.collider.name;
                if (g.y + shapeH <= barBottom + 0.1f) { why.Add($"간격 {gap:F1}: 바닥 '{gName}' y {g.y:F2} — 도형 윗면 {g.y + shapeH:F2}가 막대 아랫면 {barBottom:F2}+0.1에 못 미침"); continue; }
                if (g.y >= barTop - 0.1f) { why.Add($"간격 {gap:F1}: 바닥 '{gName}' y {g.y:F2} ≥ 막대 윗면 {barTop:F2}−0.1 — 도형이 막대 위로 뜸"); continue; }
                float lidDist = FirstSolidDistance(new Vector3(flat.x, barTop + 0.1f, flat.z), Vector3.up, 6f, null);
                float lidY = lidDist < 6f ? barTop + 0.1f + lidDist : float.PositiveInfinity;
                if (g.y + 0.05f + shapeH >= lidY - 0.02f) { why.Add($"간격 {gap:F1}: 바닥 '{gName}' y {g.y:F2} + 도형 {shapeH:F2}가 머리 위 고체 아랫면 y {lidY:F2}에 닿음"); continue; }
                float lift = Mathf.Clamp(lidY - 0.05f - shapeH - g.y, 0.05f, SpawnLift);
                Vector3 sp = g + Vector3.up * lift;
                // 몸 윗부분 상자(바닥 +0.5 ~ 도형 윗면): 경사판 자체는 바닥 +0.5 아래라 걸리지 않는다. 레버 부품은 빼고 본다.
                float bTop = g.y + lift + shapeH, bBot = g.y + 0.5f;
                if (bTop - bBot > 0.1f)
                {
                    Collider blk = SolidOverlap(new Vector3(sp.x, (bTop + bBot) * 0.5f, sp.z), new Vector3(0.55f, (bTop - bBot) * 0.5f, 0.55f), Quaternion.identity);
                    if (blk != null && (t.LeverRoot == null || !blk.transform.IsChildOf(t.LeverRoot))) { why.Add($"간격 {gap:F1}: '{blk.name}'와 겹침"); continue; }
                }
                float hit = FirstSolidDistance(new Vector3(sp.x, hc.y, sp.z), d, gap + 2f, null);
                if (hit < gap - 0.7f) { why.Add($"간격 {gap:F1}: 막대 앞 {hit:F1}U에 다른 고체"); continue; }
                spot = sp;
                how = $"간격 {gap:F1}U · 바닥 '{gName}' y {g.y:F2} · 도형 높이 {shapeH:F2}(띄움 {lift:F2}) · 막대 y {barBottom:F2}~{barTop:F2} · 머리 위 고체 {(float.IsInfinity(lidY) ? "없음" : "아랫면 y " + lidY.ToString("F2"))}";
                return true;
            }
            how = $"접근 자리 없음(막대 y {barBottom:F2}~{barTop:F2}, 도형 높이 {shapeH:F2}; " + string.Join(", ", why) + ")";
            return false;
        }

        // ═════════════════════════ S3-5 [K7①] 세 도형 꿈의 실타래 매달림 ═════════════════════════

        private IEnumerator KA_S3_5_ThreadHang()
        {
            KA_ControlState st = new KA_ControlState();
            cleanups.Add(() => KA_RestoreControl(st));
            yield return KA_Guard(KA_S3_5_Body(st), () => KA_ExitCleanup(st.Target, st), "S3-5");
        }

        private sealed class KA_HangTrial
        {
            public string Shape = "";
            public bool Ran, ExpectRefuse, Refused, Hung, Moved, Released;
            public float Weight, Moved3D;
            public string Info = "";
            public string Problem = "";
            public bool NaProblem;
        }

        private IEnumerator KA_S3_5_Body(KA_ControlState st)
        {
            const string id = "S3-5";
            if (s3 == null) { NA(id, s3Problem ?? "S3 해석 실패"); yield break; }
            DreamThreadController[] ctrls = Object.FindObjectsOfType<DreamThreadController>();
            ThreadPinPlacer[] pins = Object.FindObjectsOfType<ThreadPinPlacer>();
            List<ThreadAnchor> anchors = InScene<ThreadAnchor>(s3.Scene);
            List<ThreadAnchor> hangable = anchors.Where(a => a.connectRange > 0f).ToList();
            float minR = anchors.Count > 0 ? anchors.Min(a => a.connectRange) : 0f, maxR = anchors.Count > 0 ? anchors.Max(a => a.connectRange) : 0f;
            Detail(id, $"로드된 모든 씬: DreamThreadController {ctrls.Length}개 · ThreadPinPlacer {pins.Length}개 / S3 씬: ThreadAnchor {anchors.Count}개(connectRange {minR:F1}~{maxR:F1}, 0보다 큰 것 {hangable.Count}개) · ThreadBridge {InScene<ThreadBridge>(s3.Scene).Count}개");
            AddName("S3 꿈의 실타래 컨트롤러(DreamThreadController, 로드된 모든 씬)", "1개 이상", ctrls.Length.ToString(), ctrls.Length >= 1);
            AddName("S3 매달릴 수 있는 ThreadAnchor(connectRange>0)", "1개 이상", $"{hangable.Count}/{anchors.Count}", hangable.Count >= 1);
            if (ctrls.Length == 0 || hangable.Count == 0)
            {
                NA(id, $"검사불가(이름 불일치): 찾은 것 — DreamThreadController {ctrls.Length}개, S3 ThreadAnchor {anchors.Count}개 중 connectRange>0 {hangable.Count}개(전부 줄다리 끝 고리, connectRange {minR:F1}~{maxR:F1}) / 기대 — 컨트롤러 1개 이상 + 매달릴 수 있는 앵커 1개 이상. " +
                          "S3에는 매달릴 고리가 없다(S3_Wiring.cs AnchorConnectRange=0f). 근거: 진행/판정/2026-09-28_초안S2S3S4_판정.md:16 판정 8 '뒤 구간 흔들기 고리 40개: 뺀다'(줄다리로 구성, 핀 배치기 금지 :18 판정 10) — 설계상 부재라 검사불가가 정상");
                yield break;
            }

            DreamThreadController ctrl = ctrls[0];
            if (ctrls.Length > 1) Detail(id, $"DreamThreadController {ctrls.Length}개 — 첫 번째 '{ctrl.name}' 사용");
            LineRenderer line = ctrl.GetComponent<LineRenderer>();
            yield return KA_TakeControl(sphere, st);
            if (!st.Taken) { NA(id, "검사불가(중앙 입력 흉내 불가): " + st.Error); yield break; }

            List<KA_HangTrial> trials = new List<KA_HangTrial>();
            foreach (PlayerMover who in new[] { sphere, cube, tetra })
            {
                KA_HangTrial t = new KA_HangTrial { Shape = who.name };
                trials.Add(t);
                yield return KA_TakeControl(who, st);
                if (!st.Taken) { t.Problem = st.Error; t.NaProblem = true; continue; }
                yield return KA_HangOne(ctrl, line, hangable, who, t);
                Detail(id, $"{t.Shape}: 무게 {t.Weight:F2}(임계 {ctrl.hangWeightThreshold}) 거부 기대 {t.ExpectRefuse} → " +
                           (t.Refused ? "거부됨" : t.Hung ? "매달림" : "매달리지 못함") + (t.Hung ? $" · 위치 변화 {t.Moved3D:F2}U({(t.Moved ? "있음" : "없음")}) · 놓기 {(t.Released ? "성공" : "실패")}" : "") + (t.Problem.Length > 0 ? " · 문제: " + t.Problem : "") + (t.Info.Length > 0 ? " · " + t.Info : ""));
            }
            yield return ParkPlayers(s3.Park, sphere, cube, tetra);

            List<string> bad = new List<string>(), na = new List<string>(), good = new List<string>();
            foreach (KA_HangTrial t in trials)
            {
                if (t.NaProblem) { na.Add($"{t.Shape}: {t.Problem}"); continue; }
                if (t.ExpectRefuse)
                {
                    if (t.Refused) good.Add($"{t.Shape} 무게 {t.Weight:F2} ≥ 임계 → 팀 규칙대로 거부(지시서의 '세 도형 모두 매달림'과 다름)");
                    else bad.Add($"{t.Shape}: 무게 {t.Weight:F2} ≥ 임계 {ctrl.hangWeightThreshold}인데 거부되지 않음({(t.Hung ? "매달림" : t.Problem)})");
                    continue;
                }
                if (!t.Hung) { bad.Add($"{t.Shape}: 매달리지 못함 — {t.Problem}"); continue; }
                if (!t.Moved) { bad.Add($"{t.Shape}: 매달렸으나 힘을 줘도 위치 변화 {t.Moved3D:F2}U(<0.5U)"); continue; }
                if (!t.Released) { bad.Add($"{t.Shape}: 놓기가 안 됨"); continue; }
                good.Add($"{t.Shape} 매달림·위치 변화 {t.Moved3D:F2}U·해제");
            }
            if (bad.Count > 0) Fail(id, string.Join(" / ", bad) + (good.Count > 0 ? " · 통과분: " + string.Join(", ", good) : ""));
            else if (na.Count > 0) NA(id, string.Join(" / ", na) + (good.Count > 0 ? " · 통과분: " + string.Join(", ", good) : ""));
            else Pass(id, string.Join(" · ", good));
        }

        private IEnumerator KA_HangOne(DreamThreadController ctrl, LineRenderer line, List<ThreadAnchor> hangable, PlayerMover who, KA_HangTrial t)
        {
            Rigidbody rb = who.GetComponent<Rigidbody>();
            t.Weight = PlayerWeight.Of(rb);
            t.ExpectRefuse = t.Weight >= ctrl.hangWeightThreshold;
            yield return WaitIdle(who, 6f);
            if (IsBusy(who)) { t.Problem = "도형이 6초 안에 풀리지 않음"; t.NaProblem = true; yield break; }

            // 앵커 아래 매달릴 자리: 실 길이 L(minLength~min(maxLength, connectRange))에서 바닥 위 1.4U 이상 뜨는 가장 긴 L.
            ThreadAnchor a = hangable[0];
            Vector3 ap = a.transform.position;
            Vector3 pos = default;
            bool found = false;
            float Lmax = Mathf.Min(ctrl.maxLength, a.connectRange * 0.9f);
            for (float L = Lmax; L >= ctrl.minLength - 1e-3f; L -= 0.5f)
            {
                Vector3 p = ap + Vector3.down * L;
                if (!TryGround2(p + Vector3.up * 0.5f, 40f, null, out Vector3 g, out _)) continue;
                if (p.y - g.y < 1.4f) continue;
                if (SolidOverlap(p + Vector3.up * 0.5f, new Vector3(0.6f, 0.6f, 0.6f), Quaternion.identity) != null) continue;
                pos = p; found = true; break;
            }
            if (!found) { t.Problem = $"앵커 '{a.name}' 아래 바닥에서 1.4U 이상 뜨는 매달림 자리 없음(스윙 불가)"; t.NaProblem = true; yield break; }
            t.Ran = true;

            Teleport(who, pos);
            KA_PressResult pr = KA_Press(who, InteractionChannel.Hand, InteractionTrigger.Tap, true);
            t.Info = $"{pr.Candidates}{(pr.Note.Length > 0 ? " · " + pr.Note : "")}";
            if (pr.BlockedOnly) { t.Refused = true; t.Info += $" → 회색 '{pr.Verb}': {pr.Reason}"; yield break; }
            if (!pr.Executed) { t.Problem = "E 탭으로 실행된 액션 없음"; yield break; }
            if (pr.Provider != ctrl) { t.Problem = $"E 탭이 실타래가 아닌 {pr.Provider.GetType().Name} '{pr.Verb}'를 실행함"; yield break; }
            yield return new WaitForFixedUpdate();
            t.Hung = who.ExternallyDriven && who.GetComponent<ConfigurableJoint>() != null && (line == null || line.enabled);
            if (!t.Hung) { t.Problem = $"실행했으나 매달림 상태 아님(ExternallyDriven {who.ExternallyDriven}, 조인트 {(who.GetComponent<ConfigurableJoint>() != null)}, 실 {(line != null && line.enabled)})"; yield break; }

            // 매달린 채 이동(스윙): 팀 입력은 Input.GetAxis라 주입할 수 없다 — 컨트롤러 FixedUpdate의 펌핑과 같은 크기(pumpAcceleration)의 접선 가속을 AddForce로 준다.
            yield return new WaitForSeconds(0.3f);
            Vector3 p0 = rb.position;
            float maxMove = 0f, t0 = Time.time;
            while (Time.time - t0 < 1.8f)
            {
                Vector3 rope = rb.position - ap;
                Vector3 ropeDir = rope.sqrMagnitude > 1e-4f ? rope.normalized : Vector3.down;
                Vector3 wish = a.lockToSidePlane ? Vector3.forward : s3.Gen.forward;
                Vector3 push = Vector3.ProjectOnPlane(wish, ropeDir);
                if (push.sqrMagnitude > 1e-6f) rb.AddForce(push.normalized * ctrl.pumpAcceleration, ForceMode.Acceleration);
                yield return new WaitForFixedUpdate();
                maxMove = Mathf.Max(maxMove, Vector3.Distance(rb.position, p0));
            }
            t.Moved3D = maxMove;
            t.Moved = maxMove >= 0.5f;

            // 놓기 — 매달린 몸은 붙잡힘이라 allowWhenGripped "실 놓기"만 올라온다.
            KA_PressResult rel = KA_Press(who, InteractionChannel.Hand, InteractionTrigger.Tap, true);
            t.Info += $" · 놓기: {rel.Candidates}";
            float tr = Time.time;
            while (who.ExternallyDriven && Time.time - tr < ctrl.launchReenableTimeout + 2f) yield return null;
            yield return null;
            t.Released = rel.Executed && !who.ExternallyDriven && who.GetComponent<ConfigurableJoint>() == null;
        }

        // ═════════════════════════ G-1 [K14] InteractionController 인스턴스 수 ═════════════════════════

        private IEnumerator KA_G1_InteractionControllerCount()
        {
            const string id = "G-1";
            yield return null;
            InteractionController[] active = Object.FindObjectsOfType<InteractionController>();
            List<InteractionController> every = Resources.FindObjectsOfTypeAll<InteractionController>().Where(c => c != null && c.gameObject.scene.IsValid()).ToList();
            string where = string.Join(" / ", every.Select(c => $"'{c.gameObject.name}' 씬 '{c.gameObject.scene.name}' 활성={c.gameObject.activeInHierarchy}"));
            int autoGo = Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g != null && g.scene.IsValid() && g.name.StartsWith("InteractionController", StringComparison.Ordinal));
            int loaded = UnityEngine.SceneManagement.SceneManager.sceneCount;
            Detail(id, $"활성 {active.Length}개 · 비활성 포함(씬에 속한 것) {every.Count}개 [{where}] · 이름이 InteractionController로 시작하는 GameObject {autoGo}개 · 로드된 씬 {loaded}개 · 정적 Controlled = {Nm(InteractionController.Controlled)}");
            if (every.Count == 1 && active.Length == 1) Pass(id, $"InteractionController 인스턴스 정확히 1개({where}) — 모든 섹터 검사 뒤");
            else Fail(id, $"InteractionController 인스턴스 수가 1이 아님: 활성 {active.Length}, 비활성 포함 {every.Count} [{where}]");
        }
    }
}
#endif
