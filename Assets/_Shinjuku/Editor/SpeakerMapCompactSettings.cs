using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using UdonSharpEditor;
using UdonSharp;

/// <summary>Targeted compact footer: shared language consumer, no speaker/network changes.</summary>
[InitializeOnLoad]
public static class SpeakerMapCompactSettings
{
    private const string Output="output/speaker-map-ui-20260912";
    private const string AssetsPath="Assets/_Shinjuku/UI/SpeakerMap";
    static SpeakerMapCompactSettings(){EditorApplication.update+=Poll;}
    private static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string request=Output+"/compact-settings.request";
        if(!File.Exists(request))return;
        File.Delete(request);
        try{Apply();}catch(Exception e){File.WriteAllText(Output+"/compact-settings.failed",DateTime.Now+"\n"+e);Debug.LogException(e);}
    }
    private static void Record(UnityEngine.Object o)
    {
        EditorUtility.SetDirty(o);if(PrefabUtility.IsPartOfPrefabInstance(o))PrefabUtility.RecordPrefabInstancePropertyModifications(o);
    }
    private static void Sync(SpeakerMapPanel p){UdonSharpEditorUtility.CopyProxyToUdon(p);Record(p);Record(UdonSharpEditorUtility.GetBackingUdonBehaviour(p));}
    private static Sprite Outline()
    {
        string path=AssetsPath+"/UnifiedSettingsOutlineBCompact.png";
        if(!File.Exists(path))
        {
            const int w=2400,h=624;const float radius=56,stroke=5;
            var texture=new Texture2D(w,h,TextureFormat.RGBA32,false);var pixels=new Color32[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                float px=x+.5f,py=y+.5f;
                float dx=px-Mathf.Clamp(px,radius,w-radius),dy=py-Mathf.Clamp(py,radius,h-radius);
                float d=Mathf.Sqrt(dx*dx+dy*dy);
                float alpha=Mathf.Clamp01(radius-d+.5f)*Mathf.Clamp01(d-radius+stroke+.5f);
                pixels[y*w+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(alpha*255));
            }
            texture.SetPixels32(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.textureType=TextureImporterType.Sprite;imp.maxTextureSize=4096;imp.mipmapEnabled=false;imp.alphaIsTransparency=true;imp.textureCompression=TextureImporterCompression.Uncompressed;imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    public static void RefreshLayout(SpeakerMapPanel panel) { Layout(panel,Outline()); }
    private static void Layout(SpeakerMapPanel panel,Sprite outlineSprite)
    {
        var canvas=(RectTransform)panel.transform.Find("MapCanvas");
        foreach(string required in new[]{"FloorPlan/SpeakerMarkers","LocalSettings/SettingsOutline","LocalSettings/LocalOnly","LocalSettings/Vehicles/Switch","LocalSettings/Ambient/Switch","SpeakerUsage/Summary","SpeakerUsage/StatusDot"})
            if(!canvas.Find(required))throw new Exception("Missing existing B-layout element: "+required);
        var floor=(RectTransform)canvas.Find("FloorPlan");var raw=floor.GetComponent<UnityEngine.UI.RawImage>();
        var marker=(RectTransform)floor.Find("SpeakerMarkers");var markerWorld=marker.position;
        bool fitted=panel.GetComponent<SpeakerMap>().mapViewport!=new Vector4(0,0,1,1);
        if(marker.rect.size!=(fitted?floor.rect.size:new Vector2(1800,1200))||floor.sizeDelta.x!=1610)throw new Exception("Unexpected calibrated map layout");
        // Trim only 30 source pixels at the bottom; preserve any independently authored top crop.
        float cut=30-raw.uvRect.y*1200;
        if(cut>.01f)
        {
            var children=canvas.Cast<RectTransform>().ToArray();var positions=children.Select(c=>c.position).ToArray();
            canvas.position+=canvas.TransformVector(new Vector3(0,cut*.5f,0));
            for(int i=0;i<children.Length;i++)children[i].position=positions[i];
            floor.anchoredPosition=Vector2.zero;floor.sizeDelta-=new Vector2(0,cut);
            foreach(RectTransform c in floor){c.anchoredPosition-=new Vector2(0,cut*.5f);Record(c);}
            var uv=raw.uvRect;uv.y+=cut/1200;uv.height-=cut/1200;raw.uvRect=uv;
        }
        if(Vector3.Distance(markerWorld,marker.position)>.0001f)throw new Exception("Marker world position changed");
        canvas.sizeDelta=floor.sizeDelta+new Vector2(40,40);
        var board=(RectTransform)canvas.Find("Board");var frame=(RectTransform)canvas.Find("MetalFrame");
        board.anchoredPosition=frame.anchoredPosition=Vector2.zero;board.sizeDelta=canvas.sizeDelta;frame.sizeDelta=canvas.sizeDelta+new Vector2(40,40);
        var collider=canvas.GetComponent<BoxCollider>();collider.size=new Vector3(canvas.sizeDelta.x,canvas.sizeDelta.y,2);
        var sourceOffset=SpeakerMapViewportSetup.SourceOffset(floor);
        var footer=(RectTransform)canvas.Find("LocalSettings");
        footer.anchorMin=footer.anchorMax=footer.pivot=new Vector2(.5f,.5f);
        footer.sizeDelta=new Vector2(600,156);footer.anchoredPosition=new Vector2(-481,sourceOffset.y-468);
        var outline=(RectTransform)footer.Find("SettingsOutline");outline.anchoredPosition=Vector2.zero;outline.sizeDelta=new Vector2(600,156);
        outline.GetComponent<UnityEngine.UI.Image>().sprite=outlineSprite;
        outline.GetComponent<UnityEngine.UI.Image>().color=new Color(.553f,.588f,.522f,1);
        outline.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
        var title=footer.Find("LocalOnly").GetComponent<TextMeshProUGUI>();title.rectTransform.anchoredPosition=new Vector2(-176,48);title.rectTransform.sizeDelta=new Vector2(200,36);title.fontSize=23;title.enableWordWrapping=false;
        title.color=new Color(.314f,.337f,.294f,1);title.alignment=TextAlignmentOptions.MidlineLeft;
        panel.settingsTitle=title;
        panel.settingsTitleWidths=new Vector3(title.GetPreferredValues("Local Settings").x,title.GetPreferredValues("ローカル設定").x,title.GetPreferredValues("로컬 설정").x);
        var rule=outline.Find("HeadingRule") as RectTransform;
        if(!rule){rule=new GameObject("HeadingRule",typeof(RectTransform),typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();rule.SetParent(outline,false);}
        rule.anchorMin=rule.anchorMax=rule.pivot=Vector2.one*.5f;rule.gameObject.layer=outline.gameObject.layer;
        var ruleImage=rule.GetComponent<UnityEngine.UI.Image>();ruleImage.color=outline.GetComponent<UnityEngine.UI.Image>().color;ruleImage.raycastTarget=false;
        panel.settingsHeadingRule=null;
        foreach(Transform child in outline)if(child.name=="HeadingRule"){child.gameObject.SetActive(false);Record(child.gameObject);}
        // B: a continuous shared outline, with settings and read-only occupancy separated visually.
        StyleButton(footer,"Vehicles",-194,164,panel.vehicleLabel);
        StyleButton(footer,"Ambient",-22,164,panel.ambientLabel);
        var divider=footer.Find("UsageDivider") as RectTransform;
        if(!divider){divider=new GameObject("UsageDivider",typeof(RectTransform),typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();divider.SetParent(footer,false);}
        divider.anchorMin=divider.anchorMax=divider.pivot=Vector2.one*.5f;divider.gameObject.layer=footer.gameObject.layer;
        divider.anchoredPosition=new Vector2(84,-11);divider.sizeDelta=new Vector2(1.5f,94);
        var dividerImage=divider.GetComponent<UnityEngine.UI.Image>();dividerImage.color=new Color(.635f,.667f,.6f,1);dividerImage.raycastTarget=false;
        var usage=(RectTransform)canvas.Find("SpeakerUsage");usage.anchoredPosition=new Vector2(-291,footer.anchoredPosition.y-11);usage.sizeDelta=new Vector2(196,100);
        var usageText=usage.Find("Summary").GetComponent<TextMeshProUGUI>();
        usageText.rectTransform.anchoredPosition=new Vector2(14,-28);usageText.rectTransform.sizeDelta=new Vector2(160,40);usageText.fontSize=24;
        usageText.color=new Color(.314f,.337f,.294f,1);usageText.alignment=TextAlignmentOptions.MidlineLeft;usageText.raycastTarget=false;
        usageText.enableWordWrapping=false;usageText.enableAutoSizing=false;usageText.richText=false;
        var usageTitle=usage.Find("Title") ? usage.Find("Title").GetComponent<TextMeshProUGUI>() : null;
        if(!usageTitle){usageTitle=new GameObject("Title",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();usageTitle.transform.SetParent(usage,false);}
        usageTitle.gameObject.layer=usage.gameObject.layer;usageTitle.font=usageText.font;usageTitle.fontSharedMaterial=usageText.fontSharedMaterial;
        usageTitle.rectTransform.anchorMin=usageTitle.rectTransform.anchorMax=usageTitle.rectTransform.pivot=Vector2.one*.5f;
        usageTitle.rectTransform.anchoredPosition=new Vector2(0,20);usageTitle.rectTransform.sizeDelta=new Vector2(180,36);
        usageTitle.fontSize=22;usageTitle.color=usageText.color;usageTitle.alignment=TextAlignmentOptions.MidlineLeft;
        usageTitle.enableWordWrapping=false;usageTitle.enableAutoSizing=false;usageTitle.richText=false;usageTitle.raycastTarget=false;
        var usageDot=(RectTransform)usage.Find("StatusDot");usageDot.sizeDelta=new Vector2(12,12);usageDot.anchoredPosition=new Vector2(-84,-28);
        var map=panel.GetComponent<SpeakerMap>();map.worldLanguage=panel.worldLanguage;map.usageTitle=usageTitle;map._RefreshMap();
        UdonSharpEditorUtility.CopyProxyToUdon(map);Record(map);Record(UdonSharpEditorUtility.GetBackingUdonBehaviour(map));
        // Landmark placement belongs to its dedicated authoring helpers, not footer layout.
        var music=(RectTransform)floor.Find("MusicShopLabel");
        var icon=(RectTransform)floor.Find("MusicShopIcon");
        var gallery=(RectTransform)floor.Find("GalleryLabel");
        panel._RefreshSettings();
        foreach(var o in new UnityEngine.Object[]{canvas,floor,raw,board,frame,collider,footer,outline,outline.GetComponent<UnityEngine.UI.Image>(),title,title.rectTransform,rule,ruleImage,divider,dividerImage,usage,usageText,usageText.rectTransform,usageTitle,usageTitle.rectTransform,usageDot,music,icon,gallery})Record(o);
        Sync(panel);
    }
    private static void StyleButton(Transform footer,string name,float x,float width,TextMeshProUGUI label)
    {
        var button=(RectTransform)footer.Find(name);button.anchoredPosition=new Vector2(x,-15);button.sizeDelta=new Vector2(width,104);
        label.rectTransform.anchoredPosition=new Vector2(0,24);label.rectTransform.sizeDelta=new Vector2(width,36);
        label.fontSize=22;label.enableAutoSizing=false;label.enableWordWrapping=false;label.alignment=TextAlignmentOptions.MidlineLeft;label.color=new Color(.314f,.337f,.294f,1);
        var track=(RectTransform)button.Find("Switch");track.anchoredPosition=new Vector2(-20,-24);
        foreach(var o in new UnityEngine.Object[]{button,label,label.rectTransform,track})Record(o);
    }
    private static void Apply()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Open TEST in edit mode");
        var panels=UnityEngine.Object.FindObjectsOfType<SpeakerMapPanel>(true).Where(p=>p.gameObject.scene==scene).ToArray();
        var languages=UnityEngine.Object.FindObjectsOfType<WorldLanguage>(true).Where(p=>p.gameObject.scene==scene).ToArray();
        if(panels.Length!=2||languages.Length!=1)throw new Exception("Expected two boards and one shared WorldLanguage");
        AssetDatabase.ImportAsset(AssetsPath+"/FloorPlan.png",ImportAssetOptions.ForceUpdate);
        // Editor has no VRChat language API. Prime only the nonserialized editor cache;
        // runtime Start still initializes it from VRChat, with no new language selector.
        typeof(WorldLanguage).GetField("initialized",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(languages[0],true);
        languages[0].currentLanguage=languages[0].ResolveLanguage(languages[0].languageCode);
        UdonSharpProgramAsset.CompileAllCsPrograms();
        var font=panels[0].vehicleLabel.font;
        string required="Local Settingsローカル設定로컬 설정Vehicles車両차량Ambient sound環境音환경음Speakers 0123456789 / in useスピーカー使用中스피커사용 중";
        if(!font.HasCharacters(required))
        {
            font.atlasPopulationMode=AtlasPopulationMode.Dynamic;
            try{if(!font.TryAddCharacters(required,out string missing))throw new Exception("Missing settings glyphs: "+missing);}
            finally{font.atlasPopulationMode=AtlasPopulationMode.Static;}
            foreach(var atlas in font.atlasTextures){if(!AssetDatabase.Contains(atlas))AssetDatabase.AddObjectToAsset(atlas,font);EditorUtility.SetDirty(atlas);}
            EditorUtility.SetDirty(font);EditorUtility.SetDirty(font.material);
        }
        var sprite=Outline();
        var markerWorlds=panels.Select(p=>p.transform.Find("MapCanvas/FloorPlan/SpeakerMarkers").position).ToArray();
        foreach(var panel in panels){Undo.RegisterFullObjectHierarchyUndo(panel.gameObject,"B unified settings and speaker status");panel.worldLanguage=languages[0];}
        const string path=AssetsPath+"/SpeakerMap.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
        try{Layout(prefab.GetComponent<SpeakerMapPanel>(),sprite);PrefabUtility.SaveAsPrefabAsset(prefab,path);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
        foreach(var panel in panels){panel.worldLanguage=languages[0];Layout(panel,sprite);}
        var report=new StringBuilder();
        for(int i=0;i<panels.Length;i++)
        {
            var floor=(RectTransform)panels[i].transform.Find("MapCanvas/FloorPlan");var marker=(RectTransform)floor.Find("SpeakerMarkers");var uv=floor.GetComponent<UnityEngine.UI.RawImage>().uvRect;
            bool fitted=panels[i].GetComponent<SpeakerMap>().mapViewport!=new Vector4(0,0,1,1);
            if(Vector3.Distance(marker.position,markerWorlds[i])>.0001f||marker.rect.size!=(fitted?floor.rect.size:new Vector2(1800,1200)))throw new Exception("Final marker calibration changed");
            if(Mathf.Abs(uv.y*1200-30)>.01f||Mathf.Abs(uv.yMax*1200-1110)>.01f)throw new Exception("Top/bottom crop mismatch");
            report.AppendLine(panels[i].name+": floor="+floor.sizeDelta+" UV="+uv+" overlay="+marker.anchoredPosition+"; top crop retained, marker world calibration PASS");
        }
        var preview=new GameObject("Isolated footer language preview"){hideFlags=HideFlags.HideAndDontSave};preview.SetActive(false);
        var language=preview.AddComponent<WorldLanguage>();
        typeof(WorldLanguage).GetField("initialized",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(language,true);
        try
        {
            foreach(var panel in panels)
            {
                var map=panel.GetComponent<SpeakerMap>();var usage=panel.transform.Find("MapCanvas/SpeakerUsage/Summary").GetComponent<TextMeshProUGUI>();
                var live=panel.worldLanguage;var landmark=panel.transform.Find("MapCanvas/FloorPlan/MusicShopLabel").GetComponent<TextMeshProUGUI>();string before=landmark.text;
                try
                {
                    panel.worldLanguage=language;map.worldLanguage=language;
                    foreach(string code in new[]{"en","ja","ko","fr"})
                    {
                        language.languageCode=code;language.currentLanguage=language.ResolveLanguage(code);panel._OnWorldLanguageChanged();map._OnWorldLanguageChanged();
                        foreach(var label in new[]{panel.settingsTitle,panel.vehicleLabel,panel.ambientLabel,map.usageTitle,usage})
                        {
                            label.ForceMeshUpdate();if(label.text.Contains("\n")||label.isTextOverflowing||!label.font.HasCharacters(label.text))throw new Exception("Invalid localized label "+code+" "+label.text);
                        }
                        if(landmark.text!=before)throw new Exception("Map landmark language changed");
                        if(panel.settingsHeadingRule!=null)throw new Exception("B outline must remain continuous");
                        var canvas=(RectTransform)panel.transform.Find("MapCanvas");SpeakerMapSetup.VerifyPresentation(canvas,Output+"/"+panel.name+"-layout-B-compact-"+code+".txt");
                        if(code!="fr")SpeakerMapSetup.CapturePanel(canvas,Output+"/"+panel.name+"-layout-B-compact-"+code+".png");
                        report.AppendLine(panel.name+" "+code+": "+panel.settingsTitle.text+" / "+panel.vehicleLabel.text+" / "+panel.ambientLabel.text+" / "+usage.text+"; one line, glyphs, bounds, unchanged landmarks PASS");
                    }
                }
                finally{panel.worldLanguage=live;map.worldLanguage=live;panel._RefreshSettings();map._RefreshMap();Sync(panel);UdonSharpEditorUtility.CopyProxyToUdon(map);Record(map);Record(UdonSharpEditorUtility.GetBackingUdonBehaviour(map));}
                var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
                if(!backing.publicVariables.TryGetVariableValue("worldLanguage",out VRC.Udon.UdonBehaviour linked)||linked!=UdonSharpEditorUtility.GetBackingUdonBehaviour(live))throw new Exception("Missing Udon shared language reference");
                if(!backing.programSource.SerializedProgramAsset.RetrieveProgram().EntryPoints.HasExportedSymbol("_OnWorldLanguageChanged"))throw new Exception("Missing language event");
                var mapBacking=UdonSharpEditorUtility.GetBackingUdonBehaviour(map);
                if(!mapBacking.publicVariables.TryGetVariableValue("worldLanguage",out VRC.Udon.UdonBehaviour mapLanguage)||mapLanguage!=linked||!mapBacking.programSource.SerializedProgramAsset.RetrieveProgram().EntryPoints.HasExportedSymbol("_OnWorldLanguageChanged"))throw new Exception("Missing occupancy shared language binding/event");
                if(!mapBacking.publicVariables.TryGetVariableValue("usageTitle",out TextMeshProUGUI boundTitle)||boundTitle!=map.usageTitle)throw new Exception("Missing B speaker title Udon binding");
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(preview);}
        TestLocalizedUsage(font,report);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/compact-settings.done",DateTime.Now+"\n"+report+"Preserved shared service, six speakers, calibrated overlay, top crop; bottom trimmed 30px. Original TEST remains unsaved.\n");
    }
    private static void TestLocalizedUsage(TMP_FontAsset font,StringBuilder report)
    {
        var host=new GameObject("Isolated localized occupancy test"){hideFlags=HideFlags.HideAndDontSave};host.SetActive(false);
        try
        {
            var map=host.AddComponent<SpeakerMap>();var language=host.AddComponent<WorldLanguage>();
            typeof(WorldLanguage).GetField("initialized",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(language,true);map.worldLanguage=language;
            var textGo=new GameObject("Text",typeof(RectTransform),typeof(TextMeshProUGUI));textGo.transform.SetParent(host.transform,false);
            var label=textGo.GetComponent<TextMeshProUGUI>();label.font=font;label.fontSize=22;label.rectTransform.sizeDelta=new Vector2(360,40);label.enableWordWrapping=false;
            var titleGo=new GameObject("Title",typeof(RectTransform),typeof(TextMeshProUGUI));titleGo.transform.SetParent(host.transform,false);
            map.usageTitle=titleGo.GetComponent<TextMeshProUGUI>();map.usageTitle.font=font;map.usageTitle.fontSize=22;map.usageTitle.rectTransform.sizeDelta=new Vector2(180,36);
            label.fontSize=24;label.rectTransform.sizeDelta=new Vector2(160,40);
            var dotGo=new GameObject("Dot",typeof(RectTransform),typeof(UnityEngine.UI.Image));dotGo.transform.SetParent(host.transform,false);var dot=dotGo.GetComponent<UnityEngine.UI.Image>();
            var slots=new SpeakerController[6];for(int i=0;i<6;i++){var go=new GameObject("Slot "+i);go.transform.SetParent(host.transform);slots[i]=go.AddComponent<SpeakerController>();}
            var so=new SerializedObject(map);so.FindProperty("usageSummary").objectReferenceValue=label;so.FindProperty("usageDot").objectReferenceValue=dot;
            var refs=so.FindProperty("speakerControllers");refs.arraySize=6;for(int i=0;i<6;i++)refs.GetArrayElementAtIndex(i).objectReferenceValue=slots[i];so.ApplyModifiedPropertiesWithoutUndo();
            foreach(string code in new[]{"en","ja","ko","fr"})foreach(int count in new[]{0,2,6,0})
            {
                language.languageCode=code;language.currentLanguage=language.ResolveLanguage(code);
                for(int i=0;i<6;i++)slots[i].isSpeakerTaken=i<count;map._OnWorldLanguageChanged();
                string expected=code=="ja"?count+" / 6 使用中":code=="ko"?count+" / 6 사용 중":count+" / 6 in use";
                string expectedTitle=code=="ja"?"スピーカー":code=="ko"?"스피커":"Speakers";
                if(map.usageTitle.text!=expectedTitle)throw new Exception("Occupancy title localization mismatch");
                label.ForceMeshUpdate();
                if(label.text!=expected||label.text.Contains("\n")||label.isTextOverflowing)throw new Exception("Occupancy localization/count mismatch: "+code+" "+label.text);
                if(count==0&&dot.color.r<.6f||count>0&&dot.color.r>.2f)throw new Exception("Occupancy state color mismatch");
            }
            report.AppendLine("Isolated occupancy: en/ja/ko/English fallback x 0,2,6,0 used; exact one-line text, fit, gray/green PASS. No live occupancy/network state changed.");
        }
        finally{UnityEngine.Object.DestroyImmediate(host);}
    }
}
