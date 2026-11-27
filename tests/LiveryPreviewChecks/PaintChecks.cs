using System.Drawing;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using BLRP.LiveryPreview;
using CodeWalker.GameFiles;
using CodeWalker.Rendering;

internal static class PaintChecks
{
    private sealed class Baseline
    {
        public RenderableGeometry Geometry = null!;
        public PaintChannel? Channel;
        public int Diffuse, Livery;
        public RenderableTexture? Colour, Artwork;
        public float Intensity;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    public static int Run(string asset, string output)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Directory.CreateDirectory(output);
        byte[] original = SHA256.Create().ComputeHash(File.ReadAllBytes(asset));
        using var surface = new RenderSurface { Size = new Size(1024, 720), Opacity = 0, ShowInTaskbar = false };
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int result = 1, phase = 0; bool running = false;
        Baseline[] baseline = Array.Empty<Baseline>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(string name) => (T)typeof(RenderSurface).GetField(name, flags)!.GetValue(surface)!;
        Baseline[] Geometries()
        {
            var model = Field<YftFile>("model");
            var renderer = Field<Renderer>("renderer");
            return renderer.RenderableCache.GetRenderable(model.Fragment.Drawable).AllModels.SelectMany(part => part.Geometries).Select(geometry =>
            {
                int diffuse = Array.IndexOf(geometry.TextureParamHashes, ShaderParamNames.DiffuseSampler);
                int livery = Array.IndexOf(geometry.TextureParamHashes, ShaderParamNames.DiffuseSampler2);
                return new Baseline { Geometry = geometry, Channel = PaintMaterials.Channel(geometry.DrawableGeom.Shader), Diffuse = diffuse, Livery = livery,
                    Colour = diffuse < 0 ? null : geometry.RenderableTextures[diffuse], Artwork = livery < 0 ? null : geometry.RenderableTextures[livery], Intensity = geometry.specularIntensityMult };
            }).ToArray();
        }
        void ArtworkUnchanged()
        {
            Check(baseline.Where(item => item.Livery >= 0).All(item => ReferenceEquals(item.Artwork, item.Geometry.RenderableTextures[item.Livery])), "paint changes preserve every livery GPU texture binding");
        }
        surface.Failed += ex => { Console.Error.WriteLine(ex); surface.Close(); };
        surface.Shown += (_, _) => { surface.LoadVehicle(asset, true); timer.Start(); };
        timer.Tick += async (_, _) =>
        {
            if (running) return; running = true;
            try
            {
                if (clock.Elapsed.TotalSeconds > 35) throw new TimeoutException("Paint rendering check timed out.");
                if (phase == 0 && clock.Elapsed.TotalSeconds > 3)
                {
                    baseline = Geometries();
                    Check(surface.PaintMaterialCount(PaintChannel.Primary) > 0 && surface.PaintMaterialCount(PaintChannel.Secondary) > 0, "native model exposes distinct primary and secondary paint layers");
                    surface.Screenshot(Path.Combine(output, "original.png"), false);
                    await Task.Delay(150);
                    surface.SetPaint(PaintChannel.Primary, Color.FromArgb(180, 35, 30), PaintFinish.Matte); phase++; clock.Restart();
                }
                else if (phase == 1 && clock.Elapsed.TotalSeconds > 2)
                {
                    var primary = baseline.Where(item => item.Channel == PaintChannel.Primary && item.Diffuse >= 0).ToArray();
                    Check(primary.Length > 0 && primary.All(item => item.Geometry.RenderableTextures[item.Diffuse].Key.Name == surface.GetPaint(PaintChannel.Primary).Diffuse!.Name), "primary colour reaches only its declared GPU paint surfaces");
                    Check(baseline.Where(item => item.Channel != PaintChannel.Primary && item.Diffuse >= 0).All(item => ReferenceEquals(item.Colour, item.Geometry.RenderableTextures[item.Diffuse])), "primary paint leaves secondary, wheels, glass and trim diffuse textures unchanged");
                    ArtworkUnchanged();
                    surface.Screenshot(Path.Combine(output, "primary-matte.png"), false); await Task.Delay(150);
                    surface.SetPaint(PaintChannel.Secondary, Color.FromArgb(30, 150, 75), PaintFinish.Metallic); phase++; clock.Restart();
                }
                else if (phase == 2 && clock.Elapsed.TotalSeconds > 2)
                {
                    Check(baseline.Where(item => item.Channel == PaintChannel.Secondary && item.Diffuse >= 0).All(item => item.Geometry.RenderableTextures[item.Diffuse].Key.Name == surface.GetPaint(PaintChannel.Secondary).Diffuse!.Name), "secondary paint has its own colour and finish");
                    Check(baseline.Where(item => item.Channel == PaintChannel.Primary).All(item => item.Geometry.specularIntensityMult < 0.02f), "secondary metallic finish preserves the primary matte material");
                    ArtworkUnchanged(); surface.Screenshot(Path.Combine(output, "dual-paint.png"), false); await Task.Delay(150);
                    surface.SetPaint(PaintChannel.Primary, Color.FromArgb(180, 35, 30), PaintFinish.Chrome); phase++; clock.Restart();
                }
                else if (phase == 3 && clock.Elapsed.TotalSeconds > 2)
                {
                    Check(baseline.Where(item => item.Channel == PaintChannel.Primary).All(item => item.Geometry.specularIntensityMult > 2), "finish changes update the paint material without recolouring artwork");
                    ArtworkUnchanged(); surface.Screenshot(Path.Combine(output, "primary-chrome.png"), false); await Task.Delay(150);
                    surface.Lighting("Soft"); surface.SetBackground(Color.FromArgb(40, 45, 50), false);
                    using (var settings = new SceneSettingsForm(surface) { Opacity = 0 })
                    {
                        settings.Show(surface); settings.RefreshVehicle(); Application.DoEvents();
                        Check(settings.Controls.Count > 0, "scene settings opens with live paint, lighting and scene pages"); settings.Close();
                        Check(surface.LightingPreset == "Soft" && surface.BackgroundPreset == "Solid colour…", "opening scene settings preserves the current lighting and background selection");
                    }
                    surface.LoadVehicle(asset, false); phase++; clock.Restart();
                }
                else if (phase == 4 && clock.Elapsed.TotalSeconds > 2)
                {
                    Check(surface.GetPaint(PaintChannel.Primary).Colour!.Value.R == 180 && surface.GetPaint(PaintChannel.Secondary).Finish == PaintFinish.Metallic, "paint choices survive a livery scene reload");
                    baseline = Geometries(); surface.ResetPaint(); phase++; clock.Restart();
                }
                else if (phase == 5 && clock.Elapsed.TotalSeconds > 2)
                {
                    var model = Field<YftFile>("model");
                    Check(surface.GetPaint(PaintChannel.Primary).Colour == null && surface.GetPaint(PaintChannel.Secondary).Finish == PaintFinish.Original, "reset restores model colours and original material settings");
                    Check(Geometries().Where(item => item.Diffuse >= 0).All(item => item.Colour == null || !item.Colour.Key.Name.StartsWith("blrp_studio_paint_")), "reset restores original GPU diffuse textures after a scene reload");
                    surface.Screenshot(Path.Combine(output, "reset.png"), false); await Task.Delay(150);
                    Check(SHA256.Create().ComputeHash(File.ReadAllBytes(asset)).SequenceEqual(original), "all paint and material previews leave the native asset file byte-identical");
                    result = 0; surface.Close();
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); surface.Close(); }
            finally { running = false; }
        };
        Application.Run(surface); return result;
    }
}
