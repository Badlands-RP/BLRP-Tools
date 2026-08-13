# Changelog

This records the user-facing changes since BLRP Clothing Utility 1.4.2.

## 1.5.2 - 2026-08-12

### Fixed

- FEET imports now rebuild the creature-metadata file referenced by
  `SHOP_PED_APPAREL`, preventing existing heel-height entries from being ignored
  when that referenced file is missing or incorrectly named.
- The importer backs up and rolls back the companion metadata together with the
  shoe model, textures, and component YMT if an import fails.

## 1.5.1 - 2026-08-12

### Added

- Import multiple texture YTDs onto an existing clothing item in one operation.
- Optionally assign each imported texture to a different business, leave it
  public, or use `APPLY TO ALL` for a shared assignment.
- Replace an existing custom clothing item with a new model and complete set of
  textures while keeping its clothing ID and blacklist entries.
- Edit heel height from `YMT SETTINGS...` for indexed `FEET` items.

### Fixed

- Full item replacement now retargets embedded model and texture names, removes
  obsolete textures, updates the drawable's YMT texture metadata, and rolls the
  operation back if any step fails.
- Heel-height saves update both the component YMT expression modifier and the
  creature-metadata YMT referenced by `SHOP_PED_APPAREL`, repairing or creating
  that companion file when needed.
- Per-texture blacklist controls are disabled when the complete drawable is
  already restricted, because the drawable rule takes precedence.
- Import summaries now report the exact drawable and texture blacklist changes.

### Known limitation

- Prop import is not supported yet, so hat hair scaling/cutting settings are not
  exposed by the utility.

## 1.5.0 - 2026-08-12

### Added

- Load current custom-business names from the BadlandsRP Panel's public
  endpoint, with manual refresh and an offline cache.
- Automatically update the matching male or female Lua clothing blacklist when
  importing a model, duplicating an item, or importing a texture.
- Assign model imports and duplicates as complete-drawable restrictions, while
  texture imports restrict only the new texture.

### Security

- Documented that the business-list endpoint is the utility's only direct
  network request.
