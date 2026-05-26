using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using JLChnToZ.VRC.Foundation.I18N;

namespace JLChnToZ.VRC.VVMW {
    public partial class FrontendHandler {
        [SerializeField, LocalizedLabel] VRCUrl[] playListLyricsUrls;
        [SerializeField, LocalizedLabel] VRCUrl[] playListBadLyricsUrls;
        [SerializeField] bool useLyricsUrlPool = true;
        [SerializeField] VRCUrl[] lyricsRequestUrlPool;
        [SerializeField] VRCUrl[] lyricsReportUrlPool;
        [SerializeField] int[] playListLyricsPoolIndexes;
        [SerializeField] bool useDynamicLyricsUrlPool = true;
        [SerializeField] string dynamicLyricsMediaUrlPrefix;
        [SerializeField] VRCUrl[] dynamicLyricsRequestUrlPool;
        [SerializeField] VRCUrl[] dynamicLyricsReportUrlPool;
        [UdonSynced] int syncedDynamicLyricsPoolIndex = -1;
        int localDynamicLyricsPoolIndex = -1;

        /// <summary>
        /// The lyrics request URLs of the playlist entries.
        /// </summary>
        public VRCUrl[] PlayListLyricsUrls => playListLyricsUrls;

        /// <summary>
        /// The bad-lyrics report URLs of the playlist entries.
        /// </summary>
        public VRCUrl[] PlayListBadLyricsUrls => playListBadLyricsUrls;
        public VRCUrl[] LyricsRequestUrlPool => lyricsRequestUrlPool;
        public VRCUrl[] LyricsReportUrlPool => lyricsReportUrlPool;
        public int[] PlayListLyricsPoolIndexes => playListLyricsPoolIndexes;
        public string DynamicLyricsMediaUrlPrefix => dynamicLyricsMediaUrlPrefix;
        public VRCUrl[] DynamicLyricsRequestUrlPool => dynamicLyricsRequestUrlPool;
        public VRCUrl[] DynamicLyricsReportUrlPool => dynamicLyricsReportUrlPool;

        void SetCoreLyricsSource(int entryIndex, bool loadNow) {
            localDynamicLyricsPoolIndex = -1;
            var lyricsUrl = VRCUrl.Empty;
            var badLyricsUrl = VRCUrl.Empty;
            if (useLyricsUrlPool) {
                int poolIndex = GetLyricsPoolIndex(entryIndex);
                lyricsUrl = GetLyricsUrl(lyricsRequestUrlPool, poolIndex);
                badLyricsUrl = GetLyricsUrl(lyricsReportUrlPool, poolIndex);
            }
            if (VRCUrl.IsNullOrEmpty(lyricsUrl)) {
                lyricsUrl = GetLyricsUrl(playListLyricsUrls, entryIndex);
                badLyricsUrl = GetLyricsUrl(playListBadLyricsUrls, entryIndex);
            }
            if (loadNow)
                core.SetLyricsSourceAndLoad(lyricsUrl, badLyricsUrl);
            else
                core.SetLyricsSource(lyricsUrl, badLyricsUrl);
        }

        void ClearCoreLyricsSource() {
            localDynamicLyricsPoolIndex = -1;
            if (Utilities.IsValid(core)) core._ClearLyricsSource();
        }

        void SetCoreDynamicLyricsSource(VRCUrl pcUrl, VRCUrl questUrl, bool loadNow) {
            localDynamicLyricsPoolIndex = GetDynamicLyricsPoolIndex(pcUrl);
            if (localDynamicLyricsPoolIndex < 0 && !VRCUrl.IsNullOrEmpty(questUrl))
                localDynamicLyricsPoolIndex = GetDynamicLyricsPoolIndex(questUrl);
            SetCoreDynamicLyricsSource(localDynamicLyricsPoolIndex, loadNow);
        }

        void SetCoreDynamicLyricsSource(int poolIndex, bool loadNow) {
            localDynamicLyricsPoolIndex = poolIndex;
            if (poolIndex < 0) {
                if (Utilities.IsValid(core)) core._ClearLyricsSource();
                return;
            }
            var lyricsUrl = GetLyricsUrl(dynamicLyricsRequestUrlPool, poolIndex);
            var badLyricsUrl = GetLyricsUrl(dynamicLyricsReportUrlPool, poolIndex);
            if (loadNow)
                core.SetLyricsSourceAndLoad(lyricsUrl, badLyricsUrl);
            else
                core.SetLyricsSource(lyricsUrl, badLyricsUrl);
        }

        void PrepareLyricsSync() {
            syncedDynamicLyricsPoolIndex = localDynamicLyricsPoolIndex;
        }

        void ApplySyncedDynamicLyricsSource(bool loadNow) {
            SetCoreDynamicLyricsSource(syncedDynamicLyricsPoolIndex, loadNow);
        }

        VRCUrl GetLyricsUrl(VRCUrl[] urls, int index) {
            if (!Utilities.IsValid(urls) || index < 0 || index >= urls.Length) return VRCUrl.Empty;
            return urls[index] ?? VRCUrl.Empty;
        }

        int GetLyricsPoolIndex(int entryIndex) {
            if (!Utilities.IsValid(playListLyricsPoolIndexes) || entryIndex < 0 || entryIndex >= playListLyricsPoolIndexes.Length) return -1;
            return playListLyricsPoolIndexes[entryIndex];
        }

        int GetDynamicLyricsPoolIndex(VRCUrl mediaUrl) {
            if (!useDynamicLyricsUrlPool || VRCUrl.IsNullOrEmpty(mediaUrl) || string.IsNullOrEmpty(dynamicLyricsMediaUrlPrefix))
                return -1;
            var url = mediaUrl.Get();
            if (string.IsNullOrEmpty(url) || !url.StartsWith(dynamicLyricsMediaUrlPrefix)) return -1;
            int start = dynamicLyricsMediaUrlPrefix.Length;
            if (start >= url.Length) return -1;
            int end = start;
            while (end < url.Length) {
                char c = url[end];
                if (c < '0' || c > '9') break;
                end++;
            }
            if (end <= start) return -1;
            if (!int.TryParse(url.Substring(start, end - start), out int poolIndex)) return -1;
            return poolIndex;
        }
    }
}
