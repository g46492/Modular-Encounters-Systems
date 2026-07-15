using RichHudFramework;
using RichHudFramework.UI;
using RichHudFramework.UI.Rendering;
using System;
using VRageMath;

namespace ModularEncountersSystems.Avatar.UI {

	/// <summary>
	/// Shared visual construction and layout for a single avatar display: a relation-tinted backdrop
	/// behind the glitching portrait, a name/title caption, an AvatarAccentBar strip below the title
	/// carrying the relation status color, a transmitting/speaker badge top-left, and an optional
	/// faction badge top-right. Subclasses supply the data (a live feed vs. fixed example data) so
	/// <see cref="AvatarOverlayElement"/> (the live feed) and <see cref="AvatarPreviewElement"/> (the
	/// settings-page preview) don't duplicate ~150 lines of positioning math - both always render at
	/// the same position (the settings' X/Y offset, unmodified), so the preview is a faithful
	/// stand-in for what the live overlay will look like.
	/// </summary>
	public abstract class AvatarVisualElement : HudElementBase {

		private const float BasePortraitSize = 128f;
		private const float BaseTextLineHeight = 22f;
		// Shared with BaseGapBelowPortrait so every gap in the card (portrait-to-name,
		// name-to-title, title-to-accent-bar) reads as the same uniform rhythm.
		private const float BaseTextSpacing = 3f;
		private const float BaseGapBelowPortrait = 3f;
		// Shared by the faction icon (+ its backdrop) and the speaker icon - the two badges must be
		// the exact same size and mirror each other's offset across the top corners.
		private const float BaseBadgeIconSize = 32f;

		// Faction icon artwork is drawn smaller than its round backdrop (unlike the speaker icon,
		// which fills its badge) so there's breathing room between the icon and the backdrop's edge.
		private const float FactionIconContentScale = 0.7f;
		private const float BaseTitlePaddingX = 8f;
		private const int TitleLineCount = 2;
		private const float BaseAccentBarHeight = 20f;
		private const float BaseAccentBarTextSize = 0.55f;

		private const float BaseNameTextSize = 1.0f;
		private const float BaseTitleTextSize = 0.65f;

		// Fixed (not scale-dependent) - the accent bar's fill is one of RelationStatus's three
		// colors, and near-black is the one text color that reads clearly against all three (see
		// AvatarFormatting.ColorAccentBarText). Doesn't need to react to the dim/highlight blend the
		// way the bar's own fill does - the fill only ever gets lighter, so contrast only improves.
		private static readonly GlyphFormat AccentBarFormatBase =
			GlyphFormat.White.WithStyle(FontStyles.Bold).WithAlignment(TextAlignment.Center)
				.WithColor(AvatarFormatting.ColorAccentBarText);

		// Unsized base format, fixed color - relation status is carried by the AvatarAccentBar
		// instead (colored text read like a GPS waypoint but doubled up as the only relation cue;
		// the accent bar owns that job on its own, so the name stays a plain readable white).
		// Size still comes from the Scale slider each frame in ApplyMetrics().
		private static readonly GlyphFormat NameFormatBase =
			GlyphFormat.White.WithStyle(FontStyles.Bold).WithAlignment(TextAlignment.Center)
				.WithColor(AvatarFormatting.ColorTextInactive);

		private static readonly GlyphFormat TitleFormatBase =
			GlyphFormat.White.WithColor(AvatarFormatting.ColorIdle).WithAlignment(TextAlignment.Center);

		//The reference point for "how long is too long" for a title on this layout.
		private const int MaxTitleChars = 42;
		private const string TitleTruncationSuffix = "...";

		protected readonly AvatarSettingsPage Settings;

		private readonly TexturedBox _backdrop;
		private readonly GlitchPortraitBox _portrait;
		private readonly HudChain _textChain;
		private readonly LabelBox _nameLabel;
		private readonly LabelBox _titleLabel;
		private readonly LabelBox _accentBar;
		private readonly TexturedBox _factionIconBackdrop;
		private readonly TexturedBox _factionIcon;
		private readonly TexturedBox _speakerIcon;

		private string _currentPortraitId;
		private string _currentFactionIconId;
		private Color _relationColor = Color.White;

		protected AvatarVisualElement(HudParentBase parent, AvatarSettingsPage settings) : base(parent) {

			Settings = settings;

			// Filled backdrop sized exactly to the portrait (no gap) and drawn behind it (ZOffset
			// -1). Fully covered by the portrait under normal conditions - only shows through as a
			// relation-tinted sliver when a glitch slice shifts sideways, so the glitch always reveals
			// color instead of empty transparency.
			_backdrop = new TexturedBox(this) {
				Color = Color.White,
				ZOffset = -1
			};

			_portrait = new GlitchPortraitBox(this);

			_textChain = new HudChain(true, this) {
				SizingMode = HudChainSizingModes.None
			};

			_nameLabel = new LabelBox {
				AutoResize = false,
				VertCenterText = true,
				Format = NameFormatBase.WithSize(BaseNameTextSize),
				Color = AvatarFormatting.ColorBackplate
			};

			_titleLabel = new LabelBox {
				AutoResize = false,
				VertCenterText = true,
				Format = TitleFormatBase.WithSize(BaseTitleTextSize),
				Color = AvatarFormatting.ColorBackplate
			};
			// Wraps long titles onto a second line instead of clipping them; LineWrapWidth is kept
			// in sync with the (scale-dependent) box width every frame in ApplyMetrics().
			_titleLabel.BuilderMode = TextBuilderModes.Wrapped;

			// Relation status strip below the title. A LabelBox rather than a plain textured bar
			// so it can carry the status word ("HOSTILE"/"NEUTRAL"/"FRIENDLY") directly on the fill,
			// same background+text pattern already used for the name/title - LabelBox.Background is
			// itself a full TexturedBox, so the cut-corner AvatarAccentBar shape still applies.
			// Text format is fixed (see AccentBarFormatBase) since near-black reads well against all
			// three RelationStatus colors. Texture authored at 256x40 (matches the bar's 128x20 base
			// render size at 2x for scale headroom, both dimensions divisible by 4 for BC7 block
			// compression).
			_accentBar = new LabelBox {
				AutoResize = false,
				VertCenterText = true,
				Format = AccentBarFormatBase.WithSize(BaseAccentBarTextSize)
			};
			_accentBar.Background.Material = new Material("AvatarAccentBar", new Vector2(256f, 40f));

			_textChain.Add(_nameLabel, 0f);
			_textChain.Add(_titleLabel, 0f);
			_textChain.Add(_accentBar, 0f);

			// Neutral grayscale plate behind the faction icon (ZOffset 2, same as the icon itself) -
			// vanilla faction icons vary wildly in their own colors and can disappear against a dark
			// portrait or wash out against a light one. A fixed neutral backing independent of
			// relation/portrait color keeps every faction icon legible. Round, via RHF's built-in
			// circle material, to read like a badge - matching the speaker icon's own round artwork
			// instead of a square plate clashing with it.
			_factionIconBackdrop = new TexturedBox(this) {
				Visible = false,
				Material = Material.CircleMat,
				Color = AvatarFormatting.ColorBackplate.SetAlphaPct(1),
				ZOffset = 2
			};

			// ZOffset 3 - above its own backdrop so faction identity draws on top of it.
			_factionIcon = new TexturedBox(this) {
				Visible = false,
				ZOffset = 3
			};

			// Mirrors the faction badge on the opposite top corner - reinforces that this element
			// represents an active transmission. Always shown at full opacity: an avatar card is only
			// ever on screen because it's actively transmitting, whether that's the live overlay or
			// the settings-page preview (which only ever shows one example avatar at a time).
			_speakerIcon = new TexturedBox(this) {
				Color = Color.White,
				Material = new Material("SpeakerIcon", new Vector2(64f))
			};

		}

		/// <param name="relationStatusLabel">
		/// Word shown on the accent bar (e.g. "HOSTILE"), resolved by the caller from
		/// RelationStatus.GetLabel() - defaults to null/empty so callers with no status concept
		/// can still compile against this method without a required argument.
		/// </param>
		protected void ApplyDisplay(string portraitSubtypeId, string speakerName, string speakerTitle, Vector4 relationColor, string factionIconId, string relationStatusLabel = null) {

			if (portraitSubtypeId != _currentPortraitId) {

				_portrait.SetPortrait(portraitSubtypeId);
				_currentPortraitId = portraitSubtypeId;

			}

			_relationColor = new Color(relationColor);
			_backdrop.Color = _relationColor;
			_accentBar.Color = _relationColor;

			_nameLabel.Text = new RichText(speakerName ?? string.Empty);
			_titleLabel.Text = new RichText(TruncateTitle(speakerTitle));
			_accentBar.Text = new RichText(relationStatusLabel ?? string.Empty);

			bool hasFactionIcon = !string.IsNullOrWhiteSpace(factionIconId);
			_factionIcon.Visible = hasFactionIcon;
			_factionIconBackdrop.Visible = hasFactionIcon;

			if (hasFactionIcon && factionIconId != _currentFactionIconId) {

				_factionIcon.Material = new Material(factionIconId, new Vector2(58f));
				_factionIcon.Color = TerminalFormatting.Mint;
				_currentFactionIconId = factionIconId;

			}

		}

		/// <summary>
		/// Caps SpeakerTitle at MaxTitleChars (including the "..." suffix when it doesn't fit), since
		/// this is caller-supplied data that could otherwise run arbitrarily long and overflow the
		/// fixed two-line title box.
		/// </summary>
		private static string TruncateTitle(string title) {

			title = title ?? string.Empty;

			if (title.Length <= MaxTitleChars)
				return title;

			int keep = Math.Max(0, MaxTitleChars - TitleTruncationSuffix.Length);
			return title.Substring(0, keep).TrimEnd() + TitleTruncationSuffix;

		}

		protected void ApplyMetrics() {

			float scale = (Settings != null) ? Settings.ScalePercent / 100f : 1f;
			float xOffset = (Settings != null) ? Settings.XOffset : 0f;
			float yOffset = (Settings != null) ? Settings.YOffset : 0f;

			float portraitSize = BasePortraitSize * scale;
			float lineHeight = BaseTextLineHeight * scale;
			float textSpacing = BaseTextSpacing * scale;
			float gap = BaseGapBelowPortrait * scale;
			float titleBoxHeight = lineHeight * TitleLineCount;
			float accentBarHeight = BaseAccentBarHeight * scale;

			float frameSize = portraitSize; // backdrop matches the portrait exactly - no gap
			float textChainHeight = lineHeight + titleBoxHeight + accentBarHeight + (textSpacing * 2f);
			float totalHeight = frameSize + gap + textChainHeight;

			Offset = new Vector2(xOffset, yOffset);
			Size = new Vector2(frameSize, totalHeight);

			float frameCenterY = (totalHeight * .5f) - (frameSize * .5f);
			float chainCenterY = -(totalHeight * .5f) + (textChainHeight * .5f);

			_portrait.Size = new Vector2(portraitSize);
			_portrait.Offset = new Vector2(0f, frameCenterY);
			_portrait.GlitchStrength = (Settings != null) ? Settings.GlitchStrengthPercent / 100f : 1f;

			_backdrop.Size = new Vector2(frameSize);
			_backdrop.Offset = new Vector2(0f, frameCenterY);

			_nameLabel.Format = NameFormatBase.WithSize(BaseNameTextSize * scale);
			_nameLabel.Size = new Vector2(frameSize, lineHeight);

			float titlePaddingX = BaseTitlePaddingX * scale;
			_titleLabel.Format = TitleFormatBase.WithSize(BaseTitleTextSize * scale);
			_titleLabel.Size = new Vector2(frameSize, titleBoxHeight);
			_titleLabel.TextPadding = new Vector2(titlePaddingX, 0f);
			_titleLabel.TextBoard.LineWrapWidth = frameSize - (titlePaddingX * 2f);

			_accentBar.Format = AccentBarFormatBase.WithSize(BaseAccentBarTextSize * scale);
			_accentBar.Size = new Vector2(frameSize, accentBarHeight);

			_textChain.Spacing = textSpacing;
			_textChain.Size = new Vector2(frameSize, textChainHeight);
			_textChain.Offset = new Vector2(0f, chainCenterY);

			float frameHalf = frameSize * .5f;
			float badgeIconSize = BaseBadgeIconSize * scale;
			float badgeIconInset = badgeIconSize * .25f;
			float badgeIconY = frameCenterY + frameHalf - badgeIconInset;

			// Top-left corner.
			Vector2 speakerIconOffset = new Vector2(-frameHalf + badgeIconInset, badgeIconY);
			_speakerIcon.Size = new Vector2(badgeIconSize);
			_speakerIcon.Offset = speakerIconOffset;

			// Top-right corner - the backdrop is an exact mirror of the speaker icon's size/offset
			// across the X axis; the icon itself is drawn smaller within it (FactionIconContentScale)
			// but shares the same center point.
			Vector2 factionIconOffset = new Vector2(frameHalf - badgeIconInset, badgeIconY);
			_factionIcon.Size = new Vector2(badgeIconSize * FactionIconContentScale);
			_factionIcon.Offset = factionIconOffset;

			_factionIconBackdrop.Size = new Vector2(badgeIconSize);
			_factionIconBackdrop.Offset = factionIconOffset;

		}

	}

}
