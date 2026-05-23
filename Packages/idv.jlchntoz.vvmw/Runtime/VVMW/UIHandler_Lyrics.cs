using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using JLChnToZ.VRC.Foundation;
using JLChnToZ.VRC.Foundation.I18N;

namespace JLChnToZ.VRC.VVMW {
    public partial class UIHandler {
        [LocalizedHeader("HEADER:Lyrics")]
        [SerializeField, LocalizedLabel] GameObject lyricsPanelRoot;
        [SerializeField, LocalizedLabel] GameObject lyricsSyncedRoot;
        [SerializeField, LocalizedLabel] GameObject lyricsPlainRoot;
        [SerializeField, LocalizedLabel] GameObject lyricsStatusRoot;
        [SerializeField, LocalizedLabel] GameObject lyricsReportButtonObject;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsSyncedRoot), NullOnly = false)] Text lyricsPreviousText;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsSyncedRoot), NullOnly = false)] TextMeshProUGUI lyricsPreviousTMPro;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsSyncedRoot), NullOnly = false)] Text lyricsCurrentText;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsSyncedRoot), NullOnly = false)] TextMeshProUGUI lyricsCurrentTMPro;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsSyncedRoot), NullOnly = false)] Text lyricsNextText;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsSyncedRoot), NullOnly = false)] TextMeshProUGUI lyricsNextTMPro;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsPlainRoot), NullOnly = false)] Text lyricsPlainText;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsPlainRoot), NullOnly = false)] TextMeshProUGUI lyricsPlainTMPro;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsStatusRoot), NullOnly = false)] Text lyricsStatusText;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsStatusRoot), NullOnly = false)] TextMeshProUGUI lyricsStatusTMPro;
        [BindEvent(nameof(Button.onClick), nameof(_ReportBadLyrics))]
        [SerializeField, LocalizedLabel] Button lyricsReportButton;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsReportButtonObject), NullOnly = false)] Text lyricsReportButtonText;
        [SerializeField, HideInInspector, Resolve(nameof(lyricsReportButtonObject), NullOnly = false)] TextMeshProUGUI lyricsReportButtonTMPro;

        void InitLyricsPanel() => _OnLyricsData();

#if COMPILER_UDONSHARP
        public
#endif
        void _ReportBadLyrics() {
            if (Utilities.IsValid(core)) core._ReportBadLyrics();
        }

#if COMPILER_UDONSHARP
        public
#endif
        void _OnLyricsData() {
            if (!afterFirstRun || !Utilities.IsValid(core) || !Utilities.IsValid(lyricsPanelRoot)) return;
            var state = core.LyricsState;
            bool showSynced = state == Core.LYRICS_SYNCED;
            bool showPlain = state == Core.LYRICS_PLAIN;
            bool showStatus = state == Core.LYRICS_LOADING ||
                state == Core.LYRICS_INSTRUMENTAL ||
                state == Core.LYRICS_NOT_FOUND ||
                state == Core.LYRICS_ERROR ||
                state == Core.LYRICS_REPORTED;
            lyricsPanelRoot.SetActive(state != Core.LYRICS_NONE);
            if (Utilities.IsValid(lyricsSyncedRoot)) lyricsSyncedRoot.SetActive(showSynced);
            if (Utilities.IsValid(lyricsPlainRoot)) lyricsPlainRoot.SetActive(showPlain);
            if (Utilities.IsValid(lyricsStatusRoot)) lyricsStatusRoot.SetActive(showStatus);
            if (Utilities.IsValid(lyricsReportButtonObject)) lyricsReportButtonObject.SetActive(core.CanReportLyrics);
            if (Utilities.IsValid(lyricsReportButton)) lyricsReportButton.interactable = core.CanReportLyrics;
            SetLocalizedText(lyricsReportButtonText, lyricsReportButtonTMPro, "LyricsReportButton");

            switch (state) {
                case Core.LYRICS_SYNCED:
                    _OnLyricsLineChange();
                    break;
                case Core.LYRICS_PLAIN:
                    SetText(lyricsPlainText, lyricsPlainTMPro, core.LyricsPlainText);
                    break;
                case Core.LYRICS_LOADING:
                    SetLocalizedText(lyricsStatusText, lyricsStatusTMPro, "LyricsLoading");
                    break;
                case Core.LYRICS_INSTRUMENTAL:
                    SetLocalizedText(lyricsStatusText, lyricsStatusTMPro, "LyricsInstrumental");
                    break;
                case Core.LYRICS_NOT_FOUND:
                    SetLocalizedText(lyricsStatusText, lyricsStatusTMPro, "LyricsNotFound");
                    break;
                case Core.LYRICS_REPORTED:
                    SetLocalizedText(lyricsStatusText, lyricsStatusTMPro, "LyricsReported");
                    break;
                case Core.LYRICS_ERROR:
                    if (!string.IsNullOrEmpty(core.LyricsStatus))
                        SetText(lyricsStatusText, lyricsStatusTMPro, core.LyricsStatus);
                    else
                        SetLocalizedText(lyricsStatusText, lyricsStatusTMPro, "LyricsError");
                    break;
            }
        }

#if COMPILER_UDONSHARP
        public
#endif
        void _OnLyricsLineChange() {
            if (!afterFirstRun || !Utilities.IsValid(core) || core.LyricsState != Core.LYRICS_SYNCED) return;
            SetText(lyricsPreviousText, lyricsPreviousTMPro, core.LyricsPreviousLine);
            SetText(lyricsCurrentText, lyricsCurrentTMPro, core.LyricsCurrentLine);
            SetText(lyricsNextText, lyricsNextTMPro, core.LyricsNextLine);
        }
    }
}
