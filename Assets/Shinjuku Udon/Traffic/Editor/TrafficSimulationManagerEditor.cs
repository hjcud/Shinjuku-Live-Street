using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;

/// <summary>
/// 교통 시스템의 Inspector와 Scene Gizmo를 표시하고 차량 차체 범위를 베이크한다.
/// </summary>
[CustomEditor(typeof(TrafficSimulationManager))]
public class TrafficSimulationManagerEditor : Editor
{
    private const float PredictionTime = 1f;
    private const int ManeuverPathSampleCount = 49;
    private const string VehicleShadowAssetGuid =
        "3e42b0132c6212841a6954d4cdbc2d22";

    // Udon 실행 객체에서 읽어 Inspector에 실시간 네트워크 상태를 표시한다.
    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target))
        {
            return;
        }

        DrawDefaultInspector();

        TrafficSimulationManager manager =
            (TrafficSimulationManager)target;

        DrawVehicleBoundsInspector(manager);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Network: 단일 소유자 권위 / 16슬롯 압축 / " +
            "Manual Sync 전체 스냅샷\n" +
            "Play Mode Gizmo: 흰색=현재 차체 판정, " +
             "빨강=차선 변경 중 출발 차선 점유, " +
             "자홍=목표 차선 점유/Register, " +
             "청록=이동 중 1초 뒤 예상 위치, " +
             "초록 종단표시=권한자 장애물 감지 끝/Clear, " +
             "주황 상자=PHY, " +
             "SIG=동기화 신호 정지, " +
             "파랑 상자=LIM(목표 차선 경계로 감지 제한), " +
             "노랑/진홍=차선 변경 이동 경로 안전/Blocked",
            MessageType.Info
        );

        if (!Application.isPlaying)
        {
            return;
        }

        EditorGUILayout.LabelField(
            "Network Runtime",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Local Authority",
            GetRuntimeValue(
                manager,
                "localIsAuthority",
                manager.localIsAuthority
            ).ToString()
        );

        EditorGUILayout.LabelField(
            "Active Vehicles",
            GetRuntimeValue(
                manager,
                "activeVehicleCount",
                manager.activeVehicleCount
            ).ToString()
        );

        EditorGUILayout.LabelField(
            "Active Lane Changes",
            GetRuntimeValue(
                manager,
                "activeLaneChangeCount",
                manager.activeLaneChangeCount
            ).ToString()
        );

        EditorGUILayout.LabelField(
            "Authority Physics Queries / Hits",
            GetRuntimeValue(
                manager,
                "authorityPhysicsQueryCount",
                manager.authorityPhysicsQueryCount
            ) + " / " +
            GetRuntimeValue(
                manager,
                "authorityPhysicsHitCount",
                manager.authorityPhysicsHitCount
            )
        );

        bool laneCacheReady = GetRuntimeValue(
            manager,
            "laneVehicleCacheReady",
            false
        );
        bool laneCacheDirty = GetRuntimeValue(
            manager,
            "laneVehicleCacheDirty",
            true
        );

        EditorGUILayout.LabelField(
            "Lane Search Cache",
            laneCacheReady && !laneCacheDirty
                ? "Active"
                : "Safety Fallback"
        );

        EditorGUILayout.LabelField(
            "Received Sequence",
            GetRuntimeValue(
                manager,
                "lastReceivedSequence",
                manager.lastReceivedSequence
            ).ToString()
        );

        EditorGUILayout.LabelField(
            "Last Packet Bytes",
            GetRuntimeValue(
                manager,
                "lastSerializationBytes",
                manager.lastSerializationBytes
            ).ToString()
        );

        EditorGUILayout.LabelField(
            "Last Send Success",
            GetRuntimeValue(
                manager,
                "lastSerializationSucceeded",
                manager.lastSerializationSucceeded
            ).ToString()
        );
    }

    private static void DrawVehicleBoundsInspector(
        TrafficSimulationManager manager)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Vehicle Bounds",
            EditorStyles.boldLabel
        );

        int rootCount = manager.vehicleRoots != null
            ? manager.vehicleRoots.Length
            : 0;
        bool boundsReady = HasCompleteVehicleBounds(
            manager,
            rootCount
        );

        EditorGUILayout.HelpBox(
            boundsReady
                ? rootCount +
                  "대 차량의 실제 렌더 크기와 피벗 오프셋이 " +
                  "베이크되어 있습니다. 런타임 렌더러 검색이나 " +
                  "네트워크 전송은 발생하지 않습니다."
                : "차량별 바운더리가 없거나 Vehicle Roots와 " +
                  "일치하지 않습니다.",
            boundsReady
                ? MessageType.Info
                : MessageType.Warning
        );

        using (new EditorGUI.DisabledScope(
            Application.isPlaying || rootCount == 0
        ))
        {
            if (GUILayout.Button(
                "Bake Vehicle Bounds",
                GUILayout.Height(30f)
            ))
            {
                BakeVehicleBounds(manager, true);
            }
        }
    }

    private static bool HasCompleteVehicleBounds(
        TrafficSimulationManager manager,
        int rootCount)
    {
        if (manager == null || rootCount <= 0 ||
            manager.bakedVehicleFrontExtents == null ||
            manager.bakedVehicleRearExtents == null ||
            manager.bakedVehicleWidths == null ||
            manager.bakedVehicleFrontExtents.Length != rootCount ||
            manager.bakedVehicleRearExtents.Length != rootCount ||
            manager.bakedVehicleWidths.Length != rootCount)
        {
            return false;
        }

        for (int i = 0; i < rootCount; i++)
        {
            if (manager.bakedVehicleFrontExtents[i] <= 0.01f ||
                manager.bakedVehicleRearExtents[i] <= 0.01f ||
                manager.bakedVehicleWidths[i] <= 0.01f)
            {
                return false;
            }
        }

        return true;
    }

    private static void BakeVehicleBounds(
        TrafficSimulationManager manager,
        bool recordUndo)
    {
        if (manager == null || manager.vehicleRoots == null)
        {
            return;
        }

        int rootCount = manager.vehicleRoots.Length;
        float[] frontExtents = new float[rootCount];
        float[] rearExtents = new float[rootCount];
        float[] widths = new float[rootCount];

        if (recordUndo)
        {
            Undo.RecordObject(manager, "Bake Vehicle Bounds");
        }

        float longitudinalMargin = Mathf.Max(
            0f,
            manager.vehicleBoundsLongitudinalMargin
        );
        float lateralMargin = Mathf.Max(
            0f,
            manager.vehicleBoundsLateralMargin
        );

        for (int vehicleIndex = 0;
             vehicleIndex < rootCount;
             vehicleIndex++)
        {
            Transform root = manager.vehicleRoots[vehicleIndex];
            bool truck = vehicleIndex == manager.truckSlotIndex;
            float visualScale = Mathf.Clamp(
                truck
                    ? manager.truckVisualScale
                    : manager.normalCarVisualScale,
                0.8f,
                1.25f
            );

            Vector3 minimum;
            Vector3 maximum;

            if (root != null &&
                TryGetRendererBoundsInRootSpace(
                    root,
                    out minimum,
                    out maximum
                ))
            {
                Vector3 rootScale = root.lossyScale;
                float widthScale =
                    Mathf.Abs(rootScale.x) * visualScale;
                float lengthScale =
                    Mathf.Abs(rootScale.z) * visualScale;

                frontExtents[vehicleIndex] = Mathf.Max(
                    0.05f,
                    maximum.z * lengthScale
                ) + longitudinalMargin;
                rearExtents[vehicleIndex] = Mathf.Max(
                    0.05f,
                    -minimum.z * lengthScale
                ) + longitudinalMargin;

                float rendererWidth = Mathf.Max(
                    0.1f,
                    (maximum.x - minimum.x) * widthScale
                );
                widths[vehicleIndex] = Mathf.Max(
                    0.1f,
                    GetVehicleBodyWidthWithoutMirrors(
                        manager,
                        vehicleIndex,
                        root,
                        visualScale,
                        rendererWidth
                    ) +
                    lateralMargin * 2f
                );
            }
            else
            {
                float fallbackLength = Mathf.Max(
                    0.1f,
                    truck
                        ? manager.truckVehicleLength
                        : manager.vehicleLength
                ) * visualScale;
                float fallbackWidth = Mathf.Max(
                    0.1f,
                    truck
                        ? manager.truckVehicleWidth
                        : manager.vehicleWidth
                ) * visualScale;

                frontExtents[vehicleIndex] =
                    fallbackLength * 0.5f +
                    longitudinalMargin;
                rearExtents[vehicleIndex] =
                    fallbackLength * 0.5f +
                    longitudinalMargin;
                widths[vehicleIndex] =
                    fallbackWidth + lateralMargin * 2f;
            }
        }

        manager.bakedVehicleFrontExtents = frontExtents;
        manager.bakedVehicleRearExtents = rearExtents;
        manager.bakedVehicleWidths = widths;

        UdonSharpEditorUtility.CopyProxyToUdon(
            manager,
            ProxySerializationPolicy.All
        );
        EditorUtility.SetDirty(manager);

        UdonBehaviour backingBehaviour =
            UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);

        if (backingBehaviour != null)
        {
            EditorUtility.SetDirty(backingBehaviour);
        }

        if (manager.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(
                manager.gameObject.scene
            );
        }

    }

    private static float GetVehicleBodyWidthWithoutMirrors(
        TrafficSimulationManager manager,
        int vehicleIndex,
        Transform root,
        float visualScale,
        float rendererWidth)
    {
        if (vehicleIndex == manager.truckSlotIndex)
        {
            float parentScale = root.parent != null
                ? Mathf.Abs(root.parent.lossyScale.x)
                : 1f;

            return Mathf.Max(0.1f, manager.truckVehicleWidth) *
                parentScale * visualScale;
        }

        float commonBodyWidth = Mathf.Max(
            0.1f,
            manager.vehicleWidth
        ) * Mathf.Abs(root.lossyScale.x) * visualScale;

        return Mathf.Min(rendererWidth, commonBodyWidth);
    }

    private static bool TryGetRendererBoundsInRootSpace(
        Transform root,
        out Vector3 minimum,
        out Vector3 maximum)
    {
        minimum = new Vector3(
            float.PositiveInfinity,
            float.PositiveInfinity,
            float.PositiveInfinity
        );
        maximum = new Vector3(
            float.NegativeInfinity,
            float.NegativeInfinity,
            float.NegativeInfinity
        );

        Renderer[] renderers =
            root.GetComponentsInChildren<Renderer>(true);
        bool foundRenderer = false;

        for (int rendererIndex = 0;
             rendererIndex < renderers.Length;
             rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];

            if (renderer == null ||
                renderer is ParticleSystemRenderer ||
                renderer is TrailRenderer ||
                renderer is LineRenderer ||
                IsExcludedVehicleBoundsRenderer(renderer, root) ||
                IsEditorOnly(renderer.transform, root))
            {
                continue;
            }

            Bounds localBounds = renderer.localBounds;
            Matrix4x4 rendererToRoot =
                root.worldToLocalMatrix *
                renderer.transform.localToWorldMatrix;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localCorner = localBounds.center +
                    Vector3.Scale(
                        localBounds.extents,
                        new Vector3(
                            (corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f
                        )
                    );
                Vector3 rootCorner =
                    rendererToRoot.MultiplyPoint3x4(localCorner);

                minimum = Vector3.Min(minimum, rootCorner);
                maximum = Vector3.Max(maximum, rootCorner);
                foundRenderer = true;
            }
        }

        return foundRenderer;
    }

    private static bool IsExcludedVehicleBoundsRenderer(
        Renderer renderer,
        Transform root)
    {
        Transform current = renderer.transform;

        while (current != null)
        {
            if (string.Equals(
                    current.name,
                    "shadow",
                    System.StringComparison.OrdinalIgnoreCase
                ))
            {
                return true;
            }

            if (current == root)
            {
                break;
            }

            current = current.parent;
        }

        Mesh sharedMesh = null;
        SkinnedMeshRenderer skinnedRenderer =
            renderer as SkinnedMeshRenderer;

        if (skinnedRenderer != null)
        {
            sharedMesh = skinnedRenderer.sharedMesh;
        }
        else
        {
            MeshFilter meshFilter =
                renderer.GetComponent<MeshFilter>();

            if (meshFilter != null)
            {
                sharedMesh = meshFilter.sharedMesh;
            }
        }

        if (sharedMesh == null)
        {
            return false;
        }

        string assetPath = AssetDatabase.GetAssetPath(sharedMesh);

        return !string.IsNullOrEmpty(assetPath) &&
            string.Equals(
                AssetDatabase.AssetPathToGUID(assetPath),
                VehicleShadowAssetGuid,
                System.StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsEditorOnly(
        Transform current,
        Transform root)
    {
        while (current != null)
        {
            if (current.gameObject.tag == "EditorOnly")
            {
                return true;
            }

            if (current == root)
            {
                break;
            }

            current = current.parent;
        }

        return false;
    }

    [DrawGizmo(
        GizmoType.Selected |
        GizmoType.NonSelected |
        GizmoType.Active
    )]
    private static void DrawVehicleOccupancyVisualization(
        TrafficSimulationManager manager,
        GizmoType gizmoType)
    {
        if (!Application.isPlaying ||
            manager == null ||
            manager.laneDatabase == null ||
            !manager.laneDatabase.IsReady())
        {
            return;
        }

        bool authority = GetRuntimeValue(
            manager,
            "localIsAuthority",
            manager.localIsAuthority
        );

        bool[] active = GetRuntimeValue(
            manager,
            authority
                ? "vehicleActive"
                : "snapshotNextActive",
            (bool[])null
        );

        int[] laneIds = GetRuntimeValue(
            manager,
            authority
                ? "vehicleLaneIds"
                : "snapshotNextLaneIds",
            (int[])null
        );

        float[] laneS = GetRuntimeValue(
            manager,
            authority
                ? "vehicleS"
                : "snapshotNextS",
            (float[])null
        );

        float[] speeds = GetRuntimeValue(
            manager,
            authority
                ? "vehicleSpeeds"
                : "snapshotNextSpeeds",
            (float[])null
        );

        bool[] changing = GetRuntimeValue(
            manager,
            authority
                ? "laneChangeActive"
                : "snapshotNextLaneChangeActive",
            (bool[])null
        );

        bool[] reversing = GetRuntimeValue(
            manager,
            authority
                ? "laneChangeReverseManeuver"
                : "snapshotNextReverseManeuver",
            (bool[])null
        );

        bool[] emergency = GetRuntimeValue(
            manager,
            authority
                ? "laneChangeEmergencyManeuver"
                : "snapshotNextEmergencyManeuver",
            (bool[])null
        );

        bool[] preparing = authority
            ? GetRuntimeValue(
                manager,
                "laneChangePreparing",
                (bool[])null
              )
            : null;

        int[] targetLaneIds = GetRuntimeValue(
            manager,
            authority
                ? "laneChangeTargetLaneIds"
                : "snapshotNextTargetLaneIds",
            (int[])null
        );

        float[] progress = GetRuntimeValue(
            manager,
            authority
                ? "laneChangeProgress"
                : "snapshotNextLaneChangeProgress",
            (float[])null
        );

        float[] recoveryDistances = GetRuntimeValue(
            manager,
            authority
                ? "laneChangeRecoveryDistance"
                : "snapshotNextRecoveryDistance",
            (float[])null
        );

        bool[] maneuverPathValid = GetRuntimeValue(
            manager,
            "maneuverPathValid",
            (bool[])null
        );

        Vector3[] maneuverPathPositions = GetRuntimeValue(
            manager,
            "maneuverPathPositions",
            (Vector3[])null
        );

        Quaternion[] maneuverPathRotations = GetRuntimeValue(
            manager,
            "maneuverPathRotations",
            (Quaternion[])null
        );

        float[] maneuverPathDistances = GetRuntimeValue(
            manager,
            "maneuverPathDistances",
            (float[])null
        );

        bool[] playerSweepBlocked = authority
            ? GetRuntimeValue(
                manager,
                "laneChangePlayerBlocked",
                (bool[])null
              )
            : null;

        bool[] vehicleSweepBlocked = authority
            ? GetRuntimeValue(
                manager,
                "laneChangeVehicleBlocked",
                (bool[])null
              )
            : null;

        bool[] roadBoundaryBlocked = authority
            ? GetRuntimeValue(
                manager,
                "laneChangeRoadBoundaryBlocked",
                (bool[])null
              )
            : null;

        bool[] obstacleSweepDebugValid = authority
            ? GetRuntimeValue(
                manager,
                "laneChangeObstacleSweepDebugValid",
                (bool[])null
              )
            : null;

        Vector3[] obstacleSweepStartPositions = authority
            ? GetRuntimeValue(
                manager,
                "laneChangeObstacleSweepStartPositions",
                (Vector3[])null
              )
            : null;

        Vector3[] obstacleSweepEndPositions = authority
            ? GetRuntimeValue(
                manager,
                "laneChangeObstacleSweepEndPositions",
                (Vector3[])null
              )
            : null;

        Quaternion[] obstacleSweepStartRotations = authority
            ? GetRuntimeValue(
                manager,
                "laneChangeObstacleSweepStartRotations",
                (Quaternion[])null
              )
            : null;

        Quaternion[] obstacleSweepEndRotations = authority
            ? GetRuntimeValue(
                manager,
                "laneChangeObstacleSweepEndRotations",
                (Quaternion[])null
              )
            : null;

        bool[] physicsCastValid = authority
            ? GetRuntimeValue(
                manager,
                "physicsObstacleCastDebugValid",
                (bool[])null
              )
            : null;

        bool[] physicsCastBlocked = authority
            ? GetRuntimeValue(
                manager,
                "physicsObstacleCastDebugBlocked",
                (bool[])null
              )
            : null;

        bool[] physicsCastLaneLimited = authority
            ? GetRuntimeValue(
                manager,
                "physicsObstacleCastDebugLaneLimited",
                (bool[])null
              )
            : null;

        bool[] signalStops = authority
            ? GetRuntimeValue(
                manager,
                "signalStopDebugActive",
                (bool[])null
              )
            : null;

        Vector3[] physicsCastCenters = authority
            ? GetRuntimeValue(
                manager,
                "physicsObstacleCastDebugCenters",
                (Vector3[])null
              )
            : null;

        Vector3[] physicsCastHalfExtents = authority
            ? GetRuntimeValue(
                manager,
                "physicsObstacleCastDebugHalfExtents",
                (Vector3[])null
              )
            : null;

        Vector3[] physicsCastDirections = authority
            ? GetRuntimeValue(
                manager,
                "physicsObstacleCastDebugDirections",
                (Vector3[])null
              )
            : null;

        Quaternion[] physicsCastRotations = authority
            ? GetRuntimeValue(
                manager,
                "physicsObstacleCastDebugRotations",
                (Quaternion[])null
              )
            : null;

        float[] physicsCastDistances = authority
            ? GetRuntimeValue(
                manager,
                "physicsObstacleCastDebugDistances",
                (float[])null
              )
            : null;

        float[] physicsCastHitDistances = authority
            ? GetRuntimeValue(
                manager,
                "physicsObstacleCastDebugHitDistances",
                (float[])null
              )
            : null;

        if (active == null ||
            laneIds == null ||
            laneS == null ||
            speeds == null ||
            changing == null ||
            reversing == null ||
            emergency == null ||
            targetLaneIds == null ||
            progress == null)
        {
            return;
        }

        int count = active.Length;
        count = Mathf.Min(count, laneIds.Length);
        count = Mathf.Min(count, laneS.Length);
        count = Mathf.Min(count, speeds.Length);
        count = Mathf.Min(count, changing.Length);
        count = Mathf.Min(count, reversing.Length);
        count = Mathf.Min(count, emergency.Length);
        count = Mathf.Min(count, targetLaneIds.Length);
        count = Mathf.Min(count, progress.Length);

        TrafficLaneDatabase database = manager.laneDatabase;
        Color previousColor = Handles.color;

        if (manager.recoveryRoadArea != null)
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousGizmoColor = Gizmos.color;
            Gizmos.matrix = manager.recoveryRoadArea.transform
                .localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 1f, 0.45f, 0.5f);
            Gizmos.DrawWireCube(
                manager.recoveryRoadArea.center,
                manager.recoveryRoadArea.size
            );
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousGizmoColor;
        }

        for (int vehicleIndex = 0;
             vehicleIndex < count;
             vehicleIndex++)
        {
            if (!active[vehicleIndex])
            {
                continue;
            }

            int laneId = laneIds[vehicleIndex];

            if (laneId < 0 || laneId >= database.laneCount)
            {
                continue;
            }

            bool truck =
                vehicleIndex == manager.truckSlotIndex;
            float visualScale = Mathf.Clamp(
                truck
                    ? manager.truckVisualScale
                    : manager.normalCarVisualScale,
                0.8f,
                1.25f
            );
            bool hasBakedBounds =
                manager.bakedVehicleFrontExtents != null &&
                manager.bakedVehicleRearExtents != null &&
                manager.bakedVehicleWidths != null &&
                vehicleIndex <
                    manager.bakedVehicleFrontExtents.Length &&
                vehicleIndex <
                    manager.bakedVehicleRearExtents.Length &&
                vehicleIndex < manager.bakedVehicleWidths.Length &&
                manager.bakedVehicleFrontExtents[vehicleIndex] > 0.01f &&
                manager.bakedVehicleRearExtents[vehicleIndex] > 0.01f &&
                manager.bakedVehicleWidths[vehicleIndex] > 0.01f;
            float fallbackLength = Mathf.Max(
                0.1f,
                truck
                    ? manager.truckVehicleLength
                    : manager.vehicleLength
            ) * visualScale;
            float frontExtent = hasBakedBounds
                ? manager.bakedVehicleFrontExtents[vehicleIndex]
                : fallbackLength * 0.5f;
            float rearExtent = hasBakedBounds
                ? manager.bakedVehicleRearExtents[vehicleIndex]
                : fallbackLength * 0.5f;
            float length = frontExtent + rearExtent;
            float width = hasBakedBounds
                ? manager.bakedVehicleWidths[vehicleIndex]
                : Mathf.Max(
                    0.1f,
                    truck
                        ? manager.truckVehicleWidth
                        : manager.vehicleWidth
                  ) * visualScale;
            float centerOffset =
                (frontExtent - rearExtent) * 0.5f;
            float currentS = Mathf.Clamp(
                laneS[vehicleIndex],
                0f,
                database.laneLengths[laneId]
            );

            Vector3 sourcePosition =
                database.GetLanePosition(laneId, currentS, -1);

            Quaternion sourceRotation =
                database.GetLaneRotation(laneId, currentS, -1);

            Vector3 currentVisualPosition = sourcePosition;
            Quaternion currentVisualRotation = sourceRotation;

            if (manager.vehicleRoots != null &&
                vehicleIndex < manager.vehicleRoots.Length &&
                manager.vehicleRoots[vehicleIndex] != null &&
                manager.vehicleRoots[vehicleIndex].gameObject.activeInHierarchy)
            {
                currentVisualPosition =
                    manager.vehicleRoots[vehicleIndex].position;
                currentVisualRotation =
                    manager.vehicleRoots[vehicleIndex].rotation;
            }

            bool physicsPoseAvailable =
                authority &&
                physicsCastValid != null &&
                physicsCastBlocked != null &&
                physicsCastLaneLimited != null &&
                physicsCastCenters != null &&
                physicsCastRotations != null &&
                vehicleIndex < physicsCastValid.Length &&
                vehicleIndex < physicsCastBlocked.Length &&
                vehicleIndex < physicsCastLaneLimited.Length &&
                vehicleIndex < physicsCastCenters.Length &&
                vehicleIndex < physicsCastRotations.Length &&
                physicsCastValid[vehicleIndex];
            bool physicsBlockedNow = physicsPoseAvailable &&
                physicsCastBlocked[vehicleIndex];
            bool physicsLaneLimitedNow = physicsPoseAvailable &&
                physicsCastLaneLimited[vehicleIndex];
            bool signalStopNow = signalStops != null &&
                vehicleIndex < signalStops.Length &&
                signalStops[vehicleIndex];
            Vector3 currentCollisionPosition = currentVisualPosition;
            Quaternion currentCollisionRotation = currentVisualRotation;

            int targetLaneId = targetLaneIds[vehicleIndex];
            int ruleIndex = -1;
            float targetCurrentS = currentS;
            float smoothProgress = 0f;
            float laneSeparation = 3.2f;
            float recoveryDistance = Mathf.Max(
                0.6f,
                manager.laneChangeReverseDistance
            );
            bool currentPathBlocked =
                authority &&
                playerSweepBlocked != null &&
                vehicleSweepBlocked != null &&
                roadBoundaryBlocked != null &&
                vehicleIndex < playerSweepBlocked.Length &&
                vehicleIndex < vehicleSweepBlocked.Length &&
                vehicleIndex < roadBoundaryBlocked.Length &&
                (playerSweepBlocked[vehicleIndex] ||
                 vehicleSweepBlocked[vehicleIndex] ||
                 roadBoundaryBlocked[vehicleIndex]);

            if (reversing[vehicleIndex] &&
                recoveryDistances != null &&
                vehicleIndex < recoveryDistances.Length)
            {
                recoveryDistance = Mathf.Max(
                    0.6f,
                    recoveryDistances[vehicleIndex]
                );
            }

            bool validLaneChange =
                changing[vehicleIndex] &&
                targetLaneId >= 0 &&
                targetLaneId < database.laneCount;
            int displayLaneId = validLaneChange
                ? targetLaneId
                : laneId;

            if (validLaneChange)
            {
                ruleIndex = FindLaneChangeRule(
                    database,
                    laneId,
                    targetLaneId
                );

                validLaneChange = ruleIndex >= 0;
            }

            bool preparingLaneChange =
                validLaneChange &&
                preparing != null &&
                vehicleIndex < preparing.Length &&
                preparing[vehicleIndex];
            int maneuverPathOffset =
                vehicleIndex * ManeuverPathSampleCount;
            bool cachedManeuverAvailable =
                validLaneChange &&
                maneuverPathValid != null &&
                vehicleIndex < maneuverPathValid.Length &&
                maneuverPathValid[vehicleIndex] &&
                maneuverPathPositions != null &&
                maneuverPathRotations != null &&
                maneuverPathDistances != null &&
                maneuverPathOffset >= 0 &&
                maneuverPathOffset + ManeuverPathSampleCount <=
                    maneuverPathPositions.Length &&
                maneuverPathOffset + ManeuverPathSampleCount <=
                    maneuverPathRotations.Length &&
                maneuverPathOffset + ManeuverPathSampleCount <=
                    maneuverPathDistances.Length;

            if (validLaneChange)
            {
                targetCurrentS = MapSourceToTargetS(
                    database,
                    ruleIndex,
                    targetLaneId,
                    currentS
                );

                Vector3 targetPosition =
                    database.GetLanePosition(
                        targetLaneId,
                        targetCurrentS,
                        -1
                    );

                Quaternion targetRotation =
                    database.GetLaneRotation(
                        targetLaneId,
                        targetCurrentS,
                        -1
                    );

                laneSeparation = Mathf.Max(
                    0.5f,
                    Vector3.Distance(sourcePosition, targetPosition)
                );

                smoothProgress = GetVisualProgress(
                    manager,
                    reversing[vehicleIndex],
                    progress[vehicleIndex],
                    recoveryDistance,
                    laneSeparation,
                    length
                );

                bool targetIsOccupied =
                    reversing[vehicleIndex] ||
                    smoothProgress >= Mathf.Clamp(
                        manager.targetLaneOccupancyStart,
                        0.02f,
                        0.3f
                    );

                Vector3 targetFootprintPosition = targetPosition;
                Quaternion targetFootprintRotation = targetRotation;
                float targetFootprintLength = length;

                if (reversing[vehicleIndex] &&
                    !currentPathBlocked)
                {
                    float recoveryStartSourceS = currentS -
                        GetRecoveryLongitudinalOffset(
                            manager,
                            progress[vehicleIndex],
                            recoveryDistance,
                            laneSeparation,
                            length
                        );
                    float reservationSourceS =
                        recoveryStartSourceS -
                        recoveryDistance * 0.5f;
                    float reservationTargetS = MapSourceToTargetS(
                        database,
                        ruleIndex,
                        targetLaneId,
                        reservationSourceS
                    );

                    targetFootprintPosition =
                        database.GetLanePosition(
                            targetLaneId,
                            reservationTargetS,
                            -1
                        );
                    targetFootprintRotation =
                        database.GetLaneRotation(
                            targetLaneId,
                            reservationTargetS,
                            -1
                        );
                    targetFootprintLength =
                        length + recoveryDistance;
                }

                DrawFootprint(
                    targetFootprintPosition,
                    targetFootprintRotation,
                    targetFootprintLength,
                    reversing[vehicleIndex]
                        ? width + 0.35f
                        : width,
                    centerOffset,
                    targetIsOccupied
                        ? new Color(1f, 0.05f, 0.8f, 0.95f)
                        : new Color(1f, 0.25f, 0.85f, 0.65f),
                    targetIsOccupied,
                    !targetIsOccupied
                );

                Handles.color = new Color(
                    1f,
                    0.25f,
                    0.85f,
                    0.85f
                );

                if (!cachedManeuverAvailable)
                {
                    Handles.DrawDottedLine(
                        currentVisualPosition + Vector3.up * 0.2f,
                        targetPosition + Vector3.up * 0.2f,
                        4f
                    );
                }

                if (cachedManeuverAvailable)
                {
                    int firstPathSample = Mathf.Clamp(
                        Mathf.FloorToInt(
                            progress[vehicleIndex] *
                            (ManeuverPathSampleCount - 1)
                        ),
                        0,
                        ManeuverPathSampleCount - 2
                    );
                    Handles.color = new Color(
                        0.15f,
                        0.95f,
                        1f,
                        0.9f
                    );

                    for (int pathSample = firstPathSample;
                         pathSample < ManeuverPathSampleCount - 1;
                         pathSample++)
                    {
                        Handles.DrawLine(
                            maneuverPathPositions[
                                maneuverPathOffset + pathSample
                            ] + Vector3.up * 0.08f,
                            maneuverPathPositions[
                                maneuverPathOffset + pathSample + 1
                            ] + Vector3.up * 0.08f
                        );
                    }
                }

                if (reversing[vehicleIndex])
                {
                    Handles.Label(
                        targetFootprintPosition + Vector3.up * 0.82f,
                        "V" + vehicleIndex + "/" +
                        GetLaneName(displayLaneId) + " | " +
                        GetRecoveryStateLabel(
                            manager,
                            progress[vehicleIndex]
                        )
                    );
                }
            }

            bool sourceIsOccupied =
                !validLaneChange ||
                smoothProgress < Mathf.Clamp(
                    manager.laneChangeSourceOccupancyEnd,
                    0.7f,
                    0.95f
                );

            if (validLaneChange)
            {
                DrawFootprint(
                    sourcePosition,
                    sourceRotation,
                    length,
                    width,
                    centerOffset,
                    new Color(1f, 0.22f, 0.08f, 0.95f),
                    sourceIsOccupied,
                    !sourceIsOccupied
                );
            }

            DrawFootprint(
                currentCollisionPosition,
                currentCollisionRotation,
                length,
                width,
                centerOffset,
                new Color(1f, 1f, 0.92f, 0.9f),
                false,
                false
            );

            float speed = Mathf.Max(0f, speeds[vehicleIndex]);
            bool reversePhase = validLaneChange &&
                IsRecoveryReversePhase(
                     manager,
                     reversing[vehicleIndex],
                     progress[vehicleIndex]
                 );

            float futureS = Mathf.Clamp(
                currentS +
                    (reversePhase ? -1f : 1f) *
                    speed * PredictionTime,
                0f,
                database.laneLengths[laneId]
            );

            Vector3 futurePosition =
                database.GetLanePosition(laneId, futureS, -1);

            Quaternion futureRotation =
                database.GetLaneRotation(laneId, futureS, -1);

            if (validLaneChange)
            {
                if (!preparingLaneChange && cachedManeuverAvailable)
                {
                    float maximumProgress = 1f;

                    if (reversing[vehicleIndex] &&
                        progress[vehicleIndex] <
                            GetRecoveryPreparationEnd(manager))
                    {
                        maximumProgress =
                            GetRecoveryPreparationEnd(manager);
                    }

                    float futureProgress = AdvanceCachedManeuverProgress(
                        maneuverPathDistances,
                        maneuverPathOffset,
                        progress[vehicleIndex],
                        speed * PredictionTime,
                        maximumProgress
                    );
                    float futureCoordinate = Mathf.Clamp01(
                        futureProgress
                    ) * (ManeuverPathSampleCount - 1);
                    int futurePathSample = Mathf.Clamp(
                        Mathf.FloorToInt(futureCoordinate),
                        0,
                        ManeuverPathSampleCount - 2
                    );
                    float futurePathInterpolation =
                        futureCoordinate - futurePathSample;

                    futurePosition = Vector3.Lerp(
                        maneuverPathPositions[
                            maneuverPathOffset + futurePathSample
                        ],
                        maneuverPathPositions[
                            maneuverPathOffset +
                            futurePathSample + 1
                        ],
                        futurePathInterpolation
                    );
                    futureRotation = Quaternion.Slerp(
                        maneuverPathRotations[
                            maneuverPathOffset + futurePathSample
                        ],
                        maneuverPathRotations[
                            maneuverPathOffset +
                            futurePathSample + 1
                        ],
                        futurePathInterpolation
                    );
                }
                else if (!preparingLaneChange)
                {
                    // 런타임 이동도 고정 경로를 사용할 수 없는 동안 멈추므로 예측
                    // 표시를 절차적 위치로 옮기지 않고 실제 자세에 유지한다.
                    futurePosition = currentVisualPosition;
                    futureRotation = currentVisualRotation;
                }
            }

            bool drawFuturePrediction =
                Vector3.Distance(
                    currentVisualPosition,
                    futurePosition
                ) > 0.15f ||
                Quaternion.Angle(
                    currentVisualRotation,
                    futureRotation
                ) > 2f;

            if (drawFuturePrediction)
            {
                DrawFootprint(
                    futurePosition,
                    futureRotation,
                    length,
                    width,
                    centerOffset,
                    new Color(0.05f, 0.9f, 1f, 0.95f),
                    false,
                    true
                );

                Handles.color = new Color(
                    0.05f,
                    0.9f,
                    1f,
                    0.9f
                );

                Handles.DrawDottedLine(
                    currentVisualPosition + Vector3.up * 0.28f,
                    futurePosition + Vector3.up * 0.28f,
                    5f
                );

                float handleSize =
                    HandleUtility.GetHandleSize(futurePosition);

                Handles.SphereHandleCap(
                    0,
                    futurePosition + Vector3.up * 0.28f,
                    Quaternion.identity,
                    handleSize * 0.07f,
                    EventType.Repaint
                );
            }

            if (!currentPathBlocked)
            {
                string status = physicsBlockedNow
                    ? "PHY"
                    : signalStopNow
                        ? "SIG"
                        : physicsLaneLimitedNow
                            ? "LIM"
                            : preparingLaneChange
                                ? "PREP"
                            : validLaneChange &&
                              emergency[vehicleIndex]
                                ? "EMG(" +
                                  GetProgressQuarterLabel(
                                      progress[vehicleIndex]
                                  ) + ")"
                            : Mathf.RoundToInt(speed * 3.6f) + " km/h";
                Vector3 statusPosition =
                    physicsBlockedNow || physicsLaneLimitedNow
                        ? currentCollisionPosition
                        : drawFuturePrediction
                            ? futurePosition
                            : currentVisualPosition;

                Handles.Label(
                    statusPosition + Vector3.up * 0.55f,
                    "V" + vehicleIndex + "/" +
                    GetLaneName(displayLaneId) + " | " +
                    status
                );
            }

            bool canDrawObstacleSweep =
                authority &&
                playerSweepBlocked != null &&
                vehicleSweepBlocked != null &&
                roadBoundaryBlocked != null &&
                obstacleSweepDebugValid != null &&
                obstacleSweepStartPositions != null &&
                obstacleSweepEndPositions != null &&
                obstacleSweepStartRotations != null &&
                obstacleSweepEndRotations != null &&
                vehicleIndex < playerSweepBlocked.Length &&
                vehicleIndex < vehicleSweepBlocked.Length &&
                vehicleIndex < roadBoundaryBlocked.Length &&
                vehicleIndex < obstacleSweepDebugValid.Length &&
                vehicleIndex < obstacleSweepStartPositions.Length &&
                vehicleIndex < obstacleSweepEndPositions.Length &&
                vehicleIndex < obstacleSweepStartRotations.Length &&
                vehicleIndex < obstacleSweepEndRotations.Length &&
                obstacleSweepDebugValid[vehicleIndex];

            if (canDrawObstacleSweep)
            {
                bool blocked =
                    playerSweepBlocked[vehicleIndex] ||
                    vehicleSweepBlocked[vehicleIndex] ||
                    roadBoundaryBlocked[vehicleIndex];

                Color sweepColor = blocked
                    ? new Color(1f, 0.02f, 0.12f, 1f)
                    : new Color(1f, 0.75f, 0.05f, 0.9f);

                float playerSafetyExpansion = 2f * (
                    Mathf.Max(
                        0f,
                        manager.authorityObstacleSafetyMargin
                    ) +
                    Mathf.Max(
                        0f,
                        manager.laneChangePlayerSafetyMargin
                    )
                );

                float vehicleSafetyExpansion = 2f *
                    Mathf.Max(
                        0f,
                        manager.laneChangeVehicleSafetyMargin
                    );

                float safetyExpansion = Mathf.Max(
                    playerSafetyExpansion,
                    vehicleSafetyExpansion
                );

                DrawFootprint(
                    obstacleSweepEndPositions[vehicleIndex],
                    obstacleSweepEndRotations[vehicleIndex],
                    length + safetyExpansion,
                    width + safetyExpansion,
                    centerOffset,
                    sweepColor,
                    blocked,
                    !blocked
                );

                Handles.color = sweepColor;
                Handles.DrawDottedLine(
                    obstacleSweepStartPositions[vehicleIndex] +
                        Vector3.up * 0.36f,
                    obstacleSweepEndPositions[vehicleIndex] +
                        Vector3.up * 0.36f,
                    3f
                );

                if (blocked)
                {
                    string blockedStatus;

                    string blockedProgress = reversing[vehicleIndex]
                        ? GetRecoveryPhaseLabel(
                            manager,
                            progress[vehicleIndex]
                          )
                        : GetProgressQuarterLabel(
                            progress[vehicleIndex]
                          );

                    blockedStatus = "BLK(" + blockedProgress + ")";

                    Handles.Label(
                        obstacleSweepEndPositions[vehicleIndex] +
                            Vector3.up * 0.72f,
                        "V" + vehicleIndex + "/" +
                        GetLaneName(displayLaneId) + " | " +
                        blockedStatus
                    );
                }
            }

            bool canDrawPhysicsCast =
                authority &&
                physicsCastValid != null &&
                physicsCastBlocked != null &&
                physicsCastCenters != null &&
                physicsCastHalfExtents != null &&
                physicsCastDirections != null &&
                physicsCastRotations != null &&
                physicsCastDistances != null &&
                physicsCastHitDistances != null &&
                physicsCastLaneLimited != null &&
                vehicleIndex < physicsCastValid.Length &&
                vehicleIndex < physicsCastBlocked.Length &&
                vehicleIndex < physicsCastCenters.Length &&
                vehicleIndex < physicsCastHalfExtents.Length &&
                vehicleIndex < physicsCastDirections.Length &&
                vehicleIndex < physicsCastRotations.Length &&
                vehicleIndex < physicsCastDistances.Length &&
                vehicleIndex < physicsCastHitDistances.Length &&
                vehicleIndex < physicsCastLaneLimited.Length &&
                physicsCastValid[vehicleIndex];

            if (canDrawPhysicsCast)
            {
                DrawAuthorityPhysicsCast(
                    physicsCastCenters[vehicleIndex],
                    physicsCastHalfExtents[vehicleIndex],
                    physicsCastDirections[vehicleIndex],
                    physicsCastRotations[vehicleIndex],
                    physicsCastDistances[vehicleIndex],
                    physicsCastHitDistances[vehicleIndex],
                    physicsCastBlocked[vehicleIndex],
                    physicsCastLaneLimited[vehicleIndex],
                    Mathf.Max(
                        0.1f,
                        manager.authorityObstacleCastVerticalOffset
                    )
                );
            }
        }

        Handles.color = previousColor;
    }

    private static void DrawAuthorityPhysicsCast(
        Vector3 center,
        Vector3 halfExtents,
        Vector3 direction,
        Quaternion rotation,
        float castDistance,
        float hitDistance,
        bool blocked,
        bool laneLimited,
        float verticalOffset)
    {
        Color color = blocked
            ? new Color(1f, 0.3f, 0.05f, 0.95f)
            : laneLimited
                ? new Color(0.1f, 0.65f, 1f, 0.9f)
                : new Color(0.1f, 1f, 0.45f, 0.8f);
        float visibleDistance = blocked
            ? Mathf.Clamp(hitDistance, 0f, castDistance)
            : Mathf.Max(0f, castDistance);
        Vector3 normalizedDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : rotation * Vector3.forward;
        Vector3 endCenter = center +
            normalizedDirection * visibleDistance;
        Vector3 up = rotation * Vector3.up;

        if (up.sqrMagnitude <= 0.0001f)
        {
            up = Vector3.up;
        }
        else
        {
            up.Normalize();
        }

        Vector3 roadEndCenter = endCenter -
            up * Mathf.Max(0.1f, verticalOffset) +
            up * 0.04f;

        DrawPhysicsCastEndMarker(
            roadEndCenter,
            normalizedDirection,
            up,
            halfExtents.x,
            color
        );

        if (blocked || laneLimited)
        {
            DrawPhysicsBoxOutline(
                roadEndCenter,
                halfExtents,
                rotation,
                color
            );
        }
    }

    private static void DrawPhysicsCastEndMarker(
        Vector3 center,
        Vector3 direction,
        Vector3 up,
        float halfWidth,
        Color color)
    {
        Vector3 planarDirection = Vector3.ProjectOnPlane(
            direction,
            up
        );

        if (planarDirection.sqrMagnitude <= 0.0001f)
        {
            planarDirection = Vector3.forward;
        }
        else
        {
            planarDirection.Normalize();
        }

        Vector3 right = Vector3.Cross(up, planarDirection);

        if (right.sqrMagnitude <= 0.0001f)
        {
            right = Vector3.right;
        }
        else
        {
            right.Normalize();
        }

        float markerHalfWidth = Mathf.Max(0.25f, halfWidth);
        float markerDepth = Mathf.Clamp(
            markerHalfWidth * 0.32f,
            0.18f,
            0.45f
        );
        Vector3 left = center - right * markerHalfWidth;
        Vector3 rightPoint = center + right * markerHalfWidth;
        Vector3[] marker =
        {
            left - planarDirection * markerDepth,
            left,
            rightPoint,
            rightPoint - planarDirection * markerDepth
        };

        Handles.color = color;
        Handles.DrawAAPolyLine(3f, marker);
    }

    private static void DrawPhysicsBoxOutline(
        Vector3 center,
        Vector3 halfExtents,
        Quaternion rotation,
        Color color)
    {
        Vector3 forwardOffset =
            rotation * Vector3.forward * halfExtents.z;
        Vector3 sideOffset =
            rotation * Vector3.right * halfExtents.x;
        Vector3[] outline =
        {
            center + forwardOffset + sideOffset,
            center + forwardOffset - sideOffset,
            center - forwardOffset - sideOffset,
            center - forwardOffset + sideOffset,
            center + forwardOffset + sideOffset
        };

        Handles.color = color;
        Handles.DrawAAPolyLine(2f, outline);
    }

    private static void DrawFootprint(
        Vector3 position,
        Quaternion rotation,
        float length,
        float width,
        float centerOffset,
        Color color,
        bool filled,
        bool dotted)
    {
        Vector3 up = rotation * Vector3.up;
        Vector3 forward = rotation * Vector3.forward;
        Vector3 right = rotation * Vector3.right;

        Vector3 center = position +
            forward * centerOffset +
            up * 0.16f;
        Vector3 forwardOffset = forward * length * 0.5f;
        Vector3 sideOffset = right * width * 0.5f;

        Vector3[] corners =
        {
            center + forwardOffset + sideOffset,
            center + forwardOffset - sideOffset,
            center - forwardOffset - sideOffset,
            center - forwardOffset + sideOffset
        };

        if (filled)
        {
            Color fill = color;
            fill.a = 0.12f;
            Handles.color = fill;
            Handles.DrawAAConvexPolygon(corners);
        }

        Handles.color = color;

        if (dotted)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                Handles.DrawDottedLine(
                    corners[i],
                    corners[(i + 1) % corners.Length],
                    4f
                );
            }
        }
        else
        {
            Vector3[] outline =
            {
                corners[0],
                corners[1],
                corners[2],
                corners[3],
                corners[0]
            };

            Handles.DrawAAPolyLine(3f, outline);
        }
    }

    private static int FindLaneChangeRule(
        TrafficLaneDatabase database,
        int sourceLaneId,
        int targetLaneId)
    {
        if (sourceLaneId < 0 ||
            sourceLaneId >= database.laneCount)
        {
            return -1;
        }

        int first = database.laneRuleStarts[sourceLaneId];
        int count = database.laneRuleCounts[sourceLaneId];

        for (int i = first; i < first + count; i++)
        {
            if (database.changeToLaneIds[i] == targetLaneId)
            {
                return i;
            }
        }

        return -1;
    }

    private static float MapSourceToTargetS(
        TrafficLaneDatabase database,
        int ruleIndex,
        int targetLaneId,
        float sourceS)
    {
        Vector3 sourceStart = database.GetLanePosition(
            FindSourceLaneId(database, ruleIndex),
            database.changeStartS[ruleIndex],
            -1
        );

        Vector3 sourceEnd = database.GetLanePosition(
            FindSourceLaneId(database, ruleIndex),
            database.changeEndS[ruleIndex],
            -1
        );

        float targetStart = ProjectPositionToLaneS(
            database,
            targetLaneId,
            sourceStart
        );

        float targetEnd = ProjectPositionToLaneS(
            database,
            targetLaneId,
            sourceEnd
        );

        float sourceLength =
            database.changeEndS[ruleIndex] -
            database.changeStartS[ruleIndex];

        if (Mathf.Abs(sourceLength) < 0.001f)
        {
            return targetStart;
        }

        return Mathf.Clamp(
            targetStart +
            (sourceS - database.changeStartS[ruleIndex]) *
            (targetEnd - targetStart) / sourceLength,
            0f,
            database.laneLengths[targetLaneId]
        );
    }

    private static int FindSourceLaneId(
        TrafficLaneDatabase database,
        int ruleIndex)
    {
        for (int laneId = 0;
             laneId < database.laneCount;
             laneId++)
        {
            int first = database.laneRuleStarts[laneId];
            int count = database.laneRuleCounts[laneId];

            if (ruleIndex >= first &&
                ruleIndex < first + count)
            {
                return laneId;
            }
        }

        return 0;
    }

    private static float ProjectPositionToLaneS(
        TrafficLaneDatabase database,
        int laneId,
        Vector3 position)
    {
        int first = database.laneSampleStarts[laneId];
        int count = database.laneSampleCounts[laneId];
        float nearestDistanceSqr = float.MaxValue;
        float nearestS = 0f;

        for (int i = first; i < first + count - 1; i++)
        {
            Vector3 start = database.samplePositions[i];
            Vector3 end = database.samplePositions[i + 1];
            Vector3 segment = end - start;
            float lengthSqr = segment.sqrMagnitude;

            if (lengthSqr <= 0.0001f)
            {
                continue;
            }

            float t = Mathf.Clamp01(
                Vector3.Dot(position - start, segment) /
                lengthSqr
            );

            Vector3 closest = Vector3.Lerp(start, end, t);
            float distanceSqr =
                (position - closest).sqrMagnitude;

            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            nearestS = Mathf.Lerp(
                database.sampleDistances[i],
                database.sampleDistances[i + 1],
                t
            );
        }

        return nearestS;
    }

    private static float GetVisualProgress(
        TrafficSimulationManager manager,
        bool reverseManeuver,
        float progress,
        float recoveryDistance,
        float laneSeparation,
        float vehicleLength)
    {
        if (!reverseManeuver)
        {
            return SmoothProgress01(progress);
        }

        float preparationEnd = GetRecoveryPreparationEnd(manager);
        Vector3 preparationPose = GetRecoveryKinematicLocalPose(
            manager,
            Mathf.Min(progress, preparationEnd),
            recoveryDistance,
            laneSeparation,
            vehicleLength
        );

        if (progress < preparationEnd)
        {
            return preparationPose.y /
                Mathf.Max(0.5f, laneSeparation);
        }

        float safeLaneSeparation = Mathf.Max(0.5f, laneSeparation);
        float startLateral =
            preparationPose.y / safeLaneSeparation;
        float finalTravelDistance =
            GetRecoveryFinalTravelDistance(
                manager,
                preparationPose,
                safeLaneSeparation
            );
        float startTangent =
            Mathf.Tan(preparationPose.z) *
            finalTravelDistance / safeLaneSeparation;
        float u = Mathf.InverseLerp(
            preparationEnd,
            1f,
            progress
        );
        float u2 = u * u;
        float u3 = u2 * u;

        return Mathf.Clamp(
            (2f * u3 - 3f * u2 + 1f) * startLateral +
            (u3 - 2f * u2 + u) * startTangent +
            (-2f * u3 + 3f * u2),
            -0.5f,
            1.1f
        );
    }

    private static float GetRecoveryFirstReverseEnd(
        TrafficSimulationManager manager)
    {
        return Mathf.Clamp(
            manager.reversePhaseFraction,
            0.2f,
            0.35f
        );
    }

    private static float GetRecoveryPreparationEnd(
        TrafficSimulationManager manager)
    {
        return Mathf.Min(
            0.7f,
            GetRecoveryFirstReverseEnd(manager) + 0.25f
        );
    }

    private static float GetRecoveryFinalTravelDistance(
        TrafficSimulationManager manager,
        Vector3 preparationPose,
        float laneSeparation)
    {
        float configuredDistance = Mathf.Max(
            8f,
            manager.laneChangeMinimumTravelDistance
        );
        float safeLaneWidth = Mathf.Max(0.5f, laneSeparation);
        float headingTangent = Mathf.Abs(
            Mathf.Tan(preparationPose.z)
        );

        if (headingTangent <= 0.001f)
        {
            return configuredDistance;
        }

        float startLateral =
            preparationPose.y / safeLaneWidth;
        float remainingLateral = Mathf.Max(
            0.25f,
            1f - startLateral
        );
        float maximumNormalizedTangent = Mathf.Min(
            2.5f,
            2.4f * remainingLateral
        );

        return Mathf.Clamp(
            maximumNormalizedTangent *
            safeLaneWidth /
            headingTangent,
            8f,
            configuredDistance
        );
    }

    private static string GetRecoveryPhaseLabel(
        TrafficSimulationManager manager,
        float progress)
    {
        float preparationEnd = Mathf.Max(
            0.001f,
            GetRecoveryPreparationEnd(manager)
        );
        int percentage = Mathf.Clamp(
            Mathf.CeilToInt(
                Mathf.Clamp01(progress / preparationEnd) * 4f
            ) * 25,
            25,
            100
        );

        return percentage + "%";
    }

    private static string GetRecoveryStateLabel(
        TrafficSimulationManager manager,
        float progress)
    {
        float preparationEnd = Mathf.Max(
            0.001f,
            GetRecoveryPreparationEnd(manager)
        );

        if (progress <= preparationEnd + 0.0001f)
        {
            return "REG(" +
                GetRecoveryPhaseLabel(manager, progress) +
                ")";
        }

        return "MRG(" +
            GetProgressQuarterLabel(
                Mathf.InverseLerp(
                    preparationEnd,
                    1f,
                    progress
                )
            ) +
            ")";
    }

    private static string GetProgressQuarterLabel(float progress)
    {
        int percentage = Mathf.Clamp(
            Mathf.CeilToInt(Mathf.Clamp01(progress) * 4f) * 25,
            25,
            100
        );

        return percentage + "%";
    }

    private static bool IsRecoveryReversePhase(
        TrafficSimulationManager manager,
        bool reverseManeuver,
        float progress)
    {
        return reverseManeuver &&
            progress < GetRecoveryFirstReverseEnd(manager) - 0.0001f;
    }

    private static float AdvanceCachedManeuverProgress(
        float[] pathDistances,
        int pathOffset,
        float progress,
        float travelDistance,
        float maximumProgress)
    {
        float clampedProgress = Mathf.Clamp(
            progress,
            0f,
            maximumProgress
        );
        float coordinate = clampedProgress *
            (ManeuverPathSampleCount - 1);
        int sample = Mathf.Clamp(
            Mathf.FloorToInt(coordinate),
            0,
            ManeuverPathSampleCount - 2
        );
        float interpolation = coordinate - sample;
        float currentDistance = Mathf.Lerp(
            pathDistances[pathOffset + sample],
            pathDistances[pathOffset + sample + 1],
            interpolation
        );
        float maximumCoordinate = Mathf.Clamp01(maximumProgress) *
            (ManeuverPathSampleCount - 1);
        int maximumSample = Mathf.Clamp(
            Mathf.FloorToInt(maximumCoordinate),
            0,
            ManeuverPathSampleCount - 2
        );
        float maximumInterpolation = maximumCoordinate - maximumSample;
        float maximumDistance = Mathf.Lerp(
            pathDistances[pathOffset + maximumSample],
            pathDistances[pathOffset + maximumSample + 1],
            maximumInterpolation
        );
        float targetDistance = Mathf.Min(
            maximumDistance,
            currentDistance + Mathf.Max(0f, travelDistance)
        );

        while (sample < ManeuverPathSampleCount - 2 &&
               pathDistances[pathOffset + sample + 1] < targetDistance)
        {
            sample++;
        }

        float segmentStart = pathDistances[pathOffset + sample];
        float segmentEnd = pathDistances[pathOffset + sample + 1];
        float segmentProgress = segmentEnd > segmentStart + 0.0001f
            ? Mathf.InverseLerp(
                segmentStart,
                segmentEnd,
                targetDistance
              )
            : 0f;

        return Mathf.Min(
            maximumProgress,
            (sample + segmentProgress) /
                (ManeuverPathSampleCount - 1)
        );
    }

    private static float GetRecoveryLongitudinalOffset(
        TrafficSimulationManager manager,
        float progress,
        float recoveryDistance,
        float laneSeparation,
        float vehicleLength)
    {
        return GetRecoveryKinematicLocalPose(
            manager,
            progress,
            recoveryDistance,
            laneSeparation,
            vehicleLength
        ).x;
    }

    private static Vector3 GetRecoveryKinematicLocalPose(
        TrafficSimulationManager manager,
        float progress,
        float recoveryDistance,
        float laneSeparation,
        float vehicleLength)
    {
        float distance = Mathf.Max(0.6f, recoveryDistance);
        float reverseOneEnd = GetRecoveryFirstReverseEnd(manager);
        float preparationEnd = GetRecoveryPreparationEnd(manager);
        float primaryCurvature = GetRecoveryPrimaryCurvature(
            manager,
            distance,
            laneSeparation,
            vehicleLength
        );
        Vector3 pose = Vector3.zero;
        float phaseRatio = SmoothProgress01(
            Mathf.InverseLerp(0f, reverseOneEnd, progress)
        );

        if (progress < reverseOneEnd)
        {
            return AdvanceRecoveryKinematicPose(
                pose,
                -distance * phaseRatio,
                -primaryCurvature
            );
        }

        pose = AdvanceRecoveryKinematicPose(
            pose,
            -distance,
            -primaryCurvature
        );

        phaseRatio = SmoothRecoveryForwardExit01(
            Mathf.InverseLerp(
                reverseOneEnd,
                preparationEnd,
                progress
            )
        );

        return AdvanceRecoveryKinematicPose(
            pose,
            distance * 0.65f * phaseRatio,
            primaryCurvature
        );
    }

    private static float SmoothRecoveryForwardExit01(float progress)
    {
        float t = Mathf.Clamp01(progress);

        return t * t * (2f - t);
    }

    private static Vector3 AdvanceRecoveryKinematicPose(
        Vector3 pose,
        float signedDistance,
        float curvature)
    {
        float heading = pose.z;

        if (Mathf.Abs(curvature) <= 0.0001f)
        {
            pose.x += signedDistance * Mathf.Cos(heading);
            pose.y += signedDistance * Mathf.Sin(heading);
            return pose;
        }

        float nextHeading = heading + curvature * signedDistance;
        pose.x +=
            (Mathf.Sin(nextHeading) - Mathf.Sin(heading)) /
            curvature;
        pose.y +=
            (-Mathf.Cos(nextHeading) + Mathf.Cos(heading)) /
            curvature;
        pose.z = nextHeading;
        return pose;
    }

    private static float GetRecoveryPrimaryCurvature(
        TrafficSimulationManager manager,
        float recoveryDistance,
        float laneSeparation,
        float vehicleLength)
    {
        float distance = Mathf.Max(0.6f, recoveryDistance);
        float desiredLateralDistance = Mathf.Clamp(
            manager.reverseLateralProgress,
            0.05f,
            0.25f
        ) * Mathf.Max(0.5f, laneSeparation);
        float desiredCurvature =
            2f * desiredLateralDistance /
            Mathf.Max(0.36f, distance * distance);
        float wheelBase = Mathf.Max(2.2f, vehicleLength * 0.55f);
        float maximumCurvature = Mathf.Tan(
            Mathf.Deg2Rad * Mathf.Clamp(
                manager.blockedRecoveryMaximumSteeringAngle,
                20f,
                40f
            )
        ) / wheelBase;
        float maximumBodyCurvature =
            Mathf.Deg2Rad * Mathf.Clamp(
                manager.blockedRecoveryMaximumBodyAngle,
                18f,
                38f
            ) /
            Mathf.Max(0.1f, distance * 1.65f);

        return Mathf.Clamp(
            desiredCurvature,
            0.02f,
            Mathf.Max(
                0.02f,
                Mathf.Min(
                    maximumCurvature,
                    maximumBodyCurvature
                )
            )
        );
    }

    private static float SmoothProgress01(float progress)
    {
        float t = Mathf.Clamp01(progress);
        return t * t * t *
            (t * (t * 6f - 15f) + 10f);
    }

    private static string GetLaneName(int laneId)
    {
        switch (laneId)
        {
            case TrafficLaneDatabase.LaneL1: return "L1";
            case TrafficLaneDatabase.LaneL2: return "L2";
            case TrafficLaneDatabase.LaneL3: return "L3";
            case TrafficLaneDatabase.LaneR1: return "R1";
            case TrafficLaneDatabase.LaneR2: return "R2";
            case TrafficLaneDatabase.LaneR3: return "R3";
            case TrafficLaneDatabase.LaneR4Branch: return "R4";
            default: return "?";
        }
    }

    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    private static T GetRuntimeValue<T>(
        TrafficSimulationManager manager,
        string variableName,
        T fallback)
    {
        if (!Application.isPlaying)
        {
            return fallback;
        }

        UdonBehaviour backingBehaviour =
            UdonSharpEditorUtility.GetBackingUdonBehaviour(
                manager
            );

        if (backingBehaviour == null ||
            !backingBehaviour.IsInitialized)
        {
            return fallback;
        }

        object value = backingBehaviour.GetProgramVariable(
            variableName
        );

        if (value is T typedValue)
        {
            return typedValue;
        }

        return fallback;
    }
}
