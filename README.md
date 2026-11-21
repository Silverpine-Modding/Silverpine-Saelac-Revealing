# Revealing

A BepInEx addon for Silverpine 1.7.3 and Custom Item Loader 3.0.7. It lets a
custom clothing or accessory item define its actual chest/waist coverage and
give the LLM a precise author-written description instead of Silverpine's
generic topless, pantless, or naked text.

## Requirements

- BepInEx for Silverpine 1.7.3
- Custom Item Loader 3.0.7 or later (tabbed Custom Item Editor)
- Additional Equipment Slots is optional. When installed, Under-top and
  Under-waist are included in the same coverage calculation.

Do not install another `Newtonsoft.Json.dll`; Modding Tools already supplies
the version used by Custom Item Loader and this addon.

## Installation

Keep the addon in its own folder:

```text
BepInEx/plugins/Revealing/
├── Revealing.dll
└── README.md
```

## Editor

Open `Modding Tools` -> `Custom Item Editor`, select an item, and open the
`Appearance` tab. The `Revealing` foldout appears above the visual settings.
It initially contains one checkbox:

- `Revealing`

When `Revealing` is unchecked, the item inherits the clone's normal coverage.
Checking it displays `Fully covers chest` and `Fully covers waist`. When
neither coverage box is checked, it also displays `Revealing Description`.
Unchecking `Revealing` removes the override. Save the pack and restart
Silverpine, as with other Custom Item Loader changes.

Version 1.1.0 integrates with the loader's new tabbed form and wrapped text
fields. Existing `revealing` blocks are loaded without migration. Changes use
the loader's own addon-data storage, so Save Pack, Duplicate, switching items
or packs, and the unsaved-changes warning all include Revealing settings.
Unchecking Revealing removes its JSON block when the pack is saved. Other
addon fields are preserved. Restart Silverpine after installing this update.

The settings are stored directly on the item in its pack JSON:

```json
{
  "id": "sheer_dancer_outfit",
  "name": "Sheer Dancer Outfit",
  "clone": "Linen Shirt",
  "equipmentSlot": "Chest",
  "revealing": {
    "fullyCoversChest": false,
    "fullyCoversWaist": false,
    "description": "A translucent wrap that leaves most of the chest and hips visible."
  }
}
```

Both booleans default to `false` when a hand-authored `revealing` object omits
them. The editor always writes both values.

## Runtime behavior

- Configured custom items replace their cloned Chest, Legs, accessory,
  Under-top, and Under-waist coverage rules.
- Under-top and Under-waist can always appear by name in Silverpine's basic
  equipped-item list. An Under-top's authored `Visible detail` is included
  only when the equipped Chest item has Revealing enabled and does not fully
  cover the chest. An Under-waist's authored detail is included only when the
  equipped Legs item has Revealing enabled and does not fully cover the waist.
- Coverage is cumulative. If one revealing item leaves the chest exposed but
  another equipped item fully covers it, the chest is covered.
- Unconfigured custom items and all base-game items keep native behavior.
- When neither coverage box is selected, a non-empty description is sent to
  the LLM as a labeled record, for example:

  ```text
  Visible clothing details for Rowan (the player's character):
  - Wearer: Rowan | Slot: Feet | Item: Sock | Visible detail: Shows their heel through a hole.
  ```

  Multiple revealing items appear on separate lines under
  a player-specific heading. Repeating `Wearer` on each line prevents a model
  from assigning the clothing to another visible character while keeping the
  equipment slot, item identity, and authored observation unambiguous.

- When a described item pertains to chest or waist coverage, its authored text
  replaces the generic exposure sentence for that area. Unrelated native
  exposure text is retained.
- The same coverage result drives `Player.IsNude()`, so the nude sprite follows
  the configured chest and waist toggles.

### Player variables in Revealing Description

Revealing descriptions accept Silverpine-style player variables. They are
expanded before the prompt is sent to the LLM:

| Variable | Male result | Female result | Meaning |
|---|---|---|---|
| `%NAME%` | Player's name | Player's name | Current player-character name |
| `%HE%` | `he` | `she` | Subject pronoun |
| `%HIM%` | `him` | `her` | Object pronoun |
| `%HIS%` | `his` | `her` | Possessive determiner |

Pronouns at the beginning of the description or immediately after `.`, `!`,
or `?` are capitalized automatically, matching Silverpine's character-
description behavior.

For example, a Sock can use:

```text
A hole in the sock shows %HIS% heel as %HE% walks beside %NAME%'s companion.
```

For a male player named Rowan, the LLM receives:

```text
A hole in the sock shows his heel as he walks beside Rowan's companion.
```

For a female player named Mira, it receives:

```text
A hole in the sock shows her heel as she walks beside Mira's companion.
```

The description is prompt text, not dialogue. Write a short visual statement
that says what is covered or visible; do not add instructions to the model.

## Development checks

From the Silverpine game directory:

```powershell
dotnet build 'Plugin Development/Revealing/Revealing.csproj' -c Release
dotnet run --project 'Plugin Development/Revealing/Tests/RegressionChecks.csproj' -c Release -- .
```

The regression checks use the installed loader's real item/document types
and inspect its UI bindings. They cover existing JSON, save/reload,
duplication, removal, unknown addon data, malformed settings, and dirty-state
tracking without modifying any packs. Check the Appearance tab in-game after
installing a build to verify rendering and checkbox/text-field interaction.

## Credits

Created by **Saelac and ChatGPT**.
