using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class TrafficKinematicVehicleTestWindow : EditorWindow
{
    private const float MinimumDeltaTime = 0.0001f;

    private GameObject sourceVehicle;
    private GameObject testVehicle;
    private bool ownsTestVehicle;
    private bool running;
    private bool automaticLaneChange;
    private bool hasLaneChangeReference;
    private bool keyForward;
    private bool keyReverse;
    private bool keyLeft;
    private bool keyRight;
    private double previousEditorTime;

    private float acceleration = 3f;
    private float braking = 5f;
    private float maximumSpeed = 6.5f;
    private float maximumReverseSpeed = 1.2f;
    private float wheelBase = 2.65f;
    private float trackWidth = 1.55f;
    private float wheelRadius = 0.32f;
    private float maximumSteeringAngle = 34f;
    private float steeringResponse = 120f;
    private float laneWidth = 3.2f;
    private float laneChangeDistance = 16f;
    private float lookAheadDistance = 1.5f;
    private float maximumLateralAcceleration = 2.5f;
    private float manualThrottle;
    private float manualSteering;

    private Vector3 resetPosition;
    private Quaternion resetRotation;
    private Vector3 motionPosition;
    private Quaternion motionRotation;
    private Vector3 pathStartPosition;
    private Quaternion pathStartRotation;
    private float pathSide;
    private float speed;
    private float steeringAngle;
    private float wheelRollAngle;
    private float elapsedTime;
    private float maximumObservedBodyAngle;
    private float maximumObservedSteering;
    private float maximumPathError;
    private string status = "차량을 선택한 뒤 테스트 복제본을 만드세요.";

    private readonly Transform[] wheels = new Transform[4];
    private readonly Quaternion[] wheelBaseRotations =
        new Quaternion[4];
    private readonly List<Vector3> travelledPath =
        new List<Vector3>();

    [MenuItem("Tools/Traffic/Kinematic Vehicle Test")]
    private static void OpenWindow()
    {
        GetWindow<TrafficKinematicVehicleTestWindow>(
            "Traffic Motion Test"
        );
    }

    private void OnEnable()
    {
        previousEditorTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += EditorTick;
        SceneView.duringSceneGui += DrawSceneOverlay;
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorTick;
        SceneView.duringSceneGui -= DrawSceneOverlay;

        if (ownsTestVehicle && testVehicle != null)
        {
            DestroyImmediate(testVehicle);
        }
    }

    private void OnGUI()
    {
        HandleKeyboardEvent(Event.current);

        EditorGUILayout.LabelField(
            "조향 기반 차량 운동 테스트",
            EditorStyles.boldLabel
        );
        EditorGUILayout.HelpBox(
            "선택한 차량의 저장되지 않는 복제본을 만들어 실제 조향각, " +
            "차체 회전, 바퀴 회전을 함께 시험합니다. W/S 가감속, " +
            "A/D 조향, Q/E 자동 좌/우 차선 변경, R 초기화입니다.",
            MessageType.Info
        );

        sourceVehicle = (GameObject)EditorGUILayout.ObjectField(
            "Source Vehicle",
            sourceVehicle,
            typeof(GameObject),
            true
        );

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("현재 선택 사용"))
            {
                sourceVehicle = Selection.activeGameObject;
            }

            if (GUILayout.Button("테스트 복제본 생성"))
            {
                CreateTestVehicle();
            }
        }

        EditorGUILayout.Space();
        DrawSettings();
        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(testVehicle == null))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(running ? "일시 정지" : "수동 주행"))
                {
                    running = !running;
                    automaticLaneChange = false;
                    previousEditorTime =
                        EditorApplication.timeSinceStartup;
                }

                if (GUILayout.Button("초기화"))
                {
                    ResetTestVehicle();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("자동 왼쪽 변경 (16m)"))
                {
                    laneChangeDistance = 16f;
                    BeginAutomaticLaneChange(-1f);
                }

                if (GUILayout.Button("자동 오른쪽 변경 (16m)"))
                {
                    laneChangeDistance = 16f;
                    BeginAutomaticLaneChange(1f);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("긴급 왼쪽 변경 (10m)"))
                {
                    laneChangeDistance = 10f;
                    BeginAutomaticLaneChange(-1f);
                }

                if (GUILayout.Button("긴급 오른쪽 변경 (10m)"))
                {
                    laneChangeDistance = 10f;
                    BeginAutomaticLaneChange(1f);
                }
            }
        }

        EditorGUILayout.Space();
        DrawTelemetry();
    }

    private void DrawSettings()
    {
        EditorGUILayout.LabelField("Vehicle", EditorStyles.boldLabel);
        acceleration = EditorGUILayout.FloatField(
            "Acceleration (m/s²)",
            acceleration
        );
        braking = EditorGUILayout.FloatField(
            "Braking (m/s²)",
            braking
        );
        maximumSpeed = EditorGUILayout.FloatField(
            "Maximum Speed (m/s)",
            maximumSpeed
        );
        maximumReverseSpeed = EditorGUILayout.FloatField(
            "Reverse Speed (m/s)",
            maximumReverseSpeed
        );
        wheelBase = EditorGUILayout.FloatField(
            "Wheel Base (m)",
            wheelBase
        );
        trackWidth = EditorGUILayout.FloatField(
            "Track Width (m)",
            trackWidth
        );
        wheelRadius = EditorGUILayout.FloatField(
            "Wheel Radius (m)",
            wheelRadius
        );
        maximumSteeringAngle = EditorGUILayout.Slider(
            "Maximum Steering",
            maximumSteeringAngle,
            20f,
            40f
        );
        steeringResponse = EditorGUILayout.FloatField(
            "Steering Response (deg/s)",
            steeringResponse
        );

        EditorGUILayout.LabelField("Lane Change", EditorStyles.boldLabel);
        laneWidth = EditorGUILayout.FloatField("Lane Width", laneWidth);
        laneChangeDistance = EditorGUILayout.FloatField(
            "Travel Distance",
            laneChangeDistance
        );
        lookAheadDistance = EditorGUILayout.FloatField(
            "Controller Look Ahead",
            lookAheadDistance
        );
        maximumLateralAcceleration = EditorGUILayout.FloatField(
            "Maximum Lateral Acceleration",
            maximumLateralAcceleration
        );

        if (!automaticLaneChange)
        {
            manualThrottle = EditorGUILayout.Slider(
                "Manual Throttle",
                manualThrottle,
                -1f,
                1f
            );
            manualSteering = EditorGUILayout.Slider(
                "Manual Steering",
                manualSteering,
                -1f,
                1f
            );
        }
    }

    private void DrawTelemetry()
    {
        float maximumCurvature = CalculateMaximumCurveCurvature(
            laneChangeDistance,
            laneWidth
        );
        float requiredSteering = Mathf.Rad2Deg * Mathf.Atan(
            Mathf.Max(0.1f, wheelBase) * maximumCurvature
        );
        float safeCurveSpeed = maximumCurvature > 0.0001f
            ? Mathf.Sqrt(
                Mathf.Max(0.1f, maximumLateralAcceleration) /
                maximumCurvature
              )
            : maximumSpeed;

        EditorGUILayout.LabelField("상태", status);
        EditorGUILayout.LabelField(
            "현재 속도",
            (speed * 3.6f).ToString("F1") + " km/h"
        );
        EditorGUILayout.LabelField(
            "현재 앞바퀴 조향",
            steeringAngle.ToString("F1") + "°"
        );
        EditorGUILayout.LabelField(
            "최대 관측 차체각",
            maximumObservedBodyAngle.ToString("F1") + "°"
        );
        EditorGUILayout.LabelField(
            "곡선 필요 최대 조향",
            requiredSteering.ToString("F1") + "°"
        );
        EditorGUILayout.LabelField(
            "횡가속도 기준 속도",
            (safeCurveSpeed * 3.6f).ToString("F1") + " km/h"
        );
        EditorGUILayout.LabelField(
            "최대 경로 오차",
            maximumPathError.ToString("F3") + " m"
        );

        if (requiredSteering > maximumSteeringAngle + 0.1f)
        {
            EditorGUILayout.HelpBox(
                "현재 거리에서는 설정된 최대 조향각으로 곡선을 " +
                "따라갈 수 없습니다. 거리를 늘리거나 더 감속해야 합니다.",
                MessageType.Warning
            );
        }
    }

    private void CreateTestVehicle()
    {
        if (sourceVehicle == null)
        {
            sourceVehicle = Selection.activeGameObject;
        }

        if (sourceVehicle == null)
        {
            status = "복제할 차량을 먼저 선택하세요.";
            return;
        }

        if (ownsTestVehicle && testVehicle != null)
        {
            DestroyImmediate(testVehicle);
        }

        testVehicle = Instantiate(sourceVehicle);
        testVehicle.name = "[TEST] " + sourceVehicle.name;
        testVehicle.hideFlags =
            HideFlags.DontSaveInEditor |
            HideFlags.DontSaveInBuild;
        testVehicle.SetActive(true);
        ownsTestVehicle = true;

        Behaviour[] behaviours =
            testVehicle.GetComponentsInChildren<Behaviour>(true);

        for (int i = 0; i < behaviours.Length; i++)
        {
            behaviours[i].enabled = false;
        }

        Collider[] colliders =
            testVehicle.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        Rigidbody[] rigidbodies =
            testVehicle.GetComponentsInChildren<Rigidbody>(true);

        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].isKinematic = true;
            rigidbodies[i].detectCollisions = false;
        }

        CacheWheels();
        resetPosition = testVehicle.transform.position;
        resetRotation = testVehicle.transform.rotation;
        ResetTestVehicle();
        Selection.activeGameObject = testVehicle;
        status = "테스트 차량 준비 완료";
        SceneView.FrameLastActiveSceneView();
    }

    private void CacheWheels()
    {
        for (int i = 0; i < wheels.Length; i++)
        {
            wheels[i] = null;
            wheelBaseRotations[i] = Quaternion.identity;
        }

        Transform[] children =
            testVehicle.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < children.Length; i++)
        {
            int wheelIndex = GetWheelIndex(children[i].name);

            if (wheelIndex < 0)
            {
                continue;
            }

            wheels[wheelIndex] = children[i];
            wheelBaseRotations[wheelIndex] =
                children[i].localRotation;
        }
    }

    private int GetWheelIndex(string wheelName)
    {
        if (wheelName == "Wheel_FL") return 0;
        if (wheelName == "Wheel_FR" || wheelName == "Wheel_FB") return 1;
        if (wheelName == "Wheel_BL") return 2;
        if (wheelName == "Wheel_BR") return 3;
        return -1;
    }

    private void ResetTestVehicle()
    {
        if (testVehicle == null)
        {
            return;
        }

        running = false;
        automaticLaneChange = false;
        hasLaneChangeReference = false;
        speed = 0f;
        steeringAngle = 0f;
        wheelRollAngle = 0f;
        elapsedTime = 0f;
        maximumObservedBodyAngle = 0f;
        maximumObservedSteering = 0f;
        maximumPathError = 0f;
        motionPosition = resetPosition;
        motionRotation = resetRotation;
        pathStartPosition = resetPosition;
        pathStartRotation = resetRotation;
        travelledPath.Clear();
        travelledPath.Add(motionPosition);
        testVehicle.transform.SetPositionAndRotation(
            motionPosition,
            motionRotation
        );
        ResetWheelVisuals();
        status = "초기 위치";
        SceneView.RepaintAll();
    }

    private void BeginAutomaticLaneChange(float side)
    {
        if (testVehicle == null)
        {
            return;
        }

        automaticLaneChange = true;
        hasLaneChangeReference = true;
        running = true;
        pathSide = side < 0f ? -1f : 1f;
        pathStartPosition = motionPosition;
        pathStartRotation = motionRotation;
        elapsedTime = 0f;
        maximumObservedBodyAngle = 0f;
        maximumObservedSteering = 0f;
        maximumPathError = 0f;
        travelledPath.Clear();
        travelledPath.Add(motionPosition);
        previousEditorTime = EditorApplication.timeSinceStartup;
        status = side < 0f
            ? "자동 왼쪽 차선 변경"
            : "자동 오른쪽 차선 변경";
    }

    private void EditorTick()
    {
        double now = EditorApplication.timeSinceStartup;
        float deltaTime = Mathf.Clamp(
            (float)(now - previousEditorTime),
            0f,
            0.05f
        );
        previousEditorTime = now;

        if (!running || testVehicle == null ||
            deltaTime <= MinimumDeltaTime)
        {
            return;
        }

        StepVehicle(deltaTime);
        testVehicle.transform.SetPositionAndRotation(
            motionPosition,
            motionRotation
        );
        ApplyWheelVisuals(deltaTime);
        RecordPathPoint();
        elapsedTime += deltaTime;
        Repaint();
        SceneView.RepaintAll();
    }

    private void StepVehicle(float deltaTime)
    {
        float targetSpeed;
        float targetSteering;

        if (automaticLaneChange)
        {
            CalculateAutomaticControl(
                out targetSpeed,
                out targetSteering
            );
        }
        else
        {
            float throttle = keyForward
                ? 1f
                : (keyReverse ? -1f : manualThrottle);
            float steering = keyLeft
                ? -1f
                : (keyRight ? 1f : manualSteering);
            targetSpeed = throttle >= 0f
                ? throttle * maximumSpeed
                : throttle * maximumReverseSpeed;
            targetSteering = steering * maximumSteeringAngle;
        }

        float speedRate = Mathf.Abs(targetSpeed) < Mathf.Abs(speed)
            ? braking
            : acceleration;
        speed = Mathf.MoveTowards(
            speed,
            targetSpeed,
            Mathf.Max(0.1f, speedRate) * deltaTime
        );
        steeringAngle = Mathf.MoveTowards(
            steeringAngle,
            Mathf.Clamp(
                targetSteering,
                -maximumSteeringAngle,
                maximumSteeringAngle
            ),
            Mathf.Max(20f, steeringResponse) * deltaTime
        );

        float safeWheelBase = Mathf.Max(0.5f, wheelBase);
        float yawRate = speed / safeWheelBase * Mathf.Tan(
            steeringAngle * Mathf.Deg2Rad
        );
        Vector3 surfaceUp = pathStartRotation * Vector3.up;

        if (!automaticLaneChange)
        {
            surfaceUp = resetRotation * Vector3.up;
        }

        motionRotation = Quaternion.AngleAxis(
            yawRate * Mathf.Rad2Deg * deltaTime,
            surfaceUp
        ) * motionRotation;
        motionPosition +=
            motionRotation * Vector3.forward * speed * deltaTime;

        maximumObservedSteering = Mathf.Max(
            maximumObservedSteering,
            Mathf.Abs(steeringAngle)
        );

        if (automaticLaneChange)
        {
            float bodyAngle = Mathf.Abs(
                Vector3.SignedAngle(
                    pathStartRotation * Vector3.forward,
                    motionRotation * Vector3.forward,
                    surfaceUp
                )
            );
            maximumObservedBodyAngle = Mathf.Max(
                maximumObservedBodyAngle,
                bodyAngle
            );

            Vector3 relative =
                motionPosition - pathStartPosition;
            float travelled = Vector3.Dot(
                relative,
                pathStartRotation * Vector3.forward
            );
            float lateral = Vector3.Dot(
                relative,
                pathStartRotation * Vector3.right
            );
            float referenceLateral = pathSide * laneWidth *
                SmoothLaneChange(
                    Mathf.Clamp01(
                        travelled /
                        Mathf.Max(0.1f, laneChangeDistance)
                    )
                );
            maximumPathError = Mathf.Max(
                maximumPathError,
                Mathf.Abs(lateral - referenceLateral)
            );
        }
    }

    private void CalculateAutomaticControl(
        out float targetSpeed,
        out float targetSteering)
    {
        Vector3 startForward =
            pathStartRotation * Vector3.forward;
        Vector3 startRight = pathStartRotation * Vector3.right;
        Vector3 startUp = pathStartRotation * Vector3.up;
        Vector3 relative = motionPosition - pathStartPosition;
        float along = Vector3.Dot(relative, startForward);
        float lookX = Mathf.Max(
            0f,
            along + Mathf.Max(0.5f, lookAheadDistance)
        );
        float pathLength = Mathf.Max(2f, laneChangeDistance);
        float clampedLookX = Mathf.Min(pathLength, lookX);
        float progress = Mathf.Clamp01(clampedLookX / pathLength);
        float lookY = pathSide * laneWidth * SmoothLaneChange(progress);

        if (lookX > pathLength)
        {
            lookY = pathSide * laneWidth;
        }

        Vector3 targetPosition = pathStartPosition +
            startForward * lookX +
            startRight * lookY;
        Vector3 targetDirection = Vector3.ProjectOnPlane(
            targetPosition - motionPosition,
            startUp
        );

        if (targetDirection.sqrMagnitude <= 0.0001f)
        {
            targetSteering = 0f;
        }
        else
        {
            float headingError = Vector3.SignedAngle(
                motionRotation * Vector3.forward,
                targetDirection.normalized,
                startUp
            ) * Mathf.Deg2Rad;
            float targetDistance = Mathf.Max(
                0.5f,
                targetDirection.magnitude
            );
            targetSteering = Mathf.Rad2Deg * Mathf.Atan(
                2f * Mathf.Max(0.5f, wheelBase) *
                Mathf.Sin(headingError) /
                targetDistance
            );
        }

        float maximumCurvature = CalculateMaximumCurveCurvature(
            pathLength,
            laneWidth
        );
        float curveSpeed = maximumCurvature > 0.0001f
            ? Mathf.Sqrt(
                Mathf.Max(0.1f, maximumLateralAcceleration) /
                maximumCurvature
              )
            : maximumSpeed;
        targetSpeed = Mathf.Min(maximumSpeed, curveSpeed);

        if (along > pathLength + 3f)
        {
            targetSpeed = 0f;

            if (Mathf.Abs(speed) < 0.02f)
            {
                running = false;
                automaticLaneChange = false;
                status = "자동 테스트 완료: " +
                    elapsedTime.ToString("F2") + "초, 최대 조향 " +
                    maximumObservedSteering.ToString("F1") + "°";
            }
        }
    }

    private float CalculateMaximumCurveCurvature(
        float distance,
        float width)
    {
        float safeDistance = Mathf.Max(0.1f, distance);
        float maximum = 0f;

        for (int i = 1; i < 32; i++)
        {
            float progress = i / 32f;
            float first = SmoothLaneChangeFirstDerivative(progress);
            float second = SmoothLaneChangeSecondDerivative(progress);
            float denominatorBase =
                safeDistance * safeDistance +
                width * width * first * first;
            float curvature = Mathf.Abs(
                safeDistance * width * second /
                Mathf.Pow(
                    Mathf.Max(0.0001f, denominatorBase),
                    1.5f
                )
            );
            maximum = Mathf.Max(maximum, curvature);
        }

        return maximum;
    }

    private float SmoothLaneChange(float progress)
    {
        float p = Mathf.Clamp01(progress);
        return p * p * p * (p * (p * 6f - 15f) + 10f);
    }

    private float SmoothLaneChangeFirstDerivative(float progress)
    {
        float p = Mathf.Clamp01(progress);
        float oneMinusP = 1f - p;
        return 30f * p * p * oneMinusP * oneMinusP;
    }

    private float SmoothLaneChangeSecondDerivative(float progress)
    {
        float p = Mathf.Clamp01(progress);
        return 60f * p * (2f * p * p - 3f * p + 1f);
    }

    private void ApplyWheelVisuals(float deltaTime)
    {
        wheelRollAngle = Mathf.Repeat(
            wheelRollAngle +
            speed / Mathf.Max(0.05f, wheelRadius) *
            Mathf.Rad2Deg * deltaTime,
            360f
        );
        Quaternion roll = Quaternion.AngleAxis(
            wheelRollAngle,
            Vector3.right
        );
        float leftSteering = steeringAngle;
        float rightSteering = steeringAngle;

        if (Mathf.Abs(steeringAngle) > 0.01f)
        {
            float safeWheelBase = Mathf.Max(0.5f, wheelBase);
            float safeTrackWidth = Mathf.Max(0.5f, trackWidth);
            float turnRadius = safeWheelBase /
                Mathf.Max(
                    0.001f,
                    Mathf.Tan(
                        Mathf.Abs(steeringAngle) *
                        Mathf.Deg2Rad
                    )
                );
            float inner = Mathf.Rad2Deg * Mathf.Atan(
                safeWheelBase /
                Mathf.Max(
                    0.1f,
                    turnRadius - safeTrackWidth * 0.5f
                )
            );
            float outer = Mathf.Rad2Deg * Mathf.Atan(
                safeWheelBase /
                (turnRadius + safeTrackWidth * 0.5f)
            );

            if (steeringAngle > 0f)
            {
                leftSteering = outer;
                rightSteering = inner;
            }
            else
            {
                leftSteering = -inner;
                rightSteering = -outer;
            }
        }

        Quaternion leftSteer = Quaternion.AngleAxis(
            leftSteering,
            Vector3.up
        );
        Quaternion rightSteer = Quaternion.AngleAxis(
            rightSteering,
            Vector3.up
        );

        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null)
            {
                continue;
            }

            Quaternion steer = i == 0
                ? leftSteer
                : rightSteer;

            wheels[i].localRotation = i < 2
                ? steer * wheelBaseRotations[i] * roll
                : wheelBaseRotations[i] * roll;
        }
    }

    private void ResetWheelVisuals()
    {
        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] != null)
            {
                wheels[i].localRotation = wheelBaseRotations[i];
            }
        }
    }

    private void RecordPathPoint()
    {
        if (travelledPath.Count == 0 ||
            Vector3.Distance(
                travelledPath[travelledPath.Count - 1],
                motionPosition
            ) > 0.08f)
        {
            travelledPath.Add(motionPosition);

            if (travelledPath.Count > 800)
            {
                travelledPath.RemoveAt(0);
            }
        }
    }

    private void DrawSceneOverlay(SceneView sceneView)
    {
        if (testVehicle == null)
        {
            return;
        }

        Handles.color = new Color(0.15f, 0.95f, 1f, 0.9f);

        if (hasLaneChangeReference)
        {
            Vector3[] planned = new Vector3[49];
            Vector3 forward = pathStartRotation * Vector3.forward;
            Vector3 right = pathStartRotation * Vector3.right;

            for (int i = 0; i < planned.Length; i++)
            {
                float progress = i / (planned.Length - 1f);
                planned[i] = pathStartPosition +
                    forward * laneChangeDistance * progress +
                    right * pathSide * laneWidth *
                    SmoothLaneChange(progress);
            }

            Handles.DrawAAPolyLine(3f, planned);
        }

        if (travelledPath.Count > 1)
        {
            Handles.color = new Color(1f, 0.75f, 0.1f, 0.95f);
            Handles.DrawAAPolyLine(4f, travelledPath.ToArray());
        }

        Handles.color = Color.green;
        Handles.ArrowHandleCap(
            0,
            motionPosition,
            motionRotation,
            1.5f,
            EventType.Repaint
        );
    }

    private void HandleKeyboardEvent(Event currentEvent)
    {
        if (currentEvent == null)
        {
            return;
        }

        bool pressed = currentEvent.type == EventType.KeyDown;
        bool released = currentEvent.type == EventType.KeyUp;

        if (!pressed && !released)
        {
            return;
        }

        bool value = pressed;

        switch (currentEvent.keyCode)
        {
            case KeyCode.W:
                keyForward = value;
                break;
            case KeyCode.S:
                keyReverse = value;
                break;
            case KeyCode.A:
                keyLeft = value;
                break;
            case KeyCode.D:
                keyRight = value;
                break;
            case KeyCode.Q:
                if (pressed) BeginAutomaticLaneChange(-1f);
                break;
            case KeyCode.E:
                if (pressed) BeginAutomaticLaneChange(1f);
                break;
            case KeyCode.R:
                if (pressed) ResetTestVehicle();
                break;
        }

        currentEvent.Use();
        Repaint();
    }
}
