using VRageMath;

namespace ModularEncountersSystems.Avatar {

	/// <summary>
	/// Coarse relationship status conveyed by the avatar card's accent bar. Fixed color/label per
	/// status rather than an arbitrary caller-supplied color, so the accent bar's meaning stays
	/// consistent regardless of which system is driving it - it only ever needs to express one of
	/// these three states anyway.
	/// </summary>
	public enum RelationStatus {

		Hostile,
		Neutral,
		Friendly

	}

	public static class RelationStatusFormatting {

		public static Color GetColor(this RelationStatus status) {

			switch (status) {

				case RelationStatus.Hostile:
					return new Color(0xDD, 0x3C, 0x3C);
				case RelationStatus.Friendly:
					return new Color(0x61, 0xAC, 0x58);
				default:
					return new Color(0x90, 0xA3, 0xAD); // Neutral

			}

		}

		public static string GetLabel(this RelationStatus status) {

			switch (status) {

				case RelationStatus.Hostile:
					return "HOSTILE";
				case RelationStatus.Friendly:
					return "FRIENDLY";
				default:
					return "NEUTRAL";

			}

		}

	}

}
