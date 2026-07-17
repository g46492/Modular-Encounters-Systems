using ModularEncountersSystems.Logging;
using ModularEncountersSystems.Sync;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;

namespace ModularEncountersSystems.Avatar {

	/// <summary>
	/// Singleton coordinator for incoming avatar transmissions. Holds one active
	/// <see cref="AvatarTransmission"/> plus a FIFO queue of pending ones, and decides whether a new
	/// transmission pre-empts the active feed or waits in line based on the active feed's
	/// <see cref="AvatarTransmission.Interruptible"/> flag. Fed by <see cref="AvatarSystem"/>.
	/// </summary>
	public class PresentationQueueManager {

		public static PresentationQueueManager Instance { get; private set; }

		public static void Initialize() {

			if (Instance != null)
				return;

			Instance = new PresentationQueueManager();

		}

		public static void Shutdown() {

			Instance = null;

		}

		private readonly Queue<AvatarTransmission> _pendingQueue = new Queue<AvatarTransmission>();
		private AvatarTransmission _activePacket;
		private float _activeRemainingMs;
		private double _lastTickMs = -1.0;

		/// <summary>
		/// The feed currently being displayed, or null when nothing is active.
		/// </summary>
		public AvatarTransmission ActivePacket { get { return _activePacket; } }

		public int QueuedCount { get { return _pendingQueue.Count; } }

		/// <summary>
		/// Fired only on the transition from "nothing active" to "something active" - i.e. exactly
		/// when a dormant presentation needs to be woken up. Not fired when one active feed simply
		/// replaces another (the display is already running in that case). The overlay element uses
		/// this to flip its own Visible on directly, since an invisible element's Layout() never runs
		/// on its own to notice new state.
		/// </summary>
		public event Action FeedActivated;

		private PresentationQueueManager() {

			SpawnLogger.Write("[PresentationQueueManager] Initialized.", SpawnerDebugEnum.Dev);

		}

		/// <summary>
		/// Presents a transmission: takes over the display immediately if nothing is active or the
		/// active feed allows interruption, otherwise queues it FIFO.
		/// </summary>
		public void Present(AvatarTransmission packet) {

			if (packet == null)
				return;

			bool wasInactive = _activePacket == null;

			if (wasInactive || _activePacket.Interruptible) {

				SpawnLogger.Write("[PresentationQueueManager] Activating transmission immediately: portrait='" + packet.PortraitSubtypeId + "' speaker='" + packet.SpeakerName + "'.", SpawnerDebugEnum.Dev);
				Activate(packet);

				if (wasInactive) {

					Action handler = FeedActivated;
					if (handler != null)
						handler();

				}

			} else {

				_pendingQueue.Enqueue(packet);
				SpawnLogger.Write("[PresentationQueueManager] Active feed not interruptible; queued transmission. Queue length now " + _pendingQueue.Count + ".", SpawnerDebugEnum.Dev);

			}

		}

		/// <summary>
		/// Ends a specific transmission and advances to the next queued one - the "end command" of
		/// the start/end lifecycle (e.g. fired when the voice line audio backing the transmission
		/// stops playing). Takes the transmission instance rather than blindly ending whatever is
		/// active: if the caller's transmission has already been replaced or expired, this is a
		/// no-op, so a late audio-end signal can never cut short an unrelated newer feed.
		/// </summary>
		public void EndActive(AvatarTransmission packet) {

			if (packet == null || !object.ReferenceEquals(_activePacket, packet))
				return;

			AdvanceQueue();

		}

		/// <summary>
		/// Clears the active feed and every pending transmission.
		/// </summary>
		public void ClearAll() {

			_pendingQueue.Clear();
			_activePacket = null;
			_activeRemainingMs = 0f;

		}

		private void Activate(AvatarTransmission packet) {

			_activePacket = packet;
			_activeRemainingMs = packet.DurationMS;

			// Reset the tick baseline here rather than relying on Tick() having run continuously:
			// Tick() is only called from the overlay's Layout(), which does not run at all while
			// the overlay is invisible/dormant. Without this, the first Tick() after a long sleep
			// would see a huge elapsed delta and could expire the freshly-activated feed instantly.
			_lastTickMs = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;

		}

		private void AdvanceQueue() {

			if (_pendingQueue.Count > 0)
				Activate(_pendingQueue.Dequeue());
			else {

				_activePacket = null;
				_activeRemainingMs = 0f;

			}

		}

		/// <summary>
		/// Advances the active feed's countdown. Expected to be called once per render frame
		/// (e.g. from the overlay element's Layout()); computes its own delta time internally
		/// so callers don't need to track timing themselves.
		/// </summary>
		public void Tick() {

			double nowMs = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;

			if (_lastTickMs < 0.0) {

				_lastTickMs = nowMs;
				return;

			}

			float deltaMs = (float)(nowMs - _lastTickMs);
			_lastTickMs = nowMs;

			// DurationMS <= 0 means "until interrupted or cleared" - never auto-expires.
			if (_activePacket != null && _activeRemainingMs > 0f) {

				_activeRemainingMs -= deltaMs;

				if (_activeRemainingMs <= 0f)
					AdvanceQueue();

			}

		}

	}

}
