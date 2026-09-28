#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only physics/integration checks; never saved into the scene or included in a player build.
/// Trigger tests reposition real players, then let PhysX dispatch contacts. They do not simulate keyboard traversal.
/// </summary>
public sealed class ToyWorldPlayModeSmokeProbe : MonoBehaviour
{
    private int runtimeErrors;
    private PlayerMover sphere;
    private readonly WaitForFixedUpdate tick = new WaitForFixedUpdate();

    private void OnEnable() => Application.logMessageReceived += OnLog;
    private void OnDisable() => Application.logMessageReceived -= OnLog;

    private IEnumerator Start()
    {
        yield return null;
        yield return tick;
        IEnumerator checks = RunChecks();
        while (true)
        {
            object current;
            bool more;
            try
            {
                more = checks.MoveNext();
                current = more ? checks.Current : null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("[ToyWorldPlaySmoke] RUNTIME_SMOKE_FAIL");
                EditorApplication.Exit(1);
                yield break;
            }
            if (!more) break;
            yield return current;
        }
        if (runtimeErrors > 0)
        {
            Debug.LogError("[ToyWorldPlaySmoke] Runtime errors: " + runtimeErrors);
            EditorApplication.Exit(1);
            yield break;
        }
        Debug.Log("[ToyWorldPlaySmoke] RUNTIME_SMOKE_PASS");
        EditorApplication.Exit(0);
    }

    private IEnumerator RunChecks()
    {
        if (SessionState.GetBool("ToyWorld.ArtCapture", false))
        {
            SessionState.SetBool("ToyWorld.ArtCapture", false);
            yield return new WaitForSeconds(0.6f);
            string preview = "Assets/Map3ToyWorld/Validation/ArtPreviews/08_PlayCamera.png";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(preview));
            Camera camera = Camera.main;
            RenderTexture target = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D frame = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            frame.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            frame.Apply();
            System.IO.File.WriteAllBytes(preview, frame.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            Destroy(frame); Destroy(target);
            Debug.Log("[ToyWorldArt] PLAY_CAMERA_CAPTURE_PASS (camera only, IMGUI excluded): " + preview);
        }
        foreach (PlayerMover player in FindObjectsOfType<PlayerMover>())
            if (player.name == "Player_Sphere") sphere = player;
        Require(sphere != null, "Existing sphere player missing.");
        Require(PlayerControlSwitcher.ActiveTarget == sphere.transform && sphere.IsControlled,
            "Starting camera player and controlled player do not match.");
        Place(sphere, new Vector3(-1.5f, 0.1f, -50f));
        for (int i = 0; i < 4; i++) yield return tick;
        PlayerShapeController shapeController = sphere.GetComponent<PlayerShapeController>();
        PlayerJump manualJump = sphere.GetComponent<PlayerJump>();
        Rigidbody manualBody = sphere.GetComponent<Rigidbody>();
        Require(shapeController != null && shapeController.IsGrounded(), "Starting sphere is not grounded.");
        Require(manualJump != null && manualJump.enabled, "Starting sphere jump component is disabled.");
        FieldInfo queue = typeof(PlayerJump).GetField("jumpQueued", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(queue != null, "PlayerJump input queue field is missing.");
        queue.SetValue(manualJump, true);
        yield return tick;
        Require(manualBody.velocity.y > 1f, "Grounded manual jump input was not consumed by PlayerJump.");
        Debug.Log("[ToyWorldPlaySmoke] PASS deterministic Sphere control, grounded state, and manual jump path.");

        foreach (PlayerMover player in FindObjectsOfType<PlayerMover>())
            player.SetControlled(false);
        Place(sphere, new Vector3(5f, 0.1f, -5f));
        ToyWorldLevelDirector director = FindObjectOfType<ToyWorldLevelDirector>();
        Require(director != null && director.finalGate != null, "Director/door missing.");
        Rigidbody gateBody = director.finalGate.GetComponent<Rigidbody>();
        float lockedY = gateBody.position.y;
        Require(!director.TryCompleteLevel(), "Empty-inventory exit accepted.");

        ToyWorldRepairItemType[] order = { ToyWorldRepairItemType.PowerGear,
            ToyWorldRepairItemType.MelodyCylinder, ToyWorldRepairItemType.WindUpSpring };
        for (int i = 0; i < order.Length; i++)
        {
            ToyWorldRepairItem item = Array.Find(FindObjectsOfType<ToyWorldRepairItem>(), p => p.itemType == order[i]);
            Place(sphere, item.transform.position);
            yield return new WaitForSeconds(0.4f);
            Require(director.CollectedCount == i + 1, "Real pickup trigger failed: " + order[i]);
            Require(!item.GetComponent<Collider>().enabled && !item.visualRoot.activeSelf,
                "Pickup did not hide visual/disable trigger.");
            if (i < 2)
                Require(Mathf.Abs(gateBody.position.y - lockedY) < 0.01f, "Door physically opened before 3/3.");
        }
        yield return new WaitForSeconds(0.5f);
        Require(gateBody.position.y > lockedY + 1f, "DoorSystem gate did not physically rise at 3/3.");
        Require(!director.TryCollectItem(order[2]) && director.CollectedCount == 3, "Duplicate pickup changed count.");
        Debug.Log("[ToyWorldPlaySmoke] PASS real pickup triggers, 2/3 locked and 3/3 physical door motion.");

        ToyWorldInstallSocket[] sockets = FindObjectsOfType<ToyWorldInstallSocket>();
        ToyWorldInstallSocket gear = Array.Find(sockets, s => s.itemType == ToyWorldRepairItemType.PowerGear);
        Place(sphere, gear.transform.position);
        yield return new WaitForSeconds(0.2f);
        Require(director.InstalledCount == 0, "Out-of-order install trigger accepted.");
        for (int i = 0; i < 3; i++)
        {
            ToyWorldInstallSocket socket = Array.Find(sockets, s => (int)s.itemType == i);
            Place(sphere, socket.transform.position);
            yield return new WaitForSeconds(0.25f);
            Require(director.InstalledCount == i + 1, "Install trigger failed at " + i);
        }
        Place(sphere, new Vector3(4f, 0.1f, 39f));
        yield return new WaitForSeconds(0.4f);
        Require(!director.IsMusicBoxActivated && !director.CanUseExit,
            "Closed (-45 degree) lever falsely activated after installation.");
        Require(!director.TryCompleteLevel(), "Exit accepted before lever activation.");

        LeverHead lever = director.musicBox.activationLever;
        // Physically push the existing lever with a real player body.
        Rigidbody sphereBody = sphere.GetComponent<Rigidbody>();
        sphere.ExternallyDriven = true;
        for (int i = 0; i < 180 && !director.IsMusicBoxActivated; i++)
        {
            Vector3 approach = lever.transform.position - lever.leverPivot.forward * 0.8f;
            if (i == 0) Place(sphere, new Vector3(approach.x, 0.15f, approach.z));
            Vector3 direction = lever.transform.position - sphereBody.position;
            direction.y = 0f;
            sphereBody.velocity = direction.normalized * 3f + Vector3.up * sphereBody.velocity.y;
            yield return tick;
        }
        sphere.ExternallyDriven = false;
        Require(director.IsMusicBoxActivated && director.CanUseExit,
            "Physical push did not activate existing lever. Angle=" + lever.GetCurrentAngle() + ", player=" + sphereBody.position);
        Debug.Log("[ToyWorldPlaySmoke] PASS ordered install triggers, negative-angle regression and physical lever push.");

        foreach (LiftPad pad in FindObjectsOfType<LiftPad>())
        {
            LiftPlatform lift = pad.targetLift;
            Rigidbody liftBody = lift.GetComponent<Rigidbody>();
            float restY = liftBody.position.y;
            Place(sphere, pad.transform.position + Vector3.up * 0.05f);
            yield return new WaitForSeconds(0.8f);
            Require(liftBody.position.y > restY + 0.4f, "Existing LiftPad did not raise " + lift.name);
            Place(sphere, new Vector3(5f, 0.1f, -5f));
            yield return new WaitForSeconds(1.1f);
            Require(Mathf.Abs(liftBody.position.y - restY) < 0.1f, "Existing lift did not return: " + lift.name);
        }
        Debug.Log("[ToyWorldPlaySmoke] PASS both existing LiftPad hold/release paths.");

        Transform trainYard = GameObject.Find("Branch_TrainYard").transform;
        Require(trainYard.GetComponentsInChildren<JumpPad>().Length == 0,
            "Removed Train Yard JumpPads are still present.");
        RailCart cart = trainYard.GetComponentInChildren<RailCart>();
        Require(cart != null && cart.axle != null && cart.path != null && cart.IsOnRail,
            "Train Yard RailCart is not fully wired on its rail.");
        Rigidbody cartBody = cart.GetComponent<Rigidbody>();
        RailCartRider cartRider = cart.GetComponent<RailCartRider>();
        MethodInfo boardMethod = typeof(RailCartRider).GetMethod("Board", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo unboardMethod = typeof(RailCartRider).GetMethod("Unboard", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(cartRider != null && boardMethod != null && unboardMethod != null,
            "RailCart rider test hooks are missing.");
        boardMethod.Invoke(cartRider, new object[] { sphere, sphereBody });
        Require(sphereBody.isKinematic && sphere.transform.parent == cart.transform,
            "Player shape did not board the RailCart.");

        PlayerMover cube = Array.Find(FindObjectsOfType<PlayerMover>(), p => p.name == "Player_Cube");
        Rigidbody cubeBody = cube.GetComponent<Rigidbody>();
        Vector3 cartStart = cartBody.position;
        Vector3 forwardPush = -cart.axle.crank.forward;
        cube.ExternallyDriven = true;
        Place(cube, cart.axle.crank.position + cart.axle.crank.right * 0.9f - forwardPush * 1.2f);
        cubeBody.velocity = forwardPush * 3f;
        for (int i = 0; i < 60 && cart.axle.CurrentCharge == 0f; i++) yield return tick;
        cube.ExternallyDriven = false;
        Require(cart.axle.CurrentCharge > 0f,
            "Forward crank push did not produce positive RailCart charge.");
        Require(cart.axle.crank.GetComponent<Collider>().isTrigger,
            "Train Yard crank must remain a pass-through trigger.");
        yield return new WaitForSeconds(0.25f);
        Require(Vector3.Dot(cart.axle.crank.right, forwardPush) > 0.7f,
            "Crank end rotated opposite to the player's forward push.");
        Place(cube, new Vector3(30f, 0.1f, 20f));
        yield return new WaitForSeconds(1f);
        Require(Vector3.Distance(cartBody.position, cartStart) < 0.35f,
            "RailCart moved before its release delay elapsed.");
        yield return new WaitForSeconds(cart.releaseDelay + 1f);
        float cartTravel = Vector3.Distance(cartBody.position, cartStart);
        Require(cartTravel > 0.75f && cart.IsOnRail,
            "Released RailCart did not travel safely along its curved rail. travel=" + cartTravel +
            ", onRail=" + cart.IsOnRail + ", position=" + cartBody.position + ", velocity=" + cart.Velocity +
            ", charge=" + cart.axle.CurrentCharge + ", output=" + cart.axle.OutputPower);
        float riderSeatError = Vector3.Distance(sphere.transform.position, cartRider.seat.position);
        Require(riderSeatError < 0.1f,
            "Boarded player moved away from the RailCart seat. error=" + riderSeatError +
            ", rider=" + sphere.transform.position + ", seat=" + cartRider.seat.position);

        float forwardDistance = Vector3.Distance(cartBody.position, cartStart);
        Transform crank = cart.axle.crank;
        Vector3 reversePush = crank.forward;
        cube.ExternallyDriven = true;
        Place(cube, crank.position + crank.right * 0.9f - reversePush * 1.2f);
        cubeBody.velocity = reversePush * 3f;
        for (int i = 0; i < 60 && cart.axle.CurrentCharge >= 0f; i++) yield return tick;
        cube.ExternallyDriven = false;
        Require(cart.axle.CurrentCharge < 0f,
            "Opposite crank push did not reverse the signed charge.");
        yield return new WaitForSeconds(0.25f);
        Require(Vector3.Dot(crank.right, reversePush) > 0.7f,
            "Crank end rotated opposite to the player's reverse push.");
        Place(cube, new Vector3(30f, 0.1f, 20f));
        yield return new WaitForSeconds(cart.releaseDelay + 2f);
        float reverseDistance = Vector3.Distance(cartBody.position, cartStart);
        Require(reverseDistance < forwardDistance - 0.1f && cart.IsOnRail,
            "Negative charge did not drive the RailCart back toward its start. before=" +
            forwardDistance + ", after=" + reverseDistance);

        unboardMethod.Invoke(cartRider, null);
        Vector3 trackPoint = cart.path.Evaluate(2, 0.5f);
        Place(cube, trackPoint + Vector3.up * 1.5f);
        yield return new WaitForSeconds(0.8f);
        Require(cubeBody.position.y < trackPoint.y - 0.5f,
            "Player shape stood on the RailCart-only track instead of falling through it.");
        Place(cube, new Vector3(30f, 0.1f, 20f));
        Debug.Log("[ToyWorldPlaySmoke] PASS bidirectional crank/cart travel, boarded rider sync, and player-only track pass-through.");

        Portal[] portals = FindObjectsOfType<Portal>();
        Portal enter = Array.Find(portals, p => p.action == Portal.PortalAction.Enable);
        Portal leave = Array.Find(portals, p => p.action == Portal.PortalAction.Disable);
        Place(cube, new Vector3(enter.transform.position.x, 0.1f, enter.transform.position.z));
        yield return new WaitForSeconds(0.2f);
        PlayerRollModeReceiver roll = cube.GetComponent<PlayerRollModeReceiver>();
        Require(roll != null && roll.RollModeActive, "Existing Portal Enable trigger failed.");
        Place(cube, new Vector3(leave.transform.position.x, 0.1f, leave.transform.position.z));
        yield return new WaitForSeconds(0.2f);
        Require(!roll.RollModeActive, "Existing Portal Disable trigger failed.");
        Debug.Log("[ToyWorldPlaySmoke] PASS existing PortalSystem trigger pair.");

        SnapBlock[] blocks = FindObjectsOfType<SnapBlock>();
        SnapBlock a = blocks[0], b = blocks[1];
        PuzzleResettable resetA = a.GetComponent<PuzzleResettable>();
        Vector3 resetPosition = a.Body.position;
        resetA.CaptureResetState();
        a.transform.SetPositionAndRotation(new Vector3(70f, 5f, 70f), Quaternion.identity);
        float separation = (a.GetComponent<BoxCollider>().size.x + b.GetComponent<BoxCollider>().size.x) * 0.5f;
        b.transform.SetPositionAndRotation(a.transform.position + Vector3.right * separation, Quaternion.identity);
        List<SnapBlock.Face> facesA = new List<SnapBlock.Face>(), facesB = new List<SnapBlock.Face>();
        a.GetFaces(facesA);
        b.GetFaces(facesB);
        Require(a.Weld(b, facesA[0], facesB[1]) && a.HasConnectionTo(b), "Existing SnapBlock weld failed.");
        resetA.ResetPuzzleObject();
        yield return new WaitForSeconds(0.1f);
        Require(!a.HasConnections && !b.HasConnections, "Reset left a live block connection.");
        Require(Vector3.Distance(a.Body.position, resetPosition) < 0.2f,
            "Reset did not restore block position. Expected=" + resetPosition + ", actual=" + a.Body.position);
        Require(director.CollectedCount == 3 && director.InstalledCount == 3, "Reset cleared progression.");
        Debug.Log("[ToyWorldPlaySmoke] PASS SnapBlock weld and safe reset detachment.");

        // Every intentionally placed launch bypass must produce a real collision-driven launch
        // for each existing shape. This checks the pad wiring, not keyboard steering/landing skill.
        JumpPad[] jumpPads = FindObjectsOfType<JumpPad>();
        PlayerMover[] shapes = FindObjectsOfType<PlayerMover>();
        foreach (PlayerMover shape in shapes)
        {
            foreach (JumpPad pad in jumpPads)
            {
                Collider padCollider = pad.GetComponent<Collider>();
                Vector3 top = padCollider.bounds.center;
                top.y = padCollider.bounds.max.y + 0.6f;
                Place(shape, top);
                bool launched = false;
                for (int i = 0; i < 90; i++)
                {
                    yield return tick;
                    if (shape.GetComponent<Rigidbody>().velocity.y > 3f)
                    {
                        launched = true;
                        break;
                    }
                }
                Require(launched, "Bypass pad did not launch " + shape.name + " at " + pad.transform.position);
                Place(shape, new Vector3(5f, 0.1f, -5f));
                yield return tick;
            }
        }
        Debug.Log("[ToyWorldPlaySmoke] PASS all " + jumpPads.Length + " bypass JumpPads with all three shapes.");

        StickerSurface surface = GameObject.Find("GEO_BlockFort_SlipRamp_Bypass").GetComponent<StickerSurface>();
        PhysicMaterial originalMaterial = surface.ResolvedCollider.sharedMaterial;
        FrictionSticker sticker = FrictionSticker.Attach(surface, StickerKind.Slip,
            new FrictionStickerSettings(), surface.transform.position, surface.transform.up);
        Require(sticker != null && surface.ResolvedCollider.sharedMaterial.dynamicFriction <= 0.1f,
            "Existing Slip sticker did not change ramp friction.");
        sticker.Retract();
        sticker = FrictionSticker.Attach(surface, StickerKind.Velcro,
            new FrictionStickerSettings(), surface.transform.position, surface.transform.up);
        Require(surface.ResolvedCollider.sharedMaterial.dynamicFriction >= 0.9f,
            "Existing Velcro sticker did not change ramp friction.");
        sticker.Retract();
        yield return null;
        Require(surface.Current == null && surface.ResolvedCollider.sharedMaterial == originalMaterial,
            "Sticker retraction did not restore original ramp material.");
        Debug.Log("[ToyWorldPlaySmoke] PASS existing ramp sticker swap/retraction.");

        RespawnController respawn = FindObjectOfType<RespawnController>();
        RespawnZone cp = GameObject.Find("CP_FinalRoom").GetComponent<RespawnZone>();
        // A fresh checkpoint permits checking the existing anti-rewind checkpoint contract.
        Place(sphere, cp.transform.position - Vector3.up);
        yield return new WaitForSeconds(0.2f);
        int respawnsBefore = respawn.RespawnCount;
        respawn.RespawnPlayer(sphere.gameObject);
        yield return new WaitForSeconds(2f);
        Require(respawn.RespawnCount == respawnsBefore + 1 &&
                Vector3.Distance(sphere.GetComponent<Rigidbody>().position, cp.FindGroundPoint()) < 1.5f,
            "Existing checkpoint/respawn did not restore player to final room.");
        Require(director.CollectedCount == 3 && director.IsMusicBoxActivated, "Respawn lost map progress.");
        Debug.Log("[ToyWorldPlaySmoke] PASS existing checkpoint respawn and progress persistence.");

        // Actual final trigger, after all physical/persistence checks.
        ToyWorldExitTrigger exit = FindObjectOfType<ToyWorldExitTrigger>();
        Place(sphere, exit.transform.position);
        yield return new WaitForSeconds(0.3f);
        Require(director.IsLevelCompleted, "Real final exit trigger failed.");
        Debug.Log("[ToyWorldPlaySmoke] PASS real final exit trigger.");
    }

    private static void Place(PlayerMover player, Vector3 position)
    {
        Rigidbody body = player.GetComponent<Rigidbody>();
        if (!body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.position = position;
        Physics.SyncTransforms();
    }

    private void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Assert || type == LogType.Error)
            runtimeErrors++;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
