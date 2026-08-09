using Microsoft.Xna.Framework.Input;
using Terraria.UI;

namespace DaybreakDamageTracker.Client.UI;

[Autoload(Side = ModSide.Client)]
internal sealed class DamageResultHudSystem : ModSystem
{
    private UserInterface? _userInterface;
    private DamageResultUIState? _uiState;
    private readonly List<ClientHistoryEntry> _currentEntries = [];
    private bool _automatic;
    private int _remainingTicks;
    private int _fadeTicks;

    public bool IsVisible => _userInterface?.CurrentState is not null;

    public override void PostSetupContent()
    {
        _userInterface = new UserInterface();
        _uiState = new DamageResultUIState();
        _uiState.Activate();
        ClientRuntime.RegisterHud(this);
    }

    public void Show(ClientHistoryEntry entry, bool automatic)
    {
        if (_userInterface is null || _uiState is null)
            return;

        _currentEntries.Clear();
        _currentEntries.Add(entry);
        _uiState.Bind(_currentEntries, ClientRuntime.Presentation, ClientRuntime.PanelBackgroundOpacity);
        _uiState.SetOpacity(1f);
        _automatic = automatic;
        if (_automatic)
            ResetAutomaticTimer();
        else
        {
            _remainingTicks = 0;
            _fadeTicks = 0;
        }
        _userInterface.SetState(_uiState);
    }

    public void Append(ClientHistoryEntry entry)
    {
        if (_userInterface is null || _uiState is null || !IsVisible)
            return;

        string bossKey = entry.Public.Bosses.FirstOrDefault()?.Key ?? string.Empty;
        _currentEntries.RemoveAll(existing =>
            existing.Public.EncounterId == entry.Public.EncounterId &&
            existing.Public.BossOccurrence == entry.Public.BossOccurrence &&
            (existing.Public.Bosses.FirstOrDefault()?.Key ?? string.Empty).Equals(bossKey, StringComparison.Ordinal));
        _currentEntries.Add(entry);
        int capacity = ClientRuntime.HistoryCapacity;
        if (_currentEntries.Count > capacity)
            _currentEntries.RemoveRange(0, _currentEntries.Count - capacity);
        _uiState.Bind(_currentEntries, ClientRuntime.Presentation, ClientRuntime.PanelBackgroundOpacity);
        _uiState.SetOpacity(1f);
        if (_automatic)
            ResetAutomaticTimer();
    }

    public void RefreshPresentation()
    {
        if (!IsVisible || _uiState is null || _currentEntries.Count == 0)
            return;

        _uiState.Bind(_currentEntries, ClientRuntime.Presentation, ClientRuntime.PanelBackgroundOpacity);
        int maximumTicks = AutomaticDurationTicks();
        _fadeTicks = AutomaticFadeTicks(maximumTicks);
        if (!_automatic)
        {
            _uiState.SetOpacity(1f);
            return;
        }

        _remainingTicks = Math.Clamp(_remainingTicks, 1, maximumTicks);
        float opacity = _fadeTicks > 0 && _remainingTicks <= _fadeTicks
            ? MathHelper.Clamp(_remainingTicks / (float)_fadeTicks, 0f, 1f)
            : 1f;
        _uiState.SetOpacity(opacity);
    }

    public void ApplyLocalPreferences()
    {
        int capacity = ClientRuntime.HistoryCapacity;
        if (_currentEntries.Count > capacity)
            _currentEntries.RemoveRange(0, _currentEntries.Count - capacity);
        RefreshPresentation();
        if (_automatic && IsVisible && _uiState is not null)
        {
            ResetAutomaticTimer();
            _uiState.SetOpacity(1f);
        }
    }

    public void Hide()
    {
        _currentEntries.Clear();
        _automatic = false;
        _remainingTicks = 0;
        _fadeTicks = 0;
        _userInterface?.SetState(null);
    }

    public void KeepOpenAfterInteraction()
    {
        if (!IsVisible || _uiState is null)
            return;

        _uiState.SetOpacity(1f);
        if (_automatic)
            ResetAutomaticTimer();
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
        ClientRuntime.UnregisterHud(this);
        _currentEntries.Clear();
        _userInterface = null;
        _uiState = null;
    }

    private void ResetAutomaticTimer()
    {
        _remainingTicks = AutomaticDurationTicks();
        _fadeTicks = AutomaticFadeTicks(_remainingTicks);
    }

    private static int AutomaticDurationTicks()
        => Math.Max(1, ClientRuntime.AutoHideSeconds * 60);

    private static int AutomaticFadeTicks(int durationTicks)
        => Math.Min(
            durationTicks,
            Math.Max(0, (int)Math.Round(ClientRuntime.Presentation.FadeSeconds * 60f)));
}
