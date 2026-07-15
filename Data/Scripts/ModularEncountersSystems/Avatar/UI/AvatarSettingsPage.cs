using ModularEncountersSystems.Logging;
using RichHudFramework;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using Sandbox.ModAPI;
using System;

namespace ModularEncountersSystems.Avatar.UI {

	/// <summary>
	/// RichHudTerminal page letting the player reposition and rescale the avatar overlay.
	/// Construct after RichHudClient registration succeeds, then call <see cref="Register"/> once.
	/// X/Y offset and scale are persisted to global storage - see the Persistence region below.
	/// </summary>
	public class AvatarSettingsPage {

		private const string ConfigFileName = "MES_AvatarOverlay_Config.xml";

		private const float DefaultXOffset = 0f;
		private const float DefaultYOffset = 0f;
		private const float DefaultScalePercent = 100f;
		private const float DefaultGlitchStrengthPercent = 100f;

		public float XOffset { get; private set; }
		public float YOffset { get; private set; }
		public float ScalePercent { get; private set; }
		public float GlitchStrengthPercent { get; private set; }

		private readonly ControlPage _page;
		private readonly TerminalSlider _xOffsetSlider;
		private readonly TerminalTextField _xOffsetField;
		private readonly TerminalSlider _yOffsetSlider;
		private readonly TerminalTextField _yOffsetField;
		private readonly TerminalSlider _scaleSlider;
		private readonly TerminalTextField _scaleField;
		private readonly TerminalSlider _glitchStrengthSlider;
		private readonly TerminalCheckbox _displayPlaceholdersToggle;

		private bool _suppressSync;

		public AvatarSettingsPage() {

			XOffset = DefaultXOffset;
			YOffset = DefaultYOffset;
			ScalePercent = DefaultScalePercent;
			GlitchStrengthPercent = DefaultGlitchStrengthPercent;

			LoadSettings();

			_page = new ControlPage {
				Name = "Avatar Overlay",
				Enabled = true
			};

			var positionCategory = new ControlCategory {
				HeaderText = "Position",
				SubheaderText = "Reposition the NPC avatar overlay - drag the slider or type an exact value."
			};

			_xOffsetSlider = new TerminalSlider {
				Name = "X Offset",
				Min = -1000f,
				Max = 1000f,
				Value = XOffset,
				ValueText = $"{XOffset:F0}px"
			};

			_xOffsetField = new TerminalTextField {
				Name = "X Offset (exact)",
				Value = XOffset.ToString("F0"),
				CharFilterFunc = IsSignedNumericChar
			};

			WireNumericSync(_xOffsetSlider, _xOffsetField, "px", v => {
				XOffset = v;
				SaveSettings("X Offset changed");
			});

			_yOffsetSlider = new TerminalSlider {
				Name = "Y Offset",
				Min = -1000f,
				Max = 1000f,
				Value = YOffset,
				ValueText = $"{YOffset:F0}px"
			};

			_yOffsetField = new TerminalTextField {
				Name = "Y Offset (exact)",
				Value = YOffset.ToString("F0"),
				CharFilterFunc = IsSignedNumericChar
			};

			WireNumericSync(_yOffsetSlider, _yOffsetField, "px", v => {
				YOffset = v;
				SaveSettings("Y Offset changed");
			});

			positionCategory.Add(new ControlTile { _xOffsetSlider, _xOffsetField });
			positionCategory.Add(new ControlTile { _yOffsetSlider, _yOffsetField });

			var scaleCategory = new ControlCategory {
				HeaderText = "Scale",
				SubheaderText = "Overall size of the avatar overlay."
			};

			_scaleSlider = new TerminalSlider {
				Name = "Overall Scale",
				Min = 50f,
				Max = 200f,
				Value = ScalePercent,
				ValueText = $"{ScalePercent:F0}%"
			};

			_scaleField = new TerminalTextField {
				Name = "Overall Scale (exact)",
				Value = ScalePercent.ToString("F0"),
				CharFilterFunc = IsUnsignedNumericChar
			};

			WireNumericSync(_scaleSlider, _scaleField, "%", v => {
				ScalePercent = v;
				SaveSettings("Scale changed");
			});

			scaleCategory.Add(new ControlTile { _scaleSlider, _scaleField });

			var glitchCategory = new ControlCategory {
				HeaderText = "Glitch Effect",
				SubheaderText = "Intensity of the portrait's comm-glitch effect. 0% disables it, 100% is the default strength."
			};

			_glitchStrengthSlider = new TerminalSlider {
				Name = "Glitch Strength",
				Min = 0f,
				Max = 200f,
				Value = GlitchStrengthPercent,
				ValueText = $"{GlitchStrengthPercent:F0}%"
			};
			_glitchStrengthSlider.ControlChanged += (sender, args) => {
				GlitchStrengthPercent = _glitchStrengthSlider.Value;
				_glitchStrengthSlider.ValueText = $"{GlitchStrengthPercent:F0}%";
				SaveSettings("Glitch Strength changed");
			};

			glitchCategory.Add(new ControlTile { _glitchStrengthSlider });

			// RichHudTerminal.Root.SelectedPage doesn't reliably report this page as selected (a mod
			// with only one registered page never seems to populate it), so "is the overlay preview
			// relevant right now" can't be auto-detected. Manual toggle instead, auto-reset when the
			// terminal closes entirely (RichHudTerminal.Open is reliable) so it doesn't stay stuck on
			// in the background after the player leaves.
			var previewCategory = new ControlCategory {
				HeaderText = "Preview",
				SubheaderText = "Show an example avatar (cycling through all of them one at a time) while tuning the settings above."
			};

			_displayPlaceholdersToggle = new TerminalCheckbox {
				Name = "Display Placeholders",
				Value = false
			};
			_displayPlaceholdersToggle.ControlChanged += (sender, args) => {
				DisplayPlaceholders = _displayPlaceholdersToggle.Value;
			};

			previewCategory.Add(new ControlTile { _displayPlaceholdersToggle });

			_page.Add(positionCategory);
			_page.Add(scaleCategory);
			_page.Add(glitchCategory);
			_page.Add(previewCategory);

		}

		private static bool IsSignedNumericChar(char c) {

			return char.IsDigit(c) || c == '-' || c == '.';

		}

		private static bool IsUnsignedNumericChar(char c) {

			return char.IsDigit(c) || c == '.';

		}

		/// <summary>
		/// Keeps a slider and its companion exact-value text field synced in both directions without
		/// feedback loops (guarded by _suppressSync), and calls <paramref name="apply"/> with the
		/// resulting value once the two controls agree.
		/// </summary>
		private void WireNumericSync(TerminalSlider slider, TerminalTextField field, string suffix, Action<float> apply) {

			slider.ControlChanged += (sender, args) => {

				if (_suppressSync)
					return;

				_suppressSync = true;
				slider.ValueText = slider.Value.ToString("F0") + suffix;
				field.Value = slider.Value.ToString("F0");
				_suppressSync = false;

				apply(slider.Value);

			};

			field.ControlChanged += (sender, args) => {

				if (_suppressSync)
					return;

				float parsed;
				if (!float.TryParse(field.Value, out parsed))
					return;

				parsed = Math.Max(slider.Min, Math.Min(slider.Max, parsed));

				_suppressSync = true;
				slider.Value = parsed;
				slider.ValueText = parsed.ToString("F0") + suffix;
				field.Value = parsed.ToString("F0");
				_suppressSync = false;

				apply(parsed);

			};

		}

		/// <summary>
		/// Registers this page with the mod's terminal root. Call once, after RichHudClient is registered.
		/// </summary>
		public void Register() {

			RichHudTerminal.Root.Enabled = true;
			RichHudTerminal.Root.AddRange(new IModRootMember[] { _page });
			SpawnLogger.Write("[AvatarSettingsPage] Registered with terminal root.", SpawnerDebugEnum.Dev);

		}

		/// <summary>
		/// True while the "Display Placeholders" checkbox is on. Player-controlled rather than
		/// auto-detected - see the comment above the checkbox's construction for why.
		/// </summary>
		public bool DisplayPlaceholders { get; private set; }

		private bool _wasTerminalOpen;

		/// <summary>
		/// Call once per simulation tick. Auto-resets the placeholder toggle (and its visual state)
		/// the moment the terminal closes, so leaving it on doesn't leave the preview avatars stuck
		/// showing in the background indefinitely.
		/// </summary>
		public void UpdateTerminalState() {

			bool terminalOpen = RichHudTerminal.Open;

			if (_wasTerminalOpen && !terminalOpen && DisplayPlaceholders) {

				DisplayPlaceholders = false;
				_displayPlaceholdersToggle.Value = false;

			}

			_wasTerminalOpen = terminalOpen;

		}

		#region Persistence

		private void LoadSettings() {

			try {

				if (!MyAPIGateway.Utilities.FileExistsInGlobalStorage(ConfigFileName)) {

					SaveSettings("Creating with defaults from LoadSettings");
					return;

				}

				using (var reader = MyAPIGateway.Utilities.ReadFileInGlobalStorage(ConfigFileName)) {

					string xml = reader.ReadToEnd();
					var loaded = MyAPIGateway.Utilities.SerializeFromXML<AvatarUserConfigSettings>(xml);

					if (loaded == null) {

						SaveSettings("Sanitizing from LoadSettings");
						return;

					}

					XOffset = Math.Max(-1000f, Math.Min(1000f, loaded.XOffset));
					YOffset = Math.Max(-1000f, Math.Min(1000f, loaded.YOffset));
					ScalePercent = (loaded.ScalePercent > 0f) ? Math.Max(50f, Math.Min(200f, loaded.ScalePercent)) : DefaultScalePercent;
					GlitchStrengthPercent = Math.Max(0f, Math.Min(200f, loaded.GlitchStrengthPercent));

				}

			} catch (Exception ex) {

				SpawnLogger.Write("[AvatarSettingsPage] Error loading AvatarUserConfigSettings: " + ex, SpawnerDebugEnum.Error, true);

			}

		}

		public void SaveSettings(string source) {

			try {

				var settings = new AvatarUserConfigSettings {
					XOffset = XOffset,
					YOffset = YOffset,
					ScalePercent = ScalePercent,
					GlitchStrengthPercent = GlitchStrengthPercent
				};

				string xml = MyAPIGateway.Utilities.SerializeToXML(settings);

				using (var writer = MyAPIGateway.Utilities.WriteFileInGlobalStorage(ConfigFileName)) {

					writer.Write(xml);

				}

			} catch (Exception ex) {

				SpawnLogger.Write("[AvatarSettingsPage] Error saving AvatarUserConfigSettings (" + source + "): " + ex, SpawnerDebugEnum.Error, true);

			}

		}

		#endregion

	}

}
