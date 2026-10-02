namespace DaybreakDamageTracker.Common.Data
{
    internal enum EncounterOutcome : byte
    {
        Unknown,
        Victory,
        Defeat,
        Escaped,
        Manual,
        Mixed
    }

    internal enum FinalBlowKind : byte
    {
        None,
        DirectHit,
        DamageOverTime
    }
}

namespace DaybreakDamageTracker.Common.Systems
{
    internal readonly record struct EncounterPlayerKey(int PlayerIndex, int Generation);
}

namespace Newtonsoft.Json
{
    [AttributeUsage(AttributeTargets.Enum | AttributeTargets.Class | AttributeTargets.Property)]
    internal sealed class JsonConverterAttribute : Attribute
    {
        public JsonConverterAttribute(Type converterType)
        {
        }
    }
}

namespace Newtonsoft.Json.Converters
{
    internal sealed class StringEnumConverter
    {
    }
}
