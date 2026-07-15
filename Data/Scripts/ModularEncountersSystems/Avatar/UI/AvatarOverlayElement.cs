using ModularEncountersSystems.Logging;
using ModularEncountersSystems.Sync;
using RichHudFramework.UI;

namespace ModularEncountersSystems.Avatar.UI {

	/// <summary>
	/// Top-level HUD element for the live NPC avatar overlay, driven by <see cref="PresentationQueueManager"/>.
	/// Visual construction/layout lives in <see cref="AvatarVisualElement"/>, shared with the settings-page
	/// preview; this class only owns data-sourcing and visibility lifecycle.
	/// <para>
	/// Starts Visible = false and is woken directly by <see cref="PresentationQueueManager.FeedActivated"/>
	/// (Visible = true) the moment a feed goes from dormant to active - RHF's traversal does not call
	/// Layout() on an element that is currently invisible, so an invisible element can never revive
	/// itself from inside its own Layout() override. Once visible, Layout() runs every frame as normal,
	/// polls the queue manager for as long as something is active, and puts itself back to sleep
	/// (Visible = false) the moment nothing is - which is safe self-termination, since going dormant
	/// only requires no further wake-up until the next FeedActivated fires.
	/// </para>
	/// </summary>
	public class AvatarOverlayElement : AvatarVisualElement {

		public AvatarOverlayElement(HudParentBase parent, AvatarSettingsPage settings) : base(parent, settings) {

			Visible = false;
			PresentationQueueManager.Instance.FeedActivated += OnFeedActivated;

			SpawnLogger.Write("[AvatarOverlayElement] Constructed.", SpawnerDebugEnum.Dev);

		}

		public AvatarOverlayElement() : this(null, null) { }

		private void OnFeedActivated() {

			Visible = true;

		}

		protected override void Layout() {

			base.Layout();

			PresentationQueueManager.Instance.Tick();
			AvatarTransmission active = PresentationQueueManager.Instance.ActivePacket;

			if (active != null) {

				ApplyDisplay(active.PortraitSubtypeId, active.SpeakerName, active.SpeakerTitle, active.RelationStatus.GetColor().ToVector4(), active.FactionIconId, active.RelationStatus.GetLabel());

			} else {

				Visible = false;

			}

			ApplyMetrics();

		}

	}

}
