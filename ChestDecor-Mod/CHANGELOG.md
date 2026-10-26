# Changelog

## 1.1.0

Not tested in game yet.

- **New window look**: the decorate window uses the shared KydlDev menu style (dark panel, gold accents, Norse font), opens 5% from the right edge, can be dragged by its title and scrolls when it does not fit. The character does not move while it is open; Esc closes it.
- **Colours**: the colour wheel is replaced by the shared colour editor (preview, swatches, Red / Green / Blue sliders, hex code) with a Brightness slider. Tint / Full colour, Preview, Save and Revert work as before.
- **Position items**: the sliders have a typed number box; the dots under them still jump to 25% steps (directions) or 0 / 50 / 100% (tilt, turn, roll, size).
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
