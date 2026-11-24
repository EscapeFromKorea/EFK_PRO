using System.Collections.Generic;
using UnityEngine;

/// <summary>Two alternating sweepers. The server schedules strokes and senses
/// the exit handle. Each body owner resolves contact with the moving blade.</summary>
public sealed class KitchenSortingPushers : MonoBehaviour, IMpSynced, IMpEventSink, IInteractionProvider
{
    public Transform[] blades;
    public Transform[] sweepEnds;
    public Transform[] trayLandings;
    public Light[] warningLights;
    public BoxCollider helpHandle;
    public Transform handleGripPivot;

    private readonly Renderer[] lenses = new Renderer[2];
    private Quaternion restingGrip;

    public const float Interval = 3.6f;
    public const float Warning = 1f;
    public const float Stroke = .8f;
    public const float Delay = 2f;
    private const float Return = .65f;

    public int NextRow => SweepCount % 2;
    public int SweepCount { get; private set; }
    public int HitCount { get; private set; }
    public int DelayCount { get; private set; }
    public bool NextSweepDelayed { get; private set; }
    public float SecondsUntilNextSweep => Mathf.Max(0f, nextAt - MpSync.SharedFixedTime);

    private readonly Vector3[] home = new Vector3[2];
    private readonly BoxCollider[] bladeBoxes = new BoxCollider[2];
    private readonly float[] started = { float.NegativeInfinity, float.NegativeInfinity };
    private readonly float[] previousFraction = { 1f, 1f };
    private readonly HashSet<PlayerMover>[] struck = { new HashSet<PlayerMover>(), new HashSet<PlayerMover>() };
    private readonly Dictionary<PlayerMover, Collider[]> colliders = new Dictionary<PlayerMover, Collider[]>();
    private PlayerMover[] solo;
    private float refreshAt, nominalNextAt, nextAt;
    private bool ready;
    private uint mpId;

    private void Awake() => mpId = MpSync.Register(this);
    private void OnDestroy() => MpSync.Unregister(mpId, this);

    private void OnEnable() => InteractionController.Register(this);
    private void OnDisable() => InteractionController.Unregister(this);

    private bool InReach(PlayerMover player)
    {
        return ready && PlayerBounds(player, out Bounds bounds) && helpHandle.bounds.Intersects(bounds);
    }

    public void CollectActions(List<InteractionAction> into)
    {
        var player = InteractionController.Controlled;
        if (!player || !MpSync.IsOwnBody(player) || !InReach(player)) return;
        into.Add(new InteractionAction {
            channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
            priority = InteractionPriority.Panel, verb = "분류기 지연 손잡이 당기기",
            enabled = !NextSweepDelayed, reason = "다음 밀대가 이미 지연되었습니다.",
            distance = Vector3.Distance(helpHandle.bounds.center, player.transform.position),
            execute = PullControlled
        });
    }

    private void PullControlled() => TryPullHandle(InteractionController.Controlled);

    public bool TryPullHandle(PlayerMover player)
    {
        if (!player || !player.IsControlled || InteractionController.IsGripped(player) ||
            !MpSync.IsOwnBody(player) || !InReach(player) || NextSweepDelayed ||
            MpSync.SharedFixedTime >= nextAt) return false;
        if (MpSync.IsClient)
        {
            // Identify the intended next stroke so a late packet cannot delay its successor.
            MpSync.SendEvent(mpId, 1, new Vector3(SweepCount, 0f, 0f), MpSync.BodyId(player));
            return true;
        }
        return ApplyDelay();
    }

    public void OnMpEvent(int kind, Vector3 data, uint bodyId)
    {
        if (!MpSync.IsServer || kind != 1 || data.x != SweepCount || !MpSync.SenderOwnsBody(bodyId)) return;
        var player = MpSync.MoverById(bodyId);
        if (InReach(player)) ApplyDelay();
    }

    private bool ApplyDelay()
    {
        if (NextSweepDelayed || MpSync.SharedFixedTime >= nextAt) return false;
        nextAt = nominalNextAt + Delay;
        NextSweepDelayed = true;
        DelayCount++;
        return true;
    }

    private void Start()
    {
        if (blades == null || blades.Length != 2 || sweepEnds == null || sweepEnds.Length != 2 ||
            trayLandings == null || trayLandings.Length != 2 || warningLights == null || warningLights.Length != 2 || !helpHandle) return;
        for (int row = 0; row < 2; row++)
        {
            if (!blades[row] || !blades[row].TryGetComponent(out bladeBoxes[row]) ||
                !sweepEnds[row] || !trayLandings[row] || !warningLights[row]) return;
            home[row] = blades[row].position;
            lenses[row] = warningLights[row].GetComponent<Renderer>();
            if (lenses[row])
            {
                var appearance = new MaterialPropertyBlock();
                lenses[row].GetPropertyBlock(appearance);
                Color yellow = new Color(1f, .78f, .035f);
                appearance.SetColor("_Color", yellow);
                appearance.SetColor("_BaseColor", yellow);
                appearance.SetColor("_EmissionColor", yellow * 2.5f);
                lenses[row].SetPropertyBlock(appearance);
            }
            ShowWarning(row, false);
        }
        if (!MpSync.IsClient) nextAt = nominalNextAt = MpSync.SharedFixedTime + Interval;
        if (handleGripPivot) restingGrip = handleGripPivot.localRotation;
        ready = true;
    }

    private IReadOnlyList<PlayerMover> Players()
    {
        if (MpSync.Active) return MpSync.Bodies;
        if (solo == null || Time.unscaledTime >= refreshAt)
        {
            solo = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
            refreshAt = Time.unscaledTime + 1f;
        }
        return solo;
    }

    private bool PlayerBounds(PlayerMover mover, out Bounds bounds)
    {
        bounds = default;
        if (!mover || !mover.gameObject.activeInHierarchy) return false;
        if (!colliders.TryGetValue(mover, out Collider[] parts))
            colliders[mover] = parts = mover.GetComponentsInChildren<Collider>(true);
        bool found = false;
        foreach (Collider part in parts)
        {
            if (!part || !part.enabled || part.isTrigger || !part.gameObject.activeInHierarchy ||
                part.attachedRigidbody == null || part.attachedRigidbody.gameObject != mover.gameObject) continue;
            if (found) bounds.Encapsulate(part.bounds);
            else bounds = part.bounds;
            found = true;
        }
        return found;
    }

    private void FixedUpdate()
    {
        if (!ready) return;
        float now = MpSync.SharedFixedTime;
        var players = Players();
        if (!MpSync.IsClient)
        {
            if (now >= nextAt)
            {
                int row = NextRow;
                Begin(row, nextAt);
                MpSync.Publish(mpId, row, nextAt);
                SweepCount++;
                NextSweepDelayed = false;
                nominalNextAt += Interval;
                nextAt = nominalNextAt;
            }
            MpSync.Publish(mpId, 2, nextAt);
            MpSync.Publish(mpId, 3, SweepCount);
            MpSync.Publish(mpId, 4, NextSweepDelayed ? 1f : 0f);
            MpSync.Publish(mpId, 5, DelayCount);
        }
        for (int row = 0; row < 2; row++)
        {
            ShowWarning(row, row == NextRow && nextAt > now && nextAt - now <= Warning);
            AnimateRow(row, now, players);
        }
    }

    private void ShowWarning(int row, bool lit)
    {
        warningLights[row].enabled = lit;
        if (lenses[row]) lenses[row].enabled = lit;
    }

    private void Update()
    {
        if (!ready || !handleGripPivot) return;
        Quaternion target = NextSweepDelayed ? restingGrip * Quaternion.Euler(25f, 0f, 0f) : restingGrip;
        handleGripPivot.localRotation = Quaternion.RotateTowards(handleGripPivot.localRotation, target, Time.deltaTime * 180f);
    }

    private void Begin(int row, float time)
    {
        if (time <= started[row]) return;
        started[row] = time;
        previousFraction[row] = 0f;
        struck[row].Clear();
    }

    public void OnMpValue(int channel, float value)
    {
        switch (channel)
        {
            case 0: case 1: Begin(channel, value); break;
            case 2: nextAt = value; break;
            case 3: SweepCount = Mathf.RoundToInt(value); break;
            case 4: NextSweepDelayed = value > .5f; break;
            case 5: DelayCount = Mathf.RoundToInt(value); break;
        }
    }

    private void AnimateRow(int row, float now, IReadOnlyList<PlayerMover> players)
    {
        float age = now - started[row];
        float progress = Mathf.Clamp01(age / Stroke);
        Vector3 delta = sweepEnds[row].position - home[row];
        Transform blade = blades[row];
        BoxCollider box = bladeBoxes[row];
        // Layout keeps blades axis aligned. Sweep between fixed samples so thin
        // blades still catch fast bodies and clients receiving a late stroke.
        if (age >= 0 && age < Stroke + Return && previousFraction[row] < 1f)
        {
            Vector3 oldPos = home[row] + delta * previousFraction[row];
            Vector3 newPos = home[row] + delta * progress;
            Vector3 centerOffset = blade.TransformVector(box.center);
            Bounds swept = new Bounds(oldPos + centerOffset, Vector3.Scale(box.size, blade.lossyScale));
            swept.Encapsulate(new Bounds(newPos + centerOffset, swept.size));
            foreach (var p in players)
            {
                if (!p || !MpSync.IsOwnBody(p) || struck[row].Contains(p) ||
                    !PlayerBounds(p, out Bounds body) || !swept.Intersects(body)) continue;
                struck[row].Add(p);
                Vector3 landing = trayLandings[row].position;
                landing.x += Mathf.Clamp(body.center.x - home[row].x, -1.3f, 1.3f);
                Vector3 feet = new Vector3(body.center.x, body.min.y, body.center.z);
                Vector3 target = p.transform.position + landing + Vector3.up * .035f - feet;
                if (MpSync.Active) MpSync.TeleportOwnBody(p.transform, target);
                else
                {
                    var rb = p.GetComponent<Rigidbody>();
                    if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                    rb.position = target;
                    p.transform.position = target;
                }
                HitCount++;
            }
        }
        previousFraction[row] = progress;
        float pose = age <= Stroke ? progress : 1f - Mathf.Clamp01((age - Stroke) / Return);
        blade.position = home[row] + delta * pose;
    }
}
