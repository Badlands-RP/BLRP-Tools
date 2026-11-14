# Changelog

## v1.0.46

- Clothing Utility 1.9.4 automatically optimises incoming YTDs during import, replacement, and duplication. Oversized maps are resized to fit within 2048 pixels while preserving aspect ratio. Re-encoding uses DXT1 for opaque maps and DXT5 when alpha is present.
- Model QC flags oversized and uncompressed maps, including textures embedded in YDDs, and graphics allocations over 16 MiB. Imports surface remaining issues for review.

## v1.0.45

- Clothing Locator opens and previews the streamed model and textures for each clothing number. LOD and other backup copies are skipped.
- Use **Delete Item** to remove streamed files and free a custom YMT slot for reuse, including slots with a missing YDD. Other clothing numbers stay stable.
- Use **Previous** and **Next** in **Preview Selected** to browse texture variants. Use **Blacklist Item** to restrict clothing that is already in the pack.
- **Keep Local Backups** is off by default. Enable it to retain originals in `.clothing-locator-backups`. Temporary rollback copies still protect edits that update multiple files.

## v1.0.44

- Improved readability in Clothing Utility's Batch Previews window.

## v1.0.43

- Fixed Loading Screen Tool tab colors so labels stay readable on the dark background.
- Replaced broad patch copying with brush and small preset masks using local inpainting. Only marked pixels change, and masks covering over 2.5% of an image are rejected.
- Added Preview Removal, Show Original, Clear Mask, and Save Changes controls. Previewing does not modify project images; saving checks that originals have not changed.
- Image backups now keep their original file extensions so they can be opened directly.

## v1.0.42

- Added a separate size check and resize button in Loading Screen Tool. It center crops project images to 1920×1080, lists changed files and their previous dimensions, and keeps originals in `.blrp-image-backups`.
- Added a UI removal tab with numbered WebP selection, common FiveM watermark and FPS corner presets, and a draggable custom area. Selected areas are filled from nearby image content; originals are backed up for visual review.

## v1.0.41

- Fixed the Batch Previews window becoming unresponsive under rapid progress updates. Export and upload work run outside the UI thread, and the window displays the latest progress at most four times per second.
- Keep the owned export dialog attached to the Clothing Utility window instead of presenting a separate taskbar entry. Standalone batch-preview windows remain accessible from the taskbar.
- Show an explicit cancelling state and check cancellation while indexing custom textures.
- Label the public preview URL and progress bar. Clothing Utility is now v1.9.1.

## v1.0.40

- Added Batch Previews to Clothing Utility, with saved folders, target build and export options.
- Export Rockstar and custom clothing for both genders and every registered texture as transparent WebP previews. Unchanged clothing is cached; new or modified models and textures are regenerated.
- Improved automatic framing, prop orientation, front-facing badges, empty-slot handling and facial-hair previews on a neutral reference head. Facial hair uses reviewed collection identities rather than geometry guesses.
- Added Backblaze Update + Upload: upload unique images by content hash, skip matching remote files, retry transient failures and verify checksums and public downloads. Application keys are masked and are not saved in export settings.
- Generate collection-based catalogue metadata for the updated clothing store. Images load individually from Backblaze instead of adding the complete image library to the FiveM resource download.

## v1.0.39

- Added Loading Screen Tool to the Hub and release package.
- Link a project image folder and active server backgrounds folder. Existing numbered WebPs keep their relative order while gaps close; new PNG, JPEG, BMP, GIF and WebP images are appended as numbered WebPs.
- Processing retains original source images and records processed inputs to avoid duplicate additions on later runs.
- Export synchronizes numbered WebPs to the active folder and removes stale numbered WebPs there, leaving other files alone.

Earlier changes are documented in `release-notes/`.
