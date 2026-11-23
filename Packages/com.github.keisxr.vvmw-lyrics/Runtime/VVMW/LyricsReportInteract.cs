using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JLChnToZ.VRC.VVMW {
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class LyricsReportInteract : UdonSharpBehaviour {
        [SerializeField] UIHandler target;
        [SerializeField] int action;
        [SerializeField] float offsetDelta;

        public UIHandler Target {
            get => target;
            set => target = value;
        }

        public int Action {
            get => action;
            set => action = value;
        }

        public float OffsetDelta {
            get => offsetDelta;
            set => offsetDelta = value;
        }

        public override void Interact() {
            if (!Utilities.IsValid(target)) return;
            if (action == 1)
                target.AdjustLyricsOffset(offsetDelta);
            else
                target._ReportBadLyrics();
        }
    }
}
