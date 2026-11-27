using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Badlands.LiveryTool;
using CodeWalker.GameFiles;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        checks++; Console.WriteLine("PASS " + description);
    }
    private static void Reject(Action action, string description)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or OperationCanceledException or InvalidOperationException or UnauthorizedAccessException)
        { Check(true, description); return; }
        throw new Exception("Expected rejection: " + description);
    }

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--modkit-real-check")
        {
            var catalog = VehicleCatalog.Scan(args[1], default);
            string master = ModkitConversion.ResolveMasterList(catalog, null);
            string registry = File.ReadAllText(master);
            var car = catalog.Vehicles.First(vehicle => vehicle.Problems.Length == 0 && vehicle.Kits.Length == 1 &&
                vehicle.Kits[0].Liveries.Length == 0 && vehicle.Kits[0].Xml.Element("id") != null && !registry.Contains(vehicle.Kits[0].Name, StringComparison.OrdinalIgnoreCase));
            var inputs = car.MetadataPaths.Append(car.Kits[0].Path).Append(master).Distinct().ToDictionary(path => path, File.ReadAllBytes);
            using var plan = new LiveryBatchWorkflow().PrepareModkits(catalog, [new(car, car.Kits[0])], null, default);
            Check(plan.Modkits.Count == 1 && plan.Items.Count == 0 && plan.StagedFiles.Keys.Contains(master), "real vehicle without livery mods stages a dedicated kit and registry entry");
            Check(inputs.All(input => File.ReadAllBytes(input.Key).SequenceEqual(input.Value)), "real conversion review preserves every original repository file");
            Check(plan.Modkits[0].Id <= ushort.MaxValue && !catalog.Kits.Any(kit => (int?)kit.Xml.Element("id")?.Attribute("value") == plan.Modkits[0].Id), "real registry allocation avoids all loaded metadata IDs");
            XElement stagedKit = XDocument.Load(plan.StagedFiles[car.Kits[0].Path]).Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")!.Value == plan.Modkits[0].Name);
            XElement expectedKit = XElement.Parse(car.Kits[0].Xml.ToString(SaveOptions.DisableFormatting));
            expectedKit.Element("kitName")!.Value = plan.Modkits[0].Name;
            expectedKit.Element("id")!.SetAttributeValue("value", plan.Modkits[0].Id);
            Check(XNode.DeepEquals(stagedKit, expectedKit), "real conversion preserves all existing mods, flags and tuning data in the cloned kit");
            Console.WriteLine(plan.Review());
            return 0;
        }
        if (args.Length == 1 && args[0] is "--ui-check" or "--modkit-ui-check")
        {
            int result = 1;
            var thread = new Thread(() => result = WorkspaceCheck(args[0] == "--modkit-ui-check"));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start(); thread.Join();
            return result;
        }
        string root = Path.Combine(Path.GetTempPath(), "BLRP-Livery-Checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            LuaChecks();
            NamingChecks();
            ModkitChecks();
            RoutingChecks();
            Reject(() => PreviewMaterialSupport.Validate(XDocument.Parse("<Fragment><Shaders><Item><FileName>unsupported_custom_shader.sps</FileName></Item></Shaders></Fragment>")), "unsupported preview materials cannot silently use generic rendering");
            Fixture(root);
            VehicleCatalog catalog = VehicleCatalog.Scan(root, default);
            CatalogVehicle vehicle = catalog.Vehicles.Single(item => item.Model == "testcar");
            Check(catalog.Vehicles.Any(item => item.Model == "newcar"), "vehicles without liveries are discovered");
            Check(vehicle.DisplayName == "Test Vehicle", "display name resolves from resource text entries");
            Check(catalog.GameBuild == "3095", "target build read from repository configuration");
            VehicleKit kit = vehicle.Kits.Single();
            VehicleTemplate template = catalog.Templates(kit).Single();
            Check(kit.Name == "42_testcar" && kit.Liveries.Length == 1, "vehicle linked to exact modkit rather than first visibleMods");
            string image = Path.Combine(root, "art.png");
            var unlocked = new LiveryImport(vehicle, kit, template, "testcar_livery7", image, "Public Artwork", "", "", false);
            var locked = unlocked with { DisplayName = "Display differs from access", AccessGroup = "Test Gang", Permission = "warehouse" };
            var workflow = new LiveryBatchWorkflow();
            var baseline = Snapshot(root);
            string external = root + "-exports";
            Directory.CreateDirectory(external);
            try
            {
                string firstFolder = Path.Combine(external, "first");
                string secondFolder = Path.Combine(external, "second");
                Directory.CreateDirectory(firstFolder); Directory.CreateDirectory(secondFolder);
                using (LiveryPlan chosen = workflow.Prepare(catalog, [unlocked with { ExportFolder = firstFolder }, locked with { ExportFolder = secondFolder }], default))
                {
                    Check(chosen.Items[0].Destination.StartsWith(firstFolder + Path.DirectorySeparatorChar) && chosen.Items[1].Destination.StartsWith(secondFolder + Path.DirectorySeparatorChar), "each livery uses its manually selected export folder");
                    Check(Same(root, baseline) && !Directory.GetFiles(external, "*.yft", SearchOption.AllDirectories).Any(), "review stages external exports without changing assets or repository metadata");
                    workflow.Install(chosen, default);
                    Check(chosen.Items.All(item => File.Exists(item.Destination)), "assets export to different folders in one operation");
                    string permissions = File.ReadAllText(Path.Combine(root, "resources", "lscustoms", "client", "blrp_custom_liveries.lua"));
                    Check(permissions.Contains("Test Gang:warehouse"), "manual-folder exports update repository metadata and permissions");
                }
                Restore(root, baseline);
                foreach (string file in Directory.GetFiles(external, "*.yft", SearchOption.AllDirectories)) File.Delete(file);
                using (LiveryPlan chosen = workflow.Prepare(catalog, [unlocked with { ExportFolder = firstFolder }, locked with { ExportFolder = secondFolder }], default))
                {
                    using var cancel = new CancellationTokenSource();
                    int writes = 0;
                    Reject(() => workflow.Install(chosen, cancel.Token, new InlineProgress(_ => { if (++writes == 3) cancel.Cancel(); })), "cancellation during external-folder export is reported");
                    Check(Same(root, baseline) && !Directory.GetFiles(external, "*.yft", SearchOption.AllDirectories).Any(), "external assets and repository metadata roll back together");
                }
            }
            finally { Directory.Delete(external, true); }
            using (LiveryPlan plan = workflow.Prepare(catalog, [unlocked, locked], default))
            {
                Check(Same(root, baseline), "review and conversion do not modify repository files");
                Check(plan.Items.Select(item => item.Slot).SequenceEqual(new[] { 1, 2 }), "slots follow modkit order, not filename 7");
                Check(plan.Items.Select(item => item.Name).SequenceEqual(new[] { "testcar_livery8", "testcar_livery9" }), "batch filenames continue the vehicle's existing series without adding blrp");
                Check(plan.Items.Select(item => item.Name).Distinct().Count() == 2, "batch allocates unique streamed names");
                string luaPath = plan.StagedFiles.Keys.Single(path => path.EndsWith(".lua"));
                string lua = File.ReadAllText(plan.StagedFiles[luaPath]);
                Check(!LuaLiveryEditor.Slots(lua, "testcar").Contains(1) && LuaLiveryEditor.Slots(lua, "testcar").Contains(2), "public entry has no access restriction");
                Check(lua.Contains("Test Gang:warehouse") && !lua.Contains("Display differs from access:warehouse"), "display name and access group are independent");
                Check(lua.Contains("[0] = 'Original', -- untouched") && lua.Contains("\r\n"), "missing trailing comma repaired before comment; line endings preserved");
                foreach (PlannedLivery item in plan.Items)
                {
                    var yft = new YftFile(); yft.Load(File.ReadAllBytes(item.StagedAsset));
                    Check(yft.Fragment.Name.EndsWith(item.Name) && yft.Fragment.Drawable.ShaderGroup.TextureDictionary.Textures.data_items.Single().Name == item.Name, "compiled asset reload has unique internal identities");
                }
                workflow.Install(plan, default);
                Check(plan.Items.All(item => File.Exists(item.Destination)), "batch installs every verified native asset");
                XElement[] kits = XDocument.Load(kit.Path).Descendants("Kits").Elements("Item").ToArray();
                Check(kits[0].Element("visibleMods")!.Elements("Item").Single().Element("modelName")!.Value == "unrelated", "unrelated first modkit remains intact");
                Check(kits[1].Element("visibleMods")!.Elements("Item").Count() == 3, "selected modkit receives both entries without changing existing indices");
                Check(File.ReadAllBytes(luaPath).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF8 BOM preserved");
            }
            Restore(root, baseline);
            catalog = VehicleCatalog.Scan(root, default);
            vehicle = catalog.Vehicles.Single(item => item.Model == "testcar"); kit = vehicle.Kits.Single(); template = catalog.Templates(kit).Single();
            unlocked = unlocked with { Vehicle = vehicle, Kit = kit, Template = template };
            CatalogVehicle other = catalog.Vehicles.Single(item => item.Model == "othercar");
            VehicleKit otherKit = other.Kits.Single();
            using (LiveryPlan mixed = workflow.Prepare(catalog, [unlocked, unlocked with { Vehicle = other, Kit = otherKit, Template = catalog.Templates(otherKit).Single(), Texture = "othercar_livery99" }], default))
            {
                Check(mixed.Items.Select(item => item.Import.Vehicle.Model).Distinct().Count() == 2, "one queue stages liveries for multiple vehicles");
                Check(mixed.Items.All(item => item.Slot == 1), "different modkits allocate independent slots in the same batch");
                workflow.Install(mixed, default);
                Check(mixed.Items.All(item => File.Exists(item.Destination)), "multi-vehicle batch installs through the same transaction");
            }
            Restore(root, baseline);
            using (LiveryPlan plan = workflow.Prepare(catalog, [unlocked, unlocked], default))
            {
                using var cancel = new CancellationTokenSource();
                int writes = 0;
                Reject(() => workflow.Install(plan, cancel.Token, new InlineProgress(_ => { if (++writes == 2) cancel.Cancel(); })), "cancellation during commit is reported");
                Check(Same(root, baseline), "cancellation restores all original bytes and removes generated outputs");
            }
            using (LiveryPlan plan = workflow.Prepare(catalog, [unlocked], default))
            {
                File.AppendAllText(plan.Items[0].StagedAsset, "tamper");
                Reject(() => workflow.Install(plan, default), "changed staged binary cannot be installed");
                Check(Same(root, baseline), "invalid staged output leaves repository intact");
            }
            using (LiveryPlan plan = workflow.Prepare(catalog, [unlocked], default))
            {
                string lua = plan.StagedFiles.Keys.Single(path => path.EndsWith(".lua"));
                File.SetAttributes(lua, FileAttributes.ReadOnly);
                try { Reject(() => workflow.Install(plan, default), "write failure during commit is reported"); }
                finally { File.SetAttributes(lua, FileAttributes.Normal); }
                Check(Same(root, baseline), "write failure rolls back earlier asset and metadata writes");
            }
            using (LiveryPlan plan = workflow.Prepare(catalog, [unlocked], default))
            {
                File.AppendAllText(image, "changed");
                Reject(() => workflow.Install(plan, default), "changed input after review is rejected");
                Restore(root, baseline);
            }
            Reject(() => workflow.Prepare(catalog, [unlocked with { Label = "EXISTING_LABEL" }], default), "duplicate labels rejected across resources");
            Reject(() => workflow.Prepare(catalog, [unlocked with { OutputName = "unrelated" }], default), "duplicate streamed name rejected across resources");
            Reject(() => workflow.Prepare(catalog, [unlocked with { Permission = "warehouse" }], default), "public entry cannot carry an orphan permission");
            string wide = Path.Combine(root, "wide.png");
            using (var bitmap = new Bitmap(8, 4)) { using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.FromArgb(128, Color.Red)); bitmap.Save(wide); }
            Reject(() => workflow.Prepare(catalog, [unlocked with { Image = wide }], default), "dimension mismatch requires explicit fitting");
            using (LiveryPlan plan = workflow.Prepare(catalog, [unlocked with { Image = wide, FitImage = true }], default))
            {
                var yft = new YftFile(); yft.Load(File.ReadAllBytes(plan.Items[0].StagedAsset));
                Texture texture = yft.Fragment.Drawable.ShaderGroup.TextureDictionary.Textures.data_items.Single();
                Check(texture.Width == 4 && texture.Height == 4 && texture.Levels == 3, "fit operation produces template dimensions and full mip chain");
            }
            File.Delete(wide);
            MultiTextureCheck(root, template.Path, image);
            Check(!Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(path => path.EndsWith(".bak") || path.EndsWith(".tmp")), "no backup or task temporary files remain in repository");
            if (args.Length > 0)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                VehicleCatalog real = VehicleCatalog.Scan(args[0], default, new InlineProgress(Console.WriteLine));
                Console.WriteLine($"REAL CATALOG {real.Vehicles.Length} vehicles, {real.Files.Length} files, {real.Warnings.Length} warnings in {stopwatch.Elapsed}.");
                foreach (var car in real.Vehicles.Where(car => car.Model is "baller7r" or "aleutian")) Console.WriteLine(car + " — " + car.Status);
                Check(real.Vehicles.Any(car => car.Model == "baller7r" && car.Kits.Any()), "real addon vehicle resolves modkit");
                Check(LiveryRouting.AccessFile(real, null, default).EndsWith("blrp_custom_liveries.lua"), "real repository access configuration resolves automatically");
                // Read-only preview preparation against a real addon model. Archive resolution is optional.
                if (args.Length > 1)
                {
                    string previewModel = args.Length > 2 ? args[2] : "aleutian";
                    var car = real.Vehicles.Single(car => car.Model == previewModel);
                    using var carOnly = await VehiclePreviewAssets.Prepare(real, car, null, args[1], "mp2023_02", default, new InlineProgress(Console.WriteLine));
                    Check(File.Exists(carOnly.ModelPath), "real vehicle preview stages and reloads without a queued livery");
                    var realKit = car.Kits.Single();
                    var realTemplate = args.Length > 3
                        ? real.Templates(realKit).Single(template => Path.GetFileName(template.Path).Equals(args[3], StringComparison.OrdinalIgnoreCase))
                        : TemplateCompatibility.Choose(real.Templates(realKit)) ?? throw new Exception("Equivalent real templates were not selected automatically.");
                    Check(real.Templates(realKit).Contains(realTemplate), args.Length > 3 ? "explicit real template belongs to the selected vehicle's modkit" : "equivalent real templates normalize only asset-derived root identities");
                    string textureName = LiveryAssetBuilder.SuggestTexture(realTemplate.Path)!;
                    var realImport = unlocked with { Vehicle = car, Kit = realKit, Template = realTemplate, Texture = textureName, FitImage = true };
                    using var pending = await VehiclePreviewAssets.Prepare(real, car, realImport, args[1], "mp2023_02", default, new InlineProgress(Console.WriteLine));
                    Check(pending.Description.Contains("pending livery bindings"), "real vehicle preview applies the pending texture to matching materials");
                    if (car.Model == "caracara2")
                    {
                        using var review = workflow.Prepare(real, [realImport with { AccessGroup = "Convention check", Permission = "", DisplayName = "Convention check" }], default);
                        var item = review.Items.Single();
                        Check(item.Name.StartsWith("cara2_liv") && !item.Name.Contains("blrp") && item.Label.StartsWith("CARA2_LIV_"), "real Caracara2 review follows its existing filenames and text labels");
                        string access = LiveryRouting.AccessFile(real, null, default);
                        string staged = File.ReadAllText(review.StagedFiles[access]);
                        int added = staged.IndexOf("AddTextEntry('" + item.Label + "'");
                        Check(added > staged.IndexOf("AddTextEntry('CARA2_LIV_14'") && added < staged.IndexOf("AddTextEntry('CAV3_LIVERY11'"), "real Caracara2 name is staged with its header group rather than beside custom_liveries");
                        Check(LuaLiveryEditor.Slots(staged, "caracara2").Contains(item.Slot) && !File.Exists(item.Destination), "real review stages the correct access slot without exporting to the repository");
                        Console.WriteLine($"CONVENTION REVIEW {item.Name}.yft / {item.Label} / slot {item.Slot}");
                    }
                }
            }
            Console.WriteLine($"PASS {checks} checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }

    private static void LuaChecks()
    {
        foreach (string lua in new[] { "custom_liveries = {}", "local custom_liveries = { [`car`] = {[0] = 'old'} }", "custom_liveries = {\n\t[`car`] = {\n [0] = {'old', 1} --comment\n }\n}" })
        {
            string next = LuaLiveryEditor.Append(lua, "car", 1, "LABEL", "A 'quoted' name", "Gang", "logs");
            Check(LuaLiveryEditor.Slots(next, "car").Contains(1), "Lua insertion supports empty, compact and table-valued restrictions");
        }
        string tricky = "-- custom_liveries = {\nlocal custom_liveries = { [`car`] = { [0] = 'brace } quote \\\' x' -- } ignored\n } }";
        Check(LuaLiveryEditor.Slots(tricky, "car").SetEquals(new[] { 0 }), "Lua lexer ignores commented delimiters and escaped strings");
        Reject(() => LuaLiveryEditor.Append("custom_liveries = {[`car`] = {[1]='old'}}", "car", 1, "NEW", "name", "Gang", ""), "existing Lua slot cannot be overwritten");
        Reject(() => LuaLiveryEditor.Slots("custom_liveries = {[`car`]={[0]='a',[0]='b'}}", "car"), "duplicate Lua slots detected");
        Reject(() => LuaLiveryEditor.Slots("custom_liveries = {[`car`]={[0]='a' [1]='b'}}", "car"), "invalid existing Lua separators rejected before installation");
        string functional = "custom_liveries = {[`car`] = {[0] = function() if ready then local a = {}; a[42] = true; return hasLiveryFlag(1) end return false end -- untouched function\n}}";
        string withFunction = LuaLiveryEditor.Append(functional, "car", 1, "NEW_FUNC", "New", "Gang", "");
        Check(LuaLiveryEditor.Slots(withFunction, "car").SetEquals(new[] { 0, 1 }) && withFunction.Contains("function() if ready then local a = {}; a[42] = true; return hasLiveryFlag(1) end return false end, -- untouched function"), "existing inline permission functions remain intact and their body keys are not livery slots");
        Reject(() => LuaLiveryEditor.Slots("custom_liveries = {[`car`]={[0]=function() return true}}", "car"), "unterminated restriction function cannot consume its enclosing table");
        string grouped = "-- names\r\nAddTextEntry('CARA2_LIV_11', 'Old') -- keep comment\r\nAddTextEntry('CARA2_LIV_12', 'Existing')\r\nAddTextEntry('CAV3_LIVERY11', 'Other vehicle')\r\n\r\nlocal function flag() return true end\r\n\r\ncustom_liveries = {\r\n  [`caracara2`] = {\r\n    [10] = 'Old',\r\n    [11] = 'Existing' -- keep slot comment\r\n  },\r\n\r\n  [`cavalcade3`] = {\r\n    [10] = 'Other vehicle',\r\n  },\r\n}\r\n";
        string groupedNext = LuaLiveryEditor.Append(grouped, "caracara2", 12, "CARA2_LIV_13", "BCH", "Hawks", "logs", ["CARA2_LIV_11", "CARA2_LIV_12"]);
        Check(groupedNext.Contains("AddTextEntry('CARA2_LIV_12', 'Existing')\r\nAddTextEntry('CARA2_LIV_13', 'BCH')\r\nAddTextEntry('CAV3_LIVERY11'") &&
            groupedNext.Contains("local function flag() return true end\r\n\r\ncustom_liveries"), "display names stay in the vehicle's header group before helper functions and access metadata");
        Check(groupedNext.Contains("    [11] = 'Existing', -- keep slot comment\r\n    [12] = 'Hawks:logs',\r\n  },"), "restriction entry follows the existing indentation and preserves trailing comments");
        string tabs = "AddTextEntry(\"CAR_1\", \"Old\")\ncustom_liveries = {\n\t[`car`] = {\n\t\t[0] = 'Old',\n\t},\n}\n";
        string tabbed = LuaLiveryEditor.Append(tabs, "car", 1, "CAR_2", "New", "Gang", "", ["CAR_1"]);
        Check(tabbed.Contains("AddTextEntry(\"CAR_1\", \"Old\")\nAddTextEntry(\"CAR_2\", \"New\")") && tabbed.Contains("\t\t[1] = 'Gang',\n\t},"), "Lua quote style and tab indentation follow the surrounding entries");
        string ordered = "AddTextEntry('AIR_1', 'Air')\nAddTextEntry('ASBO_1', 'Asbo')\nAddTextEntry('BOOR_1', 'Boor')\ncustom_liveries = {\n  [`zspecial`] = {[0]='Special'},\n\n  [`airbus`] = {\n    [0] = 'Air',\n  },\n\n  [`asbo`] = {\n    [0] = 'Asbo',\n  },\n\n  [`boor`] = {\n    [0] = 'Boor',\n  },\n}\n";
        string orderedNext = LuaLiveryEditor.Append(ordered, "benson", 0, "BEN2_LIV1", "New", "Gang", "");
        Check(orderedNext.IndexOf("'ASBO_1'") < orderedNext.IndexOf("'BEN2_LIV1'") && orderedNext.IndexOf("'BEN2_LIV1'") < orderedNext.IndexOf("'BOOR_1'") &&
            orderedNext.IndexOf("[`zspecial`]") < orderedNext.IndexOf("[`airbus`]") && orderedNext.IndexOf("[`asbo`]") < orderedNext.IndexOf("[`benson`]") && orderedNext.IndexOf("[`benson`]") < orderedNext.IndexOf("[`boor`]"), "new vehicle entries join the main ordered section without moving special blocks");
    }

    private static void NamingChecks()
    {
        VehicleKit Kit(params (string Name, string Label)[] mods) => new("kit.meta", "kit", new XElement("Item", new XElement("visibleMods", mods.Select(mod =>
            new XElement("Item", new XElement("type", "VMT_LIVERY_MOD"), new XElement("modelName", mod.Name), new XElement("modShopLabel", mod.Label))))));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "othercar_blrp_liv1" };
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kit = Kit(("cara2_liv11", "CARA2_LIV_11"), ("cara2_liv14", "CARA2_LIV_14"));
        var next = LiveryNaming.Suggest(kit, new VehicleTemplate("cara2_liv14.yft", new XElement("Item")), "caracara2", names, labels);
        Check(next == ("cara2_liv15", "CARA2_LIV_15"), "model abbreviations and distinct label separators are preserved; other vehicles' blrp names have no effect");
        names.Add(next.Name); labels.Add(next.Label);
        Check(LiveryNaming.Suggest(kit, new VehicleTemplate("cara2_liv14.yft", new XElement("Item")), "caracara2", names, labels) == ("cara2_liv16", "CARA2_LIV_16"), "filename and label allocation advances together around collisions");
        var blrp = Kit(("car_blrp_liv009", "CAR_BLRP_LIV_009"));
        Check(LiveryNaming.Suggest(blrp, new VehicleTemplate("car_blrp_liv009.yft", new XElement("Item")), "car", names, labels) == ("car_blrp_liv010", "CAR_BLRP_LIV_010"), "existing blrp series and numeric padding are retained for that vehicle");
        var plain = Kit(("special_artwork", "SPECIAL_ARTWORK"));
        Check(LiveryNaming.Suggest(plain, new VehicleTemplate("special_artwork.yft", new XElement("Item")), "newcar", names, labels).Name == "newcar_liv1", "new filename series omit blrp by default");
    }

    private static void ModkitChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "BLRP-Modkit-Checks-" + Guid.NewGuid().ToString("N"));
        string external = root + "-registry.txt";
        Directory.CreateDirectory(root);
        try
        {
            Fixture(root);
            string colsPath = Path.Combine(root, "resources", "[cars]", "addon", "data", "carcols.meta");
            var cols = XDocument.Load(colsPath, LoadOptions.PreserveWhitespace);
            foreach (var kit in cols.Descendants("Kits").Elements("Item"))
                kit.Add(new XElement("id", new XAttribute("value", kit.Element("kitName")!.Value == "42_testcar" ? 42 : 43)));
            cols.Descendants("Kits").Single().Add(new XElement("Item", new XElement("kitName", "unused_reserved"), new XElement("id", new XAttribute("value", 60010)), new XElement("visibleMods")));
            cols.Save(colsPath, SaveOptions.DisableFormatting);
            string master = Path.Combine(root, "resources", "[registry]", "lists", "Vehicle Modkit Master List.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(master)!);
            File.WriteAllText(master, "# Existing registry\r\n60000_bl_existing_modkit\r\n", new UTF8Encoding(true));
            var baseline = Snapshot(root);
            var workflow = new LiveryBatchWorkflow();
            VehicleCatalog Scan() => VehicleCatalog.Scan(root, default);
            var catalog = Scan();
            var car = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
            Check(ModkitConversion.ResolveMasterList(catalog, null) == master && ModkitConversion.ResolveMasterList(catalog, Path.GetRelativePath(root, master)) == master, "master list discovery and relative overrides work in a relocated repository");
            // This vehicle has no livery mods at all, but its remaining modkit must still be preserved.
            cols = XDocument.Load(colsPath, LoadOptions.PreserveWhitespace);
            cols.Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")!.Value == "42_testcar").Element("visibleMods")!.RemoveNodes();
            cols.Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")!.Value == "42_testcar").Element("visibleMods")!.Add(
                new XElement("Item", new XElement("modelName", "unrelated"), new XElement("type", "VMT_GRILL"), new XElement("bone", "chassis")));
            cols.Save(colsPath, SaveOptions.DisableFormatting);
            catalog = Scan(); car = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
            XElement oldKit = new(car.Kits.Single().Xml);
            var noLiveryBaseline = Snapshot(root);
            using (var plan = workflow.PrepareModkits(catalog, [new(car, car.Kits.Single())], null, default))
            {
                Check(plan.Items.Count == 0 && plan.Modkits.Single().Name == "60020_bl_testcar_modkit", "a vehicle with no liveries can get a dedicated modkit without an image or template");
                Check(Same(root, noLiveryBaseline), "reviewing modkit conversion leaves source metadata and master list untouched");
                workflow.Install(plan, default);
                var saved = XDocument.Load(colsPath, LoadOptions.PreserveWhitespace);
                Check(XNode.DeepEquals(saved.Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")!.Value == "42_testcar"), oldKit), "conversion preserves the original modkit and unrelated settings");
                var converted = Scan().Vehicles.Single(vehicle => vehicle.Model == "testcar");
                Check(converted.Kits.Single().Name == "60020_bl_testcar_modkit" && converted.Kits.Single().Liveries.Length == 0, "the selected vehicle points at its verified converted modkit");
                Check(converted.Kits.Single().Xml.Element("visibleMods")!.Elements("Item").Single().Element("type")!.Value == "VMT_GRILL", "vehicles without livery mods keep their existing non-livery upgrades in the converted kit");
                Check(File.ReadAllText(master).EndsWith("60020_bl_testcar_modkit\r\n") && File.ReadAllBytes(master).AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "master list updates retain existing entries, CRLF and UTF-8 BOM");
            }
            catalog = Scan(); car = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
            using (var repeat = workflow.PrepareModkits(catalog, [new(car, car.Kits.Single())], master, default))
                Check(repeat.StagedFiles.Count == 0, "an already registered dedicated kit is not converted or registered twice");
            Restore(root, baseline);
            catalog = Scan(); car = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
            var originalKit = car.Kits.Single();
            var import = new LiveryImport(car, originalKit, catalog.Templates(originalKit).Single(), "testcar_livery7", Path.Combine(root, "art.png"), "New livery", "", "", false, ConvertModkit: true);
            using (var plan = workflow.Prepare(catalog, [import, import], default, masterList: master))
            {
                Check(plan.Modkits.Count == 1 && plan.Items.Select(item => item.Slot).SequenceEqual(new[] { 1, 2 }), "a livery batch converts each vehicle once and preserves its existing slot order");
                workflow.Install(plan, default);
                var after = Scan().Vehicles.Single(vehicle => vehicle.Model == "testcar");
                Check(after.Kits.Single().Liveries.Length == 3 && XDocument.Load(colsPath).Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")!.Value == originalKit.Name).Element("visibleMods")!.Elements("Item").Count() == 1, "new assets are attached to the converted kit while the source kit stays intact");
            }
            Restore(root, baseline);
            catalog = Scan(); car = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
            string vars = car.MetadataPaths.Single(path => XDocument.Load(path).Descendants("variationData").Any());
            using (var plan = workflow.PrepareModkits(catalog, [new(car, car.Kits.Single())], master, default))
            {
                File.SetAttributes(vars, FileAttributes.ReadOnly);
                try { Reject(() => workflow.Install(plan, default), "modkit export reports a metadata write failure"); }
                finally { File.SetAttributes(vars, FileAttributes.Normal); }
                Check(Same(root, baseline), "failed conversion restores metadata and the master list together");
            }
            using (var plan = workflow.PrepareModkits(catalog, [new(car, car.Kits.Single())], master, default))
            {
                using var cancellation = new CancellationTokenSource();
                int writes = 0;
                Reject(() => workflow.Install(plan, cancellation.Token, new InlineProgress(_ => { if (++writes == 2) cancellation.Cancel(); })), "modkit export cancellation is reported");
                Check(Same(root, baseline), "cancelled conversion restores the original kit references and registry");
            }
            File.WriteAllText(external, "60000_bl_external_modkit\n");
            using (var plan = workflow.PrepareModkits(catalog, [new(car, car.Kits.Single())], external, default))
            {
                workflow.Install(plan, default);
                Check(File.ReadAllText(external).Contains("60020_bl_testcar_modkit"), "an explicitly selected master list may live outside the repository");
            }
            Restore(root, baseline);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(master)!, "Other modkit master list.txt"), "60000_bl_other_modkit\n");
            catalog = Scan();
            Reject(() => ModkitConversion.ResolveMasterList(catalog, null), "ambiguous master lists require a user-selected file");
            Check(ModkitConversion.ResolveMasterList(catalog, master) == master, "manual master list selection resolves discovery ambiguity");
            Restore(root, baseline);
            catalog = Scan(); car = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
            using (var changed = workflow.PrepareModkits(catalog, [new(car, car.Kits.Single())], master, default))
            {
                File.AppendAllText(master, "# external edit\r\n");
                var edited = Snapshot(root);
                Reject(() => workflow.Install(changed, default), "a master-list edit after review invalidates the conversion plan");
                Check(Same(root, edited), "changed master-list detection leaves all metadata untouched");
            }
            Restore(root, baseline);
            catalog = Scan();
            var firstCar = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
            var secondCar = catalog.Vehicles.Single(vehicle => vehicle.Model == "othercar");
            using (var multiple = workflow.PrepareModkits(catalog, [new(firstCar, firstCar.Kits.Single()), new(secondCar, secondCar.Kits.Single())], master, default))
            {
                Check(multiple.Modkits.Select(item => item.Id).SequenceEqual(new[] { 60020, 60030 }), "multiple conversions allocate distinct IDs across one review");
                workflow.Install(multiple, default);
                Check(Scan().Vehicles.Where(vehicle => vehicle.Model is "testcar" or "othercar").Select(vehicle => vehicle.Kits.Single().Name).Distinct().Count() == 2, "multiple vehicle references and registry entries export together");
            }
            Restore(root, baseline);
            string variationsPath = Path.Combine(root, "resources", "[cars]", "addon", "data", "carvariations.meta");
            var sharedVars = XDocument.Load(variationsPath, LoadOptions.PreserveWhitespace);
            sharedVars.Descendants("variationData").Elements("Item").Single(item => item.Element("modelName")!.Value == "othercar").Element("kits")!.Element("Item")!.Value = "42_testcar";
            sharedVars.Save(variationsPath, SaveOptions.DisableFormatting);
            catalog = Scan(); car = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
            using (var shared = workflow.PrepareModkits(catalog, [new(car, car.Kits.Single())], master, default))
            {
                workflow.Install(shared, default);
                var after = Scan();
                Check(after.Vehicles.Single(vehicle => vehicle.Model == "othercar").Kits.Single().Name == "42_testcar" && after.Vehicles.Single(vehicle => vehicle.Model == "testcar").Kits.Single().Name == "60020_bl_testcar_modkit", "converting a shared kit leaves every other vehicle on its original kit");
            }
            Restore(root, baseline);
            File.AppendAllText(master, "60000_bl_conflicting_modkit\r\n");
            catalog = Scan(); car = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
            Reject(() => workflow.PrepareModkits(catalog, [new(car, car.Kits.Single())], master, default), "conflicting IDs in the master list cannot be extended silently");
        }
        finally { Directory.Delete(root, true); if (File.Exists(external)) File.Delete(external); }
    }

    private static int WorkspaceCheck(bool modkitOnly = false)
    {
        string root = Path.Combine(Path.GetTempPath(), "BLRP-Livery-Workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int result = 1;
        try
        {
            Fixture(root);
            string master = Path.Combine(root, "resources", "registry", "Modkit Master List.txt");
            if (modkitOnly)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(master)!);
                File.WriteAllText(master, "60000_bl_existing_modkit\n");
                string colsPath = Path.Combine(root, "resources", "[cars]", "addon", "data", "carcols.meta");
                var metadata = XDocument.Load(colsPath);
                var kit = metadata.Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")!.Value == "42_testcar");
                kit.Element("visibleMods")!.RemoveNodes();
                kit.Add(new XElement("id", new XAttribute("value", 42)));
                metadata.Save(colsPath);
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new LiveryWorkspaceForm(persistSettings: false, initialRepository: root) { Opacity = 0, ShowInTaskbar = false };
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var type = typeof(LiveryWorkspaceForm);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(form)!;
            Task Call(string name) => (Task)type.GetMethod(name, flags)!.Invoke(form, null)!;
            bool running = false;
            form.Shown += (_, _) => timer.Start();
            timer.Tick += async (_, _) =>
            {
                if (running || Field<bool>("busy")) return;
                running = true;
                try
                {
                    if (clock.Elapsed.TotalSeconds > 20) throw new TimeoutException("Workspace scan timed out.");
                    VehicleCatalog catalog = Field<VehicleCatalog>("catalog");
                    Field<ListBox>("vehicles").SelectedItem = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
                    if (modkitOnly)
                    {
                        Field<LiveryTabs>("tabs").SelectedIndex = 3;
                        Field<CheckBox>("convertModkit").Checked = true;
                        Check(Field<Label>("conversionTarget").Text.EndsWith("testcar") && Field<System.ComponentModel.BindingList<ImportRow>>("rows").Count == 0, "Advanced conversion targets the selected car without requiring queue artwork");
                        await Call("ReviewModkit");
                        Check(Field<Button>("install").Enabled && Field<LiveryPlan>("plan").Items.Count == 0 && Field<LiveryPlan>("plan").Modkits.Count == 1, "the checkbox can review conversion and master-list registration without a livery template");
                        Check(Field<Button>("install").Text == "Export modkit", "conversion-only review clearly labels its export action");
                        Check(!File.ReadAllText(master).Contains("testcar_modkit"), "the conversion-only review does not update the registry before export");
                        await Call("Install");
                        Check(File.ReadAllText(master).Contains("60010_bl_testcar_modkit"), "the workspace exports the converted kit reference and master-list entry together");
                        catalog = Field<VehicleCatalog>("catalog");
                        Field<ListBox>("vehicles").SelectedItem = catalog.Vehicles.Single(vehicle => vehicle.Model == "othercar");
                        Check(!Field<CheckBox>("convertModkit").Checked && Field<Label>("conversionTarget").Text.EndsWith("othercar"), "conversion is enabled per vehicle rather than leaking into another selected car");
                        Field<TextBox>("masterList").Text = Path.GetRelativePath(root, master);
                        Field<TextBox>("repository").Text = root + "-another-repository";
                        Check(Field<TextBox>("masterList").Text.Length == 0, "changing repositories clears the old master-list override for portable discovery");
                        result = 0;
                        return;
                    }
                    type.GetMethod("PreviewArtwork", flags)!.Invoke(form, new object[] { Path.Combine(root, "art.png") });
                    var rows = Field<System.ComponentModel.BindingList<ImportRow>>("rows");
                    Check(rows.Count == 1 && rows[0].Vehicle == catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar").Key && !Field<bool>("previewVehicleOnly"), "preview artwork creates a pending livery for the viewed car");
                    // Avoid opening the GPU renderer during the workspace transaction check.
                    Field<System.Windows.Forms.Timer>("previewDebounce").Stop();
                    Field<LiveryTabs>("tabs").SelectedIndex = 4;
                    Check(Field<LiveryTabs>("tabs").SelectedTab?.Text == "Help", "workflow help is available inside the tool");
                    Field<LiveryTabs>("tabs").SelectedIndex = 0;
                    type.GetMethod("AddImages", flags)!.Invoke(form, new object[] { new[] { Path.Combine(root, "art.png") } });
                    Check(rows.Count == 2 && Field<DataGridView>("queue").Visible, "workspace displays multiple queued liveries");
                    Field<CheckBox>("convertModkit").Checked = true;
                    Check(rows.All(row => row.ConvertModkit), "the conversion checkbox applies consistently to every queued livery of the selected vehicle");
                    Field<CheckBox>("convertModkit").Checked = false;
                    var grid = Field<DataGridView>("queue");
                    var configure = type.GetMethod("AutoConfigure", flags)!;
                    rows[1].Vehicle = catalog.Vehicles.Single(vehicle => vehicle.Model == "othercar").Key;
                    configure.Invoke(form, new object[] { rows[1] });
                    grid.CurrentCell = grid.Rows[0].Cells["LiveryName"];
                    type.GetMethod("UpdateAdvanced", flags)!.Invoke(form, null);
                    grid.CurrentCell = grid.Rows[1].Cells["LiveryName"];
                    type.GetMethod("ApplyOverrides", flags)!.Invoke(form, null);
                    Check(rows[1].Kit != null && catalog.Vehicles.Single(vehicle => vehicle.Key == rows[1].Vehicle).Kits.Contains(rows[1].Kit!), "switching queue vehicles binds Advanced overrides to the new current row");
                    rows[1].Vehicle = rows[0].Vehicle;
                    configure.Invoke(form, new object[] { rows[1] });
                    type.GetMethod("UpdateAdvanced", flags)!.Invoke(form, null);
                    await Call("Scan");
                    type.GetMethod("ApplyOverrides", flags)!.Invoke(form, null);
                    catalog = Field<VehicleCatalog>("catalog");
                    Check(rows.All(row => row.Kit == null || catalog.Vehicles.Single(vehicle => vehicle.Key == row.Vehicle).Kits.Contains(row.Kit)), "rescanning and applying Advanced overrides cannot restore a stale modkit object");
                    foreach (ImportRow row in rows)
                    {
                        if (rows.IndexOf(row) == 1)
                        {
                            row.ExportFolder = Path.Combine(root, "manual-export");
                            Directory.CreateDirectory(row.ExportFolder);
                        }
                        row.AccessGroup = row.Permission = "";
                    }
                    await Call("Review");
                    Check(Field<Button>("install").Enabled, "review stays valid after queue refresh and page navigation");
                    Check(rows[0].AutomaticFolder && rows[0].ExportFolder.EndsWith("stream") && !rows[1].AutomaticFolder, "review resolves the stream folder automatically and preserves manual overrides");
                    await Call("Install");
                    Check(rows.All(row => row.Completed && row.Status.StartsWith("Exported")), "workspace retains per-item export completion results");
                    Check(Directory.GetFiles(rows[1].ExportFolder, "*.yft").Length == 1 && File.Exists(Path.Combine(rows[0].ExportFolder, "testcar_livery8.yft")), "workspace exports automatic and manual destinations together");
                    Check(!Field<Button>("install").Enabled, "completed exports cannot be submitted twice");
                    catalog = Field<VehicleCatalog>("catalog");
                    Field<ListBox>("vehicles").SelectedItem = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
                    type.GetMethod("AddImages", flags)!.Invoke(form, new object[] { new[] { Path.Combine(root, "art.png") } });
                    rows[2].AccessGroup = rows[2].Permission = "";
                    await Call("Review");
                    var secondPlan = Field<LiveryPlan>("plan");
                    Check(secondPlan.Items.Count == 1 && secondPlan.Items[0].Slot == 3 && secondPlan.Items[0].Name == "testcar_livery10", "the next batch refreshes metadata and allocates the next slot without a manual scan");
                    Check(rows.Take(2).All(row => row.Completed && row.Status.StartsWith("Exported")), "automatic refresh preserves completed queue results");
                    await Call("Install");
                    Check(rows.All(row => row.Completed) && File.Exists(Path.Combine(rows[2].ExportFolder, "testcar_livery10.yft")), "a second export succeeds without re-exporting completed rows");
                    result = 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); }
                finally { timer.Stop(); form.Close(); }
            };
            Application.Run(form);
            return result;
        }
        finally { Directory.Delete(root, true); }
    }

    private static void RoutingChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "BLRP-Livery-Routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Fixture(root);
            var catalog = VehicleCatalog.Scan(root, default);
            var car = catalog.Vehicles.Single(vehicle => vehicle.Model == "testcar");
            Check(LiveryRouting.ExportFolder(catalog, car.Kits.Single()) == Path.Combine(car.ResourcePath, "stream"), "export folder follows the exact modkit's existing stream assets");
            string lua = LiveryRouting.AccessFile(catalog, null, default);
            string renamed = Path.Combine(Path.GetDirectoryName(lua)!, "renamed-access.lua");
            File.Move(lua, renamed);
            File.WriteAllText(lua, "-- custom_liveries = {}\nlocal example = 'custom_liveries = {}'");
            catalog = VehicleCatalog.Scan(root, default);
            Check(LiveryRouting.AccessFile(catalog, null, default) == renamed, "Lua configuration is discovered by assignment, independent of filename and comments");
            string duplicate = Path.Combine(Path.GetDirectoryName(lua)!, "second.lua");
            File.Copy(renamed, duplicate);
            catalog = VehicleCatalog.Scan(root, default);
            Reject(() => LiveryRouting.AccessFile(catalog, null, default), "competing Lua configurations require a manual choice");
            Check(LiveryRouting.AccessFile(catalog, renamed, default) == renamed, "manual Lua selection resolves configuration ambiguity");
            Reject(() => LiveryRouting.AccessFile(catalog, lua, default), "manual Lua selection must contain a valid livery table");
            var kit = car.Kits.Single();
            string template = catalog.Templates(kit).Single().Path;
            string work = Path.Combine(root, "compatibility");
            Directory.CreateDirectory(work);
            var original = new YftFile(); original.Load(File.ReadAllBytes(template));
            var xml = XDocument.Parse(YftXml.GetXml(original, work));
            XElement bone = xml.Descendants("Bones").Elements("Item").Single();
            string first = Path.Combine(work, "match_a.yft"), second = Path.Combine(work, "match_b.yft");
            bone.Element("Name")!.Value = "match_a_ng";
            File.WriteAllBytes(first, XmlYft.GetYft(xml.ToString(), work).Save());
            bone.Element("Name")!.Value = "match_b_ng";
            File.WriteAllBytes(second, XmlYft.GetYft(xml.ToString(), work).Save());
            var equivalent = new[] { new VehicleTemplate(first, new XElement("Item")), new VehicleTemplate(second, new XElement("Item")) };
            Check(TemplateCompatibility.Choose(equivalent) != null, "asset-derived root names do not require a manual template selection");
            string sourceTexture = LiveryAssetBuilder.SuggestTexture(template)!;
            foreach (var variant in new[] { (Path: first, Asset: "match_a", Texture: "paint_a"), (Path: second, Asset: "match_b", Texture: "match_b") })
            {
                var copy = new XDocument(xml);
                LiveryAssetIdentity.Rename(copy, "testcar_livery7", variant.Asset);
                copy.Descendants("Bones").Elements("Item").Single().Element("Name")!.Value = variant.Asset + "_ng";
                foreach (var name in copy.Descendants("Archetype").Elements("Name").Concat(copy.Descendants("Groups").Elements("Item").Elements("Name"))) name.Value = variant.Asset;
                foreach (var name in copy.Descendants("TextureDictionary").Elements("Item").Elements("Name").Concat(copy.Descendants("Parameters").Elements("Item").Where(item => (string?)item.Attribute("type") == "Texture").Elements("Name")))
                    if (name.Value == sourceTexture) name.Value = variant.Texture;
                File.WriteAllBytes(variant.Path, XmlYft.GetYft(copy.ToString(), work).Save());
            }
            Check(TemplateCompatibility.Choose(equivalent) != null, "mixed asset/texture naming conventions select equivalent templates automatically");
            string renamedAsset = Path.Combine(work, "renamed.yft");
            LiveryAssetBuilder.Build(first, "paint_a", Path.Combine(root, "art.png"), "renamed", renamedAsset, false, default);
            var renamedNative = new YftFile(); renamedNative.Load(File.ReadAllBytes(renamedAsset));
            var renamedXml = XDocument.Parse(YftXml.GetXml(renamedNative));
            Check(renamedXml.Descendants("Archetype").Elements("Name").Concat(renamedXml.Descendants("Groups").Elements("Item").Elements("Name")).All(name => name.Value == "renamed") &&
                renamedXml.Descendants("Bones").Elements("Item").Single().Element("Name")!.Value == "renamed_ng", "native compilation renames associated fragment identities independently of the texture");
            var changed = new YftFile(); changed.Load(File.ReadAllBytes(second));
            xml = XDocument.Parse(YftXml.GetXml(changed, work));
            bone = xml.Descendants("Bones").Elements("Item").Single();
            bone.Element("Translation")!.SetAttributeValue("x", "1");
            File.WriteAllBytes(second, XmlYft.GetYft(xml.ToString(), work).Save());
            Check(TemplateCompatibility.Choose(equivalent) == null, "template matching still rejects differing root-bone transforms");
            string alternate = Path.Combine(root, "resources", "alternate", "stream");
            Directory.CreateDirectory(alternate);
            File.WriteAllText(Path.Combine(alternate, "..", "fxmanifest.lua"), "fx_version 'cerulean'");
            File.Copy(template, Path.Combine(alternate, Path.GetFileName(template)));
            catalog = VehicleCatalog.Scan(root, default);
            Check(LiveryRouting.ExportFolder(catalog, kit) == null, "templates in competing stream folders are not routed by guesswork");
            string protectedModel = Path.Combine(root, "encrypted.yft");
            File.WriteAllBytes(protectedModel, "FXAP\u0001\0\0\0encrypted-test"u8.ToArray());
            try { NativeAssetReader.Read(protectedModel); throw new Exception("Protected asset accepted"); }
            catch (InvalidDataException ex) { Check(ex.Message.Contains("escrow") && ex.Message.Contains("Help"), "protected models produce an actionable escrow explanation before native parsing"); }
            Check(NativeAssetReader.Read(template).SequenceEqual(File.ReadAllBytes(template)), "readable native assets remain available without an escrow resource blanket block");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Fixture(string root)
    {
        string data = Path.Combine(root, "resources", "[cars]", "addon", "data");
        string stream = Path.Combine(root, "resources", "[cars]", "addon", "stream");
        string lua = Path.Combine(root, "resources", "lscustoms", "client");
        Directory.CreateDirectory(data); Directory.CreateDirectory(stream); Directory.CreateDirectory(lua);
        File.WriteAllText(Path.Combine(root, "server.cfg"), "set sv_enforceGameBuild 3095");
        File.WriteAllText(Path.Combine(data, "..", "fxmanifest.lua"), "files {'data/*.meta'}\ndata_file 'CARCOLS_FILE' 'data/carcols.meta'\ndata_file 'VEHICLE_METADATA_FILE' 'data/vehicles.meta'\ndata_file 'VEHICLE_VARIATION_FILE' 'data/carvariations.meta'");
        File.WriteAllText(Path.Combine(data, "vehicle_names.lua"), "AddTextEntry('TESTCAR', 'Test Vehicle')");
        File.WriteAllText(Path.Combine(data, "vehicles.meta"), "<CVehicleModelInfo__InitDataList><InitDatas><Item><modelName>testcar</modelName><txdName>testcar</txdName><gameName>TESTCAR</gameName></Item><Item><modelName>newcar</modelName></Item></InitDatas></CVehicleModelInfo__InitDataList>");
        File.WriteAllText(Path.Combine(data, "carvariations.meta"), "<CVehicleModelInfoVariation><variationData><Item><modelName>testcar</modelName><kits><Item>42_testcar</Item></kits></Item><Item><modelName>newcar</modelName><kits /></Item></variationData></CVehicleModelInfoVariation>");
        File.WriteAllText(Path.Combine(data, "carcols.meta"), "<CVehicleModelInfoVarGlobal>\r\n  <Kits>\r\n    <Item><kitName>other</kitName><visibleMods><Item><modelName>unrelated</modelName><modShopLabel>EXISTING_LABEL</modShopLabel><type>VMT_LIVERY_MOD</type></Item></visibleMods></Item>\r\n    <Item><kitName>42_testcar</kitName><visibleMods>\r\n      <Item><modelName>testcar_livery7</modelName><modShopLabel>OLD</modShopLabel><type>VMT_LIVERY_MOD</type><linkedModels /><turnOffBones /><bone>chassis</bone></Item>\r\n    </visibleMods></Item>\r\n  </Kits>\r\n</CVehicleModelInfoVarGlobal>");
        File.WriteAllText(Path.Combine(lua, "blrp_custom_liveries.lua"), "AddTextEntry('OLD', 'Original')\r\nlocal custom_liveries = {\r\n  [`testcar`] = {\r\n    [0] = 'Original' -- untouched\r\n  },\r\n}\r\n", new UTF8Encoding(true));
        using (var image = new Bitmap(4, 4)) { using var graphics = Graphics.FromImage(image); graphics.Clear(Color.Red); image.Save(Path.Combine(root, "art.png")); }
        var sign = new SignBatchWorkflow();
        var plan = sign.CreatePlan(root, stream, "art", "testcar_livery", 7);
        sign.Build(plan, stream, null, "template");
        File.WriteAllBytes(Path.Combine(stream, "unrelated.yft"), File.ReadAllBytes(Path.Combine(stream, "testcar_livery7.yft")));
        var otherPlan = sign.CreatePlan(root, stream, "art", "othercar_livery", 99);
        sign.Build(otherPlan, stream, null, "template");
        var vehicles = XDocument.Load(Path.Combine(data, "vehicles.meta"));
        vehicles.Descendants("InitDatas").Single().Add(new XElement("Item", new XElement("modelName", "othercar")));
        vehicles.Save(Path.Combine(data, "vehicles.meta"));
        var variations = XDocument.Load(Path.Combine(data, "carvariations.meta"));
        variations.Descendants("variationData").Single().Add(new XElement("Item", new XElement("modelName", "othercar"), new XElement("kits", new XElement("Item", "43_othercar"))));
        variations.Save(Path.Combine(data, "carvariations.meta"));
        var cols = XDocument.Load(Path.Combine(data, "carcols.meta"), LoadOptions.PreserveWhitespace);
        cols.Descendants("Kits").Single().Add(new XElement("Item", new XElement("kitName", "43_othercar"), new XElement("visibleMods",
            new XElement("Item", new XElement("modelName", "othercar_livery99"), new XElement("modShopLabel", "OTHER_OLD"), new XElement("type", "VMT_LIVERY_MOD"), new XElement("linkedModels"), new XElement("turnOffBones")))));
        cols.Save(Path.Combine(data, "carcols.meta"), SaveOptions.DisableFormatting);
    }

    private static void MultiTextureCheck(string root, string template, string image)
    {
        string work = Path.Combine(root, "multi-texture-check");
        Directory.CreateDirectory(work);
        var source = new YftFile(); source.Load(File.ReadAllBytes(template));
        var xml = XDocument.Parse(YftXml.GetXml(source, work));
        XElement dictionary = xml.Descendants("TextureDictionary").Single();
        XElement extra = new(dictionary.Elements("Item").Single());
        extra.Element("Name")!.Value = "larger_unrelated";
        extra.Element("FileName")!.Value = "larger_unrelated.dds";
        new LiveryImageConverter().ConvertForTemplate(image, Path.Combine(work, "larger_unrelated.dds"), 8, 8, true, default);
        extra.Element("Width")!.SetAttributeValue("value", 8); extra.Element("Height")!.SetAttributeValue("value", 8);
        dictionary.Add(extra);
        string multiple = Path.Combine(work, "multiple.yft");
        File.WriteAllBytes(multiple, XmlYft.GetYft(xml.ToString(), work).Save());
        Check(LiveryAssetBuilder.SuggestTexture(multiple) == "testcar_livery7", "material sampler selects the livery texture instead of the largest embedded texture");
        string output = Path.Combine(work, "result.yft");
        LiveryAssetBuilder.Build(multiple, "testcar_livery7", image, "unique_result", output, false, default);
        var before = new YftFile(); before.Load(File.ReadAllBytes(multiple));
        var after = new YftFile(); after.Load(File.ReadAllBytes(output));
        Texture original = before.Fragment.Drawable.ShaderGroup.TextureDictionary.Textures.data_items.Single(texture => texture.Name == "larger_unrelated");
        Texture retained = after.Fragment.Drawable.ShaderGroup.TextureDictionary.Textures.data_items.Single(texture => texture.Name == "larger_unrelated");
        Check(original.Data.FullData.SequenceEqual(retained.Data.FullData) && retained.Width == 8, "non-target embedded texture payload survives native compilation unchanged");
        Directory.Delete(work, true);
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
    private static bool Same(string root, Dictionary<string, byte[]> expected) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == expected.Count && expected.All(file => File.Exists(file.Key) && File.ReadAllBytes(file.Key).SequenceEqual(file.Value));
    private static void Restore(string root, Dictionary<string, byte[]> expected)
    {
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) if (!expected.ContainsKey(file)) File.Delete(file);
        foreach (var file in expected) File.WriteAllBytes(file.Key, file.Value);
    }
    private sealed class InlineProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
}
