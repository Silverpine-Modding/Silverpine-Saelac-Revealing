using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SilverpineMods.Revealing;

// Exercise the installed loader's real ItemDefinition and PackDocument types.
// No game process, pack files, or Unity UI objects are modified by these tests.
string root = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
string loaderPath = Path.Combine(root, "BepInEx/plugins/CustomItemLoader/CustomItemLoader.dll");
string[] dependencyDirectories = {
    Path.Combine(root, "BepInEx/core"),
    Path.Combine(root, "Silverpine_Data/Managed"),
    Path.Combine(root, "BepInEx/plugins/ModdingTools")
};
AssemblyLoadContext.Default.Resolving += (_, name) => {
    string? path = dependencyDirectories.Select(dir => Path.Combine(dir, name.Name + ".dll"))
        .FirstOrDefault(File.Exists);
    return path == null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
};
Assembly loader = AssemblyLoadContext.Default.LoadFromAssemblyPath(loaderPath);
Type definitionType = loader.GetType("SilverpineMods.CustomItemLoader.ItemDefinition", true)!;
int passed = 0;

void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
    passed++;
}

object Item(string json = "{}") => JsonConvert.DeserializeObject(json, definitionType)!;
JObject Json(object value) => JObject.Parse(JsonConvert.SerializeObject(value));

object plain = Item("""{"id":"normal","name":"Normal Shirt","unrelatedAddon":{"keep":42}}""");
Check(EditorItemData.Read(plain) == null, "Unconfigured items retain native behavior");
string original = JsonConvert.SerializeObject(plain);
EditorItemData.Write(plain, null);
Check(JsonConvert.SerializeObject(plain) == original, "Disabling a plain item is a no-op");
EditorItemData.Write(plain, new RevealingSettings {
    Description = "A hole reveals %HIS% back muscles."
});
Check(Json(plain)["revealing"]?["description"]?.Value<string>() ==
    "A hole reveals %HIS% back muscles.", "Settings serialize on the item with placeholders intact");
Check(Json(plain)["extensionData"] == null && Json(plain)["unrelatedAddon"]?["keep"]?.Value<int>() == 42,
    "Native extension-data serialization preserves other addons without a nested wrapper");

object legacy = Item("""
    {"id":"sock","revealing":{"fullyCoversChest":false,"fullyCoversWaist":true,
    "description":"Shows their heel through a hole","futureOption":"retain"}}
    """);
RevealingSettings loaded = EditorItemData.Read(legacy)!;
Check(!loaded.FullyCoversChest && loaded.FullyCoversWaist &&
    loaded.Description == "Shows their heel through a hole", "Version 1.0.0 settings load without migration");
loaded.FullyCoversChest = true;
loaded.FullyCoversWaist = false;
loaded.Description = "%NAME% wears this.\nA tear shows %HIS% knee.";
EditorItemData.Write(legacy, loaded);
object reloaded = Item(JsonConvert.SerializeObject(legacy));
RevealingSettings afterSave = EditorItemData.Read(reloaded)!;
Check(afterSave.FullyCoversChest && !afterSave.FullyCoversWaist &&
    afterSave.Description == loaded.Description, "Coverage and multiline descriptions survive save/reload");
Check(Json(reloaded)["revealing"]?["futureOption"]?.Value<string>() == "retain",
    "Unknown Revealing properties survive editing");

// This is the loader's Duplicate path: serialize and deserialize the definition.
object duplicate = Item(JsonConvert.SerializeObject(reloaded));
var duplicateSettings = EditorItemData.Read(duplicate)!;
duplicateSettings.Description = "Duplicate only";
EditorItemData.Write(duplicate, duplicateSettings);
Check(EditorItemData.Read(reloaded)!.Description == loaded.Description &&
    EditorItemData.Read(duplicate)!.Description == "Duplicate only", "Duplicated items have independent settings");
EditorItemData.Write(duplicate, null);
Check(EditorItemData.Read(duplicate) == null && Json(duplicate)["revealing"] == null,
    "Unchecking Revealing removes the saved override");
Check(EditorItemData.Read(Item(JsonConvert.SerializeObject(duplicate))) == null,
    "Removed settings stay removed after reload");

object mixedCase = Item("""
    {"ReVeAlInG":{"FullyCoversChest":true,"Description":"Old"},"anotherAddon":"keep"}
    """);
Check(EditorItemData.Read(mixedCase)!.FullyCoversChest, "Hand-authored casing is read compatibly");
EditorItemData.Write(mixedCase, new RevealingSettings { Description = "New" });
Check(Json(mixedCase)["ReVeAlInG"] == null &&
    Json(mixedCase)["revealing"]?["Description"] == null &&
    Json(mixedCase)["revealing"]?["description"]?.Value<string>() == "New",
    "Editing normalizes known property names without duplicate keys");
EditorItemData.Write(mixedCase, null);
Check(Json(mixedCase)["anotherAddon"]?.Value<string>() == "keep" &&
    !Json(mixedCase).Properties().Any(p => p.Name.Equals("revealing", StringComparison.OrdinalIgnoreCase)),
    "Disabling removes case-insensitive overrides and retains unrelated properties");

object malformed = Item("""{"revealing":{"fullyCoversChest":"yes"}}""");
string malformedJson = JsonConvert.SerializeObject(malformed);
bool rejected = false;
try { EditorItemData.Read(malformed); } catch (InvalidDataException) { rejected = true; }
Check(rejected && JsonConvert.SerializeObject(malformed) == malformedJson,
    "Malformed settings are reported without silently erasing saved data");
EditorItemData.Write(malformed, null);
Check(EditorItemData.Read(malformed) == null, "Explicit clearing recovers malformed data");
Check(EditorItemData.Read(Item("""{"revealing":{}}""")) is {
    FullyCoversChest: false, FullyCoversWaist: false, Description: ""
}, "Omitted fields retain the generic-fallback defaults");

Type packType = loader.GetType("SilverpineMods.CustomItemLoader.ItemPackDefinition", true)!;
Type documentType = loader.GetType("SilverpineMods.CustomItemLoader.CustomItemEditorUI+PackDocument", true)!;
object pack = JsonConvert.DeserializeObject("""{"packId":"test","items":[{"id":"shirt"}]}""", packType)!;
object document = Activator.CreateInstance(documentType, true)!;
const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
documentType.GetField("Pack", fields)!.SetValue(document, pack);
documentType.GetField("SourceText", fields)!.SetValue(document, JsonConvert.SerializeObject(pack));
documentType.GetField("SavedSnapshot", fields)!.SetValue(document, JsonConvert.SerializeObject(pack));
bool Dirty() => (bool)documentType.GetProperty("HasUnsavedChanges", fields)!.GetValue(document)!;
object packItem = ((IList)packType.GetField("items")!.GetValue(pack)!)[0]!;
Check(!Dirty(), "Real loader PackDocument starts clean");
EditorItemData.Write(packItem, new RevealingSettings { Description = "Test" });
Check(Dirty(), "Enabling Revealing triggers the loader's unsaved-change warning");
documentType.GetField("SavedSnapshot", fields)!.SetValue(document, JsonConvert.SerializeObject(pack));
Check(!Dirty(), "Saving the snapshot clears the dirty state");
var changed = EditorItemData.Read(packItem)!;
changed.FullyCoversWaist = true;
EditorItemData.Write(packItem, changed);
Check(Dirty(), "Coverage changes trigger the loader's unsaved-change warning");
documentType.GetField("SavedSnapshot", fields)!.SetValue(document, JsonConvert.SerializeObject(pack));
changed.Description = "Edited description";
EditorItemData.Write(packItem, changed);
Check(Dirty(), "Description changes trigger the loader's unsaved-change warning");
documentType.GetField("SavedSnapshot", fields)!.SetValue(document, JsonConvert.SerializeObject(pack));
EditorItemData.Write(packItem, null);
Check(Dirty(), "Disabling Revealing triggers the loader's unsaved-change warning");

// Inspect actual installed UI IL without constructing Unity objects outside the game.
using var module = ModuleDefinition.ReadModule(loaderPath);
var editor = module.GetType("SilverpineMods.CustomItemLoader.CustomItemEditorUI");
var visuals = editor.Methods.Single(m => m.Name == "DrawVisuals" && m.Parameters.Count == 1);
Check(visuals.Parameters[0].ParameterType.FullName == definitionType.FullName && !visuals.IsStatic,
    "Installed loader exposes the expected Appearance injection target");
Check(editor.Methods.Single(m => m.Name == "DrawDetailsPanel").Body.Instructions.Any(i =>
    i.Operand is MethodReference called && called.FullName == visuals.FullName),
    "Installed tabbed editor actually calls the Appearance injection target");
bool Method(string name, params string[] parameters) => editor.Methods.Any(m =>
    m.Name == name && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters));
Check(Method("DrawFoldout", "System.String", "System.Boolean&") &&
    Method("BeginField", "System.String") && Method("EndField") &&
    Method("DrawHelp", "System.String") && Method("DrawWrappedTextInput", "System.String",
        "System.Boolean", "System.Single", "System.Nullable`1<System.Single>", "System.Single"),
    "Native foldout, wrapped description and help bindings match the installed editor");

Console.WriteLine($"All {passed} Revealing regression checks passed against Custom Item Loader {loader.GetName().Version}.");
