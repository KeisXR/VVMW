using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JLChnToZ.VRC.VVMW {
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class LyricsReportInteract : UdonSharpBehaviour {
        [SerializeField] UIHandler target;

        public UIHandler Target {
            get => target;
            set => target = value;
        }

        public override void Interact() {
            if (Utilities.IsValid(target)) target._ReportBadLyrics();
        }
    }
}
