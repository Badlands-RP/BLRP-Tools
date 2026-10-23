# Changelog

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
