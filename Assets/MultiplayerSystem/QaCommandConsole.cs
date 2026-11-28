#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Shape = PlayerShapeStats.ShapeKind;

/// <summary>Ephemeral QA commands. The host is the only writer of shared QA state.</summary>
[DefaultExecutionOrder(-500)]
internal sealed class QaCommandConsole : MonoBehaviour, IMpSynced, IMpPosed
{
    // The console lives in DontDestroyOnLoad; root ordering there is different on each peer.
    private const uint SyncId = 0x0AFC071;
    private const string Help = "help | gather | save | clear\nspeed <0.1–50> [도형] | weight <0.05–50> [도형]\n도형: all, sphere/s/구, cube/c/정육면체, tetrahedron/tetra/t/정사면체";
    private const string FieldName = "QaCommandInput";
    private static QaCommandConsole instance;

    private readonly float[] speeds = new float[3];
    private readonly float[] masses = new float[3];
    private readonly List<PlayerMover> roster = new();
    private readonly Dictionary<PlayerMover, EditedStats> edited = new();
    private readonly List<PlayerMover> destroyedBodies = new();
    private readonly Dictionary<PlayerMover, bool> inputLocks = new();
    private bool open;
    private bool focusInput;
    private string input = "";
    private string message = "";
    private CursorLockMode previousLock;
    private bool previousCursorVisible;
    private PlayerFollowCamera orbitCamera;
    private bool previousOrbit;
    private Scene map;
    private MpLobby lobby;
    private int round;
    private GameObject marker;
    private Material markerMaterial;
    private Vector3? temporaryCheckpoint;
    private float nextRosterScan;

    private sealed class EditedStats
    {
        public PlayerShapeIdentity Identity;
        public PlayerShapeStats Original;
        public PlayerShapeStats Clone;
        public Rigidbody Body;
        public float OriginalSpeed;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("QaCommandConsole");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<QaCommandConsole>();
    }

    private void OnDestroy()
    {
        ResetSession();
        MpSync.Unregister(SyncId, this);
        if (markerMaterial != null) Destroy(markerMaterial);
        if (instance == this) instance = null;
    }

    private void Update()
    {
        RefreshSession();
        if (IsCleared())
        {
            Close();
            return;
        }
        bool toggle = Input.GetKeyDown(KeyCode.BackQuote);
        if (open)
        {
            if (toggle || Input.GetKeyDown(KeyCode.Escape)) Close();
            return;
        }
        if (toggle && HasPlayableScene() && !InputGate.Blocked && InputGate.PanelOpenCount == 0)
            Open();
    }

    private void LateUpdate()
    {
        // Slot overrides are kept through respawn/rejoin. New bodies run their usual Start first.
        if (!HasPlayableScene() || !HasOverrides()) return;
        if (Time.unscaledTime < nextRosterScan) return;
        nextRosterScan = Time.unscaledTime + .15f;
        RefreshRoster();
        PruneDestroyedStats();
        foreach (PlayerMover player in roster) ApplyStats(player);
    }

    private bool HasOverrides()
    {
        for (int i = 0; i < speeds.Length; i++)
            if (speeds[i] > 0f || masses[i] > 0f) return true;
        return false;
    }

    private void RefreshSession()
    {
        Scene active = SceneManager.GetActiveScene();
        MpLobby currentLobby = MpLobby.I;
        int currentRound = currentLobby != null ? currentLobby.Round.Value : 0;
        if (active == map && ReferenceEquals(currentLobby, lobby) && currentRound == round) return;

        ResetSession();
        map = active;
        lobby = currentLobby;
        round = currentRound;
        MpSync.Register(this, SyncId);
    }

    private void ResetSession()
    {
        Close();
        SetCheckpoint(null);
        Array.Clear(speeds, 0, speeds.Length);
        Array.Clear(masses, 0, masses.Length);
        foreach (var pair in edited)
        {
            PlayerMover player = pair.Key;
            EditedStats state = pair.Value;
            if (player != null)
            {
                player.moveSpeed = state.OriginalSpeed;
                if (state.Identity != null && state.Identity.stats == state.Clone)
                {
                    if (state.Body != null)
                        state.Body.mass *= state.Original.mass / Mathf.Max(.001f, state.Clone.mass);
                    state.Identity.stats = state.Original;
                }
            }
            if (state.Clone != null) Destroy(state.Clone);
        }
        edited.Clear();
        roster.Clear();
        nextRosterScan = 0f;
        input = "";
        message = "";
    }

    private bool HasPlayableScene()
    {
        if (!map.IsValid() || !map.isLoaded || map.name == MpNet.HomeScene) return false;
        if (lobby == null) return true;
        int index = lobby.Map.Value;
        return round > 0 && index >= 0 && index < MpLobby.MapScenes.Length && map.path == MpLobby.MapScenes[index];
    }

    private bool IsCleared() => (lobby != null && lobby.Cleared.Value) ||
        (LevelManager.Instance != null && LevelManager.Instance.IsCleared);

    internal static void CloseForStageClear() => instance?.Close();

    private void Open()
    {
        // After a multiplayer scene loads, its owned body can arrive a few frames later.
        if (AnchorPlayer() == null) return;

        open = true;
        input = "";
        focusInput = true;
        InputGate.PanelOpened();
        InputGate.Blocked = true;
        previousLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        orbitCamera = FindFirstObjectByType<PlayerFollowCamera>();
        if (orbitCamera != null)
        {
            previousOrbit = orbitCamera.enableMouseOrbit;
            orbitCamera.enableMouseOrbit = false;
        }
        foreach (PlayerMover player in roster)
        {
            if (MpSync.Active && !MpSync.IsOwnBody(player)) continue;
            inputLocks[player] = player.InputLocked;
            player.InputLocked = true;
        }
    }

    private void Close()
    {
        if (!open) return;
        open = false;
        InputGate.PanelClosed();
        InputGate.Blocked = false;
        foreach (var pair in inputLocks)
            if (pair.Key != null) pair.Key.InputLocked = pair.Value;
        inputLocks.Clear();
        if (orbitCamera != null) orbitCamera.enableMouseOrbit = previousOrbit;
        orbitCamera = null;
        Cursor.lockState = previousLock;
        Cursor.visible = previousCursorVisible;
    }

    private void OnGUI()
    {
        if (!open) return;
        const float margin = 12f;
        const float height = 155f;
        float width = Mathf.Min(590f, Screen.width - 2f * margin);
        GUILayout.BeginArea(new Rect(margin, Screen.height - height - margin, width, height), GUI.skin.box);
        GUILayout.Label("QA Console  [` / Esc]");
        GUILayout.Label(Help);
        bool isClient = MpSync.IsClient;
        if (isClient) GUILayout.Label("호스트만 명령을 실행할 수 있습니다.");
        else if (!string.IsNullOrEmpty(message) && message != Help) GUILayout.Label(message);
        GUI.enabled = !isClient;
        GUILayout.BeginHorizontal();
        Event evt = Event.current;
        bool enter = evt.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == FieldName &&
            (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter);
        if (enter || (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.BackQuote)) evt.Use();
        GUI.SetNextControlName(FieldName);
        input = GUILayout.TextField(input, 150);
        bool submit = GUILayout.Button("실행", GUILayout.Width(65f));
        GUILayout.EndHorizontal();
        if (focusInput && Event.current.type == EventType.Repaint)
        {
            GUI.FocusControl(FieldName);
            focusInput = false;
        }
        if ((submit || enter) && !isClient)
        {
            message = ExecuteCommand(input);
            input = "";
            focusInput = true;
        }
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    internal string ExecuteCommand(string command)
    {
        RefreshSession();
        string[] parts = (command ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || (parts.Length == 1 && parts[0].Equals("help", StringComparison.OrdinalIgnoreCase)))
            return Help;
        if (!HasPlayableScene() || IsCleared()) return "플레이 중인 맵에서 사용해 주세요.";
        if (MpSync.IsClient || (lobby != null && !MpSync.IsServer)) return "호스트만 실행할 수 있습니다.";

        switch (parts[0].ToLowerInvariant())
        {
            case "gather" when parts.Length == 1: return Gather();
            case "save" when parts.Length == 1: return SaveCheckpoint();
            case "clear" when parts.Length == 1:
                SetCheckpoint(null);
                MpSync.PublishPose(SyncId, Vector3.zero, Quaternion.AngleAxis(180f, Vector3.right));
                return "임시 체크포인트를 지웠습니다. 기존 체크포인트를 사용합니다.";
            case "speed": return SetStat(parts, isSpeed: true);
            case "weight": return SetStat(parts, isSpeed: false);
            default: return "명령 또는 인자가 올바르지 않습니다. help를 입력해 주세요.";
        }
    }

    private void RefreshRoster()
    {
        roster.Clear();
        foreach (PlayerMover player in FindObjectsByType<PlayerMover>(FindObjectsSortMode.None))
        {
            if (!player.TryGetComponent(out PlayerShapeIdentity identity) || identity.stats == null) continue;
            if (!player.TryGetComponent<Rigidbody>(out _)) continue;
            if (MpSync.Active && (!player.TryGetComponent(out NetworkObject no) || !no.IsSpawned)) continue;
            roster.Add(player);
        }
        roster.Sort((a, b) => a.GetComponent<PlayerShapeIdentity>().Kind.CompareTo(b.GetComponent<PlayerShapeIdentity>().Kind));
    }

    private PlayerMover AnchorPlayer()
    {
        RefreshRoster();
        Transform target = MpSync.Active ? NetProtoRunner.LocalPlayer : PlayerControlSwitcher.ActiveTarget;
        foreach (PlayerMover player in roster)
            if (player.transform == target) return player;
        foreach (PlayerMover player in roster)
            if (MpSync.Active ? MpSync.IsOwnBody(player) : player.IsControlled) return player;
        return null;
    }

    private string SaveCheckpoint()
    {
        PlayerMover player = AnchorPlayer();
        if (player == null || FindFirstObjectByType<RespawnController>() == null)
            return "플레이어 또는 리스폰 시스템이 아직 준비되지 않았습니다.";
        if (!TryFloor(PlayerFootprintCenter(player), out Vector3 floor))
            return "플레이어 근처에 임시 체크포인트를 놓을 바닥이 없습니다.";
        SetCheckpoint(floor);
        MpSync.PublishPose(SyncId, floor, Quaternion.identity);
        return $"노란 임시 체크포인트를 만들었습니다. {floor:F1}";
    }

    private void SetCheckpoint(Vector3? point)
    {
        temporaryCheckpoint = point;
        if (!point.HasValue)
        {
            if (marker != null) { marker.SetActive(false); Destroy(marker); }
            marker = null;
            return;
        }
        if (marker == null)
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "QA Temporary Checkpoint";
            var collider = marker.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            if (markerMaterial == null)
            {
                markerMaterial = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                markerMaterial.color = new Color(1f, .9f, .06f);
            }
            marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
            marker.transform.localScale = Vector3.one * .65f;
        }
        marker.transform.position = point.Value + Vector3.up * .4f;
    }

    internal static bool TryGetCheckpoint(out Vector3 point)
    {
        point = default;
        if (instance == null) return false;
        instance.RefreshSession();
        if (!instance.HasPlayableScene() || !instance.temporaryCheckpoint.HasValue) return false;
        point = instance.temporaryCheckpoint.Value;
        return true;
    }

    public void OnMpPose(Vector3 position, Quaternion rotation)
    {
        if (MpSync.IsClient && HasPlayableScene())
            SetCheckpoint(Mathf.Abs(rotation.w) > .5f ? position : null);
    }

    private string Gather()
    {
        PlayerMover anchor = AnchorPlayer();
        if (anchor == null) return "기준으로 사용할 플레이어가 없습니다.";
        roster.Remove(anchor);
        roster.Insert(0, anchor);
        var reserved = new List<Bounds>();
        var positions = new List<Vector3>();
        Vector3 anchorCenter = PlayerFootprintCenter(anchor);
        foreach (PlayerMover player in roster)
        {
            if (!TryGatherPosition(player, anchorCenter, reserved, out Vector3 position))
                return "팀 전체가 설 수 있는 넓은 바닥에서 다시 실행해 주세요.";
            positions.Add(position);
        }

        int dispatched = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            PlayerMover player = roster[i];
            if (MpSync.IsServer && player.TryGetComponent(out NetworkObject no) && !no.IsOwner)
            {
                lobby.QaGatherRpc(no.NetworkObjectId, positions[i], round,
                    lobby.NetworkManager.RpcTarget.Single(no.OwnerClientId, RpcTargetUse.Temp));
                dispatched++;
            }
            else if (TeleportPlayer(player, positions[i])) dispatched++;
        }
        return dispatched == roster.Count ? $"도형 {dispatched}개의 이동을 요청했습니다." :
            $"{dispatched}/{roster.Count}개를 이동했습니다. 기믹에 고정된 도형은 해제 후 다시 실행해 주세요.";
    }

    internal static bool TeleportPlayer(PlayerMover player, Vector3 position)
    {
        if (player == null || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
            return false;
        if (MpSync.Active && !MpSync.IsOwnBody(player)) return false;
        if (!player.TryGetComponent<Rigidbody>(out _) || !RespawnController.TryReleaseForQaTeleport(player))
        {
            Debug.LogWarning("[QA] 고정된 도형을 이동할 수 없습니다.", player);
            return false;
        }
        if (player.TryGetComponent(out PlayerAccelReceiver boost)) boost.CancelBoost();
        if (player.TryGetComponent(out PlayerGravityOverride gravity)) gravity.RestoreDefault(0f);
        MpSync.TeleportOwnBody(player.transform, position);
        return true;
    }

    private static bool TryFloor(Vector3 near, out Vector3 floor)
    {
        floor = default;
        float closest = float.MaxValue;
        var hits = Physics.RaycastAll(near + Vector3.up * 1.5f, Vector3.down, 4.5f, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= closest || hit.normal.y < .65f || hit.point.y > near.y + .2f ||
                hit.collider.GetComponentInParent<PlayerMover>() != null ||
                OutOfBoundsVolume.AnyContains(hit.point + Vector3.up * .1f)) continue;
            closest = hit.distance;
            floor = hit.point;
        }
        return closest < float.MaxValue;
    }

    private static Vector3 PlayerFootprintCenter(PlayerMover player)
    {
        var collider = player.GetComponent<PlayerShapeIdentity>().solidCollider;
        return collider != null && collider.enabled ? collider.bounds.center : player.transform.position;
    }

    private static bool TryGatherPosition(PlayerMover player, Vector3 anchor, List<Bounds> reserved, out Vector3 position)
    {
        position = default;
        var identity = player.GetComponent<PlayerShapeIdentity>();
        if (identity.solidCollider == null || !identity.solidCollider.enabled) return false;
        Bounds body = identity.solidCollider.bounds;
        Vector3 offset = body.center - player.transform.position;
        for (int i = 0; i <= 32; i++)
        {
            float radians = i * Mathf.PI / 4f;
            float radius = i == 0 ? 0f : ((i - 1) / 8 + 1) * 1.25f;
            Vector3 near = anchor + new Vector3(Mathf.Cos(radians) * radius, 0f, Mathf.Sin(radians) * radius);
            if (!TryFloor(near, out Vector3 floor)) continue;
            Vector3 center = floor + Vector3.up * (body.extents.y + .12f);
            var proposed = new Bounds(center, body.size + Vector3.one * .08f);
            if (reserved.Exists(other => other.Intersects(proposed))) continue;
            bool blocked = false;
            foreach (Collider hit in Physics.OverlapBox(proposed.center, proposed.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                if (hit.GetComponentInParent<PlayerMover>() != player) { blocked = true; break; }
            if (blocked) continue;
            position = center - offset;
            reserved.Add(proposed);
            return true;
        }
        return false;
    }

    private string SetStat(string[] parts, bool isSpeed)
    {
        float minimum = isSpeed ? .1f : .05f;
        if (parts.Length < 2 || parts.Length > 3 ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
            !float.IsFinite(value) || value < minimum || value > 50f)
            return isSpeed ? "speed <0.1~50> [도형]" : "weight <0.05~50> [도형]";
        int slot = parts.Length == 2 ? -1 : ShapeSlot(parts[2]);
        if (slot == -2) return "도형을 찾지 못했습니다. sphere/s, cube/c, tetrahedron/tetra/t, all";
        RefreshRoster();
        int count = 0;
        foreach (var player in roster)
            if (slot < 0 || (int)player.GetComponent<PlayerShapeIdentity>().Kind == slot) count++;
        if (count == 0) return "바꿀 도형이 맵에 없습니다.";

        float[] values = isSpeed ? speeds : masses;
        for (int i = 0; i < values.Length; i++)
        {
            if (slot >= 0 && slot != i) continue;
            values[i] = value;
            MpSync.Publish(SyncId, isSpeed ? i : i + speeds.Length, value);
        }
        foreach (PlayerMover player in roster) ApplyStats(player);
        return $"{(isSpeed ? "이동 속도를" : "기본 질량을")} {value.ToString(CultureInfo.InvariantCulture)}로 설정했습니다. 대상 {count}개.";
    }

    private static int ShapeSlot(string name) => name.ToLowerInvariant() switch
    {
        "all" => -1,
        "sphere" or "s" or "구" => (int)Shape.Sphere,
        "cube" or "c" or "정육면체" => (int)Shape.Cube,
        "tetrahedron" or "tetra" or "t" or "정사면체" => (int)Shape.Tetrahedron,
        _ => -2
    };

    public void OnMpValue(int channel, float value)
    {
        if (!MpSync.IsClient || !HasPlayableScene() || !float.IsFinite(value) || value > 50f) return;
        if (channel >= 0 && channel < speeds.Length && value >= .1f) speeds[channel] = value;
        else if (channel >= speeds.Length && channel < 2 * speeds.Length && value >= .05f)
            masses[channel - speeds.Length] = value;
        nextRosterScan = 0f;
    }

    private void ApplyStats(PlayerMover player)
    {
        var identity = player.GetComponent<PlayerShapeIdentity>();
        int idx = (int)identity.Kind;
        if (speeds[idx] <= 0f && masses[idx] <= 0f) return;
        if (!edited.TryGetValue(player, out EditedStats state))
        {
            state = new EditedStats
            {
                Identity = identity,
                Original = identity.stats,
                Clone = Instantiate(identity.stats),
                Body = player.GetComponent<Rigidbody>(),
                OriginalSpeed = player.moveSpeed
            };
            identity.stats = state.Clone;
            edited.Add(player, state);
        }
        if (speeds[idx] > 0f)
        {
            state.Clone.moveSpeed = speeds[idx];
            player.moveSpeed = speeds[idx];
        }
        if (masses[idx] > 0f && !Mathf.Approximately(state.Clone.mass, masses[idx]))
        {
            // Scaling computes from stats.mass. Keep the existing size multiplier on the current physics body.
            state.Body.mass *= masses[idx] / Mathf.Max(.001f, state.Clone.mass);
            state.Clone.mass = masses[idx];
        }
    }

    private void PruneDestroyedStats()
    {
        destroyedBodies.Clear();
        foreach (var pair in edited)
            if (pair.Key == null)
            {
                if (pair.Value.Clone != null) Destroy(pair.Value.Clone);
                destroyedBodies.Add(pair.Key);
            }
        foreach (PlayerMover player in destroyedBodies) edited.Remove(player);
    }
}
#endif
