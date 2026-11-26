using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
#if COMPILER_UDONSHARP && UDON_NETWORKING_UPDATED
using VRC.SDK3.UdonNetworkCalling;
#endif
using VRC.Udon.Common.Interfaces;
using JLChnToZ.VRC.Foundation.I18N;

namespace JLChnToZ.VRC.VVMW {
    public partial class Core {
        const double HEALTH_CHECK_SEND_INTERVAL = 3;
        const float HEALTH_CHECK_VALIDATE_INTERVAL_MIN = 5, HEALTH_CHECK_VALIDATE_INTERVAL_MAX = 10;
        [SerializeField, LocalizedLabel, Range(0, 5)] float timeDriftDetectThreshold = 0.9F;
        [UdonSynced] int ownerNetworkTime;
        // When playing, it is the virtual network time (scaled by speed) when the video started playing;
        // When paused, it is the progress of the video in milliseconds.
        [UdonSynced] int time;
        [UdonSynced] int rangeLoopStart = -1, rangeLoopEnd = -1;
        [UdonSynced] float syncedSpeed = 1, syncedActualSpeed = 1;
        [FieldChangeCallback(nameof(PerformerId))]
        [UdonSynced] ushort performerId;
        [FieldChangeCallback(nameof(SyncOffset))]
        float syncOffset = 0;
        [FieldChangeCallback(nameof(Speed))]
        float speed = 1;
        float actualSpeed = 1;
        float localRangeLoopStart, localRangeLoopEnd;
        int localRangeLoopStartMs = -1, localRangeLoopEndMs = -1;
        float rangeLoopDuration;
        int rangeLoopDurationMs = 1; // prevent division by zero
        double syncLatency;
        float lastSyncRawTime;
        float localSeekIgnoreRemoteUntil;
        double lastLocalSeekServerTime = -1;
        bool isBuffering;
        bool isResyncTime;
        bool isCheckingRangeLoop;
        VRCPlayerApi performer;
        DateTime lastSyncNetworkTime;
        double lastHealthCheckTime;
        // The start time in seconds that the URL being loaded asks for, or -1 if none.
        float urlStartTime = -1;

        /// <summary>
        /// The current time of the video in seconds.
        /// </summary>
        /// <remarks>
        /// If it is a live stream, this value will be zero.
        /// </remarks>
        // Find this code useful to your project? You are welcome to adopt it.
        // If you are respectful, please kindly leave a credit that you got inspired here;
        // or you can be an asshole who just rip it off, refactor it and then claim you made it.
        public float Time {
            get => Utilities.IsValid(activeHandler) ? activeHandler.Time : 0;
            private set {
                if (IsRangeLooping) value = CalculateLoopTime(value);
                activeHandler.Time = value;
                lastSyncRawTime = value;
                isBuffering = false; // Reset buffering state
                UpdateLyricsLine();
            }
        }

        /// <summary>
        /// The duration of the video in seconds.
        /// </summary>
        /// <remarks>
        /// If it is a live stream, this value will be infinity.
        /// </remarks>
        public float Duration => Utilities.IsValid(activeHandler) ? activeHandler.Duration : 0;

        /// <summary>
        /// The start time of the range loop in seconds. If it is negative, the range loop is disabled.
        /// </summary>
        public float RangeLoopStart => localRangeLoopStart;

        /// <summary>
        /// The end time of the range loop in seconds. If it is negative, the range loop is disabled.
        /// </summary>
        public float RangeLoopEnd => localRangeLoopEnd;

        /// <summary>
        /// The offset of the video time to other players, in seconds.
        /// </summary>
        public float SyncOffset {
            get => syncOffset;
            set {
                if (syncOffset == value) return;
                syncOffset = value;
                SendEvent("_OnSyncOffsetChange");
                if (synced && Utilities.IsValid(activeHandler) && activeHandler.IsPlaying) {
                    var duration = activeHandler.Duration;
                    if (duration <= 0 || float.IsInfinity(duration)) return;
                    Time = CalcVideoTime();
                    SendEvent("_OnTimeDrift");
                }
            }
        }

        /// <summary>
        /// The playback progress of the video, from 0 to 1.
        /// </summary>
        /// <remarks>
        /// If it is a live stream, this value will be zero, and setting this value will have no effect.
        /// </remarks>
        public float Progress {
            get {
                if (!Utilities.IsValid(activeHandler)) return 0;
                var duration = activeHandler.Duration;
                if (duration <= 0 || float.IsInfinity(duration)) return 0;
                return activeHandler.Time / duration;
            }
            set {
                if (!Utilities.IsValid(activeHandler)) return;
                var duration = activeHandler.Duration;
                if (duration <= 0 || float.IsInfinity(duration)) return;
                if (synced) {
                    localSeekIgnoreRemoteUntil = UnityEngine.Time.time + 3F;
                    lastLocalSeekServerTime = Networking.GetServerTimeInSeconds();
                    if (!Networking.IsOwner(gameObject))
                        Networking.SetOwner(Networking.LocalPlayer, gameObject);
                }
                Time = duration * value;
                UpdateLyricsLine();
                if (synced && Networking.IsOwner(gameObject)) {
                    lastSyncNetworkTime = Networking.GetNetworkDateTime();
                    ownerNetworkTime = Networking.GetServerTimeInMilliseconds();
                    syncedSpeed = speed;
                    state = activeHandler.IsPlaying ? PLAYING : PAUSED;
                    time = (int)(CalcSyncTime(out actualSpeed) * 1000);
                    syncedActualSpeed = actualSpeed;
                    RequestSerialization();
                } else
                    RequestSync();
            }
        }

        /// <summary>
        /// Is current video player backend support speed adjustment.
        /// </summary>
        public bool SupportSpeedAdjustment => Utilities.IsValid(activeHandler) && activeHandler.SupportSpeedAdjustment;

        /// <summary>
        /// The playback speed of the video.
        /// </summary>
        /// <remarks>
        /// The value will be clamped between 0.1 and 2.
        /// </remarks>
        public float Speed {
            get => speed;
            set {
                if (speed == value) return;
                speed = Mathf.Clamp(value, 0.1F, 2);
                SyncSpeed();
                RequestSync();
            }
        }

        /// <summary>
        /// Whether the video player is currently looping in a specific range.
        /// </summary>
        public bool IsRangeLooping => localRangeLoopStartMs >= 0 && localRangeLoopEndMs > localRangeLoopStartMs;

        /// <summary>
        /// Gets the performer of the video player playback.
        /// </summary>
        public VRCPlayerApi Performer => performer;

        ushort PerformerId {
            set {
                performerId = value;
                if (performerId == 0)
                    performer = null;
                else
                    performer = VRCPlayerApi.GetPlayerById(performerId);
                SendEvent("_OnPerformerChange");
            }
        }

        float PerformerLatency => Utilities.IsValid(performer) && !performer.isLocal ? UnityEngine.Time.realtimeSinceStartup - Networking.SimulationTime(performer) : 0;

        /// <summary>
        /// Event entry point on the ownership of the video player is transferred.
        /// Internal use only. Do not call this method.
        /// </summary>
        public override void OnOwnershipTransferred(VRCPlayerApi player) {
            Heartbeat();
            bool isLocalOwner = Utilities.IsValid(player) && player.isLocal;
            if (!isLocalOwner) isLocalReloading = false;
            syncLatency = 0;
            if (isLocalOwner && synced)
                SendCustomEventDelayedFrames(nameof(_DeferredOwnerSync), 0);
        }

#if COMPILER_UDONSHARP
        public
#endif
        void _DeferredOwnerSync() {
            if (!synced || !Networking.IsOwner(gameObject)) return;
            RequestSerialization();
        }

        void StartSyncTime() {
            StartCheckRangeLoop();
            if (!synced) return;
            SyncTime(true, false);
            if (!isResyncTime) {
                isResyncTime = true;
                SendCustomEventDelayedSeconds(nameof(_AutoSyncTime), 0.5F);
            }
        }

        void StartCheckRangeLoop() {
            if (isCheckingRangeLoop) return;
            isCheckingRangeLoop = true;
            SendCustomEventDelayedFrames(nameof(_CheckRangeLoop), 0);
        }

        float CalculateLoopTime(float value) => float.IsInfinity(value) ? value : ((Mathf.RoundToInt(value * 1000) - localRangeLoopStartMs) % rangeLoopDurationMs + localRangeLoopStartMs) * 0.001F;

#if COMPILER_UDONSHARP
        public
#endif
        void _AutoSyncTime() {
            if (!isActiveAndEnabled || isLoading || isLocalReloading || !Utilities.IsValid(activeHandler) || !activeHandler.IsReady) {
                isResyncTime = false;
                return;
            }
            var duration = activeHandler.Duration;
            if (duration > 0 && !float.IsInfinity(duration))
                SyncTime(false, true);
            else if (Networking.IsOwner(gameObject))
                ScheduleHeartbeat();
            else
                HealthCheck();
            SendCustomEventDelayedSeconds(nameof(_AutoSyncTime), 0.5F);
        }

        double CalcSyncTime(out float actualSpeed) {
            if (!Utilities.IsValid(activeHandler)) {
                actualSpeed = 1;
                return 0;
            }
            actualSpeed = activeHandler.Speed;
            var duration = activeHandler.Duration;
            if (duration <= 0 || float.IsInfinity(duration)) return 0;
            var videoTime = Mathf.Repeat(activeHandler.Time, duration);
            double syncTime = videoTime / actualSpeed - syncOffset + PerformerLatency;
            if (activeHandler.IsPlaying) {
                long serverTimeMs = Networking.GetServerTimeInMilliseconds() - (long)(syncTime * 1000);
                if (serverTimeMs > int.MaxValue) serverTimeMs -= uint.MaxValue;
                syncTime = serverTimeMs * 0.001;
            }
            if (synced) syncedActualSpeed = actualSpeed;
            return syncTime;
        }

        float CalcVideoTime() {
            if (!Utilities.IsValid(activeHandler)) return 0;
            var duration = activeHandler.Duration;
            if (duration <= 0 || float.IsInfinity(duration)) return 0;
            float videoTime;
            int intState = state;
            switch (intState) {
                case PLAYING: videoTime = (float)((Networking.CalculateServerDeltaTime(Networking.GetServerTimeInSeconds(), time * 0.001) + syncOffset + syncLatency - PerformerLatency) * actualSpeed); break;
                case PAUSED: videoTime = time * 0.001F; break;
                default: return 0;
            }
            return loop ? Mathf.Repeat(videoTime, duration) : Mathf.Clamp(videoTime, 0, duration);
        }

        void SyncTime(bool forced, bool doHealthCheck) {
            if (Networking.IsOwner(gameObject)) {
                if (!forced && Utilities.IsValid(activeHandler) && IsRangeLooping) {
                    var halfRange = rangeLoopDuration / 2;
                    var diff = CalculateLoopTime(CalcVideoTime()) - activeHandler.Time;
                    if (diff > halfRange) diff -= rangeLoopDuration;
                    else if (diff < -halfRange) diff += rangeLoopDuration;
                    if (Mathf.Abs(diff) / activeHandler.Speed < timeDriftDetectThreshold) return;
                    forced = true;
                }
                var newTime = CalcSyncTime(out float speed);
                if (forced || Mathf.Abs((float)Networking.CalculateServerDeltaTime(newTime, time * 0.001)) >= timeDriftDetectThreshold) {
                    time = (int)(newTime * 1000);
                    actualSpeed = speed;
                    RequestSerialization();
                } else if (doHealthCheck) ScheduleHeartbeat();
            } else {
                var duration = activeHandler.Duration;
                if (duration <= 0 || float.IsInfinity(duration)) return;
                float t = CalcVideoTime();
                var t2 = activeHandler.Time;
                // Is playing + time (progress) not changed since last check = video is buffering or paused
                // Do not align the time this case as it may cause current non-ready buffer to be discard and rebuffered
                // causing the video to be stuck in buffering state.
                bool isPlaying = activeHandler.IsPlaying;
                if (!isPlaying || t2 != lastSyncRawTime) {
                    // If it was buffering, we shift the time a bit further, estimates user loading time for catching up
                    if (isBuffering) {
                        if (isPlaying) {
                            float tCatchup = t2 - lastSyncRawTime;
                            if (tCatchup > timeDriftDetectThreshold) t += tCatchup;
                        }
                        isBuffering = false;
                    }
                    lastSyncRawTime = t2;
                    if (!forced) {
                        if (loop) t2 = Mathf.Repeat(t2, duration);
                        forced = Mathf.Abs(t2 - t) >= timeDriftDetectThreshold;
                    }
                } else
                    isBuffering = isPlaying;
                if (forced) {
                    Time = t;
                    SendEvent("_OnTimeDrift");
                }
                if (doHealthCheck) HealthCheck();
            }
        }

        void ScheduleHeartbeat() {
            // Send a signal to all other clients to indicate that this client is still active.
            var currentTime = Networking.GetServerTimeInSeconds();
            if (Networking.CalculateServerDeltaTime(currentTime, lastHealthCheckTime) >= HEALTH_CHECK_SEND_INTERVAL) {
                lastHealthCheckTime = currentTime;
                SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(Heartbeat));
            }
        }

        void HealthCheck() {
            // If current owner is unresponsive, take over ownership to ensure an active client to do progress timekeep.
            // We add some randomness to stagger the health check intervals.
            var currentTime = Networking.GetServerTimeInSeconds();
            if (Networking.CalculateServerDeltaTime(currentTime, lastHealthCheckTime) >=
                UnityEngine.Random.Range(HEALTH_CHECK_VALIDATE_INTERVAL_MIN, HEALTH_CHECK_VALIDATE_INTERVAL_MAX)) {
                lastHealthCheckTime = currentTime;
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }

        // If get notified, update the last health check time.
#if COMPILER_UDONSHARP
        public
#endif
        void Heartbeat() => lastHealthCheckTime = Networking.GetServerTimeInSeconds();

        /// <summary>
        /// Set the video player to loop in a specific range.
        /// </summary>
        /// <param name="start">The start time of the range loop in seconds. It must be non-negative and less than the end time.</param>
        /// <param name="end">The end time of the range loop in seconds. It must be greater than the start time.</param>
        public void SetRangeLoop(float start, float end) {
            var duration = Duration;
            if (duration <= 0 || float.IsInfinity(duration)) {
                _ClearRangeLoop();
                return;
            }
            SetRangeInternal(
                Mathf.RoundToInt(Mathf.Clamp(start, 0, end) * 1000),
                Mathf.RoundToInt(Mathf.Clamp(end, start, duration) * 1000)
            );
            RequestSync();
        }

        void SetRangeInternal(int start, int end) {
            if (localRangeLoopStartMs == start && localRangeLoopEndMs == end) return;
            bool wasRangeLoop = IsRangeLooping;
            localRangeLoopStartMs = start;
            localRangeLoopEndMs = end;
            rangeLoopDurationMs = end - start;
            localRangeLoopStart = localRangeLoopStartMs * 0.001F;
            localRangeLoopEnd = localRangeLoopEndMs * 0.001F;
            rangeLoopDuration = rangeLoopDurationMs * 0.001F;
            var isRangeLoop = IsRangeLooping;
            if (wasRangeLoop != isRangeLoop) SendEvent("_OnRangeLoopToggled");
            if (isRangeLoop) {
                SendEvent("_OnRangeLoopChange");
                StartCheckRangeLoop();
            }
        }

        /// <summary>
        /// Clear the range loop and disable looping in a specific range.
        /// </summary>
        public void _ClearRangeLoop() {
            if (!IsRangeLooping) return;
            localRangeLoopStartMs = -1;
            localRangeLoopEndMs = -1;
            rangeLoopDurationMs = 1;
            localRangeLoopStart = 0;
            localRangeLoopEnd = float.PositiveInfinity;
            rangeLoopDuration = 0;
            SendEvent("_OnRangeLoopToggled");
            RequestSync();
        }

        // Find this code useful to your project? You are welcome to adopt it.
        // If you are respectful, please kindly leave a credit that you got inspired here;
        // or you can be an asshole who just rip it off, refactor it and then claim you made it.
#if COMPILER_UDONSHARP
        public
#endif
        void _CheckRangeLoop() {
            if (!Utilities.IsValid(activeHandler) || !activeHandler.IsPlaying || localRangeLoopStartMs < 0 || localRangeLoopEndMs <= localRangeLoopStartMs) {
                isCheckingRangeLoop = false;
                return;
            }
            SendCustomEventDelayedFrames(nameof(_CheckRangeLoop), 0);
            var time = activeHandler.Time;
            if (time < localRangeLoopStart) {
                Time = localRangeLoopStart;
                SendEvent("_OnTimeDrift");
                return;
            }
            if (time > localRangeLoopEnd) {
                Time = time; // The time will be wrapped in Time's setter
                SendEvent("_OnTimeDrift");
                return;
            }
        }

        /// <summary>
        /// Request the owner to synchronize the video player state.
        /// </summary>
        /// <remarks>
        /// If synchronization is disabled, this method will have no effect.
        /// </remarks>
        public void _RequestOwnerSync() {
            isOwnerSyncRequested = false;
            if (!synced) return;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(OwnerSync));
        }

        /// <summary>
        /// Let current user be the performer of current video player playback. (Experimental)
        /// </summary>
        /// <param name="enable">Enable or disable the performance mode.</param>
        /// <remarks>
        /// Video playback time will be auto adjusted to cancel-out the latency between the performer and audience (other users).
        /// </remarks>
        public void SetOwnPerformer(bool enable) {
            if (!synced) return;
            var player = Networking.LocalPlayer;
            if (enable) {
                var newHostId = player.playerId;
                if (newHostId == performerId) return;
                PerformerId = (ushort)newHostId;
            } else if (performerId == player.playerId) {
                PerformerId = 0;
            } else return;
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(player, gameObject);
            RequestSerialization();
        }

        /// <summary>
        /// Clear the performer if the given player ID matches the current performer.
        /// </summary>
        /// <param name="playerId">The player ID to check against the current performer.</param>
        public void ClearPerformerByPlayerId(int playerId) {
            if (!synced || performerId != (ushort)playerId) return;
            PerformerId = 0;
            var localPlayer = Networking.LocalPlayer;
            if (Utilities.IsValid(localPlayer) && !Networking.IsOwner(gameObject))
                Networking.SetOwner(localPlayer, gameObject);
            RequestSerialization();
        }

#if COMPILER_UDONSHARP
#if UDON_NETWORKING_UPDATED
        [NetworkCallable]
#endif
        public
#endif
        void OwnerSync() {
            isActiveOwnerSyncRequested = false;
            if (!Networking.IsOwner(gameObject) || !synced) return;
            if ((Networking.GetNetworkDateTime() - lastSyncNetworkTime).Ticks < OWNER_SYNC_COOLDOWN_TICKS) return;
            RequestSerialization();
        }

        /// <summary>
        /// Read the start time a YouTube link asks for, such as <c>?t=90</c>, <c>&amp;t=1m30s</c> or <c>?start=90</c>.
        /// </summary>
        /// <param name="mediaUrl">The URL to read.</param>
        /// <returns>The start time in seconds, or -1 if the URL does not give one.</returns>
        float GetUrlStartTime(VRCUrl mediaUrl) {
            if (VRCUrl.IsNullOrEmpty(mediaUrl)) return -1;
            var url = mediaUrl.Get();
            if (string.IsNullOrEmpty(url)) return -1;
            int domainStart = url.IndexOf("://");
            if (domainStart < 0) return -1;
            domainStart += 3;
            int domainEnd = url.IndexOf('/', domainStart);
            if (domainEnd < 0) domainEnd = url.Length;
            var domain = url.Substring(domainStart, domainEnd - domainStart);
            if (!domain.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) &&
                !domain.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase) &&
                !domain.EndsWith("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase))
                return -1;
            for (int i = domainEnd, length = url.Length; i < length; i++) {
                char c = url[i];
                if (c != '?' && c != '&' && c != '#') continue;
                int valueStart;
                if (url.IndexOf("t=", i + 1) == i + 1) valueStart = i + 3;
                else if (url.IndexOf("start=", i + 1) == i + 1) valueStart = i + 7;
                else continue;
                int valueEnd = valueStart;
                while (valueEnd < length && url[valueEnd] != '&' && url[valueEnd] != '#') valueEnd++;
                float startTime = ParseUrlTime(url, valueStart, valueEnd);
                if (startTime >= 0) return startTime;
            }
            return -1;
        }

        /// <summary>
        /// Parse a YouTube time value, such as <c>90</c>, <c>90s</c> or <c>1h2m3s</c>.
        /// </summary>
        /// <returns>The time in seconds, or -1 if the value is not a time.</returns>
        float ParseUrlTime(string url, int start, int end) {
            if (start >= end) return -1;
            // The total is a float so that an absurd value such as 999999h cannot overflow.
            float total = 0;
            int number = 0;
            bool hasNumber = false;
            for (int i = start; i < end; i++) {
                char c = url[i];
                if (c >= '0' && c <= '9') {
                    number = number * 10 + c - '0';
                    // A value this long is not a time; also keeps the number itself from overflowing.
                    if (number > 1000000) return -1;
                    hasNumber = true;
                    continue;
                }
                if (!hasNumber) return -1;
                if (c == 'h' || c == 'H') total += number * 3600F;
                else if (c == 'm' || c == 'M') total += number * 60F;
                else if (c == 's' || c == 'S') total += number;
                else return -1;
                number = 0;
                hasNumber = false;
            }
            // A number without a unit is in seconds.
            if (hasNumber) total += number;
            return total;
        }

        /// <summary>
        /// Take the start time the loaded URL asked for, so it is applied only once.
        /// </summary>
        /// <returns>The start time in seconds, or 0 if there is none or it is outside the video.</returns>
        float TakeUrlStartTime() {
            float startTime = urlStartTime;
            urlStartTime = -1;
            if (startTime <= 0 || !Utilities.IsValid(activeHandler)) return 0;
            var duration = activeHandler.Duration;
            if (duration <= 0 || float.IsInfinity(duration) || startTime >= duration) return 0;
            return startTime;
        }

        void SyncSpeed() {
            if (Utilities.IsValid(activeHandler) && activeHandler.SupportSpeedAdjustment)
                activeHandler.Speed = speed;
            SetAudioPitch();
            SendEvent("_OnSpeedChange");
        }
    }
}
