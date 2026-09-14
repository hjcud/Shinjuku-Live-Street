using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using UdonSharp;
using UdonSharpEditor;

/// <summary>
/// 입구 스피커 안내도 제작을 위한 씬 확인 및 UI 설정
/// </summary>
[InitializeOnLoad]
public static class SpeakerMapSetup
{
    private const string Output = "output/speaker-map-ui-20260912";
    private const string AssetsPath = "Assets/_Shinjuku/UI/SpeakerMap";

    static SpeakerMapSetup() { EditorApplication.update += CheckRequest; }

    private static void CheckRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string signSymbolsCleanV2 = Output + "/sign-symbols-clean-v2.request";
        if (File.Exists(signSymbolsCleanV2))
        {
            File.Delete(signSymbolsCleanV2);
            try { RefreshSignSymbolsOnly(); }
            catch (Exception exception) { File.WriteAllText(Output + "/sign-symbols-clean-v2.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string signSymbolsClean = Output + "/sign-symbols-clean-v1.request";
        if (File.Exists(signSymbolsClean))
        {
            File.Delete(signSymbolsClean);
            try { RefreshSignSymbolsOnly(); }
            catch (Exception exception) { File.WriteAllText(Output + "/sign-symbols-clean-v1.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string simplifyMapLabels = Output + "/map-callouts-v2.request";
        if (File.Exists(simplifyMapLabels))
        {
            File.Delete(simplifyMapLabels);
            try { SimplifyMapLabels(); }
            catch (Exception exception) { File.WriteAllText(Output + "/simplify-map-labels.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string smokingAreaIconV3 = Output + "/smoking-area-icon-v3.request";
        if (File.Exists(smokingAreaIconV3))
        {
            File.Delete(smokingAreaIconV3);
            try { UpdateSmokingAreaIcon(); }
            catch (Exception exception) { File.WriteAllText(Output + "/smoking-area-icon-v3.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string smokingAreaIconV2 = Output + "/smoking-area-icon-v2.request";
        if (File.Exists(smokingAreaIconV2))
        {
            File.Delete(smokingAreaIconV2);
            try { UpdateSmokingAreaIcon(); }
            catch (Exception exception) { File.WriteAllText(Output + "/smoking-area-icon-v2.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string smokingLayoutInspect = Output + "/smoking-layout-inspect.request";
        if (File.Exists(smokingLayoutInspect))
        {
            File.Delete(smokingLayoutInspect);
            try { InspectSmokingLayout(); }
            catch (Exception exception) { File.WriteAllText(Output + "/smoking-layout-inspect.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string smokingAreaIcon = Output + "/smoking-area-icon.request";
        if (File.Exists(smokingAreaIcon))
        {
            File.Delete(smokingAreaIcon);
            try { UpdateSmokingAreaIcon(); }
            catch (Exception exception) { File.WriteAllText(Output + "/smoking-area-icon.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string musicShopIcon = Output + "/music-shop-icon.request";
        if (File.Exists(musicShopIcon))
        {
            File.Delete(musicShopIcon);
            try { UpdateMusicShopIcon(); }
            catch (Exception exception) { File.WriteAllText(Output + "/music-shop-icon.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string musicShopTextures = Output + "/music-shop-textures.request";
        if (File.Exists(musicShopTextures))
        {
            File.Delete(musicShopTextures);
            try { InspectMusicShopTextures(); }
            catch (Exception exception) { File.WriteAllText(Output + "/music-shop-textures.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string northAccess = Output + "/north-access.request";
        if (File.Exists(northAccess))
        {
            File.Delete(northAccess);
            try
            {
                Capture(new Vector3(96.5f, 8.3f, 27), 6, 1.8f, Output + "/north-access-ground.png");
                Capture(new Vector3(96.5f, 11, 27), 6, 1.8f, Output + "/north-access-middle.png");
                InspectCollisions();
                File.WriteAllText(Output + "/north-access.done", "Read-only north bridge ground and middle slices captured.");
            }
            catch (Exception exception) { File.WriteAllText(Output + "/north-access.failed", exception.ToString()); }
            return;
        }
        string recessRequest = Output + "/west-recesses.request";
        if (File.Exists(recessRequest))
        {
            File.Delete(recessRequest);
            try
            {
                // 서쪽 양쪽 보행로의 천장 아래 공간. 월드 오브젝트는 변경하지 않음.
                Capture(new Vector3(-43, 8.3f, 34), 12, 1.2f, Output + "/northwest-recess-slice.png");
                Capture(new Vector3(-43, 8.3f, -33), 13, 1.2f, Output + "/southwest-recess-slice.png");
                InspectCollisions();
                File.WriteAllText(Output + "/west-recesses.done", "Read-only under-roof views and player collision boundary captured.");
            }
            catch (Exception exception) { File.WriteAllText(Output + "/west-recesses.failed", exception.ToString()); }
            return;
        }
        string nameStyleRequest = Output + "/name-style.request";
        if (File.Exists(nameStyleRequest))
        {
            File.Delete(nameStyleRequest);
            try { UpdateNameStyle(); }
            catch (Exception exception) { File.WriteAllText(Output + "/name-style.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string presentationRequest = Output + "/presentation-audit.request";
        if (File.Exists(presentationRequest))
        {
            File.Delete(presentationRequest);
            try { AuditMapPresentation(); }
            catch (Exception exception) { File.WriteAllText(Output + "/presentation-audit.failed", exception.ToString()); }
            return;
        }
        string roomsRequest = Output + "/rooms.request";
        if (File.Exists(roomsRequest))
        {
            File.Delete(roomsRequest);
            try
            {
                Capture(new Vector3(35, 7.3f, -40), 13, 2, Output + "/spawn-rooms-slice.png");
                Capture(new Vector3(26, 7.8f, 32), 6, 1.5f, Output + "/opposite-room-slice.png");
                Capture(new Vector3(73, 7.8f, 30), 6, 1.5f, Output + "/smoking-room-slice.png");
                Capture(new Vector3(57, 8.8f, -43), 8, 1.75f, Output + "/spawn-gates-slice.png");
                // 개찰구 끝 벽체와 셔터 주변은 천장 아래에서 따로 확인
                Capture(new Vector3(69, 8.1f, -51), 7, 1.5f, Output + "/spawn-end-wall-slice.png");
                // 낮은 중앙 분리대와 기둥 받침대를 횡단보도 양끝에서 확인
                Capture(new Vector3(50, 9, 0), 4, 4, Output + "/road-median-crossing.png");
                Capture(new Vector3(21, 8.1f, 29), 9, 2, Output + "/opposite-facade-slice.png");
                // 육교 남측 계단은 상부/중간/지상 높이를 나눠 계단참 아래의 반환 계단까지 확인
                Capture(new Vector3(85, 18, -25.5f), 4, 2.5f, Output + "/south-stairs-upper.png");
                Capture(new Vector3(85, 12, -25.5f), 4, 2.5f, Output + "/south-stairs-middle.png");
                Capture(new Vector3(85, 8, -25.5f), 4, 2.5f, Output + "/south-stairs-lower.png");
                InspectSpawnGates();
                InspectGateBodies();
                Capture(new Vector3(102, 18, 28), 5, 1.5f, Output + "/north-stairs-detail.png");
                // 다른 외부 계단도 지상 진입부와 계단참이 막힌 선으로 보이지 않는지 확인
                Capture(new Vector3(-37.6f, 9.5f, 35.5f), 10, .8f, Output + "/west-stairs-detail.png");
                Capture(new Vector3(56, 9.5f, 29), 5, 2, Output + "/middle-stairs-detail.png");
                File.WriteAllText(Output + "/rooms.done", "Read-only interior slice: x=9..61, z=-53..-27, camera y=7.3.");
            }
            catch (Exception exception) { File.WriteAllText(Output + "/rooms.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string footprintRequest = Output + "/footprints.request";
        if (File.Exists(footprintRequest))
        {
            File.Delete(footprintRequest);
            try { InspectFootprints(); }
            catch (Exception exception) { File.WriteAllText(Output + "/footprints.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string landmarksRequest = Output + "/landmarks.request";
        if (File.Exists(landmarksRequest))
        {
            File.Delete(landmarksRequest);
            try { UpdateLandmarks(); }
            catch (Exception exception) { File.WriteAllText(Output + "/landmarks.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string mountRequest = Output + "/mount.request";
        if (File.Exists(mountRequest))
        {
            File.Delete(mountRequest);
            try { MountMaps(); }
            catch (Exception exception) { File.WriteAllText(Output + "/mount.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string wallsRequest = Output + "/walls.request";
        if (File.Exists(wallsRequest))
        {
            File.Delete(wallsRequest);
            try { InspectWalls(); }
            catch (Exception exception) { File.WriteAllText(Output + "/walls.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string simplifyRequest = Output + "/simplify.request";
        if (File.Exists(simplifyRequest))
        {
            File.Delete(simplifyRequest);
            try { SimplifyExisting(); }
            catch (Exception exception) { File.WriteAllText(Output + "/simplify.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string collisionRequest = Output + "/collisions.request";
        if (File.Exists(collisionRequest))
        {
            File.Delete(collisionRequest);
            try { InspectCollisions(); }
            catch (Exception exception) { File.WriteAllText(Output + "/collisions.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string validateRequest = Output + "/validate.request";
        if (File.Exists(validateRequest))
        {
            File.Delete(validateRequest);
            try { ValidateExisting(); }
            catch (Exception exception) { File.WriteAllText(Output + "/validate.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string finishRequest = Output + "/finish.request";
        if (File.Exists(finishRequest))
        {
            File.Delete(finishRequest);
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
                var root = scene.GetRootGameObjects().Single(r => r.name == "Entrance Speaker Map");
                var speakers = UnityEngine.Object.FindObjectsOfType<SpeakerController>(true).Where(s => s.gameObject.scene == scene).OrderBy(s => s.name).ToArray();
                Finish(root, speakers, true);
            }
            catch (Exception exception) { File.WriteAllText(Output + "/finish.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string buildRequest = Output + "/build.request";
        if (File.Exists(buildRequest))
        {
            File.Delete(buildRequest);
            try { Build(); }
            catch (Exception exception) { File.WriteAllText(Output + "/build.failed", exception.ToString()); Debug.LogException(exception); }
            return;
        }
        string request = Output + "/inspect.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        try { Inspect(); }
        catch (Exception exception) { File.WriteAllText(Output + "/failed.txt", exception.ToString()); Debug.LogException(exception); }
    }

    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }

    /// <summary>현재 배치와 라벨은 그대로 두고 FloorPlan 텍스처의 간판 기호만 갱신한다.</summary>
    private static void RefreshSignSymbolsOnly()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var roots = scene.GetRootGameObjects().Where(r => r.name == "Entrance Speaker Map" || r.name == "Opposite Gate Speaker Map").ToArray();
        if (roots.Length != 2) throw new InvalidOperationException("Expected two maps.");
        var before = roots.Select(root => EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>())).ToArray();
        var canvases = roots.Select(root => (RectTransform)root.transform.Find("MapCanvas")).ToArray();
        var positions = canvases.Select(canvas => canvas.position).ToArray();
        var rotations = canvases.Select(canvas => canvas.rotation).ToArray();
        var scales = canvases.Select(canvas => canvas.localScale).ToArray();

        ImportTexture("FloorPlan.png", false);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetsPath + "/FloorPlan.png");
        if (!texture || texture.width <= 0 || texture.height <= 0)
            throw new InvalidOperationException("FloorPlan texture is missing or could not be imported.");

        string prefabPath = AssetsPath + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var prefabFloor = prefab.transform.Find("MapCanvas/FloorPlan").GetComponent<UnityEngine.UI.RawImage>();
            if (!prefabFloor) throw new InvalidOperationException("Prefab FloorPlan RawImage missing.");
            prefabFloor.texture = texture;
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        for (int i = 0; i < roots.Length; i++)
        {
            var floor = roots[i].transform.Find("MapCanvas/FloorPlan").GetComponent<UnityEngine.UI.RawImage>();
            if (!floor) throw new InvalidOperationException("Scene FloorPlan RawImage missing: " + roots[i].name);
            floor.texture = texture;
            if (before[i] != EditorJsonUtility.ToJson(roots[i].GetComponent<SpeakerMap>()) || positions[i] != canvases[i].position || rotations[i] != canvases[i].rotation || scales[i] != canvases[i].localScale)
                throw new InvalidOperationException("Map configuration or placement changed: " + roots[i].name);
            if (PrefabUtility.IsPartOfPrefabInstance(floor)) PrefabUtility.RecordPrefabInstancePropertyModifications(floor);
            CapturePanel(canvases[i], Output + "/" + (roots[i].name == "Entrance Speaker Map" ? "entrance" : "opposite") + "-presentation-current.png");
        }
        EditorSceneManager.SaveScene(scene, Output + "/TEST.with-speaker-map.unity", true);
        File.WriteAllText(Output + "/sign-symbols-clean-v1.done", "PASS: six sign markers replaced by uniform 22x6px outline symbols; two stray support fragments omitted; map settings and wall transforms unchanged; recovery copy saved; TEST remains unsaved.");
    }

    /// <summary>도형만으로 알 수 있는 일반 공간명을 숨기고 남은 방향·랜드마크를 재정렬한다.</summary>
    private static void SimplifyMapLabels()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var roots = scene.GetRootGameObjects().Where(r => r.name == "Entrance Speaker Map" || r.name == "Opposite Gate Speaker Map").ToArray();
        if (roots.Length != 2) throw new InvalidOperationException("Expected two maps.");

        ImportTexture("FloorPlan.png", false);
        string prefabPath = AssetsPath + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            ApplySimplifiedMapLabels(prefab.transform);
            VerifySimplifiedMapLabels(prefab.transform);
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        foreach (var root in roots)
        {
            string before = EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>());
            var canvas = (RectTransform)root.transform.Find("MapCanvas");
            Vector3 position = canvas.position;
            Quaternion rotation = canvas.rotation;
            Vector3 scale = canvas.localScale;
            Undo.RegisterFullObjectHierarchyUndo(root, "Simplify map labels");
            ApplySimplifiedMapLabels(root.transform);
            VerifySimplifiedMapLabels(root.transform);
            if (before != EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>()) || position != canvas.position || rotation != canvas.rotation || scale != canvas.localScale)
                throw new InvalidOperationException("Map configuration or placement changed.");
            foreach (string name in SimplifiedHiddenLabels.Concat(new[] { "OppositeGate", "SpawnGate", "MusicShopLabel", "GalleryLabel", "StairsLabel", "WestStairsLabel", "SmokingRoomLabel", "SmokingAreaIcon" }))
            {
                var item = root.transform.Find("MapCanvas/FloorPlan/" + name);
                if (!item) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(item.gameObject)) PrefabUtility.RecordPrefabInstancePropertyModifications(item.gameObject);
                if (PrefabUtility.IsPartOfPrefabInstance(item)) PrefabUtility.RecordPrefabInstancePropertyModifications(item);
                var caption = item.GetComponent<TextMeshProUGUI>();
                if (caption && PrefabUtility.IsPartOfPrefabInstance(caption)) PrefabUtility.RecordPrefabInstancePropertyModifications(caption);
            }
        }

        foreach (var root in roots)
        {
            var canvas = (RectTransform)root.transform.Find("MapCanvas");
            VerifyPresentation(canvas, Output + "/" + root.name + "-simplified-labels-layout.txt");
            CapturePanel(canvas, Output + "/" + (root.name == "Entrance Speaker Map" ? "entrance" : "opposite") + "-simplified-labels.png");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        AuditMapPresentation();
        ValidateExisting();
        File.WriteAllText(Output + "/map-callouts-v2.done", "PASS: orphan room leader removed; escalators centered above their symbols at 20pt; smoking leader extended; music shop and gallery staggered with separate leaders; prefab and both maps updated; original scene unsaved.");
    }

    private static readonly string[] SimplifiedHiddenLabels =
    {
        "IndoorPassage", "Road", "EntranceWalkway", "OppositeWalkway", "OppositeRoomLabel"
    };

    private static void ApplySimplifiedMapLabels(Transform root)
    {
        var floor = root.Find("MapCanvas/FloorPlan");
        if (!floor) throw new InvalidOperationException("Missing FloorPlan: " + root.name);
        foreach (string name in SimplifiedHiddenLabels)
            if (!floor.Find(name)) throw new InvalidOperationException("Missing map label: " + name);

        // 중복 보행로 문구의 자리를 각 방향명이 이어받아 방향 정보만 한 번 표시한다.
        ((RectTransform)floor.Find("OppositeGate")).anchoredPosition = ((RectTransform)floor.Find("OppositeWalkway")).anchoredPosition;
        ((RectTransform)floor.Find("SpawnGate")).anchoredPosition = ((RectTransform)floor.Find("EntranceWalkway")).anchoredPosition;
        // 점포마다 독립된 연결선 끝에 문구를 두어 선이 다른 문구를 가로지르지 않게 한다.
        var music = (RectTransform)floor.Find("MusicShopLabel");
        var gallery = (RectTransform)floor.Find("GalleryLabel");
        gallery.anchoredPosition = new Vector2(945 - 900, 600 - 1080);
        foreach (string name in new[] { "StairsLabel", "WestStairsLabel" })
        {
            var caption = floor.Find(name).GetComponent<TextMeshProUGUI>();
            caption.fontSize = 20;
            caption.rectTransform.sizeDelta = new Vector2(210, 60);
            caption.rectTransform.anchoredPosition = name == "WestStairsLabel"
                ? new Vector2(324 - 900, 600 - 70)
                : new Vector2(1250 - 900, 600 - 174);
        }
        ((RectTransform)floor.Find("SmokingRoomLabel")).anchoredPosition = new Vector2(1428 - 900, 600 - 136);
        ((RectTransform)floor.Find("SmokingAreaIcon")).anchoredPosition = new Vector2(1485 - 900, 600 - 114);
        foreach (string name in SimplifiedHiddenLabels) floor.Find(name).gameObject.SetActive(false);
    }

    private static void VerifySimplifiedMapLabels(Transform root)
    {
        var floor = root.Find("MapCanvas/FloorPlan");
        if (!floor || SimplifiedHiddenLabels.Any(name => floor.Find(name).gameObject.activeSelf))
            throw new InvalidOperationException("Redundant map labels are still visible: " + root.name);
        if (((RectTransform)floor.Find("OppositeGate")).anchoredPosition != ((RectTransform)floor.Find("OppositeWalkway")).anchoredPosition ||
            ((RectTransform)floor.Find("SpawnGate")).anchoredPosition != ((RectTransform)floor.Find("EntranceWalkway")).anchoredPosition)
            throw new InvalidOperationException("Direction labels were not moved into the cleared walkway positions: " + root.name);
    }

    /// <summary>원본 간판에서 분리한 미도리 심볼만 기존 지도 두 장에 추가한다.</summary>
    private static void UpdateMusicShopIcon()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var roots = scene.GetRootGameObjects().Where(r => r.name == "Entrance Speaker Map" || r.name == "Opposite Gate Speaker Map").ToArray();
        if (roots.Length != 2) throw new InvalidOperationException("Expected two maps.");

        ImportTexture("MidoriMusicIcon.png", true);
        string spritePath = AssetsPath + "/MidoriMusicIcon.png";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (!sprite) throw new InvalidOperationException("Could not import MidoriMusicIcon as a Sprite.");

        string prefabPath = AssetsPath + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            EnsureMusicShopIcon(prefab.transform, sprite);
            VerifyMusicShopIcon(prefab.transform, sprite);
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        foreach (var root in roots)
        {
            string before = EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>());
            var canvas = (RectTransform)root.transform.Find("MapCanvas");
            Vector3 position = canvas.position;
            Quaternion rotation = canvas.rotation;
            Vector3 scale = canvas.localScale;
            Undo.RegisterFullObjectHierarchyUndo(root, "Add Midori music shop map icon");
            EnsureMusicShopIcon(root.transform, sprite);
            VerifyMusicShopIcon(root.transform, sprite);
            if (before != EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>()) || position != canvas.position || rotation != canvas.rotation || scale != canvas.localScale)
                throw new InvalidOperationException("Map configuration or placement changed.");
            var icon = root.transform.Find("MapCanvas/FloorPlan/MusicShopIcon");
            foreach (var component in icon.GetComponents<Component>())
                if (component && PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        AuditMapPresentation();
        ValidateExisting();
        File.WriteAllText(Output + "/music-shop-icon.done", "PASS: transparent Midori symbol added beside the music-shop label on the prefab and both map instances; map configuration and wall transforms unchanged; recovery scene and current previews saved; TEST remains unsaved.");
    }

    private static void EnsureMusicShopIcon(Transform root, Sprite sprite)
    {
        var floor = root.Find("MapCanvas/FloorPlan");
        var label = floor ? floor.Find("MusicShopLabel") : null;
        if (!floor || !label) throw new InvalidOperationException("Missing FloorPlan or MusicShopLabel: " + root.name);
        var existing = floor.Find("MusicShopIcon");
        // 스프라이트 내부의 투명 여백을 감안해 실제 심볼 오른쪽 끝이 일본어 'み' 바로 앞에 오도록 배치.
        var rect = existing ? (RectTransform)existing : Rect(floor, "MusicShopIcon", 657 - 900, 600 - 966, 48, 48);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = ((RectTransform)label).anchoredPosition + new Vector2(-100,15);
        rect.sizeDelta = new Vector2(48, 48);
        var image = rect.GetComponent<UnityEngine.UI.Image>();
        if (!image) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        rect.SetSiblingIndex(label.GetSiblingIndex());
    }

    private static void VerifyMusicShopIcon(Transform root, Sprite expectedSprite)
    {
        var icon = root.Find("MapCanvas/FloorPlan/MusicShopIcon");
        var label = root.Find("MapCanvas/FloorPlan/MusicShopLabel") as RectTransform;
        var rect = icon as RectTransform;
        var image = icon ? icon.GetComponent<UnityEngine.UI.Image>() : null;
        if (!rect || !label || !image || image.sprite != expectedSprite || image.raycastTarget || !image.preserveAspect || image.color != Color.white)
            throw new InvalidOperationException("Midori music shop icon settings invalid: " + root.name);
        if (Vector2.Distance(rect.sizeDelta, new Vector2(48, 48)) > .01f || Vector2.Distance(rect.anchoredPosition, label.anchoredPosition + new Vector2(-100,15)) > .01f)
            throw new InvalidOperationException("Midori music shop icon layout invalid: " + root.name + "; position=" + rect.anchoredPosition + "; size=" + rect.sizeDelta);
    }

    /// <summary>투명 흡연장 픽토그램만 기존 지도 두 장에 추가한다.</summary>
    private static void UpdateSmokingAreaIcon()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var roots = scene.GetRootGameObjects().Where(r => r.name == "Entrance Speaker Map" || r.name == "Opposite Gate Speaker Map").ToArray();
        if (roots.Length != 2) throw new InvalidOperationException("Expected two maps.");

        ImportTexture("SmokingAreaIcon.png", true);
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetsPath + "/SmokingAreaIcon.png");
        if (!sprite) throw new InvalidOperationException("Could not import SmokingAreaIcon as a Sprite.");

        string prefabPath = AssetsPath + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            EnsureSmokingAreaIcon(prefab.transform, sprite);
            VerifySmokingAreaIcon(prefab.transform, sprite);
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        foreach (var root in roots)
        {
            string before = EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>());
            var canvas = (RectTransform)root.transform.Find("MapCanvas");
            Vector3 position = canvas.position;
            Quaternion rotation = canvas.rotation;
            Vector3 scale = canvas.localScale;
            Undo.RegisterFullObjectHierarchyUndo(root, "Add smoking-area map icon");
            EnsureSmokingAreaIcon(root.transform, sprite);
            VerifySmokingAreaIcon(root.transform, sprite);
            if (before != EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>()) || position != canvas.position || rotation != canvas.rotation || scale != canvas.localScale)
                throw new InvalidOperationException("Map configuration or placement changed.");
            var icon = root.transform.Find("MapCanvas/FloorPlan/SmokingAreaIcon");
            foreach (var component in icon.GetComponents<Component>())
                if (component && PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        AuditMapPresentation();
        ValidateExisting();
        File.WriteAllText(Output + "/smoking-area-icon.done", "PASS: transparent rounded smoking-area pictogram added beside the label on the prefab and both map instances; map configuration and wall transforms unchanged; recovery scene and current previews saved; TEST remains unsaved.");
    }

    private static void EnsureSmokingAreaIcon(Transform root, Sprite sprite)
    {
        var floor = root.Find("MapCanvas/FloorPlan");
        var label = floor ? floor.Find("SmokingRoomLabel") : null;
        if (!floor || !label) throw new InvalidOperationException("Missing FloorPlan or SmokingRoomLabel: " + root.name);
        var existing = floor.Find("SmokingAreaIcon");
        // 행 높이가 아닌 실제 한자 글립의 시각 높이에 맞추고, 글립 오른쪽 간격은 약 4px로 유지한다.
        var rect = existing ? (RectTransform)existing : Rect(floor, "SmokingAreaIcon", 1485 - 900, 600 - 114, 26, 26);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = ((RectTransform)label).anchoredPosition + new Vector2(57,22);
        rect.sizeDelta = new Vector2(26, 26);
        var image = rect.GetComponent<UnityEngine.UI.Image>();
        if (!image) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        rect.SetSiblingIndex(label.GetSiblingIndex());
    }

    private static void VerifySmokingAreaIcon(Transform root, Sprite expectedSprite)
    {
        var icon = root.Find("MapCanvas/FloorPlan/SmokingAreaIcon");
        var label = root.Find("MapCanvas/FloorPlan/SmokingRoomLabel") as RectTransform;
        var rect = icon as RectTransform;
        var image = icon ? icon.GetComponent<UnityEngine.UI.Image>() : null;
        if (!rect || !label || !image || image.sprite != expectedSprite || image.raycastTarget || !image.preserveAspect || image.color != Color.white)
            throw new InvalidOperationException("Smoking-area icon settings invalid: " + root.name);
        if (Vector2.Distance(rect.sizeDelta, new Vector2(26, 26)) > .01f || Vector2.Distance(rect.anchoredPosition, label.anchoredPosition + new Vector2(57,22)) > .01f)
            throw new InvalidOperationException("Smoking-area icon layout invalid: " + root.name + "; position=" + rect.anchoredPosition + "; size=" + rect.sizeDelta);
        if (rect.anchoredPosition.y <= label.anchoredPosition.y)
            throw new InvalidOperationException("Smoking icon must align to the upper localized label line.");
    }

    private static void InspectSmokingLayout()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var report = new StringBuilder();
        foreach (var root in scene.GetRootGameObjects().Where(r => r.name == "Entrance Speaker Map" || r.name == "Opposite Gate Speaker Map"))
        {
            var label = root.transform.Find("MapCanvas/FloorPlan/SmokingRoomLabel").GetComponent<TextMeshProUGUI>();
            var icon = (RectTransform)root.transform.Find("MapCanvas/FloorPlan/SmokingAreaIcon");
            label.ForceMeshUpdate(true);
            report.AppendLine(root.name + "; text=" + label.text.Replace("\n", "\\n") + "; rectPos=" + label.rectTransform.anchoredPosition + "; rectSize=" + label.rectTransform.sizeDelta + "; fontSize=" + label.fontSize + "; bounds=" + label.textBounds);
            report.AppendLine("iconPos=" + icon.anchoredPosition + "; iconSize=" + icon.sizeDelta);
            for (int i = 0; i < label.textInfo.lineCount; i++)
            {
                var extents = label.textInfo.lineInfo[i].lineExtents;
                report.AppendLine("line" + i + "; min=" + extents.min + "; max=" + extents.max + "; floorMin=" + (extents.min + label.rectTransform.anchoredPosition) + "; floorMax=" + (extents.max + label.rectTransform.anchoredPosition));
            }
        }
        File.WriteAllText(Output + "/smoking-layout-inspect.txt", report.ToString());
        File.WriteAllText(Output + "/smoking-layout-inspect.done", "Read-only smoking label/icon mesh bounds captured.");
    }

    // 이름만 전용 머티리얼을 사용. 지도 지명과 대체 글꼴의 원본 스타일은 변경하지 않음
    private static void UpdateNameStyle()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var roots = scene.GetRootGameObjects().Where(r => r.name == "Entrance Speaker Map" || r.name == "Opposite Gate Speaker Map").ToArray();
        if (roots.Length != 2) throw new InvalidOperationException("Expected two maps.");
        foreach (var root in roots) VerifyNameTemplate(root.transform);
        if (!TMP_Settings.matchMaterialPreset) throw new InvalidOperationException("Fallback material preset matching is required.");
        string path = AssetsPath + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try
        {
            VerifyNameTemplate(prefab.transform);
            var font = prefab.transform.Find("MapCanvas/FloorPlan/SpeakerMarkers/SpeakerMarkerTemplate/PerformerName").GetComponent<TextMeshProUGUI>().font;
            string materialPath = AssetsPath + "/PerformerNameOutline.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(font.material) { name = "PerformerNameOutline" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_FaceColor", ColorOf("4B6954").linear);
            material.SetColor("_OutlineColor", ColorOf("EEE7D5").linear);
            material.SetFloat("_OutlineWidth", .28f);
            material.SetFloat("_FaceDilate", .3f);
            material.EnableKeyword("OUTLINE_ON");
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            ApplyNameStyle(prefab.transform, material);
            PrefabUtility.SaveAsPrefabAsset(prefab, path);
            foreach (var root in roots)
            {
                Undo.RegisterFullObjectHierarchyUndo(root, "Outline map performer names");
                ApplyNameStyle(root.transform, material);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        EditorSceneManager.MarkSceneDirty(scene);
        ValidateExisting();
        File.WriteAllText(Output + "/name-style.done", "PASS: prefab and both map templates use hidden name plates and a shared white outline material; map runtime unchanged; sample render and recovery scene saved; TEST remains unsaved.");
    }

    private static void VerifyNameTemplate(Transform root)
    {
        var template = root.Find("MapCanvas/FloorPlan/SpeakerMarkers/SpeakerMarkerTemplate");
        if (!template || !template.Find("NamePlate") || !template.Find("PerformerName") || !template.Find("PerformerName").GetComponent<TextMeshProUGUI>())
            throw new InvalidOperationException("Missing performer name template: " + root.name);
    }

    private static void ApplyNameStyle(Transform root, Material material)
    {
        var template = root.Find("MapCanvas/FloorPlan/SpeakerMarkers/SpeakerMarkerTemplate");
        var plate = template.Find("NamePlate").gameObject;
        plate.SetActive(false);
        var label = template.Find("PerformerName").GetComponent<TextMeshProUGUI>();
        label.color = Color.white;
        label.fontSharedMaterial = material;
        label.fontStyle = FontStyles.Bold;
        label.fontWeight = FontWeight.Bold;
        label.extraPadding = true;
        label.UpdateMeshPadding();
        SpeakerMapMarkerReview.ConfigureEdgeLabels(label);
        // 비활성 카드와 글자 변경을 씬 인스턴스에도 명시적으로 기록
        if (PrefabUtility.IsPartOfPrefabInstance(plate)) PrefabUtility.RecordPrefabInstancePropertyModifications(plate);
        if (PrefabUtility.IsPartOfPrefabInstance(label)) PrefabUtility.RecordPrefabInstancePropertyModifications(label);
        if (plate.activeSelf || label.fontSharedMaterial != material) throw new InvalidOperationException("Name style was not applied.");
    }

    // 현재 씬의 실제 표시 상태를 확인. 프리팹이나 예전 미리보기만으로 판정하지 않음
    private static void AuditMapPresentation()
    {
        var report = new StringBuilder();
        var scene = SceneManager.GetActiveScene();
        report.AppendLine("Active scene=" + scene.path + "; dirty=" + scene.isDirty);
        foreach (var map in UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true))
        {
            report.AppendLine("MAP " + PathOf(map.transform) + "; scene=" + map.gameObject.scene.path);
            var canvas = map.transform.Find("MapCanvas");
            if (!canvas) continue;
            foreach (string name in new[] { "TopAccent", "SpeakerLegend", "LegendText", "RefreshNote", "PillarLegend", "PillarLegendText", "TreeLegend", "TreeLegendText", "SignLegend", "SignLegendText", "FloorPlan/SpawnGate", "FloorPlan/OppositeGate" })
            {
                var item = canvas.Find(name);
                var text = item ? item.GetComponent<TextMeshProUGUI>() : null;
                report.AppendLine(name + ": " + (item ? "active=" + item.gameObject.activeSelf + "; text=" + (text ? text.text : "") : "missing"));
            }
            string prefix = map.name == "Entrance Speaker Map" ? "entrance" : "opposite";
            CapturePanel((RectTransform)canvas, Output + "/" + prefix + "-presentation-current.png");
        }
        File.WriteAllText(Output + "/presentation-audit.txt", report.ToString());
    }

    // 개찰구 몸체만 골라 같은 기준의 평면 사각형으로 정리. 문짝과 장식 부품은 제외
    private static void InspectGateBodies()
    {
        var report = new StringBuilder("name,x,z,yaw,width,length,height\n");
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var filter in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
        {
            if (!PathOf(filter.transform).StartsWith("00_World/Shinjuku/1-UV O-2048")) continue;
            var mesh = filter.sharedMesh;
            if (!mesh) continue;
            var world = mesh.vertices.Select(v => filter.transform.TransformPoint(v)).ToArray();
            // UV 경계에서 분리된 정점도 같은 위치면 연결된 부품으로 취급
            var keys = new System.Collections.Generic.Dictionary<Vector3Int, int>();
            var parent = Enumerable.Range(0, world.Length).ToArray();
            Func<int, int> find = null;
            find = i => parent[i] == i ? i : (parent[i] = find(parent[i]));
            for (int i = 0; i < world.Length; i++)
            {
                var key = Vector3Int.RoundToInt(world[i] * 1000);
                if (keys.TryGetValue(key, out int other)) parent[find(i)] = find(other);
                else keys.Add(key, i);
            }
            var indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                parent[find(indices[i + 1])] = find(indices[i]);
                parent[find(indices[i + 2])] = find(indices[i]);
            }
            float yaw = filter.transform.eulerAngles.y;
            var rotation = Quaternion.Euler(0, yaw, 0);
            var inverse = Quaternion.Inverse(rotation);
            foreach (var group in Enumerable.Range(0, world.Length).GroupBy(i => find(i)))
            {
                var points = group.Select(i => inverse * world[i]).ToArray();
                var bounds = new Bounds(points[0], Vector3.zero);
                foreach (var point in points) bounds.Encapsulate(point);
                var center = rotation * bounds.center;
                bool north = center.x > 17 && center.x < 24 && center.z > 31 && center.z < 35;
                bool south = center.x > 47 && center.x < 67 && center.z > -49 && center.z < -39;
                if ((!north && !south) || bounds.size.x < .09f || bounds.size.x > .65f || bounds.size.z < 1.5f || bounds.size.z > 2.3f || bounds.size.y < .4f) continue;
                report.AppendLine(filter.name + "," + string.Join(",", new[] { center.x, center.z, yaw, bounds.size.x, bounds.size.z, bounds.size.y }.Select(v => v.ToString("F4", culture))));
            }
        }
        File.WriteAllText(Output + "/gate-bodies.csv", report.ToString());
    }

    // 개찰구 몸체/난간을 실제 위치에서 투영. 반복 간격을 추정하지 않고 모델 배치를 그대로 사용
    private static void InspectSpawnGates()
    {
        var report = new StringBuilder("kind,x1,y1,z1,x2,y2,z2,x3,y3,z3\n");
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var filter in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
        {
            if (!PathOf(filter.transform).StartsWith("00_World/Shinjuku/1-UV O-2048")) continue;
            var mesh = filter.sharedMesh;
            if (!mesh) continue;
            var vertices = mesh.vertices.Select(v => filter.transform.TransformPoint(v)).ToArray();
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
                var center = (a + b + c) / 3;
                if (center.x < 47.5f || center.x > 66.5f || center.z < -49 || center.z > -39 || center.y < 6.85f || center.y > 8) continue;
                report.AppendLine("gate," + string.Join(",", new[] { a.x,a.y,a.z,b.x,b.y,b.z,c.x,c.y,c.z }.Select(v => v.ToString("F4", culture))));
            }
        }
        File.WriteAllText(Output + "/spawn-gate-footprints.csv", report.ToString());
    }

    // 지상 받침대와 육교 계단의 실제 투영을 확인하기 위한 읽기 전용 추출
    private static void InspectFootprints()
    {
        if (SceneManager.GetActiveScene().path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var bases = new StringBuilder("kind,x1,y1,z1,x2,y2,z2,x3,y3,z3\n");
        var bridge = new StringBuilder("kind,x1,y1,z1,x2,y2,z2,x3,y3,z3\n");
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var filter in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
        {
            if (!PathOf(filter.transform).StartsWith("00_World/Shinjuku/")) continue;
            var mesh = filter.sharedMesh;
            if (!mesh) continue;
            bool tree = filter.name.Contains("Tree Fin") && filter.transform.position.y < 8;
            bool sign = filter.name.StartsWith("1-UV I-2048 Fin") || filter.name.StartsWith("1-UV H-1024 Fin");
            var vertices = mesh.vertices.Select(v => filter.transform.TransformPoint(v)).ToArray();
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
                var center = (a + b + c) / 3;
                bool isBase = (tree || sign) && a.y >= 6.65f && b.y >= 6.65f && c.y >= 6.65f && a.y <= 7.5f && b.y <= 7.5f && c.y <= 7.5f;
                bool isBridge = center.x > 76 && center.x < 109 && center.z > -30 && center.z < 40 && center.y > 7.4f && center.y < 18 && Mathf.Abs(Vector3.Cross(b-a, c-a).normalized.y) > .8f;
                if (!isBase && !isBridge) continue;
                string coordinates = string.Join(",", new[] { a.x,a.y,a.z,b.x,b.y,b.z,c.x,c.y,c.z }.Select(v => v.ToString("F4", culture)));
                if (isBase) bases.AppendLine((tree ? "tree" : "sign") + "," + coordinates);
                if (isBridge) bridge.AppendLine("surface," + coordinates);
            }
        }
        File.WriteAllText(Output + "/base-footprints.csv", bases.ToString());
        File.WriteAllText(Output + "/bridge-surfaces.csv", bridge.ToString());
        Capture(new Vector3(97, 30, 0), 50, .6f, Output + "/bridge-detail.png");
        File.WriteAllText(Output + "/footprints.done", "Read-only world mesh projections exported.");
    }

    /// <summary>지도 배경과 랜드마크 설명만 갱신. 벽면 배치 및 스피커 연결은 유지</summary>
    private static void UpdateLandmarks()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var roots = scene.GetRootGameObjects().Where(r => r.name == "Entrance Speaker Map" || r.name == "Opposite Gate Speaker Map").ToArray();
        if (roots.Length != 2 || roots.Any(r => !r.GetComponent<SpeakerMap>() || !r.transform.Find("MapCanvas/FloorPlan")))
            throw new InvalidOperationException("Expected two existing map hierarchies.");
        EditorSceneManager.SaveScene(scene, Output + "/TEST.before-landmarks.unity", true);
        ImportTexture("FloorPlan.png", false);
        string path = AssetsPath + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try { LandmarkLabels(prefab.transform); PrefabUtility.SaveAsPrefabAsset(prefab, path); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (var root in roots)
        {
            string before = EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>());
            var canvas = (RectTransform)root.transform.Find("MapCanvas");
            Vector3 position = canvas.position;
            Quaternion rotation = canvas.rotation;
            Vector3 scale = canvas.localScale;
            Undo.RegisterFullObjectHierarchyUndo(root, "Update map landmarks");
            LandmarkLabels(root.transform);
            if (before != EditorJsonUtility.ToJson(root.GetComponent<SpeakerMap>()) || position != canvas.position || rotation != canvas.rotation || scale != canvas.localScale)
                throw new InvalidOperationException("Map configuration or placement changed.");
            foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>())
            {
                label.ForceMeshUpdate();
                if (label.isTextOverflowing) throw new InvalidOperationException("Landmark label overflow: " + label.name);
            }
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                if (component && PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            string prefix = root.name == "Entrance Speaker Map" ? "entrance" : "opposite";
            VerifyPresentation(canvas, Output + "/" + prefix + "-ui-audit.txt");
            CapturePanel(canvas, Output + "/" + prefix + "-map-ui.png");
            CaptureEntrance(canvas, Output + "/" + prefix + "-wall-angle.png", canvas.position - canvas.forward * 5 + canvas.right * 2.5f);
        }
        ValidateExisting();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, Output + "/TEST.with-speaker-map.unity", true);
        File.WriteAllText(Output + "/landmarks.done", "PASS: both maps updated; map configuration and wall transforms unchanged; active label overflow and overlap checks passed; scene copy saved, active TEST remains unsaved.\n");
    }

    // 글자 영역끼리 겹치는지 검사. 숨겨진 표시 원본과 런타임 공연자 이름은 정적 안내 검사에서 제외
    public static void VerifyPresentation(RectTransform canvas, string reportPath)
    {
        var labels = canvas.GetComponentsInChildren<TextMeshProUGUI>()
            .Where(label => !label.transform.IsChildOf(canvas.Find("FloorPlan/SpeakerMarkers")) && !string.IsNullOrWhiteSpace(label.text)).ToArray();
        var areas = new UnityEngine.Rect[labels.Length];
        var report = new StringBuilder();
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i].ForceMeshUpdate();
            var bounds = labels[i].textBounds;
            Vector3 min = canvas.InverseTransformPoint(labels[i].transform.TransformPoint(bounds.min));
            Vector3 max = canvas.InverseTransformPoint(labels[i].transform.TransformPoint(bounds.max));
            areas[i] = UnityEngine.Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            report.AppendLine(labels[i].name + ": " + labels[i].text + " " + areas[i]);
        }
        int overlaps = 0;
        for (int i = 0; i < areas.Length; i++)
            for (int j = i + 1; j < areas.Length; j++)
            {
                float width = Mathf.Min(areas[i].xMax, areas[j].xMax) - Mathf.Max(areas[i].xMin, areas[j].xMin);
                float height = Mathf.Min(areas[i].yMax, areas[j].yMax) - Mathf.Max(areas[i].yMin, areas[j].yMin);
                if (width <= .5f || height <= .5f) continue;
                report.AppendLine("OVERLAP: " + labels[i].name + " / " + labels[j].name);
                overlaps++;
            }
        report.AppendLine("Static labels=" + labels.Length + "; overlaps=" + overlaps);
        File.WriteAllText(reportPath, report.ToString());
        if (overlaps > 0) throw new InvalidOperationException("Static map labels overlap. See " + reportPath);
    }

    private static void LandmarkLabels(Transform root)
    {
        var canvas = root.Find("MapCanvas");
        var floor = canvas.Find("FloorPlan");
        // 나무의 실제 위치는 유지하고 보행로 이름을 기둥과 수목 사이 빈 공간으로 이동
        ((RectTransform)floor.Find("OppositeWalkway")).anchoredPosition = new Vector2(-200, 282);
        // 도로 중앙 시설물을 가리지 않도록 차도 이름을 북쪽 차로의 빈 면으로 이동
        if (floor.Find("Road")) ((RectTransform)floor.Find("Road")).anchoredPosition = new Vector2(-130, 120);
        var font = canvas.Find("Title").GetComponent<TextMeshProUGUI>().font;
        // 점포 이름은 바닥 도형 밖의 연결선 끝에 표시. 기존 정적 글꼴에 필요한 글자만 추가
        const string roomNames = "미도리 악기점사진 전시관흡연실개찰구 옆 방VRC 신주쿠역 쪽파스타 신주쿠 쪽역 쪽 보행로파스타 쪽 보행로에스컬레이터";
        string pending = new string(roomNames.Where(c => !char.IsWhiteSpace(c) && !font.HasCharacter(c)).Distinct().ToArray());
        if (pending.Length > 0)
        {
            var previousMode = font.atlasPopulationMode;
            try
            {
                font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                if (!font.TryAddCharacters(pending, out string missing)) throw new InvalidOperationException("Missing room glyphs: " + missing);
            }
            finally { font.atlasPopulationMode = previousMode; }
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
        }
        if (!floor.Find("MusicShopLabel")) MapLabel(floor, font, "MusicShopLabel", "미도리 악기점", 757, 981, 200, 27, "596773");
        if (!floor.Find("GalleryLabel")) MapLabel(floor, font, "GalleryLabel", "사진 전시관", 945, 1025, 170, 27, "596773");
        if (!floor.Find("OppositeRoomLabel")) MapLabel(floor, font, "OppositeRoomLabel", "개찰구 옆 방", 1105, 110, 190, 27, "596773");
        if (!floor.Find("SmokingRoomLabel")) MapLabel(floor, font, "SmokingRoomLabel", "흡연실", 1428, 160, 160, 27, "596773");
        if (!floor.Find("BridgeLabel")) MapLabel(floor, font, "BridgeLabel", "육교 · 상부", 1470, 510, 260, 28, "637E99");
        ((RectTransform)floor.Find("BridgeLabel")).anchoredPosition = new Vector2(550, 130);
        if (!floor.Find("StairsLabel")) MapLabel(floor, font, "StairsLabel", "계단", 1255, 175, 160, 25, "778793");
        if (!floor.Find("WestStairsLabel")) MapLabel(floor, font, "WestStairsLabel", "계단", 385, 170, 100, 25, "778793");
        // 지상 단면에서 확인한 두 외부 시설은 일반 계단이 아니라 에스컬레이터
        foreach (string name in new[] { "StairsLabel", "WestStairsLabel" })
        {
            var label = floor.Find(name).GetComponent<TextMeshProUGUI>();
            label.text = "에스컬레이터";
            label.rectTransform.sizeDelta = new Vector2(230, 52);
        }
        ((RectTransform)floor.Find("WestStairsLabel")).anchoredPosition = new Vector2(-415, 430);
        if (!canvas.Find("TreeLegend")) Panel(canvas, "TreeLegend", -60, -708, 20, 20, "78AD89");
        if (!canvas.Find("TreeLegendText")) Label(canvas, "TreeLegendText", "나무", 15, -708, 100, 60, 28, font, "596773", TextAlignmentOptions.MidlineLeft);
        if (!canvas.Find("SignLegend")) Panel(canvas, "SignLegend", 135, -708, 14, 26, "A66872");
        if (!canvas.Find("SignLegendText")) Label(canvas, "SignLegendText", "표지판", 218, -708, 130, 60, 28, font, "596773", TextAlignmentOptions.MidlineLeft);
        // 종류별 아이콘 범례 대신 실제 바닥 점유 면을 뜻하는 장애물 범례로 통일
        canvas.Find("PillarLegend").GetComponent<UnityEngine.UI.Image>().color = ColorOf("87959C");
        var obstacleLegend = canvas.Find("PillarLegendText").GetComponent<TextMeshProUGUI>();
        obstacleLegend.text = "장애물 · 기둥 / 화분 / 표지판 받침대";
        obstacleLegend.rectTransform.sizeDelta = new Vector2(600, 60);
        obstacleLegend.rectTransform.anchoredPosition = new Vector2(67, -708);
        // 안내판은 위치 정보에 집중. 하단 범례/갱신 안내와 상단 장식선은 표시하지 않음
        foreach (string name in new[] { "TopAccent", "SpeakerLegend", "LegendText", "RefreshNote", "PillarLegend", "PillarLegendText", "TreeLegend", "TreeLegendText", "SignLegend", "SignLegendText" })
        {
            var decoration = canvas.Find(name);
            if (decoration) decoration.gameObject.SetActive(false);
        }
        // 어느 쪽에서 스폰하더라도 바뀌지 않는 주변 시설 기준 이름 사용
        var galleryGate = floor.Find("SpawnGate").GetComponent<TextMeshProUGUI>();
        galleryGate.text = "VRC 신주쿠역 쪽";
        galleryGate.rectTransform.sizeDelta = new Vector2(360, galleryGate.rectTransform.sizeDelta.y);
        var smokingGate = floor.Find("OppositeGate").GetComponent<TextMeshProUGUI>();
        smokingGate.text = "파스타 신주쿠 쪽";
        smokingGate.rectTransform.sizeDelta = new Vector2(360, smokingGate.rectTransform.sizeDelta.y);
        // 보행로도 개찰구와 같은 기준으로 명명. 어느 안내판에서 읽어도 같은 쪽을 가리킴
        floor.Find("OppositeWalkway").GetComponent<TextMeshProUGUI>().text = "파스타 쪽 보행로";
        floor.Find("EntranceWalkway").GetComponent<TextMeshProUGUI>().text = "역 쪽 보행로";
        // 현재 위치 점은 움직이지 않고 글자만 같은 여백으로 붙임
        var here = (RectTransform)floor.Find("Here");
        var hereLabel = here.Find("Label").GetComponent<TextMeshProUGUI>();
        bool textOnLeft = here.anchoredPosition.x < 0;
        hereLabel.rectTransform.anchoredPosition = new Vector2(textOnLeft ? -122 : 122, 0);
        hereLabel.rectTransform.sizeDelta = new Vector2(180, 48);
        hereLabel.alignment = textOnLeft ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
    }

    /// <summary>
    /// 기존 안내도 디자인을 유지 보수하고 두 개찰구 벽면에 연결
    /// </summary>
    private static void MountMaps()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var entrance = scene.GetRootGameObjects().Single(r => r.name == "Entrance Speaker Map");
        var speakers = UnityEngine.Object.FindObjectsOfType<SpeakerController>(true).Where(s => s.gameObject.scene == scene).OrderBy(s => s.name).ToArray();
        if (speakers.Length == 0) throw new InvalidOperationException("No scene speakers found.");
        EditorSceneManager.SaveScene(scene, Output + "/TEST.before-wall-maps.unity", true);
        ImportTexture("FloorPlan.png", false);
        ImportTexture("SpeakerIcon.png", true);
        ImportTexture("HereIcon.png", true);
        string prefabPath = AssetsPath + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try { StyleMap(prefab.transform); PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var opposite = scene.GetRootGameObjects().SingleOrDefault(r => r.name == "Opposite Gate Speaker Map");
        if (!opposite)
        {
            opposite = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
            opposite.name = "Opposite Gate Speaker Map";
            Undo.RegisterCreatedObjectUndo(opposite, "Add opposite gate map");
        }
        var report = new StringBuilder();
        PlaceWallMap(entrance, speakers, new Vector3(70, 8.55f, -38.5f), Vector3.right, "entrance", report);
        PlaceWallMap(opposite, speakers, new Vector3(20, 8.55f, 38), Vector3.left, "opposite", report);
        UdonSharpProgramAsset.CompileAllCsPrograms();
        ValidateExisting();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, Output + "/TEST.with-speaker-map.unity", true);
        File.WriteAllText(Output + "/mount.done", report + "\nBoth maps connected to five speakers. Active TEST remains unsaved; scene copy saved.\n");
    }

    private static void StyleMap(Transform root)
    {
        var canvas = root.Find("MapCanvas");
        canvas.Find("Board").GetComponent<UnityEngine.UI.Image>().color = Color.white;
        if (!canvas.Find("MetalFrame")) Panel(canvas, "MetalFrame", 0, 0, 2000, 1620, "303940");
        canvas.Find("MetalFrame").SetAsFirstSibling();
        if (!canvas.Find("TopAccent")) Panel(canvas, "TopAccent", 0, 785, 1960, 10, "03B75B");
        foreach (var text in canvas.GetComponentsInChildren<TextMeshProUGUI>(true)) text.color = ColorOf("596773");
        canvas.Find("Title").GetComponent<TextMeshProUGUI>().color = ColorOf("202B34");
        canvas.Find("Brand").GetComponent<TextMeshProUGUI>().color = ColorOf("078D51");
        var floor = canvas.Find("FloorPlan");
        floor.Find("Building").gameObject.SetActive(false);
        floor.Find("OppositeGate").GetComponent<TextMeshProUGUI>().color = ColorOf("168458");
        floor.Find("SpawnGate").GetComponent<TextMeshProUGUI>().color = ColorOf("168458");
        // 기둥과 개찰구 도형을 가리지 않는 빈 공간에 이름 배치
        ((RectTransform)floor.Find("SpawnGate")).anchoredPosition = new Vector2(700, -460);
        var here = floor.Find("Here");
        var dot = here.Find("Dot").GetComponent<UnityEngine.UI.Image>();
        dot.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetsPath + "/HereIcon.png");
        dot.color = Color.white;
        dot.rectTransform.sizeDelta = new Vector2(42, 42);
        var hereLabel = here.Find("Label").GetComponent<TextMeshProUGUI>();
        hereLabel.color = ColorOf("287CF5");
        hereLabel.rectTransform.anchoredPosition = new Vector2(122, 0);
        hereLabel.rectTransform.sizeDelta = new Vector2(180, 48);
        hereLabel.alignment = TextAlignmentOptions.MidlineLeft;
        var plate = floor.Find("SpeakerMarkers/SpeakerMarkerTemplate/NamePlate").GetComponent<UnityEngine.UI.Image>();
        plate.color = Color.white;
        var outline = plate.GetComponent<UnityEngine.UI.Outline>();
        if (!outline) outline = plate.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = ColorOf("CBD3D9");
        outline.effectDistance = new Vector2(1, -1);
        var nameMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetsPath + "/PerformerNameOutline.mat");
        if (nameMaterial) ApplyNameStyle(root, nameMaterial);
        var font = canvas.Find("Title").GetComponent<TextMeshProUGUI>().font;
        if (!floor.Find("Road")) MapLabel(floor, font, "Road", "차도", 770, 530, 200, 28, "83919D");
        if (!canvas.Find("PillarLegend")) Panel(canvas, "PillarLegend", -270, -708, 18, 18, "778592");
        if (!canvas.Find("PillarLegendText")) Label(canvas, "PillarLegendText", "기둥", -193, -708, 100, 60, 28, font, "596773", TextAlignmentOptions.MidlineLeft);
    }

    private static void PlaceWallMap(GameObject root, SpeakerController[] speakers, Vector3 rayOrigin, Vector3 direction, string prefix, StringBuilder report)
    {
        var hit = Physics.RaycastAll(rayOrigin, direction, 15, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => h.collider.name.StartsWith("Collider") && Mathf.Abs(h.normal.y) < .1f).OrderBy(h => h.distance).FirstOrDefault();
        if (!hit.collider) throw new InvalidOperationException("No wall found for " + prefix);
        Undo.RegisterFullObjectHierarchyUndo(root, "Style and mount speaker map");
        StyleMap(root.transform);
        var canvas = (RectTransform)root.transform.Find("MapCanvas");
        canvas.SetPositionAndRotation(hit.point + hit.normal * .045f, Quaternion.LookRotation(-hit.normal, Vector3.up));
        canvas.localScale = Vector3.one * .00125f;
        var origin = root.transform.Find("MapOrigin");
        if (origin.position != new Vector3(-70, 0, -67) || origin.rotation != Quaternion.identity || origin.lossyScale != Vector3.one)
            throw new InvalidOperationException("Map origin changed.");
        var here = (RectTransform)canvas.Find("FloorPlan/Here");
        var point = origin.InverseTransformPoint(canvas.position);
        here.anchoredPosition = new Vector2(point.x * 10 - 900, point.z * 10 - 600);
        if (prefix == "entrance") ((RectTransform)here.Find("Label")).anchoredPosition = new Vector2(122, 5);
        else ((RectTransform)here.Find("Label")).anchoredPosition = new Vector2(-205, 0);
        var map = root.GetComponent<SpeakerMap>();
        var settings = new SerializedObject(map);
        var sources = settings.FindProperty("speakerControllers");
        sources.arraySize = speakers.Length;
        for (int i = 0; i < speakers.Length; i++) sources.GetArrayElementAtIndex(i).objectReferenceValue = speakers[i];
        settings.ApplyModifiedProperties();
        UdonSharpEditorUtility.CopyProxyToUdon(map);
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(map);
        Component[] references;
        if (!backing.publicVariables.TryGetVariableValue("speakerControllers", out references) || references.Length != speakers.Length || references.Any(s => !s))
            throw new InvalidOperationException("Speaker references missing for " + prefix);
        foreach (var component in root.GetComponentsInChildren<Component>(true))
            if (component && PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>())
        {
            label.ForceMeshUpdate();
            if (label.isTextOverflowing) throw new InvalidOperationException("Label overflow: " + prefix + "/" + label.name);
        }
        CapturePanel(canvas, Output + "/" + prefix + "-map-ui.png");
        CaptureEntrance(canvas, Output + "/" + prefix + "-wall-preview.png", canvas.position - canvas.forward * 7);
        CaptureEntrance(canvas, Output + "/" + prefix + "-wall-angle.png", canvas.position - canvas.forward * 5 + canvas.right * 2.5f);
        report.AppendLine(prefix + " wall=" + PathOf(hit.collider.transform) + " position=" + canvas.position.ToString("F3") + " rotation=" + canvas.eulerAngles.ToString("F2") + " gap=0.045m; Here=" + here.anchoredPosition);
    }

    private static void InspectWalls()
    {
        if (SceneManager.GetActiveScene().path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var report = new StringBuilder();
        var origins = new[] { new Vector3(55.72f, 8.5f, -52.32f), new Vector3(20, 8.5f, 35), new Vector3(65, 8.5f, -46), new Vector3(70, 8.5f, -43) };
        var directions = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        foreach (var origin in origins)
            foreach (var direction in directions)
            {
                var hits = Physics.RaycastAll(origin, direction, 25, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
                foreach (var hit in hits.Take(3)) report.AppendLine("from=" + origin + " direction=" + direction + " hit=" + hit.point + " normal=" + hit.normal + " path=" + PathOf(hit.collider.transform));
            }
        File.WriteAllText(Output + "/walls.txt", report.ToString());
    }

    private static void SimplifyExisting()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var root = scene.GetRootGameObjects().Single(r => r.name == "Entrance Speaker Map");
        var map = root.GetComponent<SpeakerMap>();
        string before = EditorJsonUtility.ToJson(map);
        EditorSceneManager.SaveScene(scene, Output + "/TEST.before-simplification.unity", true);
        ImportTexture("FloorPlan.png", false);
        string prefabPath = AssetsPath + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            SimplifyLabels(prefab.transform);
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        Undo.RegisterFullObjectHierarchyUndo(root, "Simplify speaker map labels");
        SimplifyLabels(root.transform);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        if (before != EditorJsonUtility.ToJson(map)) throw new InvalidOperationException("Map configuration unexpectedly changed.");
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>())
        {
            label.ForceMeshUpdate();
            if (label.isTextOverflowing) throw new InvalidOperationException("Label overflow: " + label.name);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        ValidateExisting();
        CapturePanel((RectTransform)root.transform.Find("MapCanvas"), Output + "/map-simplified.png");
        File.WriteAllText(Output + "/simplify.done", "Simplified existing floor plan and labels. Runtime map JSON unchanged. Prefab saved; active TEST scene left unsaved, scene copies preserved. See validate.done for connection checks.");
    }

    private static void SimplifyLabels(Transform root)
    {
        var map = root.Find("MapCanvas/FloorPlan");
        if (!map || !map.Find("OppositeGate") || !map.Find("SpawnGate") || !map.Find("Here/Label"))
            throw new InvalidOperationException("Expected existing map labels.");
        ((RectTransform)map.Find("OppositeGate")).anchoredPosition = new Vector2(5, 530);
        ((RectTransform)map.Find("SpawnGate")).anchoredPosition = new Vector2(425, -325);
        ((RectTransform)map.Find("IndoorPassage")).anchoredPosition = new Vector2(185, -241);
        map.Find("Building").gameObject.SetActive(false);
        var here = map.Find("Here/Label").GetComponent<TextMeshProUGUI>();
        here.rectTransform.anchoredPosition = new Vector2(121, 0);
        here.rectTransform.sizeDelta = new Vector2(180, 48);
        here.alignment = TextAlignmentOptions.MidlineLeft;
        here.fontSize = 26;
    }

    private static void InspectCollisions()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var report = new StringBuilder("Scene dirty=" + scene.isDirty + "\n");
        var segments = new StringBuilder("x1,y1,x2,y2\n");
        int playerLayer = LayerMask.NameToLayer("PlayerLocal");
        report.AppendLine("PlayerLocal layer=" + playerLayer);
        foreach (var collider in UnityEngine.Object.FindObjectsOfType<Collider>())
        {
            if (collider.gameObject.scene != scene || !collider.enabled || collider.isTrigger) continue;
            if (playerLayer >= 0 && Physics.GetIgnoreLayerCollision(playerLayer, collider.gameObject.layer)) continue;
            var bounds = collider.bounds;
            if (bounds.min.y > 7.7f || bounds.max.y < 7.7f || bounds.min.x > 110 || bounds.max.x < -70 || bounds.min.z > 53 || bounds.max.z < -67) continue;
            report.AppendLine(PathOf(collider.transform) + " | " + collider.GetType().Name + " | " + bounds);
            if (collider is MeshCollider meshCollider && meshCollider.sharedMesh)
            {
                var mesh = meshCollider.sharedMesh;
                var vertices = mesh.vertices.Select(v => collider.transform.TransformPoint(v)).ToArray();
                var indices = mesh.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                    SliceTriangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]], segments);
            }
            else if (collider is BoxCollider box)
            {
                var corners = new Vector3[8];
                for (int i = 0; i < 8; i++) corners[i] = box.transform.TransformPoint(box.center + Vector3.Scale(box.size * .5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                int[] triangles = { 0,1,3,0,3,2,4,6,7,4,7,5,0,4,5,0,5,1,2,3,7,2,7,6,0,2,6,0,6,4,1,5,7,1,7,3 };
                for (int i = 0; i < triangles.Length; i += 3) SliceTriangle(corners[triangles[i]], corners[triangles[i+1]], corners[triangles[i+2]], segments);
            }
        }
        File.WriteAllText(Output + "/collisions.txt", report.ToString());
        File.WriteAllText(Output + "/collision-slice.csv", segments.ToString());
    }

    private static void SliceTriangle(Vector3 a, Vector3 b, Vector3 c, StringBuilder output)
    {
        Vector3[] vertices = { a, b, c };
        var points = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < 3; i++)
        {
            Vector3 first = vertices[i], second = vertices[(i + 1) % 3];
            if ((first.y < 7.7f) == (second.y < 7.7f) || Mathf.Abs(first.y - second.y) < .0001f) continue;
            points.Add(Vector3.Lerp(first, second, (7.7f - first.y) / (second.y - first.y)));
        }
        if (points.Count != 2) return;
        output.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2},{3:F2}", (points[0].x + 70) * 10, (53 - points[0].z) * 10, (points[1].x + 70) * 10, (53 - points[1].z) * 10));
    }

    [MenuItem("Tools/Shinjuku/Add speaker map to TEST")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open TEST in edit mode.");
        if (scene.GetRootGameObjects().Any(r => r.name == "Entrance Speaker Map"))
            throw new InvalidOperationException("Speaker map already exists. Edit it instead of creating a duplicate.");
        var speakers = UnityEngine.Object.FindObjectsOfType<SpeakerController>(true)
            .Where(s => s.gameObject.scene == scene).OrderBy(s => s.name).ToArray();
        if (speakers.Length == 0) throw new InvalidOperationException("No scene speakers found.");
        bool wasDirty = scene.isDirty;
        Directory.CreateDirectory(Output);
        if (!File.Exists(Output + "/TEST.before-map.unity")) File.Copy(scene.path, Output + "/TEST.before-map.unity");
        ImportTexture("FloorPlan.png", false);
        ImportTexture("SpeakerIcon.png", true);
        var font = CreateFont("MapLabels", false);
        var namesFont = CreateFont("PerformerNames", true);
        const string labels = "공연 안내도 설치된 스피커와 공연자 위치 맞은편 개찰구 보행로 입구 쪽 실내 연결 통로 스폰 현재 위치 건물 및 지도 밖 설치 반환 시 갱신 SHINJUKU LIVE STREET YOU ARE HERE SPEAKER MAP 2.9";
        if (font.atlasPopulationMode != AtlasPopulationMode.Static)
        {
            if (!font.TryAddCharacters(labels, out string missing)) throw new InvalidOperationException("Missing map glyphs: " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(font);
        }

        var root = new GameObject("Entrance Speaker Map");
        Undo.RegisterCreatedObjectUndo(root, "Add entrance speaker map");
        var canvasRect = Rect(root.transform, "MapCanvas", 0, 0, 1960, 1580);
        canvasRect.SetPositionAndRotation(new Vector3(49, 8.55f, -50.5f), Quaternion.Euler(0, 270, 0));
        canvasRect.localScale = Vector3.one * .0019f;
        var canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        Panel(canvasRect, "Board", 0, 0, 1960, 1580, "203E34");
        Label(canvasRect, "Title", "공연 안내도", -450, 672, 900, 100, 76, font, "F8FAF4", TextAlignmentOptions.MidlineLeft);
        Label(canvasRect, "Subtitle", "설치된 스피커와 공연자 위치", -450, 597, 900, 48, 30, font, "C6D9CE", TextAlignmentOptions.MidlineLeft);
        Label(canvasRect, "Brand", "SHINJUKU\nLIVE STREET", 650, 652, 450, 104, 38, font, "F8FAF4", TextAlignmentOptions.MidlineRight);

        var mapRect = Rect(canvasRect, "FloorPlan", 0, -40, 1800, 1200);
        var background = mapRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        background.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetsPath + "/FloorPlan.png");
        background.raycastTarget = false;
        MapLabel(mapRect, font, "OppositeGate", "맞은편 개찰구", 930, 155, 260, 32);
        MapLabel(mapRect, font, "OppositeWalkway", "맞은편 보행로", 695, 351, 340, 34);
        MapLabel(mapRect, font, "EntranceWalkway", "입구 쪽 보행로", 695, 726, 340, 34);
        MapLabel(mapRect, font, "IndoorPassage", "실내 연결 통로", 1110, 848, 300, 30);
        MapLabel(mapRect, font, "SpawnGate", "스폰 개찰구", 1320, 1063, 230, 30);
        MapLabel(mapRect, font, "Building", "건물 / 지도 밖", 559, 999, 330, 28, "7A8F85");
        var here = Rect(mapRect, "Here", 1190 - 900, 600 - 1035, 22, 22);
        Panel(here, "Dot", 0, 0, 22, 22, "C96A39");
        Label(here, "Label", "현재 위치", -118, 0, 192, 48, 30, font, "9A4525", TextAlignmentOptions.MidlineRight);

        // 바탕과 지명은 고정, 스피커 표시만 별도 Canvas에서 갱신
        var overlay = Rect(mapRect, "SpeakerMarkers", 0, 0, 1800, 1200);
        overlay.gameObject.AddComponent<Canvas>();
        var template = Rect(overlay, "SpeakerMarkerTemplate", 0, 0, 42, 42);
        var icon = template.gameObject.AddComponent<UnityEngine.UI.Image>();
        icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetsPath + "/SpeakerIcon.png");
        icon.raycastTarget = false;
        Panel(template, "NamePlate", 0, -53, 300, 54, "203E34");
        var nameLabel = Label(template, "PerformerName", "", 0, -38, 280, 50, 30, namesFont, "FFFFFF", TextAlignmentOptions.Midline);
        nameLabel.overflowMode = TextOverflowModes.Ellipsis;
        template.gameObject.SetActive(false);
        var legend = Rect(canvasRect, "SpeakerLegend", -870, -708, 46, 46);
        var legendIcon = legend.gameObject.AddComponent<UnityEngine.UI.Image>();
        legendIcon.sprite = icon.sprite;
        legendIcon.raycastTarget = false;
        Label(canvasRect, "LegendText", "스피커 · 공연자", -604, -708, 460, 60, 32, font, "F8FAF4", TextAlignmentOptions.MidlineLeft);
        Label(canvasRect, "RefreshNote", "설치 · 반환 시 갱신", 589, -708, 600, 60, 28, font, "C6D9CE", TextAlignmentOptions.MidlineRight);
        var origin = new GameObject("MapOrigin").transform;
        origin.SetParent(root.transform, false);
        origin.position = new Vector3(-70, 0, -67);
        Finish(root, speakers, wasDirty);
    }

    private static void Finish(GameObject root, SpeakerController[] speakers, bool wasDirty)
    {
        var scene = root.scene;
        var canvasRect = (RectTransform)root.transform.Find("MapCanvas");
        var overlay = (RectTransform)canvasRect.Find("FloorPlan/SpeakerMarkers");
        var template = (RectTransform)overlay.Find("SpeakerMarkerTemplate");
        var origin = root.transform.Find("MapOrigin");
        var font = CreateFont("MapLabels", false);
        var namesFont = CreateFont("PerformerNames", true);
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        string text = string.Join("", root.GetComponentsInChildren<TextMeshProUGUI>(true).Select(t => t.text));
        string pending = new string(text.Where(c => !char.IsWhiteSpace(c) && !font.HasCharacter(c)).Distinct().ToArray());
        string missing;
        if (pending.Length > 0 && !font.TryAddCharacters(pending, out missing)) throw new InvalidOperationException("Missing map glyphs: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        // 표시 이름은 미리 알 수 없으므로 기존 부분 글꼴 대신 CJK 원본을 동적 대체 글꼴로 사용
        string fullFontPath = AssetsPath + "/NotoSansCJKjp-Regular.otf";
        if (!File.Exists(fullFontPath)) File.Copy("output/exhibition-20260909/NotoSansCJKjp-Regular.otf", fullFontPath);
        if (!File.Exists(AssetsPath + "/OFL.txt")) File.Copy("Assets/_ShinjukuExhibition/Fonts/OFL.txt", AssetsPath + "/OFL.txt");
        AssetDatabase.ImportAsset(fullFontPath);
        var fullFont = CreateFont("CJKNames", true, fullFontPath);
        var latin = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fullFont, latin };
        template.GetComponentInChildren<TextMeshProUGUI>(true).font = font;
        pending = new string("…공연자クディン新宿".Where(c => !fullFont.HasCharacter(c)).Distinct().ToArray());
        if (pending.Length > 0 && !fullFont.TryAddCharacters(pending, out missing)) throw new InvalidOperationException("Missing name glyphs: " + missing);
        EditorUtility.SetDirty(fullFont);
        AssetDatabase.SaveAssetIfDirty(fullFont);
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssetIfDirty(font);
        AssetDatabase.SaveAssetIfDirty(namesFont);

        const string programPath = "Assets/_Shinjuku/Scripts/UI/SpeakerMap.asset";
        var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath);
        if (!program)
        {
            if (File.Exists(programPath)) throw new InvalidOperationException("SpeakerMap program path is occupied.");
            program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/_Shinjuku/Scripts/UI/SpeakerMap.cs");
            AssetDatabase.CreateAsset(program, programPath);
        }
        UdonSharpProgramAsset.CompileAllCsPrograms();
        program.UpdateProgram();
        var map = root.GetComponent<SpeakerMap>();
        if (!map) map = root.AddUdonSharpComponent<SpeakerMap>();
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(map);
        if (!backing) throw new InvalidOperationException("Missing backing Udon behaviour.");
        backing.programSource = program;
        var backingSettings = new SerializedObject(backing);
        backingSettings.FindProperty("serializedProgramAsset").objectReferenceValue = program.SerializedProgramAsset;
        backingSettings.ApplyModifiedPropertiesWithoutUndo();
        var serialized = new SerializedObject(map);
        serialized.FindProperty("mapRect").objectReferenceValue = overlay;
        serialized.FindProperty("markerTemplate").objectReferenceValue = template;
        serialized.FindProperty("mapOrigin").objectReferenceValue = origin;
        serialized.FindProperty("worldSize").vector2Value = new Vector2(180, 120);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        UdonSharpEditorUtility.CopyProxyToUdon(map);
        UdonSharpProgramAsset.CompileAllCsPrograms();
        if (!backing || !backing.programSource || !backing.programSource.SerializedProgramAsset || backing.programSource.SerializedProgramAsset.RetrieveProgram() == null)
            throw new InvalidOperationException("Speaker map Udon program is not compiled.");

        // 프리팹에는 지도 내부 참조만 저장하고 씬 스피커는 인스턴스에 연결
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, AssetsPath + "/SpeakerMap.prefab", InteractionMode.AutomatedAction);
        serialized.Update();
        var sources = serialized.FindProperty("speakerControllers");
        sources.arraySize = speakers.Length;
        for (int i = 0; i < speakers.Length; i++) sources.GetArrayElementAtIndex(i).objectReferenceValue = speakers[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        UdonSharpEditorUtility.CopyProxyToUdon(map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.ForceMeshUpdate(true);
            if (label.text.Length > 0 && label.isTextOverflowing) throw new InvalidOperationException("Label overflow: " + label.name);
        }
        CapturePanel(canvasRect, Output + "/map-ui.png");
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssetIfDirty(font);
        AssetDatabase.SaveAssetIfDirty(namesFont);
        AssetDatabase.SaveAssetIfDirty(program);
        if (!wasDirty) EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        File.WriteAllText(Output + "/build.done", "Map created; speakers=" + speakers.Length + "; sceneSaved=" + !wasDirty + "\nOrigin=(-70,0,-67), worldSize=(180,120), map=(1800,1200).\nPrefab contains internal references; scene instance contains speaker references.\nUdon program and label overflow checked. Live multiplayer remains to test.");
    }

    private static void ImportTexture(string file, bool sprite)
    {
        string path = AssetsPath + "/" + file;
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = sprite;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }

    private static TMP_FontAsset CreateFont(string name, bool dynamic, string sourcePath = "Assets/_ShinjukuExhibition/Fonts/ShinjukuVisitorUI.otf")
    {
        string path = AssetsPath + "/" + name + ".asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font && font.material && font.atlasTextures.Length > 0 && font.atlasTextures[0]) return font;
        var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
        var created = TMP_FontAsset.CreateFontAsset(source, 50, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        if (font)
        {
            EditorUtility.CopySerialized(created, font);
            UnityEngine.Object.DestroyImmediate(created);
        }
        else
        {
            font = created;
            AssetDatabase.CreateAsset(font, path);
        }
        font.name = name;
        var settings = new SerializedObject(font);
        settings.FindProperty("m_ClearDynamicDataOnBuild").boolValue = dynamic;
        settings.ApplyModifiedPropertiesWithoutUndo();
        foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssetIfDirty(font);
        return font;
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static Color ColorOf(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }

    private static void Panel(Transform parent, string name, float x, float y, float width, float height, string color)
    {
        var panel = Rect(parent, name, x, y, width, height).gameObject.AddComponent<UnityEngine.UI.Image>();
        panel.color = ColorOf(color);
        panel.raycastTarget = false;
    }

    private static TextMeshProUGUI Label(Transform parent, string name, string content, float x, float y, float width, float height, int size, TMP_FontAsset font, string color, TextAlignmentOptions alignment)
    {
        var text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = ColorOf(color);
        text.enableAutoSizing = false;
        text.enableWordWrapping = false;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }

    private static void MapLabel(Transform parent, TMP_FontAsset font, string name, string content, float x, float y, float width, int size, string color = "3F5E51")
    {
        Label(parent, name, content, x - 900, 600 - y, width, 52, size, font, color, TextAlignmentOptions.Midline);
    }

    public static void CapturePanel(RectTransform panel, string path)
    {
        var children = panel.GetComponentsInChildren<Transform>(true);
        var layers = children.Select(t => t.gameObject.layer).ToArray();
        foreach (var t in children) t.gameObject.layer = 31;
        var go = new GameObject("Map UI preview") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(panel.position - panel.forward * 5, panel.rotation);
        camera.orthographic = true;
        camera.orthographicSize = panel.rect.height * panel.lossyScale.y * .5f + .055f;
        camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = ColorOf("DCE5E0");
        var target = new RenderTexture(Mathf.RoundToInt(panel.rect.width), Mathf.RoundToInt(panel.rect.height), 24);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(go);
            if (image) UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
            for (int i = 0; i < children.Length; i++) children[i].gameObject.layer = layers[i];
        }
    }

    private static void ValidateExisting()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var root = scene.GetRootGameObjects().Single(r => r.name == "Entrance Speaker Map");
        var map = root.GetComponent<SpeakerMap>();
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(map);
        var settings = new SerializedObject(map);
        var overlay = (RectTransform)settings.FindProperty("mapRect").objectReferenceValue;
        var template = (RectTransform)settings.FindProperty("markerTemplate").objectReferenceValue;
        var origin = (Transform)settings.FindProperty("mapOrigin").objectReferenceValue;
        var canvas = (RectTransform)root.transform.Find("MapCanvas");
        var diagnostic = new StringBuilder();
        diagnostic.AppendLine("Proxy sources: " + settings.FindProperty("speakerControllers").arraySize);
        foreach (string symbol in backing.publicVariables.VariableSymbols)
        {
            object value;
            backing.publicVariables.TryGetVariableValue(symbol, out value);
            diagnostic.AppendLine(symbol + "=" + (value == null ? "null" : value.GetType().FullName + " " + value));
            if (value is Array array) foreach (object item in array) diagnostic.AppendLine("  " + item);
        }
        File.WriteAllText(Output + "/connections.txt", diagnostic.ToString());
        Component[] references;
        int sceneSpeakerCount = UnityEngine.Object.FindObjectsOfType<SpeakerController>(true).Count(s => s.gameObject.scene == scene);
        if (!backing.publicVariables.TryGetVariableValue("speakerControllers", out references) || references.Length != sceneSpeakerCount || references.Any(s => !(s is VRC.Udon.UdonBehaviour)))
            throw new InvalidOperationException("Expected all scene speakers connected to Udon map.");
        if (!backing.programSource.SerializedProgramAsset.RetrieveProgram().EntryPoints.HasExportedSymbol("_RefreshMap"))
            throw new InvalidOperationException("Missing compiled map refresh entry point.");
        var displayedFloor=(RectTransform)canvas.Find("FloorPlan");
        var displayedUv=displayedFloor.GetComponent<UnityEngine.UI.RawImage>().uvRect;
        bool fitted=map.mapViewport!=new Vector4(0,0,1,1);
        if (origin.lossyScale != Vector3.one || origin.rotation != Quaternion.identity || overlay.rect.size != (fitted?displayedFloor.rect.size:new Vector2(1800,1200))
            || (fitted && map.mapViewport!=new Vector4(displayedUv.x,displayedUv.y,displayedUv.width,displayedUv.height)))
            throw new InvalidOperationException("Map coordinate calibration changed.");
        var label = template.GetComponentInChildren<TextMeshProUGUI>(true);
        if (template.gameObject.activeSelf || label.richText || label.raycastTarget || label.enableAutoSizing)
            throw new InvalidOperationException("Marker template settings invalid.");
        foreach (var font in new[] { label.font, label.font.fallbackFontAssetTable[0] })
            if (!AssetDatabase.Contains(font.material) || font.atlasTextures.Any(t => !t || !AssetDatabase.Contains(t)))
                throw new InvalidOperationException("Font sub-assets are not persistent.");
        var musicShopSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetsPath + "/MidoriMusicIcon.png");
        if (!musicShopSprite) throw new InvalidOperationException("Missing imported Midori music shop icon Sprite.");
        var smokingAreaSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetsPath + "/SmokingAreaIcon.png");
        if (!smokingAreaSprite) throw new InvalidOperationException("Missing imported smoking-area icon Sprite.");
        foreach (var mapRoot in scene.GetRootGameObjects().Where(r => r.name == "Entrance Speaker Map" || r.name == "Opposite Gate Speaker Map"))
        {
            VerifyMusicShopIcon(mapRoot.transform, musicShopSprite);
            VerifySmokingAreaIcon(mapRoot.transform, smokingAreaSprite);
            VerifySimplifiedMapLabels(mapRoot.transform);
        }

        CapturePanel(canvas, Output + "/map-ui.png");
        var positions = new[] { new Vector3(-20, 7, -18), new Vector3(40, 7, 18), new Vector3(35, 7, -34) };
        var names = new[] { "공연자 A", "クディン", "긴 플레이어 이름 표시 확인용 SAMPLE" };
        var samples = new GameObject[positions.Length];
        try
        {
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = UnityEngine.Object.Instantiate(template.gameObject, overlay);
                samples[i].hideFlags = HideFlags.DontSave;
                var marker = (RectTransform)samples[i].transform;
                marker.anchorMin = marker.anchorMax = map.WorldToMapAnchor(positions[i]);
                marker.anchoredPosition3D = Vector3.zero;
                samples[i].GetComponentInChildren<TextMeshProUGUI>(true).text = names[i];
                samples[i].SetActive(true);
            }
            CapturePanel(canvas, Output + "/map-sample-preview.png");
        }
        finally { foreach (var sample in samples) if (sample) UnityEngine.Object.DestroyImmediate(sample); }
        CaptureEntrance(canvas, Output + "/entrance-preview.png");
        // 현재 씬의 다른 미저장 변경을 강제로 저장하지 않고 복구 가능한 별도 사본 보관
        EditorSceneManager.SaveScene(scene, Output + "/TEST.with-speaker-map.unity", true);
        File.WriteAllText(Output + "/validate.done", "PASS: " + sceneSpeakerCount + " non-null serialized Udon speaker references; compiled refresh entry point; calibrated origin/overlay; inactive literal-text marker template; persistent font materials/atlases.\nPreview markers were temporary and removed.\nScene copy saved to output; current TEST scene remains unsaved.\nVRChat multiplayer installation/return/late-join not exercised live.");
    }

    private static void CaptureEntrance(RectTransform panel, string path, Vector3? viewpoint = null)
    {
        var go = new GameObject("Entrance map preview") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.transform.position = viewpoint ?? new Vector3(55.72f, 8.55f, -52.32f);
        camera.transform.LookAt(panel.position);
        camera.fieldOfView = 50;
        camera.nearClipPlane = .05f;
        var target = new RenderTexture(1600, 1000, 24);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(go);
            if (image) UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    /// <summary>미도리 악기점 주변 렌더러의 원본 텍스처 경로만 읽어 보고한다.</summary>
    private static void InspectMusicShopTextures()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST in edit mode.");
        var center = new Vector3(5.7f, 8f, -45.1f);
        var report = new StringBuilder();
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>(true)
                     .Where(item => item.gameObject.scene == scene && Vector3.Distance(item.bounds.ClosestPoint(center), center) < 18f)
                     .OrderBy(item => Vector3.Distance(item.bounds.center, center)))
        {
            report.AppendLine(PathOf(renderer.transform) + " | bounds=" + renderer.bounds);
            foreach (var material in renderer.sharedMaterials.Where(item => item))
            {
                report.AppendLine("  MAT " + material.name + " | " + AssetDatabase.GetAssetPath(material));
                foreach (var property in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(property);
                    if (texture) report.AppendLine("    " + property + " = " + texture.name + " | " + AssetDatabase.GetAssetPath(texture));
                }
            }
        }
        File.WriteAllText(Output + "/music-shop-textures.txt", report.ToString());
        File.WriteAllText(Output + "/music-shop-textures.done", "Read-only nearby renderer texture inventory complete.");
    }

    private static void Inspect()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST in edit mode.");
        var report = new StringBuilder();
        report.AppendLine("Scene=" + scene.path + "; dirty=" + scene.isDirty);
        foreach (var root in scene.GetRootGameObjects())
        {
            var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer).ToArray();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                report.AppendLine("ROOT " + root.name + " bounds=" + bounds + " renderers=" + renderers.Length);
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<RectTransform>() != null) continue;
                var renderer = t.GetComponent<Renderer>();
                report.AppendLine(PathOf(t) + " | pos=" + t.position.ToString("F2") + " rot=" + t.eulerAngles.ToString("F1") + " active=" + t.gameObject.activeInHierarchy + (renderer == null ? "" : " bounds=" + renderer.bounds));
            }
        }
        foreach (var font in AssetDatabase.FindAssets("t:TMP_FontAsset")) report.AppendLine("FONT " + AssetDatabase.GUIDToAssetPath(font));
        File.WriteAllText(Output + "/scene.txt", report.ToString());
        Capture(new Vector3(0, 150, 0), 100, 1.8f, Output + "/overview.png");
        Capture(new Vector3(20, 9.5f, -7), 60, 1.5f, Output + "/ground-slice.png");
        Capture(new Vector3(20, 13.5f, -7), 60, 1.5f, Output + "/upper-slice.png");
        File.WriteAllText(Output + "/inspect.done", "Scene inspected and orthographic overview captured.");
    }

    private static void Capture(Vector3 position, float halfHeight, float aspect, string path)
    {
        var go = new GameObject("Speaker map capture") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(position, Quaternion.Euler(90, 0, 0));
        camera.orthographic = true;
        camera.orthographicSize = halfHeight;
        camera.aspect = aspect;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 500;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.12f, .14f, .17f);
        var rt = new RenderTexture(Mathf.RoundToInt(1200 * aspect), 1200, 24);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
