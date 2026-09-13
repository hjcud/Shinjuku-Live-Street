using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Texture-only map checks, independent of other labels being edited.</summary>
[InitializeOnLoad]
public static class SpeakerMapTextureReview
{
    private const string Output="output/speaker-map-ui-20260912";
    static SpeakerMapTextureReview(){EditorApplication.update+=Poll;}
    private static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string request=Output+"/texture-review.request";
        if(!File.Exists(request))return;
        File.Delete(request);
        try
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new InvalidOperationException("Expected TEST scene.");
            var roots=scene.GetRootGameObjects().Where(r=>r.name=="Entrance Speaker Map"||r.name=="Opposite Gate Speaker Map").ToArray();
            if(roots.Length!=2)throw new InvalidOperationException("Expected both map panels.");
            const string path="Assets/_Shinjuku/UI/SpeakerMap/FloorPlan.png";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth,out int sourceHeight);
            // Existing import settings rescale NPOT textures; validate original map
            // dimensions without changing that unrelated rendering configuration.
            if(!texture||sourceWidth!=1800||sourceHeight!=1200)throw new InvalidOperationException("Map source dimensions changed.");
            foreach(var root in roots)
            {
                var canvas=(RectTransform)root.transform.Find("MapCanvas");
                var floor=(RectTransform)canvas.Find("FloorPlan");
                if(floor.sizeDelta!=new Vector2(1800,1200)||floor.GetComponent<UnityEngine.UI.RawImage>().texture!=texture)
                    throw new InvalidOperationException("Map texture reference or size changed: "+root.name);
                string before=EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>());
                SpeakerMapSetup.CapturePanel(canvas,Output+"/"+root.name+"-texture-review.png");
                if(before!=EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>()))throw new InvalidOperationException("Map configuration changed.");
            }
            File.WriteAllText(Output+"/texture-review.done","PASS: both maps reference updated 1800x1200 texture; map dimensions and configuration unchanged; previews captured. No scene save or collider changes.");
        }
        catch(Exception error){File.WriteAllText(Output+"/texture-review.failed",error.ToString());Debug.LogException(error);}
    }
}
