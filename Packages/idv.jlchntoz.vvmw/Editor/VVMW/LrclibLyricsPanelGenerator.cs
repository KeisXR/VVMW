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
            var (previousLegacy, previousTmp) = CreateText(syncedRoot.transform, "Previous Line", 20, 10, 22, FontStyles.Normal, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.55F));
            SetRect(previousTmp.rectTransform, 0.66F, 1F);
            var (currentLegacy, currentTmp) = CreateText(syncedRoot.transform, "Current Line", 32, 16, 38, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            SetRect(currentTmp.rectTransform, 0.28F, 0.72F);
            var (nextLegacy, nextTmp) = CreateText(syncedRoot.transform, "Next Line", 20, 10, 22, FontStyles.Normal, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.55F));
            SetRect(nextTmp.rectTransform, 0F, 0.34F);

            var plainRoot = CreateChild(root.transform, "Plain Lyrics");
            var plainRect = plainRoot.GetComponent<RectTransform>();
            plainRect.anchorMin = Vector2.zero;
            plainRect.anchorMax = Vector2.one;
            plainRect.offsetMin = new Vector2(margin, margin);
            plainRect.offsetMax = new Vector2(-margin, -(reportButtonHeight + reportButtonMargin + 12F));
            var (plainLegacy, plainTmp) = CreateText(plainRoot.transform, "Plain Text", 18, 10, 22, FontStyles.Normal, TextAlignmentOptions.TopLeft, Color.white);
            plainTmp.enableWordWrapping = true;
            plainTmp.overflowMode = TextOverflowModes.Ellipsis;
            plainTmp.rectTransform.anchorMin = Vector2.zero;
            plainTmp.rectTransform.anchorMax = Vector2.one;
            plainTmp.rectTransform.offsetMin = Vector2.zero;
            plainTmp.rectTransform.offsetMax = Vector2.zero;

            var statusRoot = CreateChild(root.transform, "Status");
            var statusRect = statusRoot.GetComponent<RectTransform>();
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = new Vector2(margin, margin);
            statusRect.offsetMax = new Vector2(-margin, -(reportButtonHeight + reportButtonMargin + 12F));
            var (statusLegacy, statusTmp) = CreateText(statusRoot.transform, "Status Text", 22, 12, 26, FontStyles.Normal, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.78F));
            statusTmp.rectTransform.anchorMin = Vector2.zero;
            statusTmp.rectTransform.anchorMax = Vector2.one;
            statusTmp.rectTransform.offsetMin = Vector2.zero;
            statusTmp.rectTransform.offsetMax = Vector2.zero;

            var reportObject = new GameObject("Report Bad Lyrics", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            GameObjectUtility.SetParentAndAlign(reportObject, root);
            var reportRect = reportObject.GetComponent<RectTransform>();
            reportRect.anchorMin = new Vector2(1F, 1F);
            reportRect.anchorMax = new Vector2(1F, 1F);
            reportRect.pivot = new Vector2(1F, 1F);
            reportRect.sizeDelta = new Vector2(reportButtonWidth, reportButtonHeight);
            reportRect.anchoredPosition = new Vector2(-reportButtonMargin, -reportButtonMargin);
            reportObject.GetComponent<Image>().color = new Color(0.16F, 0.16F, 0.16F, 0.9F);
            var (reportLegacy, reportTmp) = CreateText(reportObject.transform, "Label", 14, 8, 16, FontStyles.Normal, TextAlignmentOptions.Center, Color.white);
            reportTmp.rectTransform.anchorMin = Vector2.zero;
            reportTmp.rectTransform.anchorMax = Vector2.one;
            reportTmp.rectTransform.offsetMin = new Vector2(8, 2);
            reportTmp.rectTransform.offsetMax = new Vector2(-8, -2);

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

        static (Text legacy, TextMeshProUGUI tmp) CreateText(Transform parent, string name, int size, int minSize, int maxSize, FontStyles style, TextAlignmentOptions alignment, Color color) {
            var go = new GameObject(name, typeof(RectTransform));
            GameObjectUtility.SetParentAndAlign(go, parent.gameObject);
            var legacy = go.AddComponent<Text>();
            legacy.text = "";
            legacy.fontSize = size;
            legacy.fontStyle = ConvertFontStyle(style);
            legacy.alignment = ConvertAlignment(alignment);
            legacy.color = color;
            legacy.raycastTarget = false;
            legacy.horizontalOverflow = HorizontalWrapMode.Wrap;
            legacy.verticalOverflow = VerticalWrapMode.Truncate;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            var font = GetFontAsset();
            if (font != null) tmp.font = font;
            tmp.text = "";
            tmp.fontSize = size;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = minSize;
            tmp.fontSizeMax = maxSize;
            tmp.fontStyle = style;
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return (legacy, tmp);
        }

        static FontStyle ConvertFontStyle(FontStyles style) {
            if ((style & FontStyles.Bold) != 0) return FontStyle.Bold;
            if ((style & FontStyles.Italic) != 0) return FontStyle.Italic;
            return FontStyle.Normal;
        }

        static TextAnchor ConvertAlignment(TextAlignmentOptions alignment) {
            if (alignment == TextAlignmentOptions.Center) return TextAnchor.MiddleCenter;
            if (alignment == TextAlignmentOptions.TopLeft) return TextAnchor.UpperLeft;
            return TextAnchor.MiddleCenter;
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
                var prev = syncedRoot.Find("Previous Line");
                var curr = syncedRoot.Find("Current Line");
                var next = syncedRoot.Find("Next Line");
                var plain = plainRoot.Find("Plain Text");
                var status = statusRoot.Find("Status Text");
                var label = report.Find("Label");
                so.FindProperty("lyricsPreviousText").objectReferenceValue = prev.GetComponent<Text>();
                so.FindProperty("lyricsPreviousTMPro").objectReferenceValue = prev.GetComponent<TextMeshProUGUI>();
                so.FindProperty("lyricsCurrentText").objectReferenceValue = curr.GetComponent<Text>();
                so.FindProperty("lyricsCurrentTMPro").objectReferenceValue = curr.GetComponent<TextMeshProUGUI>();
                so.FindProperty("lyricsNextText").objectReferenceValue = next.GetComponent<Text>();
                so.FindProperty("lyricsNextTMPro").objectReferenceValue = next.GetComponent<TextMeshProUGUI>();
                so.FindProperty("lyricsPlainText").objectReferenceValue = plain.GetComponent<Text>();
                so.FindProperty("lyricsPlainTMPro").objectReferenceValue = plain.GetComponent<TextMeshProUGUI>();
                so.FindProperty("lyricsStatusText").objectReferenceValue = status.GetComponent<Text>();
                so.FindProperty("lyricsStatusTMPro").objectReferenceValue = status.GetComponent<TextMeshProUGUI>();
                so.FindProperty("lyricsReportButton").objectReferenceValue = report.GetComponent<Button>();
                so.FindProperty("lyricsReportButtonText").objectReferenceValue = label.GetComponent<Text>();
                so.FindProperty("lyricsReportButtonTMPro").objectReferenceValue = label.GetComponent<TextMeshProUGUI>();
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
