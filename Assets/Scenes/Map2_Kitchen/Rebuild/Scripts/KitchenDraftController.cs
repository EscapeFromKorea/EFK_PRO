using UnityEngine;

namespace Map2KitchenDraft
{
    /// <summary>Kitchen map review controls and the confirmed key-only exit condition.</summary>
    public sealed class KitchenDraftController : MonoBehaviour, IMpSynced, IMpEventSink
    {
        public Transform[] chapterStarts;
        public string[] chapterNames;
        public DraftChapterRegion[] chapters;
        // Kept only for older serialized draft scenes; chapter selection uses 3D regions.
        public float[] chapterBoundaries;
        public Transform key;
        public Transform doorVisual;
        public Collider doorBlocker;
        public Vector3 doorOpenOffset = new Vector3(0f, 3f, 0f);
        public Transform finalDropPoint;
        public Camera previewCamera;
        // Set by the rebuilt scene to the actual lower, hollow sink interior.
        // The record supports review; the physical kitchen layout defines the route.
        public BoxCollider sinkInteriorVolume;
        public float interactionRadius = 2f;
        [SerializeField] private RespawnZone[] fallCheckpoints;

        public bool HasKey { get; private set; }
        public bool ExitReached { get; private set; }
        public bool HasVisitedSink { get; private set; }
        // Zero-based; display text uses +1.
        public int CurrentChapter { get; private set; }

        /// <summary>조작 중인 도형. Tab 스위처가 있으면 그 활성 도형, 없으면(멀티플레이: 스위처를 지우고 내 도형만 조작) 상호작용 컨트롤러가 고른 내 도형. 둘 다 없으면 null.</summary>
        public static Transform ControlledTransform()
        {
            Transform switched = PlayerControlSwitcher.ActiveTarget;
            if (switched != null) return switched;
            PlayerMover mine = InteractionController.Controlled;
            return mine != null ? mine.transform : null;
        }

        public Transform ActivePlayer
        {
            get
            {
                Transform switched = ControlledTransform();
                if (switched != null) return switched;
                PlayerMover fallback = FindFirstObjectByType<PlayerMover>();
                return fallback != null ? fallback.transform : null;
            }
        }

        private Vector3 closedDoorLocalPosition;
        private bool doorPositionCaptured;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;
        private GUIStyle accentStyle;
        private Texture2D panelPixel;
        private bool showReviewHelp;
        private KitchenMapHandoff mapHandoff;
        private RespawnController fallRecovery;

        // [멀티] 열쇠·도착은 팀 공유 상태로 서버가 확정한다(채널 0=열쇠, 1=도착). 참가자는 E를 이벤트 0(arg=내 몸 ID)으로 보고한다.
        private uint mpId;

        private void Awake()
        {
            mapHandoff = FindFirstObjectByType<KitchenMapHandoff>();
            CaptureDoorPosition();
            ApplyDoorState();
            mpId = MpSync.Register(this);
        }

        private void Start()
        {
            if (MpSync.Active || !HasFallCheckpoints) return;
            RespawnController.SetCheckpoint(fallCheckpoints[0]);
            fallRecovery = FindFirstObjectByType<RespawnController>();
            if (fallRecovery != null) fallRecovery.PlayerRespawned += RearmPursuersAfterRespawn;
        }

        private void RearmPursuersAfterRespawn(GameObject player)
        {
            Transform actor = ActivePlayer;
            if (actor == null || actor.gameObject != player) return;
            foreach (var pursuer in FindObjectsByType<KitchenPursuit>(FindObjectsSortMode.None))
                pursuer.ResetPursuit();
        }

        private bool HasFallCheckpoints => fallCheckpoints != null && fallCheckpoints.Length > 0;

        public void SetFallCheckpoints(RespawnZone[] checkpoints) => fallCheckpoints = checkpoints;

        public void OnMpValue(int channel, float value)
        {
            if (channel == 1) { ExitReached = value > 0.5f; return; }
            if (channel != 0) return;
            HasKey = value > 0.5f;
            if (key != null) key.gameObject.SetActive(!HasKey);
            ApplyDoorState();
        }

        public void OnMpEvent(int kind, Vector3 data, uint arg)
        {
            if (kind != 0 || !MpSync.IsServer || HasKey || key == null || !MpSync.SenderOwnsBody(arg)) return;
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(arg, out var body) || body == null) return;
            // 복사본 위치는 전송 지연만큼 뒤처진다 — 그만큼만 여유를 둔다
            if (Vector3.Distance(body.transform.position, key.position) <= interactionRadius + 1.5f) GrantKey();
        }

        private void GrantKey()
        {
            HasKey = true;
            key.gameObject.SetActive(false);
            ApplyDoorState();
            MpSync.Publish(mpId, 0, 1f);
        }

        /// <summary>[멀티] R: 내 도형만 지금 구역의 시작점으로 돌려보내고 크기를 기본값으로 되돌린다. 팀 상태(열쇠·문)는 유지한다.</summary>
        public void RecoverOwnShape()
        {
            Transform actor = ControlledTransform();
            if (actor == null || chapterStarts == null || CurrentChapter >= chapterStarts.Length) return;
            Transform start = chapterStarts[CurrentChapter];
            PlayerMover mover = actor.GetComponent<PlayerMover>();
            if (start == null || mover == null || mover.ExternallyDriven) return;
            if (actor.TryGetComponent(out PlayerShapeController shape)) shape.ResetToDefault();
            MoveActor(actor, start.position);
            PlayerFollowCamera.SnapToTarget(actor);
        }

        private void Update()
        {
            Transform actor = ActivePlayer;
            if (actor != null)
            {
                CurrentChapter = FindChapter(actor.position);
                TrackSinkVisit(actor);
                if (Input.GetKeyDown(KeyCode.E) && !(MpSync.Active && InputGate.Blocked)) TryCollectKey(actor);
            }

            if (MpSync.Active)
            {
                // 검토용 구역 이동·전체 초기화는 팀 상태를 PC마다 어긋나게 하므로 멀티에서는 쓰지 않는다
                if (Input.GetKeyDown(KeyCode.R) && !InputGate.Blocked) RecoverOwnShape();
                if (Input.GetKeyDown(KeyCode.F1)) showReviewHelp = !showReviewHelp;
                return;
            }

            // 싱글 테스트 전용: 1–7 = 해당 구역 시작점으로 이동, F1 = 나머지 도형을 조작 중인 도형 곁으로 모은다, F2 = 안내.
            // R은 여기서 처리하지 않는다 — RespawnController가 마지막 체크포인트로 복귀시킨다(예전엔 ResetDraft가 CH1로 되감았다).
            if (InputGate.Blocked) return;
            for (int index = 0; index < 7; index++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + index)))
                {
                    PreviewChapter(index);
                    break;
                }
            }
            if (Input.GetKeyDown(KeyCode.F1)) GatherOthersToActive();
            if (Input.GetKeyDown(KeyCode.F2)) showReviewHelp = !showReviewHelp;
        }

        /// <summary>Records an active shape reaching the actual lower sink floor.</summary>
        public bool TrackSinkVisit(Transform actor)
        {
            if (HasVisitedSink || sinkInteriorVolume == null || actor == null) return false;
            PlayerMover mover = actor.GetComponentInParent<PlayerMover>();
            if (mover == null || !mover.isActiveAndEnabled) return false;
            Transform selected = ControlledTransform();
            if (selected != null && selected != mover.transform) return false;
            if (mover.transform.position.y >= 4.1f ||
                !sinkInteriorVolume.bounds.Contains(mover.transform.position)) return false;

            HasVisitedSink = true;
            return true;
        }

        public void PreviewChapter(int zeroBasedIndex)
        {
            if (chapterStarts == null || zeroBasedIndex < 0 || zeroBasedIndex >= chapterStarts.Length) return;
            Transform start = chapterStarts[zeroBasedIndex];
            Transform actor = ActivePlayer;
            if (start == null || actor == null) return;

            MoveActor(actor, start.position);
            CurrentChapter = zeroBasedIndex;
            PlayerFollowCamera.SnapToTarget(actor);
            if (!MpSync.Active && HasFallCheckpoints && zeroBasedIndex < fallCheckpoints.Length)
                RespawnController.RestartCheckpoint(fallCheckpoints[zeroBasedIndex], fallCheckpoints);
        }

        /// <summary>[싱글 테스트 전용] 조작 중이 아닌 도형들을 조작 중인 도형 좌우로 옮긴다. 멀티에서는 아무것도 안 한다.</summary>
        public void GatherOthersToActive()
        {
            if (MpSync.Active) return;
            Transform actor = ControlledTransform();
            if (actor == null) return;
            // 겹쳐 놓으면 물리가 튕겨내므로 카메라 기준 좌우로 1.3씩 벌린다(구는 자유회전이라 actor.right는 믿을 수 없다).
            Vector3 side = Camera.main != null ? Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized : Vector3.right;
            if (side == Vector3.zero) side = Vector3.right;
            int slot = 0;
            foreach (PlayerMover mover in FindObjectsByType<PlayerMover>(FindObjectsSortMode.None))
            {
                if (mover.transform == actor || mover.ExternallyDriven) continue;
                float offset = 1.3f * (slot / 2 + 1) * (slot % 2 == 0 ? 1f : -1f);
                MoveActor(mover.transform, actor.position + side * offset + Vector3.up * 0.3f);
                slot++;
            }
        }

        public void ResetDraft()
        {
            HasKey = false;
            ExitReached = false;
            HasVisitedSink = false;
            if (key != null) key.gameObject.SetActive(true);
            ApplyDoorState();
            foreach (var pursuer in FindObjectsByType<KitchenPursuit>(FindObjectsSortMode.None))
                pursuer.ResetPursuit();
            // Only the selected shape moves. Other shapes retain their positions.
            PreviewChapter(0);
        }

        public bool TryCollectKey(Transform actor)
        {
            if (HasKey || key == null || actor == null) return false;
            PlayerMover mover = actor.GetComponentInParent<PlayerMover>();
            if (mover == null || !mover.isActiveAndEnabled) return false;
            if (Vector3.Distance(mover.transform.position, key.position) > interactionRadius) return false;

            if (MpSync.IsClient)
            {
                var no = mover.GetComponent<Unity.Netcode.NetworkObject>();
                if (no != null) MpSync.SendEvent(mpId, 0, Vector3.zero, (uint)no.NetworkObjectId);
                return true;
            }
            GrantKey();
            return true;
        }

        public void NotifyExit(Transform actor)
        {
            if (!HasKey || ExitReached || actor == null) return;
            PlayerMover mover = actor.GetComponentInParent<PlayerMover>();
            if (mover == null || (!MpSync.Active && !mover.isActiveAndEnabled)) return; // 멀티의 남의 몸 복사본은 mover가 꺼져 있다
            ExitReached = true;
            MpSync.Publish(mpId, 1, 1f);
            // In the playable scene KitchenMapHandoff begins the scene load before recording arrival.
        }

        private void CaptureDoorPosition()
        {
            if (doorVisual == null || doorPositionCaptured) return;
            closedDoorLocalPosition = doorVisual.localPosition;
            doorPositionCaptured = true;
        }

        private void ApplyDoorState()
        {
            CaptureDoorPosition();
            if (doorBlocker != null) doorBlocker.enabled = !HasKey;
            if (doorVisual != null && doorPositionCaptured)
                doorVisual.localPosition = closedDoorLocalPosition + (HasKey ? doorOpenOffset : Vector3.zero);
        }

        private int FindChapter(Vector3 playerPosition)
        {
            // The food dispatch shaft continues CH7 beyond the cooking chapter volume.
            if (HasKey && mapHandoff != null && mapHandoff.ContainsDispatchRoute(playerPosition))
                return 6;

            int index = 0;
            float nearestInside = float.PositiveInfinity;
            bool insideAny = false;
            if (chapters != null)
            {
                for (int i = 0; i < chapters.Length; i++)
                {
                    DraftChapterRegion region = chapters[i];
                    if (region == null || region.volume == null || !region.volume.bounds.Contains(playerPosition)) continue;
                    float distance = region.start != null
                        ? (playerPosition - region.start.position).sqrMagnitude : 0f;
                    if (!insideAny || distance < nearestInside)
                    {
                        insideAny = true;
                        nearestInside = distance;
                        index = region.index;
                    }
                }
            }
            if (insideAny) return Mathf.Clamp(index, 0, 6);

            float nearest = float.PositiveInfinity;
            if (chapters != null)
            {
                for (int i = 0; i < chapters.Length; i++)
                {
                    DraftChapterRegion region = chapters[i];
                    if (region == null || region.start == null) continue;
                    float distance = (playerPosition - region.start.position).sqrMagnitude;
                    if (distance < nearest)
                    {
                        nearest = distance;
                        index = region.index;
                    }
                }
            }
            if (float.IsPositiveInfinity(nearest) && chapterStarts != null)
            {
                for (int i = 0; i < chapterStarts.Length; i++)
                {
                    if (chapterStarts[i] == null) continue;
                    float distance = (playerPosition - chapterStarts[i].position).sqrMagnitude;
                    if (distance < nearest)
                    {
                        nearest = distance;
                        index = i;
                    }
                }
            }
            return Mathf.Clamp(index, 0, 6);
        }

        internal static void MoveActor(Transform actor, Vector3 destination)
        {
            Rigidbody body = actor.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = destination;
            }
            actor.position = destination;
            if (actor.TryGetComponent(out Unity.Netcode.Components.NetworkTransform nt) && nt.IsSpawned && nt.CanCommitToTransform)
                nt.Teleport(destination, actor.rotation, actor.localScale); // 다른 PC에서 미끄러져 보이지 않게
            Physics.SyncTransforms();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            EnsureHud();

            DraftChapterRegion currentRegion = null;
            if (chapters != null)
                for (int i = 0; i < chapters.Length; i++)
                    if (chapters[i] != null && chapters[i].index == CurrentChapter)
                    { currentRegion = chapters[i]; break; }
            string chapterName = currentRegion != null && !string.IsNullOrEmpty(currentRegion.chapterName)
                ? currentRegion.chapterName
                : chapterNames != null && CurrentChapter < chapterNames.Length
                    ? chapterNames[CurrentChapter] : "주방";
            string status = ExitReached ? "음식 배출구 아래에 도착했습니다"
                : CurrentChapter == 6 && HasKey ? "상단 출구가 열렸습니다"
                : currentRegion != null && !string.IsNullOrEmpty(currentRegion.objective)
                    ? currentRegion.objective : "벨트를 따라 다음 구역으로 이동하세요";

            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                Fill(new Rect(20, 20, 580, 124), new Color(.055f, .09f, .11f, .88f));
                Fill(new Rect(20, 20, 5, 124), new Color(.49f, .84f, .78f, 1f));
                GUI.Label(new Rect(42, 30, 300, 25), $"THE AXIOM   /   CH {CurrentChapter + 1:00} — 07", smallStyle);
                GUI.Label(new Rect(42, 56, 530, 36), chapterName, titleStyle);
                GUI.Label(new Rect(42, 98, 530, 38), status, labelStyle);

                Fill(new Rect(1066, 20, 194, 46), HasKey
                    ? new Color(.52f, .34f, .11f, .95f) : new Color(.055f, .09f, .11f, .88f));
                GUI.Label(new Rect(1080, 30, 168, 28), HasKey ? "열쇠  획득" : "열쇠  미획득", accentStyle);

                Fill(new Rect(20, 674, 790, 29), new Color(.055f, .09f, .11f, .88f));
                GUI.Label(new Rect(32, 678, 766, 23),
                    MpSync.Active ? "WASD 이동   마우스 시점   Space 점프   Tab 도형   E 상호작용   F1 안내"
                                  : "WASD 이동   마우스 시점   Space 점프   Tab 도형   E 상호작용   F1 도형 모으기   F2 안내", smallStyle);

                if (showReviewHelp)
                {
                    Fill(new Rect(870, 78, 390, 185), new Color(.055f, .09f, .11f, .94f));
                    Fill(new Rect(870, 78, 390, 4), new Color(.49f, .84f, .78f, 1f));
                    GUI.Label(new Rect(890, 93, 350, 33), "플레이 안내", titleStyle);
                    GUI.Label(new Rect(890, 133, 350, 30), "1–7 구역 이동  F1 모으기  R 체크포인트", labelStyle);
                    GUI.Label(new Rect(890, 165, 350, 30), MpSync.Active ? "Esc  커서 해제     F1  안내 닫기" : "Esc  커서 해제     F2  안내 닫기", labelStyle);
                    GUI.Label(new Rect(890, 209, 350, 30),
                        HasVisitedSink ? "싱크대 내부 통과 확인" : "싱크대 내부를 지나가세요", smallStyle);
                }
            }
            finally { GUI.matrix = previousMatrix; }
        }

        private void EnsureHud()
        {
            if (titleStyle != null) return;
            panelPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            panelPixel.SetPixel(0, 0, Color.white);
            panelPixel.Apply();
            panelPixel.hideFlags = HideFlags.HideAndDontSave;

            Font koreanFont = null;
            string[] installedFonts = Font.GetOSInstalledFontNames();
            for (int i = 0; i < installedFonts.Length; i++)
            {
                if (!installedFonts[i].Contains("Malgun Gothic") &&
                    !installedFonts[i].Contains("맑은 고딕")) continue;
                koreanFont = Font.CreateDynamicFontFromOSFont(installedFonts[i], 20);
                break;
            }

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Color.white;
            titleStyle.font = koreanFont;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            labelStyle.normal.textColor = new Color(.85f, .91f, .90f);
            labelStyle.font = koreanFont;
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            smallStyle.normal.textColor = new Color(.71f, .80f, .79f);
            smallStyle.font = koreanFont;
            accentStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter };
            accentStyle.normal.textColor = new Color(1f, .87f, .60f);
            accentStyle.font = koreanFont;
        }

        private void Fill(Rect rect, Color tint)
        {
            Color previousColor = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, panelPixel, ScaleMode.StretchToFill);
            GUI.color = previousColor;
        }

        private void OnDestroy()
        {
            if (fallRecovery != null) fallRecovery.PlayerRespawned -= RearmPursuersAfterRespawn;
            MpSync.Unregister(mpId, this);
            if (panelPixel != null) Destroy(panelPixel);
        }
    }
}
