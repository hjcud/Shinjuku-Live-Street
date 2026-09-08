using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 파일 이름 접미사를 기준으로 Texture를 같은 이름의 Material 속성에 연결하는 Editor 도구
/// </summary>
public class MaterialTextureAssigner : MonoBehaviour
{
    [MenuItem("Tools/Assign Textures To Materials")]
    private static void AssignTexturesToMaterials()
    {
        string textureFolderPath = "Assets/model/MAT/Texture";
        string materialFolderPath = "Assets/model/MAT";

        if (!AssetDatabase.IsValidFolder(textureFolderPath) || !AssetDatabase.IsValidFolder(materialFolderPath))
        {
            Debug.LogWarning("[MaterialTextureAssigner] Material or texture folder is missing.");
            return;
        }

        // Cuding Edit: Resources 외부의 에셋은 AssetDatabase로 검색하고 이름을 한 번만 색인
        var textures = new Dictionary<string, Texture>();
        var duplicates = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture", new[] { textureFolderPath }))
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(guid));
            if (texture == null) continue;
            if (textures.ContainsKey(texture.name)) duplicates.Add(texture.name);
            else textures.Add(texture.name, texture);
        }
        string[] suffixes = { "_BaseColor", "_Normal", "_Metallic" };
        string[] properties = { "_MainTex", "_BumpMap", "_MetallicGlossMap" };
        int changedCount = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { materialFolderPath }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat == null) continue;
            bool changed = false;
            for (int i = 0; i < suffixes.Length; i++)
            {
                // 이름 접두사가 비슷한 다른 Material의 텍스처를 잘못 연결하지 않음
                string name = mat.name + suffixes[i];
                if (duplicates.Contains(name))
                {
                    Debug.LogWarning($"[MaterialTextureAssigner] Duplicate texture skipped: {name}");
                    continue;
                }
                if (!mat.HasProperty(properties[i]) || !textures.TryGetValue(name, out Texture tex)) continue;
                if (mat.GetTexture(properties[i]) == tex) continue;
                if (!changed) Undo.RecordObject(mat, "Assign Material Textures");
                mat.SetTexture(properties[i], tex);
                changed = true;
            }
            if (!changed) continue;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            changedCount++;
        }
        Debug.Log($"[MaterialTextureAssigner] Updated {changedCount} materials.");
    }
}
