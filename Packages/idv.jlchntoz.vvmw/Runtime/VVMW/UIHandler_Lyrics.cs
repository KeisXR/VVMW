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

        public void _ReportBadLyrics() {
            if (Utilities.IsValid(core)) core._ReportBadLyrics();
        }

#if COMPILER_UDONSHARP
        public
#endif
        void _OnLyricsData() {
            if (!afterFirstRun || !Utilities.IsValid(core) || !Utilities.IsValid(lyricsPanelRoot)) return;
            var state = core.LyricsState;
            bool showSynced = state == Core.LYRICS_SYNCED;
            lyricsPanelRoot.SetActive(true);
            if (Utilities.IsValid(lyricsSyncedRoot)) lyricsSyncedRoot.SetActive(showSynced);
            if (Utilities.IsValid(lyricsPlainRoot)) lyricsPlainRoot.SetActive(false);
            if (Utilities.IsValid(lyricsStatusRoot)) lyricsStatusRoot.SetActive(true);
            if (Utilities.IsValid(lyricsReportButtonObject)) lyricsReportButtonObject.SetActive(core.CanReportLyrics);
            if (Utilities.IsValid(lyricsReportButton)) lyricsReportButton.interactable = core.CanReportLyrics;
            SetLocalizedText(lyricsReportButtonText, lyricsReportButtonTMPro, "LyricsReportButton");

            switch (state) {
                case Core.LYRICS_SYNCED:
                    _OnLyricsLineChange();
                    UpdateLyricsPlaybackStatus();
                    break;
                case Core.LYRICS_PLAIN:
                    SetLocalizedText(lyricsStatusText, lyricsStatusTMPro, "LyricsPlainUnavailable");
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
                default:
                    SetLocalizedText(lyricsStatusText, lyricsStatusTMPro, core.State > 0 && !core.HasLyricsSource ? "LyricsNoSource" : "Ready");
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

        void UpdateLyricsPlaybackStatus() {
            if (!afterFirstRun || !Utilities.IsValid(core) || core.LyricsState != Core.LYRICS_SYNCED) return;
            SetLocalizedText(lyricsStatusText, lyricsStatusTMPro, core.IsPlaying ? "LyricsPlaying" : "Ready");
        }
    }
}
