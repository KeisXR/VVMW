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
        /// <summary>
        /// Nobody in the instance could reach the lyrics server. <see cref="LyricsStatus"/> holds the last error.
        /// </summary>
        public const byte LYRICS_UNREACHABLE = 8;
        const string LYRICS_VIDEO_ID_KEY_PREFIX = "vid:";

        [SerializeField, LocalizedLabel] bool enableLyrics = true;
        [SerializeField, LocalizedLabel, Range(-30F, 30F)] float lyricsOffset = 0;
        [SerializeField, LocalizedLabel, Range(1, 2048)] int maxSyncedLyricsLines = 512;
        [SerializeField, LocalizedLabel, Range(0.05F, 1F)] float lyricsUpdateInterval = 0.2F;

        /// <summary>
        /// The characters a YouTube video ID is made of, in the order the video ID lyrics URL pool is laid out.
        /// </summary>
        public const string LYRICS_VIDEO_ID_CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        /// <summary>
        /// The length of a YouTube video ID.
        /// </summary>
        public const int LYRICS_VIDEO_ID_LENGTH = 11;
        /// <summary>
        /// How many requests it takes to send a whole video ID: five pairs of characters, then the last character.
        /// </summary>
        public const int LYRICS_VIDEO_ID_CHUNKS = 6;
        /// <summary>
        /// How many pool entries each chunk owns: one for every pair of characters.
        /// </summary>
        public const int LYRICS_VIDEO_ID_CHUNK_SIZE = 64 * 64;
        /// <summary>
        /// The size of the video ID lyrics URL pool. The last chunk carries a single character.
        /// </summary>
        public const int LYRICS_VIDEO_ID_POOL_SIZE = (LYRICS_VIDEO_ID_CHUNKS - 1) * LYRICS_VIDEO_ID_CHUNK_SIZE + 64;

        // Udon cannot build a VRCUrl at runtime, so a video played from a pasted URL has no lyrics URL of its own.
        // This pool holds one fixed URL for every chunk of a video ID, which lets the ID be spelled out to the server.
        [SerializeField] VRCUrl[] lyricsVideoIdUrlPool;
        [SerializeField] VRCUrl lyricsVideoIdReportUrl;
        // Lets one player fetch the lyrics and share them, instead of everyone hitting the server. Optional.
        [SerializeField, LocalizedLabel] LyricsRelay lyricsRelay;

        // What the current fetch is for: the lyrics URL, or the video ID prefixed with LYRICS_VIDEO_ID_KEY_PREFIX.
        string lyricsFetchKey = "";
        // True while this client has a lyrics request of its own in flight.
        bool isLyricsFetching;
        // True when this client fetches on its own because the relay could not settle on someone.
        bool isLyricsRelayBypassed;

        VRCUrl lyricsRequestUrl = VRCUrl.Empty;
        VRCUrl lyricsBadReportUrl = VRCUrl.Empty;
        VRCUrl requestedLyricsUrl = VRCUrl.Empty;
        VRCUrl requestedReportUrl = VRCUrl.Empty;
        bool isLyricsUpdating, isLyricsReportPending;
        int lyricsLineIndex = -1;
        string lyricsVideoId = "";
        int lyricsVideoIdChunk = -1;
        bool lyricsVideoIdRetried;

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
        public float LyricsOffset {
            get => lyricsOffset;
            set {
                value = Mathf.Clamp(Mathf.Round(value * 10F) * 0.1F, -30F, 30F);
                if (lyricsOffset == value) return;
                lyricsOffset = value;
                UpdateLyricsLine();
                SendEvent("_OnLyricsOffsetChange");
            }
        }

        void ResetLyricsOffset(bool notify) {
            if (lyricsOffset == 0F) return;
            lyricsOffset = 0F;
            if (!notify) return;
            UpdateLyricsLine();
            SendEvent("_OnLyricsOffsetChange");
        }

        public bool HasLyricsSource => !VRCUrl.IsNullOrEmpty(lyricsRequestUrl) || !string.IsNullOrEmpty(lyricsVideoId);
        public bool CanReportLyrics =>
            !isLyricsReportPending &&
            !VRCUrl.IsNullOrEmpty(lyricsBadReportUrl) &&
            (lyricsState == LYRICS_SYNCED || lyricsState == LYRICS_PLAIN || lyricsState == LYRICS_INSTRUMENTAL || lyricsState == LYRICS_NOT_FOUND);

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
            lyricsVideoId = "";
            lyricsVideoIdChunk = -1;
            CancelLyricsFetch();
            ResetLyricsOffset(false);
            ClearLyricsData(LYRICS_NONE);
        }

        void LoadLRCLIB() {
            if (!enableLyrics) {
                lyricsVideoId = "";
                lyricsVideoIdChunk = -1;
                requestedLyricsUrl = VRCUrl.Empty;
                requestedReportUrl = VRCUrl.Empty;
                CancelLyricsFetch();
                ClearLyricsData(LYRICS_NONE);
                return;
            }
            if (VRCUrl.IsNullOrEmpty(lyricsRequestUrl)) {
                LoadLyricsByVideoId();
                return;
            }
            lyricsVideoId = "";
            lyricsVideoIdChunk = -1;
            if (IsSameUrl(lyricsRequestUrl, requestedLyricsUrl) && IsLyricsResultReusable()) return;
            requestedLyricsUrl = lyricsRequestUrl;
            requestedReportUrl = lyricsBadReportUrl;
            ClearLyricsData(LYRICS_LOADING);
            BeginLyricsFetch(requestedLyricsUrl.Get());
        }

        // Fallback for anything played without a lyrics URL of its own, such as a URL pasted into the input field.
        // Every client works the video ID out of the URL it is playing, so nothing has to be synced for it.
        void LoadLyricsByVideoId() {
            string videoId = "";
            // The alternate URL outlives the video it belongs to, so it only counts while something is loaded.
            if (Utilities.IsValid(lyricsVideoIdUrlPool) &&
                lyricsVideoIdUrlPool.Length >= LYRICS_VIDEO_ID_POOL_SIZE &&
                !VRCUrl.IsNullOrEmpty(localUrl)) {
                videoId = GetYouTubeVideoId(localUrl);
                if (string.IsNullOrEmpty(videoId)) videoId = GetYouTubeVideoId(altUrl);
            }
            if (string.IsNullOrEmpty(videoId)) {
                lyricsVideoId = "";
                lyricsVideoIdChunk = -1;
                lyricsBadReportUrl = VRCUrl.Empty;
                requestedLyricsUrl = VRCUrl.Empty;
                requestedReportUrl = VRCUrl.Empty;
                CancelLyricsFetch();
                ClearLyricsData(LYRICS_NONE);
                return;
            }
            lyricsBadReportUrl = lyricsVideoIdReportUrl ?? VRCUrl.Empty;
            if (videoId == lyricsVideoId && IsLyricsResultReusable()) return;
            lyricsVideoId = videoId;
            lyricsVideoIdRetried = false;
            requestedReportUrl = lyricsBadReportUrl;
            ClearLyricsData(LYRICS_LOADING);
            BeginLyricsFetch(LYRICS_VIDEO_ID_KEY_PREFIX + videoId);
        }

        bool UseLyricsRelay => synced && Utilities.IsValid(lyricsRelay) && !isLyricsRelayBypassed;

        void BeginLyricsFetch(string key) {
            lyricsFetchKey = key;
            isLyricsFetching = false;
            isLyricsRelayBypassed = false;
            if (UseLyricsRelay) lyricsRelay._Request(key);
            else StartLocalLyricsFetch();
        }

        void CancelLyricsFetch() {
            lyricsFetchKey = "";
            isLyricsFetching = false;
            isLyricsRelayBypassed = false;
            if (Utilities.IsValid(lyricsRelay)) lyricsRelay._CancelRequest();
        }

        // Actually sends the request from this client.
        void StartLocalLyricsFetch() {
            isLyricsFetching = true;
            if (lyricsFetchKey.StartsWith(LYRICS_VIDEO_ID_KEY_PREFIX)) {
                lyricsVideoIdRetried = false;
                RequestLyricsVideoIdChunk(0);
                return;
            }
            lyricsVideoIdChunk = -1;
            VRCStringDownloader.LoadUrl(requestedLyricsUrl, (IUdonEventReceiver)this);
        }

        bool HasFinalLyrics =>
            lyricsState == LYRICS_SYNCED ||
            lyricsState == LYRICS_PLAIN ||
            lyricsState == LYRICS_INSTRUMENTAL ||
            lyricsState == LYRICS_NOT_FOUND ||
            lyricsState == LYRICS_REPORTED;

        /// <summary>
        /// Internal use only. The relay picked this client to fetch.
        /// </summary>
        public void _RelayStartFetch(string key) {
            if (key != lyricsFetchKey) return;
            StartLocalLyricsFetch();
        }

        /// <summary>
        /// Internal use only. The relay gave up on this client's request, which went silent.
        /// </summary>
        public void _RelayAbortFetch(string key) {
            if (key != lyricsFetchKey) return;
            isLyricsFetching = false;
            lyricsVideoIdChunk = -1;
        }

        /// <summary>
        /// Internal use only. The relay could not settle on anyone, so this client fetches on its own.
        /// </summary>
        public void _RelayBypass(string key) {
            if (key != lyricsFetchKey || isLyricsFetching || HasFinalLyrics) return;
            isLyricsRelayBypassed = true;
            StartLocalLyricsFetch();
        }

        /// <summary>
        /// Internal use only. Lyrics fetched by another player.
        /// </summary>
        public void _RelayDeliverResult(string key, string json) {
            if (key != lyricsFetchKey || HasFinalLyrics) return;
            isLyricsFetching = false;
            lyricsVideoIdChunk = -1;
            ParseLyricsResult(json);
        }

        /// <summary>
        /// Internal use only. The server answered another player with an error.
        /// </summary>
        public void _RelayDeliverError(string key, string message) {
            if (key != lyricsFetchKey || HasFinalLyrics) return;
            isLyricsFetching = false;
            lyricsVideoIdChunk = -1;
            ResetLyricsData(LYRICS_ERROR);
            lyricsStatus = message;
            SendEvent("_OnLyricsData");
        }

        /// <summary>
        /// Internal use only. Nobody in the instance could reach the server.
        /// </summary>
        public void _RelayDeliverUnreachable(string key, string message) {
            if (key != lyricsFetchKey || HasFinalLyrics) return;
            isLyricsFetching = false;
            lyricsVideoIdChunk = -1;
            ResetLyricsData(LYRICS_UNREACHABLE);
            lyricsStatus = message;
            SendEvent("_OnLyricsData");
        }

        // Whether the shared result of this fetch should go through the relay.
        bool ShouldReportToRelay => isLyricsFetching && UseLyricsRelay;

        void FinishLocalFetchWithResult(string json) {
            if (ShouldReportToRelay) lyricsRelay._ReportResult(lyricsFetchKey, json);
            isLyricsFetching = false;
        }

        void FinishLocalFetchWithError(string message) {
            if (ShouldReportToRelay) lyricsRelay._ReportError(lyricsFetchKey, message);
            isLyricsFetching = false;
        }

        // An HTTP status means the server was reached; anything else (DNS, refused, timeout, blocked) means it was not.
        bool IsServerReached(int errorCode) => errorCode >= 100 && errorCode < 600;

        void RequestLyricsVideoIdChunk(int chunk) {
            int offset = chunk * 2;
            int index = chunk * LYRICS_VIDEO_ID_CHUNK_SIZE;
            if (chunk < LYRICS_VIDEO_ID_CHUNKS - 1)
                index += LYRICS_VIDEO_ID_CHARS.IndexOf(lyricsVideoId[offset]) * 64 + LYRICS_VIDEO_ID_CHARS.IndexOf(lyricsVideoId[offset + 1]);
            else
                index += LYRICS_VIDEO_ID_CHARS.IndexOf(lyricsVideoId[offset]);
            var chunkUrl = lyricsVideoIdUrlPool[index];
            if (VRCUrl.IsNullOrEmpty(chunkUrl)) {
                lyricsVideoIdChunk = -1;
                requestedLyricsUrl = VRCUrl.Empty;
                FinishLocalFetchWithError("");
                ClearLyricsData(LYRICS_ERROR);
                return;
            }
            // Each chunk is another round trip, which tells the relay this client is still at it.
            if (chunk > 0 && ShouldReportToRelay) lyricsRelay._ReportProgress(lyricsFetchKey);
            lyricsVideoIdChunk = chunk;
            requestedLyricsUrl = chunkUrl;
            VRCStringDownloader.LoadUrl(chunkUrl, (IUdonEventReceiver)this);
        }

        // The server answers every chunk with the lyrics of the video it thinks is meant, if any.
        // Only an answer naming this exact video ID is taken; otherwise the next chunk narrows it down.
        void HandleLyricsVideoIdResult(string json) {
            if (!string.IsNullOrEmpty(json) &&
                VRCJson.TryDeserializeFromJson(json, out DataToken token) &&
                token.TokenType == TokenType.DataDictionary &&
                GetTokenString(token.DataDictionary, "videoId") == lyricsVideoId) {
                lyricsVideoIdChunk = -1;
                FinishLocalFetchWithResult(json);
                ApplyLyricsResult(token.DataDictionary);
                return;
            }
            if (lyricsVideoIdChunk < LYRICS_VIDEO_ID_CHUNKS - 1) {
                RequestLyricsVideoIdChunk(lyricsVideoIdChunk + 1);
                return;
            }
            // The whole ID has been sent but the server answered for another video, which happens when a request
            // left over from the previous video slips in between. Spell the ID out once more before giving up.
            if (!lyricsVideoIdRetried) {
                lyricsVideoIdRetried = true;
                RequestLyricsVideoIdChunk(0);
                return;
            }
            lyricsVideoIdChunk = -1;
            FinishLocalFetchWithResult("");
            ClearLyricsData(LYRICS_NOT_FOUND);
        }

        string GetYouTubeVideoId(VRCUrl mediaUrl) {
            if (VRCUrl.IsNullOrEmpty(mediaUrl)) return "";
            var url = mediaUrl.Get();
            if (string.IsNullOrEmpty(url)) return "";
            int index = url.IndexOf("youtube.com/");
            if (index >= 0)
                index += 12;
            else {
                index = url.IndexOf("youtube-nocookie.com/");
                if (index >= 0)
                    index += 21;
                else {
                    index = url.IndexOf("youtu.be/");
                    return index < 0 ? "" : ReadYouTubeVideoId(url, index + 9);
                }
            }
            if (url.IndexOf("watch", index) == index) {
                int queryIndex = url.IndexOf("?v=", index);
                if (queryIndex < 0) queryIndex = url.IndexOf("&v=", index);
                return queryIndex < 0 ? "" : ReadYouTubeVideoId(url, queryIndex + 3);
            }
            if (url.IndexOf("shorts/", index) == index) return ReadYouTubeVideoId(url, index + 7);
            if (url.IndexOf("embed/", index) == index) return ReadYouTubeVideoId(url, index + 6);
            if (url.IndexOf("live/", index) == index) return ReadYouTubeVideoId(url, index + 5);
            if (url.IndexOf("v/", index) == index) return ReadYouTubeVideoId(url, index + 2);
            return "";
        }

        string ReadYouTubeVideoId(string url, int start) {
            int end = start;
            while (end < url.Length && LYRICS_VIDEO_ID_CHARS.IndexOf(url[end]) >= 0) end++;
            if (end - start != LYRICS_VIDEO_ID_LENGTH) return "";
            var videoId = url.Substring(start, LYRICS_VIDEO_ID_LENGTH);
            // Embed paths that are as long as a video ID but name a playlist or a channel's live stream.
            if (videoId == "videoseries" || videoId == "live_stream") return "";
            return videoId;
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
            // A request the relay already gave up on may still come back late; somebody else owns that job now.
            if (!isLyricsFetching || !IsSameUrl(result.Url, requestedLyricsUrl)) return;
            if (lyricsVideoIdChunk >= 0) {
                HandleLyricsVideoIdResult(result.Result);
                return;
            }
            FinishLocalFetchWithResult(result.Result);
            ParseLyricsResult(result.Result);
        }

        public override void OnStringLoadError(IVRCStringDownload result) {
            if (IsSameUrl(result.Url, requestedReportUrl)) {
                isLyricsReportPending = false;
                lyricsState = LYRICS_REPORTED;
                SendEvent("_OnLyricsData");
                return;
            }
            if (!isLyricsFetching || !IsSameUrl(result.Url, requestedLyricsUrl)) return;
            int errorCode = result.ErrorCode;
            // A server that answers "not found" for a chunk it cannot resolve yet still wants the rest of the ID.
            if (lyricsVideoIdChunk >= 0 && errorCode == 404) {
                HandleLyricsVideoIdResult("");
                return;
            }
            lyricsVideoIdChunk = -1;
            var message = string.IsNullOrEmpty(result.Error) ? errorCode.ToString() : result.Error;
            if (!IsServerReached(errorCode) && ShouldReportToRelay) {
                // This client could not get through at all; the relay hands the job to the next player and
                // tells everyone, this client included, if nobody else can get through either.
                isLyricsFetching = false;
                lyricsRelay._ReportUnreachable(lyricsFetchKey, message);
                return;
            }
            FinishLocalFetchWithError(message);
            ResetLyricsData(IsServerReached(errorCode) ? LYRICS_ERROR : LYRICS_UNREACHABLE);
            lyricsStatus = message;
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
            ApplyLyricsResult(token.DataDictionary);
        }

        void ApplyLyricsResult(DataDictionary dict) {
            lyricsSourceId = GetTokenString(dict, "id");
            lyricsTrackName = GetTokenString(dict, "trackName");
            lyricsArtistName = GetTokenString(dict, "artistName");
            lyricsPlainText = GetTokenString(dict, "plainLyrics");
            var syncedLyrics = GetTokenString(dict, "syncedLyrics");
            bool instrumental = false;
            if (dict.TryGetValue("instrumental", TokenType.Boolean, out DataToken token))
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
