using ModularEncountersSystems.Avatar;
using ProtoBuf;

namespace ModularEncountersSystems.Sync {

	/// <summary>
	/// Data for a single NPC avatar transmission shown on a client's HUD via the Rich HUD Framework
	/// overlay (see AvatarSystem). Doubles as the wire format (server -> client inside a
	/// SyncContainer with SyncMode.AvatarDisplay) and the client-side display/queue payload.
	/// </summary>
	[ProtoContract]
	public class AvatarTransmission {

		/// <summary>SubtypeId of a TransparentMaterial registered as a portrait.</summary>
		[ProtoMember(1)]
		public string PortraitSubtypeId;

		/// <summary>How long to display, in milliseconds. Zero or negative means "until interrupted
		/// or explicitly cleared" - it will never auto-expire on its own.</summary>
		[ProtoMember(2)]
		public int DurationMS;

		/// <summary>If true, a new transmission arriving while this one is active immediately
		/// replaces it. If false, new transmissions queue up (FIFO) until this one ends.</summary>
		[ProtoMember(3)]
		public bool Interruptible;

		[ProtoMember(4)]
		public string SpeakerName;

		[ProtoMember(5)]
		public string SpeakerTitle;

		/// <summary>Coarse relationship status - drives both the accent bar's fixed color and its
		/// label text. See RelationStatus for the three values and their colors.</summary>
		[ProtoMember(6)]
		public RelationStatus RelationStatus;

		/// <summary>SubtypeId of a TransparentMaterial to show as the faction badge (top-right).
		/// Null/empty hides the badge entirely.</summary>
		[ProtoMember(7)]
		public string FactionIconId;

		/// <summary>If true, this message is a command to clear the active feed and pending queue
		/// on the receiving client instead of displaying anything.</summary>
		[ProtoMember(8)]
		public bool ClearActiveFeeds;

		public AvatarTransmission() {

			PortraitSubtypeId = "";
			DurationMS = 0;
			Interruptible = true;
			SpeakerName = "";
			SpeakerTitle = "";
			RelationStatus = RelationStatus.Neutral;
			FactionIconId = "";
			ClearActiveFeeds = false;

		}

	}

}
