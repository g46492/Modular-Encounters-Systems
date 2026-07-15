using ModularEncountersSystems.Logging;
using RichHudFramework.UI;

namespace ModularEncountersSystems.Avatar.UI {

	/// <summary>
	/// A single fixed-content example avatar shown in the terminal as a live preview of the overlay
	/// while the player has the "Display Placeholders" checkbox on (on the Avatar Overlay settings
	/// page), so the Scale/Offset sliders have something on screen to preview against. Manual toggle
	/// rather than auto-detecting the page being open: RichHudTerminal.Root.SelectedPage doesn't
	/// reliably report a mod's own page as selected when that mod only has one page registered.
	/// Visual construction/layout lives in <see cref="AvatarVisualElement"/>, shared with the live
	/// <see cref="AvatarOverlayElement"/> - including position: this never overrides the offset, so
	/// it always renders at the exact same spot the live overlay would, making it a faithful stand-in.
	/// <para>
	/// Only one preview is ever visible at a time - AvatarSystem cycles through however many example
	/// avatars exist, showing exactly one and hiding the rest, rather than showing several side by side.
	/// </para>
	/// <para>
	/// Unlike AvatarOverlayElement, this element's Visible flag is driven entirely from the outside
	/// (AvatarSystem sets it every simulation tick, unconditionally, based on
	/// AvatarSettingsPage.DisplayPlaceholders) rather than through a wake-up event - the trigger here
	/// is synchronous per-tick state, not an asynchronous arrival, so there's no need for the
	/// FeedActivated-style pattern AvatarOverlayElement uses. Because visibility is set continuously
	/// rather than only on a state change, this element never needs to revive itself from inside its
	/// own Layout() - it simply reflects whatever AvatarSystem told it last, every tick, regardless
	/// of whether its own Layout() happens to be running.
	/// </para>
	/// </summary>
	public class AvatarPreviewElement : AvatarVisualElement {

		private readonly int _index;

		public AvatarPreviewElement(
			HudParentBase parent,
			AvatarSettingsPage settings,
			int index,
			string portraitSubtypeId,
			string speakerName,
			string speakerTitle,
			RelationStatus relationStatus,
			string factionIconId = null)
			: base(parent, settings) {

			_index = index;

			ApplyDisplay(portraitSubtypeId, speakerName, speakerTitle, relationStatus.GetColor().ToVector4(), factionIconId, relationStatus.GetLabel());

			Visible = false;

			SpawnLogger.Write("[AvatarPreviewElement] Constructed preview " + index + " (" + speakerName + ", portrait='" + portraitSubtypeId + "', relation='" + relationStatus + "', faction='" + factionIconId + "').", SpawnerDebugEnum.Dev);

		}

		protected override void Layout() {

			base.Layout();

			// Display data is fixed for a preview instance - only positioning needs to re-run each
			// frame, so Scale/Offset slider changes are reflected live while the page is open.
			ApplyMetrics();

		}

	}

}
