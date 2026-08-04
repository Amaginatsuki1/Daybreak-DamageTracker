using Microsoft.Xna.Framework.Input;
using Terraria.UI;

namespace DaybreakDamageTracker.Client.UI;

[Autoload(Side = ModSide.Client)]
internal sealed class DamageResultHudSystem : ModSystem
{
    private UserInterface? _userInterface;
    private DamageResultUIState? _uiState;
    private ClientHistoryEntry? _currentEntry;
    private bool _automatic;
    private int _remainingTicks;
    private int _fadeTicks;

    public bool IsVisible => _userInterface?.CurrentState is not null;

    public override void PostSetupContent()
    {
        _userInterface = new UserInterface();
        _uiState = new DamageResultUIState();
        _uiState.Activate();
    }

    public void Show(ClientHistoryEntry entry, bool automatic)
    {
        if (_userInterface is null || _uiState is null)
            return;

        _currentEntry = entry;
        _uiState.Bind(entry, ClientRuntime.Presentation);
        _uiState.SetOpacity(1f);
        _automatic = automatic;
        _remainingTicks = Math.Max(1, (int)Math.Round(ClientRuntime.Presentation.AutoShowSeconds * 60f));
        _fadeTicks = Math.Max(0, (int)Math.Round(ClientRuntime.Presentation.FadeSeconds * 60f));
        _userInterface.SetState(_uiState);
    }

    public void RefreshPresentation()
    {
        if (!IsVisible || _uiState is null || _currentEntry is null)
            return;

        if (_automatic && !ClientRuntime.Presentation.AutoShow)
        {
            Hide();
            return;
        }

        _uiState.Bind(_currentEntry, ClientRuntime.Presentation);
        _fadeTicks = Math.Max(0, (int)Math.Round(ClientRuntime.Presentation.FadeSeconds * 60f));
        if (!_automatic)
        {
            _uiState.SetOpacity(1f);
            return;
        }

        int maximumTicks = Math.Max(1, (int)Math.Round(ClientRuntime.Presentation.AutoShowSeconds * 60f));
        _remainingTicks = Math.Clamp(_remainingTicks, 1, maximumTicks);
        float opacity = _fadeTicks > 0 && _remainingTicks <= _fadeTicks
            ? MathHelper.Clamp(_remainingTicks / (float)_fadeTicks, 0f, 1f)
            : 1f;
        _uiState.SetOpacity(opacity);
    }

    public void Hide()
    {
        _currentEntry = null;
        _automatic = false;
        _remainingTicks = 0;
        _fadeTicks = 0;
        _userInterface?.SetState(null);
    }

    public void KeepOpenAfterInteraction()
    {
        if (!IsVisible || _uiState is null)
            return;

        _automatic = false;
        _remainingTicks = 0;
        _fadeTicks = 0;
        _uiState.SetOpacity(1f);
    }

    public void ToggleLatest()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        if (ClientResultHistory.Latest is ClientHistoryEntry latest)
            Show(latest, automatic: false);
    }

    public override void UpdateUI(GameTime gameTime)
    {
        if (_userInterface?.CurrentState is null || _uiState is null)
            return;

        if (!Main.drawingPlayerChat && Main.keyState.IsKeyDown(Keys.Escape) && Main.oldKeyState.IsKeyUp(Keys.Escape))
        {
            Hide();
            return;
        }

        if (_automatic && !Main.gamePaused)
        {
            _remainingTicks--;
            if (_remainingTicks <= 0)
            {
                Hide();
                return;
            }

            float opacity = _fadeTicks > 0 && _remainingTicks <= _fadeTicks
                ? MathHelper.Clamp(_remainingTicks / (float)_fadeTicks, 0f, 1f)
                : 1f;
            _uiState.SetOpacity(opacity);
        }

        _userInterface.Update(gameTime);
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text", StringComparison.Ordinal));
        if (mouseTextIndex == -1)
            return;

        layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
            "Daybreak DamageTracker: Result HUD",
            () =>
            {
                if (_userInterface?.CurrentState is not null)
                    _userInterface.Draw(Main.spriteBatch, new GameTime());
                return true;
            },
            InterfaceScaleType.UI));
    }

    public override void Unload()
    {
        _currentEntry = null;
        _userInterface = null;
        _uiState = null;
    }
}
