# Loading Screen Tool

Open this tool from BLRP Tools Hub. Choose the exact folder containing the images to process, such as `loading screen\Halloween`, and choose the server's active backgrounds folder. Both paths are saved between launches. On the current machine, the tool suggests the existing `Desktop\loading screen` and `BadlandsRP\resources\blrp_loadingscreen\backgrounds\halloween` folders until you choose different paths.

**Process images** works on files directly inside the project folder. It orders existing `1.webp`, `2.webp`, etc. numerically and closes gaps without changing their relative order. It converts new PNG, JPEG, BMP, GIF and unnumbered WebP files to WebP and appends them after the existing images. Original source files stay in place. A small `.blrp-loading-screen.json` ledger prevents the same source file from being added again on the next run. If you remove a numbered WebP, processing closes the gap; if you replace a source file with new content, it is treated as a new image.

**Export to active folder** copies the project's numbered WebPs to the linked active folder and removes numbered WebPs there that are no longer in the project. Other files in the active folder are left alone. Process the project folder first when its numbering has gaps.

**Check + resize** scans supported images in the project folder and center crops any image with dimensions other than 1920×1080. It lists each changed file and its original dimensions for review. Original files are backed up under `.blrp-image-backups`. Previously processed source images remain recorded, so resizing them does not add duplicate numbered WebPs on the next process run. GIF resizing uses a single decoded frame.

**Remove UI elements** is a separate tab for numbered WebPs. Select one or more images, choose a FiveM watermark or FPS corner preset, or drag a custom rectangle on the preview. The tool replaces that area with the best matching nearby image patch. Review every result before exporting; complex backgrounds may need manual editing. Originals are retained under `.blrp-image-backups`.

Run the offline workflow check with `BLRP.LoadingScreenTool.dll --self-test` through `dotnet`.
