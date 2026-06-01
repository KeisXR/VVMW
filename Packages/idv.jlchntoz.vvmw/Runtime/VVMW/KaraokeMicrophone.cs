using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using JLChnToZ.VRC.Foundation.I18N;

namespace JLChnToZ.VRC.VVMW.Pickups {
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [RequireComponent(typeof(VRC_Pickup))]
    [DisallowMultipleComponent]
    [AddComponentMenu("VizVid/Components/Karaoke Microphone")]
    [HelpURL("https://xtlcdn.github.io/VizVid/docs/")]
    public class KaraokeMicrophone : UdonSharpBehaviour {
        [SerializeField, LocalizedLabel] Core core;

        public override void OnPickup() {
            if (!Utilities.IsValid(core)) return;
            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer)) return;
            core.SetOwnPerformer(true);
        }

        public override void OnDrop() {
            if (!Utilities.IsValid(core)) return;
            var performer = core.Performer;
            if (Utilities.IsValid(performer) && performer.isLocal)
                core.SetOwnPerformer(false);
        }

        public override void OnPlayerLeft(VRCPlayerApi player) {
            if (!Utilities.IsValid(core) || !Utilities.IsValid(player)) return;
            core.ClearPerformerByPlayerId(player.playerId);
        }
    }
}
