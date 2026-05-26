using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using JLChnToZ.VRC.Foundation.I18N;

namespace JLChnToZ.VRC.VVMW {
    public partial class Core {
        public const byte LYRICS_NONE = 0;
        public const byte LYRICS_LOADING = 1;
        public const byte LYRICS_SYNCED = 2;
        public const byte LYRICS_PLAIN = 3;
        public const byte LYRICS_INSTRUMENTAL = 4;
        public const byte LYRICS_NOT_FOUND = 5;
        public const byte LYRICS_ERROR = 6;
        public const byte LYRICS_REPORTED = 7;

        [SerializeField, LocalizedLabel] bool enableLyrics = true;
        [SerializeField, LocalizedLabel, Range(-10F, 10F)] float lyricsOffset = 0;
        [SerializeField, LocalizedLabel, Range(1, 2048)] int maxSyncedLyricsLines = 512;
        [SerializeField, LocalizedLabel, Range(0.05F, 1F)] float lyricsUpdateInterval = 0.2F;

        VRCUrl lyricsRequestUrl = VRCUrl.Empty;
        VRCUrl lyricsBadReportUrl = VRCUrl.Empty;
        VRCUrl requestedLyricsUrl = VRCUrl.Empty;
        VRCUrl requestedReportUrl = VRCUrl.Empty;
        bool isLyricsUpdating, isLyricsReportPending;
        int lyricsLineIndex = -1;

        [NonSerialized] public byte lyricsState;
        [NonSerialized] public string lyricsStatus = "";
        [NonSerialized] public string lyricsSourceId = "";
        [NonSerialized] public string lyricsTrackName = "";
        [NonSerialized] public string lyricsArtistName = "";
        [NonSerialized] public string lyricsPlainText = "";
        [NonSerialized] public string lyricsPreviousLine = "";
        [NonSerialized] public string lyricsCurrentLine = "";
        [NonSerialized] public string lyricsNextLine = "";
        [NonSerialized] public float[] lyricsTimes = new float[0];
        [NonSerialized] public string[] lyricsLines = new string[0];

        public byte LyricsState => lyricsState;
        public string LyricsStatus => lyricsStatus;
        public string LyricsPlainText => lyricsPlainText;
        public string LyricsPreviousLine => lyricsPreviousLine;
        public string LyricsCurrentLine => lyricsCurrentLine;
        public string LyricsNextLine => lyricsNextLine;
        public string LyricsTrackName => lyricsTrackName;
        public string LyricsArtistName => lyricsArtistName;
        public bool HasLyricsSource => !VRCUrl.IsNullOrEmpty(lyricsRequestUrl);
        public bool CanReportLyrics =>
            !isLyricsReportPending &&
            !VRCUrl.IsNullOrEmpty(lyricsBadReportUrl) &&
            (lyricsState == LYRICS_SYNCED || lyricsState == LYRICS_PLAIN || lyricsState == LYRICS_INSTRUMENTAL);

        public void SetLyricsSource(VRCUrl requestUrl, VRCUrl badReportUrl) {
            lyricsRequestUrl = requestUrl ?? VRCUrl.Empty;
            lyricsBadReportUrl = badReportUrl ?? VRCUrl.Empty;
        }

        public void SetLyricsSourceAndLoad(VRCUrl requestUrl, VRCUrl badReportUrl) {
            SetLyricsSource(requestUrl, badReportUrl);
            LoadLRCLIB();
        }

        public void _ClearLyricsSource() {
            lyricsRequestUrl = VRCUrl.Empty;
            lyricsBadReportUrl = VRCUrl.Empty;
            requestedLyricsUrl = VRCUrl.Empty;
            requestedReportUrl = VRCUrl.Empty;
            ClearLyricsData(LYRICS_NONE);
        }

        void LoadLRCLIB() {
            if (!enableLyrics || VRCUrl.IsNullOrEmpty(lyricsRequestUrl)) {
                requestedLyricsUrl = VRCUrl.Empty;
                requestedReportUrl = VRCUrl.Empty;
                ClearLyricsData(LYRICS_NONE);
                return;
            }
            if (IsSameUrl(lyricsRequestUrl, requestedLyricsUrl) && IsLyricsResultReusable()) return;
            requestedLyricsUrl = lyricsRequestUrl;
            requestedReportUrl = lyricsBadReportUrl;
            ClearLyricsData(LYRICS_LOADING);
            VRCStringDownloader.LoadUrl(requestedLyricsUrl, (IUdonEventReceiver)this);
        }

        public void _ReportBadLyrics() {
            if (!CanReportLyrics) return;
            isLyricsReportPending = true;
            requestedReportUrl = lyricsBadReportUrl;
            lyricsState = LYRICS_REPORTED;
            lyricsStatus = "";
            SendEvent("_OnLyricsData");
            VRCStringDownloader.LoadUrl(lyricsBadReportUrl, (IUdonEventReceiver)this);
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result) {
            if (IsSameUrl(result.Url, requestedReportUrl)) {
                isLyricsReportPending = false;
                lyricsState = LYRICS_REPORTED;
                SendEvent("_OnLyricsData");
                return;
            }
            if (!IsSameUrl(result.Url, requestedLyricsUrl)) return;
            ParseLyricsResult(result.Result);
        }

        public override void OnStringLoadError(IVRCStringDownload result) {
            if (IsSameUrl(result.Url, requestedReportUrl)) {
                isLyricsReportPending = false;
                lyricsState = LYRICS_REPORTED;
                SendEvent("_OnLyricsData");
                return;
            }
            if (!IsSameUrl(result.Url, requestedLyricsUrl)) return;
            ResetLyricsData(LYRICS_ERROR);
            lyricsStatus = result.Error;
            SendEvent("_OnLyricsData");
        }

        bool IsSameUrl(VRCUrl a, VRCUrl b) {
            if (VRCUrl.IsNullOrEmpty(a) || VRCUrl.IsNullOrEmpty(b)) return false;
            return a.Get() == b.Get();
        }

        bool IsLyricsResultReusable() {
            return lyricsState == LYRICS_LOADING ||
                lyricsState == LYRICS_SYNCED ||
                lyricsState == LYRICS_PLAIN ||
                lyricsState == LYRICS_INSTRUMENTAL ||
                lyricsState == LYRICS_NOT_FOUND;
        }

        void ParseLyricsResult(string json) {
            if (string.IsNullOrEmpty(json)) {
                ClearLyricsData(LYRICS_NOT_FOUND);
                return;
            }
            if (!VRCJson.TryDeserializeFromJson(json, out DataToken token) || token.TokenType != TokenType.DataDictionary) {
                ResetLyricsData(LYRICS_ERROR);
                lyricsStatus = "Invalid JSON";
                SendEvent("_OnLyricsData");
                return;
            }

            var dict = token.DataDictionary;
            lyricsSourceId = GetTokenString(dict, "id");
            lyricsTrackName = GetTokenString(dict, "trackName");
            lyricsArtistName = GetTokenString(dict, "artistName");
            lyricsPlainText = GetTokenString(dict, "plainLyrics");
            var syncedLyrics = GetTokenString(dict, "syncedLyrics");
            bool instrumental = false;
            if (dict.TryGetValue("instrumental", TokenType.Boolean, out token))
                instrumental = token.Boolean;

            if (instrumental) {
                lyricsState = LYRICS_INSTRUMENTAL;
                lyricsTimes = new float[0];
                lyricsLines = new string[0];
                UpdateLyricsLineIndex(-1);
                SendEvent("_OnLyricsData");
                return;
            }

            if (!string.IsNullOrEmpty(syncedLyrics) && ParseSyncedLyrics(syncedLyrics)) {
                lyricsState = LYRICS_SYNCED;
                UpdateLyricsLine();
                SendEvent("_OnLyricsData");
                StartLyricsLineUpdate();
                return;
            }

            if (!string.IsNullOrEmpty(lyricsPlainText)) {
                lyricsState = LYRICS_PLAIN;
                lyricsTimes = new float[0];
                lyricsLines = new string[0];
                UpdateLyricsLineIndex(-1);
                SendEvent("_OnLyricsData");
                return;
            }

            ClearLyricsData(LYRICS_NOT_FOUND);
        }

        string GetTokenString(DataDictionary dict, string key) {
            if (!dict.TryGetValue(key, out DataToken token)) return "";
            switch (token.TokenType) {
                case TokenType.String: return token.String;
                case TokenType.Double: return ((int)token.Double).ToString();
                case TokenType.Int: return token.Int.ToString();
                default: return "";
            }
        }

        bool ParseSyncedLyrics(string syncedLyrics) {
            int maxLines = Mathf.Max(1, maxSyncedLyricsLines);
            var tempTimes = new float[maxLines];
            var tempLines = new string[maxLines];
            int count = 0;
            var rawLines = syncedLyrics.Replace("\r\n", "\n").Split('\n');
            for (int i = 0, lineCount = rawLines.Length; i < lineCount && count < maxLines; i++) {
                var rawLine = rawLines[i];
                if (string.IsNullOrEmpty(rawLine)) continue;
                if (rawLine.EndsWith("\r")) rawLine = rawLine.Substring(0, rawLine.Length - 1);
                int position = 0;
                int tagCount = 0;
                var tagTimes = new float[8];
                while (position < rawLine.Length && rawLine[position] == '[') {
                    int closeIndex = rawLine.IndexOf(']', position + 1);
                    if (closeIndex < 0) break;
                    var tag = rawLine.Substring(position + 1, closeIndex - position - 1);
                    if (TryParseLrcTimestamp(tag, out float timestamp) && tagCount < tagTimes.Length) {
                        tagTimes[tagCount] = timestamp;
                        tagCount++;
                    }
                    position = closeIndex + 1;
                }
                if (tagCount == 0) continue;
                var text = position < rawLine.Length ? rawLine.Substring(position).Trim() : "";
                for (int j = 0; j < tagCount && count < maxLines; j++) {
                    tempTimes[count] = tagTimes[j];
                    tempLines[count] = text;
                    count++;
                }
            }

            if (count <= 0) return false;
            SortLyrics(tempTimes, tempLines, count);
            lyricsTimes = new float[count];
            lyricsLines = new string[count];
            Array.Copy(tempTimes, lyricsTimes, count);
            Array.Copy(tempLines, lyricsLines, count);
            return true;
        }

        bool TryParseLrcTimestamp(string tag, out float timestamp) {
            timestamp = 0;
            int colonIndex = tag.IndexOf(':');
            if (colonIndex <= 0 || colonIndex >= tag.Length - 1) return false;
            if (!int.TryParse(tag.Substring(0, colonIndex), out int minutes)) return false;
            int dotIndex = tag.IndexOf('.', colonIndex + 1);
            string secondsText = dotIndex >= 0 ?
                tag.Substring(colonIndex + 1, dotIndex - colonIndex - 1) :
                tag.Substring(colonIndex + 1);
            if (!int.TryParse(secondsText, out int seconds)) return false;
            float fraction = 0;
            if (dotIndex >= 0 && dotIndex < tag.Length - 1) {
                var fractionText = tag.Substring(dotIndex + 1);
                int digits = Mathf.Min(3, fractionText.Length);
                int value = 0;
                int scale = 1;
                for (int i = 0; i < digits; i++) {
                    char c = fractionText[i];
                    if (c < '0' || c > '9') break;
                    value = value * 10 + c - '0';
                    scale *= 10;
                }
                fraction = scale > 1 ? (float)value / scale : 0;
            }
            timestamp = minutes * 60F + seconds + fraction;
            return true;
        }

        void SortLyrics(float[] times, string[] lines, int count) {
            for (int i = 1; i < count; i++) {
                float time = times[i];
                string line = lines[i];
                int j = i - 1;
                while (j >= 0 && times[j] > time) {
                    times[j + 1] = times[j];
                    lines[j + 1] = lines[j];
                    j--;
                }
                times[j + 1] = time;
                lines[j + 1] = line;
            }
        }

        void ResetLyricsData(byte state) {
            isLyricsUpdating = false;
            isLyricsReportPending = false;
            lyricsLineIndex = -1;
            lyricsState = state;
            lyricsStatus = "";
            lyricsSourceId = "";
            lyricsTrackName = "";
            lyricsArtistName = "";
            lyricsPlainText = "";
            lyricsPreviousLine = "";
            lyricsCurrentLine = "";
            lyricsNextLine = "";
            lyricsTimes = new float[0];
            lyricsLines = new string[0];
        }

        void ClearLyricsData(byte state) {
            ResetLyricsData(state);
            SendEvent("_OnLyricsData");
        }

        void StartLyricsLineUpdate() {
            if (lyricsState != LYRICS_SYNCED || !IsPlaying || isLyricsUpdating) return;
            isLyricsUpdating = true;
            SendCustomEventDelayedSeconds(nameof(_UpdateLyricsLine), lyricsUpdateInterval);
        }

#if COMPILER_UDONSHARP
        public
#endif
        void _UpdateLyricsLine() {
            if (lyricsState != LYRICS_SYNCED || !IsPlaying) {
                isLyricsUpdating = false;
                return;
            }
            UpdateLyricsLine();
            SendCustomEventDelayedSeconds(nameof(_UpdateLyricsLine), lyricsUpdateInterval);
        }

        void UpdateLyricsLine() {
            if (lyricsState != LYRICS_SYNCED || lyricsTimes == null || lyricsTimes.Length == 0) {
                UpdateLyricsLineIndex(-1);
                return;
            }
            float time = Time + lyricsOffset;
            int index = -1;
            for (int i = 0, count = lyricsTimes.Length; i < count; i++) {
                if (lyricsTimes[i] > time) break;
                index = i;
            }
            UpdateLyricsLineIndex(index);
        }

        void UpdateLyricsLineIndex(int index) {
            if (lyricsLineIndex == index) return;
            lyricsLineIndex = index;
            if (index < 0 || lyricsLines == null || index >= lyricsLines.Length) {
                lyricsPreviousLine = "";
                lyricsCurrentLine = "";
                lyricsNextLine = "";
            } else {
                lyricsPreviousLine = index > 0 ? lyricsLines[index - 1] : "";
                lyricsCurrentLine = lyricsLines[index];
                lyricsNextLine = index + 1 < lyricsLines.Length ? lyricsLines[index + 1] : "";
            }
            SendEvent("_OnLyricsLineChange");
        }
    }
}
