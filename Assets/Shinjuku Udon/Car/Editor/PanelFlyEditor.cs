using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Components;

[CustomEditor(typeof(PanelFly))]
public class PanelFlyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Setup High Impact Star Effect"))
        {
            PanelFlySceneSetup.SetupOpenScenes(true);
        }
    }
}

[InitializeOnLoad]
public static class PanelFlySceneSetup
{
    private const string PanelParticleName =
        "PanelStarMeshParticle";
    private const string FlashParticleName =
        "PanelStarFlashParticle";
    private const string AssetFolder =
        "Assets/Shinjuku Udon/Car/StarEffect";
    private const string StarMeshPath =
        AssetFolder + "/PanelStarMesh.asset";
    private const string StarMaterialPath =
        AssetFolder + "/PanelStarMaterial.mat";

    static PanelFlySceneSetup()
    {
        EditorApplication.delayCall += AutoSetupOpenScenes;
    }

    private static void AutoSetupOpenScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        SetupOpenScenes(false);
    }

    [MenuItem("Tools/Shinjuku/Setup Panel Star Effects")]
    public static void SetupFromMenu()
    {
        SetupOpenScenes(true);
    }

    public static void SetupOpenScenes(bool forceReconfigure)
    {
        TrafficSimulationManager manager =
            FindLoadedTrafficManager();
        bool collisionSetupChanged =
            SetupStandeeCollisionTargets(manager) |
            SetupVehicleCollisionProxies(manager);

        PanelFly[] panels =
            Resources.FindObjectsOfTypeAll<PanelFly>();
        if (panels == null || panels.Length == 0)
        {
            return;
        }

        Mesh starMesh = GetOrCreateStarMesh();
        Material starMaterial = GetOrCreateStarMaterial();
        HashSet<UnityEngine.SceneManagement.Scene> changedScenes =
            new HashSet<UnityEngine.SceneManagement.Scene>();

        if (collisionSetupChanged && manager != null)
        {
            changedScenes.Add(manager.gameObject.scene);
        }

        for (int i = 0; i < panels.Length; i++)
        {
            PanelFly panel = panels[i];
            if (panel == null ||
                !panel.gameObject.scene.IsValid() ||
                !panel.gameObject.scene.isLoaded)
            {
                continue;
            }

            bool changed = false;
            Undo.RecordObject(panel, "Setup panel star effect");

            if (panel.trafficManager == null && manager != null)
            {
                panel.trafficManager = manager;
                changed = true;
            }

            MeshFilter panelMeshFilter =
                FindPanelMeshFilter(panel.transform);
            Renderer panelRenderer = panelMeshFilter != null
                ? panelMeshFilter.GetComponent<Renderer>()
                : null;

            ParticleSystem panelParticle = FindParticle(
                panel.transform,
                PanelParticleName
            );
            if (panelParticle == null)
            {
                panelParticle = CreateParticleChild(
                    panel.transform,
                    PanelParticleName
                );
                changed = true;
                forceReconfigure = true;
            }

            if (forceReconfigure && panelParticle != null)
            {
                ConfigurePanelParticle(
                    panelParticle,
                    panelMeshFilter != null
                        ? panelMeshFilter.sharedMesh
                        : null,
                    panelRenderer
                );
                changed = true;
            }

            if (panel.starPanelParticle != panelParticle)
            {
                panel.starPanelParticle = panelParticle;
                changed = true;
            }

            ParticleSystem flashParticle = FindParticle(
                panel.transform,
                FlashParticleName
            );
            if (flashParticle == null)
            {
                flashParticle = CreateParticleChild(
                    panel.transform,
                    FlashParticleName
                );
                changed = true;
                forceReconfigure = true;
            }

            if (forceReconfigure && flashParticle != null)
            {
                ConfigureFlashParticle(
                    flashParticle,
                    starMesh,
                    starMaterial
                );
                changed = true;
            }

            if (panel.starFlashParticle != flashParticle)
            {
                panel.starFlashParticle = flashParticle;
                changed = true;
            }

            if (!changed)
            {
                continue;
            }

            EditorUtility.SetDirty(panel);
            changedScenes.Add(panel.gameObject.scene);
        }

        foreach (UnityEngine.SceneManagement.Scene scene
            in changedScenes)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (changedScenes.Count > 0)
        {
            AssetDatabase.SaveAssets();
            if (forceReconfigure)
            {
                Debug.Log(
                    "[PanelFly] High-impact star effects configured."
                );
            }
        }
    }

    private static bool SetupStandeeCollisionTargets(
        TrafficSimulationManager manager)
    {
        GameObject standee = GameObject.Find("등신대");
        if (standee == null)
        {
            return false;
        }

        bool changed = false;
        int carLayerMask = 1 << 24;

        for (int i = 0; i < standee.transform.childCount; i++)
        {
            Transform child = standee.transform.GetChild(i);
            GameObject target = child.gameObject;

            if (string.Equals(
                child.name,
                "cuding",
                System.StringComparison.OrdinalIgnoreCase
            ))
            {
                if (target.layer != 25)
                {
                    Undo.RecordObject(target, "Set fixed obstacle layer");
                    target.layer = 25;
                    changed = true;
                }

                continue;
            }

            if (target.layer != 13)
            {
                Undo.RecordObject(target, "Set panel pickup layer");
                target.layer = 13;
                changed = true;
            }

            VRCObjectSync objectSync =
                target.GetComponent<VRCObjectSync>();
            if (objectSync == null)
            {
                objectSync = Undo.AddComponent<VRCObjectSync>(target);
                objectSync.AllowCollisionOwnershipTransfer = true;
                changed = true;
            }

            VRCPickup pickup = target.GetComponent<VRCPickup>();
            if (pickup == null)
            {
                pickup = Undo.AddComponent<VRCPickup>(target);
                pickup.pickupable = true;
                pickup.proximity = 0.65f;
                pickup.InteractionText = child.name;
                pickup.UseText = child.name;
                changed = true;
            }

            Rigidbody body = target.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = Undo.AddComponent<Rigidbody>(target);
                body.mass = 2f;
                body.drag = 1.7f;
                body.angularDrag = 2f;
                body.useGravity = true;
                changed = true;
            }

            if (body.includeLayers != (LayerMask)carLayerMask ||
                body.collisionDetectionMode !=
                    CollisionDetectionMode.ContinuousDynamic ||
                body.interpolation != RigidbodyInterpolation.Interpolate)
            {
                Undo.RecordObject(body, "Configure panel collision body");
                body.includeLayers = carLayerMask;
                body.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                changed = true;
            }

            BoxCollider box = target.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = Undo.AddComponent<BoxCollider>(target);
                Renderer renderer = target.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Bounds bounds = renderer.localBounds;
                    box.center = bounds.center;
                    box.size = new Vector3(
                        Mathf.Max(0.05f, bounds.size.x),
                        Mathf.Max(0.05f, bounds.size.y),
                        Mathf.Max(0.05f, bounds.size.z)
                    );
                }
                changed = true;
            }

            if (box.includeLayers != (LayerMask)carLayerMask ||
                box.layerOverridePriority != 1)
            {
                Undo.RecordObject(box, "Configure panel collider layers");
                box.includeLayers = carLayerMask;
                box.excludeLayers = 0;
                box.layerOverridePriority = 1;
                changed = true;
            }

            PanelFly panel = target.GetComponent<PanelFly>();
            if (panel == null)
            {
                panel = UdonSharpUndo.AddComponent<PanelFly>(target);
                panel.bounceForce = 20f;
                changed = true;
            }

            if (panel.trafficManager == null && manager != null)
            {
                Undo.RecordObject(panel, "Assign traffic manager");
                panel.trafficManager = manager;
                EditorUtility.SetDirty(panel);
                changed = true;
            }
        }

        return changed;
    }

    private static bool SetupVehicleCollisionProxies(
        TrafficSimulationManager manager)
    {
        if (manager == null || manager.vehicleRoots == null)
        {
            return false;
        }

        bool changed = false;
        int fixedObstacleLayerMask = 1 << 25;

        if ((manager.authorityObstacleLayerMask &
             fixedObstacleLayerMask) == 0)
        {
            Undo.RecordObject(
                manager,
                "Include fixed traffic obstacles"
            );
            manager.authorityObstacleLayerMask |=
                fixedObstacleLayerMask;
            changed = true;
        }

        for (int i = 0; i < manager.vehicleRoots.Length; i++)
        {
            Transform root = manager.vehicleRoots[i];
            if (root == null)
            {
                continue;
            }

            if (root.gameObject.layer != 24)
            {
                Undo.RecordObject(
                    root.gameObject,
                    "Set vehicle collision layer"
                );
                root.gameObject.layer = 24;
                changed = true;
            }

            BoxCollider proxy = root.GetComponent<BoxCollider>();
            bool created = false;
            if (proxy == null)
            {
                proxy = Undo.AddComponent<BoxCollider>(root.gameObject);
                created = true;
                changed = true;
            }

            Vector3 minimum;
            Vector3 maximum;
            bool hasRendererBounds = TryGetLocalRendererBounds(
                root,
                out minimum,
                out maximum
            );

            bool hasBakedBounds =
                manager.bakedVehicleFrontExtents != null &&
                manager.bakedVehicleRearExtents != null &&
                manager.bakedVehicleWidths != null &&
                i < manager.bakedVehicleFrontExtents.Length &&
                i < manager.bakedVehicleRearExtents.Length &&
                i < manager.bakedVehicleWidths.Length;

            if (!hasBakedBounds)
            {
                continue;
            }

            float visualScale = Mathf.Clamp(
                i == manager.truckSlotIndex
                    ? manager.truckVisualScale
                    : manager.normalCarVisualScale,
                0.8f,
                1.25f
            );
            Vector3 rootScale = root.lossyScale;
            float widthScale = Mathf.Max(
                0.0001f,
                Mathf.Abs(rootScale.x) * visualScale
            );
            float lengthScale = Mathf.Max(
                0.0001f,
                Mathf.Abs(rootScale.z) * visualScale
            );
            float front = manager.bakedVehicleFrontExtents[i];
            float rear = manager.bakedVehicleRearExtents[i];

            Vector3 desiredCenter = new Vector3(
                hasRendererBounds
                    ? (minimum.x + maximum.x) * 0.5f
                    : 0f,
                hasRendererBounds
                    ? (minimum.y + maximum.y) * 0.5f
                    : 0.8f,
                (front - rear) * 0.5f / lengthScale
            );
            Vector3 desiredSize = new Vector3(
                Mathf.Max(0.1f, manager.bakedVehicleWidths[i]) /
                    widthScale,
                hasRendererBounds
                    ? Mathf.Max(0.2f, maximum.y - minimum.y)
                    : 1.6f,
                Mathf.Max(0.1f, front + rear) / lengthScale
            );

            if (created ||
                (proxy.center - desiredCenter).sqrMagnitude > 0.000001f ||
                (proxy.size - desiredSize).sqrMagnitude > 0.000001f ||
                !proxy.isTrigger || !proxy.enabled)
            {
                Undo.RecordObject(proxy, "Configure vehicle collision proxy");
                proxy.center = desiredCenter;
                proxy.size = desiredSize;
                // Central traffic is transform-driven. A trigger proxy is
                // queried by PanelFly without adding per-vehicle Rigidbody
                // simulation or relying on unreliable static-collider events.
                proxy.isTrigger = true;
                proxy.enabled = true;
                proxy.includeLayers = 0;
                proxy.excludeLayers = 0;
                proxy.layerOverridePriority = 0;
                changed = true;
            }
        }

        if (changed)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(
                manager,
                ProxySerializationPolicy.All
            );
            EditorUtility.SetDirty(manager);
        }

        return changed;
    }

    private static bool TryGetLocalRendererBounds(
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
        bool found = false;
        Renderer[] renderers =
            root.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null ||
                renderer is ParticleSystemRenderer ||
                renderer is TrailRenderer ||
                renderer is LineRenderer ||
                string.Equals(
                    renderer.name,
                    "shadow",
                    System.StringComparison.OrdinalIgnoreCase
                ))
            {
                continue;
            }

            Bounds bounds = renderer.localBounds;
            Matrix4x4 toRoot = root.worldToLocalMatrix *
                renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(
                    bounds.extents,
                    new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f
                    )
                );
                point = toRoot.MultiplyPoint3x4(point);
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
                found = true;
            }
        }

        return found;
    }

    private static TrafficSimulationManager
        FindLoadedTrafficManager()
    {
        TrafficSimulationManager[] managers =
            Resources.FindObjectsOfTypeAll<TrafficSimulationManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            TrafficSimulationManager manager = managers[i];
            if (manager != null &&
                manager.gameObject.scene.IsValid() &&
                manager.gameObject.scene.isLoaded)
            {
                return manager;
            }
        }

        return null;
    }

    private static MeshFilter FindPanelMeshFilter(
        Transform panelRoot)
    {
        MeshFilter ownMesh = panelRoot.GetComponent<MeshFilter>();
        if (ownMesh != null && ownMesh.sharedMesh != null)
        {
            return ownMesh;
        }

        MeshFilter[] meshes =
            panelRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshes.Length; i++)
        {
            MeshFilter mesh = meshes[i];
            if (mesh == null ||
                mesh.sharedMesh == null ||
                mesh.transform.name == PanelParticleName ||
                mesh.transform.name == FlashParticleName)
            {
                continue;
            }

            return mesh;
        }

        return null;
    }

    private static ParticleSystem FindParticle(
        Transform parent,
        string childName)
    {
        Transform child = parent.Find(childName);
        return child != null
            ? child.GetComponent<ParticleSystem>()
            : null;
    }

    private static ParticleSystem CreateParticleChild(
        Transform parent,
        string childName)
    {
        GameObject child = new GameObject(childName);
        Undo.RegisterCreatedObjectUndo(
            child,
            "Create panel star particle"
        );
        child.transform.SetParent(parent, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        return Undo.AddComponent<ParticleSystem>(child);
    }

    private static void ConfigureCommonParticle(
        ParticleSystem particle,
        float lifetime,
        int maximumParticles)
    {
        Undo.RecordObject(particle, "Configure panel particle");

        ParticleSystem.MainModule main = particle.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = Mathf.Max(0.1f, lifetime);
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.maxParticles = maximumParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = particle.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particle.shape;
        shape.enabled = false;

        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        EditorUtility.SetDirty(particle);
    }

    private static void ConfigurePanelParticle(
        ParticleSystem particle,
        Mesh mesh,
        Renderer sourceRenderer)
    {
        ConfigureCommonParticle(particle, 2.6f, 2);

        ParticleSystem.MainModule main = particle.main;
        main.gravityModifier = 1f;

        ParticleSystem.RotationOverLifetimeModule rotation =
            particle.rotationOverLifetime;
        rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(9f);
        rotation.y = new ParticleSystem.MinMaxCurve(13f);
        rotation.z = new ParticleSystem.MinMaxCurve(11f);

        ParticleSystem.SizeOverLifetimeModule size =
            particle.sizeOverLifetime;
        size.enabled = true;
        size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.65f, 1f),
                new Keyframe(0.88f, 0.12f),
                new Keyframe(1f, 0f)
            )
        );

        ParticleSystemRenderer renderer =
            particle.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.motionVectorGenerationMode =
            MotionVectorGenerationMode.ForceNoMotion;

        if (sourceRenderer != null)
        {
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
        }

        EditorUtility.SetDirty(renderer);
    }

    private static void ConfigureFlashParticle(
        ParticleSystem particle,
        Mesh starMesh,
        Material starMaterial)
    {
        ConfigureCommonParticle(particle, 1.1f, 12);

        ParticleSystem.MainModule main = particle.main;
        main.gravityModifier = 0f;
        main.startColor = Color.white;

        ParticleSystem.SizeOverLifetimeModule size =
            particle.sizeOverLifetime;
        size.enabled = true;
        size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.2f, 1.25f),
                new Keyframe(0.55f, 1f),
                new Keyframe(1f, 0f)
            )
        );

        ParticleSystem.RotationOverLifetimeModule rotation =
            particle.rotationOverLifetime;
        rotation.enabled = true;
        rotation.separateAxes = false;
        rotation.z = new ParticleSystem.MinMaxCurve(4f);

        ParticleSystemRenderer renderer =
            particle.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = starMesh;
        renderer.sharedMaterial = starMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.motionVectorGenerationMode =
            MotionVectorGenerationMode.ForceNoMotion;
        EditorUtility.SetDirty(renderer);
    }

    private static Mesh GetOrCreateStarMesh()
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(
            StarMeshPath
        );
        if (existing != null)
        {
            return existing;
        }

        EnsureAssetFolder();

        const int pointCount = 10;
        const float outerRadius = 0.5f;
        const float innerRadius = 0.22f;
        const float halfDepth = 0.06f;

        Vector3[] vertices = new Vector3[pointCount * 2 + 2];
        vertices[0] = new Vector3(0f, 0f, -halfDepth);
        vertices[pointCount + 1] =
            new Vector3(0f, 0f, halfDepth);

        for (int i = 0; i < pointCount; i++)
        {
            float angle =
                Mathf.PI * 0.5f +
                i * Mathf.PI * 2f / pointCount;
            float radius = (i & 1) == 0
                ? outerRadius
                : innerRadius;
            Vector3 point = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                0f
            );
            vertices[i + 1] =
                point + Vector3.back * halfDepth;
            vertices[pointCount + 2 + i] =
                point + Vector3.forward * halfDepth;
        }

        List<int> triangles = new List<int>();
        for (int i = 0; i < pointCount; i++)
        {
            int next = (i + 1) % pointCount;
            int front = i + 1;
            int frontNext = next + 1;
            int back = pointCount + 2 + i;
            int backNext = pointCount + 2 + next;

            triangles.Add(0);
            triangles.Add(frontNext);
            triangles.Add(front);

            triangles.Add(pointCount + 1);
            triangles.Add(back);
            triangles.Add(backNext);

            triangles.Add(front);
            triangles.Add(frontNext);
            triangles.Add(backNext);
            triangles.Add(front);
            triangles.Add(backNext);
            triangles.Add(back);
        }

        Mesh mesh = new Mesh
        {
            name = "PanelStarMesh",
            vertices = vertices,
            triangles = triangles.ToArray()
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, StarMeshPath);
        return mesh;
    }

    private static Material GetOrCreateStarMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(
            StarMaterialPath
        );
        if (existing != null)
        {
            Color blue = new Color(0.56f, 0.85f, 0.97f, 1f);
            if (existing.color != blue)
            {
                existing.color = blue;
                EditorUtility.SetDirty(existing);
            }
            return existing;
        }

        EnsureAssetFolder();
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader)
        {
            name = "PanelStarMaterial",
            color = new Color(0.56f, 0.85f, 0.97f, 1f)
        };
        AssetDatabase.CreateAsset(material, StarMaterialPath);
        return material;
    }

    private static void EnsureAssetFolder()
    {
        if (AssetDatabase.IsValidFolder(AssetFolder))
        {
            return;
        }

        AssetDatabase.CreateFolder(
            "Assets/Shinjuku Udon/Car",
            "StarEffect"
        );
    }
}
