#nullable enable

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SilverpineMods.CustomItemLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SilverpineMods.Revealing;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(
    CustomItemLoaderGuid,
    "3.0.7")]
[BepInDependency(
    AdditionalEquipmentSlotsGuid,
    BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "renegadex.silverpine.revealing";
    public const string PluginName = "Revealing";
    public const string PluginVersion = "1.1.0";
    public const string CustomItemLoaderGuid =
        "renegadex.silverpine.customitemloader";
    public const string AdditionalEquipmentSlotsGuid =
        "renegadex.silverpine.additionalequipmentslots";

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        RevealingRuntime.ReloadDefinitions();
        Harmony.CreateAndPatchAll(typeof(PlayerExposurePatch), PluginGuid);
        Harmony.CreateAndPatchAll(
            typeof(PlayerVisibleDescriptionPatch),
            PluginGuid);
        Harmony.CreateAndPatchAll(
            typeof(CustomItemEditorAppearancePatch),
            PluginGuid);
        ReplaceAdditionalEquipmentSlotsExposurePatch();

        Logger.LogInfo(
            $"Loaded {PluginName} {PluginVersion}. Found " +
            $"{RevealingRuntime.DefinitionCount} configured custom item(s).");
    }

    private void ReplaceAdditionalEquipmentSlotsExposurePatch()
    {
        MethodInfo? isExposed = AccessTools.Method(typeof(Player), "IsExposed");
        if (isExposed == null)
            return;

        Patches? patches = Harmony.GetPatchInfo(isExposed);
        if (patches?.Postfixes.Any(patch =>
                patch.owner.Equals(
                    AdditionalEquipmentSlotsGuid,
                    StringComparison.Ordinal)) != true)
            return;

        new Harmony(PluginGuid).Unpatch(
            isExposed,
            HarmonyPatchType.Postfix,
            AdditionalEquipmentSlotsGuid);
        Logger.LogInfo(
            "Replaced Additional Equipment Slots' under-layer exposure " +
            "postfix with Revealing's unified coverage calculation.");
    }
}

internal static class RevealingRuntime
{
    private const string UnderTopId = "silverpine.equipslots.under-top";
    private const string UnderWaistId = "silverpine.equipslots.under-waist";

    private static readonly Dictionary<string, RevealingSettings> BySpriteKey =
        new(StringComparer.OrdinalIgnoreCase);

    internal static int DefinitionCount => BySpriteKey.Count;

    internal static void ReloadDefinitions()
    {
        BySpriteKey.Clear();
        string root = Path.Combine(Paths.ConfigPath, "CustomItemLoader");
        if (!Directory.Exists(root))
            return;

        foreach (string path in Directory.GetFiles(
                     root,
                     "*.json",
                     SearchOption.AllDirectories)
                 .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                LoadPack(path);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    $"Could not read Revealing settings from '{path}': " +
                    exception.Message);
            }
        }
    }

    internal static bool TryGet(
        Item? item,
        out RevealingSettings settings)
    {
        if (item != null &&
            !string.IsNullOrWhiteSpace(item.spriteName) &&
            BySpriteKey.TryGetValue(item.spriteName, out settings!))
            return true;

        settings = null!;
        return false;
    }

    internal static bool IsExposed(
        Player player,
        EquipmentSlotType target)
    {
        foreach (KeyValuePair<EquipmentSlotType, Item> equipped in
                 player.equippedItems)
        {
            if (TryGet(equipped.Value, out RevealingSettings settings))
            {
                bool covered = target switch
                {
                    EquipmentSlotType.Chest =>
                        settings.FullyCoversChest,
                    EquipmentSlotType.Legs =>
                        settings.FullyCoversWaist,
                    _ => false
                };
                if (covered)
                    return false;

                // A configured item replaces its cloned slot/accessory
                // coverage. Other equipped items can still cover the area.
                continue;
            }

            if (NativeItemCovers(equipped.Key, equipped.Value, target))
                return false;
        }

        return true;
    }

    internal static DescriptionBuildContext CreateDescriptionContext(
        Player player)
    {
        bool showUnderTop = ShouldShowUnderLayer(
            player,
            EquipmentSlotType.Chest);
        bool showUnderWaist = ShouldShowUnderLayer(
            player,
            EquipmentSlotType.Legs);
        DescriptionBuildContext context = new(player.playerName);
        foreach (KeyValuePair<EquipmentSlotType, Item> equipped in
                 player.equippedItems)
        {
            if ((IsUnderTop(equipped.Key) && !showUnderTop) ||
                (IsUnderWaist(equipped.Key) && !showUnderWaist))
                continue;

            if (!TryGet(equipped.Value, out RevealingSettings settings))
                continue;

            // The authored description represents an item which fully covers
            // neither area. Coverage-only overrides do not need extra prompt
            // text.
            if (settings.FullyCoversChest || settings.FullyCoversWaist)
                continue;

            string description = NormalizeDescription(
                ExpandPlayerVariables(player, settings.Description));
            if (description.Length == 0)
                continue;

            context.Descriptions.Add(new RevealingDescription(
                GetSlotDisplayName(equipped.Key),
                equipped.Value.name,
                description));

            if (settings.FullyCoversChest ||
                NativeItemCovers(
                    equipped.Key,
                    equipped.Value,
                    EquipmentSlotType.Chest))
                context.SuppressedGenericAreas.Add(
                    EquipmentSlotType.Chest);

            if (settings.FullyCoversWaist ||
                NativeItemCovers(
                    equipped.Key,
                    equipped.Value,
                    EquipmentSlotType.Legs))
                context.SuppressedGenericAreas.Add(
                    EquipmentSlotType.Legs);
        }
        return context;
    }

    private static string ExpandPlayerVariables(
        Player player,
        string? value)
    {
        string result = value ?? "";
        result = result.Replace("%NAME%", player.playerName);
        result = Regex.Replace(
            result,
            "(^|[.!?] )(%HE%|%HIM%|%HIS%)",
            match =>
            {
                string separator = match.Groups[1].Value;
                string replacement = GetPronoun(
                    player,
                    match.Groups[2].Value);
                return separator + replacement.ToUpperFirstLetter();
            });
        return result
            .Replace("%HE%", player.pronouns.He())
            .Replace("%HIM%", player.pronouns.Him())
            .Replace("%HIS%", player.pronouns.His());
    }

    private static string GetPronoun(Player player, string variable) =>
        variable switch
        {
            "%HE%" => player.pronouns.He(),
            "%HIM%" => player.pronouns.Him(),
            "%HIS%" => player.pronouns.His(),
            _ => variable
        };

    internal static bool IsUnderTop(EquipmentSlotType slotType) =>
        IsRegisteredSlot(slotType, UnderTopId);

    internal static bool IsUnderWaist(EquipmentSlotType slotType) =>
        IsRegisteredSlot(slotType, UnderWaistId);

    private static bool ShouldShowUnderLayer(
        Player player,
        EquipmentSlotType outerSlot)
    {
        if (!player.equippedItems.TryGetValue(
                outerSlot,
                out Item outerItem) ||
            !TryGet(outerItem, out RevealingSettings settings))
            return false;

        return outerSlot switch
        {
            EquipmentSlotType.Chest => !settings.FullyCoversChest,
            EquipmentSlotType.Legs => !settings.FullyCoversWaist,
            _ => false
        };
    }

    private static string GetSlotDisplayName(EquipmentSlotType slotType) =>
        CustomEquipmentSlotRegistry.TryGetSlot(
            (int)slotType,
            out CustomEquipmentSlotInfo slot)
            ? slot.DisplayName
            : slotType.ToString();

    internal static bool NativeItemCovers(
        EquipmentSlotType equippedSlot,
        Item item,
        EquipmentSlotType target)
    {
        if (equippedSlot == target)
            return true;

        if (target == EquipmentSlotType.Chest &&
            IsRegisteredSlot(equippedSlot, UnderTopId))
            return true;
        if (target == EquipmentSlotType.Legs &&
            IsRegisteredSlot(equippedSlot, UnderWaistId))
            return true;

        return item.TryGetItemComponent<ItemComponent_Accessory>(
                   out ItemComponent_Accessory accessory) &&
               accessory.coveredSlots != null &&
               accessory.coveredSlots.Contains(target);
    }

    private static bool IsRegisteredSlot(
        EquipmentSlotType actual,
        string id) =>
        CustomEquipmentSlotRegistry.TryGetSlot(
            id,
            out CustomEquipmentSlotInfo slot) &&
        actual == slot.SlotType;

    private static string NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        StringBuilder result = new();
        bool pendingSpace = false;
        foreach (char character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && result.Length > 0)
                result.Append(' ');
            pendingSpace = false;
            result.Append(character);
        }
        return result.ToString();
    }

    private static void LoadPack(string path)
    {
        JObject pack = JObject.Parse(File.ReadAllText(path));
        string? packId = pack.GetValue(
                "packId",
                StringComparison.OrdinalIgnoreCase)?
            .Value<string>();
        if (string.IsNullOrWhiteSpace(packId) ||
            pack.GetValue(
                "items",
                StringComparison.OrdinalIgnoreCase) is not JArray items)
            return;

        foreach (JToken token in items)
        {
            if (token is not JObject item)
                continue;
            string? itemId = item.GetValue(
                    "id",
                    StringComparison.OrdinalIgnoreCase)?
                .Value<string>();
            JToken? revealing = item.GetValue(
                "revealing",
                StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(itemId) || revealing == null)
                continue;

            try
            {
                BySpriteKey["custom:" + packId.Trim() + ":" + itemId] =
                    RevealingSettings.FromJson(revealing);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    $"Ignoring invalid Revealing settings for " +
                    $"'{packId}:{itemId}' in '{path}': {exception.Message}");
            }
        }
    }
}

internal sealed class DescriptionBuildContext
{
    internal DescriptionBuildContext(string wearerName)
    {
        WearerName = wearerName;
    }

    internal string WearerName { get; }
    internal DescriptionBuildContext? Previous;
    internal HashSet<EquipmentSlotType> SuppressedGenericAreas { get; } =
        new();
    internal List<RevealingDescription> Descriptions { get; } = new();
}

internal sealed class RevealingDescription
{
    internal RevealingDescription(
        string slotName,
        string itemName,
        string description)
    {
        SlotName = slotName;
        ItemName = itemName;
        Description = description;
    }

    internal string SlotName { get; }
    internal string ItemName { get; }
    internal string Description { get; }
}

[HarmonyPatch(typeof(Player), "IsExposed")]
internal static class PlayerExposurePatch
{
    private static bool Prefix(
        Player __instance,
        EquipmentSlotType equipmentSlotType,
        ref bool __result)
    {
        if (equipmentSlotType != EquipmentSlotType.Chest &&
            equipmentSlotType != EquipmentSlotType.Legs)
            return true;

        DescriptionBuildContext? context =
            PlayerVisibleDescriptionPatch.CurrentContext;
        if (context != null &&
            context.SuppressedGenericAreas.Contains(equipmentSlotType))
        {
            __result = false;
            return false;
        }
        __result = RevealingRuntime.IsExposed(
            __instance,
            equipmentSlotType);
        return false;
    }
}

[HarmonyPatch(
    typeof(Player),
    nameof(Player.GetNPCVisibleObjectDescription))]
internal static class PlayerVisibleDescriptionPatch
{
    [ThreadStatic]
    private static DescriptionBuildContext? currentContext;

    internal static DescriptionBuildContext? CurrentContext => currentContext;

    private static void Prefix(
        Player __instance,
        out DescriptionBuildContext __state)
    {
        __state = RevealingRuntime.CreateDescriptionContext(__instance);
        __state.Previous = currentContext;
        currentContext = __state;
    }

    private static void Postfix(
        ref string __result,
        DescriptionBuildContext __state)
    {
        try
        {
            if (__state.Descriptions.Count == 0)
                return;

            string addition =
                "Visible clothing details for " + __state.WearerName +
                " (the player's character):\n" +
                string.Join(
                    "\n",
                    __state.Descriptions.Select(value =>
                        "- Wearer: " + __state.WearerName +
                        " | Slot: " + value.SlotName +
                        " | Item: " + value.ItemName +
                        " | Visible detail: " +
                        EnsureTerminalPunctuation(value.Description)));

            int closingParenthesis = __result.LastIndexOf(')');
            __result = closingParenthesis < 0
                ? __result + "\n" + addition
                : __result.Substring(0, closingParenthesis).TrimEnd() +
                  "\n" + addition + "\n" +
                  __result.Substring(closingParenthesis);
        }
        finally
        {
            currentContext = __state.Previous;
        }
    }

    private static string EnsureTerminalPunctuation(string value)
    {
        if (value.Length == 0)
            return value;
        char final = value[value.Length - 1];
        return final is '.' or '!' or '?' ? value : value + ".";
    }

    private static Exception? Finalizer(
        Exception? __exception,
        DescriptionBuildContext? __state)
    {
        if (__state != null && ReferenceEquals(currentContext, __state))
            currentContext = __state.Previous;
        return __exception;
    }
}
