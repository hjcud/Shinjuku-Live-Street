using UnityEngine;
using UnityEditor;

/// <summary>
/// 파일 이름 접미사를 기준으로 Texture를 같은 이름의 Material 속성에 연결하는 Editor 도구이다.
/// </summary>
public class MaterialTextureAssigner : MonoBehaviour
{
    [MenuItem("Tools/Assign Textures To Materials")]
    private static void AssignTexturesToMaterials()
    {
        string textureFolderPath = "Assets/model/MAT/Texture";
        string materialFolderPath = "Assets/model/MAT";

        Texture[] textures = Resources.LoadAll<Texture>(textureFolderPath);
        Material[] materials = Resources.LoadAll<Material>(materialFolderPath);

        foreach (Material mat in materials)
        {
            foreach (Texture tex in textures)
            {
                // Material 이름과 접미사 규칙이 일치하는 Texture만 자동으로 연결한다.
                if (tex.name.StartsWith(mat.name))
                {
                    if (tex.name.EndsWith("_BaseColor"))
                    {
                        mat.SetTexture("_MainTex", tex);
                        Debug.Log($"Applied {tex.name} as _MainTex to {mat.name}");
                    }
                    else if (tex.name.EndsWith("_Normal"))
                    {
                        mat.SetTexture("_BumpMap", tex);
                        Debug.Log($"Applied {tex.name} as _BumpMap to {mat.name}");
                    }
                    else if (tex.name.EndsWith("_Metallic"))
                    {
                        mat.SetTexture("_MetallicGlossMap", tex);
                        Debug.Log($"Applied {tex.name} as _MetallicGlossMap to {mat.name}");
                    }
                }
            }
        }

        AssetDatabase.SaveAssets();
    }
}
