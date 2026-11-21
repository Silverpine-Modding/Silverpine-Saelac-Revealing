#nullable enable

using HarmonyLib;
using SilverpineMods.CustomItemLoader;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace SilverpineMods.Revealing;

internal static class EditorIntegration
{
    private static readonly Type EditorType = typeof(CustomItemApi).Assembly.GetType(
        "SilverpineMods.CustomItemLoader.CustomItemEditorUI", throwOnError: true)!;
    private static readonly Type DefinitionType = typeof(CustomItemApi).Assembly.GetType(
        "SilverpineMods.CustomItemLoader.ItemDefinition", throwOnError: true)!;

    // Bind exact methods in the new editor. DrawVisuals runs inside the
    // Appearance tab's scroll/form area, even when its visual choices change.
    internal static readonly MethodInfo AppearanceMethod = RequiredMethod(
        "DrawVisuals", DefinitionType);
    private static readonly MethodInfo Foldout = RequiredMethod(
        "DrawFoldout", typeof(string), typeof(bool).MakeByRefType());
    private static readonly MethodInfo BeginField = RequiredMethod("BeginField", typeof(string));
    private static readonly MethodInfo EndField = RequiredMethod("EndField");
    private static readonly MethodInfo DrawHelp = RequiredMethod("DrawHelp", typeof(string));
    private static readonly MethodInfo DrawTextArea = RequiredMethod(
        "DrawWrappedTextInput", typeof(string), typeof(bool), typeof(float),
        typeof(float?), typeof(float));

    private sealed class FoldoutState { public bool Expanded = true; }
    private static readonly ConditionalWeakTable<object, FoldoutState> Foldouts = new();

    internal static void ValidateContract() => EditorItemData.GetExtensionField(DefinitionType);

    internal static void Draw(object editor, object definition)
    {
        FoldoutState foldout = Foldouts.GetValue(definition, _ => new FoldoutState());
        object?[] arguments = { "Revealing", foldout.Expanded };
        bool expanded = (bool)Invoke(Foldout, editor, arguments)!;
        if (expanded != foldout.Expanded)
        {
            foldout.Expanded = expanded;
            GUIUtility.ExitGUI();
        }
        if (!expanded)
            return;

        RevealingSettings? settings;
        try
        {
            settings = EditorItemData.Read(definition);
        }
        catch (Exception exception)
        {
            Help(editor, "The saved Revealing settings could not be read: " + exception.Message);
            if (GUILayout.Button("Clear invalid Revealing settings"))
            {
                EditorItemData.Write(definition, null);
                GUIUtility.ExitGUI();
            }
            return;
        }

        bool enabled = settings != null;
        bool nextEnabled = GUILayout.Toggle(enabled, "Revealing", GUILayout.MinHeight(44));
        if (nextEnabled != enabled)
        {
            EditorItemData.Write(definition, nextEnabled ? new RevealingSettings() : null);
            // The conditional controls change the IMGUI layout. Follow the
            // loader's tab/selection convention and start a fresh GUI pass.
            GUIUtility.ExitGUI();
        }
        if (settings == null)
        {
            Help(editor, "Uses this item's base-game chest and waist coverage.");
            return;
        }

        bool chest = GUILayout.Toggle(settings.FullyCoversChest,
            "Fully covers chest", GUILayout.MinHeight(44));
        bool waist = GUILayout.Toggle(settings.FullyCoversWaist,
            "Fully covers waist", GUILayout.MinHeight(44));
        if (chest != settings.FullyCoversChest || waist != settings.FullyCoversWaist)
        {
            settings.FullyCoversChest = chest;
            settings.FullyCoversWaist = waist;
            EditorItemData.Write(definition, settings);
            GUIUtility.ExitGUI();
        }

        if (!settings.FullyCoversChest && !settings.FullyCoversWaist)
        {
            Invoke(BeginField, editor, "Revealing Description");
            string description;
            try
            {
                description = (string)Invoke(DrawTextArea, editor,
                    settings.Description, true, 100f, null, 0f)!;
            }
            finally { Invoke(EndField, editor); }
            if (description != settings.Description)
            {
                settings.Description = description;
                EditorItemData.Write(definition, settings);
            }
            Help(editor, "Describe what is visible while this item is worn. " +
                "The LLM receives the player's name, equipment slot, item name, and this description.");
            Help(editor, "Player variables: %NAME% = player name, %HE% = he/she, " +
                "%HIM% = him/her, %HIS% = his/her. Example: A hole in the sock shows %HIS% heel.");
            Help(editor, "A blank description uses the game's generic exposure wording " +
                "when no other equipped item covers that area.");
        }
        else
            Help(editor, "Revealing Description is available when neither coverage box is selected.");
    }

    private static void Help(object editor, string text) => Invoke(DrawHelp, editor, text);

    private static MethodInfo RequiredMethod(string name, params Type[] parameters) =>
        AccessTools.Method(EditorType, name, parameters) ??
        throw new MissingMethodException(EditorType.FullName, name);

    private static object? Invoke(MethodInfo method, object editor, params object?[] arguments)
    {
        try { return method.Invoke(method.IsStatic ? null : editor, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            // Preserve Unity's ExitGUIException when a native field ends a pass.
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}

[HarmonyPatch]
internal static class CustomItemEditorAppearancePatch
{
    private static MethodBase TargetMethod()
    {
        EditorIntegration.ValidateContract();
        return EditorIntegration.AppearanceMethod;
    }

    private static void Prefix(object __instance, object __0) =>
        EditorIntegration.Draw(__instance, __0);
}
