#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>기존 게임 카메라로 CH1 시작 시야를 관찰한다. 실제 입력/사람 완주가 아닌
/// 공개 카메라 API·물리 Linecast 진단이며, 팀 카메라나 맵 씬 설정을 수정하지 않는다.</summary>
public static class Map4PresentationChecks
{
    private const string Tag = "[Map4PresentationChecks]";
    private const int Width = 1280;
    private const int Height = 720;

    public static IEnumerator Run(SectorController[] sectors, PlayerMover[] players, Action<string, bool, string> record)
    {
        if (record == null) yield break;
        if (sectors == null || sectors.Length < 8 || sectors[0] == null || sectors[0].sectorId != 1 ||
            sectors[6] == null || sectors[7] == null ||
            players == null || players.Length != 3 || players.Any(p => p == null))
        { record("PRESENT.setup", false, "S1 SectorController와 구/네모/세모 도형 3개 필요"); yield break; }

        Camera gameCamera = Camera.main;
        PlayerFollowCamera follow = gameCamera != null ? gameCamera.GetComponent<PlayerFollowCamera>() : null;
        if (gameCamera == null || follow == null || !gameCamera.enabled || !gameCamera.gameObject.activeInHierarchy)
        { record("PRESENT.camera", false, "활성 Main Camera/PlayerFollowCamera 없음"); yield break; }
        Lab_CameraOcclusion occlusion = gameCamera.GetComponent<Lab_CameraOcclusion>();
        record("PRESENT.occlusion", occlusion != null && occlusion.enabled,
            occlusion != null ? $"Map4 가림 보정 장착 probeRadius={occlusion.probeRadius} clearance={occlusion.surfaceClearance}" : "Map4 카메라 가림 보정 없음");
        if (sectors[0].spawnSlots == null || sectors[0].spawnSlots.Length < 3 || sectors[0].spawnSlots.Take(3).Any(t => t == null))
        { record("PRESENT.spawn", false, "S1 스폰 슬롯 3개 누락"); yield break; }

        string root = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        string folder = Path.Combine(root, "검증", "N04_present_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(folder);
        StringBuilder summary = new StringBuilder();
        summary.AppendLine("기존 Main Camera 직접 렌더. 직접 입력·사람 완주 검증 아님. IMGUI OnGUI는 Camera.Render PNG에 포함되지 않을 수 있다.");
        summary.AppendLine($"camera follow offset={follow.offset} initialYaw={follow.cameraYawOffset} enabledMouseOrbit={follow.enableMouseOrbit} (Master serialized 값은 0,4,-10 / yaw 90)");
        var probe = RenderSettings.ambientProbe;
        summary.AppendLine($"LIGHTING ambientMode={RenderSettings.ambientMode} ambientLight={RenderSettings.ambientLight} " +
            $"ambientIntensity={RenderSettings.ambientIntensity:F3} probeL0RGB=({probe[0, 0]:F3},{probe[1, 0]:F3},{probe[2, 0]:F3}) " +
            $"activeColorSpace={QualitySettings.activeColorSpace}");
        Vector3 s1Spawn = sectors[0].spawnSlots[0].position;
        Light[] activeLights = UnityEngine.Object.FindObjectsOfType<Light>()
            .Where(l => l != null && l.enabled && l.gameObject.activeInHierarchy).ToArray();
        summary.AppendLine($"LIGHTING activeLightCount={activeLights.Length} distanceReference=S1 first spawn {s1Spawn}");
        foreach (Light light in activeLights.OrderBy(l => Vector3.Distance(l.transform.position, s1Spawn)))
            summary.AppendLine($"LIGHT {HitPath(light.transform)} type={light.type} intensity={light.intensity:F3} " +
                $"range={light.range:F2} distToS1Spawn={Vector3.Distance(light.transform.position, s1Spawn):F2} " +
                $"color={light.color} layer={light.gameObject.layer}");
        record("PRESENT.camera", true, $"Main Camera='{gameCamera.name}' offset={follow.offset} initialYaw={follow.cameraYawOffset}; 새 관측 카메라 생성 없음");

        Transform previousTarget = follow.target;
        bool previousOcclusionEnabled = occlusion != null && occlusion.enabled;
        Transform hookOwner = players[0].transform;
        try
        {
            for (int i = 0; i < 3; i++)
            {
                PlayerMover mover = players[i];
                Rigidbody rb = mover.GetComponent<Rigidbody>();
                if (rb == null)
                { record("PRESENT.body." + i, false, "Rigidbody 없음"); continue; }
                Transform slot = sectors[0].spawnSlots[i];
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = slot.position;
                rb.transform.position = slot.position;
                Physics.SyncTransforms();

                PlayerFollowCamera.SetActiveTarget(mover.transform);
                PlayerFollowCamera.SnapToTarget(mover.transform);
                yield return null;
                yield return null;
                Physics.SyncTransforms();

                PlayerShapeIdentity shape = mover.GetComponent<PlayerShapeIdentity>();
                string shapeName = shape != null ? shape.Kind.ToString() : mover.name;
                Vector3 center = shape != null && shape.solidCollider != null
                    ? shape.solidCollider.bounds.center : mover.transform.position;
                Vector3 viewport = gameCamera.WorldToViewportPoint(center);
                bool inFrame = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
                bool hitAnything = Physics.Linecast(gameCamera.transform.position, center, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore);
                bool hitOwnBody = hitAnything && hit.collider != null && hit.collider.transform.IsChildOf(mover.transform);
                string firstHit = !hitAnything ? "(없음)" : HitPath(hit.collider != null ? hit.collider.transform : null);
                Collider cameraInside = Physics.OverlapSphere(gameCamera.transform.position, 0.05f, ~0, QueryTriggerInteraction.Ignore)
                    .FirstOrDefault(c => c != null && !c.CompareTag("Player"));
                string terrainHit = FirstTerrainHit(gameCamera.transform.position, center);
                bool blocked = (hitAnything && !hitOwnBody) || cameraInside != null || terrainHit != "(없음)";
                Renderer[] bodyRenderers = mover.GetComponentsInChildren<Renderer>(true);
                Renderer[] enabledBodyRenderers = bodyRenderers.Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy).ToArray();
                string rendererDetail = string.Join("; ", bodyRenderers.Select(r =>
                    r == null ? "(null)" : $"{HitPath(r.transform)} enabled={r.enabled} active={r.gameObject.activeInHierarchy} layer={r.gameObject.layer} bounds={r.bounds} " +
                    "materials=[" + string.Join(",", r.sharedMaterials.Select(m => m == null ? "(null)" :
                        $"{m.name}/shader={m.shader?.name}/color={(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.clear)}")) + "]"));
                int nearPlaneWallCount = 0;
                string nearPlaneDetail = "";
                for (int gy = 0; gy < 3; gy++)
                    for (int gx = 0; gx < 3; gx++)
                    {
                        float u = 0.1f + 0.4f * gx, v = 0.1f + 0.4f * gy;
                        Vector3 sample = gameCamera.ViewportToWorldPoint(new Vector3(u, v, gameCamera.nearClipPlane));
                        Collider overlap = Physics.OverlapSphere(sample, 0.02f, ~0, QueryTriggerInteraction.Ignore)
                            .FirstOrDefault(c => c != null && c.GetComponentInParent<PlayerMover>() == null);
                        Ray ray = gameCamera.ViewportPointToRay(new Vector3(u, v, 0f));
                        RaycastHit[] sampleHits = Physics.RaycastAll(ray, Mathf.Max(10f, viewport.z), ~0, QueryTriggerInteraction.Ignore);
                        Transform firstWall = sampleHits.OrderBy(h => h.distance)
                            .Where(h => h.collider != null && h.collider.GetComponentInParent<PlayerMover>() == null)
                            .Select(h => h.collider.transform).FirstOrDefault();
                        if (overlap != null) nearPlaneWallCount++;
                        nearPlaneDetail += $" [{u:F1},{v:F1}:near={HitPath(overlap != null ? overlap.transform : null)}, ray={HitPath(firstWall)}]";
                    }
                string pose = $"shape={shapeName} slot={slot.position} cameraPos={gameCamera.transform.position} cameraRot={gameCamera.transform.rotation.eulerAngles} " +
                    $"bodyCenter={center} viewport={viewport} inFrame={inFrame} linecastFirst={firstHit} firstTerrain={terrainHit} cameraInside={HitPath(cameraInside != null ? cameraInside.transform : null)} " +
                    $"occlusionAllowed={(occlusion != null ? occlusion.AllowedDistance.ToString("F2") : "n/a")} occlusionBlocker={HitPath(occlusion != null && occlusion.LastObstruction != null ? occlusion.LastObstruction.transform : null)} " +
                    $"nearClip={gameCamera.nearClipPlane:F3} cullingMask={gameCamera.cullingMask} selectedRenderers={rendererDetail} nearPlaneWall={nearPlaneWallCount}/9:{nearPlaneDetail}";
                summary.AppendLine(pose);

                string png = Path.Combine(folder, $"S1_{i}_{shapeName}.png");
                string noBodyPng = Path.Combine(folder, $"S1_{i}_{shapeName}_withoutBody.png");
                Vector3 beforeCapturePos = gameCamera.transform.position;
                Quaternion beforeCaptureRot = gameCamera.transform.rotation;
                Color32[] withBody = null, withoutBody = null;
                bool[] savedRendererState = bodyRenderers.Select(r => r != null && r.enabled).ToArray();
                Exception captureError = null;
                int renderCallbacks = 0;
                float maxRenderCallbackPosDelta = 0f, maxRenderCallbackRotDelta = 0f;
                Action<ScriptableRenderContext, Camera> onBeginCamera = (context, renderingCamera) =>
                {
                    if (renderingCamera != gameCamera) return;
                    renderCallbacks++;
                    maxRenderCallbackPosDelta = Mathf.Max(maxRenderCallbackPosDelta,
                        Vector3.Distance(beforeCapturePos, renderingCamera.transform.position));
                    maxRenderCallbackRotDelta = Mathf.Max(maxRenderCallbackRotDelta,
                        Quaternion.Angle(beforeCaptureRot, renderingCamera.transform.rotation));
                };
                Camera.CameraCallback onBuiltInCamera = renderingCamera => onBeginCamera(default, renderingCamera);
                RenderPipelineManager.beginCameraRendering += onBeginCamera;
                Camera.onPreRender += onBuiltInCamera;
                try
                {
                    withBody = CaptureExistingCamera(gameCamera, png);
                    try
                    {
                        foreach (Renderer r in bodyRenderers) if (r != null) r.enabled = false;
                        withoutBody = CaptureExistingCamera(gameCamera, noBodyPng);
                    }
                    finally
                    {
                        for (int k = 0; k < bodyRenderers.Length; k++)
                            if (bodyRenderers[k] != null) bodyRenderers[k].enabled = savedRendererState[k];
                    }
                }
                catch (Exception e) { captureError = e; }
                finally
                {
                    RenderPipelineManager.beginCameraRendering -= onBeginCamera;
                    Camera.onPreRender -= onBuiltInCamera;
                }
                float capturePosDelta = Vector3.Distance(beforeCapturePos, gameCamera.transform.position);
                float captureRotDelta = Quaternion.Angle(beforeCaptureRot, gameCamera.transform.rotation);
                bool poseStable = capturePosDelta < 0.001f && captureRotDelta < 0.01f;
                record("PRESENT.capturePose." + shapeName, poseStable,
                    $"두 번 Camera.Render 전후 position Δ={capturePosDelta:F5}U rotation Δ={captureRotDelta:F5}°; nearClip={gameCamera.nearClipPlane:F3}, cullingMask={gameCamera.cullingMask}");
                record("PRESENT.renderCallback." + shapeName,
                    renderCallbacks >= 2 && maxRenderCallbackPosDelta < 0.001f && maxRenderCallbackRotDelta < 0.01f,
                    $"SRP/기본 렌더 카메라 콜백 {renderCallbacks}회, 캡처 시작 pose 대비 최대 Δpos={maxRenderCallbackPosDelta:F5}U Δrot={maxRenderCallbackRotDelta:F5}° (콜백0이면 Camera.Render 경로 확인 필요)");
                if (captureError != null || withBody == null || withoutBody == null)
                {
                    record("PRESENT.png." + shapeName, false, "기존 카메라 ON/OFF PNG 캡처 실패: " + captureError);
                    record("PRESENT.view." + shapeName, false, pose + " / ON/OFF 캡처 실패");
                    continue;
                }
                int changedPixels = CountChangedPixelsNear(withBody, withoutBody, viewport);
                bool visiblePixels = enabledBodyRenderers.Length > 0 && changedPixels >= 20;
                record("PRESENT.bodyPixels." + shapeName, visiblePixels,
                    $"몸통 Renderer ON/OFF 차이: 투영 중심 ±12%W/18%H 내 변화 픽셀 {changedPixels}; enabledRenderers={enabledBodyRenderers.Length}/{bodyRenderers.Length}, on={png}, off={noBodyPng}");
                record("PRESENT.nearPlane." + shapeName, nearPlaneWallCount == 0,
                    $"현재 카메라 near-plane 3×3 벽/지형 교차={nearPlaneWallCount}/9; {nearPlaneDetail}");
                record("PRESENT.view." + shapeName, inFrame && !blocked && poseStable && visiblePixels && nearPlaneWallCount == 0,
                    pose + $" on={png} off={noBodyPng} changedPixels={changedPixels} (실제 캡처 픽셀 가시성 포함)");
            }

            // 팀의 공개 조준/굴리기 훅으로 카메라 상태를 만들고 가림 보정 OFF/ON의 forward만 비교한다.
            // 실제 우클릭·굴리기 입력을 재현하는 검사가 아니라, 보정기가 팀 광축을 보존하는지의 API 검사다.
            if (occlusion != null)
            {
                PlayerFollowCamera.SetActiveTarget(hookOwner);
                PlayerFollowCamera.ExitAimMode();
                PlayerFollowCamera.SetTrackingAnchor(hookOwner, null);
                PlayerFollowCamera.SnapToTarget(hookOwner);
                float settle = Time.realtimeSinceStartup + 0.5f;
                while (Time.realtimeSinceStartup < settle) yield return null;

                occlusion.enabled = false;
                yield return null;
                yield return null;
                Vector3 ordinaryOff = gameCamera.transform.forward;
                Vector3 ordinaryOffPos = gameCamera.transform.position;
                occlusion.enabled = true;
                yield return null;
                yield return null;
                RecordForward("normal", ordinaryOff, ordinaryOffPos, gameCamera, occlusion, record, summary);

                PlayerFollowCamera.EnterAimMode();
                settle = Time.realtimeSinceStartup + Mathf.Max(0.5f, follow.aimTransitionSeconds + 0.25f);
                while (Time.realtimeSinceStartup < settle) yield return null;
                occlusion.enabled = false;
                yield return null;
                yield return null;
                Vector3 aimOff = gameCamera.transform.forward;
                Vector3 aimOffPos = gameCamera.transform.position;
                occlusion.enabled = true;
                yield return null;
                yield return null;
                RecordForward("aimHook", aimOff, aimOffPos, gameCamera, occlusion, record, summary);

                PlayerFollowCamera.ExitAimMode();
                settle = Time.realtimeSinceStartup + Mathf.Max(0.5f, follow.aimTransitionSeconds + 0.25f);
                while (Time.realtimeSinceStartup < settle) yield return null;
                PlayerFollowCamera.SetTrackingAnchor(hookOwner, hookOwner.position + new Vector3(0f, 1.2f, 0.5f));
                settle = Time.realtimeSinceStartup + Mathf.Max(0.5f, follow.rollAnchorEnterSeconds + 0.25f);
                while (Time.realtimeSinceStartup < settle) yield return null;
                occlusion.enabled = false;
                yield return null;
                yield return null;
                Vector3 anchorOff = gameCamera.transform.forward;
                Vector3 anchorOffPos = gameCamera.transform.position;
                occlusion.enabled = true;
                yield return null;
                yield return null;
                RecordForward("trackingAnchorHook", anchorOff, anchorOffPos, gameCamera, occlusion, record, summary);
            }

            // OnGUI는 RoleAssignmentManager가 활성화된 동안 전역 GUI 콜백으로 호출된다.
            // 실제 가시성은 게임 UI 화면에서 별도 관찰해야 하며, 여기서는 객체 활성·표시 옵션만 기록한다.
            Lab_MapPresentation mapPresentation = UnityEngine.Object.FindObjectOfType<Lab_MapPresentation>();
            record("PRESENT.hudAdapter", mapPresentation != null && mapPresentation.enabled,
                mapPresentation != null ? "Map4 현재 챕터 HUD 표시 범위 어댑터 활성" : "Map4 현재 챕터 HUD 표시 범위 어댑터 없음");
            RoleAssignmentManager[] managers = Resources.FindObjectsOfTypeAll<RoleAssignmentManager>()
                .Where(m => m != null && m.gameObject.scene.IsValid()).ToArray();
            if (managers.Length == 0) record("PRESENT.hud", false, "RoleAssignmentManager 없음 — OnGUI 범위 확인 불가");
            foreach (RoleAssignmentManager manager in managers)
            {
                string origin = manager.gameObject.scene.name;
                bool active = manager.gameObject.activeInHierarchy && manager.enabled;
                bool currentScope = Lab_MapPresentation.IsCurrentSector(manager);
                bool overlayEscaped = active && !currentScope && manager.showStatusOverlay;
                string detail = $"scene={origin} object={HitPath(manager.transform)} active={active} showStatusOverlay={manager.showStatusOverlay} state={manager.State} " +
                    $"currentSectorScope={currentScope} S7flowActive={FlowActive<Lab_ServerRoomFlow>(sectors[6])} S8managerActive={FlowActive<ManagerChapterController>(sectors[7])}; " +
                    "OnGUI 범위는 showStatusOverlay와 현재 챕터 어댑터의 결과에 좌우됨";
                summary.AppendLine("HUD " + detail);
                record("PRESENT.hud." + origin, !overlayEscaped, detail + " (실제 화면 표시 여부는 UI 실측 필요)");
            }
        }
        finally
        {
            PlayerFollowCamera.ExitAimMode();
            PlayerFollowCamera.SetTrackingAnchor(hookOwner, null);
            if (occlusion != null) occlusion.enabled = previousOcclusionEnabled;
            PlayerFollowCamera.SetActiveTarget(previousTarget);
            if (previousTarget != null) PlayerFollowCamera.SnapToTarget(previousTarget);
            File.WriteAllText(Path.Combine(folder, "summary.txt"), summary.ToString(), new UTF8Encoding(false));
            Debug.Log(Tag + " 결과: " + folder);
        }
    }

    private static string HitPath(Transform leaf)
    {
        if (leaf == null) return "(null)";
        List<string> parts = new List<string>();
        for (Transform t = leaf; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static bool FlowActive<T>(SectorController sector) where T : Component
    {
        T flow = sector.GetComponentInChildren<T>(true);
        return flow != null && flow.gameObject.activeInHierarchy;
    }

    private static void RecordForward(string mode, Vector3 offForward, Vector3 offPosition,
        Camera camera, Lab_CameraOcclusion occlusion, Action<string, bool, string> record, StringBuilder summary)
    {
        float angle = Vector3.Angle(offForward, camera.transform.forward);
        string detail = $"{mode} adapter OFF pos={offPosition} forward={offForward}; ON pos={camera.transform.position} " +
            $"forward={camera.transform.forward}; angle={angle:F4}° (허용 ≤0.05°), allowed={occlusion.AllowedDistance:F3}, " +
            $"blocker={HitPath(occlusion.LastObstruction != null ? occlusion.LastObstruction.transform : null)}. " +
            "공개 API 훅의 시야 보존 증거이며 실제 사람 조준/굴리기 조작 시험은 아님";
        summary.AppendLine("FORWARD " + detail);
        record("PRESENT.forward." + mode, angle <= 0.05f, detail);
    }

    private static string FirstTerrainHit(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < 1e-6f) return "(없음)";
        RaycastHit[] hits = Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits.OrderBy(h => h.distance))
            if (hit.collider != null && !hit.collider.CompareTag("Player")) return HitPath(hit.collider.transform);
        return "(없음)";
    }

    private static int CountChangedPixelsNear(Color32[] on, Color32[] off, Vector3 viewport)
    {
        if (on == null || off == null || on.Length != Width * Height || off.Length != on.Length) return 0;
        int centerX = Mathf.RoundToInt(viewport.x * Width), centerY = Mathf.RoundToInt(viewport.y * Height);
        int x0 = Mathf.Clamp(centerX - Mathf.RoundToInt(Width * 0.12f), 0, Width - 1);
        int x1 = Mathf.Clamp(centerX + Mathf.RoundToInt(Width * 0.12f), 0, Width - 1);
        int y0 = Mathf.Clamp(centerY - Mathf.RoundToInt(Height * 0.18f), 0, Height - 1);
        int y1 = Mathf.Clamp(centerY + Mathf.RoundToInt(Height * 0.18f), 0, Height - 1);
        int changed = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int k = y * Width + x;
                if (Math.Abs((int)on[k].r - off[k].r) + Math.Abs((int)on[k].g - off[k].g) +
                    Math.Abs((int)on[k].b - off[k].b) >= 30) changed++;
            }
        return changed;
    }

    private static Color32[] CaptureExistingCamera(Camera camera, string path)
    {
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture target = null;
        Texture2D image = null;
        try
        {
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            Color32[] pixels = image.GetPixels32();
            File.WriteAllBytes(path, image.EncodeToPNG());
            return pixels;
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (image != null) UnityEngine.Object.Destroy(image);
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
        }
    }
}
#endif
