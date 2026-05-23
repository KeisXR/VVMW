using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace JLChnToZ.VRC.VVMW.Editors {
    public static class LrclibLyricsPanelGenerator {
        const string menuRoot = "GameObject/VizVid/";
        const string fallbackPackageRoot = "Packages/idv.jlchntoz.vvmw";
        const string prefabRelativePath = "Prefabs/UI Elements/Lyrics Panel.prefab";
        const string fontRelativePath = "Fonts/Comfortaa-Regular SDF.asset";
        const float defaultPanelWidth = 1800F;
        const float defaultPanelHeight = 260F;
        const float maxPanelWidth = 2200F;
        const float minPanelWidth = 900F;
        const float margin = 24F;
        const float reportButtonWidth = 230F;
        const float reportButtonHeight = 36F;
        const float reportButtonMargin = 10F;
        static TMP_FontAsset fontAsset;

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
        public static void GenerateLyricsPanelPrefab() {
            var prefabPath = GetPackageAssetPath(prefabRelativePath);
            EnsureFolder(System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/'));
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
            ConfigurePanelRect(rect, parent as RectTransform);
            var image = root.GetComponent<Image>();
            image.color = new Color(0, 0, 0, 0.55F);
            image.raycastTarget = false;

            var syncedRoot = CreateChild(root.transform, "Synced Lyrics");
            var syncedRect = syncedRoot.GetComponent<RectTransform>();
            syncedRect.anchorMin = Vector2.zero;
            syncedRect.anchorMax = Vector2.one;
            syncedRect.offsetMin = new Vector2(margin, margin);
            syncedRect.offsetMax = new Vector2(-margin, -(reportButtonHeight + reportButtonMargin + 12F));
            var previous = CreateText(syncedRoot.transform, "Previous Line", 20, 10, 22, FontStyles.Normal, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.55F));
            SetRect(previous.rectTransform, 0.66F, 1F);
            var current = CreateText(syncedRoot.transform, "Current Line", 32, 16, 38, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            SetRect(current.rectTransform, 0.28F, 0.72F);
            var next = CreateText(syncedRoot.transform, "Next Line", 20, 10, 22, FontStyles.Normal, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.55F));
            SetRect(next.rectTransform, 0F, 0.34F);

            var plainRoot = CreateChild(root.transform, "Plain Lyrics");
            var plainRect = plainRoot.GetComponent<RectTransform>();
            plainRect.anchorMin = Vector2.zero;
            plainRect.anchorMax = Vector2.one;
            plainRect.offsetMin = new Vector2(margin, margin);
            plainRect.offsetMax = new Vector2(-margin, -(reportButtonHeight + reportButtonMargin + 12F));
            var plain = CreateText(plainRoot.transform, "Plain Text", 18, 10, 22, FontStyles.Normal, TextAlignmentOptions.TopLeft, Color.white);
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
            statusRect.offsetMin = new Vector2(margin, margin);
            statusRect.offsetMax = new Vector2(-margin, -(reportButtonHeight + reportButtonMargin + 12F));
            var status = CreateText(statusRoot.transform, "Status Text", 22, 12, 26, FontStyles.Normal, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.78F));
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
            reportRect.sizeDelta = new Vector2(reportButtonWidth, reportButtonHeight);
            reportRect.anchoredPosition = new Vector2(-reportButtonMargin, -reportButtonMargin);
            reportObject.GetComponent<Image>().color = new Color(0.16F, 0.16F, 0.16F, 0.9F);
            var reportText = CreateText(reportObject.transform, "Label", 14, 8, 16, FontStyles.Normal, TextAlignmentOptions.Center, Color.white);
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

        static TextMeshProUGUI CreateText(Transform parent, string name, int size, int minSize, int maxSize, FontStyles style, TextAlignmentOptions alignment, Color color) {
            var go = new GameObject(name, typeof(RectTransform));
            GameObjectUtility.SetParentAndAlign(go, parent.gameObject);
            var text = go.AddComponent<TextMeshProUGUI>();
            var font = GetFontAsset();
            if (font != null) text.font = font;
            text.text = "";
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = minSize;
            text.fontSizeMax = maxSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        static TMP_FontAsset GetFontAsset() {
            if (fontAsset == null)
                fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GetPackageAssetPath(fontRelativePath));
            return fontAsset;
        }

        static void ConfigurePanelRect(RectTransform rect, RectTransform parentRect) {
            rect.localScale = Vector3.one;
            if (parentRect == null) {
                rect.anchorMin = new Vector2(0.5F, 0.5F);
                rect.anchorMax = new Vector2(0.5F, 0.5F);
                rect.pivot = new Vector2(0.5F, 0.5F);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(defaultPanelWidth, defaultPanelHeight);
                return;
            }

            float parentWidth = parentRect.rect.width;
            float panelWidth = parentWidth > 0 ? Mathf.Clamp(parentWidth * 0.78F, minPanelWidth, maxPanelWidth) : defaultPanelWidth;
            rect.anchorMin = new Vector2(0.5F, 1F);
            rect.anchorMax = new Vector2(0.5F, 1F);
            rect.pivot = new Vector2(0.5F, 0F);
            rect.anchoredPosition = new Vector2(0, 24F);
            rect.sizeDelta = new Vector2(panelWidth, defaultPanelHeight);
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

        static string GetPackageAssetPath(string relativePath) {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LrclibLyricsPanelGenerator).Assembly);
            var packageRoot = packageInfo != null && !string.IsNullOrEmpty(packageInfo.assetPath) ? packageInfo.assetPath : fallbackPackageRoot;
            return $"{packageRoot}/{relativePath}";
        }
    }
}
