using System;
using UnityEngine;
using VRC.SDKBase;
using UdonSharp;
using JLChnToZ.VRC.Foundation;
using JLChnToZ.VRC.Foundation.I18N;
using VVMW.ThirdParties.Yttl;

namespace JLChnToZ.VRC.VVMW {
    public partial class Core {
        [Locatable(
            InstaniatePrefabPath = "Packages/idv.jlchntoz.vvmw/Prefabs/Third-Parties/YTTL/YTTL Manager.prefab",
            InstaniatePrefabPosition = LocatableAttribute.InstaniatePrefabHierachyPosition.First
        ), SerializeField, LocalizedLabel]
        YttlManager yttl;
#if COMPILER_UDONSHARP
        [NonSerialized, FieldChangeCallback(nameof(URL))] public
#endif
        VRCUrl url = VRCUrl.Empty;
        /// <summary>
        /// The author of the video. This may be custom assigned or fetched from the video.
        /// Directly setting this value is unsupported, use <see cref="SetTitle"/> instead.
        /// </summary>
        [NonSerialized, FieldChangeCallback(nameof(Author))]
        public string author = "";
        /// <summary>
        /// The title of the video. This may be custom assigned or fetched from the video.
        /// Directly setting this value is unsupported, use <see cref="SetTitle"/> instead.
        /// </summary>
        [NonSerialized, FieldChangeCallback(nameof(Title))]
        public string title = "";
        /// <summary>
        /// View count of the video. This is fetched from the video.
        /// Directly setting this value is unsupported.
        /// </summary>
        [NonSerialized, FieldChangeCallback(nameof(ViewCount))]
        public string viewCount = "";
        /// <summary>
        /// Description of the video. This is fetched from the video.
        /// Directly setting this value is unsupported.
        /// </summary>
        [NonSerialized, FieldChangeCallback(nameof(Description))]
        public string description = "";
        bool hasCustomTitle;

        VRCUrl URL {
            get => url;
            set => url = value ?? VRCUrl.Empty;
        }

        string Title {
            get => title;
            set {
                if (hasCustomTitle || !IsYttlDataForLocalUrl()) return;
                title = value;
            }
        }

        string Author {
            get => author;
            set {
                if (hasCustomTitle || !IsYttlDataForLocalUrl()) return;
                author = value;
            }
        }

        string ViewCount {
            get => viewCount;
            set {
                if (hasCustomTitle || !IsYttlDataForLocalUrl()) return;
                viewCount = value;
            }
        }

        string Description {
            get => description;
            set {
                if (hasCustomTitle || !IsYttlDataForLocalUrl()) return;
                description = value;
            }
        }

#if COMPILER_UDONSHARP
        public void Yttl_OnDataLoaded() => SendEvent("_OnTitleData");
#endif

        /// <summary>
        /// Sets the title and author of the video to be displayed.
        /// </summary>
        /// <param name="title">The title of the video.</param>
        /// <param name="author">The author of the video.</param>
        public void SetTitle(string title, string author) {
            hasCustomTitle = true;
            url = VRCUrl.Empty;
            this.title = title;
            this.author = author;
            description = "";
            viewCount = "";
            SendEvent("_OnTitleData");
        }

        /// <summary>
        /// Clears the custom displayed title and author of the video.
        /// </summary>
        public void _ResetTitle() {
            if (!hasCustomTitle) return;
            hasCustomTitle = false;
            url = VRCUrl.Empty;
            if (!Utilities.IsValid(yttl)) {
                author = "";
                title = "";
                viewCount = "";
                description = "";
            } else
                LoadYTTL();
            SendEvent("_OnTitleData");
        }

        void LoadYTTL() {
            if (!Utilities.IsValid(yttl) || hasCustomTitle || IsYttlDataForLocalUrl()) return;
            author = "";
            title = "";
            viewCount = "";
            description = "";
            if (Utilities.IsValid(localUrl))
                yttl.LoadData(localUrl, this);
        }

        bool IsYttlDataForLocalUrl() {
            if (url.Equals(localUrl)) return true;
            if (VRCUrl.IsNullOrEmpty(url) || VRCUrl.IsNullOrEmpty(localUrl)) return false;
            return TryGetYoutubeVideoId(url.Get(), out var urlVideoId) &&
                TryGetYoutubeVideoId(localUrl.Get(), out var localVideoId) &&
                urlVideoId == localVideoId;
        }

        bool TryGetYoutubeVideoId(string urlStr, out string videoId) {
            videoId = string.Empty;
            if (string.IsNullOrEmpty(urlStr)) return false;
            int schemeIndex = urlStr.IndexOf("://");
            if (schemeIndex < 0) return false;

            int hostStart = schemeIndex + 3;
            int pathStart = urlStr.IndexOf("/", hostStart);
            int queryStart = urlStr.IndexOf("?", hostStart);
            int hostEnd = pathStart >= 0 ? pathStart : queryStart >= 0 ? queryStart : urlStr.Length;
            var host = urlStr.Substring(hostStart, hostEnd - hostStart);
            int portIndex = host.IndexOf(":");
            if (portIndex >= 0) host = host.Substring(0, portIndex);
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                host = host.Substring(4);

            if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
                return TryGetPathSegment(urlStr, pathStart, out videoId);

            if (!host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
                return false;

            if (TryGetQueryValue(urlStr, "v=", hostEnd, out videoId))
                return true;

            int shortsIndex = pathStart >= 0 ?
                urlStr.IndexOf("/shorts/", pathStart, StringComparison.OrdinalIgnoreCase) : -1;
            if (shortsIndex >= 0)
                return TryGetPathSegment(urlStr, shortsIndex + "/shorts".Length, out videoId);

            return false;
        }

        bool TryGetPathSegment(string urlStr, int pathStart, out string segment) {
            segment = string.Empty;
            if (pathStart < 0 || pathStart + 1 >= urlStr.Length) return false;
            int segmentStart = pathStart + 1;
            int segmentEnd = urlStr.Length;
            int slashIndex = urlStr.IndexOf("/", segmentStart);
            if (slashIndex >= 0 && slashIndex < segmentEnd) segmentEnd = slashIndex;
            int queryIndex = urlStr.IndexOf("?", segmentStart);
            if (queryIndex >= 0 && queryIndex < segmentEnd) segmentEnd = queryIndex;
            int hashIndex = urlStr.IndexOf("#", segmentStart);
            if (hashIndex >= 0 && hashIndex < segmentEnd) segmentEnd = hashIndex;
            if (segmentEnd <= segmentStart) return false;
            segment = urlStr.Substring(segmentStart, segmentEnd - segmentStart);
            return true;
        }

        bool TryGetQueryValue(string urlStr, string key, int startIndex, out string value) {
            value = string.Empty;
            int queryIndex = urlStr.IndexOf("?", startIndex);
            if (queryIndex < 0 || queryIndex + 1 >= urlStr.Length) return false;

            int keyIndex = urlStr.IndexOf(key, queryIndex + 1, StringComparison.OrdinalIgnoreCase);
            while (keyIndex >= 0) {
                if (keyIndex == queryIndex + 1 || urlStr[keyIndex - 1] == '&') {
                    int valueStart = keyIndex + key.Length;
                    int valueEnd = urlStr.Length;
                    int ampIndex = urlStr.IndexOf("&", valueStart);
                    if (ampIndex >= 0 && ampIndex < valueEnd) valueEnd = ampIndex;
                    int hashIndex = urlStr.IndexOf("#", valueStart);
                    if (hashIndex >= 0 && hashIndex < valueEnd) valueEnd = hashIndex;
                    if (valueEnd <= valueStart) return false;
                    value = urlStr.Substring(valueStart, valueEnd - valueStart);
                    return true;
                }
                keyIndex = urlStr.IndexOf(key, keyIndex + key.Length, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
    }
}
