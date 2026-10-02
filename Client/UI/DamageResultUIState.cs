using ReLogic.Content;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;

namespace DaybreakDamageTracker.Client.UI;

internal sealed class DamageResultUIState : UIState
{
    private const float PanelTop = 18f;
    private ResultPanelElement _panel = null!;
    private CloseOnlyButton _closeButton = null!;

    public override void OnInitialize()
    {
        _panel = new ResultPanelElement();
        _panel.IgnoresMouseInteraction = false;
        _panel.OnLeftClick += (_, _) =>
        {
            if (_panel.TryToggleSourceTree(Main.MouseScreen))
                ApplyPanelLayout();
            ModContent.GetInstance<DamageResultHudSystem>().KeepOpenAfterInteraction();
        };
        _panel.ScrollInteraction += () =>
            ModContent.GetInstance<DamageResultHudSystem>().KeepOpenAfterInteraction();
        Append(_panel);

        Asset<Texture2D> closeTexture = ModContent.Request<Texture2D>("Terraria/Images/UI/ButtonDelete");
        _closeButton = new CloseOnlyButton(closeTexture);
        _closeButton.Width.Set(ResultPanelElement.CloseButtonSize, 0f);
        _closeButton.Height.Set(ResultPanelElement.CloseButtonSize, 0f);
        _closeButton.OnLeftClick += (_, _) => ModContent.GetInstance<DamageResultHudSystem>().Hide();
        Append(_closeButton);
    }

    public void Bind(
        IReadOnlyList<ClientHistoryEntry> entries,
        Common.Data.PresentationSettings settings,
        float backgroundOpacity,
        IReadOnlyList<ResultPanelSection> sectionOrder)
    {
        _panel.Bind(entries, settings, backgroundOpacity, sectionOrder);
        ApplyPanelLayout();
    }

    public override void Update(GameTime gameTime)
    {
        if (_panel.RefreshLayoutForViewport())
            ApplyPanelLayout();
        base.Update(gameTime);
    }

    private void ApplyPanelLayout()
    {
        _panel.Width.Set(_panel.DesiredWidth, 0f);
        _panel.Height.Set(_panel.DesiredHeight, 0f);
        _panel.Left.Set(-_panel.DesiredWidth / 2f, 0.5f);
        _panel.Top.Set(PanelTop, 0f);

        float closeSize = Math.Max(1f, Math.Min(ResultPanelElement.CloseButtonSize, _panel.DesiredWidth - 4f));
        float closeInset = Math.Min(ResultPanelElement.CloseButtonInset, Math.Max(2f, (_panel.DesiredWidth - closeSize) / 2f));
        _closeButton.Width.Set(closeSize, 0f);
        _closeButton.Height.Set(closeSize, 0f);
        float closeLeft = Math.Max(
            -_panel.DesiredWidth / 2f,
            _panel.DesiredWidth / 2f - closeSize - closeInset);
        _closeButton.Left.Set(closeLeft, 0.5f);
        _closeButton.Top.Set(PanelTop + ResultPanelElement.CloseButtonInset, 0f);
        Recalculate();
    }

    public void SetOpacity(float opacity)
    {
        _panel.Opacity = opacity;
        _closeButton.SetVisibility(opacity, opacity * 0.8f);
    }

    private sealed class CloseOnlyButton(Asset<Texture2D> texture) : UIImageButton(texture)
    {
        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (ContainsPoint(Main.MouseScreen) && !PlayerInput.IgnoreMouseInterface)
                Main.LocalPlayer.mouseInterface = true;
        }
    }
}
