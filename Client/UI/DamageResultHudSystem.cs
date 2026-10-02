using DaybreakDamageTracker.Common.Data;
using Microsoft.Xna.Framework.Input;
using Terraria.UI;

namespace DaybreakDamageTracker.Client.UI;

[Autoload(Side = ModSide.Client)]
internal sealed class DamageResultHudSystem : ModSystem
{
    private const float AutomaticFadeDurationSeconds = PresentationSettings.DefaultFadeSeconds;
    private UserInterface? _userInterface;
    private DamageResultUIState? _uiState;
    private readonly List<ClientHistoryEntry> _currentEntries = [];
    private bool _automatic;
    // The game loop can call UpdateUI more often than 60 times per second (frame
    // skip off, high refresh rates), so the auto-hide window is tracked in real
    // GameTime instead of per-call tick decrements. While the game is paused the
    // deadline is pushed forward so a paused player keeps the panel.
    private TimeSpan _lastUiClock;
    private TimeSpan _autoHideDeadline;
    private float _durationSeconds = 1f;
    private float _fadeSeconds;

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
        _uiState.Bind(
            _currentEntries,
            ClientRuntime.EffectivePresentation,
            ClientRuntime.PanelBackgroundOpacity,
            ClientRuntime.PanelSectionOrder);
        _uiState.SetOpacity(1f);
        _automatic = automatic;
        if (_automatic)
            ResetAutomaticTimer();
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
        _uiState.Bind(
            _currentEntries,
            ClientRuntime.EffectivePresentation,
            ClientRuntime.PanelBackgroundOpacity,
            ClientRuntime.PanelSectionOrder);
        _uiState.SetOpacity(1f);
        if (_automatic)
            ResetAutomaticTimer();
    }

    public void RefreshPresentation()
    {
        if (!IsVisible || _uiState is null || _currentEntries.Count == 0)
            return;

        _uiState.Bind(
            _currentEntries,
            ClientRuntime.EffectivePresentation,
            ClientRuntime.PanelBackgroundOpacity,
            ClientRuntime.PanelSectionOrder);
        _durationSeconds = AutomaticDurationSeconds();
        _fadeSeconds = AutomaticFadeSeconds(_durationSeconds);
        if (!_automatic)
        {
            _uiState.SetOpacity(1f);
            return;
        }

        double remainingSeconds = Math.Clamp(
            (_autoHideDeadline - _lastUiClock).TotalSeconds,
            0.05,
            Math.Max(0.05, _durationSeconds));
        _autoHideDeadline = _lastUiClock + TimeSpan.FromSeconds(remainingSeconds);
        _uiState.SetOpacity(ComputeOpacity(TimeSpan.FromSeconds(remainingSeconds)));
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
        _autoHideDeadline = TimeSpan.Zero;
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
        // Result packets for independently resolved Bosses can arrive in one update.
        // Publish the final command mapping once, after the packet burst has settled.
        ClientRuntime.FlushPendingChatSummary();
        _lastUiClock = gameTime.TotalGameTime;

        if (_userInterface?.CurrentState is null || _uiState is null)
            return;

        if (!Main.drawingPlayerChat && Main.keyState.IsKeyDown(Keys.Escape) && Main.oldKeyState.IsKeyUp(Keys.Escape))
        {
            Hide();
            return;
        }

        if (_automatic)
        {
            if (Main.gamePaused)
            {
                _autoHideDeadline += gameTime.ElapsedGameTime;
            }
            else
            {
                TimeSpan remaining = _autoHideDeadline - gameTime.TotalGameTime;
                if (remaining <= TimeSpan.Zero)
                {
                    Hide();
                    return;
                }

                _uiState.SetOpacity(ComputeOpacity(remaining));
            }
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
        _durationSeconds = AutomaticDurationSeconds();
        _fadeSeconds = AutomaticFadeSeconds(_durationSeconds);
        _autoHideDeadline = _lastUiClock + TimeSpan.FromSeconds(_durationSeconds);
    }

    private float ComputeOpacity(TimeSpan remaining)
        => _fadeSeconds > 0f
            ? MathHelper.Clamp((float)(remaining.TotalSeconds / _fadeSeconds), 0f, 1f)
            : 1f;

    private static float AutomaticDurationSeconds()
        => Math.Max(1f, ClientRuntime.AutoHideSeconds);

    private static float AutomaticFadeSeconds(float durationSeconds)
        => Math.Min(durationSeconds, AutomaticFadeDurationSeconds);
}
