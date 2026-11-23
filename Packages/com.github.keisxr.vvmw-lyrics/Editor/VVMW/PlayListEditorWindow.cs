using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using Cysharp.Threading.Tasks;
using VRC.SDKBase;
using JLChnToZ.VRC.Foundation.Editors;
using JLChnToZ.VRC.Foundation.I18N;
using JLChnToZ.VRC.Foundation.I18N.Editors;
using JLChnToZ.VRC.Foundation.ThirdParties.LitJson;

using UnityObject = UnityEngine.Object;
using FUtils = JLChnToZ.VRC.Foundation.Editors.Utils;

namespace JLChnToZ.VRC.VVMW.Editors {

    public class PlayListEditorWindow : EditorWindow {
        static EditorI18N i18n;
        FrontendHandler frontendHandler;
        Core loadedCore;
        string[] playerHandlerNames;
        PlayerType[] playerHandlerTypes;
        string[] localeCodes, localeDisplayNames;
        int selectedLocaleIndex = -2;
        int firstUnityPlayerIndex = -1, firstAvProPlayerIndex = -1;
        [SerializeField] List<PlayList> playLists = new List<PlayList>();
        ReorderableList playListView;
        ReorderableList playListEntryView;
        [NonSerialized] PlayList selectedPlayList;
        bool isDirty;
        Vector2 playListViewScrollPosition, playListEntryViewScrollPosition;
        const string DefaultLyricsEndpoint = "https://k3isd.net/lrc";
        const string NomSeekVrcUrlBase = "https://api.u2b.cx/vrcurl";
        const string NomSeekVrcUrlSetterPath = "Assets/Nomlas/NomSeek/VRCURLSetter.prefab";
        string ytPlaylistUrl;
        [SerializeField] string lyricsEndpoint = DefaultLyricsEndpoint;
        [SerializeField] string lyricsPoolId;
        [SerializeField] string dynamicLyricsPoolId;
        [SerializeField] int dynamicLyricsPoolSize = 10000;
        public static event Action<FrontendHandler> OnFrontendUpdated;
        static readonly GUILayoutOption[] noExpandWidthOptions = new[] { GUILayout.ExpandWidth(false) };

        public FrontendHandler FrontendHandler {
            get => frontendHandler;
            set {
                if (frontendHandler == value) return;
                SaveIfRequired();
                frontendHandler = value;
                DeserializePlayList();
            }
        }

        public static void StartEditPlayList(FrontendHandler handler) {
            var window = GetWindow<PlayListEditorWindow>();
            window.FrontendHandler = handler;
        }

        void UpdateTitle() {
            if (this == null) return;
            VVMWEditorBase.UpdateTitle(titleContent, "PlaylistEditor.title", isDirty);
            titleContent = titleContent; // Trigger update title
        }

        void SetDirty(bool isDirty) {
            if (this.isDirty == isDirty) return;
            this.isDirty = isDirty;
            UpdateTitle();
        }

        void OnEnable() {
            if (i18n == null) i18n = EditorI18N.Instance;
            UpdateTitle();
            if (playListView == null) playListView = new ReorderableList(playLists, typeof(PlayList), true, true, true, true) {
                drawHeaderCallback = DrawPlayListHeader,
                drawElementCallback = DrawPlayList,
                onSelectCallback = PlayListSelected,
                onAddCallback = AddPlayList,
                onRemoveCallback = RemovePlayList,
                onReorderCallback = ReorderPlayList,
                elementHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
                showDefaultBackground = false,
            };
            TrustedUrlUtils.OnTrustedUrlsReady += Repaint;
        }

        void OnDisable() {
            TrustedUrlUtils.OnTrustedUrlsReady -= Repaint;
            SaveIfRequired();
        }

        void OnGUI() {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                FrontendHandler = EditorGUILayout.ObjectField(FrontendHandler, typeof(FrontendHandler), true) as FrontendHandler;
                if (GUILayout.Button(i18n.GetOrDefault("PlaylistEditor.reload"), EditorStyles.toolbarButton, noExpandWidthOptions) &&
                    i18n.DisplayLocalizedDialog2("PlaylistEditor.reload_confirm"))
                    DeserializePlayList();
                using (new EditorGUI.DisabledGroupScope(!isDirty))
                    if (GUILayout.Button(i18n.GetOrDefault("PlaylistEditor.save"), EditorStyles.toolbarButton, noExpandWidthOptions))
                        SerializePlayList();
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledGroupScope(playLists.Count == 0))
                    if (GUILayout.Button(i18n.GetOrDefault("PlaylistEditor.exportAll"), EditorStyles.toolbarButton, noExpandWidthOptions))
                        ExportPlayListToJson(true);
                using (new EditorGUI.DisabledGroupScope(selectedPlayList.entries == null))
                    if (GUILayout.Button(i18n.GetOrDefault("PlaylistEditor.exportSelected"), EditorStyles.toolbarButton, noExpandWidthOptions))
                        ExportPlayListToJson(false);
                if (GUILayout.Button(i18n.GetOrDefault("PlaylistEditor.import"), EditorStyles.toolbarButton, noExpandWidthOptions))
                    ImportPlayListFromJson();
                EditorGUILayout.Space();
                if (GUILayout.Button(i18n.GetOrDefault("PlaylistEditor.updateYTDLP"), EditorStyles.toolbarButton, noExpandWidthOptions))
                    YtdlpResolver.DownLoadYtDlp().Forget();
                if (GUILayout.Button(GUIContent.none, EditorStyles.toolbarDropDown, noExpandWidthOptions)) {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(i18n.GetOrDefault("PlaylistEditor.selectYTDLPInstallation")), false, SelectYtdlpInstallation);
                    menu.ShowAsContext();
                }
            }
            var evt = Event.current;
            using (new EditorGUILayout.HorizontalScope()) {
                Rect playListRect, playListEntryRect;
                using (var vert = new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(Mathf.Min(400, position.width / 2)))) {
                    playListRect = vert.rect;
                    using (var scroll = new EditorGUILayout.ScrollViewScope(playListViewScrollPosition, GUI.skin.box)) {
                        if (frontendHandler == null) EditorGUILayout.HelpBox(i18n.GetOrDefault("PlaylistEditor.select_frontend_message"), MessageType.Info);
                        else playListView?.DoLayoutList();
                        GUILayout.FlexibleSpace();
                        playListViewScrollPosition = scroll.scrollPosition;
                    }
                }
                using (var vert = new EditorGUILayout.VerticalScope()) {
                    playListEntryRect = vert.rect;
                    using (var scroll = new EditorGUILayout.ScrollViewScope(playListEntryViewScrollPosition, GUI.skin.box)) {
                        if (frontendHandler != null) playListEntryView?.DoLayoutList();
                        GUILayout.FlexibleSpace();
                        playListEntryViewScrollPosition = scroll.scrollPosition;
                    }
                    if (playListEntryView != null) {
                        using (new EditorGUILayout.HorizontalScope()) {
                            ytPlaylistUrl = EditorGUILayout.TextField(ytPlaylistUrl);
                            if (selectedLocaleIndex < -1) {
                                var dict = YtdlpResolver.AvailableLocales;
                                localeCodes = new string[dict.Count];
                                localeDisplayNames = new string[dict.Count];
                                int i = 0;
                                foreach (var kv in dict) {
                                    localeCodes[i] = kv.Key;
                                    localeDisplayNames[i] = kv.Value;
                                    i++;
                                }
                                selectedLocaleIndex = Array.IndexOf(localeCodes, YtdlpResolver.SelectedLocale);
                            }
                            using (var changed = new EditorGUI.ChangeCheckScope()) {
                                selectedLocaleIndex = EditorGUILayout.Popup(selectedLocaleIndex, localeDisplayNames, noExpandWidthOptions);
                                if (changed.changed && selectedLocaleIndex >= 0 && selectedLocaleIndex < localeCodes.Length)
                                    YtdlpResolver.SelectedLocale = localeCodes[selectedLocaleIndex];
                            }
                            if (GUILayout.Button(i18n.GetOrDefault("PlaylistEditor.loadFromYoutube"), noExpandWidthOptions)) {
                                FetchPlayList(ytPlaylistUrl).Forget();
                                ytPlaylistUrl = string.Empty;
                            }
                            using (new EditorGUI.DisabledGroupScope(selectedPlayList.entries == null || selectedPlayList.entries.Count == 0))
                                if (GUILayout.Button(i18n.GetOrDefault("PlaylistEditor.fetchTitles"), noExpandWidthOptions))
                                    FetchTitles().Forget();
                            using (new EditorGUI.DisabledGroupScope(selectedPlayList.entries == null || selectedPlayList.entries.Count == 0))
                                if (GUILayout.Button(i18n.GetOrDefault("PlaylistEditor.reverse"), noExpandWidthOptions))
                                    ReversePlaylist();
                        }
                        using (new EditorGUILayout.HorizontalScope()) {
                            lyricsEndpoint = EditorGUILayout.TextField("Lyrics Proxy", GetLyricsEndpoint());
                            using (new EditorGUI.DisabledGroupScope(selectedPlayList.entries == null || selectedPlayList.entries.Count == 0 || string.IsNullOrWhiteSpace(lyricsEndpoint)))
                                if (GUILayout.Button("Generate Direct Lyrics URLs", noExpandWidthOptions))
                                    GenerateLyricsUrls();
                        }
                        using (new EditorGUILayout.HorizontalScope()) {
                            lyricsPoolId = EditorGUILayout.TextField("Lyrics Pool", string.IsNullOrWhiteSpace(lyricsPoolId) ? GetDefaultLyricsPoolId() : lyricsPoolId);
                            using (new EditorGUI.DisabledGroupScope(playLists.Count == 0 || string.IsNullOrWhiteSpace(lyricsEndpoint) || string.IsNullOrWhiteSpace(lyricsPoolId))) {
                                if (GUILayout.Button("Generate Lyrics Pool", noExpandWidthOptions))
                                    GenerateLyricsPool();
                                if (GUILayout.Button("Export Lyrics Manifest", noExpandWidthOptions))
                                    ExportLyricsManifest();
                            }
                        }
                    }
                }
                switch (evt.type) {
                    case EventType.DragUpdated:
                    case EventType.DragPerform:
                        if (playListRect.Contains(evt.mousePosition)) {
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                            if (evt.type == EventType.DragPerform) {
                                HandlePlayListObjectDrop(true);
                                evt.Use();
                            }
                        } else if (playListEntryRect.Contains(evt.mousePosition)) {
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                            if (evt.type == EventType.DragPerform) {
                                HandlePlayListObjectDrop(false);
                                evt.Use();
                            }
                        }
                        break;
                    case EventType.KeyUp:
                        switch (evt.keyCode) {
                            case KeyCode.S:
                                if (evt.control) {
                                    if (!evt.shift) {
                                        SerializePlayList();
                                        evt.Use();
                                    } else if (playLists.Count > 0) {
                                        ExportPlayListToJson(true);
                                        evt.Use();
                                    }
                                }
                                break;
                            case KeyCode.O:
                                if (evt.control) {
                                    ImportPlayListFromJson();
                                    evt.Use();
                                }
                                break;
                            case KeyCode.R:
                                if (evt.control) {
                                    DeserializePlayList();
                                    evt.Use();
                                }
                                break;
                        }
                        break;
                }
            }
            EditorGUILayout.HelpBox(i18n.GetOrDefault("PlaylistEditor.hint"), MessageType.Info);
        }

        void DrawPlayListHeader(Rect rect) => EditorGUI.LabelField(rect, i18n.GetOrDefault("PlaylistEditor.playLists"), EditorStyles.boldLabel);

        void DrawPlayList(Rect rect, int index, bool isActive, bool isFocused) {
            var playList = playLists[index];
            if (playListView.index == index) selectedPlayList = playList;
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                rect.y += 1;
                rect.height = EditorGUIUtility.singleLineHeight;
                var newTitle = EditorGUI.TextField(rect, playList.title);
                if (changed.changed) {
                    playList.title = newTitle;
                    playLists[index] = playList;
                    SetDirty(true);
                }
            }
        }

        void PlayListSelected(ReorderableList list) {
            var index = list.index;
            if (index < 0 || index >= playLists.Count) {
                selectedPlayList = default;
                playListEntryView = null;
                return;
            }
            selectedPlayList = playLists[index];
            playListEntryView = new ReorderableList(selectedPlayList.entries, typeof(PlayListEntry), true, true, true, true) {
                drawHeaderCallback = DrawPlayListEntryHeader,
                drawElementCallback = DrawPlayListEntry,
                onAddCallback = AddPlayListEntry,
                onRemoveCallback = RemovePlayListEntry,
                onReorderCallback = ReorderPlayListEntry,
                elementHeight = (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 6,
                showDefaultBackground = false,
            };
        }

        void DrawPlayListEntryHeader(Rect rect) => EditorGUI.LabelField(rect, selectedPlayList.title, EditorStyles.boldLabel);

        void DrawPlayListEntry(Rect rect, int index, bool isActive, bool isFocused) {
            var labelSize = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 80;
            var entry = selectedPlayList.entries[index];
            rect.y += 1;
            var titleRect = rect;
            titleRect.height = EditorGUIUtility.singleLineHeight;
            float playerRectWidth = titleRect.width;
            if (playerHandlerNames != null) {
                playerRectWidth = EditorStyles.popup.CalcSize(
                    FUtils.GetTempContent(entry.playerIndex >= 0 && entry.playerIndex < playerHandlerNames.Length ? playerHandlerNames[entry.playerIndex] : "")
                ).x;
            }
            titleRect.xMax = titleRect.width - playerRectWidth - 10;
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                titleRect = EditorGUI.PrefixLabel(titleRect, i18n.GetLocalizedContent("PlaylistEditor.entryTitle"));
                var newTitle = EditorGUI.TextField(titleRect, entry.title);
                if (changed.changed) {
                    entry.title = newTitle;
                    selectedPlayList.entries[index] = entry;
                    SetDirty(true);
                }
            }
            if (playerHandlerNames != null) {
                var playerRect = rect;
                playerRect.yMin = titleRect.yMin;
                playerRect.height = EditorGUIUtility.singleLineHeight;
                playerRect.xMin = titleRect.xMax;
                using (var changed = new EditorGUI.ChangeCheckScope()) {
                    var newPlayerIndex = EditorGUI.Popup(playerRect, entry.playerIndex, playerHandlerNames);
                    if (changed.changed) {
                        entry.playerIndex = (byte)newPlayerIndex;
                        selectedPlayList.entries[index] = entry;
                        SetDirty(true);
                    }
                }
            }
            var playerType = playerHandlerTypes != null && entry.playerIndex >= 0 && entry.playerIndex < playerHandlerTypes.Length ? playerHandlerTypes[entry.playerIndex] : PlayerType.Unknown;
            var urlRect = rect;
            urlRect.yMin = titleRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            urlRect.height = EditorGUIUtility.singleLineHeight;
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                urlRect = EditorGUI.PrefixLabel(urlRect, i18n.GetLocalizedContent("PlaylistEditor.url"));
                var newUrl = TrustedUrlUtils.DrawUrlField(entry.url, playerType.ToTrustUrlType(BuildTarget.StandaloneWindows64), urlRect, "");
                if (changed.changed) {
                    entry.url = newUrl;
                    selectedPlayList.entries[index] = entry;
                    SetDirty(true);
                }
            }
            var urlQuestRect = rect;
            urlQuestRect.yMin = urlRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            urlQuestRect.height = EditorGUIUtility.singleLineHeight;
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                urlQuestRect = EditorGUI.PrefixLabel(urlQuestRect, i18n.GetLocalizedContent("PlaylistEditor.urlQuest"));
                var newUrl = string.IsNullOrEmpty(entry.urlForQuest) ? entry.url : entry.urlForQuest;
                newUrl = TrustedUrlUtils.DrawUrlField(newUrl, playerType.ToTrustUrlType(BuildTarget.Android), urlQuestRect, "");
                if (changed.changed) {
                    entry.urlForQuest = newUrl == entry.url ? string.Empty : newUrl;
                    selectedPlayList.entries[index] = entry;
                    SetDirty(true);
                }
            }
            var lyricsUrlRect = rect;
            lyricsUrlRect.yMin = urlQuestRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            lyricsUrlRect.height = EditorGUIUtility.singleLineHeight;
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                lyricsUrlRect = EditorGUI.PrefixLabel(lyricsUrlRect, FUtils.GetTempContent("Lyrics URL"));
                var newUrl = EditorGUI.TextField(lyricsUrlRect, entry.lyricsUrl);
                if (changed.changed) {
                    entry.lyricsUrl = newUrl;
                    selectedPlayList.entries[index] = entry;
                    SetDirty(true);
                }
            }
            var badLyricsUrlRect = rect;
            badLyricsUrlRect.yMin = lyricsUrlRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            badLyricsUrlRect.height = EditorGUIUtility.singleLineHeight;
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                badLyricsUrlRect = EditorGUI.PrefixLabel(badLyricsUrlRect, FUtils.GetTempContent("Bad Lyrics URL"));
                var newUrl = EditorGUI.TextField(badLyricsUrlRect, entry.badLyricsUrl);
                if (changed.changed) {
                    entry.badLyricsUrl = newUrl;
                    selectedPlayList.entries[index] = entry;
                    SetDirty(true);
                }
            }
            var lyricsPoolIndexRect = rect;
            lyricsPoolIndexRect.yMin = badLyricsUrlRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            lyricsPoolIndexRect.height = EditorGUIUtility.singleLineHeight;
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                lyricsPoolIndexRect = EditorGUI.PrefixLabel(lyricsPoolIndexRect, FUtils.GetTempContent("Lyrics Pool"));
                var newIndex = EditorGUI.IntField(lyricsPoolIndexRect, entry.lyricsPoolIndex);
                if (changed.changed) {
                    entry.lyricsPoolIndex = newIndex;
                    selectedPlayList.entries[index] = entry;
                    SetDirty(true);
                }
            }
            EditorGUIUtility.labelWidth = labelSize;
        }

        void DeserializePlayList() {
            if (frontendHandler == null) {
                playerHandlerNames = null;
                firstUnityPlayerIndex = -1;
                firstAvProPlayerIndex = -1;
                loadedCore = null;
                playLists.Clear();
                SetDirty(false);
                return;
            }
            UpdatePlayerHandlerInfos();
            playLists.Clear();
            AppendPlaylist(frontendHandler);
            SetDirty(false);
            if (playListView != null) {
                playListView.index = -1;
                PlayListSelected(playListView);
            }
        }

        void AppendPlaylist(UnityObject frontendHandler) {
            using (var serializedObject = new SerializedObject(frontendHandler)) {
                var playListTitlesProperty = serializedObject.FindProperty("playListTitles");
                var playListUrlOffsetsProperty = serializedObject.FindProperty("playListUrlOffsets");
                var playListUrlsProperty = serializedObject.FindProperty("playListUrls");
                var playListUrlsQuestProperty = serializedObject.FindProperty("playListUrlsQuest");
                var playListEntryTitlesProperty = serializedObject.FindProperty("playListEntryTitles");
                var playListPlayerIndexProperty = serializedObject.FindProperty("playListPlayerIndex");
                var playListLyricsUrlsProperty = serializedObject.FindProperty("playListLyricsUrls");
                var playListBadLyricsUrlsProperty = serializedObject.FindProperty("playListBadLyricsUrls");
                var playListLyricsPoolIndexesProperty = serializedObject.FindProperty("playListLyricsPoolIndexes");
                var playListCount = playListTitlesProperty.arraySize;
                for (int i = 0; i < playListCount; i++) {
                    var urlOffset = playListUrlOffsetsProperty.GetArrayElementAtIndex(i).intValue;
                    var urlCount = (i < playListCount - 1 ? playListUrlOffsetsProperty.GetArrayElementAtIndex(i + 1).intValue : playListUrlsProperty.arraySize) - urlOffset;
                    var playList = new PlayList {
                        title = playListTitlesProperty.GetArrayElementAtIndex(i).stringValue,
                        entries = new List<PlayListEntry>(urlCount)
                    };
                    for (int j = 0; j < urlCount; j++)
                        playList.entries.Add(new PlayListEntry {
                            title = playListEntryTitlesProperty.GetArrayElementAtIndex(urlOffset + j).stringValue,
                            url = playListUrlsProperty.GetArrayElementAtIndex(urlOffset + j).FindPropertyRelative("url").stringValue,
                            urlForQuest = playListUrlsQuestProperty.GetArrayElementAtIndex(urlOffset + j).FindPropertyRelative("url").stringValue,
                            lyricsUrl = GetUrlAt(playListLyricsUrlsProperty, urlOffset + j),
                            badLyricsUrl = GetUrlAt(playListBadLyricsUrlsProperty, urlOffset + j),
                            lyricsPoolIndex = GetIntAt(playListLyricsPoolIndexesProperty, urlOffset + j, -1),
                            playerIndex = playListPlayerIndexProperty.GetArrayElementAtIndex(urlOffset + j).intValue - 1
                        });
                    playLists.Add(playList);
                }
            }
            SetDirty(true);
        }

        void SerializePlayList() {
            if (frontendHandler == null) return;
            using (var serializedObject = new SerializedObject(frontendHandler)) {
                var temp = new List<PlayListEntry>();
                int offset = 0;
                int count = playLists.Count;
                var playListTitlesProperty = serializedObject.FindProperty("playListTitles");
                var playListUrlOffsetsProperty = serializedObject.FindProperty("playListUrlOffsets");
                var playListUrlsProperty = serializedObject.FindProperty("playListUrls");
                var playListUrlsQuestProperty = serializedObject.FindProperty("playListUrlsQuest");
                var playListEntryTitlesProperty = serializedObject.FindProperty("playListEntryTitles");
                var playListPlayerIndexProperty = serializedObject.FindProperty("playListPlayerIndex");
                var playListLyricsUrlsProperty = serializedObject.FindProperty("playListLyricsUrls");
                var playListBadLyricsUrlsProperty = serializedObject.FindProperty("playListBadLyricsUrls");
                var playListLyricsPoolIndexesProperty = serializedObject.FindProperty("playListLyricsPoolIndexes");
                playListTitlesProperty.arraySize = count;
                playListUrlOffsetsProperty.arraySize = count;
                for (int i = 0; i < count; i++) {
                    PlayList playList = playLists[i];
                    playListTitlesProperty.GetArrayElementAtIndex(i).stringValue = playList.title;
                    playListUrlOffsetsProperty.GetArrayElementAtIndex(i).intValue = offset;
                    temp.AddRange(playList.entries);
                    offset += playList.entries.Count;
                }
                playListUrlsProperty.arraySize = offset;
                playListUrlsQuestProperty.arraySize = offset;
                playListEntryTitlesProperty.arraySize = offset;
                playListPlayerIndexProperty.arraySize = offset;
                playListLyricsUrlsProperty.arraySize = offset;
                playListBadLyricsUrlsProperty.arraySize = offset;
                if (playListLyricsPoolIndexesProperty != null) playListLyricsPoolIndexesProperty.arraySize = offset;
                for (int i = 0; i < offset; i++) {
                    var entry = temp[i];
                    playListUrlsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("url").stringValue = entry.url;
                    playListUrlsQuestProperty.GetArrayElementAtIndex(i).FindPropertyRelative("url").stringValue = entry.urlForQuest;
                    playListEntryTitlesProperty.GetArrayElementAtIndex(i).stringValue = entry.title;
                    playListPlayerIndexProperty.GetArrayElementAtIndex(i).intValue = entry.playerIndex + 1;
                    playListLyricsUrlsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("url").stringValue = entry.lyricsUrl;
                    playListBadLyricsUrlsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("url").stringValue = entry.badLyricsUrl;
                    if (playListLyricsPoolIndexesProperty != null) playListLyricsPoolIndexesProperty.GetArrayElementAtIndex(i).intValue = entry.lyricsPoolIndex;
                }
                SerializeLyricsPools(serializedObject, temp);
                serializedObject.ApplyModifiedProperties();
            }
            SetDirty(false);
            OnFrontendUpdated?.Invoke(frontendHandler);
        }

        void SaveIfRequired() {
            if (isDirty && i18n.DisplayLocalizedDialog2("PlaylistEditor.unsave_confirm"))
                SerializePlayList();
        }

        void AddPlayList(ReorderableList list) {
            playLists.Add(new PlayList {
                title = $"Playlist {playLists.Count + 1}",
                entries = new List<PlayListEntry>(),
            });
            list.index = playLists.Count - 1;
            SetDirty(true);
            PlayListSelected(list);
        }

        void RemovePlayList(ReorderableList list) {
            playLists.RemoveAt(list.index);
            list.index = Mathf.Clamp(playListView.index, 0, playLists.Count - 1);
            SetDirty(true);
            PlayListSelected(list);
        }

        void ReorderPlayList(ReorderableList list) {
            PlayListSelected(list);
            SetDirty(true);
        }

        void AddPlayListEntry(ReorderableList list) {
            int previousCount = selectedPlayList.entries != null ? selectedPlayList.entries.Count : 0;
            ReorderableList.defaultBehaviours.DoAddButton(list);
            if (selectedPlayList.entries != null && selectedPlayList.entries.Count > previousCount) {
                int index = selectedPlayList.entries.Count - 1;
                var entry = selectedPlayList.entries[index];
                entry.lyricsPoolIndex = -1;
                selectedPlayList.entries[index] = entry;
            }
            SetDirty(true);
        }

        void RemovePlayListEntry(ReorderableList list) {
            ReorderableList.defaultBehaviours.DoRemoveButton(list);
            SetDirty(true);
        }

        void ReorderPlayListEntry(ReorderableList list) {
            SetDirty(true);
        }

        void UpdatePlayerHandlerInfos() {
            if (loadedCore == frontendHandler.core) return;
            loadedCore = frontendHandler.core;
            firstUnityPlayerIndex = -1;
            firstAvProPlayerIndex = -1;
            using (var coreSerializedObject = new SerializedObject(frontendHandler.core)) {
                var playerHandlersProperty = coreSerializedObject.FindProperty("playerHandlers");
                var handlersCount = playerHandlersProperty.arraySize;
                if (playerHandlerNames == null || playerHandlerNames.Length != handlersCount)
                    playerHandlerNames = new string[handlersCount];
                if (playerHandlerTypes == null || playerHandlerTypes.Length != handlersCount)
                    playerHandlerTypes = new PlayerType[handlersCount];
                for (int i = 0; i < handlersCount; i++) {
                    var handler = playerHandlersProperty.GetArrayElementAtIndex(i).objectReferenceValue as AbstractMediaPlayerHandler;
                    if (handler == null) {
                        playerHandlerNames[i] = string.Format(i18n.GetOrDefault("PlaylistEditor.playerN"), i + 1);
                        continue;
                    }
                    playerHandlerNames[i] = string.IsNullOrEmpty(handler.playerName) ? handler.name : handler.playerName;
                    playerHandlerTypes[i] = handler.GetPlayerType();
                    switch (playerHandlerTypes[i]) {
                        case PlayerType.Unity:
                            if (firstUnityPlayerIndex < 0) firstUnityPlayerIndex = i;
                            break;
                        case PlayerType.AVPro:
                            if (firstAvProPlayerIndex < 0) firstAvProPlayerIndex = i;
                            break;
                    }
                }
            }
            if (firstUnityPlayerIndex < 0) firstUnityPlayerIndex = 0;
            if (firstAvProPlayerIndex < 0) firstAvProPlayerIndex = 0;
        }

        void SelectYtdlpInstallation() {
            var path = YtdlpResolver.YtdlpPath;
            path = EditorUtility.OpenFilePanel(i18n.GetOrDefault("PlaylistEditor.selectYTDLPInstallation"), Path.GetDirectoryName(path),
#if UNITY_EDITOR_WIN
                "exe"
#else
                ""
#endif
            );
            if (string.IsNullOrEmpty(path)) return;
            YtdlpResolver.YtdlpPath = path;
        }

        #region Playlist Importers
        void HandlePlayListObjectDrop(bool creaeNewPlayList = false) {
            DragAndDrop.AcceptDrag();
            UpdatePlayerHandlerInfos();
            var queue = new Queue<UnityObject>(DragAndDrop.objectReferences);
            while (queue.Count > 0) {
                var obj = queue.Dequeue();
                if (obj is GameObject gameObject) {
                    foreach (var component in gameObject.GetComponents<MonoBehaviour>())
                        queue.Enqueue(component);
                    continue;
                }
                if (obj is MonoBehaviour mb) {
                    var type = obj.GetType();
                    switch ($"{type.Namespace}.{type.Name}") {
                        case "JLChnToZ.VRC.VVMW.FrontendHandler":
                            AppendPlaylist(obj);
                            break;
                        case "UdonSharp.Video.USharpVideoPlayer":
                            ImportPlayListFromUSharpVideo(mb, creaeNewPlayList);
                            break;
                        case "Yamadev.YamaStream.Script.PlayList":
                            ImportPlayListFromYamaPlayer(mb, creaeNewPlayList);
                            break;
                        case "Kinel.VideoPlayer.Scripts.KinelPlaylistGroupManagerScript":
                            ImportPlayListGroupFromKienL(mb, creaeNewPlayList);
                            break;
                        case "Kinel.VideoPlayer.Scripts.KinelPlaylistScript":
                            ImportPlayListFromKinel(mb, creaeNewPlayList);
                            break;
                        case "HoshinoLabs.IwaSync3.Playlist":
                            ImportPlayListFromIwaSync3(mb, creaeNewPlayList);
                            break;
                        case "ArchiTech.Playlist":
                        case "ArchiTech.PlaylistData":
                            ImportPlayListFromProTV(mb, 2, creaeNewPlayList);
                            break;
                        case "ArchiTech.ProTV.Playlist":
                        case "ArchiTech.ProTV.PlaylistData":
                            ImportPlayListFromProTV(mb, 3, creaeNewPlayList);
                            break;
                        case "Texel.PlaylistData":
                            ImportPlayListFromTXL(mb, creaeNewPlayList);
                            break;
                        case "JTPlaylist.Udon.JTPlaylist":
                            ImportPlayListFromJT(mb, creaeNewPlayList);
                            break;
                    }
                }
            }
        }

        PlayList GetOrCreatePlayList(string name, bool forceCreate = false) {
            if (!forceCreate && playLists.Count > 0)
                return playListView.index >= 0 && playListView.index < playLists.Count ?
                    playLists[playListView.index] :
                    playLists[0];
            var playList = new PlayList {
                title = name,
                entries = new List<PlayListEntry>(),
            };
            playLists.Add(playList);
            playListView.index = playLists.Count - 1;
            PlayListSelected(playListView);
            SetDirty(true);
            return playList;
        }

        void ImportPlayListFromUSharpVideo(dynamic usharpVideo, bool newPlayList = false) {
            var playList = GetOrCreatePlayList("Imported Playlist", newPlayList);
            try {
                VRCUrl[] urls = usharpVideo.playlist;
                bool defaultStremMode = usharpVideo.defaultStreamMode;
                var playerIndex = defaultStremMode ? firstAvProPlayerIndex : firstUnityPlayerIndex;
                foreach (var url in urls) {
                    playList.entries.Add(new PlayListEntry {
                        title = string.Empty,
                        url = url?.Get() ?? string.Empty,
                        urlForQuest = string.Empty,
                        lyricsPoolIndex = -1,
                        playerIndex = playerIndex,
                    });
                    SetDirty(true);
                }
            } catch (Exception ex) {
                Debug.LogException(ex);
            }
        }

        void ImportPlayListFromProTV(dynamic proTVPlayList, int version, bool newPlayList = false) {
            var playList = GetOrCreatePlayList("Imported Playlist", newPlayList);
            try {
                VRCUrl[] urls = version >= 3 ? proTVPlayList.mainUrls : proTVPlayList.urls;
                VRCUrl[] alts = version >= 3 ? proTVPlayList.alternateUrls : proTVPlayList.alts;
                string[] titles = proTVPlayList.titles;
                for (int i = 0; i < urls.Length; i++) {
                    var url = urls[i];
                    var alt = alts[i];
                    var title = titles[i];
                    playList.entries.Add(new PlayListEntry {
                        title = title,
                        url = url?.Get() ?? string.Empty,
                        urlForQuest = alt.Get() ?? string.Empty,
                        lyricsPoolIndex = -1,
                        playerIndex = 0,
                    });
                    SetDirty(true);
                }
            } catch (Exception ex) {
                Debug.LogException(ex);
            }
        }

        void ImportPlayListFromIwaSync3(dynamic iwaSync3PlayList, bool newPlayList = false) {
            var playList = GetOrCreatePlayList("Imported Playlist", newPlayList);
            try {
                foreach (var track in (dynamic)((object)iwaSync3PlayList).GetType().GetField("tracks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(iwaSync3PlayList)) {
                    var trackMode = (int)track.mode;
                    var title = track.title;
                    var url = track.url;
                    playList.entries.Add(new PlayListEntry {
                        title = title,
                        url = url,
                        urlForQuest = string.Empty,
                        lyricsPoolIndex = -1,
                        playerIndex = trackMode == 1 ? firstAvProPlayerIndex : firstUnityPlayerIndex,
                    });
                    SetDirty(true);
                }
            } catch (Exception ex) {
                Debug.LogException(ex);
            }
        }

        void ImportPlayListGroupFromKienL(dynamic kinelPlayListGroup, bool newPlayList = false) {
            try {
                string[] playListNames = kinelPlayListGroup.playlists;
                Canvas[] playListCanvases = kinelPlayListGroup.playlistCanvas;
                for (int i = 0; i < playListNames.Length; i++) {
                    var playListName = playListNames[i];
                    var playListCanvas = playListCanvases[i];
                    var playList = GetOrCreatePlayList(playListName, true);
                    try {
                        var kinelPlaylistScript = playListCanvas.GetComponentInParent(Type.GetType("KinelPlaylistScript, Assembly-CSharp"));
                        ImportPlayListFromKinel(kinelPlaylistScript, newPlayList, playListName);
                    } catch (Exception ex) {
                        Debug.LogException(ex);
                    }
                }
            } catch (Exception ex) {
                Debug.LogException(ex);
            }
        }

        void ImportPlayListFromKinel(dynamic kinelPlayList, bool newPlayList = false, string playListName = null) {
            var playList = GetOrCreatePlayList(playListName ?? "Imported Playlist", newPlayList);
            try {
                foreach (var videoData in kinelPlayList.videoDatas) {
                    var trackMode = (int)videoData.mode;
                    var title = videoData.title;
                    var url = videoData.url;
                    playList.entries.Add(new PlayListEntry {
                        title = title,
                        url = url,
                        urlForQuest = string.Empty,
                        lyricsPoolIndex = -1,
                        playerIndex = trackMode == 1 ? firstAvProPlayerIndex : firstUnityPlayerIndex,
                    });
                    SetDirty(true);
                }
            } catch (Exception ex) {
                Debug.LogException(ex);
            }
        }

        void ImportPlayListFromYamaPlayer(dynamic yamaPlayerPlayList, bool newPlayList = false) {
            try {
                var playList = GetOrCreatePlayList(yamaPlayerPlayList.PlayListName ?? "Imported Playlist", newPlayList);
                foreach (var track in yamaPlayerPlayList.Tracks) {
                    var trackMode = (int)track.Mode;
                    var title = track.Title;
                    var url = track.Url;
                    playList.entries.Add(new PlayListEntry {
                        title = title,
                        url = url,
                        urlForQuest = string.Empty,
                        lyricsPoolIndex = -1,
                        playerIndex = trackMode == 1 ? firstAvProPlayerIndex : firstUnityPlayerIndex,
                    });
                    SetDirty(true);
                }
            } catch (Exception ex) {
                Debug.LogException(ex);
            }
        }

        void ImportPlayListFromJT(dynamic jtPlaylist, bool newPlayList = false) {
            var playList = GetOrCreatePlayList("Imported Playlist", newPlayList);
            try {
                string[] titles = jtPlaylist.titles;
                VRCUrl[] urls = jtPlaylist.urls;
                bool[] isLives = jtPlaylist.isLive;
                for (int i = 0; i < urls.Length; i++) {
                    var url = urls[i];
                    var title = titles[i];
                    var isLive = isLives[i];
                    playList.entries.Add(new PlayListEntry {
                        title = title,
                        url = url?.Get() ?? string.Empty,
                        urlForQuest = string.Empty,
                        lyricsPoolIndex = -1,
                        playerIndex = isLive ? firstAvProPlayerIndex : firstUnityPlayerIndex,
                    });
                    SetDirty(true);
                }
            } catch (Exception ex) {
                Debug.LogException(ex);
            }
        }

        void ImportPlayListFromTXL(dynamic txlPlaylist, bool newPlaylist = false) {
            try {
                var playList = GetOrCreatePlayList(txlPlaylist.playlistName ?? "Imported Playlist", newPlaylist);
                VRCUrl[] playlist = txlPlaylist.playlist;
                VRCUrl[] questPlaylist = txlPlaylist.questPlaylist ?? playlist;
                string[] trackNames = txlPlaylist.trackNames;
                for (int i = 0; i < playlist.Length; i++) {
                    var url = playlist[i];
                    var questUrl = questPlaylist[i];
                    var title = trackNames[i];
                    playList.entries.Add(new PlayListEntry {
                        title = title,
                        url = url?.Get() ?? string.Empty,
                        urlForQuest = questUrl?.Get() ?? string.Empty,
                        lyricsPoolIndex = -1,
                        playerIndex = 0,
                    });
                    SetDirty(true);
                }
            } catch (Exception ex) {
                Debug.LogException(ex);
            }
        }
        #endregion

        #region Playlist Exporters
        void ExportPlayListToJson(bool saveAll) {
            var path = EditorUtility.SaveFilePanel(i18n.GetOrDefault("PlaylistEditor.exportTitle"), Application.dataPath, "playList.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            var jsonWriter = new JsonWriter {
                PrettyPrint = true,
                IndentValue = 2
            };
            if (saveAll) {
                jsonWriter.WriteArrayStart();
                foreach (var playList in playLists)
                    WriteEntries(jsonWriter, playList);
                jsonWriter.WriteArrayEnd();
            } else {
                WriteEntries(jsonWriter, selectedPlayList);
            }
            File.WriteAllText(path, jsonWriter.ToString());
        }

        void WriteEntries(JsonWriter jsonWriter, PlayList playList) {
            jsonWriter.WriteObjectStart();
            jsonWriter.WritePropertyName("title");
            jsonWriter.Write(playList.title);
            jsonWriter.WritePropertyName("entries");
            jsonWriter.WriteArrayStart();
            foreach (var entry in playList.entries)
                WriteEntry(jsonWriter, entry);
            jsonWriter.WriteArrayEnd();
            jsonWriter.WriteObjectEnd();
        }

        void WriteEntry(JsonWriter jsonWriter, PlayListEntry entry) {
            jsonWriter.WriteObjectStart();
            jsonWriter.WritePropertyName("title");
            jsonWriter.Write(entry.title);
            jsonWriter.WritePropertyName("url");
            jsonWriter.Write(entry.url);
            jsonWriter.WritePropertyName("urlForQuest");
            jsonWriter.Write(entry.urlForQuest);
            jsonWriter.WritePropertyName("playerIndex");
            jsonWriter.Write(entry.playerIndex);
            if (!string.IsNullOrEmpty(entry.lyricsUrl)) {
                jsonWriter.WritePropertyName("lyricsUrl");
                jsonWriter.Write(entry.lyricsUrl);
            }
            if (!string.IsNullOrEmpty(entry.badLyricsUrl)) {
                jsonWriter.WritePropertyName("badLyricsUrl");
                jsonWriter.Write(entry.badLyricsUrl);
            }
            if (entry.lyricsPoolIndex >= 0) {
                jsonWriter.WritePropertyName("lyricsPoolIndex");
                jsonWriter.Write(entry.lyricsPoolIndex);
            }
            jsonWriter.WriteObjectEnd();
        }

        void ImportPlayListFromJson() {
            var path = EditorUtility.OpenFilePanel(i18n.GetOrDefault("PlaylistEditor.importTitle"), Application.dataPath, "json");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var jsonData = JsonMapper.ToObject(File.ReadAllText(path));
            switch (jsonData.GetJsonType()) {
                case JsonType.Array:
                    if (playLists.Count > 0 && !i18n.DisplayLocalizedDialog2("PlaylistEditor.import_array")) {
                        playLists.Clear();
                        SetDirty(true);
                    }
                    LoadPlayLists(jsonData);
                    break;
                case JsonType.Object:
                    if (selectedPlayList.entries != null)
                        switch (i18n.DisplayLocalizedDialog3("PlaylistEditor.import_object")) {
                            case 1: selectedPlayList.entries.Clear(); SetDirty(true); break;
                            case 2: selectedPlayList = GetOrCreatePlayList(jsonData["title"].ToString(), true); break;
                        }
                    else
                        selectedPlayList = GetOrCreatePlayList(jsonData["title"].ToString(), true);
                    LoadEntries(jsonData["entries"]);
                    break;
            }
        }

        void LoadPlayLists(JsonData playListLists) {
            if (playListLists == null || playListLists.GetJsonType() != JsonType.Array) return;
            foreach (JsonData playList in playListLists)
                try {
                    GetOrCreatePlayList(playList["title"].ToString(), true);
                    LoadEntries(playList["entries"]);
                } catch (Exception ex) {
                    Debug.LogException(ex);
                }
        }

        void LoadEntries(JsonData entries) {
            if (entries == null || entries.GetJsonType() != JsonType.Array) return;
            foreach (JsonData entry in entries) {
                try {
                    var title = entry["title"].ToString();
                    var url = entry["url"].ToString();
                    var urlForQuest = entry["urlForQuest"]?.ToString();
                    var lyricsUrl = GetOptionalString(entry, "lyricsUrl");
                    var badLyricsUrl = GetOptionalString(entry, "badLyricsUrl");
                    var lyricsPoolIndex = GetOptionalInt(entry, "lyricsPoolIndex", -1);
                    var playerIndex = (int)entry["playerIndex"];
                    selectedPlayList.entries.Add(new PlayListEntry {
                        title = title,
                        url = url,
                        urlForQuest = urlForQuest,
                        lyricsUrl = lyricsUrl,
                        badLyricsUrl = badLyricsUrl,
                        lyricsPoolIndex = lyricsPoolIndex,
                        playerIndex = playerIndex,
                    });
                    SetDirty(true);
                } catch (Exception ex) {
                    Debug.LogException(ex);
                }
            }
        }
        #endregion

        async UniTask FetchPlayList(string url) {
            var ytPlaylist = await YtdlpResolver.GetPlayLists(url);
            var playList = GetOrCreatePlayList("Imported Playlist");
            foreach (var entry in ytPlaylist.Where(e => e.title != "[Deleted video]" && e.title != "[Private video]"))
                playList.entries.Add(new PlayListEntry {
                    title = entry.title,
                    url = entry.url,
                    urlForQuest = string.Empty,
                    lyricsPoolIndex = -1,
                    playerIndex = 0,
                });
            SetDirty(true);
        }

        async UniTask FetchTitles() {
            if (selectedPlayList.entries == null || selectedPlayList.entries.Count == 0) return;
            var entries = new YtdlpPlayListEntry[selectedPlayList.entries.Count];
            for (int i = 0; i < entries.Length; i++)
                entries[i] = new YtdlpPlayListEntry {
                    title = selectedPlayList.entries[i].title,
                    url = selectedPlayList.entries[i].url,
                };
            await YtdlpResolver.FetchTitles(entries);
            for (int i = 0; i < entries.Length; i++) {
                var entry = selectedPlayList.entries[i];
                entry.title = entries[i].title;
                selectedPlayList.entries[i] = entry;
            }
            SetDirty(true);
        }

        void ReversePlaylist() {
            if (selectedPlayList.entries == null || selectedPlayList.entries.Count == 0) return;
            selectedPlayList.entries.Reverse();
            SetDirty(true);
        }

        void GenerateLyricsUrls() {
            if (selectedPlayList.entries == null || selectedPlayList.entries.Count == 0) return;
            var endpoint = GetLyricsEndpoint();
            if (string.IsNullOrEmpty(endpoint)) return;
            for (int i = 0; i < selectedPlayList.entries.Count; i++) {
                var entry = selectedPlayList.entries[i];
                var title = Uri.EscapeDataString((entry.title ?? "").Trim());
                var mediaUrl = Uri.EscapeDataString((entry.url ?? "").Trim());
                entry.lyricsUrl = $"{endpoint}/v1/lyrics?title={title}&url={mediaUrl}";
                entry.badLyricsUrl = $"{endpoint}/v1/report?title={title}&url={mediaUrl}";
                entry.lyricsPoolIndex = -1;
                selectedPlayList.entries[i] = entry;
            }
            SetDirty(true);
        }

        void GenerateLyricsPool() {
            if (playLists.Count == 0) return;
            lyricsEndpoint = GetLyricsEndpoint();
            lyricsPoolId = NormalizeLyricsPoolId(string.IsNullOrWhiteSpace(lyricsPoolId) ? GetDefaultLyricsPoolId() : lyricsPoolId);
            int slot = 0;
            for (int i = 0; i < playLists.Count; i++) {
                var playList = playLists[i];
                if (playList.entries == null) continue;
                for (int j = 0; j < playList.entries.Count; j++) {
                    var entry = playList.entries[j];
                    entry.lyricsPoolIndex = slot++;
                    entry.lyricsUrl = string.Empty;
                    entry.badLyricsUrl = string.Empty;
                    playList.entries[j] = entry;
                }
                playLists[i] = playList;
            }
            SerializePlayList();
        }

        void ExportLyricsManifest() {
            if (playLists.Count == 0) return;
            lyricsPoolId = NormalizeLyricsPoolId(string.IsNullOrWhiteSpace(lyricsPoolId) ? GetDefaultLyricsPoolId() : lyricsPoolId);
            if (!HasLyricsPoolIndexes()) GenerateLyricsPool();
            var path = EditorUtility.SaveFilePanel("Export Lyrics Manifest", Application.dataPath, $"{lyricsPoolId}.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            var jsonWriter = new JsonWriter {
                PrettyPrint = true,
                IndentValue = 2
            };
            jsonWriter.WriteObjectStart();
            jsonWriter.WritePropertyName("poolId");
            jsonWriter.Write(lyricsPoolId);
            jsonWriter.WritePropertyName("slots");
            jsonWriter.WriteArrayStart();
            var written = new HashSet<int>();
            for (int i = 0; i < playLists.Count; i++) {
                var playList = playLists[i];
                if (playList.entries == null) continue;
                for (int j = 0; j < playList.entries.Count; j++) {
                    var entry = playList.entries[j];
                    if (entry.lyricsPoolIndex < 0 || !written.Add(entry.lyricsPoolIndex)) continue;
                    WriteLyricsManifestSlot(jsonWriter, playList.title, i, j, entry);
                }
            }
            jsonWriter.WriteArrayEnd();
            jsonWriter.WriteObjectEnd();
            File.WriteAllText(path, jsonWriter.ToString());
        }

        void DetectNomSeekLyricsPool() {
            if (TryDetectNomSeekPool(out string poolId, out int poolSize)) {
                dynamicLyricsPoolId = poolId;
                dynamicLyricsPoolSize = Mathf.Max(1, poolSize);
            } else {
                EditorUtility.DisplayDialog(
                    "NomSeek Pool",
                    $"Could not detect a NomSeek VRCURLSetter at {NomSeekVrcUrlSetterPath}.",
                    "OK"
                );
            }
        }

        void GenerateDynamicLyricsPool() {
            if (frontendHandler == null) return;
            var poolId = (dynamicLyricsPoolId ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrEmpty(poolId)) return;
            dynamicLyricsPoolId = poolId;
            dynamicLyricsPoolSize = Mathf.Max(1, dynamicLyricsPoolSize);
            int arraySize = dynamicLyricsPoolSize + 1;
            var endpoint = GetLyricsEndpoint();
            using (var serializedObject = new SerializedObject(frontendHandler)) {
                var usePoolProperty = serializedObject.FindProperty("useDynamicLyricsUrlPool");
                var mediaPrefixProperty = serializedObject.FindProperty("dynamicLyricsMediaUrlPrefix");
                var requestPoolProperty = serializedObject.FindProperty("dynamicLyricsRequestUrlPool");
                var reportPoolProperty = serializedObject.FindProperty("dynamicLyricsReportUrlPool");
                if (requestPoolProperty == null || reportPoolProperty == null || mediaPrefixProperty == null) return;
                if (usePoolProperty != null) usePoolProperty.boolValue = true;
                mediaPrefixProperty.stringValue = $"{NomSeekVrcUrlBase}/{poolId}/";
                requestPoolProperty.arraySize = arraySize;
                reportPoolProperty.arraySize = arraySize;
                for (int i = 0; i < arraySize; i++) {
                    requestPoolProperty.GetArrayElementAtIndex(i).FindPropertyRelative("url").stringValue = $"{endpoint}/nomseek/{poolId}/{i}/lyrics";
                    reportPoolProperty.GetArrayElementAtIndex(i).FindPropertyRelative("url").stringValue = $"{endpoint}/nomseek/{poolId}/{i}/report";
                }
                serializedObject.ApplyModifiedProperties();
            }
            EditorUtility.SetDirty(frontendHandler);
            OnFrontendUpdated?.Invoke(frontendHandler);
        }

        void WriteLyricsManifestSlot(JsonWriter jsonWriter, string playListTitle, int playListIndex, int entryIndex, PlayListEntry entry) {
            jsonWriter.WriteObjectStart();
            jsonWriter.WritePropertyName("slot");
            jsonWriter.Write(entry.lyricsPoolIndex);
            jsonWriter.WritePropertyName("key");
            jsonWriter.Write($"{lyricsPoolId}:{entry.lyricsPoolIndex}");
            jsonWriter.WritePropertyName("title");
            jsonWriter.Write((entry.title ?? string.Empty).Trim());
            jsonWriter.WritePropertyName("media_url");
            jsonWriter.Write((entry.url ?? string.Empty).Trim());
            jsonWriter.WritePropertyName("playlist");
            jsonWriter.Write(playListTitle ?? string.Empty);
            jsonWriter.WritePropertyName("playlist_index");
            jsonWriter.Write(playListIndex);
            jsonWriter.WritePropertyName("entry_index");
            jsonWriter.Write(entryIndex);
            jsonWriter.WritePropertyName("player_index");
            jsonWriter.Write(entry.playerIndex);
            jsonWriter.WriteObjectEnd();
        }

        void SerializeLyricsPools(SerializedObject serializedObject, List<PlayListEntry> entries) {
            var requestPoolProperty = serializedObject.FindProperty("lyricsRequestUrlPool");
            var reportPoolProperty = serializedObject.FindProperty("lyricsReportUrlPool");
            var usePoolProperty = serializedObject.FindProperty("useLyricsUrlPool");
            if (requestPoolProperty == null || reportPoolProperty == null) return;

            int maxIndex = -1;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].lyricsPoolIndex > maxIndex) maxIndex = entries[i].lyricsPoolIndex;
            if (usePoolProperty != null) usePoolProperty.boolValue = maxIndex >= 0;
            if (maxIndex < 0) {
                requestPoolProperty.arraySize = 0;
                reportPoolProperty.arraySize = 0;
                return;
            }

            var endpoint = GetLyricsEndpoint();
            var poolId = NormalizeLyricsPoolId(string.IsNullOrWhiteSpace(lyricsPoolId) ? GetDefaultLyricsPoolId() : lyricsPoolId);
            lyricsPoolId = poolId;
            requestPoolProperty.arraySize = maxIndex + 1;
            reportPoolProperty.arraySize = maxIndex + 1;
            for (int i = 0; i <= maxIndex; i++) {
                requestPoolProperty.GetArrayElementAtIndex(i).FindPropertyRelative("url").stringValue = string.Empty;
                reportPoolProperty.GetArrayElementAtIndex(i).FindPropertyRelative("url").stringValue = string.Empty;
            }
            for (int i = 0; i < entries.Count; i++) {
                int index = entries[i].lyricsPoolIndex;
                if (index < 0) continue;
                requestPoolProperty.GetArrayElementAtIndex(index).FindPropertyRelative("url").stringValue = $"{endpoint}/pool/{poolId}/{index}/lyrics";
                reportPoolProperty.GetArrayElementAtIndex(index).FindPropertyRelative("url").stringValue = $"{endpoint}/pool/{poolId}/{index}/report";
            }
        }

        bool HasLyricsPoolIndexes() {
            for (int i = 0; i < playLists.Count; i++) {
                var playList = playLists[i];
                if (playList.entries == null) continue;
                for (int j = 0; j < playList.entries.Count; j++)
                    if (playList.entries[j].lyricsPoolIndex >= 0) return true;
            }
            return false;
        }

        string GetLyricsEndpoint() {
            var endpoint = string.IsNullOrWhiteSpace(lyricsEndpoint) ? DefaultLyricsEndpoint : lyricsEndpoint;
            return endpoint.Trim().TrimEnd('/');
        }

        string GetDefaultLyricsPoolId() => GetDefaultLyricsPoolId(frontendHandler);

        static string GetDefaultLyricsPoolId(FrontendHandler handler) {
            var scenePath = handler != null ? handler.gameObject.scene.path : string.Empty;
            var sceneName = string.IsNullOrEmpty(scenePath) ? "vvmw" : Path.GetFileNameWithoutExtension(scenePath);
            return NormalizeLyricsPoolId(sceneName);
        }

        static string NormalizeLyricsPoolId(string value) {
            value = (value ?? string.Empty).Trim().ToLowerInvariant();
            var chars = value.ToCharArray();
            int writeIndex = 0;
            bool previousDash = false;
            for (int i = 0; i < chars.Length; i++) {
                char c = chars[i];
                bool valid = c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '.';
                if (valid) {
                    chars[writeIndex++] = c;
                    previousDash = false;
                } else if (!previousDash && writeIndex > 0) {
                    chars[writeIndex++] = '-';
                    previousDash = true;
                }
            }
            while (writeIndex > 0 && chars[writeIndex - 1] == '-') writeIndex--;
            return writeIndex > 0 ? new string(chars, 0, writeIndex) : "vvmw";
        }

        static bool TryDetectNomSeekPool(out string poolId, out int poolSize) {
            poolId = string.Empty;
            poolSize = 0;
            var path = Path.Combine(Directory.GetCurrentDirectory(), NomSeekVrcUrlSetterPath);
            if (!File.Exists(path)) return false;
            const string prefix = "  - url: https://api.u2b.cx/vrcurl/";
            int maxIndex = -1;
            foreach (var line in File.ReadLines(path)) {
                if (!line.StartsWith(prefix)) continue;
                var value = line.Substring(prefix.Length).Trim();
                int slashIndex = value.LastIndexOf('/');
                if (slashIndex <= 0 || slashIndex >= value.Length - 1) continue;
                var currentPoolId = value.Substring(0, slashIndex);
                if (!int.TryParse(value.Substring(slashIndex + 1), out int index)) continue;
                if (string.IsNullOrEmpty(poolId)) poolId = currentPoolId;
                if (currentPoolId == poolId && index > maxIndex) maxIndex = index;
            }
            if (string.IsNullOrEmpty(poolId) || maxIndex < 0) return false;
            poolSize = maxIndex;
            return true;
        }

        [Serializable]
        struct PlayList {
            public string title;
            public List<PlayListEntry> entries;
        }

        [Serializable]
        struct PlayListEntry {
            public string title;
            public string url;
            public string urlForQuest;
            public string lyricsUrl;
            public string badLyricsUrl;
            public int lyricsPoolIndex;
            public int playerIndex;
        }

        static string GetUrlAt(SerializedProperty property, int index) {
            if (property == null || index < 0 || index >= property.arraySize) return string.Empty;
            return property.GetArrayElementAtIndex(index).FindPropertyRelative("url").stringValue;
        }

        static int GetIntAt(SerializedProperty property, int index, int defaultValue) {
            if (property == null || index < 0 || index >= property.arraySize) return defaultValue;
            return property.GetArrayElementAtIndex(index).intValue;
        }

        static string GetOptionalString(JsonData data, string key) {
            if (data == null || data.GetJsonType() != JsonType.Object || !data.Keys.Contains(key)) return string.Empty;
            var value = data[key];
            return value == null ? string.Empty : value.ToString();
        }

        static int GetOptionalInt(JsonData data, string key, int defaultValue) {
            if (data == null || data.GetJsonType() != JsonType.Object || !data.Keys.Contains(key)) return defaultValue;
            var value = data[key];
            if (value == null) return defaultValue;
            try {
                return (int)value;
            } catch {
                if (int.TryParse(value.ToString(), out int result)) return result;
                return defaultValue;
            }
        }
    }
}
