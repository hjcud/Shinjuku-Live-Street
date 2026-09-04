using System;
using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 도로 Mesh와 배치 Marker에서 차선 샘플 및 차선 변경 규칙을 생성하고 Scene에 미리 표시한다.
/// </summary>
public class TrafficLaneBakerEditor : EditorWindow
{
    private const int RoadLayer = 22;
    private const int SignalGroupMain = 0;

    private static readonly string[] LaneNames =
    {
        "Lane_L1",
        "Lane_L2",
        "Lane_L3",
        "Lane_R1",
        "Lane_R2",
        "Lane_R3",
        "Lane_R4_Branch"
    };

    private static readonly string[] SpawnMarkerNames =
    {
        "Spawn_L1",
        "Spawn_L2",
        "Spawn_L3",
        "Spawn_R1",
        "Spawn_R2",
        "Spawn_R3"
    };

    private static readonly string[] DespawnMarkerNames =
    {
        "Despawn_L1",
        "Despawn_L2",
        "Despawn_L3",
        "Despawn_R1",
        "Despawn_R2",
        "Despawn_R3",
        "Despawn_R4_Branch"
    };

    private static readonly float[] DefaultSpawnWeights =
    {
        0.25f,
        1f,
        1f,
        1f,
        1f,
        0.2f,
        0f
    };
    
    private static readonly Color[] LaneColors =
    {
        new Color(0.10f, 0.65f, 1.00f), // L1
        new Color(0.10f, 1.00f, 0.85f), // L2
        new Color(0.35f, 1.00f, 0.25f), // L3
        new Color(1.00f, 0.25f, 0.20f), // R1
        new Color(1.00f, 0.60f, 0.10f), // R2
        new Color(1.00f, 0.20f, 0.75f), // R3
        new Color(0.65f, 0.25f, 1.00f)  // R4 Branch
    };

    private Transform trafficRoot;
    private TrafficLaneDatabase database;

    private float sampleSpacing = 2f;
    private float rayStartHeight = 5f;
    private float raycastDepth = 15f;
    private float markerWarningDistance = 4f;
    private float defaultSpeedLimit = 8f;
    
    private bool showBakedPreview = true;
    private bool showSamplePoints = true;
    private bool showDirections = true;
    private bool showDataMarkers = true;
    private bool showChangeZones = true;

    private float previewPointSize = 0.15f;
    private float previewArrowSize = 1.2f;
    private int directionSampleStride = 10;

    private Vector2 scrollPosition;
    private string lastReport = "아직 베이크하지 않았습니다.";
    private MessageType lastReportType = MessageType.Info;

    private class LaneData
    {
        public string name;
        public Vector3[] positions;
        public Quaternion[] rotations;
        public float[] distances;
        public float length;
    }

    private class MarkerProjection
    {
        public float laneS;
        public float distance;
    }

    private class ChangeRule
    {
        public int fromLaneId;
        public int toLaneId;
        public float startS;
        public float endS;
        public int vehicleMask;
    }

    [MenuItem("Tools/Traffic System V2/Lane Baker")]
    public static void OpenWindow()
    {
        TrafficLaneBakerEditor window =
            GetWindow<TrafficLaneBakerEditor>();

        window.titleContent = new GUIContent("Traffic Lane Baker");
        window.minSize = new Vector2(430f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui -= DrawBakedPreview;
        SceneView.duringSceneGui += DrawBakedPreview;

        AutoFindObjects();
        SceneView.RepaintAll();
    }
    
    private void OnDisable()
    {
        SceneView.duringSceneGui -= DrawBakedPreview;
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(
            scrollPosition
        );

        EditorGUILayout.LabelField(
            "Traffic System V2 Lane Baker",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "TEST.unity의 LaneSources와 Markers를 읽어 " +
            "Layer 22 CarRoad 표면에 차선 데이터를 베이크합니다.",
            MessageType.Info
        );

        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();

        trafficRoot = (Transform)EditorGUILayout.ObjectField(
            "Traffic Root",
            trafficRoot,
            typeof(Transform),
            true
        );

        if (EditorGUI.EndChangeCheck())
        {
            FindDatabaseUnderRoot();
        }

        database =
            (TrafficLaneDatabase)EditorGUILayout.ObjectField(
                "Lane Database",
                database,
                typeof(TrafficLaneDatabase),
                true
            );

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Auto Find"))
        {
            AutoFindObjects();
        }

        EditorGUI.BeginDisabledGroup(
            trafficRoot == null || database != null
        );

        if (GUILayout.Button("Create Database"))
        {
            CreateDatabase();
        }

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Bake Settings",
            EditorStyles.boldLabel
        );

        sampleSpacing = Mathf.Max(
            0.25f,
            EditorGUILayout.FloatField(
                "Sample Spacing (m)",
                sampleSpacing
            )
        );

        rayStartHeight = Mathf.Max(
            0.1f,
            EditorGUILayout.FloatField(
                "Ray Start Height (m)",
                rayStartHeight
            )
        );

        raycastDepth = Mathf.Max(
            0.1f,
            EditorGUILayout.FloatField(
                "Raycast Depth (m)",
                raycastDepth
            )
        );

        markerWarningDistance = Mathf.Max(
            0.1f,
            EditorGUILayout.FloatField(
                "Marker Warning Distance",
                markerWarningDistance
            )
        );

        defaultSpeedLimit = Mathf.Max(
            0.1f,
            EditorGUILayout.FloatField(
                "Default Speed Limit (m/s)",
                defaultSpeedLimit
            )
        );

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(
            EditorApplication.isPlaying ||
            trafficRoot == null ||
            database == null
        );

        if (GUILayout.Button("Bake Lane Database", GUILayout.Height(36f)))
        {
            Bake();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(lastReport, lastReportType);

        if (database != null && database.IsReady())
        {
            EditorGUILayout.LabelField(
                "Current Sample Count",
                database.samplePositions.Length.ToString()
            );
        }

        EditorGUILayout.EndScrollView();
    }

    private void AutoFindObjects()
    {
        trafficRoot = FindTransformInLoadedScenes(
            "TrafficSystem_V2"
        );

        FindDatabaseUnderRoot();

        if (trafficRoot == null)
        {
            SetReport(
                "TrafficSystem_V2를 찾지 못했습니다.",
                MessageType.Warning
            );
        }
    }

    private void FindDatabaseUnderRoot()
    {
        database = null;

        if (trafficRoot != null)
        {
            database = trafficRoot.GetComponentInChildren
                <TrafficLaneDatabase>(true);
        }
    }

    private void CreateDatabase()
    {
        if (trafficRoot == null)
        {
            return;
        }

        Transform runtime = trafficRoot.Find("Runtime");

        if (runtime == null)
        {
            SetReport(
                "TrafficSystem_V2/Runtime을 찾지 못했습니다.",
                MessageType.Error
            );

            return;
        }

        database = UdonSharpUndo
            .AddComponent<TrafficLaneDatabase>(
                runtime.gameObject
            );

        Selection.activeObject = database;
        EditorSceneManager.MarkSceneDirty(
            runtime.gameObject.scene
        );

        SetReport(
            "Runtime에 TrafficLaneDatabase를 생성했습니다.",
            MessageType.Info
        );
    }

    private void Bake()
    {
        try
        {
            if (LayerMask.LayerToName(RoadLayer) != "CarRoad")
            {
                throw new InvalidOperationException(
                    "Layer 22의 이름이 CarRoad가 아닙니다."
                );
            }

            Transform laneSources = RequirePath(
                trafficRoot,
                "LaneSources"
            );

            Transform laneChangeZones = RequirePath(
                trafficRoot,
                "Markers/LaneChangeZones"
            );

            Transform stopLines = RequirePath(
                trafficRoot,
                "Markers/StopLines"
            );

            Transform spawnPoints = RequirePath(
                trafficRoot,
                "Markers/SpawnPoints"
            );

            Transform despawnPoints = RequirePath(
                trafficRoot,
                "Markers/DespawnPoints"
            );

            Physics.SyncTransforms();

            List<string> warnings = new List<string>();
            LaneData[] lanes =
                new LaneData[TrafficLaneDatabase.FixedLaneCount];

            for (int laneId = 0;
                 laneId < TrafficLaneDatabase.FixedLaneCount;
                 laneId++)
            {
                Transform laneSource = RequireChild(
                    laneSources,
                    LaneNames[laneId]
                );

                lanes[laneId] = BakeLane(
                    laneSource,
                    warnings
                );
            }

            float[] spawnS = BakeSpawnPoints(
                lanes,
                spawnPoints,
                warnings
            );

            float[] despawnS = BakeDespawnPoints(
                lanes,
                despawnPoints,
                warnings
            );

            float[] stopLineS = BakeStopLines(
                lanes,
                stopLines,
                warnings
            );

            List<ChangeRule> rules = BakeChangeRules(
                lanes,
                laneChangeZones,
                warnings
            );

            ApplyBake(
                lanes,
                spawnS,
                despawnS,
                stopLineS,
                rules
            );

            SceneView.RepaintAll();

            int sampleCount = database.samplePositions.Length;

            string report =
                "베이크 완료\n" +
                "차선: " + lanes.Length + "\n" +
                "샘플: " + sampleCount + "\n" +
                "차선 변경 규칙: " + rules.Count;

            if (warnings.Count > 0)
            {
                report += "\n\n경고:\n- " +
                          string.Join(
                              "\n- ",
                              warnings.ToArray()
                          );
            }

            SetReport(
                report,
                warnings.Count > 0
                    ? MessageType.Warning
                    : MessageType.Info
            );

        }
        catch (Exception exception)
        {
            SetReport(
                "베이크 실패\n" + exception.Message,
                MessageType.Error
            );
        }
    }

    private LaneData BakeLane(
        Transform laneSource,
        List<string> warnings)
    {
        if (laneSource.childCount < 2)
        {
            throw new InvalidOperationException(
                laneSource.name +
                "에는 최소 2개의 자식 웨이포인트가 필요합니다."
            );
        }

        List<Vector3> controlPoints = new List<Vector3>();

        for (int i = 0; i < laneSource.childCount; i++)
        {
            Vector3 position =
                laneSource.GetChild(i).position;

            if (i > 0 &&
                Vector3.Distance(
                    controlPoints[i - 1],
                    position
                ) < 0.05f)
            {
                throw new InvalidOperationException(
                    laneSource.name +
                    "에 서로 겹친 웨이포인트가 있습니다. Index: " +
                    i
                );
            }

            controlPoints.Add(position);
        }

        List<Vector3> denseCurve =
            BuildDenseCurve(controlPoints);

        List<Vector3> resampledCurve =
            ResampleCurve(denseCurve, sampleSpacing);

        if (resampledCurve.Count < 2)
        {
            throw new InvalidOperationException(
                laneSource.name +
                "의 베이크 샘플이 부족합니다."
            );
        }

        Vector3[] positions =
            new Vector3[resampledCurve.Count];

        Vector3[] normals =
            new Vector3[resampledCurve.Count];

        int roadMask = 1 << RoadLayer;

        for (int i = 0; i < resampledCurve.Count; i++)
        {
            Vector3 rayOrigin =
                resampledCurve[i] +
                Vector3.up * rayStartHeight;

            RaycastHit hit;

            bool hitRoad = Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out hit,
                rayStartHeight + raycastDepth,
                roadMask,
                QueryTriggerInteraction.Ignore
            );

            if (!hitRoad)
            {
                throw new InvalidOperationException(
                    laneSource.name +
                    "의 노면 Raycast가 실패했습니다. Sample: " +
                    i +
                    ", Position: " +
                    resampledCurve[i]
                );
            }

            positions[i] = hit.point;
            normals[i] = hit.normal.normalized;
        }

        float[] distances =
            BuildCumulativeDistances(positions);

        Quaternion[] rotations = BuildRotations(
            laneSource.name,
            positions,
            normals,
            warnings
        );

        LaneData lane = new LaneData();
        lane.name = laneSource.name;
        lane.positions = positions;
        lane.rotations = rotations;
        lane.distances = distances;
        lane.length = distances[distances.Length - 1];

        if (lane.length <= 0.1f)
        {
            throw new InvalidOperationException(
                laneSource.name +
                "의 최종 길이가 올바르지 않습니다."
            );
        }

        return lane;
    }

    private List<Vector3> BuildDenseCurve(
        List<Vector3> controls)
    {
        List<Vector3> result = new List<Vector3>();
        result.Add(controls[0]);

        for (int segmentIndex = 0;
             segmentIndex < controls.Count - 1;
             segmentIndex++)
        {
            Vector3 p1 = controls[segmentIndex];
            Vector3 p2 = controls[segmentIndex + 1];

            Vector3 p0 = segmentIndex > 0
                ? controls[segmentIndex - 1]
                : p1 + (p1 - p2);

            Vector3 p3 =
                segmentIndex + 2 < controls.Count
                    ? controls[segmentIndex + 2]
                    : p2 + (p2 - p1);

            float chordLength = Vector3.Distance(p1, p2);

            int subdivisions = Mathf.Max(
                4,
                Mathf.CeilToInt(chordLength / 0.25f)
            );

            for (int step = 1;
                 step <= subdivisions;
                 step++)
            {
                float t = step / (float)subdivisions;

                result.Add(
                    EvaluateCentripetalCatmullRom(
                        p0,
                        p1,
                        p2,
                        p3,
                        t
                    )
                );
            }
        }

        return result;
    }

    private Vector3 EvaluateCentripetalCatmullRom(
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        float normalizedT)
    {
        float t0 = 0f;
        float t1 = t0 + GetKnotInterval(p0, p1);
        float t2 = t1 + GetKnotInterval(p1, p2);
        float t3 = t2 + GetKnotInterval(p2, p3);

        float t = Mathf.Lerp(t1, t2, normalizedT);

        Vector3 a1 = InterpolateKnot(
            p0, p1, t0, t1, t
        );

        Vector3 a2 = InterpolateKnot(
            p1, p2, t1, t2, t
        );

        Vector3 a3 = InterpolateKnot(
            p2, p3, t2, t3, t
        );

        Vector3 b1 = InterpolateKnot(
            a1, a2, t0, t2, t
        );

        Vector3 b2 = InterpolateKnot(
            a2, a3, t1, t3, t
        );

        return InterpolateKnot(
            b1, b2, t1, t2, t
        );
    }

    private float GetKnotInterval(
        Vector3 start,
        Vector3 end)
    {
        return Mathf.Max(
            0.0001f,
            Mathf.Sqrt(Vector3.Distance(start, end))
        );
    }

    private Vector3 InterpolateKnot(
        Vector3 startValue,
        Vector3 endValue,
        float startTime,
        float endTime,
        float currentTime)
    {
        float duration = endTime - startTime;

        if (duration <= 0.000001f)
        {
            return startValue;
        }

        float ratio =
            (currentTime - startTime) / duration;

        return Vector3.LerpUnclamped(
            startValue,
            endValue,
            ratio
        );
    }

    private List<Vector3> ResampleCurve(
        List<Vector3> denseCurve,
        float spacing)
    {
        float[] distances =
            BuildCumulativeDistances(denseCurve.ToArray());

        float totalLength = distances[distances.Length - 1];

        if (totalLength <= 0.1f)
        {
            throw new InvalidOperationException(
                "보간된 경로의 길이가 너무 짧습니다."
            );
        }

        List<Vector3> result = new List<Vector3>();
        int segmentIndex = 0;

        for (float targetS = 0f;
             targetS < totalLength;
             targetS += spacing)
        {
            while (segmentIndex < distances.Length - 2 &&
                   distances[segmentIndex + 1] < targetS)
            {
                segmentIndex++;
            }

            float startS = distances[segmentIndex];
            float endS = distances[segmentIndex + 1];
            float segmentLength = endS - startS;

            float t = segmentLength > 0.0001f
                ? Mathf.Clamp01(
                    (targetS - startS) / segmentLength
                )
                : 0f;

            result.Add(
                Vector3.Lerp(
                    denseCurve[segmentIndex],
                    denseCurve[segmentIndex + 1],
                    t
                )
            );
        }

        Vector3 finalPosition =
            denseCurve[denseCurve.Count - 1];

        if (result.Count == 0 ||
            Vector3.Distance(
                result[result.Count - 1],
                finalPosition
            ) > 0.01f)
        {
            result.Add(finalPosition);
        }

        return result;
    }

    private float[] BuildCumulativeDistances(
        Vector3[] positions)
    {
        float[] distances = new float[positions.Length];
        distances[0] = 0f;

        for (int i = 1; i < positions.Length; i++)
        {
            distances[i] =
                distances[i - 1] +
                Vector3.Distance(
                    positions[i - 1],
                    positions[i]
                );
        }

        return distances;
    }

    private Quaternion[] BuildRotations(
        string laneName,
        Vector3[] positions,
        Vector3[] normals,
        List<string> warnings)
    {
        Quaternion[] rotations =
            new Quaternion[positions.Length];

        Vector3 previousForward = Vector3.zero;
        bool directionWarningAdded = false;
        bool slopeWarningAdded = false;

        for (int i = 0; i < positions.Length; i++)
        {
            int previousIndex = Mathf.Max(0, i - 1);
            int nextIndex = Mathf.Min(
                positions.Length - 1,
                i + 1
            );

            Vector3 tangent =
                positions[nextIndex] -
                positions[previousIndex];

            Vector3 normal = normals[i];

            if (i > 0)
            {
                normal += normals[i - 1];
            }

            if (i < normals.Length - 1)
            {
                normal += normals[i + 1];
            }

            normal.Normalize();

            Vector3 forward =
                Vector3.ProjectOnPlane(
                    tangent,
                    normal
                ).normalized;

            if (forward.sqrMagnitude < 0.0001f)
            {
                throw new InvalidOperationException(
                    laneName +
                    "의 진행 방향을 계산할 수 없습니다. Sample: " +
                    i
                );
            }

            if (previousForward.sqrMagnitude > 0.1f)
            {
                float dot = Vector3.Dot(
                    previousForward,
                    forward
                );

                if (dot < 0f)
                {
                    throw new InvalidOperationException(
                        laneName +
                        "의 진행 방향이 반대로 뒤집혔습니다. Sample: " +
                        i
                    );
                }

                if (!directionWarningAdded &&
                    Vector3.Angle(
                        previousForward,
                        forward
                    ) > 25f)
                {
                    warnings.Add(
                        laneName +
                        "에 급격한 방향 변화가 있습니다."
                    );

                    directionWarningAdded = true;
                }
            }

            if (!slopeWarningAdded &&
                Vector3.Angle(
                    normal,
                    Vector3.up
                ) > 20f)
            {
                warnings.Add(
                    laneName +
                    "에 경사가 큰 노면 샘플이 있습니다."
                );

                slopeWarningAdded = true;
            }

            rotations[i] = Quaternion.LookRotation(
                forward,
                normal
            );

            previousForward = forward;
        }

        return rotations;
    }

    private float[] BakeSpawnPoints(
        LaneData[] lanes,
        Transform container,
        List<string> warnings)
    {
        float[] result =
        {
            -1f, -1f, -1f, -1f, -1f, -1f, -1f
        };

        for (int laneId = 0;
             laneId < SpawnMarkerNames.Length;
             laneId++)
        {
            Transform marker = RequireChild(
                container,
                SpawnMarkerNames[laneId]
            );

            result[laneId] = ProjectMarker(
                marker,
                lanes[laneId],
                warnings
            ).laneS;
        }

        // R4_BRANCH는 합류 전용 차선이므로 차량을 직접 생성하지 않는다.
        result[TrafficLaneDatabase.LaneR4Branch] = -1f;

        return result;
    }

    private float[] BakeDespawnPoints(
        LaneData[] lanes,
        Transform container,
        List<string> warnings)
    {
        float[] result =
            new float[TrafficLaneDatabase.FixedLaneCount];

        for (int laneId = 0;
             laneId < DespawnMarkerNames.Length;
             laneId++)
        {
            Transform marker = RequireChild(
                container,
                DespawnMarkerNames[laneId]
            );

            result[laneId] = ProjectMarker(
                marker,
                lanes[laneId],
                warnings
            ).laneS;
        }

        return result;
    }

    private float[] BakeStopLines(
        LaneData[] lanes,
        Transform container,
        List<string> warnings)
    {
        float[] result =
        {
            -1f, -1f, -1f, -1f, -1f, -1f, -1f
        };

        Transform stopLineL = RequireChild(
            container,
            "StopLine_L"
        );

        Transform stopLineR = RequireChild(
            container,
            "StopLine_R"
        );

        for (int laneId = TrafficLaneDatabase.LaneL1;
             laneId <= TrafficLaneDatabase.LaneL3;
             laneId++)
        {
            result[laneId] = ProjectMarker(
                stopLineL,
                lanes[laneId],
                warnings
            ).laneS;
        }

        for (int laneId = TrafficLaneDatabase.LaneR1;
             laneId <= TrafficLaneDatabase.LaneR3;
             laneId++)
        {
            result[laneId] = ProjectMarker(
                stopLineR,
                lanes[laneId],
                warnings
            ).laneS;
        }

        result[TrafficLaneDatabase.LaneR4Branch] = -1f;

        return result;
    }

    private List<ChangeRule> BakeChangeRules(
        LaneData[] lanes,
        Transform container,
        List<string> warnings)
    {
        List<ChangeRule> rules =
            new List<ChangeRule>();

        AddChangeRule(
            rules, lanes, container,
            TrafficLaneDatabase.LaneL1,
            TrafficLaneDatabase.LaneL2,
            "LC_L1_L2_Start",
            "LC_L1_L2_End",
            warnings
        );

        AddChangeRule(
            rules, lanes, container,
            TrafficLaneDatabase.LaneL2,
            TrafficLaneDatabase.LaneL1,
            "LC_L1_L2_Start",
            "LC_L1_L2_End",
            warnings
        );

        AddChangeRule(
            rules, lanes, container,
            TrafficLaneDatabase.LaneL2,
            TrafficLaneDatabase.LaneL3,
            "LC_L2_L3_Start",
            "LC_L2_L3_End",
            warnings
        );

        AddChangeRule(
            rules, lanes, container,
            TrafficLaneDatabase.LaneL3,
            TrafficLaneDatabase.LaneL2,
            "LC_L2_L3_Start",
            "LC_L2_L3_End",
            warnings
        );

        AddChangeRule(
            rules, lanes, container,
            TrafficLaneDatabase.LaneR1,
            TrafficLaneDatabase.LaneR2,
            "LC_R1_R2_Start",
            "LC_R1_R2_End",
            warnings
        );

        AddChangeRule(
            rules, lanes, container,
            TrafficLaneDatabase.LaneR2,
            TrafficLaneDatabase.LaneR1,
            "LC_R1_R2_Start",
            "LC_R1_R2_End",
            warnings
        );

        AddChangeRule(
            rules, lanes, container,
            TrafficLaneDatabase.LaneR2,
            TrafficLaneDatabase.LaneR3,
            "LC_R2_R3_Start",
            "LC_R2_R3_End",
            warnings
        );

        AddChangeRule(
            rules, lanes, container,
            TrafficLaneDatabase.LaneR3,
            TrafficLaneDatabase.LaneR2,
            "LC_R2_R3_Start",
            "LC_R2_R3_End",
            warnings
        );

        AddChangeRule(
            rules, lanes, container,
            TrafficLaneDatabase.LaneR3,
            TrafficLaneDatabase.LaneR4Branch,
            "LC_R3_R4_Start",
            "LC_R3_R4_End",
            warnings
        );

        return rules;
    }

    private void AddChangeRule(
        List<ChangeRule> rules,
        LaneData[] lanes,
        Transform container,
        int fromLaneId,
        int toLaneId,
        string startMarkerName,
        string endMarkerName,
        List<string> warnings)
    {
        Transform startMarker = RequireChild(
            container,
            startMarkerName
        );

        Transform endMarker = RequireChild(
            container,
            endMarkerName
        );

        float startS = ProjectMarker(
            startMarker,
            lanes[fromLaneId],
            warnings
        ).laneS;

        float endS = ProjectMarker(
            endMarker,
            lanes[fromLaneId],
            warnings
        ).laneS;

        if (startS >= endS)
        {
            throw new InvalidOperationException(
                LaneNames[fromLaneId] +
                " → " +
                LaneNames[toLaneId] +
                " 차선 변경 마커 순서가 잘못되었습니다. " +
                "Start S: " + startS.ToString("F2") +
                ", End S: " + endS.ToString("F2")
            );
        }

        if (endS - startS < 20f)
        {
            warnings.Add(
                LaneNames[fromLaneId] +
                " → " +
                LaneNames[toLaneId] +
                " 차선 변경 구간이 20m보다 짧습니다."
            );
        }

        ChangeRule rule = new ChangeRule();
        rule.fromLaneId = fromLaneId;
        rule.toLaneId = toLaneId;
        rule.startS = startS;
        rule.endS = endS;
        rule.vehicleMask = TrafficLaneDatabase.VehicleCar;

        rules.Add(rule);
    }

    private MarkerProjection ProjectMarker(
        Transform marker,
        LaneData lane,
        List<string> warnings)
    {
        float bestDistanceSqr = float.MaxValue;
        float bestLaneS = -1f;

        for (int i = 0;
             i < lane.positions.Length - 1;
             i++)
        {
            Vector3 start = lane.positions[i];
            Vector3 end = lane.positions[i + 1];
            Vector3 segment = end - start;

            float segmentSqrMagnitude =
                segment.sqrMagnitude;

            float t = 0f;

            if (segmentSqrMagnitude > 0.000001f)
            {
                t = Mathf.Clamp01(
                    Vector3.Dot(
                        marker.position - start,
                        segment
                    ) / segmentSqrMagnitude
                );
            }

            Vector3 closest = start + segment * t;

            float distanceSqr =
                (marker.position - closest).sqrMagnitude;

            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;

                bestLaneS = Mathf.Lerp(
                    lane.distances[i],
                    lane.distances[i + 1],
                    t
                );
            }
        }

        if (bestLaneS < 0f)
        {
            throw new InvalidOperationException(
                marker.name +
                "을 " +
                lane.name +
                "에 투영하지 못했습니다."
            );
        }

        float distance = Mathf.Sqrt(bestDistanceSqr);

        if (distance > markerWarningDistance)
        {
            warnings.Add(
                marker.name +
                "과 " +
                lane.name +
                " 중심선의 거리가 " +
                distance.ToString("F2") +
                "m입니다."
            );
        }

        MarkerProjection result =
            new MarkerProjection();

        result.laneS = bestLaneS;
        result.distance = distance;

        return result;
    }

    private void ApplyBake(
        LaneData[] lanes,
        float[] spawnS,
        float[] despawnS,
        float[] stopLineS,
        List<ChangeRule> rules)
    {
        List<float> allDistances = new List<float>();
        List<Vector3> allPositions = new List<Vector3>();
        List<Quaternion> allRotations =
            new List<Quaternion>();

        int[] laneStarts =
            new int[TrafficLaneDatabase.FixedLaneCount];

        int[] laneCounts =
            new int[TrafficLaneDatabase.FixedLaneCount];

        float[] laneLengths =
            new float[TrafficLaneDatabase.FixedLaneCount];

        for (int laneId = 0;
             laneId < lanes.Length;
             laneId++)
        {
            LaneData lane = lanes[laneId];

            laneStarts[laneId] = allPositions.Count;
            laneCounts[laneId] = lane.positions.Length;
            laneLengths[laneId] = lane.length;

            allDistances.AddRange(lane.distances);
            allPositions.AddRange(lane.positions);
            allRotations.AddRange(lane.rotations);
        }

        int[] vehicleMasks =
        {
            TrafficLaneDatabase.VehicleCar |
            TrafficLaneDatabase.VehicleTruck,

            TrafficLaneDatabase.VehicleCar |
            TrafficLaneDatabase.VehicleTruck,

            TrafficLaneDatabase.VehicleCar |
            TrafficLaneDatabase.VehicleTruck,

            TrafficLaneDatabase.VehicleCar |
            TrafficLaneDatabase.VehicleTruck,

            TrafficLaneDatabase.VehicleCar |
            TrafficLaneDatabase.VehicleTruck,

            TrafficLaneDatabase.VehicleCar |
            TrafficLaneDatabase.VehicleTruck,

            TrafficLaneDatabase.VehicleCar
        };

        float[] speedLimits =
            new float[TrafficLaneDatabase.FixedLaneCount];

        int[] signalGroupIds =
            new int[TrafficLaneDatabase.FixedLaneCount];

        for (int laneId = 0;
             laneId < TrafficLaneDatabase.FixedLaneCount;
             laneId++)
        {
            speedLimits[laneId] = defaultSpeedLimit;

            signalGroupIds[laneId] =
                laneId == TrafficLaneDatabase.LaneR4Branch
                    ? -1
                    : SignalGroupMain;
        }

        int[] ruleStarts =
            new int[TrafficLaneDatabase.FixedLaneCount];

        int[] ruleCounts =
            new int[TrafficLaneDatabase.FixedLaneCount];

        int ruleCursor = 0;

        for (int laneId = 0;
             laneId < TrafficLaneDatabase.FixedLaneCount;
             laneId++)
        {
            ruleStarts[laneId] = ruleCursor;

            while (ruleCursor < rules.Count &&
                   rules[ruleCursor].fromLaneId == laneId)
            {
                ruleCursor++;
            }

            ruleCounts[laneId] =
                ruleCursor - ruleStarts[laneId];
        }

        if (ruleCursor != rules.Count)
        {
            throw new InvalidOperationException(
                "차선 변경 규칙이 Source Lane ID 순서로 " +
                "정렬되지 않았습니다."
            );
        }

        int[] changeToLaneIds = new int[rules.Count];
        float[] changeStartS = new float[rules.Count];
        float[] changeEndS = new float[rules.Count];
        int[] changeVehicleMasks = new int[rules.Count];

        for (int i = 0; i < rules.Count; i++)
        {
            changeToLaneIds[i] = rules[i].toLaneId;
            changeStartS[i] = rules[i].startS;
            changeEndS[i] = rules[i].endS;
            changeVehicleMasks[i] = rules[i].vehicleMask;
        }

        Undo.RecordObject(
            database,
            "Bake Traffic Lane Database"
        );

        var backingBehaviour =
            UdonSharpEditorUtility
                .GetBackingUdonBehaviour(database);

        if (backingBehaviour != null)
        {
            Undo.RecordObject(
                backingBehaviour,
                "Bake Traffic Lane Database"
            );
        }

        database.laneCount =
            TrafficLaneDatabase.FixedLaneCount;

        database.sampleSpacing = sampleSpacing;

        database.laneSampleStarts = laneStarts;
        database.laneSampleCounts = laneCounts;

        database.sampleDistances =
            allDistances.ToArray();

        database.samplePositions =
            allPositions.ToArray();

        database.sampleRotations =
            allRotations.ToArray();

        database.laneLengths = laneLengths;
        database.laneVehicleMasks = vehicleMasks;

        database.spawnS = spawnS;
        database.despawnS = despawnS;

        database.spawnWeights =
            (float[])DefaultSpawnWeights.Clone();

        database.speedLimits = speedLimits;
        database.stopLineS = stopLineS;
        database.signalGroupIds = signalGroupIds;

        database.laneRuleStarts = ruleStarts;
        database.laneRuleCounts = ruleCounts;

        database.changeToLaneIds = changeToLaneIds;
        database.changeStartS = changeStartS;
        database.changeEndS = changeEndS;
        database.changeVehicleMasks =
            changeVehicleMasks;

        UdonSharpEditorUtility.CopyProxyToUdon(
            database,
            ProxySerializationPolicy.All
        );

        EditorUtility.SetDirty(database);

        if (backingBehaviour != null)
        {
            EditorUtility.SetDirty(backingBehaviour);
        }

        EditorSceneManager.MarkSceneDirty(
            database.gameObject.scene
        );
    }

    private Transform RequirePath(
        Transform root,
        string path)
    {
        Transform result = root.Find(path);

        if (result == null)
        {
            throw new InvalidOperationException(
                root.name + "/" + path +
                "를 찾지 못했습니다."
            );
        }

        return result;
    }

    private Transform RequireChild(
        Transform parent,
        string childName)
    {
        Transform result = parent.Find(childName);

        if (result == null)
        {
            throw new InvalidOperationException(
                parent.name + "/" + childName +
                "를 찾지 못했습니다."
            );
        }

        return result;
    }

    private Transform FindTransformInLoadedScenes(
        string objectName)
    {
        for (int sceneIndex = 0;
             sceneIndex < SceneManager.sceneCount;
             sceneIndex++)
        {
            Scene scene =
                SceneManager.GetSceneAt(sceneIndex);

            if (!scene.isLoaded)
            {
                continue;
            }

            GameObject[] roots =
                scene.GetRootGameObjects();

            for (int rootIndex = 0;
                 rootIndex < roots.Length;
                 rootIndex++)
            {
                Transform found = FindRecursive(
                    roots[rootIndex].transform,
                    objectName
                );

                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private Transform FindRecursive(
        Transform current,
        string objectName)
    {
        if (current.name == objectName)
        {
            return current;
        }

        for (int i = 0; i < current.childCount; i++)
        {
            Transform found = FindRecursive(
                current.GetChild(i),
                objectName
            );

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void DrawBakedPreview(SceneView sceneView)
    {
        if (!showBakedPreview ||
            database == null ||
            !database.IsReady() ||
            Event.current.type != EventType.Repaint)
        {
            return;
        }

        Color previousColor = Handles.color;

        DrawBakedLanes();

        if (showDataMarkers)
        {
            DrawBakedMarkers();
        }

        if (showChangeZones)
        {
            DrawChangeZones();
        }

        Handles.color = previousColor;
    }

    private void DrawBakedLanes()
    {
        int arrowStride = Mathf.Max(
            1,
            directionSampleStride
        );

        for (int laneId = 0;
             laneId < database.laneCount;
             laneId++)
        {
            int first = database.laneSampleStarts[laneId];
            int count = database.laneSampleCounts[laneId];
            int last = first + count - 1;

            if (count < 2 ||
                first < 0 ||
                last >= database.samplePositions.Length)
            {
                continue;
            }

            Handles.color =
                LaneColors[laneId % LaneColors.Length];

            for (int sampleIndex = first;
                 sampleIndex < last;
                 sampleIndex++)
            {
                Handles.DrawLine(
                    database.samplePositions[sampleIndex],
                    database.samplePositions[sampleIndex + 1],
                    3f
                );
            }

            if (showSamplePoints)
            {
                for (int sampleIndex = first;
                     sampleIndex <= last;
                     sampleIndex++)
                {
                    Handles.SphereHandleCap(
                        0,
                        database.samplePositions[sampleIndex],
                        Quaternion.identity,
                        previewPointSize,
                        EventType.Repaint
                    );
                }
            }

            if (showDirections)
            {
                for (int sampleIndex = first;
                     sampleIndex <= last;
                     sampleIndex += arrowStride)
                {
                    Handles.ArrowHandleCap(
                        0,
                        database.samplePositions[sampleIndex] +
                        Vector3.up * 0.08f,
                        database.sampleRotations[sampleIndex],
                        previewArrowSize,
                        EventType.Repaint
                    );
                }
            }

            Handles.Label(
                database.samplePositions[first] +
                Vector3.up * 0.5f,
                LaneNames[laneId]
            );
        }
    }

    private void DrawBakedMarkers()
    {
        for (int laneId = 0;
             laneId < database.laneCount;
             laneId++)
        {
            if (database.spawnS[laneId] >= 0f)
            {
                DrawMarker(
                    laneId,
                    database.spawnS[laneId],
                    new Color(0.1f, 1f, 0.2f),
                    "SPAWN",
                    false
                );
            }

            if (database.despawnS[laneId] >= 0f)
            {
                DrawMarker(
                    laneId,
                    database.despawnS[laneId],
                    new Color(1f, 0.15f, 0.1f),
                    "DESPAWN",
                    false
                );
            }

            if (database.stopLineS[laneId] >= 0f)
            {
                DrawMarker(
                    laneId,
                    database.stopLineS[laneId],
                    new Color(1f, 0.9f, 0.1f),
                    "STOP",
                    true
                );
            }
        }
    }

    private void DrawMarker(
        int laneId,
        float laneS,
        Color color,
        string markerName,
        bool drawCube)
    {
        int sampleIndex = database.FindSampleIndex(
            laneId,
            laneS,
            -1
        );

        if (sampleIndex < 0)
        {
            return;
        }

        Vector3 position = database.GetLanePosition(
            laneId,
            laneS,
            sampleIndex
        );

        Quaternion rotation = database.GetLaneRotation(
            laneId,
            laneS,
            sampleIndex
        );

        Vector3 markerPosition =
            position + Vector3.up * 0.25f;

        Handles.color = color;

        if (drawCube)
        {
            Handles.CubeHandleCap(
                0,
                markerPosition,
                rotation,
                0.5f,
                EventType.Repaint
            );
        }
        else
        {
            Handles.SphereHandleCap(
                0,
                markerPosition,
                Quaternion.identity,
                0.45f,
                EventType.Repaint
            );
        }

        Handles.Label(
            position + Vector3.up * 0.7f,
            markerName + " " + LaneNames[laneId] +
            "\ns=" + laneS.ToString("F1") + "m"
        );
    }

    private void DrawChangeZones()
    {
        Handles.color = new Color(
            0.1f,
            1f,
            1f,
            1f
        );

        for (int laneId = 0;
             laneId < database.laneCount;
             laneId++)
        {
            int firstRule =
                database.laneRuleStarts[laneId];

            int ruleCount =
                database.laneRuleCounts[laneId];

            for (int ruleOffset = 0;
                 ruleOffset < ruleCount;
                 ruleOffset++)
            {
                int ruleIndex =
                    firstRule + ruleOffset;

                if (ruleIndex < 0 ||
                    ruleIndex >=
                    database.changeToLaneIds.Length)
                {
                    continue;
                }

                DrawChangeZone(
                    laneId,
                    ruleIndex
                );
            }
        }
    }

    private void DrawChangeZone(
        int fromLaneId,
        int ruleIndex)
    {
        float startS =
            database.changeStartS[ruleIndex];

        float endS =
            database.changeEndS[ruleIndex];

        int sampleIndex = database.FindSampleIndex(
            fromLaneId,
            startS,
            -1
        );

        if (sampleIndex < 0)
        {
            return;
        }

        Vector3 offset = Vector3.up * 0.18f;

        Vector3 previousPosition =
            database.GetLanePosition(
                fromLaneId,
                startS,
                sampleIndex
            ) + offset;

        int laneLast =
            database.laneSampleStarts[fromLaneId] +
            database.laneSampleCounts[fromLaneId] - 1;

        int currentIndex = sampleIndex + 1;

        while (currentIndex <= laneLast &&
               database.sampleDistances[currentIndex] < endS)
        {
            Vector3 currentPosition =
                database.samplePositions[currentIndex] +
                offset;

            Handles.DrawLine(
                previousPosition,
                currentPosition,
                6f
            );

            previousPosition = currentPosition;
            currentIndex++;
        }

        int endSampleIndex = database.FindSampleIndex(
            fromLaneId,
            endS,
            Mathf.Max(
                sampleIndex,
                currentIndex - 1
            )
        );

        if (endSampleIndex < 0)
        {
            return;
        }

        Vector3 endPosition =
            database.GetLanePosition(
                fromLaneId,
                endS,
                endSampleIndex
            ) + offset;

        Handles.DrawLine(
            previousPosition,
            endPosition,
            6f
        );

        int targetLaneId =
            database.changeToLaneIds[ruleIndex];

        Handles.Label(
            database.GetLanePosition(
                fromLaneId,
                startS,
                sampleIndex
            ) + Vector3.up,
            LaneNames[fromLaneId] +
            " → " +
            LaneNames[targetLaneId]
        );
    }

    private void SetReport(
        string message,
        MessageType type)
    {
        lastReport = message;
        lastReportType = type;
        Repaint();
    }
}
