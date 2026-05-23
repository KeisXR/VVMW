using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace JLChnToZ.VRC.VVMW.Editors {
    static class LrclibLyricsPanelGenerator {
        const string menuRoot = "GameObject/VizVid/";
        const string prefabPath = "Packages/idv.jlchntoz.vvmw/Prefabs/UI Elements/Lyrics Panel.prefab";

        [MenuItem(menuRoot + "Modules/Lyrics Panel", false, 121)]
        static void CreateLyricsPanelInScene() {
            var handler = FindSelectedUIHandler();
            var parent = handler != null ? handler.transform : Selection.activeTransform;
            var panel = CreatePanel(parent);
            Undo.RegisterCreatedObjectUndo(panel, "Create Lyrics Panel");
            if (handler != null) Assign(panel, handler);
            Selection.activeGameObject = panel;
        }

        [MenuItem("Tools/VizVid/LRCLIB/Generate Lyrics Panel Prefab")]
        static void GenerateLyricsPanelPrefab() {
            EnsureFolder("Packages/idv.jlchntoz.vvmw/Prefabs/UI Elements");
            var panel = CreatePanel(null);
            PrefabUtility.SaveAsPrefabAsset(panel, prefabPath);
            Object.DestroyImmediate(panel);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        static UIHandler FindSelectedUIHandler() {
            var selected = Selection.activeGameObject;
            if (selected == null) return Object.FindObjectOfType<UIHandler>();
            var handler = selected.GetComponentInParent<UIHandler>();
            if (handler != null) return handler;
            return selected.GetComponentInChildren<UIHandler>(true);
        }

        static GameObject CreatePanel(Transform parent) {
            var root = new GameObject("Lyrics Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (parent != null) GameObjectUtility.SetParentAndAlign(root, parent.gameObject);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.1F, 0.02F);
            rect.anchorMax = new Vector2(0.9F, 0.24F);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = root.GetComponent<Image>();
            image.color = new Color(0, 0, 0, 0.55F);

            var syncedRoot = CreateChild(root.transform, "Synced Lyrics");
            var syncedRect = syncedRoot.GetComponent<RectTransform>();
            syncedRect.anchorMin = Vector2.zero;
            syncedRect.anchorMax = Vector2.one;
            syncedRect.offsetMin = new Vector2(16, 40);
            syncedRect.offsetMax = new Vector2(-16, -12);
            var previous = CreateText(syncedRoot.transform, "Previous Line", 20, FontStyles.Normal, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.55F));
            SetRect(previous.rectTransform, 0.66F, 1F);
            var current = CreateText(syncedRoot.transform, "Current Line", 30, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            SetRect(current.rectTransform, 0.30F, 0.70F);
            var next = CreateText(syncedRoot.transform, "Next Line", 20, FontStyles.Normal, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.55F));
            SetRect(next.rectTransform, 0F, 0.34F);

            var plainRoot = CreateChild(root.transform, "Plain Lyrics");
            var plainRect = plainRoot.GetComponent<RectTransform>();
            plainRect.anchorMin = Vector2.zero;
            plainRect.anchorMax = Vector2.one;
            plainRect.offsetMin = new Vector2(16, 40);
            plainRect.offsetMax = new Vector2(-16, -12);
            var plain = CreateText(plainRoot.transform, "Plain Text", 20, FontStyles.Normal, TextAlignmentOptions.TopLeft, Color.white);
            plain.enableWordWrapping = true;
            plain.overflowMode = TextOverflowModes.Ellipsis;
            plain.rectTransform.anchorMin = Vector2.zero;
            plain.rectTransform.anchorMax = Vector2.one;
            plain.rectTransform.offsetMin = Vector2.zero;
            plain.rectTransform.offsetMax = Vector2.zero;

            var statusRoot = CreateChild(root.transform, "Status");
            var statusRect = statusRoot.GetComponent<RectTransform>();
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = new Vector2(16, 40);
            statusRect.offsetMax = new Vector2(-16, -12);
            var status = CreateText(statusRoot.transform, "Status Text", 22, FontStyles.Normal, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.78F));
            status.rectTransform.anchorMin = Vector2.zero;
            status.rectTransform.anchorMax = Vector2.one;
            status.rectTransform.offsetMin = Vector2.zero;
            status.rectTransform.offsetMax = Vector2.zero;

            var reportObject = new GameObject("Report Bad Lyrics", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            GameObjectUtility.SetParentAndAlign(reportObject, root);
            var reportRect = reportObject.GetComponent<RectTransform>();
            reportRect.anchorMin = new Vector2(1F, 1F);
            reportRect.anchorMax = new Vector2(1F, 1F);
            reportRect.pivot = new Vector2(1F, 1F);
            reportRect.sizeDelta = new Vector2(160, 28);
            reportRect.anchoredPosition = new Vector2(-8, -8);
            reportObject.GetComponent<Image>().color = new Color(0.16F, 0.16F, 0.16F, 0.9F);
            var reportText = CreateText(reportObject.transform, "Label", 14, FontStyles.Normal, TextAlignmentOptions.Center, Color.white);
            reportText.rectTransform.anchorMin = Vector2.zero;
            reportText.rectTransform.anchorMax = Vector2.one;
            reportText.rectTransform.offsetMin = new Vector2(8, 2);
            reportText.rectTransform.offsetMax = new Vector2(-8, -2);

            plainRoot.SetActive(false);
            statusRoot.SetActive(false);
            reportObject.SetActive(false);
            return root;
        }

        static GameObject CreateChild(Transform parent, string name) {
            var go = new GameObject(name, typeof(RectTransform));
            GameObjectUtility.SetParentAndAlign(go, parent.gameObject);
            return go;
        }

        static TextMeshProUGUI CreateText(Transform parent, string name, int size, FontStyles style, TextAlignmentOptions alignment, Color color) {
            var go = new GameObject(name, typeof(RectTransform));
            GameObjectUtility.SetParentAndAlign(go, parent.gameObject);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = "";
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        static void SetRect(RectTransform rect, float minY, float maxY) {
            rect.anchorMin = new Vector2(0, minY);
            rect.anchorMax = new Vector2(1, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void Assign(GameObject panel, UIHandler handler) {
            var syncedRoot = panel.transform.Find("Synced Lyrics");
            var plainRoot = panel.transform.Find("Plain Lyrics");
            var statusRoot = panel.transform.Find("Status");
            var report = panel.transform.Find("Report Bad Lyrics");
            using (var so = new SerializedObject(handler)) {
                so.FindProperty("lyricsPanelRoot").objectReferenceValue = panel;
                so.FindProperty("lyricsSyncedRoot").objectReferenceValue = syncedRoot.gameObject;
                so.FindProperty("lyricsPlainRoot").objectReferenceValue = plainRoot.gameObject;
                so.FindProperty("lyricsStatusRoot").objectReferenceValue = statusRoot.gameObject;
                so.FindProperty("lyricsReportButtonObject").objectReferenceValue = report.gameObject;
                so.FindProperty("lyricsPreviousTMPro").objectReferenceValue = syncedRoot.Find("Previous Line").GetComponent<TextMeshProUGUI>();
                so.FindProperty("lyricsCurrentTMPro").objectReferenceValue = syncedRoot.Find("Current Line").GetComponent<TextMeshProUGUI>();
                so.FindProperty("lyricsNextTMPro").objectReferenceValue = syncedRoot.Find("Next Line").GetComponent<TextMeshProUGUI>();
                so.FindProperty("lyricsPlainTMPro").objectReferenceValue = plainRoot.Find("Plain Text").GetComponent<TextMeshProUGUI>();
                so.FindProperty("lyricsStatusTMPro").objectReferenceValue = statusRoot.Find("Status Text").GetComponent<TextMeshProUGUI>();
                so.FindProperty("lyricsReportButton").objectReferenceValue = report.GetComponent<Button>();
                so.FindProperty("lyricsReportButtonTMPro").objectReferenceValue = report.Find("Label").GetComponent<TextMeshProUGUI>();
                so.ApplyModifiedProperties();
            }
            EditorUtility.SetDirty(handler);
        }

        static void EnsureFolder(string folderPath) {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            var parts = folderPath.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++) {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
