#nullable enable

using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace SilverpineMods.Revealing;

internal sealed class RevealingSettings
{
    internal bool FullyCoversChest;
    internal bool FullyCoversWaist;
    internal string Description = "";

    internal RevealingSettings Clone() => new()
    {
        FullyCoversChest = FullyCoversChest,
        FullyCoversWaist = FullyCoversWaist,
        Description = Description
    };

    internal JObject ToJson() => new()
    {
        ["fullyCoversChest"] = FullyCoversChest,
        ["fullyCoversWaist"] = FullyCoversWaist,
        ["description"] = Description
    };

    internal static RevealingSettings FromJson(JToken token)
    {
        if (token is not JObject value)
            throw new InvalidDataException("revealing must be a JSON object.");

        return new RevealingSettings
        {
            FullyCoversChest = ReadBoolean(
                value,
                "fullyCoversChest"),
            FullyCoversWaist = ReadBoolean(
                value,
                "fullyCoversWaist"),
            Description = value.GetValue(
                    "description",
                    StringComparison.OrdinalIgnoreCase)?
                .Value<string>() ?? ""
        };
    }

    private static bool ReadBoolean(JObject value, string propertyName)
    {
        JToken? token = value.GetValue(
            propertyName,
            StringComparison.OrdinalIgnoreCase);
        if (token == null)
            return false;
        if (token.Type != JTokenType.Boolean)
            throw new InvalidDataException(
                $"revealing.{propertyName} must be true or false.");
        return token.Value<bool>();
    }
}
