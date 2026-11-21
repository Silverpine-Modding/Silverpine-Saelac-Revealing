#nullable enable

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SilverpineMods.Revealing;

// Custom Item Loader 3 stores addon properties in ItemDefinition.extensionData.
// Keeping the edit here lets its own serialization, duplication, dirty-state
// tracking, discard and pack-saving paths all see the same data.
internal static class EditorItemData
{
    internal static FieldInfo GetExtensionField(Type definitionType)
    {
        FieldInfo field = definitionType.GetField("extensionData") ??
            throw new MissingFieldException(definitionType.FullName, "extensionData");
        if (!typeof(IDictionary<string, JToken>).IsAssignableFrom(field.FieldType))
            throw new NotSupportedException("The loader's addon data format has changed.");
        return field;
    }

    internal static RevealingSettings? Read(object definition)
    {
        var data = (IDictionary<string, JToken>?)GetExtensionField(
            definition.GetType()).GetValue(definition);
        string? key = data?.Keys.FirstOrDefault(IsRevealing);
        return key == null ? null : RevealingSettings.FromJson(data![key]);
    }

    internal static void Write(object definition, RevealingSettings? settings)
    {
        FieldInfo field = GetExtensionField(definition.GetType());
        var data = (IDictionary<string, JToken>?)field.GetValue(definition);
        if (data == null)
        {
            if (settings == null)
                return;
            data = new Dictionary<string, JToken>(StringComparer.Ordinal);
            field.SetValue(definition, data);
        }

        string[] keys = data.Keys.Where(IsRevealing).ToArray();
        // Preserve additional fields authored by another version of the addon.
        JObject updated = keys.Length > 0 && data[keys[0]] is JObject existing
            ? (JObject)existing.DeepClone()
            : new JObject();
        foreach (string key in keys)
            data.Remove(key);

        if (settings == null)
            return;
        foreach (JProperty property in settings.ToJson().Properties())
        {
            foreach (JProperty old in updated.Properties().Where(old =>
                         old.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase)).ToArray())
                old.Remove();
            updated[property.Name] = property.Value.DeepClone();
        }
        data["revealing"] = updated;
    }

    private static bool IsRevealing(string name) =>
        name.Equals("revealing", StringComparison.OrdinalIgnoreCase);
}
