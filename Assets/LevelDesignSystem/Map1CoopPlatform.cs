using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class Map1CoopPlatform : MonoBehaviour
{
    public Map1CoopSwitch call;
    public Map1CoopSwitch depart;
    public Map1CoopSwitch[] requires = Array.Empty<Map1CoopSwitch>();
    public PlayerShapeStats.ShapeKind[] passengers = Array.Empty<PlayerShapeStats.ShapeKind>();
    public Vector3 pickup;
    public Vector3 destination;
    public float speed = 2f;

    private enum Journey { Parked, ToPickup, Boarding, ToDestination, Arrived }
    private Journey journey;
    private Rigidbody body;
    private BoxCollider deck;
    private bool hasDelivered;
    private readonly Collider[] contacts = new Collider[32];
    private readonly HashSet<Rigidbody> riders = new HashSet<Rigidbody>();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        deck = GetComponent<BoxCollider>();
        MpBodySync.Ensure(this);
        journey = call == null ? Journey.Boarding : Journey.Parked;
    }

    private void FixedUpdate()
    {
        // 참가자 PC: 자세와 탑승자 이동은 MpBodySync가 맡는다(LiftPlatform과 같음). 여기서도 옮기면 2배로 밀린다.
        if (MpSync.IsClient) return;
        int boarding = FindRiders();

        if (journey == Journey.Parked && call.IsSet) journey = Journey.ToPickup;
        int required = PassengerMask();
        int present = boarding & required;
        bool ready = hasDelivered ? present != 0 : present == required;
        if (journey == Journey.Boarding && (depart == null || depart.IsSet) &&
            Map1CoopSwitch.AllSet(requires) && ready)
            journey = Journey.ToDestination;
        if (journey == Journey.Arrived && riders.Count == 0 && PassengerNearPickup())
            journey = Journey.ToPickup;

        if (journey != Journey.ToPickup && journey != Journey.ToDestination) return;
        Vector3 target = journey == Journey.ToPickup ? pickup : destination;
        Vector3 next = Vector3.MoveTowards(body.position, target, speed * Time.fixedDeltaTime);
        Carry(next - body.position);
        body.MovePosition(next);
        if (next == target)
        {
            journey = journey == Journey.ToPickup ? Journey.Boarding : Journey.Arrived;
            hasDelivered |= journey == Journey.Arrived;
        }
    }

    private int FindRiders()
    {
        riders.Clear();
        Bounds bounds = deck.bounds;
        var center = new Vector3(bounds.center.x, bounds.max.y + 0.12f, bounds.center.z);
        var extents = new Vector3(bounds.extents.x, 0.24f, bounds.extents.z);
        int count = Physics.OverlapBoxNonAlloc(center, extents, contacts, Quaternion.identity,
            ~0, QueryTriggerInteraction.Ignore);
        int boarding = 0;
        for (int i = 0; i < count; i++)
        {
            Collider hit = contacts[i];
            var shape = hit.GetComponentInParent<PlayerShapeIdentity>();
            if (shape == null || hit.attachedRigidbody == null ||
                !bounds.Contains(new Vector3(hit.bounds.center.x, bounds.center.y, hit.bounds.center.z)) ||
                Mathf.Abs(hit.bounds.min.y - bounds.max.y) > 0.32f)
                continue;
            riders.Add(hit.attachedRigidbody);
            boarding |= 1 << (int)shape.Kind;
        }
        return boarding;
    }

    private int PassengerMask()
    {
        int mask = 0;
        foreach (var kind in passengers) mask |= 1 << (int)kind;
        return mask;
    }

    private bool PassengerNearPickup()
    {
        Vector3 radius = deck.bounds.extents + new Vector3(2.5f, 2.5f, 2.5f);
        int count = Physics.OverlapBoxNonAlloc(pickup, radius, contacts, Quaternion.identity,
            ~0, QueryTriggerInteraction.Ignore);
        int mask = PassengerMask();
        for (int i = 0; i < count; i++)
        {
            var shape = contacts[i].GetComponentInParent<PlayerShapeIdentity>();
            if (shape != null && (mask & (1 << (int)shape.Kind)) != 0) return true;
        }
        return false;
    }

    private void Carry(Vector3 delta)
    {
        if (delta == Vector3.zero) return;
        foreach (Rigidbody rider in riders)
            if (!rider.isKinematic && MpSync.IsOwnBody(rider)) rider.position += PlayerGroundContact.CarryDelta(rider, delta);
    }
}
