using DaybreakDamageTracker.Common.Config;
using DaybreakDamageTracker.Common.Data;
using ReLogic.Content;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader.Config.UI;
using Terraria.UI;

namespace DaybreakDamageTracker.Client.UI;

// A native ModConfig element that embeds the same ResultPanelElement used by the HUD.
// It reads Item (the unsaved config clone bound by tModLoader), so toggles and sliders
// are reflected immediately without applying them to the running client or server.
public sealed class ResultPanelPreviewConfigElement : ConfigElement
{
    private const float HorizontalPadding = 10f;
    private const float PanelTop = 78f;
    private const float MaximumPreviewHeight = 430f;

    private ResultPanelElement _panel = null!;
    private FittedPreviewText _title = null!;
    private FittedPreviewText _summary = null!;
    private FittedPreviewText _interactionHint = null!;
    private UIImage _closeIcon = null!;
    private ClientHistoryEntry _previewEntry = null!;
    private int _lastFingerprint = int.MinValue;

    public override void OnBind()
    {
        base.OnBind();
        DrawLabel = false;
        Height.Set(PanelTop + MaximumPreviewHeight + 10f, 0f);

        _title = new FittedPreviewText(Label, 0.9f, Color.White)
        {
            Left = new StyleDimension(HorizontalPadding, 0f),
            Top = new StyleDimension(4f, 0f),
            Width = new StyleDimension(-HorizontalPadding * 2f, 1f),
            Height = new StyleDimension(22f, 0f)
        };
        Append(_title);

        _summary = new FittedPreviewText(string.Empty, 0.72f, new Color(190, 210, 225))
        {
            Left = new StyleDimension(HorizontalPadding, 0f),
            Top = new StyleDimension(30f, 0f),
            Width = new StyleDimension(-HorizontalPadding * 2f, 1f),
            Height = new StyleDimension(18f, 0f)
        };
        Append(_summary);

        _interactionHint = new FittedPreviewText(
            ClientRuntime.Text("UI.PreviewInteractionHint", "Hover to scroll; click source arrows to expand or collapse."),
            0.66f,
            new Color(135, 195, 225))
        {
            Left = new StyleDimension(HorizontalPadding, 0f),
            Top = new StyleDimension(51f, 0f),
            Width = new StyleDimension(-HorizontalPadding * 2f, 1f),
            Height = new StyleDimension(18f, 0f)
        };
        Append(_interactionHint);

        _panel = new ResultPanelElement
        {
            IgnoresMouseInteraction = false,
            Left = new StyleDimension(HorizontalPadding, 0f),
            Top = new StyleDimension(PanelTop, 0f)
        };
        _panel.OnLeftClick += (_, _) =>
        {
            if (_panel.TryToggleSourceTree(Main.MouseScreen))
                ApplyPanelLayout();
        };
        Append(_panel);

        Asset<Texture2D> closeTexture = ModContent.Request<Texture2D>("Terraria/Images/UI/ButtonDelete");
        _closeIcon = new UIImage(closeTexture)
        {
            IgnoresMouseInteraction = true,
            Width = new StyleDimension(22f, 0f),
            Height = new StyleDimension(22f, 0f),
            Top = new StyleDimension(PanelTop + 12f, 0f)
        };
        Append(_closeIcon);

        _previewEntry = ResultPanelPreviewData.Create();
        RefreshPreview(force: true);
    }

    public override void Update(GameTime gameTime)
    {
        bool contentChanged = RefreshPreview(force: false);

        CalculatedStyle inner = GetInnerDimensions();
        float width = Math.Max(1f, inner.Width - HorizontalPadding * 2f);
        bool viewportChanged = _panel.ConfigureEmbeddedViewport(width, MaximumPreviewHeight, interactive: true);
        if (contentChanged || viewportChanged)
            ApplyPanelLayout();

        base.Update(gameTime);
    }

    private bool RefreshPreview(bool force)
    {
        int fingerprint = ComputeFingerprint();
        if (!force && fingerprint == _lastFingerprint)
            return false;
        _lastFingerprint = fingerprint;

        (PresentationSettings settings, float opacity, string summary, IReadOnlyList<ResultPanelSection> order) = BuildPreviewState();
        _summary.SetText(summary);
        _panel.Bind([_previewEntry], settings, opacity, order);
        return true;
    }

    private (PresentationSettings Settings, float Opacity, string Summary, IReadOnlyList<ResultPanelSection> Order) BuildPreviewState()
    {
        if (Item is ClientConfig client)
        {
            PresentationSettings settings = ClientRuntime.Presentation.SanitizedClone();
            string autoShow = client.AutoShowResults
                ? ClientRuntime.Text("UI.PreviewEnabled", "On")
                : ClientRuntime.Text("UI.PreviewDisabled", "Off");
            string summary = ClientRuntime.Text(
                "UI.ClientPreviewSummary",
                "Auto-open: {0}  ·  Duration: {1}s  ·  Local history: {2}",
                autoShow,
                Math.Clamp(client.AutoHideSeconds, 1, 120),
                Math.Clamp(client.HistoryCount, 1, 10));
            return (
                settings,
                MathHelper.Clamp(client.PanelBackgroundOpacity, 0f, 1f),
                summary,
                ResultPanelSectionOrderPolicy.Sanitize(client.PanelSectionOrder));
        }

        if (Item is ServerConfig server)
        {
            PresentationSettings serverPolicy = server.ToPresentationSnapshot();
            string recipients = server.Recipients == ResultRecipients.Contributors
                ? ClientRuntime.Text("UI.PreviewContributors", "Contributors")
                : ClientRuntime.Text("UI.PreviewAllPlayers", "All players");
            string summary = ClientRuntime.Text(
                "UI.ServerPreviewSummary",
                "Server allowance  ·  Recipients: {0}  ·  Rows: {1}/{2}/{3}",
                recipients,
                Math.Clamp(server.RankingRows, 1, 64),
                Math.Clamp(server.SourceRootRows, 1, 32),
                Math.Clamp(server.SourceLeafRows, 1, 16));
            return (serverPolicy, ClientRuntime.PanelBackgroundOpacity, summary, ClientRuntime.PanelSectionOrder);
        }

        return (
            ClientRuntime.EffectivePresentation,
            ClientRuntime.PanelBackgroundOpacity,
            string.Empty,
            ClientRuntime.PanelSectionOrder);
    }

    private int ComputeFingerprint()
    {
        HashCode hash = new();
        if (Item is ClientConfig client)
        {
            hash.Add(client.AutoShowResults);
            hash.Add(client.HistoryCount);
            hash.Add(client.AutoHideSeconds);
            hash.Add(client.PanelBackgroundOpacity);
            foreach (ResultPanelSection section in ResultPanelSectionOrderPolicy.Sanitize(client.PanelSectionOrder))
                hash.Add(section);
            AddPresentation(ref hash, ClientRuntime.Presentation);
        }
        else if (Item is ServerConfig server)
        {
            hash.Add(server.ShowBossHeader);
            hash.Add(server.ShowTeamTotal);
            hash.Add(server.ShowBossBodyDamage);
            hash.Add(server.ShowDuration);
            hash.Add(server.ShowRanking);
            hash.Add(server.ShowPersonalBreakdown);
            hash.Add(server.ShowUnattributedDamage);
            hash.Add(server.ShowFinalBlow);
            hash.Add(server.ChatSummary);
            hash.Add(server.RankingRows);
            hash.Add(server.SourceRootRows);
            hash.Add(server.SourceLeafRows);
            hash.Add(server.Recipients);
            hash.Add(ClientRuntime.PanelBackgroundOpacity);
            AddPresentation(ref hash, ClientRuntime.EffectivePresentation);
        }
        return hash.ToHashCode();
    }

    private static void AddPresentation(ref HashCode hash, PresentationSettings settings)
    {
        hash.Add(settings.ShowBossHeader);
        hash.Add(settings.ShowTeamTotal);
        hash.Add(settings.ShowBossBodyDamage);
        hash.Add(settings.ShowDuration);
        hash.Add(settings.ShowRanking);
        hash.Add(settings.ShowPersonalBreakdown);
        hash.Add(settings.ShowUnattributedDamage);
        hash.Add(settings.ShowFinalBlow);
        hash.Add(settings.RankingRows);
        hash.Add(settings.SourceRootRows);
        hash.Add(settings.SourceLeafRows);
    }

    private void ApplyPanelLayout()
    {
        _panel.Width.Set(_panel.DesiredWidth, 0f);
        _panel.Height.Set(_panel.DesiredHeight, 0f);
        float closeSize = Math.Max(1f, Math.Min(22f, _panel.DesiredWidth - 4f));
        _closeIcon.Width.Set(closeSize, 0f);
        _closeIcon.Height.Set(closeSize, 0f);
        float closeLeft = Math.Max(
            HorizontalPadding,
            HorizontalPadding + _panel.DesiredWidth - closeSize -
            Math.Min(12f, Math.Max(2f, (_panel.DesiredWidth - closeSize) / 2f)));
        _closeIcon.Left.Set(closeLeft, 0f);
        Recalculate();
    }

    private sealed class FittedPreviewText(string text, float scale, Color color) : UIElement
    {
        private string _text = text;

        public void SetText(string value) => _text = value ?? string.Empty;

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            CalculatedStyle dimensions = GetInnerDimensions();
            string fitted = ResultPanelElement.FitTextToWidth(_text, Math.Max(0f, dimensions.Width), scale);
            if (!string.IsNullOrEmpty(fitted))
                Utils.DrawBorderString(spriteBatch, fitted, new Vector2(dimensions.X, dimensions.Y), color, scale);
        }
    }
}
