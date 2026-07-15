using System.Xml.Serialization;

namespace ModularEncountersSystems.Avatar.UI {

	/// <summary>
	/// Serialization DTO for AvatarSettingsPage's persisted values (plain public fields,
	/// XML-serializable, parameterless constructor) so it round-trips through
	/// MyAPIGateway.Utilities.SerializeToXML / SerializeFromXML.
	/// </summary>
	[XmlRoot("AvatarUserConfigSettings")]
	public class AvatarUserConfigSettings {

		public float XOffset = -850f;
		public float YOffset = 320f;
		public float ScalePercent = 100f;
		public float GlitchStrengthPercent = 100f;

		// Parameterless constructor required for XML serialization
		public AvatarUserConfigSettings() { }

	}

}
