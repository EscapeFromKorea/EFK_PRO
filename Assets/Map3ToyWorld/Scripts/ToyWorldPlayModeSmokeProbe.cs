#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Command-line validation only. It is never placed in the saved scene and is excluded from player builds.
/// The file lives outside Editor so Unity can attach it while the Editor is in Play Mode.
/// </summary>
public sealed class ToyWorldPlayModeSmokeProbe : MonoBehaviour
{
    private int runtimeErrors;

    private void OnEnable() => Application.logMessageReceived += OnLog;
    private void OnDisable() => Application.logMessageReceived -= OnLog;

    private IEnumerator Start()
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        try
        {
            RunSmokeChecks();
            Require(runtimeErrors == 0, "Runtime logged an exception/assert/error during smoke checks.");
            Debug.Log("[ToyWorldPlaySmoke] RUNTIME_SMOKE_PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("[ToyWorldPlaySmoke] RUNTIME_SMOKE_FAIL");
            EditorApplication.Exit(1);
        }
    }

    private void RunSmokeChecks()
    {
        ToyWorldLevelDirector director = FindObjectOfType<ToyWorldLevelDirector>();
        Require(director != null, "Director is missing at runtime.");
        Require(director.finalGate != null && !director.finalGate.IsOpen, "Final Gate must begin locked.");

        Require(director.TryCollectItem(ToyWorldRepairItemType.PowerGear), "PowerGear collection failed.");
        Require(director.TryCollectItem(ToyWorldRepairItemType.MelodyCylinder), "MelodyCylinder collection failed.");
        Require(!director.finalGate.IsOpen, "Final Gate opened at 2/3.");
        Require(director.TryCollectItem(ToyWorldRepairItemType.WindUpSpring), "WindUpSpring collection failed.");
        Require(director.finalGate.IsOpen, "Final Gate did not open at 3/3.");
        Require(!director.TryCollectItem(ToyWorldRepairItemType.WindUpSpring) && director.CollectedCount == 3,
            "Duplicate item collection was not ignored.");

        Require(!director.TryInstallItem(ToyWorldRepairItemType.PowerGear), "Out-of-order installation was accepted.");
        Require(director.TryInstallItem(ToyWorldRepairItemType.WindUpSpring), "Spring installation failed.");
        Require(director.TryInstallItem(ToyWorldRepairItemType.PowerGear), "Gear installation failed.");
        Require(director.TryInstallItem(ToyWorldRepairItemType.MelodyCylinder), "Cylinder installation failed.");
        Require(!director.CanUseExit, "Exit became valid before winding the Music Box.");

        director.musicBox.ApplyWindUpPower(3f, 1f);
        Require(director.IsMusicBoxActivated && director.CanUseExit, "Wind-up activation did not validate the exit.");
        Require(director.TryCompleteLevel() && director.IsLevelCompleted, "Final completion failed.");

        TorqueStateController torque = FindObjectOfType<TorqueStateController>();
        Require(torque != null, "Torque state adapter is missing.");
        torque.SetTorqueEnabled(true);
        Require(torque.IsTorqueEnabled, "Torque Enable failed.");
        torque.SetTorqueEnabled(false);
        Require(!torque.IsTorqueEnabled, "Torque Disable failed.");

        ToyRailSwitch railSwitch = FindObjectOfType<ToyRailSwitch>();
        Require(railSwitch != null && !railSwitch.IsAligned, "Rail branch switch must begin diverted.");
        railSwitch.SetAligned(true);
        Require(railSwitch.IsAligned, "Rail branch switch pad action failed.");

        ToyRailCart cart = FindObjectOfType<ToyRailCart>();
        PuzzleResettable cartReset = cart != null ? cart.GetComponent<PuzzleResettable>() : null;
        Require(cart != null && cartReset != null, "Rail cart reset wiring is missing.");
        Rigidbody cartBody = cart.GetComponent<Rigidbody>();
        cartReset.CaptureResetState();
        Vector3 cartOrigin = cartBody.position;
        cartBody.position += new Vector3(0f, -20f, 4f);
        cart.Derail();
        cartReset.ResetPuzzleObject();
        Require(Vector3.Distance(cartBody.position, cartOrigin) < 0.05f && !cart.IsDerailed,
            "Rail cart did not restore its rail checkpoint state.");

        SnapBlock[] blocks = FindObjectsOfType<SnapBlock>();
        Require(blocks.Length >= 2, "SnapBlock smoke pair is missing.");
        SnapBlock a = blocks[0];
        SnapBlock b = blocks[1];
        BoxCollider aBox = a.GetComponent<BoxCollider>();
        BoxCollider bBox = b.GetComponent<BoxCollider>();
        Require(aBox != null && bBox != null, "SnapBlock BoxColliders are missing.");
        a.transform.SetPositionAndRotation(new Vector3(70f, 5f, 70f), Quaternion.identity);
        float separation = (aBox.size.x + bBox.size.x) * 0.5f;
        b.transform.SetPositionAndRotation(a.transform.position + Vector3.right * separation, Quaternion.identity);
        a.Body.velocity = Vector3.zero;
        b.Body.velocity = Vector3.zero;
        List<SnapBlock.Face> facesA = new List<SnapBlock.Face>();
        List<SnapBlock.Face> facesB = new List<SnapBlock.Face>();
        a.GetFaces(facesA);
        b.GetFaces(facesB);
        Require(a.Weld(b, facesA[0], facesB[1]) && a.HasConnectionTo(b), "SnapBlock weld failed.");
        a.DetachFrom(b);
        Require(!a.HasConnectionTo(b), "SnapBlock detach failed.");
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
