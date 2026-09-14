using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDK3.Components;

[InitializeOnLoad]
public static class LocalPatchBoardSetup
{
    const string Output = "output/patch-board-20260910";
    const string Target = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    const string BoardName = "2.9 Update Notice";
    const string Content = "Assets/_Shinjuku/UI/UpdateNotice/UpdateNoticeLanguages.json";
    const string Prefab = "Assets/_Shinjuku/UI/UpdateNotice/UpdateNotice.prefab";
    [Serializable] class Translations { public string[] keys, english, japanese, korean; }
    static LocalPatchBoardSetup() { EditorApplication.update += Tick; }
    static string PathOf(Transform t) { return t.parent ? PathOf(t.parent) + "/" + t.name : t.name; }
    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if(File.Exists(Output+"/github-alignment.request"))
        {
            File.Delete(Output+"/github-alignment.request");
            try { RefineGitHubAlignment(); }
            catch(Exception e) { File.WriteAllText(Output+"/github-alignment.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        if(File.Exists(Output+"/brush-titles.request"))
        {
            File.Delete(Output+"/brush-titles.request");
            try { FinishNotice(); File.WriteAllText(Output+"/brush-titles.done",DateTime.Now.ToString("s")); }
            catch(Exception e) { File.WriteAllText(Output+"/brush-titles.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        if(File.Exists(Output+"/reviewed-layout.request") || File.Exists(Output+"/simple-copy.request"))
        {
            if(File.Exists(Output+"/reviewed-layout.request"))File.Delete(Output+"/reviewed-layout.request");
            if(File.Exists(Output+"/simple-copy.request"))File.Delete(Output+"/simple-copy.request");
            try { FinishNotice(); File.WriteAllText(Output+"/reviewed-layout.done",DateTime.Now.ToString("s")); }
            catch(Exception e) { File.WriteAllText(Output+"/reviewed-layout.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        if(File.Exists(Output+"/polish.request") || File.Exists(Output+"/header-fix.request"))
        {
            if(File.Exists(Output+"/polish.request"))File.Delete(Output+"/polish.request");
            if(File.Exists(Output+"/header-fix.request"))File.Delete(Output+"/header-fix.request");
            try { PolishNotice(); }
            catch(Exception e) { File.WriteAllText(Output+"/polish.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        if(File.Exists(Output+"/redesign.request"))
        {
            File.Delete(Output+"/redesign.request");
            try { RedesignNotice(); }
            catch(Exception e) { File.WriteAllText(Output+"/redesign.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        if(File.Exists(Output+"/finish.request"))
        {
            File.Delete(Output+"/finish.request");
            try { FinishNotice(); }
            catch(Exception e) { File.WriteAllText(Output+"/finish.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        if (File.Exists(Output + "/build.request"))
        {
            File.Delete(Output + "/build.request");
            try { PlaceNotice(); }
            catch(Exception e) { File.WriteAllText(Output + "/build.failed", e.ToString()); Debug.LogException(e); }
            return;
        }
        string request = Output + "/inspect.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != Target) throw new Exception("TEST must be active.");
            var log = new StringBuilder("Scene dirty=" + scene.isDirty + "\n");
            foreach (var root in scene.GetRootGameObjects())
                log.AppendLine("ROOT " + root.name + " " + root.transform.position);
            foreach (var descriptor in UnityEngine.Object.FindObjectsOfType<VRCSceneDescriptor>())
                foreach (var spawn in descriptor.spawns)
                    if (spawn)
                    {
                        log.AppendLine("SPAWN " + PathOf(spawn) + " pos=" + spawn.position.ToString("F3") + " rot=" + spawn.eulerAngles);
                        if (!File.Exists(Output + "/entrance-before.png")) Capture(spawn.position + Vector3.up * 1.6f, spawn.rotation, Output + "/entrance-before.png");
                    }
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.gameObject.scene != scene) continue;
                string name = t.name.ToLowerInvariant();
                if (name.Contains("spawn") || name.Contains("welcome") || name.Contains("entry") || name.Contains("entrance") || name.Contains("guide") || name.Contains("notice"))
                    log.AppendLine("OBJECT " + PathOf(t) + " pos=" + t.position.ToString("F3") + " rot=" + t.eulerAngles);
            }
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Canvas>())
                if (c.gameObject.scene == scene) log.AppendLine("CANVAS " + PathOf(c.transform) + " pos=" + c.transform.position.ToString("F3") + " rot=" + c.transform.eulerAngles + " scale=" + c.transform.lossyScale);
            File.WriteAllText(Output + "/inspection.txt", log.ToString());
        }
        catch (Exception e) { File.WriteAllText(Output + "/inspection.txt", e.ToString()); }
    }
    static void PolishNotice()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!=Target)throw new Exception("TEST only.");
        var language=UnityEngine.Object.FindObjectsOfType<ExhibitionLanguage>(true).Single(l=>l.gameObject.scene==scene && l.name==BoardName);
        var root=language.transform;var board=language.gameObject;
        Undo.RegisterFullObjectHierarchyUndo(board,"Simplify update notice typography");
        foreach(Transform child in root)
            if(child.name.StartsWith("Icon ") || child.name=="World name")
            {child.gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);}
        string path="Assets/_Shinjuku/UI/UpdateNotice/Header.png";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency=true;importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=2048;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        var header=(RectTransform)root.Find("Header image");header.gameObject.SetActive(true);
        header.GetComponent<AspectRatioFitter>().aspectMode=AspectRatioFitter.AspectMode.None;
        header.GetComponent<AspectRatioFitter>().enabled=false;
        header.anchorMin=header.anchorMax=header.pivot=new Vector2(.5f,.5f);
        header.localScale=Vector3.one;header.localRotation=Quaternion.identity;
        var picture=header.GetComponent<RawImage>();picture.texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        // 원본 이미지 변경 없이 투명 여백만 제외한 로고 영역 표시
        picture.uvRect=new Rect(71f/1820f,331f/1024f,1695f/1820f,357f/1024f);
        Move(root,"Header image",new Vector2(-368,755),new Vector2(720,720f*357/1695));
        Canvas.ForceUpdateCanvases();
        if(Mathf.Abs(header.rect.width-720)>1 || header.rect.height>160)throw new Exception("Header image layout exceeds reserved bounds.");
        Move(root,"title",new Vector2(408,755),new Vector2(640,110),56);
        Move(root,"speakerTitle",new Vector2(0,209),new Vector2(1456,62),40);
        Move(root,"speakerBody",new Vector2(0,42),new Vector2(1456,250),32);
        Move(root,"trafficTitle",new Vector2(-382,-181),new Vector2(692,62),37);
        Move(root,"effectsTitle",new Vector2(382,-181),new Vector2(692,62),37);
        Move(root,"FPS statistic",new Vector2(492,491),new Vector2(370,152),112);
        Move(root,"FPS label",new Vector2(492,398),new Vector2(370,52),31);
        var mark=root.Find("GitHub mark") as RectTransform;
        if(!mark)
        {
            mark=Rect("GitHub mark",root,new Vector2(-425,-484),new Vector2(54,54));
            var logo=mark.gameObject.AddComponent<RawImage>();logo.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_ShinjukuExhibition/Textures/GitHub-Mark.png");logo.raycastTarget=false;
            Undo.RegisterCreatedObjectUndo(mark.gameObject,"Add GitHub brand mark");
        }
        Move(root,"githubTitle",new Vector2(157,-482),new Vector2(1030,60),36);
        foreach(var label in language.allLabels)
        {
            label.supportRichText=true;
            if(label.name.EndsWith("Title") || label.name=="title")label.fontStyle=FontStyle.Bold;
            PrefabUtility.RecordPrefabInstancePropertyModifications(label);
        }
        language.originalSizes=language.allLabels.Select(t=>t.fontSize).ToArray();language.multilingualSizes=(int[])language.originalSizes.Clone();
        EditorUtility.SetDirty(language);PrefabUtility.RecordPrefabInstancePropertyModifications(language);
        PrefabUtility.RecordPrefabInstancePropertyModifications(picture);PrefabUtility.RecordPrefabInstancePropertyModifications(header.gameObject);PrefabUtility.RecordPrefabInstancePropertyModifications(header.GetComponent<AspectRatioFitter>());
        FinishNotice();
        File.WriteAllText(Output+"/polish.done","Original logo placed using UV crop of transparent padding. Four decorative icons hidden. GitHub brand mark added. Key phrases emphasized in all three languages. Root transform preserved; prefab saved; scene left unsaved.\n");
    }
    static void Move(Transform root,string name,Vector2 position,Vector2 size,int fontSize=0)
    {
        var rect=root.Find(name) as RectTransform;
        if(!rect)throw new Exception("Missing notice element: "+name);
        Undo.RecordObject(rect,"Refine update notice layout");rect.anchoredPosition=position;rect.sizeDelta=size;
        var text=rect.GetComponent<Text>();
        if(text && fontSize>0){Undo.RecordObject(text,"Refine update notice typography");text.fontSize=fontSize;}
        PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
        if(text)PrefabUtility.RecordPrefabInstancePropertyModifications(text);
    }
    static void Stroke(Transform parent,Vector2 a,Vector2 b,float width,Color color)
    {
        var rect=Rect("Stroke",parent,(a+b)*.5f,new Vector2(Vector2.Distance(a,b),width));
        rect.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg);
        var image=rect.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;
    }
    static void Arc(Transform parent,Vector2 center,float radius,float from,float to,Color color,float width=4)
    {
        int count=Mathf.CeilToInt(Mathf.Abs(to-from)/12);
        for(int i=0;i<count;i++)
        {
            float a=Mathf.Lerp(from,to,(float)i/count)*Mathf.Deg2Rad,b=Mathf.Lerp(from,to,(float)(i+1)/count)*Mathf.Deg2Rad;
            Stroke(parent,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,width,color);
        }
    }
    static void Outline(Transform parent,Vector2 center,Vector2 size,Color color)
    {
        Vector2 a=center-size*.5f,b=center+size*.5f;
        Stroke(parent,a,new Vector2(b.x,a.y),4,color);Stroke(parent,new Vector2(b.x,a.y),b,4,color);
        Stroke(parent,b,new Vector2(a.x,b.y),4,color);Stroke(parent,new Vector2(a.x,b.y),a,4,color);
    }
    static void Spark(Transform parent,Vector2 c,float radius,Color color)
    {
        Vector2[] points={new Vector2(0,radius),new Vector2(radius*.28f,radius*.28f),new Vector2(radius,0),new Vector2(radius*.28f,-radius*.28f),new Vector2(0,-radius),new Vector2(-radius*.28f,-radius*.28f),new Vector2(-radius,0),new Vector2(-radius*.28f,radius*.28f)};
        for(int i=0;i<8;i++)Stroke(parent,c+points[i],c+points[(i+1)%8],3.5f,color);
    }
    static void Icon(Transform root,string kind,Vector2 position,Color color)
    {
        if(root.Find("Icon "+kind))return;
        var icon=Rect("Icon "+kind,root,position,new Vector2(100,100));Undo.RegisterCreatedObjectUndo(icon.gameObject,"Add update notice icon");
        if(kind=="performance")
        {
            Arc(icon,new Vector2(0,-13),38,0,180,color);Stroke(icon,new Vector2(-38,-13),new Vector2(-38,-25),4,color);Stroke(icon,new Vector2(38,-13),new Vector2(38,-25),4,color);
            Stroke(icon,new Vector2(0,-13),new Vector2(24,15),5,color);Arc(icon,new Vector2(0,-13),5,0,360,color);
        }
        else if(kind=="speaker")
        {
            Outline(icon,Vector2.zero,new Vector2(54,78),color);Arc(icon,new Vector2(0,-14),17,0,360,color);Arc(icon,new Vector2(0,22),7,0,360,color);
            Arc(icon,new Vector2(10,0),35,-40,40,color);Arc(icon,new Vector2(10,0),47,-40,40,color);
        }
        else if(kind=="traffic")
        {
            Stroke(icon,new Vector2(-43,-14),new Vector2(-43,9),4,color);Stroke(icon,new Vector2(-43,9),new Vector2(-26,31),4,color);
            Stroke(icon,new Vector2(-26,31),new Vector2(24,31),4,color);Stroke(icon,new Vector2(24,31),new Vector2(40,9),4,color);
            Stroke(icon,new Vector2(40,9),new Vector2(43,-14),4,color);Stroke(icon,new Vector2(-43,-14),new Vector2(43,-14),4,color);
            Stroke(icon,new Vector2(-34,7),new Vector2(34,7),4,color);Arc(icon,new Vector2(-25,-21),8,0,360,color);Arc(icon,new Vector2(25,-21),8,0,360,color);
        }
        else {Spark(icon,new Vector2(-10,0),30,color);Spark(icon,new Vector2(34,29),12,color);Spark(icon,new Vector2(26,-33),9,color);}
    }
    static void RedesignNotice()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!=Target)throw new Exception("TEST only.");
        var board=scene.GetRootGameObjects().Single(g=>g.name==BoardName);var root=board.transform;
        if(!File.Exists(Output+"/UpdateNotice-before-redesign.prefab"))File.Copy(Prefab,Output+"/UpdateNotice-before-redesign.prefab");
        var language=board.GetComponent<ExhibitionLanguage>();var font=language.multilingualFont;
        var white=new Color(.93f,.96f,.98f);var gold=new Color(.84f,.71f,.43f);var muted=new Color(.69f,.77f,.81f);
        Undo.RegisterFullObjectHierarchyUndo(board,"Redesign update notice");
        root.Find("World name").gameObject.SetActive(false);
        Move(root,"title",new Vector2(245,754),new Vector2(900,110),68);
        var title=root.Find("title").GetComponent<Text>();title.alignment=TextAnchor.MiddleLeft;
        var header=root.Find("Header image") as RectTransform;
        if(!header)
        {
            header=Rect("Header image",root,new Vector2(-515,753),new Vector2(426,220));
            var picture=header.gameObject.AddComponent<RawImage>();picture.raycastTarget=false;
            var fit=header.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
            Undo.RegisterCreatedObjectUndo(header.gameObject,"Add update header image");
        }
        string headerPath="Assets/_Shinjuku/UI/UpdateNotice/Header.png";
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(headerPath);
        if(texture){header.GetComponent<RawImage>().texture=texture;header.GetComponent<AspectRatioFitter>().aspectMode=AspectRatioFitter.AspectMode.None;header.sizeDelta=new Vector2(426,Mathf.Min(230,426f*texture.height/texture.width));header.gameObject.SetActive(true);}
        else header.gameObject.SetActive(false);
        Move(root,"Main update",new Vector2(0,446),new Vector2(1456,344));
        Move(root,"MAIN badge",new Vector2(-630,567),new Vector2(118,42));Move(root,"MAIN",new Vector2(-630,567),new Vector2(118,42),23);
        Move(root,"performanceTitle",new Vector2(-164,563),new Vector2(780,60),40);
        Move(root,"performanceBody",new Vector2(-200,438),new Vector2(958,174),32);
        Move(root,"measurement",new Vector2(-200,307),new Vector2(958,40),22);
        if(!root.Find("FPS statistic"))
        {
            var metric=Label("FPS statistic",root,new Vector2(492,449),new Vector2(370,152),112,font,gold);metric.text="2.1x";metric.fontStyle=FontStyle.Bold;metric.alignment=TextAnchor.MiddleCenter;
            var caption=Label("FPS label",root,new Vector2(492,358),new Vector2(370,52),31,font,muted);caption.text="FPS";caption.alignment=TextAnchor.MiddleCenter;
        }
        Icon(root,"performance",new Vector2(492,559),gold);
        Move(root,"speakerTitle",new Vector2(62,209),new Vector2(1332,62),40);
        Move(root,"speakerBody",new Vector2(62,42),new Vector2(1332,250),31);
        Icon(root,"speaker",new Vector2(-674,187),new Color(.40f,.80f,.75f));
        Move(root,"Section rule",new Vector2(0,-116),new Vector2(1456,2));
        Move(root,"trafficTitle",new Vector2(-333,-181),new Vector2(588,62),37);
        Move(root,"effectsTitle",new Vector2(431,-181),new Vector2(588,62),37);
        Icon(root,"traffic",new Vector2(-683,-177),new Color(.61f,.74f,.88f));
        Icon(root,"effects",new Vector2(81,-177),gold);
        Move(root,"trafficBody",new Vector2(-382,-308),new Vector2(692,176),31);
        Move(root,"effectsBody",new Vector2(382,-308),new Vector2(692,176),31);
        Move(root,"GitHub area",new Vector2(0,-537),new Vector2(1456,228));
        Move(root,"GitHub QR",new Vector2(-590,-537),new Vector2(188,188));
        Move(root,"githubTitle",new Vector2(122,-482),new Vector2(1100,60),36);
        Move(root,"githubBody",new Vector2(122,-543),new Vector2(1100,48),28);
        Move(root,"GitHub URL",new Vector2(122,-597),new Vector2(1100,42),25);
        language.originalSizes=language.allLabels.Select(t=>t.fontSize).ToArray();language.multilingualSizes=(int[])language.originalSizes.Clone();
        PrefabUtility.RecordPrefabInstancePropertyModifications(language);
        FinishNotice();
        File.WriteAllText(Output+"/redesign.done","Updated existing notice in place; root position and scale preserved. Header image="+(texture?headerPath:"awaiting supplied image")+"\n");
    }
    static void ApplyReviewedLayout(ExhibitionLanguage language, Translations table)
    {
        var root=language.transform;
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject,"Refine update notice content and layout");
        var font=language.multilingualFont;
        var white=new Color(.91f,.94f,.95f);
        var muted=new Color(.69f,.76f,.79f);
        foreach(Transform child in root)
            if(child.name.StartsWith("Icon ") || child.name=="World name")child.gameObject.SetActive(false);
        // 기존 위치와 전체 크기를 유지하며 내용별 읽기 영역 분리
        Move(root,"Main update",new Vector2(0,535),new Vector2(1456,230));
        Move(root,"MAIN badge",new Vector2(-630,606),new Vector2(118,42));
        Move(root,"MAIN",new Vector2(-630,606),new Vector2(118,42),23);
        Move(root,"performanceTitle",new Vector2(70,606),new Vector2(1220,54),39);
        Move(root,"performanceBody",new Vector2(-200,536),new Vector2(958,82),31);
        root.Find("performanceBody").GetComponent<Text>().alignment=TextAnchor.MiddleLeft;
        Move(root,"measurement",new Vector2(-200,457),new Vector2(958,60),22);
        Move(root,"FPS statistic",new Vector2(543,558),new Vector2(280,126),100);
        Icon(root,"performance",new Vector2(345,558),new Color(.84f,.71f,.43f));
        var performanceIcon=root.Find("Icon performance");
        performanceIcon.gameObject.SetActive(true);
        performanceIcon.localPosition=new Vector3(345,558,0);
        performanceIcon.localScale=Vector3.one*.85f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(performanceIcon);
        PrefabUtility.RecordPrefabInstancePropertyModifications(performanceIcon.gameObject);
        root.Find("FPS label").gameObject.SetActive(false);
        if(!root.Find("metricCaption"))Label("metricCaption",root,Vector2.zero,Vector2.one,25,font,muted);
        Move(root,"metricCaption",new Vector2(492,479),new Vector2(410,48),25);
        root.Find("metricCaption").GetComponent<Text>().alignment=TextAnchor.MiddleCenter;
        Move(root,"speakerTitle",new Vector2(0,372),new Vector2(1456,54),39);
        Move(root,"speakerBody",new Vector2(0,185),new Vector2(1456,288),31);
        Move(root,"Section rule",new Vector2(0,22),new Vector2(1456,2));
        Move(root,"trafficTitle",new Vector2(-382,-20),new Vector2(692,54),36);
        Move(root,"effectsTitle",new Vector2(382,-20),new Vector2(692,54),36);
        Move(root,"trafficBody",new Vector2(-382,-191),new Vector2(692,252),30);
        Move(root,"effectsBody",new Vector2(382,-191),new Vector2(692,252),30);
        if(!root.Find("Other rule"))Panel("Other rule",root,Vector2.zero,Vector2.one,new Color(.20f,.23f,.24f));
        Move(root,"Other rule",new Vector2(0,-332),new Vector2(1456,2));
        if(!root.Find("miscTitle"))Label("miscTitle",root,Vector2.zero,Vector2.one,32,font,white);
        if(!root.Find("miscBody"))Label("miscBody",root,Vector2.zero,Vector2.one,28,font,muted);
        Move(root,"miscTitle",new Vector2(0,-370),new Vector2(1456,46),32);
        Move(root,"miscBody",new Vector2(0,-442),new Vector2(1456,86),28);
        Move(root,"GitHub area",new Vector2(0,-589),new Vector2(1456,174));
        Move(root,"GitHub QR",new Vector2(-602,-589),new Vector2(150,150));
        AlignGitHubContent(root);
        Move(root,"language",new Vector2(0,-716),new Vector2(1456,42),25);
        string[] methods={"UseAuto","UseJapanese","UseKorean","UseEnglish"};
        for(int i=0;i<methods.Length;i++)Move(root,methods[i],new Vector2(-552+i*368,-797),new Vector2(352,76));
        language.keys=table.keys;
        language.staticKeys=table.keys;
        language.staticLabels=table.keys.Select(key=>root.Find(key).GetComponent<Text>()).ToArray();
        language.allLabels=language.staticLabels;
        foreach(var text in language.allLabels)
        {
            text.supportRichText=true;
            if(text.name.EndsWith("Title") || text.name=="title")text.fontStyle=FontStyle.Bold;
            PrefabUtility.RecordPrefabInstancePropertyModifications(text);
        }
        language.originalFonts=language.allLabels.Select(t=>(UnityEngine.Object)t.font).ToArray();
        language.originalSizes=language.allLabels.Select(t=>t.fontSize).ToArray();
        language.multilingualSizes=(int[])language.originalSizes.Clone();
        EditorUtility.SetDirty(language);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
    }
    static void AlignGitHubContent(Transform root)
    {
        // 제목 글자 높이에 아이콘 정렬, 설명과 주소는 아이콘 왼쪽 선에 정렬
        Move(root,"GitHub mark",new Vector2(-426,-539),new Vector2(34,34));
        Move(root,"githubTitle",new Vector2(160,-547),new Vector2(1100,48));
        Move(root,"githubBody",new Vector2(133,-599),new Vector2(1152,42));
        Move(root,"GitHub URL",new Vector2(133,-641),new Vector2(1152,36));
    }
    static void RefineGitHubAlignment()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=Target)throw new Exception("TEST only.");
        var language=UnityEngine.Object.FindObjectsOfType<ExhibitionLanguage>(true).Single(l=>l.gameObject.scene==scene && l.name==BoardName);
        var root=language.transform;
        int savedMode=language.languageMode,savedCurrent=language.currentLanguage;
        var savedColors=language.modeButtons.Select(button=>button.color).ToArray();
        AlignGitHubContent(root);
        try
        {
            for(int mode=1;mode<=3;mode++)
            {
                language.languageMode=mode;language.ApplyLanguage();Canvas.ForceUpdateCanvases();
                foreach(string name in new[]{"githubTitle","githubBody","GitHub URL"})
                {
                    var text=root.Find(name).GetComponent<Text>();
                    if(text.preferredHeight>text.rectTransform.rect.height+1)throw new Exception("GitHub text overflow: "+mode+" / "+name);
                }
                CaptureBoard(root,Output+"/github-alignment-"+(mode==1?"ja":mode==2?"ko":"en")+".png");
            }
        }
        finally
        {
            language.languageMode=savedCurrent==0?3:savedCurrent;
            language.ApplyLanguage();language.languageMode=savedMode;
            for(int i=0;i<savedColors.Length;i++)language.modeButtons[i].color=savedColors[i];
        }
        // GitHub 영역의 위치와 크기만 프리팹에 반영, 다른 씬 변경 저장 방지
        foreach(string name in new[]{"GitHub mark","githubTitle","githubBody","GitHub URL"})
        {
            var serialized=new SerializedObject(root.Find(name));
            PrefabUtility.ApplyPropertyOverride(serialized.FindProperty("m_AnchoredPosition"),Prefab,InteractionMode.AutomatedAction);
            PrefabUtility.ApplyPropertyOverride(serialized.FindProperty("m_SizeDelta"),Prefab,InteractionMode.AutomatedAction);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        File.WriteAllText(Output+"/github-alignment.done",DateTime.Now.ToString("s")+" / GitHub layout only. Three languages checked. Scene left unsaved.");
    }
    static void ApplyBrushTitles(ExhibitionLanguage language)
    {
        string[] codes={"en","ja","ko"};
        string folder="Assets/_Shinjuku/UI/UpdateNotice/";
        if(!codes.All(code=>File.Exists(folder+"Title-"+code+".png")))return;
        var root=language.transform;
        language.languageVisuals=new GameObject[3];
        var report=new StringBuilder();
        for(int i=0;i<codes.Length;i++)
        {
            string path=folder+"Title-"+codes[i]+".png";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency=true;
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=2048;
            importer.mipmapEnabled=true;
            importer.wrapMode=TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            var source=new Texture2D(2,2,TextureFormat.RGBA32,false);
            Rect uv; float aspect;
            try
            {
                if(!source.LoadImage(File.ReadAllBytes(path)))throw new Exception("Invalid title PNG: "+path);
                var pixels=source.GetPixels32();
                int minX=source.width,minY=source.height,maxX=-1,maxY=-1;
                for(int y=0;y<source.height;y++)for(int x=0;x<source.width;x++)
                    if(pixels[y*source.width+x].a>32)
                    {minX=Mathf.Min(minX,x);minY=Mathf.Min(minY,y);maxX=Mathf.Max(maxX,x);maxY=Mathf.Max(maxY,y);}
                if(maxX<0 || pixels[0].a>0)throw new Exception("Title must contain lettering on transparent background: "+path);
                // 원본 알파 채널 유지, 투명 여백만 UV 범위에서 제외
                minX=Mathf.Max(0,minX-8);minY=Mathf.Max(0,minY-8);
                maxX=Mathf.Min(source.width-1,maxX+8);maxY=Mathf.Min(source.height-1,maxY+8);
                uv=new Rect((float)minX/source.width,(float)minY/source.height,(float)(maxX-minX+1)/source.width,(float)(maxY-minY+1)/source.height);
                aspect=(float)(maxX-minX+1)/(maxY-minY+1);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
            string name="Brush title "+codes[i];
            var rect=root.Find(name) as RectTransform;
            if(!rect)
            {
                rect=Rect(name,root,Vector2.zero,Vector2.one);
                rect.gameObject.AddComponent<RawImage>();
                Undo.RegisterCreatedObjectUndo(rect.gameObject,"Apply localized title artwork");
            }
            float width=Mathf.Min(640,114*aspect);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            rect.anchoredPosition=new Vector2(408,755);
            rect.sizeDelta=new Vector2(width,width/aspect);
            rect.localScale=Vector3.one;
            rect.localRotation=Quaternion.identity;
            var image=rect.GetComponent<RawImage>();
            image.texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            image.uvRect=uv;image.color=Color.white;image.raycastTarget=false;
            image.material=root.Find("Header image").GetComponent<RawImage>().material;
            language.languageVisuals[i]=rect.gameObject;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            PrefabUtility.RecordPrefabInstancePropertyModifications(image);
            report.AppendLine(codes[i]+" / "+path+" / size="+rect.sizeDelta+" / UV="+uv);
        }
        root.Find("title").gameObject.SetActive(false);
        PrefabUtility.RecordPrefabInstancePropertyModifications(root.Find("title").gameObject);
        File.WriteAllText(Output+"/brush-title-layout.txt",report.ToString());
    }
    static void FinishNotice()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=Target)throw new Exception("TEST only.");
        var board=UnityEngine.Object.FindObjectsOfType<ExhibitionLanguage>(true).Single(l=>l.gameObject.scene==scene && l.name==BoardName).gameObject;
        var language=board.GetComponent<ExhibitionLanguage>();
        Undo.RecordObject(language,"Refine update notice translations");
        var table=JsonUtility.FromJson<Translations>(File.ReadAllText(Content));
        if(table.keys.Contains("metricCaption"))ApplyReviewedLayout(language,table);
        language.english=table.english;language.japanese=table.japanese;language.korean=table.korean;
        ApplyBrushTitles(language);
        Validate(language);
        var report=new StringBuilder();
        string[] methods={"UseAuto","UseJapanese","UseKorean","UseEnglish"};
        var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(language);
        if(!backing || !backing.programSource)throw new Exception("Missing Udon program.");
        for(int i=0;i<4;i++)
        {
            var button=language.modeButtons[i].GetComponent<Button>();
            if(button.onClick.GetPersistentEventCount()!=1 || button.onClick.GetPersistentTarget(0)!=backing || button.onClick.GetPersistentMethodName(0)!="SendCustomEvent")throw new Exception("Bad button binding.");
            var serialized=new SerializedObject(button);
            if(serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue!=methods[i])throw new Exception("Bad button argument.");
            // 편집 모드에서 VRChat 언어 API 미제공, 자동 모드는 연결과 코드 분기로 검증
            if(i==0){report.AppendLine("PASS AUTO binding; locale resolution checked separately. Live VRChat locale callback not executed in edit mode.");continue;}
            typeof(ExhibitionLanguage).GetMethod(methods[i]).Invoke(language,null);
            if(language.languageMode!=i)throw new Exception("Language mode not applied.");
            int resolved=language.currentLanguage;
            language.OnLanguageChanged("fr");
            if(i!=0 && language.currentLanguage!=resolved)throw new Exception("Manual choice overwritten.");
            report.AppendLine("PASS button "+methods[i]+" / local mode="+i+" / current="+language.currentLanguage);
        }
        for(int mode=1;mode<=3;mode++)
        {
            language.languageMode=mode;language.ApplyLanguage();Canvas.ForceUpdateCanvases();
            CaptureBoard(board.transform,Output+"/notice-"+(mode==1?"ja":mode==2?"ko":"en")+".png");
        }
        language.languageMode=1;language.ApplyLanguage();language.languageMode=0;
        for(int i=0;i<4;i++)language.modeButtons[i].color=i==0?new Color(.14f,.38f,.27f,1):new Color(.045f,.15f,.108f,1);
        UdonSharpEditorUtility.CopyProxyToUdon(language);EditorUtility.SetDirty(language);
        PrefabUtility.RecordPrefabInstancePropertyModifications(language);
        PrefabUtility.SaveAsPrefabAssetAndConnect(board,Prefab,InteractionMode.AutomatedAction);
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms();
        report.AppendLine("AUTO initial mode restored. Existing TEST changes remain unsaved. Prefab updated. VR device input not tested.");
        File.WriteAllText(Output+"/finish.done",report.ToString());
        Selection.activeGameObject=board;
    }
    [MenuItem("Tools/Shinjuku/Place update notice in TEST")]
    static void PlaceNotice()
    {
        var scene = SceneManager.GetActiveScene();
        if(scene.path != Target) throw new Exception("Open TEST first.");
        bool wasDirty = scene.isDirty;
        if(!File.Exists(Output+"/TEST-before.unity")) File.Copy(Target,Output+"/TEST-before.unity");
        var spawn = UnityEngine.Object.FindObjectsOfType<VRCSceneDescriptor>().First(d=>d.gameObject.scene==scene).spawns.First(t=>t);
        Vector3 eye = spawn.position + Vector3.up * 1.65f;
        Vector3 position = eye + spawn.forward*2f + spawn.right*1.5f;
        Quaternion rotation = Quaternion.LookRotation(spawn.right,Vector3.up);
        // 입구 우측 벽 우선 배치, 벽 미검출 시 임시 위치 사용
        RaycastHit hit;
        bool wall = Physics.Raycast(eye+spawn.forward*1.5f,spawn.right,out hit,5f,~0,QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y)<.15f;
        if(wall) { position=hit.point+hit.normal*.035f; position.y=spawn.position.y+1.8f; rotation=Quaternion.LookRotation(-hit.normal,Vector3.up); }
        var board=Build(position,rotation);
        var localization=board.GetComponent<ExhibitionLanguage>();
        for(int mode=1;mode<=3;mode++)
        {
            localization.languageMode=mode;localization.ApplyLanguage();Canvas.ForceUpdateCanvases();
            CaptureBoard(board.transform,Output+"/notice-"+(mode==1?"ja":mode==2?"ko":"en")+".png");
        }
        localization.languageMode=1;localization.ApplyLanguage();
        localization.languageMode=0;UdonSharpEditorUtility.CopyProxyToUdon(localization);
        AssetDatabase.SaveAssets();
        PrefabUtility.SaveAsPrefabAssetAndConnect(board,Prefab,InteractionMode.AutomatedAction);
        Capture(eye,spawn.rotation,Output+"/entrance-after.png");
        if(!wasDirty && !EditorSceneManager.SaveScene(scene)) throw new Exception("TEST save failed.");
        File.WriteAllText(Output+"/build.done","Board="+BoardName+"\nPosition="+board.transform.position+" Rotation="+board.transform.eulerAngles+"\nWall="+wall+"\nPrefab="+Prefab+"\nScene saved="+!wasDirty+"\nAuto language enabled. Existing scene changes preserved.\n");
    }
    static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 0;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }
    static void Panel(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var image = Rect(name, parent, position, size).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }
    static Text Label(string key, Transform parent, Vector2 position, Vector2 size, int points, Font font, Color color)
    {
        var label = Rect(key, parent, position, size).gameObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = points;
        label.alignment = TextAnchor.UpperLeft;
        label.color = color;
        label.supportRichText = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.raycastTarget = false;
        return label;
    }
    // TEST 전용 안내판 생성, 기존 안내판 중복 생성 방지
    public static GameObject Build(Vector3 position, Quaternion rotation)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != Target || EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("TEST edit mode only.");
        if (scene.GetRootGameObjects().Any(g => g.name == BoardName)) throw new Exception("Notice already exists.");
        var table = JsonUtility.FromJson<Translations>(File.ReadAllText(Content));
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/_ShinjukuExhibition/Fonts/ShinjukuVisitorUI.otf");
        if (!font || table.keys.Length != 13 || table.english.Length != 13 || table.japanese.Length != 13 || table.korean.Length != 13) throw new Exception("Invalid notice content or font.");
        var root = Rect(BoardName, null, Vector2.zero, new Vector2(1600, 1800));
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Place multilingual update notice");
        root.SetPositionAndRotation(position, rotation);
        root.localScale = Vector3.one * .0016f;
        root.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var white = new Color(.93f, .96f, .98f);
        var muted = new Color(.69f, .77f, .81f);
        var accent = new Color(.84f,.71f,.43f);
        Panel("Background", root, Vector2.zero, root.sizeDelta, new Color(.042f,.051f,.058f,1));
        var masthead=Label("World name",root,new Vector2(0,820),new Vector2(1456,45),25,font,accent);
        masthead.text="SHINJUKU LIVE STREET / 2.8 > 2.9";
        Panel("Header rule", root, new Vector2(0, 657), new Vector2(1456, 2), new Color(.28f,.31f,.32f));
        var labels = new Text[13];
        labels[0] = Label("title", root, new Vector2(0,735), new Vector2(1456,105), 68, font, white);
        labels[0].fontStyle=FontStyle.Bold;
        Panel("Main update",root,new Vector2(0,482),new Vector2(1456,286),new Color(.078f,.095f,.093f));
        Panel("MAIN badge",root,new Vector2(-619,572),new Vector2(136,48),accent);
        var badge=Label("MAIN",root,new Vector2(-619,572),new Vector2(136,48),25,font,new Color(.08f,.10f,.10f));
        badge.text="MAIN";badge.alignment=TextAnchor.MiddleCenter;badge.fontStyle=FontStyle.Bold;
        labels[1]=Label("performanceTitle",root,new Vector2(64,568),new Vector2(1160,60),40,font,white);
        labels[2]=Label("performanceBody",root,new Vector2(0,468),new Vector2(1360,136),34,font,white);
        labels[3]=Label("measurement",root,new Vector2(0,374),new Vector2(1360,40),24,font,muted);
        labels[4]=Label("speakerTitle",root,new Vector2(0,263),new Vector2(1456,62),40,font,white);
        labels[5]=Label("speakerBody",root,new Vector2(0,103),new Vector2(1456,232),32,font,white);
        Panel("Section rule",root,new Vector2(0,-40),new Vector2(1456,2),new Color(.28f,.31f,.32f));
        labels[6]=Label("trafficTitle",root,new Vector2(-382,-111),new Vector2(692,62),39,font,white);
        labels[7]=Label("trafficBody",root,new Vector2(-382,-245),new Vector2(692,190),32,font,white);
        labels[8]=Label("effectsTitle",root,new Vector2(382,-111),new Vector2(692,62),39,font,white);
        labels[9]=Label("effectsBody",root,new Vector2(382,-245),new Vector2(692,190),32,font,white);
        Panel("GitHub area",root,new Vector2(0,-503),new Vector2(1456,252),new Color(.068f,.080f,.087f));
        var qr=Rect("GitHub QR",root,new Vector2(-583,-503),new Vector2(208,208)).gameObject.AddComponent<RawImage>();
        qr.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_ShinjukuExhibition/Textures/Profiles/GitHubWithIconQR.png");
        if(!qr.texture) throw new Exception("Missing GitHub QR.");
        qr.raycastTarget=false;
        labels[10]=Label("githubTitle",root,new Vector2(135,-443),new Vector2(1060,60),37,font,white);
        labels[11]=Label("githubBody",root,new Vector2(135,-506),new Vector2(1060,50),29,font,muted);
        var address=Label("GitHub URL",root,new Vector2(135,-567),new Vector2(1060,43),25,font,accent);
        address.text="github.com/hjcud/Shinjuku-Live-Street";
        labels[12]=Label("language",root,new Vector2(0,-684),new Vector2(1456,48),27,font,muted);
        var language = root.gameObject.AddUdonSharpComponent<ExhibitionLanguage>();
        language.keys=table.keys; language.english=table.english; language.japanese=table.japanese; language.korean=table.korean;
        language.staticLabels=labels; language.staticKeys=table.keys; language.allLabels=labels;
        language.originalFonts=labels.Select(t=>(UnityEngine.Object)font).ToArray();
        language.originalSizes=labels.Select(t=>t.fontSize).ToArray();
        language.multilingualSizes=labels.Select(t=>t.fontSize).ToArray();
        language.multilingualFont=font; language.modeButtons=new Image[4];
        language.selectionStatus=null;
        root.gameObject.AddComponent<GraphicRaycaster>();
        root.gameObject.AddComponent<VRCUiShape>();
        var box=root.gameObject.AddComponent<BoxCollider>();
        box.size=new Vector3(1600,1800,2);box.isTrigger=true;
        string[] names={"AUTO","日本語","한국어","English"};
        string[] methods={"UseAuto","UseJapanese","UseKorean","UseEnglish"};
        var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(language);
        for(int i=0;i<4;i++)
        {
            var image=Rect(methods[i],root,new Vector2(-552+i*368,-781),new Vector2(352,84)).gameObject.AddComponent<Image>();
            var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;button.transition=Selectable.Transition.None;
            var nav=button.navigation;nav.mode=Navigation.Mode.None;button.navigation=nav;
            UnityEventTools.AddStringPersistentListener(button.onClick,backing.SendCustomEvent,methods[i]);
            var caption=Label("Caption",image.transform,Vector2.zero,new Vector2(336,80),33,font,white);
            caption.text=names[i];caption.alignment=TextAnchor.MiddleCenter;
            language.modeButtons[i]=image;
        }
        Validate(language);
        language.languageMode=1; language.ApplyLanguage();
        language.languageMode=0;
        UdonSharpEditorUtility.CopyProxyToUdon(language);
        EditorUtility.SetDirty(language);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject=root.gameObject;
        return root.gameObject;
    }
    static void CaptureBoard(Transform board,string file)
    {
        // 안내판 정면 검토용 격리 렌더, 씬 오브젝트 위치 유지
        int[] layers=board.GetComponentsInChildren<Transform>(true).Select(t=>t.gameObject.layer).ToArray();
        var children=board.GetComponentsInChildren<Transform>(true);
        for(int i=0;i<children.Length;i++)children[i].gameObject.layer=31;
        var go=new GameObject("Notice preview camera");go.hideFlags=HideFlags.HideAndDontSave;
        var c=go.AddComponent<Camera>();c.transform.SetPositionAndRotation(board.position-board.forward*4,board.rotation);
        c.orthographic=true;c.orthographicSize=1.50f;c.cullingMask=1<<31;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.025f,.03f,.035f);c.nearClipPlane=.01f;c.farClipPlane=6;
        var rt=new RenderTexture(1600,1800,24);var previous=RenderTexture.active;Texture2D texture=null;
        try{c.targetTexture=rt;c.Render();RenderTexture.active=rt;texture=new Texture2D(1600,1800,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1600,1800),0,0);texture.Apply();File.WriteAllBytes(file,texture.EncodeToPNG());}
        finally{for(int i=0;i<children.Length;i++)children[i].gameObject.layer=layers[i];RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(go);if(texture)UnityEngine.Object.DestroyImmediate(texture);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
    }
    static void Validate(ExhibitionLanguage language)
    {
        var report = new StringBuilder();
        if (language.ResolveLanguage("ja-JP")!=1 || language.ResolveLanguage("ko-KR")!=2 || language.ResolveLanguage("en-US")!=0 || language.ResolveLanguage("fr")!=0) throw new Exception("Language resolution failed.");
        for (int mode=1;mode<=3;mode++)
        {
            language.languageMode=mode;
            language.ApplyLanguage();
            Canvas.ForceUpdateCanvases();
            if(language.languageVisuals!=null && language.languageVisuals.Length>0)
            {
                if(language.languageVisuals.Length!=3)throw new Exception("Expected three localized title images.");
                for(int i=0;i<3;i++)
                    if(!language.languageVisuals[i] || language.languageVisuals[i].activeSelf!=(i==language.currentLanguage))
                        throw new Exception("Localized title visibility mismatch: "+mode+" / "+i);
                report.AppendLine("PASS localized title / mode="+mode+" / visible="+language.languageVisuals[language.currentLanguage].name);
            }
            foreach (var text in language.allLabels)
            {
                var settings=text.GetGenerationSettings(text.rectTransform.rect.size);
                settings.verticalOverflow=VerticalWrapMode.Overflow;
                float height=new TextGenerator().GetPreferredHeight(text.text,settings)/text.pixelsPerUnit;
                report.AppendLine(mode+" / "+text.name+" height="+height+" available="+text.rectTransform.rect.height);
                if(height>text.rectTransform.rect.height+1) throw new Exception("Text overflow: "+mode+" / "+text.name+" / "+height);
                foreach(char c in text.text) if(!char.IsWhiteSpace(c) && !text.font.HasCharacter(c)) throw new Exception("Missing font glyph: "+c);
            }
        }
        File.WriteAllText(Output+"/layout-validation.txt",report.ToString());
    }
    static void Capture(Vector3 position, Quaternion rotation, string file)
    {
        var go = new GameObject("Temporary patch board preview camera");
        go.hideFlags = HideFlags.HideAndDontSave;
        var camera = go.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(position, rotation);
        camera.fieldOfView = 65;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 200;
        camera.clearFlags = CameraClearFlags.Skybox;
        var rt = new RenderTexture(1800, 1200, 24);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            image = new Texture2D(1800, 1200, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1800, 1200), 0, 0);
            image.Apply();
            File.WriteAllBytes(file, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(go);
            if (image) UnityEngine.Object.DestroyImmediate(image);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
