# Changelog

## 2.0.0

- **Requires KydlCore.** The window look, the input lock, the KY hub and the KY-Items list now come from the KydlCore mod, which has to be installed (the mod manager adds it).
- **Menu moved to the KY hub (F6)**: all ChestDecor settings are on its own page in the hub (the KY hub lists every KydlDev mod). The decorate window itself still opens with the *Customize Hotkey* (K) while you look at a chest.
- **Esc** closes the decorate window through KydlCore (only the front window closes when more than one is open).
- **KY-Items**: the list of items and pieces of other KydlDev mods is read from KydlCore, so a new KydlDev mod with items shows up here without a ChestDecor update.
- **Position items**: *Tilt*, *Turn*, *Roll* and *Size* now have the same clickable dots as the directions, one every 12.5% of the slider (nine dots; before they had three).
- **One switch per part**: new `[Colours] Enable`, `[Attachments] Enable` and `[Camera] Enable` (the old *Free Camera* is now `[Camera] Enable`). A part works only while `[General] EnableAll` and its own switch are on; a switched-off part has no page in the window. *Live Preview*, *Default Colour Mode* and *Split Texture Into Parts* moved from `[General]` to `[Colours]`; your values are kept.
- Saved chests stay as they were; nothing to do when updating from 1.1.0.

## 1.1.0

- **New window look**: the decorate window uses the shared KydlDev menu style (dark panel, gold accents, Norse font), opens 5% from the right edge, can be dragged by its title and scrolls when it does not fit. The character does not move while it is open; Esc closes it.
- **Colours**: the colour wheel is replaced by the shared colour editor (preview, swatches, Red / Green / Blue sliders, hex code) with a Brightness slider. Tint / Full colour, Preview, Save and Revert work as before.
- **Position items**: the sliders have a typed number box; the dots under them still jump to 25% steps (directions) or 0 / 50 / 100% (tilt, turn, roll, size).
- **Placement limits** are measured from the item's middle: at most 10 cm away from the chest and at most 10 cm into it, the same at every tilt, turn, roll and size (a long item standing up could go over 1 m away before, and turning it changed the limit). A standing spear can be pushed half into the lid, like a spike. Older placements that are further out move inside when selected.
- **Camera**: replaced the free-flying camera with one that circles the chest, always aimed at it: A / D and W / S (or arrows, or right mouse drag) circle, mouse wheel or E / Q zoom from 1 m to *Camera Max Distance* away from the chest (1 m is a hard limit: never closer, never inside it). It stops where it would hit a wall, ceiling, floor or the terrain instead of going through.
- **KY-Items**: items and pieces from other KydlDev mods (and anything in the KY-Items build category) can be attached. They show under their normal filter and under a new KY-Items filter, which only appears when at least one such item exists.
- **Config**: new `[General] EnableAll` and `[DebugLog] Enable`. The developer options of the old `[Debug]` section left the player config. Every setting now applies as soon as it is changed, also in an open window.

## 1.0.0

First release. See the README for details.

- **Colours**: HSV wheel, brightness slider and hex code; base and trim parts; Tint or Full colour mode; Preview, Save and Revert.
- **Attach items**: up to 5 per chest (configurable), with filters, search and paging, and the cost shown on every row.
  - Trophies
  - Trader goods and valuables
  - Produce: fruits, vegetables, seeds, plants, bushes and tree saplings
  - Mob drops
  - Food: including uncooked and food preparation table items
  - Arrows and bolts
  - Weapons and armor: including casts
  - Decor: furniture and light sources
- **Position items**: move, tilt, turn, roll and resize with sliders and clickable dots; Save, Revert, Reset and Remove.
- **Refunds**: removing an item or destroying the chest returns the materials that were paid.
- **Free camera**: fly around the chest while editing, no admin rights needed.
- **Multiplayer**: one editor per chest, data stored on the chest, cosmetic only so players without the mod can still join.
- **Config and debug**: hotkey, limits, ignored ingredients, extra decor prefabs, and a catalog dump for checking the item list.
- Requires BepInEx 5 and Jotunn.
