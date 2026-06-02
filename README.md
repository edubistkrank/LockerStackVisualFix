# LockerStackVisualFix

Visual-only compatibility fix for **Visible Locker Interior** in Subnautica.

When stack mods are used, locker interiors can look almost empty because the original visual logic is based on occupied item slots, not real stacked amounts. This mod keeps Visible Locker Interior behavior and redistributes visual item display proportionally to real quantities.

## Features

- Independent mini-fix plugin
- Keeps Visible Locker Interior flow, only adjusts visual distribution
- Uses a fixed visual reference capacity of **48 slots**
- Proportional distribution by real item amounts (Largest Remainder / Hamilton-style)
- Stable results with repeated stacks of the same TechType

## Requirements

- Subnautica (BepInEx)
- **Visible Locker Interior**

## Compatibility

- Explicit support:
  - Mades Redo Inventory Stacking
  - Inventory Resource Stacks
- Safe fallback to vanilla unit-per-item visualization if no stack backend is detected

## Scope

- This is a visual-only fix.
- Does not change inventory content.
- Does not consume/move items.
- Does not change crafting or gameplay logic.

## Installation

1. Install BepInEx and **Visible Locker Interior**.
2. Copy `LockerStackVisualFix.dll` to:
   - `Subnautica/BepInEx/plugins/LockerStackVisualFix/`
3. Launch the game.

## Author

0ctop3dus
