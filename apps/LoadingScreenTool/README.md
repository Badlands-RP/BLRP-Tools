# Loading Screen Tool

Open this tool from BLRP Tools Hub. Choose the exact folder containing the images to process, such as `loading screen\Halloween`, and choose the server's active backgrounds folder. Both paths are saved between launches. On the current machine, the tool suggests the existing `Desktop\loading screen` and `BadlandsRP\resources\blrp_loadingscreen\backgrounds\halloween` folders until you choose different paths.

**Process images** works on files directly inside the project folder. It orders existing `1.webp`, `2.webp`, etc. numerically and closes gaps without changing their relative order. It converts new PNG, JPEG, BMP, GIF and unnumbered WebP files to WebP and appends them after the existing images. Original source files stay in place. A small `.blrp-loading-screen.json` ledger prevents the same source file from being added again on the next run. If you remove a numbered WebP, processing closes the gap; if you replace a source file with new content, it is treated as a new image.

**Export to active folder** copies the project's numbered WebPs to the linked active folder and removes numbered WebPs there that are no longer in the project. Other files in the active folder are left alone. Process the project folder first when its numbering has gaps.

Run the offline workflow check with `BLRP.LoadingScreenTool.dll --self-test` through `dotnet`.
