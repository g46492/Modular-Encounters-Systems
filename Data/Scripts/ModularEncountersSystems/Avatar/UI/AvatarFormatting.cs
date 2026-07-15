using RichHudFramework;
using RichHudFramework.UI;
using VRageMath;

namespace ModularEncountersSystems.Avatar.UI {

	/// <summary>
	/// Centralized stylesheet for the avatar overlay UI.
	/// </summary>
	public static class AvatarFormatting {

		public static readonly Color ColorIdle = TerminalFormatting.MistBlue;

		public static readonly Color ColorTextInactive = new Color(255, 255, 255, 255);

		public static readonly Color ColorBackplate = TerminalFormatting.OuterSpace.SetAlphaPct(0.75f);

		// Near-black, used for text drawn directly on a saturated colored fill (e.g. the avatar
		// accent bar) - measured against the three RelationStatus colors, white text only reads well
		// on the hostile red (~4.4:1 contrast) while falling short on neutral/friendly (~2.6-2.8:1);
		// near-black reads well against all three (~4.8-8.0:1).
		public static readonly Color ColorAccentBarText = new Color(24, 24, 24);

	}

}
