using System.Globalization;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader.Config.UI;
using Terraria.ModLoader.UI;
using Terraria.UI;

namespace DaybreakDamageTracker.Client.UI;

public sealed class ResultPanelSectionOrderConfigElement : ConfigElement
{
    private const float TopOffset = 70f;
    private const float RowHeight = 30f;
    private const float RowGap = 4f;
    private readonly List<(Rectangle Up, Rectangle Down)> _moveButtons = [];

    public override void OnBind()
    {
        base.OnBind();
        DrawLabel = false;
        Height.Set(TopOffset + 6f * (RowHeight + RowGap) + 6f, 0f);
        EnsureOrder();
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        if (Item is not ClientConfig config)
            return;

        List<ResultPanelSection> order = EnsureOrder();
        CalculatedStyle dimensions = GetInnerDimensions();
        float left = dimensions.X + 8f;
        float right = dimensions.X + dimensions.Width - 8f;

        DrawFittedText(spriteBatch, Label, left, dimensions.Y + 4f, right - left, Color.White, 0.82f);
        DrawFittedText(
            spriteBatch,
            ClientRuntime.Text(
                "UI.SectionOrderDefault",
                "Default: total → body → unattributed → duration → ranking → sources"),
            left,
            dimensions.Y + 27f,
            right - left,
            new Color(255, 215, 135),
            0.68f);
        DrawFittedText(
            spriteBatch,
            ClientRuntime.Text(
                "UI.SectionOrderHint",
                "Use the arrows to reorder sections. Boss identity and final blow stay fixed at the top."),
            left,
            dimensions.Y + 47f,
            right - left,
            new Color(180, 205, 220),
            0.68f);

        _moveButtons.Clear();
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        for (int index = 0; index < order.Count; index++)
        {
            float rowY = dimensions.Y + TopOffset + index * (RowHeight + RowGap);
            Rectangle row = new((int)left, (int)rowY, Math.Max(1, (int)(right - left)), (int)RowHeight);
            Rectangle down = new(row.Right - 30, row.Y + 2, 26, row.Height - 4);
            Rectangle up = new(down.X - 30, down.Y, 26, down.Height);
            _moveButtons.Add((up, down));

            bool hovered = row.Contains((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y);
            spriteBatch.Draw(pixel, row, hovered ? new Color(48, 74, 99) : new Color(34, 50, 68));
            spriteBatch.Draw(pixel, new Rectangle(row.X, row.Y, row.Width, 1), new Color(78, 132, 165));

            string sectionName = Language.GetTextValue($"Mods.DaybreakDamageTracker.Configs.ResultPanelSection.{order[index]}.Label");
            string rowText = $"{index + 1}. {sectionName}";
            float rowTextWidth = Math.Max(1f, up.X - (row.X + 9f) - 6f);
            rowText = TrimToWidth(rowText, rowTextWidth, 0.74f);
            Utils.DrawBorderString(
                spriteBatch,
                rowText,
                new Vector2(row.X + 9f, row.Y + 5f),
                Color.White,
                0.74f);

            DrawMoveButton(spriteBatch, pixel, up, "▲", index > 0);
            DrawMoveButton(spriteBatch, pixel, down, "▼", index < order.Count - 1);
        }
    }

    public override void LeftClick(UIMouseEvent evt)
    {
        base.LeftClick(evt);
        if (Item is not ClientConfig config)
            return;

        List<ResultPanelSection> order = EnsureOrder();
        Point mouse = Main.MouseScreen.ToPoint();
        for (int index = 0; index < _moveButtons.Count; index++)
        {
            int target = _moveButtons[index].Up.Contains(mouse)
                ? index - 1
                : _moveButtons[index].Down.Contains(mouse)
                    ? index + 1
                    : index;
            if (target < 0 || target >= order.Count || target == index)
                continue;

            (order[index], order[target]) = (order[target], order[index]);
            config.PanelSectionOrder = order;
            // ConfigElement.SetObject is the supported path that also marks the
            // native ModConfig page dirty. The bound marker value itself stays null.
            SetObject(GetObject());
            SoundEngine.PlaySound(SoundID.MenuTick);
            return;
        }
    }

    private List<ResultPanelSection> EnsureOrder()
    {
        if (Item is not ClientConfig config)
            return ResultPanelSectionOrderPolicy.CreateDefault();

        List<ResultPanelSection> sanitized = ResultPanelSectionOrderPolicy.Sanitize(config.PanelSectionOrder);
        if (config.PanelSectionOrder is null || !config.PanelSectionOrder.SequenceEqual(sanitized))
            config.PanelSectionOrder = sanitized;
        return config.PanelSectionOrder;
    }

    private static void DrawMoveButton(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Rectangle bounds,
        string glyph,
        bool enabled)
    {
        bool hovered = enabled && bounds.Contains((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y);
        Color background = !enabled
            ? new Color(35, 39, 44)
            : hovered
                ? new Color(95, 168, 210)
                : new Color(57, 103, 135);
        spriteBatch.Draw(pixel, bounds, background);

        Vector2 size = FontAssets.MouseText.Value.MeasureString(glyph) * 0.7f;
        Utils.DrawBorderString(
            spriteBatch,
            glyph,
            bounds.Center.ToVector2() - size / 2f,
            enabled ? Color.White : new Color(100, 105, 112),
            0.7f);
    }

    private static void DrawFittedText(
        SpriteBatch spriteBatch,
        string text,
        float x,
        float y,
        float maximumWidth,
        Color color,
        float preferredScale)
    {
        float rawWidth = FontAssets.MouseText.Value.MeasureString(text).X;
        float scale = rawWidth <= 0f
            ? preferredScale
            : Math.Min(preferredScale, maximumWidth / rawWidth);
        scale = Math.Max(0.46f, scale);
        string fitted = TrimToWidth(text, maximumWidth, scale);
        Utils.DrawBorderString(spriteBatch, fitted, new Vector2(x, y), color, scale);
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
}
