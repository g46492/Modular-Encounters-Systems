using RichHudFramework.UI;
using RichHudFramework.UI.Rendering;
using Sandbox.ModAPI;
using System;
using VRage.Utils;
using VRageMath;

namespace ModularEncountersSystems.Avatar.UI {

	/// <summary>
	/// Renders a portrait texture as a stack of horizontal strips, each with a small chance per frame
	/// of a random horizontal jitter - a "comm glitch" effect. Uses RHF's quad primitives, bypassing
	/// MatBoard (which recomputes its UV rect from Material.UVBounds on every Draw() call and so can't
	/// hold a per-slice crop) in favor of BillBoardUtils.AddQuad directly, same as MatBoard uses internally.
	/// </summary>
	public class GlitchPortraitBox : HudElementBase {

		private const int SliceCount = 12;
		private const float BaseGlitchChance = 0.06f;
		private const float BaseMaxJitterPixels = 6f;

		// System.Random()'s parameterless constructor seeds from a low-resolution system clock -
		// constructing several instances back-to-back (e.g. one GlitchPortraitBox per preview slot,
		// all built in the same HudInit() call) can hand them identical seeds, making every avatar
		// glitch in perfect lockstep instead of independently. Decorrelate using the (whitelisted)
		// game clock plus a per-instance counter, rather than Environment.TickCount, which the SE
		// whitelist analyzer prohibits.
		private static int _instanceCount;

		public Color Color { get; set; }

		// Multiplier on BaseGlitchChance/BaseMaxJitterPixels - 1.0 reproduces the original hardcoded
		// behavior, 0 disables the effect entirely, driven live from AvatarSettingsPage's "Glitch
		// Strength" slider so it can be tuned without a rebuild. Negative input is clamped away
		// rather than validated, since a slider-driven value should never be negative anyway.
		public float GlitchStrength { get; set; } = 1f;

		private readonly Random _rand;
		private MyStringId _textureId;
		private bool _hasPortrait;

		public GlitchPortraitBox(HudParentBase parent) : base(parent) {

			int seed = (int)MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds + (_instanceCount++ * 7919);
			_rand = new Random(seed);
			Color = Color.White;
			Size = new Vector2(128f);

		}

		public GlitchPortraitBox() : this(null) { }

		/// <summary>
		/// Assigns the transparent-material subtype to slice and render. Pass null/empty to blank the portrait.
		/// </summary>
		public void SetPortrait(string subtypeId) {

			if (string.IsNullOrWhiteSpace(subtypeId)) {

				_hasPortrait = false;
				return;

			}

			_textureId = MyStringId.GetOrCompute(subtypeId);
			_hasPortrait = true;

		}

		protected override void Draw() {

			if (!_hasPortrait || Color.A == 0)
				return;

			Vector2 halfSize = UnpaddedSize * .5f;
			float left = Position.X - halfSize.X;
			float right = Position.X + halfSize.X;
			float top = Position.Y + halfSize.Y;
			float sliceHeight = UnpaddedSize.Y / SliceCount;

			BoundingBox2? mask = MaskingBox;
			MatrixD[] matrixRef = HudSpace.PlaneToWorldRef;
			Color tint = Color;

			float strength = Math.Max(0f, GlitchStrength);
			float effectiveChance = BaseGlitchChance * strength;
			float effectiveJitter = BaseMaxJitterPixels * strength;

			for (int i = 0; i < SliceCount; i++) {

				float sliceTop = top - i * sliceHeight;
				float sliceBottom = sliceTop - sliceHeight;

				float offsetX = 0f;
				if (_rand.NextDouble() < effectiveChance)
					offsetX = (float)(_rand.NextDouble() - 0.5) * effectiveJitter;

				BoundingBox2 sliceBounds = new BoundingBox2(
					new Vector2(left + offsetX, sliceBottom),
					new Vector2(right + offsetX, sliceTop));

				float vMin = i / (float)SliceCount;
				float vMax = (i + 1) / (float)SliceCount;

				var quadBoard = new QuadBoard(
					_textureId,
					new BoundingBox2(new Vector2(0f, vMin), new Vector2(1f, vMax)),
					tint);

				FlatQuad quad = new FlatQuad(
					sliceBounds.Max,
					new Vector2(sliceBounds.Max.X, sliceBounds.Min.Y),
					sliceBounds.Min,
					new Vector2(sliceBounds.Min.X, sliceBounds.Max.Y));

				BillBoardUtils.AddQuad(ref quad, ref quadBoard.materialData, matrixRef, mask);

			}

		}

	}

}
