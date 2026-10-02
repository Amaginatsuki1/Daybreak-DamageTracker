using System.Globalization;
using DaybreakDamageTracker.Common.Config;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader.Config.UI;
using Terraria.ModLoader.UI;
using Terraria.UI;

namespace DaybreakDamageTracker.Client.UI;

public sealed class ResetDefaultsConfigElement : ConfigElement
{
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(5);
    private DateTime _confirmationDeadline;
    private DateTime _successUntil;

    private bool AwaitingConfirmation => DateTime.UtcNow < _confirmationDeadline;
    private bool ShowingSuccess => DateTime.UtcNow < _successUntil;

    public override void OnBind()
    {
        base.OnBind();
        DrawLabel = false;
        Height.Set(58f, 0f);
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        CalculatedStyle dimensions = GetInnerDimensions();
        Rectangle bounds = new(
            (int)dimensions.X + 6,
            (int)dimensions.Y + 5,
            Math.Max(1, (int)dimensions.Width - 12),
            Math.Max(1, (int)dimensions.Height - 10));

        float pulse = (MathF.Sin((float)Main.GlobalTimeWrappedHourly * 7f) + 1f) * 0.5f;
        bool hover = bounds.Contains((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y);
        Color core = ShowingSuccess
            ? new Color(46, 155, 102)
            : AwaitingConfirmation
                ? Color.Lerp(new Color(168, 43, 30), new Color(245, 113, 35), pulse)
                : hover
                    ? new Color(132, 65, 38)
                    : new Color(92, 45, 38);
        Color glow = ShowingSuccess
            ? new Color(90, 255, 175)
            : Color.Lerp(new Color(255, 70, 35), new Color(255, 205, 75), pulse);

        Texture2D pixel = TextureAssets.MagicPixel.Value;
        if (AwaitingConfirmation || ShowingSuccess)
        {
            for (int layer = 4; layer >= 1; layer--)
            {
                Rectangle expanded = new(
                    bounds.X - layer,
                    bounds.Y - layer,
                    bounds.Width + layer * 2,
                    bounds.Height + layer * 2);
                spriteBatch.Draw(pixel, expanded, glow * (0.045f * (5 - layer)));
            }
        }

        spriteBatch.Draw(pixel, bounds, core);
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 2), glow);
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 2), glow);
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, 2, bounds.Height), glow);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Right - 2, bounds.Y, 2, bounds.Height), glow);

        string text = ShowingSuccess
            ? ClientRuntime.Text("UI.ResetDefaultsApplied", "Defaults staged — Save to apply")
            : AwaitingConfirmation
                ? ClientRuntime.Text("UI.ResetDefaultsConfirm", "Click again to restore every setting")
                : Label;
        float scale = 0.8f;
        float rawWidth = FontAssets.MouseText.Value.MeasureString(text).X;
        if (rawWidth * scale > bounds.Width - 12f)
            scale = Math.Max(0.5f, (bounds.Width - 12f) / Math.Max(1f, rawWidth));
        text = TrimToWidth(text, bounds.Width - 12f, scale);
        Vector2 textSize = FontAssets.MouseText.Value.MeasureString(text) * scale;
        Utils.DrawBorderString(
            spriteBatch,
            text,
            bounds.Center.ToVector2() - textSize / 2f,
            Color.White,
            scale);
    }

    private static string TrimToWidth(string text, float maximumWidth, float scale)
    {
        if (FontAssets.MouseText.Value.MeasureString(text).X * scale <= maximumWidth)
            return text;

        const string ellipsis = "…";
        List<string> elements = [];
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            elements.Add(enumerator.GetTextElement());
        while (elements.Count > 0)
        {
            elements.RemoveAt(elements.Count - 1);
            string candidate = string.Concat(elements) + ellipsis;
            if (FontAssets.MouseText.Value.MeasureString(candidate).X * scale <= maximumWidth)
                return candidate;
        }
        return string.Empty;
    }

    public override void LeftClick(UIMouseEvent evt)
    {
        base.LeftClick(evt);
        if (!AwaitingConfirmation)
        {
            _confirmationDeadline = DateTime.UtcNow + ConfirmationWindow;
            _successUntil = DateTime.MinValue;
            SoundEngine.PlaySound(SoundID.MenuTick);
            return;
        }

        if (Item is ClientConfig client)
            client.ResetToDefaults();
        else if (Item is ServerConfig server)
            server.ResetToDefaults();
        else
            return;

        _confirmationDeadline = DateTime.MinValue;
        _successUntil = DateTime.UtcNow.AddSeconds(1.5);
        // Mark the pending ModConfig clone dirty through ConfigElement's public
        // integration path without reaching into tModLoader's internal UI state.
        SetObject(GetObject());
        SoundEngine.PlaySound(SoundID.Item4);
    }
}
