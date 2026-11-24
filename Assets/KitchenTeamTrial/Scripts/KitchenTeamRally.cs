using System;
using UnityEngine;

/// <summary>
/// Lets a solo player bring the two idle shapes to a cooperative station.
/// Multiplayer players keep control of their own bodies and are never moved here.
/// </summary>
public sealed class KitchenTeamRally : MonoBehaviour
{
    [SerializeField] private Transform[] arrivalSlots;
    [SerializeField] private float interactionRadius = 3.2f;
    [SerializeField] private float repeatDelay = 0.8f;

    private float nextUseTime;

    public void Configure(Transform[] slots)
    {
        arrivalSlots = slots;
    }

    private void Update()
    {
        if (MpSync.Active || InputGate.Blocked || Time.time < nextUseTime)
            return;

        Transform active = PlayerControlSwitcher.ActiveTarget;
        if (active == null ||
            (active.position - transform.position).sqrMagnitude > interactionRadius * interactionRadius ||
            !Input.GetKeyDown(KeyCode.E))
            return;

        GatherIdleShapes(active);
        nextUseTime = Time.time + repeatDelay;
    }

    public int GatherIdleShapes(Transform active)
    {
        if (MpSync.Active || active == null || arrivalSlots == null || arrivalSlots.Length < 3)
            return 0;

        PlayerMover[] players = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
        Array.Sort(players, (left, right) => string.CompareOrdinal(left.name, right.name));

        int moved = 0;
        foreach (PlayerMover player in players)
        {
            if (player == null || !player.isActiveAndEnabled || player.transform == active)
                continue;

            Vector3 position = player.transform.position;
            bool alreadyNearby = (position - transform.position).sqrMagnitude <= 8f * 8f;
            bool alreadyBeyond = position.x > transform.position.x + 4f;
            if (alreadyNearby || alreadyBeyond)
                continue;

            Transform slot = FirstOpenSlot(active, players);
            if (slot == null)
                break;

            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = slot.position;
            }
            player.transform.position = slot.position;
            moved++;
        }

        if (moved > 0)
            Physics.SyncTransforms();
        return moved;
    }

    private Transform FirstOpenSlot(Transform active, PlayerMover[] players)
    {
        foreach (Transform slot in arrivalSlots)
        {
            if (slot == null || (slot.position - active.position).sqrMagnitude < 2f * 2f)
                continue;

            bool occupied = false;
            foreach (PlayerMover player in players)
            {
                if (player != null && player.transform != active &&
                    (player.transform.position - slot.position).sqrMagnitude < 1.8f * 1.8f)
                {
                    occupied = true;
                    break;
                }
            }
            if (!occupied)
                return slot;
        }
        return null;
    }

    private void OnGUI()
    {
        if (MpSync.Active || InputGate.Blocked)
            return;

        Transform active = PlayerControlSwitcher.ActiveTarget;
        if (active == null ||
            (active.position - transform.position).sqrMagnitude > interactionRadius * interactionRadius)
            return;

        GUI.Box(new Rect(Screen.width * 0.5f - 95f, Screen.height - 115f, 190f, 30f),
            "E  도형 집결");
    }
}
