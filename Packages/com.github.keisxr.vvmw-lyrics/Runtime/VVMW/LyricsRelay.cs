using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace JLChnToZ.VRC.VVMW {
    /// <summary>
    /// Lets one player fetch the lyrics for everyone and shares what came back, so the lyrics server sees one request
    /// per song instead of one per player.
    /// </summary>
    /// <remarks>
    /// The player who started the video fetches first. Only a fetcher that cannot reach the server at all (no HTTP
    /// status, or no answer within <see cref="fetchTimeout"/>) passes the job on: the next player with the lowest ID who
    /// has not tried yet takes over. Any answer from the server, even an HTTP error, is final and shared as is.
    /// When nobody can reach the server, everyone shows the last error.
    /// Keep this on its own GameObject: fetching takes ownership of it, which must not move ownership of the core.
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [DisallowMultipleComponent]
    public class LyricsRelay : UdonSharpBehaviour {
        const byte STATE_IDLE = 0;
        const byte STATE_FETCHING = 1;
        const byte STATE_DONE = 2;
        const byte STATE_ERROR = 3;
        const byte STATE_UNREACHABLE = 4;

        [SerializeField] Core core;
        [Tooltip("Seconds to wait for the player who started the video to begin fetching before somebody else does.")]
        [SerializeField, Range(1F, 30F)] float claimTimeout = 4F;
        [Tooltip("Seconds without any progress from the fetcher before the next player takes over.")]
        [SerializeField, Range(5F, 120F)] float fetchTimeout = 15F;

        [UdonSynced] string syncedKey = "";
        [UdonSynced] byte syncedState = STATE_IDLE;
        // The response JSON when done, otherwise the error message.
        [UdonSynced] string syncedData = "";
        [UdonSynced] int syncedFetcher = -1;
        [UdonSynced] int[] syncedTried = new int[0];
        [UdonSynced] int syncedProgress;

        string localKey = "";
        bool isLocalFetching, isWatching;
        float requestTime, lastProgressTime;
        int lastSeenProgress = -1;
        string lastSeenKey = "";

        int LocalId {
            get {
                var player = Networking.LocalPlayer;
                return Utilities.IsValid(player) ? player.playerId : -1;
            }
        }

        /// <summary>
        /// Internal use only. Called by the core whenever it wants the lyrics behind <paramref name="key"/>.
        /// </summary>
        public void _Request(string key) {
            localKey = key == null ? "" : key;
            isLocalFetching = false;
            if (string.IsNullOrEmpty(localKey)) return;
            requestTime = Time.time;
            if (syncedKey == localKey) {
                switch (syncedState) {
                    case STATE_DONE:
                        Deliver();
                        return;
                    case STATE_ERROR:
                    case STATE_UNREACHABLE:
                        // The player who started the video asks again on a reload, which is the moment to retry.
                        if (IsCoreOwner()) Claim();
                        else Deliver();
                        return;
                    case STATE_FETCHING:
                        if (syncedFetcher == LocalId) BeginLocalFetch();
                        else StartWatching();
                        return;
                }
            }
            if (IsCoreOwner()) Claim();
            else StartWatching();
        }

        /// <summary>
        /// Internal use only. Called by the core when it no longer wants any lyrics.
        /// </summary>
        public void _CancelRequest() {
            localKey = "";
            isLocalFetching = false;
        }

        /// <summary>
        /// Internal use only. The fetch made another step, such as one chunk of a video ID.
        /// </summary>
        public void _ReportProgress(string key) {
            if (!IsMyFetch(key)) return;
            lastProgressTime = Time.time;
            syncedProgress++;
            RequestSerialization();
        }

        /// <summary>
        /// Internal use only. The server answered; <paramref name="json"/> is what everyone should parse.
        /// </summary>
        public void _ReportResult(string key, string json) {
            if (!IsMyFetch(key)) return;
            Finish(STATE_DONE, Compact(json));
        }

        /// <summary>
        /// Internal use only. The server was reached but answered with an error.
        /// </summary>
        public void _ReportError(string key, string message) {
            if (!IsMyFetch(key)) return;
            Finish(STATE_ERROR, message);
        }

        /// <summary>
        /// Internal use only. The server could not be reached from this client.
        /// </summary>
        public void _ReportUnreachable(string key, string message) {
            if (!IsMyFetch(key)) return;
            isLocalFetching = false;
            // Somebody already took over after this client went quiet, so leave the job with them.
            if (syncedFetcher != LocalId) {
                StartWatching();
                return;
            }
            HandOver(LocalId, message);
        }

        public override void OnDeserialization() {
            if (string.IsNullOrEmpty(localKey) || syncedKey != localKey) return;
            switch (syncedState) {
                case STATE_DONE:
                case STATE_ERROR:
                case STATE_UNREACHABLE:
                    isLocalFetching = false;
                    Deliver();
                    break;
                case STATE_FETCHING:
                    if (syncedFetcher == LocalId && !isLocalFetching) BeginLocalFetch();
                    break;
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player) {
            if (player == null || string.IsNullOrEmpty(localKey)) return;
            if (syncedKey != localKey || syncedState != STATE_FETCHING) return;
            int leftId = player.playerId;
            if (leftId != syncedFetcher) return;
            if (PickCandidate(leftId, true) == LocalId) TakeOver(leftId);
        }

#if COMPILER_UDONSHARP
        public
#endif
        void _Watch() {
            isWatching = false;
            if (string.IsNullOrEmpty(localKey) || !Utilities.IsValid(core)) return;
            float now = Time.time;
            if (syncedKey == localKey) {
                if (syncedState != STATE_FETCHING) return;
                if (syncedProgress != lastSeenProgress || syncedKey != lastSeenKey) {
                    lastSeenProgress = syncedProgress;
                    lastSeenKey = syncedKey;
                    lastProgressTime = now;
                }
                if (now - lastProgressTime > fetchTimeout) {
                    lastProgressTime = now;
                    if (isLocalFetching) {
                        // This client's own request went silent, e.g. an untrusted URL that never calls back.
                        isLocalFetching = false;
                        core._RelayAbortFetch(localKey);
                        if (syncedFetcher == LocalId) {
                            HandOver(LocalId, "Timeout");
                            return;
                        }
                    } else if (syncedFetcher != LocalId && PickCandidate(syncedFetcher, true) == LocalId) {
                        TakeOver(syncedFetcher);
                        return;
                    }
                }
            } else if (now - requestTime > claimTimeout + fetchTimeout) {
                // The relay is busy with something else for too long, e.g. a Quest client asking for another
                // video ID than the PC owner. Fall back to fetching alone rather than never showing anything.
                core._RelayBypass(localKey);
                return;
            } else if (now - requestTime > claimTimeout && syncedState != STATE_FETCHING && PickCandidate(-1, false) == LocalId) {
                // Nobody has started on this video yet, e.g. the player who started it has already left.
                Claim();
                return;
            }
            StartWatching();
        }

        void StartWatching() {
            if (isWatching) return;
            isWatching = true;
            SendCustomEventDelayedSeconds(nameof(_Watch), 1F);
        }

        void Claim() {
            TakeOwnership();
            syncedKey = localKey;
            syncedState = STATE_FETCHING;
            syncedData = "";
            syncedFetcher = LocalId;
            syncedTried = new int[0];
            syncedProgress = 0;
            RequestSerialization();
            BeginLocalFetch();
        }

        void TakeOver(int failedId) {
            TakeOwnership();
            syncedTried = Append(syncedTried, failedId);
            syncedFetcher = LocalId;
            syncedProgress++;
            RequestSerialization();
            BeginLocalFetch();
        }

        void HandOver(int failedId, string message) {
            TakeOwnership();
            syncedTried = Append(syncedTried, failedId);
            int next = PickCandidate(-1, true);
            if (next < 0) {
                syncedState = STATE_UNREACHABLE;
                syncedData = message == null ? "" : message;
                syncedFetcher = -1;
                RequestSerialization();
                Deliver();
                return;
            }
            syncedFetcher = next;
            syncedProgress++;
            RequestSerialization();
            StartWatching();
        }

        void BeginLocalFetch() {
            TakeOwnership();
            isLocalFetching = true;
            lastProgressTime = Time.time;
            StartWatching();
            core._RelayStartFetch(localKey);
        }

        void Finish(byte state, string data) {
            TakeOwnership();
            isLocalFetching = false;
            syncedState = state;
            syncedData = data == null ? "" : data;
            syncedFetcher = -1;
            RequestSerialization();
        }

        void Deliver() {
            if (string.IsNullOrEmpty(localKey) || syncedKey != localKey) return;
            switch (syncedState) {
                case STATE_DONE: core._RelayDeliverResult(localKey, syncedData); break;
                case STATE_ERROR: core._RelayDeliverError(localKey, syncedData); break;
                case STATE_UNREACHABLE: core._RelayDeliverUnreachable(localKey, syncedData); break;
            }
        }

        bool IsMyFetch(string key) => isLocalFetching && key == localKey && key == syncedKey;

        bool IsCoreOwner() => Utilities.IsValid(core) && Networking.IsOwner(core.gameObject);

        void TakeOwnership() {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        // The player with the lowest ID who has not tried yet, so every client picks the same one.
        int PickCandidate(int exclude, bool skipTried) {
            var players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            int best = -1;
            for (int i = 0; i < players.Length; i++) {
                var player = players[i];
                if (!Utilities.IsValid(player)) continue;
                int id = player.playerId;
                if (id == exclude || (skipTried && Contains(syncedTried, id))) continue;
                if (best < 0 || id < best) best = id;
            }
            return best;
        }

        bool Contains(int[] values, int value) {
            if (values == null) return false;
            for (int i = 0; i < values.Length; i++)
                if (values[i] == value) return true;
            return false;
        }

        int[] Append(int[] values, int value) {
            if (values == null) values = new int[0];
            if (Contains(values, value)) return values;
            var result = new int[values.Length + 1];
            System.Array.Copy(values, result, values.Length);
            result[values.Length] = value;
            return result;
        }

        // Keeps only what the core reads. Plain lyrics are dropped when synced ones exist, which roughly halves the size.
        string Compact(string json) {
            if (string.IsNullOrEmpty(json)) return "";
            if (!VRCJson.TryDeserializeFromJson(json, out DataToken token) || token.TokenType != TokenType.DataDictionary)
                return json;
            var source = token.DataDictionary;
            var result = new DataDictionary();
            CopyKey(source, result, "id");
            CopyKey(source, result, "videoId");
            CopyKey(source, result, "trackName");
            CopyKey(source, result, "artistName");
            CopyKey(source, result, "instrumental");
            if (source.TryGetValue("syncedLyrics", TokenType.String, out DataToken synced) && !string.IsNullOrEmpty(synced.String))
                result.SetValue("syncedLyrics", synced);
            else
                CopyKey(source, result, "plainLyrics");
            if (VRCJson.TrySerializeToJson(result, JsonExportType.Minify, out DataToken output)) return output.String;
            return json;
        }

        void CopyKey(DataDictionary source, DataDictionary target, string key) {
            if (source.TryGetValue(key, out DataToken value)) target.SetValue(key, value);
        }
    }
}
