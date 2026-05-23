using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using JLChnToZ.VRC.Foundation.I18N;

namespace JLChnToZ.VRC.VVMW {
    public partial class FrontendHandler {
        [SerializeField, LocalizedLabel] VRCUrl[] playListLyricsUrls;
        [SerializeField, LocalizedLabel] VRCUrl[] playListBadLyricsUrls;

        /// <summary>
        /// The lyrics request URLs of the playlist entries.
        /// </summary>
        public VRCUrl[] PlayListLyricsUrls => playListLyricsUrls;

        /// <summary>
        /// The bad-lyrics report URLs of the playlist entries.
        /// </summary>
        public VRCUrl[] PlayListBadLyricsUrls => playListBadLyricsUrls;

        void SetCoreLyricsSource(int entryIndex, bool loadNow) {
            var lyricsUrl = GetLyricsUrl(playListLyricsUrls, entryIndex);
            var badLyricsUrl = GetLyricsUrl(playListBadLyricsUrls, entryIndex);
            if (loadNow)
                core.SetLyricsSourceAndLoad(lyricsUrl, badLyricsUrl);
            else
                core.SetLyricsSource(lyricsUrl, badLyricsUrl);
        }

        void ClearCoreLyricsSource() {
            if (Utilities.IsValid(core)) core._ClearLyricsSource();
        }

        VRCUrl GetLyricsUrl(VRCUrl[] urls, int index) {
            if (!Utilities.IsValid(urls) || index < 0 || index >= urls.Length) return VRCUrl.Empty;
            return urls[index] ?? VRCUrl.Empty;
        }
    }
}
