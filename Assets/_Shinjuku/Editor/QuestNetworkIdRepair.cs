using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.SDKBase.Network;

[InitializeOnLoad]
public static class QuestNetworkIdRepair
{
    // Quest-only network ID maintenance; never opens or modifies TEST_PC.
    const string PcScene = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    const string QuestScene = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";
    const string Output = "output/quest-optimization-20260913/";
    const string Request = Output + "repair-network-ids.request";
    const string InspectRequest = Output + "inspect-network-errors.request";
    const string FixTypesRequest = Output + "fix-network-types.request";

    static QuestNetworkIdRepair() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(FixTypesRequest)) { FixIncompatibleTypes(); return; }
        if (File.Exists(InspectRequest)) { InspectErrors(); return; }
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        try
        {
            if (SceneManager.GetActiveScene().path != QuestScene) throw new InvalidOperationException("TEST_Quest must be active.");
            string pcHashBefore = Hash(PcScene);
            var scene = SceneManager.GetActiveScene();
            var containers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .OfType<INetworkIDContainer>().ToArray();
            if (containers.Length != 1) throw new InvalidOperationException("Expected one network ID container, found " + containers.Length);
            var container = containers[0];
            int before = container.NetworkIDCollection.Count;
            int stale = container.NetworkIDCollection.RemoveAll(pair => pair.gameObject == null);
            var result = NetworkIDAssignment.ConfigureNetworkIDs(container, out List<NetworkIDAssignment.SetErrorLocation> errors,
                NetworkIDAssignment.SetError.IncompatibleTypes);
            var newIds = result.newIDs.ToArray();
            if (errors.Count > 0)
                throw new InvalidOperationException("Network ID assignment still has " + errors.Count + " errors: " +
                                                    string.Join(" | ", errors.Select(e => e.ToString())));
            var component = (Component)container;
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            string pcHashAfter = Hash(PcScene);
            if (pcHashBefore != pcHashAfter) throw new InvalidOperationException("PC scene changed during Quest network ID repair.");
            File.WriteAllText(Output + "repair-network-ids.done",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " PASS\n" +
                $"CollectionBefore={before}\nRemovedStale={stale}\nAdded={newIds.Length}\nCollectionAfter={container.NetworkIDCollection.Count}\n" +
                "PCSceneHash=" + pcHashAfter + "\n", new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Output + "repair-network-ids.failed", ex.ToString(), new UTF8Encoding(false));
            Debug.LogException(ex);
        }
    }

    static void FixIncompatibleTypes()
    {
        File.Delete(FixTypesRequest);
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != QuestScene) throw new InvalidOperationException("TEST_Quest must be active.");
            string pcHashBefore = Hash(PcScene);
            var container = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .OfType<INetworkIDContainer>().Single();

            NetworkIDAssignment.ConfigureNetworkIDs(container, out List<NetworkIDAssignment.SetErrorLocation> errors);
            var incompatible = errors.Where(e => e.error == NetworkIDAssignment.SetError.IncompatibleTypes).ToList();
            var report = new List<string> { "Updated=" + incompatible.Count };
            foreach (var error in incompatible)
            {
                var oldPair = error.location;
                var currentTypes = NetworkIDAssignment.GetSerializedTypes(oldPair.gameObject);
                report.Add($"ID={oldPair.ID} Path={PathOf(oldPair.gameObject.transform)} Old=[{string.Join(", ", oldPair.SerializedTypeNames)}] New=[{string.Join(", ", currentTypes)}]");
                container.NetworkIDCollection = container.NetworkIDCollection
                    .Where(pair => !(pair.ID == oldPair.ID && pair.gameObject == oldPair.gameObject))
                    .Append(new NetworkIDPair
                    {
                        ID = oldPair.ID,
                        gameObject = oldPair.gameObject,
                        SerializedTypeNames = currentTypes
                    }).ToList();
            }

            NetworkIDAssignment.ConfigureNetworkIDs(container, out List<NetworkIDAssignment.SetErrorLocation> remaining);
            if (remaining.Count > 0)
                throw new InvalidOperationException("Network ID validation still has " + remaining.Count + " errors: " +
                                                    string.Join(" | ", remaining.Select(error => Describe(error))));

            var component = (Component)container;
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            string pcHashAfter = Hash(PcScene);
            if (pcHashBefore != pcHashAfter) throw new InvalidOperationException("PC scene changed during Quest network type repair.");
            report.Add("RemainingErrors=0");
            report.Add("PCSceneHash=" + pcHashAfter);
            File.WriteAllText(Output + "fix-network-types.done", string.Join("\n", report), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Output + "fix-network-types.failed", ex.ToString(), new UTF8Encoding(false));
            Debug.LogException(ex);
        }
    }

    static void InspectErrors()
    {
        File.Delete(InspectRequest);
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != QuestScene) throw new InvalidOperationException("TEST_Quest must be active.");
            var container = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .OfType<INetworkIDContainer>().Single();
            NetworkIDAssignment.ConfigureNetworkIDs(container, out List<NetworkIDAssignment.SetErrorLocation> errors);
            var lines = new List<string> { "Errors=" + errors.Count };
            foreach (var error in errors) lines.Add(Describe(error));
            File.WriteAllText(Output + "inspect-network-errors.done", string.Join("\n", lines), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Output + "inspect-network-errors.failed", ex.ToString(), new UTF8Encoding(false));
        }
    }

    static string Describe(object value)
    {
        if (value == null) return "<null>";
        var type = value.GetType();
        var parts = new List<string> { type.FullName, value.ToString() };
        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
        {
            object fieldValue;
            try { fieldValue = field.GetValue(value); } catch { continue; }
            if (fieldValue is GameObject go) fieldValue = PathOf(go.transform);
            else if (fieldValue is Component component) fieldValue = PathOf(component.transform) + " (" + component.GetType().FullName + ")";
            parts.Add(field.Name + "=" + (fieldValue ?? "<null>"));
        }
        foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
            object propertyValue;
            try { propertyValue = property.GetValue(value, null); } catch { continue; }
            if (propertyValue is GameObject go) propertyValue = PathOf(go.transform);
            else if (propertyValue is Component component) propertyValue = PathOf(component.transform) + " (" + component.GetType().FullName + ")";
            parts.Add(property.Name + "=" + (propertyValue ?? "<null>"));
        }
        return string.Join(" | ", parts);
    }

    static string PathOf(Transform transform)
    {
        var names = new List<string>();
        while (transform != null) { names.Add(transform.name); transform = transform.parent; }
        names.Reverse();
        return string.Join("/", names);
    }

    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
}
