# Changelog

This records the user-facing changes since BLRP Clothing Utility 1.4.2.

## 1.7.5 - 2026-09-02

### Added

- FEET YMT settings now include a shoe-sound dropdown alongside heel height.

### Fixed

- Preserve untouched YMT component metadata when importing clothing, preventing FEET imports from altering LOWR settings.

## 1.7.4 - 2026-08-31

### Fixed

- Model imports preserve existing component texture metadata instead of
  regenerating it while appending a new item.
- Prop imports accept legacy YMT texture layouts without falsely reporting that
  an existing prop changed.

## 1.7.3 - 2026-08-26

### Fixed

- The optimiser and repository QC now recover polygon totals from a model's
  real index buffer when third-party YDDs incorrectly declare zero triangles.
- These malformed-but-renderable models can now generate reviewed LODs instead
  of reporting `HIGH 0` and failing the Medium reduction check.

## 1.7.2 - 2026-08-24

### Fixed

- LOD generation now updates the serialized index, triangle, and vertex counts;
  OpenIV and the utility therefore report the same reduced polygon totals.
- Candidates are reloaded and rejected before apply/export if polygon metadata,
  vertex indices, shader bindings, or texture references are inconsistent.
- Model imports now verify that every existing drawable and prop YMT entry is
  unchanged before writing the appended entry, preventing adjacent-slot damage.
- Optional texture compression preserves unsupported source formats instead of
  aborting a texture batch partway through.

### Added

- `EXTERNAL YDD...` opens the optimiser for clothing that is not in the active
  repository and exports the reviewed YDD with all detected sibling YTDs.

## 1.5.4 - 2026-08-22

### Added

- Blacklist selectors now combine current Panel businesses, common vRP service
  groups, and restriction values already used by the selected EUP repository.
- Multiple groups can be selected and saved as the server's existing
  pipe-separated access rule, such as `LEO|LSFD`.

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
