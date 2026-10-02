using System.Globalization;
using Terraria.ModLoader.Config.UI;

namespace DaybreakDamageTracker.Client.UI;

// tModLoader's FloatElement displays the raw Single produced by its increment
// calculation (for example, 18 * 0.05f becomes 0.90000004). Keep the value and
// slider behavior unchanged while presenting the precision promised by the UI.
public sealed class PanelBackgroundOpacityConfigElement : FloatElement
{
    public override void OnBind()
    {
        base.OnBind();
        TextDisplayFunction = () =>
            $"{Label}: {GetValue().ToString("0.00", CultureInfo.CurrentCulture)}";
    }
}
