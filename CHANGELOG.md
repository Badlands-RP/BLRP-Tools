# Changelog

## v1.0.39

- Added Loading Screen Tool to the Hub and release package.
- Link a project image folder and active server backgrounds folder. Existing numbered WebPs keep their relative order while gaps close; new PNG, JPEG, BMP, GIF and WebP images are appended as numbered WebPs.
- Processing retains original source images and records processed inputs to avoid duplicate additions on later runs.
- Export synchronizes numbered WebPs to the active folder and removes stale numbered WebPs there, leaving other files alone.

Earlier changes are documented in `release-notes/`.
