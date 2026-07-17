using ModularEncountersSystems.Avatar;
using ModularEncountersSystems.Entities;
using ModularEncountersSystems.Logging;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;

namespace ModularEncountersSystems.Sync {

	public class ChatSoundData {

		public string SoundId;
		public string Avatar;
		public float VolumeMultiplier;
		public AvatarTransmission AvatarData;

		public ChatSoundData(string soundId, string avatar, float volume, AvatarTransmission avatarData = null) {

			SoundId = soundId;
			Avatar = avatar;
			VolumeMultiplier = volume;
			AvatarData = avatarData;

		}

	}

	public static class EffectManager {

		public static bool SoundsPending = false;
		public static bool SoundsPlaying = false;
		public static List<ChatSoundData> SoundsPendingList = new List<ChatSoundData>();

		public static IMyEntity CurrentPlayerEntity;
		public static MyEntity3DSoundEmitter SoundEmitter;
		public static bool GotFirstEmitter;

		//The transmission started for the currently playing voice line, so the audio-end poll can
		//end exactly that one (and not a newer feed that may have replaced it). See AvatarSystem.
		private static AvatarTransmission _activeAudioAvatar;

		//Safety timeout for audio-backed transmissions: audio length can't be read up front, so the
		//display is armed with max(text estimate, floor) + grace and normally ends earlier, the
		//moment the emitter reports the sound stopped. This only matters if that end signal is lost.
		private const int AudioAvatarTimeoutFloorMs = 15000;
		private const int AudioAvatarTimeoutGraceMs = 5000;

		public static void ClientReceiveEffect(Effects effectData) {

			if(effectData.Mode == EffectSyncMode.PlayerSound) {

				if (!string.IsNullOrWhiteSpace(effectData.SoundId)) {

					SoundsPendingList.Add(new ChatSoundData(effectData.SoundId, effectData.AvatarId, effectData.SoundVolume, effectData.AvatarData));
					SoundsPending = true;

				} else if (effectData.AvatarData != null) {

					//Soundless cue: its chat text already displayed on arrival, so the portrait shows
					//immediately too (it does not wait behind queued voice lines) and times out on the
					//text-length estimate the server put in DurationMS.
					AvatarSystem.ShowTransmission(effectData.AvatarData);

				}

			}

			if(effectData.Mode == EffectSyncMode.PositionSound) {

				//Logger.Write("Process Position Sound");
				ProcessPositionSoundEffect(effectData);

			}

			if (effectData.Mode == EffectSyncMode.Particle) {

				//Logger.Write("Process Particle");
				ProcessParticleEffect(effectData);

			}

		}

		public static void SendParticleEffectRequest(string id, MatrixD remoteMatrix, Vector3D offset, float scale, float maxTime, Vector3D color) {

			var effect = new Effects();
			effect.Mode = EffectSyncMode.Particle;
			effect.Coords = Vector3D.Transform(offset, remoteMatrix);
			effect.ParticleId = id;
			effect.ParticleScale = scale;
			effect.ParticleColor = color;
			effect.ParticleMaxTime = maxTime;
			effect.ParticleForwardDir = remoteMatrix.Forward;
			effect.ParticleUpDir = remoteMatrix.Up;
			var syncData = new SyncContainer(effect);

			foreach (var player in PlayerManager.Players) {

				if(player.ActiveEntity() && player.Distance(effect.Coords) <= 15000)
					SyncManager.SendSyncMesage(syncData, player.Player.SteamUserId);

			}

		}

		public static void ProcessParticleEffect(Effects effectData) {

			MyParticleEffect effect;
			var particleMatrix = MatrixD.CreateWorld(effectData.Coords, effectData.ParticleForwardDir, effectData.ParticleUpDir);
			var particleCoords = effectData.Coords;

			if (MyParticlesManager.TryCreateParticleEffect(effectData.ParticleId, ref particleMatrix, ref particleCoords, uint.MaxValue, out effect) == false) {

				return;

			}

			effect.UserScale = effectData.ParticleScale;

			if (effectData.ParticleMaxTime > 0) {

				//effect.DurationMin = effectData.ParticleMaxTime;
				//effect.DurationMax = effectData.ParticleMaxTime;

			}

			if (effectData.ParticleColor != Vector3D.Zero) {

				//var newColor = new Vector4((float)effectData.ParticleColor.X, (float)effectData.ParticleColor.Y, (float)effectData.ParticleColor.Z, 1);
				//effect.UserColorMultiplier = newColor;

			}

			effect.Velocity = effectData.Velocity;
			//effect.Loop = false;

		}

		public static void ProcessPlayerSoundEffect() {

			//Keeps polling past the last queued sound while an audio-driven avatar is still up, so
			//its end-of-audio signal isn't missed (SoundsPending drops the moment the queue empties,
			//while the final voice line is usually still playing).
			if (SoundsPending == false && _activeAudioAvatar == null) {

				return;

			}

			if(CheckPlayerSoundEmitter() == false) {

				return;

			}

			if (SoundEmitter.IsPlaying == true) {

				return;

			}

			//The voice line ended (or nothing is playing anymore): end its avatar transmission.
			//No-op inside AvatarSystem if that transmission already timed out or was replaced.
			if (_activeAudioAvatar != null) {

				AvatarSystem.EndTransmission(_activeAudioAvatar);
				_activeAudioAvatar = null;

			}

			if (SoundsPendingList.Count > 0) {

				var chatSound = SoundsPendingList[0];
				var soundPair = new MySoundPair(chatSound.SoundId);
				SoundEmitter.VolumeMultiplier = chatSound.VolumeMultiplier;
				SoundEmitter.PlaySound(soundPair, false, false, true, true, false);
				SoundsPlaying = true;
				SoundsPendingList.RemoveAt(0);

				//Start the avatar in lockstep with its voice line. DurationMS becomes the safety
				//timeout - the normal end is the emitter poll above, whichever comes first.
				if (chatSound.AvatarData != null) {

					chatSound.AvatarData.DurationMS = Math.Max(chatSound.AvatarData.DurationMS, AudioAvatarTimeoutFloorMs) + AudioAvatarTimeoutGraceMs;
					AvatarSystem.ShowTransmission(chatSound.AvatarData);
					_activeAudioAvatar = chatSound.AvatarData;

				}

			}

			if(SoundsPendingList.Count == 0){

				SoundsPlaying = false;
				SoundsPending = false;

			}

		}

		public static bool CheckPlayerSoundEmitter() {

			if(MyAPIGateway.Session.LocalHumanPlayer?.Controller?.ControlledEntity?.Entity == null) {

				CurrentPlayerEntity = null;
				SoundEmitter = null;
				SoundsPlaying = false;

				//Emitter lost mid-line: the audio is gone, so the avatar backing it ends with it.
				if (_activeAudioAvatar != null) {

					AvatarSystem.EndTransmission(_activeAudioAvatar);
					_activeAudioAvatar = null;

				}

				if (GotFirstEmitter) {

					SoundsPendingList.Clear();
					SoundsPending = false;

				}

				return false;

			}

			if(MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity.Entity == CurrentPlayerEntity) {

				return true;

			}

			CurrentPlayerEntity = MyAPIGateway.Session.LocalHumanPlayer.Character;
			SoundEmitter = new MyEntity3DSoundEmitter(CurrentPlayerEntity as MyEntity);
			GotFirstEmitter = true;
			return true;

		}

		public static void ProcessPositionSoundEffect(Effects effectData) {

			MyVisualScriptLogicProvider.PlaySingleSoundAtPosition(effectData.SoundId, effectData.Coords);

		}

	}
}
