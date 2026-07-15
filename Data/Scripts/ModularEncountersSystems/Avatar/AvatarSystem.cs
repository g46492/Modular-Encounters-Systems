using ModularEncountersSystems.API;
using ModularEncountersSystems.Avatar.UI;
using ModularEncountersSystems.Core;
using ModularEncountersSystems.Entities;
using ModularEncountersSystems.Logging;
using ModularEncountersSystems.Sync;
using ModularEncountersSystems.Tasks;
using RichHudFramework.Client;
using RichHudFramework.UI.Client;
using Sandbox.ModAPI;
using System;

namespace ModularEncountersSystems.Avatar {

	/// <summary>
	/// Single safe entry point for the NPC avatar HUD overlay. The Rich HUD Framework is a soft
	/// dependency: the bundled RHF client library stays completely inert unless the RHF Master mod
	/// (workshop 1965654081) is detected in the session, so every method here is safe to call from
	/// anywhere in MES regardless of whether RHF is installed, whether this is a dedicated server,
	/// or whether the display previously failed. Failures are logged through SpawnLogger and, after
	/// repeated exceptions, permanently disable the display for the session (circuit breaker) so a
	/// broken overlay can never take the rest of MES down with it.
	/// </summary>
	public static class AvatarSystem {

		//How many caught exceptions before the display permanently disables for this session.
		private const int MaxFaults = 3;

		private const int PreviewCount = 3;
		private const double SpeakingCycleMs = 3000.0;

		private static bool _setupDone;
		private static bool _initCalled;
		private static bool _faulted;
		private static int _faultCount;

		private static AvatarSettingsPage _settingsPage;
		private static AvatarOverlayElement _overlay;
		private static readonly AvatarPreviewElement[] _previews = new AvatarPreviewElement[PreviewCount];

		private static bool _lastShowPreview;
		private static int _speakingIndex;
		private static double _speakingCycleStartMs = -1.0;

		/// <summary>
		/// True only when the local client can actually render an avatar right now: RHF Master
		/// detected, not a dedicated server, client registration completed, and the circuit breaker
		/// has not tripped. Server-side senders do not need to check this - it describes the local
		/// display only.
		/// </summary>
		public static bool CanDisplay {

			get { return AddonManager.RichHudFramework && !MES_SessionCore.IsDedicated && !_faulted && RichHudClient.Registered; }

		}

		/// <summary>
		/// Called once from MES_SessionCore.LoadData (after AddonManager.DetectAddons). Does nothing
		/// on dedicated servers or when the RHF Master mod is absent - in that case the bundled RHF
		/// client code is never activated at all.
		/// </summary>
		public static void Setup() {

			if (_setupDone)
				return;

			_setupDone = true;

			if (MES_SessionCore.IsDedicated)
				return;

			if (!AddonManager.RichHudFramework) {

				SpawnLogger.Write("Rich HUD Framework Not Detected. Avatar Display Will Remain Inactive.", SpawnerDebugEnum.Startup);
				return;

			}

			//Deferred to the first Tick10 run (session already simulating) instead of calling
			//RichHudClient.Init from LoadData directly - RHF Master may not have loaded yet, and its
			//client-side registration queue is only reliable once the session is running.
			TaskProcessor.Tick10.Tasks += TryInitClient;
			MES_SessionCore.UnloadActions += Close;

		}

		private static void TryInitClient() {

			TaskProcessor.Tick10.Tasks -= TryInitClient;

			if (_initCalled || _faulted)
				return;

			_initCalled = true;

			try {

				PresentationQueueManager.Initialize();
				RichHudClient.Init("Modular Encounters Systems", HudInit, ClientReset);
				SpawnLogger.Write("RichHudClient Init Requested.", SpawnerDebugEnum.Startup);

			} catch (Exception exc) {

				Fault("TryInitClient", exc);

			}

		}

		/// <summary>
		/// Invoked by RichHudClient once registration with the framework succeeds.
		/// Only safe to touch HudMain/RichHudTerminal from this point on.
		/// </summary>
		private static void HudInit() {

			try {

				SpawnLogger.Write("RichHudClient Registered. Constructing Avatar Overlay.", SpawnerDebugEnum.Startup);

				_settingsPage = new AvatarSettingsPage();
				_settingsPage.Register();

				_overlay = new AvatarOverlayElement(HudMain.HighDpiRoot, _settingsPage);

				_previews[0] = new AvatarPreviewElement(HudMain.HighDpiRoot, _settingsPage, 0,
					"Enenra", "Enenra", "Chief Digital Beauty and Function Officer", RelationStatus.Hostile, "PirateIcon");

				_previews[1] = new AvatarPreviewElement(HudMain.HighDpiRoot, _settingsPage, 1,
					"CptArthur", "Captain Arthur", "Chief Scenario and Planet Crafter", RelationStatus.Friendly, "BuilderIcon_3");

				_previews[2] = new AvatarPreviewElement(HudMain.HighDpiRoot, _settingsPage, 2,
					"Uzar", "Uzar", "Bloke in the Engine Room", RelationStatus.Neutral, "MinerIcon_1");

				TaskProcessor.Tick1.Tasks += UpdateClientVisuals;

			} catch (Exception exc) {

				Fault("HudInit", exc);

			}

		}

		/// <summary>
		/// Invoked on client reset (session unload, unhandled exception inside RHF, or manual
		/// RichHudClient.Reset()). All RHF-backed elements are invalid past this point.
		/// </summary>
		private static void ClientReset() {

			TaskProcessor.Tick1.Tasks -= UpdateClientVisuals;

			_overlay = null;

			for (int i = 0; i < _previews.Length; i++)
				_previews[i] = null;

			_settingsPage = null;

		}

		/// <summary>
		/// Displays a transmission on the local client (or clears the feeds when the transmission's
		/// ClearActiveFeeds flag is set). Safe no-op when the display is unavailable. This is the
		/// method MES logic (and SyncManager, for transmissions arriving from the server) should call.
		/// </summary>
		public static void ShowTransmission(AvatarTransmission transmission) {

			if (transmission == null || !CanDisplay)
				return;

			try {

				if (transmission.ClearActiveFeeds) {

					PresentationQueueManager.Instance?.ClearAll();
					return;

				}

				PresentationQueueManager.Instance?.Present(transmission);

			} catch (Exception exc) {

				Fault("ShowTransmission", exc);

			}

		}

		/// <summary>
		/// Clears the active feed and pending queue on the local client. Safe no-op when the display
		/// is unavailable.
		/// </summary>
		public static void ClearFeeds() {

			if (!CanDisplay)
				return;

			try {

				PresentationQueueManager.Instance?.ClearAll();

			} catch (Exception exc) {

				Fault("ClearFeeds", exc);

			}

		}

		/// <summary>
		/// Server-side: sends a transmission to one player (steamId != 0) or to every online player
		/// with an active character/entity (steamId == 0) using MES's built-in sync channel. Never
		/// touches RHF, so it is safe on dedicated servers; each receiving client decides for itself
		/// whether it can display the avatar. In single player / listen-server hosts the message
		/// loops back through the same sync path other MES effects already use.
		/// </summary>
		public static void SendTransmission(AvatarTransmission transmission, ulong steamId = 0) {

			if (transmission == null || !MES_SessionCore.IsServer)
				return;

			try {

				var container = new SyncContainer(transmission);

				if (steamId != 0) {

					SyncManager.SendSyncMesage(container, steamId);
					return;

				}

				foreach (var player in PlayerManager.Players) {

					if (player.ActiveEntity())
						SyncManager.SendSyncMesage(container, player.Player.SteamUserId);

				}

			} catch (Exception exc) {

				Fault("SendTransmission", exc);

			}

		}

		/// <summary>
		/// Server-side: clears the avatar feeds on one player's client (steamId != 0) or on every
		/// online player's client (steamId == 0).
		/// </summary>
		public static void SendClearFeeds(ulong steamId = 0) {

			var clear = new AvatarTransmission();
			clear.ClearActiveFeeds = true;
			SendTransmission(clear, steamId);

		}

		/// <summary>
		/// Runs every tick (Tick1) once HudInit has succeeded. Continuously reflects the settings
		/// page's "Display Placeholders" toggle onto the preview avatars' Visible flags - set
		/// unconditionally every tick (not just on a state change), so the previews never depend on
		/// their own Layout() running while invisible to notice they should wake up. Only one preview
		/// is ever visible at a time, rotating to the next example avatar every SpeakingCycleMs while
		/// shown, reset to the first avatar each time the preview turns on.
		/// </summary>
		private static void UpdateClientVisuals() {

			if (_faulted || _settingsPage == null)
				return;

			try {

				_settingsPage.UpdateTerminalState();
				bool showPreview = _settingsPage.DisplayPlaceholders;

				bool justToggledOn = showPreview && !_lastShowPreview;
				_lastShowPreview = showPreview;

				// Reset to a clean, predictable starting point whenever the preview turns on, rather
				// than continuing mid-cycle from last time.
				if (justToggledOn) {

					_speakingIndex = 0;
					_speakingCycleStartMs = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;

				}

				if (showPreview) {

					double nowMs = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;

					if (nowMs - _speakingCycleStartMs >= SpeakingCycleMs) {

						_speakingCycleStartMs = nowMs;
						_speakingIndex = (_speakingIndex + 1) % _previews.Length;

					}

				}

				for (int i = 0; i < _previews.Length; i++) {

					if (_previews[i] == null)
						continue;

					_previews[i].Visible = showPreview && (i == _speakingIndex);

				}

			} catch (Exception exc) {

				Fault("UpdateClientVisuals", exc);

			}

		}

		private static void Fault(string context, Exception exc) {

			_faultCount++;
			SpawnLogger.Write("Exception in AvatarSystem." + context, SpawnerDebugEnum.Error, true);
			SpawnLogger.Write(exc.ToString(), SpawnerDebugEnum.Error, true);

			if (_faultCount >= MaxFaults && !_faulted) {

				_faulted = true;
				TaskProcessor.Tick1.Tasks -= UpdateClientVisuals;
				SpawnLogger.Write("MES Avatar Display Encountered Repeated Errors and Has Been Disabled For This Session.", SpawnerDebugEnum.Error, true);

			}

		}

		private static void Close() {

			try {

				TaskProcessor.Tick10.Tasks -= TryInitClient;
				TaskProcessor.Tick1.Tasks -= UpdateClientVisuals;
				PresentationQueueManager.Shutdown();

				if (RichHudClient.Registered)
					RichHudClient.Reset();

			} catch (Exception exc) {

				SpawnLogger.Write("Exception in AvatarSystem.Close", SpawnerDebugEnum.Error, true);
				SpawnLogger.Write(exc.ToString(), SpawnerDebugEnum.Error, true);

			}

		}

	}

}
