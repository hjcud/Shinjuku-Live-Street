using UnityEngine;
using UnityEditor;

public class MaterialTextureAssigner : MonoBehaviour
{
    [MenuItem("Tools/Assign Textures To Materials")]
    private static void AssignTexturesToMaterials()
    {
        string textureFolderPath = "Assets/model/MAT/Texture";   // 텍스처 폴더 경로
        string materialFolderPath = "Assets/model/MAT"; // 마테리얼 폴더 경로

        Texture[] textures = Resources.LoadAll<Texture>(textureFolderPath);
        Material[] materials = Resources.LoadAll<Material>(materialFolderPath);

        foreach (Material mat in materials)
        {
            foreach (Texture tex in textures)
            {
                // 텍스처 이름에서 마테리얼 이름과 타입 분리
                if (tex.name.StartsWith(mat.name))
                {
                    if (tex.name.EndsWith("_BaseColor"))
                    {
                        mat.SetTexture("_MainTex", tex);  // 기본 색상 텍스처
                        Debug.Log($"Applied {tex.name} as _MainTex to {mat.name}");
                    }
                    else if (tex.name.EndsWith("_Normal"))
                    {
                        mat.SetTexture("_BumpMap", tex);  // 노멀 맵 텍스처
                        Debug.Log($"Applied {tex.name} as _BumpMap to {mat.name}");
                    }
                    else if (tex.name.EndsWith("_Metallic"))
                    {
                        mat.SetTexture("_MetallicGlossMap", tex);  // 메탈릭 맵 텍스처
                        Debug.Log($"Applied {tex.name} as _MetallicGlossMap to {mat.name}");
                    }
                    // 필요에 따라 다른 텍스처 유형 추가 가능
                }
            }
        }

        AssetDatabase.SaveAssets();
    }
}
