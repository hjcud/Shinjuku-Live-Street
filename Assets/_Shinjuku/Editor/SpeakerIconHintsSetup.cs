using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Uses only allowlisted uGUI components and built-in sprites; no custom runtime Graphic.
[InitializeOnLoad]
public static class SpeakerIconHintsSetup
{
    const string Output = "output/speaker-icon-hints-20260914/";
    static readonly string[] Paths = { "Assets/_Shinjuku/Scenes/TEST_PC.unity", "Assets/_Shinjuku/Scenes/TEST_Quest.unity", "Assets/_ShinjukuExhibition/Scenes/TechnicalExhibition.unity" };
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Color Ivory = new Color(.90f, .88f, .81f, .85f);
    static readonly Color Ink = new Color(.13f, .14f, .13f, .94f);
    static readonly Color Red = new Color(.86f, .31f, .28f, 1f);
    static Sprite round, circle;
    static SpeakerIconHintsSetup() { EditorApplication.update += Poll; }
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    static object Call(object target, string name, params object[] values) => target.GetType().GetMethod(name, Flags).Invoke(target, values);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string vrControllerV2 = Output + "refine-vr-controller-v2.request";
        if (File.Exists(vrControllerV2))
        {
            File.Delete(vrControllerV2);
            try { RefineVrControllers(); } catch(Exception e) { File.WriteAllText(Output+"refine-vr-controller-v2.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        string vrController = Output + "refine-vr-controller-v1.request";
        if (File.Exists(vrController))
        {
            File.Delete(vrController);
            try { RefineVrControllers(); } catch(Exception e) { File.WriteAllText(Output+"refine-vr-controller.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        string normalize = Output + "normalize-canvas-v1.request";
        if (File.Exists(normalize))
        {
            File.Delete(normalize);
            try { NormalizeCanvases(); } catch(Exception e) { File.WriteAllText(Output+"normalize-canvas.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        string audit = Output + "canvas-audit-v1.request";
        if (File.Exists(audit))
        {
            File.Delete(audit);
            try { AuditCanvases(); } catch(Exception e) { File.WriteAllText(Output+"canvas-audit.failed",e.ToString()); }
            return;
        }
        string mouse = Output + "refine-mouse-v2.request";
        if (File.Exists(mouse))
        {
            File.Delete(mouse);
            try { RefineMouse(); } catch(Exception e) { File.WriteAllText(Output+"refine-mouse.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        string capture = Output + "capture-scene-v2.request";
        if (File.Exists(capture))
        {
            File.Delete(capture);
            try { CaptureInScene(); } catch(Exception e) { File.WriteAllText(Output+"capture.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        string request = Output + "apply-v3.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        try { Apply(); } catch (Exception e) { File.WriteAllText(Output + "apply.failed", e.ToString()); Debug.LogException(e); }
    }
    static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var found = parent.Find(name);
        var rect = found ? found.GetComponent<RectTransform>() : new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        if (!found) rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition3D = new Vector3(pos.x, pos.y, 0);
        rect.sizeDelta = size; rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
        return rect;
    }
    static void AuditCanvases()
    {
        var report=new StringBuilder(); var original=SceneManager.GetActiveScene();
        report.AppendLine("Active="+original.path+" play="+EditorApplication.isPlaying);
        foreach(string path in Paths)
        {
            var scene=SceneManager.GetSceneByPath(path); bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                var manager=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SpeakerManager>(true)).Single();
                report.AppendLine("SCENE "+path);
                foreach(string field in new[]{"speakerPlacements","messageUI","ringObject","holoSpeaker"})
                {
                    var go=Get<GameObject>(manager,field);report.AppendLine("FIELD "+field+" = "+go.name);
                    for(var t=go.transform;t;t=t.parent)report.AppendLine("  "+t.name+" localScale="+t.localScale.ToString("F6")+" worldScale="+t.lossyScale.ToString("F6"));
                    foreach(var canvas in go.GetComponentsInChildren<Canvas>(true))
                    {
                        var r=(RectTransform)canvas.transform;var corners=new Vector3[4];r.GetWorldCorners(corners);
                        report.AppendLine("CANVAS "+AnimationUtility.CalculateTransformPath(r,manager.transform.root)+" mode="+canvas.renderMode+" enabled="+canvas.enabled+" active="+r.gameObject.activeSelf+" rect="+r.rect+" scale="+r.lossyScale.ToString("F6")+" worldSize="+Vector3.Distance(corners[0],corners[3])+" x "+Vector3.Distance(corners[0],corners[1]));
                        foreach(var child in r.GetComponentsInChildren<RectTransform>(true).Where(t=>t.parent==r))report.AppendLine(" CHILD "+child.name+" size="+child.rect+" scale="+child.localScale.ToString("F6")+" pos="+child.localPosition);
                    }
                }
                foreach(var anim in Get<GameObject>(manager,"speakerPlacements").GetComponentsInChildren<Animator>(true))
                    if(anim.runtimeAnimatorController)foreach(var clip in anim.runtimeAnimatorController.animationClips.Distinct())
                        foreach(var binding in AnimationUtility.GetCurveBindings(clip))report.AppendLine("CURVE "+anim.name+" "+clip.name+" "+binding.path+" "+binding.propertyName);
            }
            finally { if(opened)EditorSceneManager.CloseScene(scene,true); }
        }
        if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);
        File.WriteAllText(Output+"canvas-audit.done",report.ToString());
    }
    // Keep every rendered descendant at the same world size; scale the Canvas, not only its contents.
    static void NormalizeCanvases()
    {
        var report=new StringBuilder();var original=SceneManager.GetActiveScene();
        try
        {
            foreach(string path in Paths)
            {
                var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
                if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                bool saved=false;
                try
                {
                    var manager=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SpeakerManager>(true)).Single();
                    var canvas=Get<GameObject>(manager,"messageUI").GetComponent<Canvas>();
                    Check(canvas&&canvas.renderMode==RenderMode.WorldSpace,"Expected world-space message Canvas");
                    var root=(RectTransform)canvas.transform;
                    var contents=root.Find("ImageScaleParent") as RectTransform;
                    Check(contents&&root.childCount==1,"Unexpected message hierarchy");
                    Check(contents.localPosition.sqrMagnitude<.000001f,"Unexpected content offset");
                    Check(EditorSceneManager.SaveScene(scene,Output+scene.name+".before-canvas-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true),"Backup failed");
                    var rects=contents.GetComponentsInChildren<RectTransform>(true);
                    var before=rects.Select(r=>{var c=new Vector3[4];r.GetWorldCorners(c);return c;}).ToArray();
                    var oldRootScale=root.localScale;var oldContentScale=contents.localScale;
                    try
                    {
                        Check(Mathf.Abs(oldContentScale.x-oldContentScale.y)<.000001f&&Mathf.Abs(oldContentScale.x-oldContentScale.z)<.000001f,"Expected uniform content scale");
                        root.localScale=Vector3.Scale(oldRootScale,oldContentScale);
                        contents.localScale=Vector3.one;
                        for(int i=0;i<rects.Length;i++)
                        {
                            var after=new Vector3[4];rects[i].GetWorldCorners(after);
                            for(int j=0;j<4;j++)Check(Vector3.Distance(before[i][j],after[j])<.0001f,"UI moved: "+rects[i].name);
                        }
                        var corners=new Vector3[4];root.GetWorldCorners(corners);
                        float width=Vector3.Distance(corners[0],corners[3]);float height=Vector3.Distance(corners[0],corners[1]);
                        Check(width>1f&&width<2f&&height>.5f&&height<1f,"Unexpected Canvas world dimensions");
                        // Exercise the same transform update used while holding/placing, then toggle activation.
                        var oldPos=root.position;var oldRot=root.rotation;bool oldActive=root.gameObject.activeSelf;
                        try
                        {
                            root.gameObject.SetActive(false);root.gameObject.SetActive(true);
                            Call(manager,"SetUITransform",root.gameObject,new Vector3(45,8,-10),new Vector3(45,8,-11));
                            root.GetWorldCorners(corners);
                            Check(Mathf.Abs(Vector3.Distance(corners[0],corners[3])-width)<.0001f,"Activation changed Canvas size");
                        }
                        finally {root.position=oldPos;root.rotation=oldRot;root.gameObject.SetActive(oldActive);}
                        report.AppendLine(scene.name+": Canvas="+width+"m x "+height+"m; "+rects.Length+" UI rects unchanged; activation/pose PASS");
                    }
                    catch {root.localScale=oldRootScale;contents.localScale=oldContentScale;throw;}
                    EditorUtility.SetDirty(root);EditorUtility.SetDirty(contents);EditorSceneManager.MarkSceneDirty(scene);
                    Check(EditorSceneManager.SaveScene(scene),"Scene save failed");saved=true;
                    File.WriteAllText(Output+"normalize-canvas.progress",report.ToString());
                }
                finally {if(opened&&saved)EditorSceneManager.CloseScene(scene,true);}
            }
            File.WriteAllText(Output+"normalize-canvas.done",report+"PASS: all 3 scenes saved; no runtime code/components added.");
        }
        finally {if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);}
    }
    static UnityEngine.UI.Image Shape(Transform parent, string name, Vector2 pos, Vector2 size, Color color, Sprite sprite = null)
    {
        var rect = Rect(parent, name, pos, size);
        var image = rect.GetComponent<UnityEngine.UI.Image>(); if (!image) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = sprite; image.type = sprite == round ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        image.color = color; image.raycastTarget = false; return image;
    }
    static TextMeshProUGUI Text(Transform parent, string name, Vector2 pos, Vector2 size, string text, TextMeshProUGUI source, float fontSize)
    {
        var rect = Rect(parent, name, pos, size);
        var label = rect.GetComponent<TextMeshProUGUI>(); if (!label) label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = source.font; label.fontSharedMaterial = source.fontSharedMaterial;
        label.fontSize = fontSize; label.fontStyle = FontStyles.Normal; label.alignment = TextAlignmentOptions.Midline;
        label.color = new Color(.94f,.92f,.85f,1); label.text = text; label.richText = false;
        label.enableWordWrapping = false; label.enableAutoSizing = false; label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var shadow = label.GetComponent<UnityEngine.UI.Shadow>(); if (!shadow) shadow = label.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0,0,0,.6f); shadow.effectDistance = new Vector2(1.2f,-1.2f);
        return label;
    }
    static void Mouse(Transform parent, bool left)
    {
        Shape(parent, "BodyOutline", Vector2.zero, new Vector2(40,52), Ivory, round).pixelsPerUnitMultiplier=.45f;
        Shape(parent, "BodyInside", Vector2.zero, new Vector2(34,46), Ink, round).pixelsPerUnitMultiplier=.55f;
        Shape(parent, "PressedButton", new Vector2(left ? -8 : 8, 9.5f), new Vector2(14,18), Red, round).pixelsPerUnitMultiplier=1f;
        Shape(parent, "ButtonSplit", new Vector2(0,10), new Vector2(2,20), Ivory);
        Shape(parent, "ButtonBase", new Vector2(0,-1), new Vector2(32,2), Ivory);
        Shape(parent, "WheelWell", new Vector2(0,10), new Vector2(6,12), Ink, round).pixelsPerUnitMultiplier=2f;
        var wheel=Shape(parent, "Wheel", new Vector2(0,10), new Vector2(2.5f,7), Ivory, round);
        wheel.transform.SetAsLastSibling();
    }
    static void RefineMouse()
    {
        round=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");Check(round,"Missing UI sprite");
        var report=new StringBuilder();var original=SceneManager.GetActiveScene();
        foreach(string path in Paths)
        {
            var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            bool saved=false;
            try
            {
                var manager=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SpeakerManager>(true)).Single();
                var root=Get<GameObject>(manager,"desktopPlacementHints");Check(root,"Desktop hint row missing");
                Check(EditorSceneManager.SaveScene(scene,Output+scene.name+".before-mouse-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true),"Backup failed");
                foreach(bool left in new[]{true,false})
                {
                    var icon=root.transform.Find((left?"Place":"Cancel")+"/InputIcon");Check(icon,"Mouse icon missing");Mouse(icon,left);
                    var fill=icon.Find("PressedButton").GetComponent<UnityEngine.UI.Image>();
                    Check((fill.rectTransform.anchoredPosition.x<0)==left&&fill.color==Red,"Wrong mouse button highlight");
                    Check(icon.GetComponentsInChildren<UnityEngine.UI.Image>().All(i=>!i.raycastTarget),"Mouse icon blocks clicks");
                }
                if(scene.name=="TEST_PC")
                {
                    bool oldVr=Get<bool>(manager,"isVrUser");int oldLanguage=Get<int>(manager,"languageIndex");
                    try { Set(manager,"isVrUser",false);Set(manager,"languageIndex",2);Call(manager,"RefreshPlacementHints");CaptureUI(manager,2,false); }
                    finally { Set(manager,"isVrUser",oldVr);Set(manager,"languageIndex",oldLanguage);Call(manager,"RefreshPlacementHints"); }
                }
                EditorSceneManager.MarkSceneDirty(scene);Check(EditorSceneManager.SaveScene(scene),"Save failed");saved=true;
                report.AppendLine("PASS "+path+": two mouse visuals updated, correct red side, raycasts disabled; other hints unchanged.");
            }
            finally{if(opened&&saved)EditorSceneManager.CloseScene(scene,true);}
        }
        if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);
        File.WriteAllText(Output+"refine-mouse.done",report.ToString());
    }
    static void Controller(Transform parent, string hand, TextMeshProUGUI source)
    {
        bool right=hand=="R";float mirror=right ? 1f : -1f;
        var bodyOutline=Shape(parent,"BodyOutline",new Vector2(-mirror*2,-8),new Vector2(22,42),Ivory,round);
        bodyOutline.pixelsPerUnitMultiplier=.8f;bodyOutline.rectTransform.localRotation=Quaternion.Euler(0,0,mirror*7);
        var bodyInside=Shape(parent,"BodyInside",new Vector2(-mirror*2,-8),new Vector2(17,37),Ink,round);
        bodyInside.pixelsPerUnitMultiplier=.9f;bodyInside.rectTransform.localRotation=Quaternion.Euler(0,0,mirror*7);
        var headOutline=Shape(parent,"HeadOutline",new Vector2(mirror*2,11),new Vector2(38,22),Ivory,round);
        headOutline.pixelsPerUnitMultiplier=.75f;headOutline.rectTransform.localRotation=Quaternion.Euler(0,0,-mirror*8);
        var headInside=Shape(parent,"HeadInside",new Vector2(mirror*2,11),new Vector2(33,17),Ink,round);
        headInside.pixelsPerUnitMultiplier=.85f;headInside.rectTransform.localRotation=Quaternion.Euler(0,0,-mirror*8);
        var trigger=Shape(parent,"Trigger",new Vector2(mirror*9,2),new Vector2(8,18),Red,round);
        trigger.pixelsPerUnitMultiplier=1.2f;trigger.rectTransform.localRotation=Quaternion.Euler(0,0,-mirror*7);
        var handLabel=Text(parent,"Hand",new Vector2(-mirror*2,-14),new Vector2(24,24),hand,source,12);
        handLabel.alignment=TextAlignmentOptions.Center;
        bodyOutline.transform.SetSiblingIndex(0);bodyInside.transform.SetSiblingIndex(1);
        headOutline.transform.SetSiblingIndex(2);headInside.transform.SetSiblingIndex(3);
        trigger.transform.SetSiblingIndex(4);handLabel.transform.SetSiblingIndex(5);
    }
    static string SelectorSignature(SpeakerManager manager)
    {
        var root=Get<RectTransform>(manager,"modelSelector");
        return string.Join("|",root.GetComponentsInChildren<RectTransform>(true).Select(r=>AnimationUtility.CalculateTransformPath(r,root)+":"+r.anchoredPosition3D+":"+r.sizeDelta+":"+r.localRotation.eulerAngles));
    }
    static void RefineVrControllers()
    {
        round=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");Check(round,"Missing UI sprite");
        var report=new StringBuilder();var original=SceneManager.GetActiveScene();
        foreach(string path in Paths)
        {
            var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            bool saved=false;
            try
            {
                var manager=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SpeakerManager>(true)).Single();
                var root=Get<GameObject>(manager,"vrPlacementHints");var source=Get<TextMeshProUGUI>(manager,"placementGuideText");
                Check(root&&source,"VR hint row missing");string selectorBefore=SelectorSignature(manager);
                Check(EditorSceneManager.SaveScene(scene,Output+scene.name+".before-vr-controller-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true),"Backup failed");
                foreach(bool place in new[]{true,false})
                {
                    string hand=place?"R":"L";var icon=root.transform.Find((place?"Place":"Cancel")+"/InputIcon") as RectTransform;
                    Check(icon,"VR controller icon missing");icon.sizeDelta=new Vector2(44,58);
                    var layout=icon.GetComponent<UnityEngine.UI.LayoutElement>();Check(layout,"Icon layout missing");layout.preferredWidth=44;layout.minWidth=44;
                    Controller(icon,hand,source);
                    var trigger=icon.Find("Trigger").GetComponent<UnityEngine.UI.Image>();
                    Check(trigger.color==Red&&Mathf.Sign(trigger.rectTransform.anchoredPosition.x)==(place?1:-1),"Trigger highlight is on wrong side");
                    Check(icon.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g=>!g.raycastTarget),"VR icon blocks raycasts");
                }
                Check(selectorBefore==SelectorSignature(manager),"Top model selector changed");
                if(scene.name=="TEST_PC")
                {
                    bool oldVr=Get<bool>(manager,"isVrUser");int oldLanguage=Get<int>(manager,"languageIndex");
                    try {Set(manager,"isVrUser",true);Set(manager,"languageIndex",2);Call(manager,"RefreshPlacementHints");CaptureUI(manager,2,true);}
                    finally {Set(manager,"isVrUser",oldVr);Set(manager,"languageIndex",oldLanguage);Call(manager,"RefreshPlacementHints");}
                }
                EditorSceneManager.MarkSceneDirty(scene);Check(EditorSceneManager.SaveScene(scene),"Save failed");saved=true;
                report.AppendLine("PASS "+path+": lower R/L controllers refined; top model selector unchanged; raycasts disabled.");
            }
            finally {if(opened&&saved)EditorSceneManager.CloseScene(scene,true);}
        }
        if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);
        File.WriteAllText(Output+"refine-vr-controller-v2.done",report.ToString());
    }
    static UnityEngine.UI.Image[] Chevron(Transform parent, string name, Vector2 pos, bool previous, bool vertical)
    {
        var root = Rect(parent, name, pos, new Vector2(22,26));
        if (vertical) root.localRotation = Quaternion.Euler(0,0,-90);
        var a = Shape(root, "Upper", new Vector2(0,4), new Vector2(2.2f,12), Ivory);
        var b = Shape(root, "Lower", new Vector2(0,-4), new Vector2(2.2f,12), Ivory);
        a.rectTransform.localRotation = Quaternion.Euler(0,0,previous ? -42 : 42);
        b.rectTransform.localRotation = Quaternion.Euler(0,0,previous ? 42 : -42);
        return new[] { a,b };
    }
    static UnityEngine.UI.Image Key(Transform parent, string name, float x, string key, TextMeshProUGUI source, bool vr)
    {
        var root = Rect(parent, name, new Vector2(x,0), new Vector2(44,44));
        var border = Shape(root,"Outline",Vector2.zero,new Vector2(44,44),Ivory,round);
        Shape(root,"Inside",Vector2.zero,new Vector2(40,40),Ink,round);
        if (!vr) Text(root,"Key",Vector2.zero,new Vector2(40,40),key,source,27);
        else
        {
            Shape(root,"StickBase",new Vector2(0,-8),new Vector2(25,12),Ivory,circle);
            Shape(root,"StickStem",new Vector2(0,-1),new Vector2(4,15),Ivory,round);
            Shape(root,"StickCap",new Vector2(0,7),new Vector2(16,10),Ivory,circle);
            Text(root,"Hand",new Vector2(15,-15),new Vector2(24,28),"R",source,13);
        }
        return border;
    }
    static void Configure(SpeakerManager manager)
    {
        var source = Get<TextMeshProUGUI>(manager,"placementGuideText"); Check(source,"Missing existing guide");
        var actions = Rect(source.transform.parent,"PlacementActionHints",new Vector2(0,-465),new Vector2(640,80));
        var places = new List<TextMeshProUGUI>(); var cancels = new List<TextMeshProUGUI>();
        foreach (bool vr in new[] { false,true })
        {
            var root = Rect(actions,vr ? "VR" : "Desktop",Vector2.zero,new Vector2(640,80));
            var row = root.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>(); if(!row)row=root.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            row.childAlignment=TextAnchor.MiddleCenter;row.spacing=90;row.childControlWidth=true;row.childControlHeight=false;row.childForceExpandWidth=false;row.childForceExpandHeight=false;
            foreach(bool place in new[] {true,false})
            {
                var group = Rect(root,place ? "Place" : "Cancel",new Vector2(place ? -155 : 155,0),new Vector2(290,74));
                var item = group.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();if(!item)item=group.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                item.childAlignment=TextAnchor.MiddleCenter;item.spacing=16;item.childControlWidth=true;item.childControlHeight=false;item.childForceExpandWidth=false;item.childForceExpandHeight=false;
                var icon = Rect(group,"InputIcon",new Vector2(-108,0),new Vector2(40,56));
                var iconLayout=icon.GetComponent<UnityEngine.UI.LayoutElement>();if(!iconLayout)iconLayout=icon.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();iconLayout.preferredWidth=40;iconLayout.minWidth=40;
                if (vr) Controller(icon,place ? "R" : "L",source); else Mouse(icon,place);
                var label = Text(group,"Action",new Vector2(32,0),new Vector2(225,65),place ? "Place" : "Cancel",source,32);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                // Tight icon-to-text gap, enough room for Japanese キャンセル.
                label.rectTransform.anchoredPosition = new Vector2(33,0);
                if(place)places.Add(label);else cancels.Add(label);
            }
            Set(manager,vr ? "vrPlacementHints" : "desktopPlacementHints",root.gameObject);
        }
        Set(manager,"placeActionLabels",places.ToArray()); Set(manager,"cancelActionLabels",cancels.ToArray());
        var placements = Get<GameObject>(manager,"speakerPlacements");
        var selector = Rect(placements.transform,"ModelSelector",Vector2.zero,new Vector2(410,74));
        var canvas = selector.GetComponent<Canvas>(); if(!canvas)canvas=selector.gameObject.AddComponent<Canvas>();
        canvas.renderMode=RenderMode.WorldSpace; canvas.sortingOrder=1;
        selector.localScale=Vector3.one*.0019f;
        var modelName=Text(selector,"ModelName",Vector2.zero,new Vector2(170,56),"POLY",source,29);
        modelName.characterSpacing=2f; Set(manager,"modelNameLabel",modelName);
        Set(manager,"modelNameRect",modelName.rectTransform);
        var prev = new List<UnityEngine.UI.Image>(); var next = new List<UnityEngine.UI.Image>();
        foreach(bool vr in new[]{false,true})
        {
            var keys=Rect(selector,vr ? "VR" : "Desktop",Vector2.zero,new Vector2(410,74));
            prev.Add(Key(keys,"PreviousKey",-151,"Q",source,vr));
            next.Add(Key(keys,"NextKey",151,"E",source,vr));
            prev.AddRange(Chevron(keys,"PreviousArrow",new Vector2(-103,0),true,vr));
            next.AddRange(Chevron(keys,"NextArrow",new Vector2(103,0),false,vr));
            Set(manager,vr ? "vrModelKeys" : "desktopModelKeys",keys.gameObject);
        }
        Set(manager,"modelSelector",selector); Set(manager,"previousModelAccents",prev.ToArray()); Set(manager,"nextModelAccents",next.ToArray());
        Call(manager,"RefreshPlacementHints"); Call(manager,"SetModelHintFeedback",0);
        EditorUtility.SetDirty(source);
    }
    static void Apply()
    {
        Check(!UdonSharpProgramAsset.AnyUdonSharpScriptHasError(),"Udon compile error");
        round=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        circle=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"); Check(round&&circle,"Missing built-in UI sprites");
        var report=new StringBuilder(); var original=SceneManager.GetActiveScene();
        foreach(string path in Paths)
        {
            var scene=SceneManager.GetSceneByPath(path); bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            bool saved=false;
            try
            {
                var manager=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SpeakerManager>(true)).Single();
                Check(EditorSceneManager.SaveScene(scene,Output+scene.name+".before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true),"Backup failed");
                Configure(manager); Verify(manager,report);
                UdonSharpEditorUtility.CopyProxyToUdon(manager);
                var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
                Check(backing.publicVariables.TryGetVariableValue("modelSelector",out RectTransform linked)&&linked==Get<RectTransform>(manager,"modelSelector"),"Compiled selector reference missing");
                EditorUtility.SetDirty(manager); EditorUtility.SetDirty(backing);
                EditorSceneManager.MarkSceneDirty(scene); Check(EditorSceneManager.SaveScene(scene),"Save failed"); saved=true;
                report.AppendLine("SAVED "+path); File.WriteAllText(Output+"apply.progress",report.ToString());
            }
            finally { if(opened&&saved)EditorSceneManager.CloseScene(scene,true); }
        }
        if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);
        File.WriteAllText(Output+"apply.done",report+"PASS 3 scenes. Editor checks only; VRChat hardware not exercised.");
    }
    static void Verify(SpeakerManager manager,StringBuilder report)
    {
        var selector=Get<RectTransform>(manager,"modelSelector");
        var actions=Get<GameObject>(manager,"desktopPlacementHints").transform.parent;
        var source=Get<TextMeshProUGUI>(manager,"placementGuideText");
        bool oldVr=Get<bool>(manager,"isVrUser"); int oldLanguage=Get<int>(manager,"languageIndex");
        var oldPosition=selector.position;var oldRotation=selector.rotation;
        try
        {
            for(int language=0;language<4;language++)foreach(bool vr in new[]{false,true})
            {
                Set(manager,"isVrUser",vr); Set(manager,"languageIndex",language); Call(manager,"RefreshPlacementHints");
                Check(!source.gameObject.activeSelf,"Old prose guide still visible");
                Check(Get<GameObject>(manager,"desktopPlacementHints").activeSelf!=vr&&Get<GameObject>(manager,"vrPlacementHints").activeSelf==vr,"Incorrect platform hints");
                Check(Get<GameObject>(manager,"desktopModelKeys").activeSelf!=vr&&Get<GameObject>(manager,"vrModelKeys").activeSelf==vr,"Incorrect platform selector");
                CaptureUI(manager,language,vr);
            }
            foreach(var image in actions.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Concat(selector.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)))Check(!image.raycastTarget,"Hint intercepts placement clicks");
            Call(manager,"SetModelHintFeedback",-1);
            Check(Get<UnityEngine.UI.Image[]>(manager,"previousModelAccents")[0].color!=Get<UnityEngine.UI.Image[]>(manager,"nextModelAccents")[0].color,"Previous key feedback missing");
            Call(manager,"SetModelHintFeedback",1);
            Check(Get<UnityEngine.UI.Image[]>(manager,"nextModelAccents")[0].color.r==1f,"Next key feedback missing");
            Call(manager,"SetModelHintFeedback",0);
            Call(manager,"UpdateModelSelectorPose",Get<GameObject>(manager,"holoSpeaker").transform.position+new Vector3(0,1.7f,-2),Quaternion.identity);
            Check(selector.position.y>Get<GameObject>(manager,"holoSpeaker").transform.position.y,"Selector placed below ground");
            report.AppendLine("PASS "+manager.gameObject.scene.name+" 4 languages x 2 platforms; nonblocking graphics; direction feedback; above-ground selector");
        }
        finally
        {
            Set(manager,"isVrUser",oldVr);Set(manager,"languageIndex",oldLanguage);Call(manager,"RefreshPlacementHints");Call(manager,"SetModelHintFeedback",0);
            selector.SetPositionAndRotation(oldPosition,oldRotation);
        }
    }
    static void CaptureUI(SpeakerManager manager,int language,bool vr)
    {
        // Actual authored UI rendered on an isolated canvas, not an AI mockup.
        var root=new GameObject("Temporary icon UI capture",typeof(RectTransform),typeof(Canvas)){hideFlags=HideFlags.HideAndDontSave};
        root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace; root.transform.position=Vector3.one*100000;
        var action=UnityEngine.Object.Instantiate(Get<GameObject>(manager,vr?"vrPlacementHints":"desktopPlacementHints"),root.transform,false);
        action.SetActive(true); action.GetComponent<RectTransform>().anchoredPosition=new Vector2(0,-65);
        var selection=UnityEngine.Object.Instantiate(Get<RectTransform>(manager,"modelSelector"),root.transform,false);
        UnityEngine.Object.DestroyImmediate(selection.GetComponent<Canvas>());selection.localScale=Vector3.one;selection.localRotation=Quaternion.identity;selection.anchoredPosition3D=new Vector3(0,65,0); selection.gameObject.SetActive(true);
        var cameraObject=new GameObject("Temporary UI capture camera"){hideFlags=HideFlags.HideAndDontSave};var camera=cameraObject.AddComponent<Camera>();
        camera.transform.position=root.transform.position+Vector3.back*10;camera.orthographic=true;camera.orthographicSize=160;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.17f,.15f,1);camera.nearClipPlane=.1f;camera.farClipPlane=20;
        var rt=new RenderTexture(960,384,24);var previous=RenderTexture.active;Texture2D texture=null;
        try
        {
            foreach(var label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if(!label.gameObject.activeInHierarchy)continue;
                label.ForceMeshUpdate(true);Check(label.textInfo!=null&&!label.isTextOverflowing,"Label clipped: "+label.text);
                foreach(char c in label.text)if(!char.IsWhiteSpace(c))Check(label.font.HasCharacter(c,true,true),"Missing glyph "+c);
            }
            Canvas.ForceUpdateCanvases();camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            texture=new Texture2D(960,384,TextureFormat.RGB24,false);texture.ReadPixels(new UnityEngine.Rect(0,0,960,384),0,0);texture.Apply();
            if(manager.gameObject.scene.name=="TEST_PC")File.WriteAllBytes(Output+"hints-"+language+(vr?"-vr":"-desktop")+".png",texture.EncodeToPNG());
        }
        finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(rt);if(texture)UnityEngine.Object.DestroyImmediate(texture);}
    }
    static void CaptureInScene()
    {
        var scene=SceneManager.GetActiveScene();Check(Paths.Contains(scene.path),"Open a target scene for in-scene capture: "+scene.path);
        var manager=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SpeakerManager>(true)).Single();
        var placements=Get<GameObject>(manager,"speakerPlacements");var holo=Get<GameObject>(manager,"holoSpeaker");
        var message=Get<GameObject>(manager,"messageUI"); var selector=Get<RectTransform>(manager,"modelSelector");
        var transforms=placements.GetComponentsInChildren<Transform>(true).Concat(message.GetComponentsInChildren<Transform>(true)).Distinct().ToArray();
        var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();var scales=transforms.Select(t=>t.localScale).ToArray();var active=transforms.Select(t=>t.gameObject.activeSelf).ToArray();
        var lines=placements.GetComponentsInChildren<LineRenderer>(true);var enabled=lines.Select(l=>l.enabled).ToArray();
        var animator=Get<Animator>(manager,"holoAnimator");bool animatorEnabled=animator.enabled;
        bool oldVr=Get<bool>(manager,"isVrUser");int oldLanguage=Get<int>(manager,"languageIndex");
        var cameraObject=new GameObject("Temporary placement icon preview camera"){hideFlags=HideFlags.HideAndDontSave};var camera=cameraObject.AddComponent<Camera>();
        var rt=new RenderTexture(1200,800,24);var previous=RenderTexture.active;Texture2D shot=null;
        try
        {
            Vector3 center=new Vector3(48,6.5f,-8);
            if(Physics.Raycast(center+Vector3.up*2,Vector3.down,out RaycastHit floor,5,Get<int>(manager,"rayLayerMask"),QueryTriggerInteraction.Ignore))center.y=floor.point.y;
            placements.SetActive(true);holo.SetActive(true);message.SetActive(true);animator.enabled=false;
            foreach(var line in lines)line.enabled=false;
            camera.transform.position=center+new Vector3(0,1.65f,-2.3f);camera.transform.LookAt(center+Vector3.up*.22f);
            camera.fieldOfView=65;camera.nearClipPlane=.03f;camera.targetTexture=rt;
            holo.transform.SetPositionAndRotation(center,Quaternion.LookRotation(Vector3.back,Vector3.up));
            foreach(bool vr in new[]{false,true})
            {
                Set(manager,"isVrUser",vr);Set(manager,"languageIndex",2);Call(manager,"RefreshPlacementHints");
                Call(manager,"SetUITransform",message,camera.transform.position+camera.transform.forward,camera.transform.position);
                Call(manager,"UpdateModelSelectorPose",camera.transform.position,camera.transform.rotation);
                selector.gameObject.SetActive(true);
                foreach(var label in placements.GetComponentsInChildren<TextMeshProUGUI>(true))if(label.gameObject.activeInHierarchy)label.ForceMeshUpdate(true);
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
                shot=new Texture2D(1200,800,TextureFormat.RGB24,false);shot.ReadPixels(new UnityEngine.Rect(0,0,1200,800),0,0);shot.Apply();
                File.WriteAllBytes(Output+(vr?"scene-vr.png":"scene-desktop.png"),shot.EncodeToPNG());UnityEngine.Object.DestroyImmediate(shot);shot=null;
            }
            File.WriteAllText(Output+"capture.done","Editor scene renders captured with temporary placement state. All transforms/visibility restored; not a VRChat client capture.");
        }
        finally
        {
            Set(manager,"isVrUser",oldVr);Set(manager,"languageIndex",oldLanguage);Call(manager,"RefreshPlacementHints");
            for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];transforms[i].localScale=scales[i];transforms[i].gameObject.SetActive(active[i]);}
            for(int i=0;i<lines.Length;i++)lines[i].enabled=enabled[i];animator.enabled=animatorEnabled;
            RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(rt);if(shot)UnityEngine.Object.DestroyImmediate(shot);
        }
    }
}
