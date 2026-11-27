using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Linq;
using CodeWalker.GameFiles;
using CodeWalker.Rendering;
using CodeWalker.World;
using CodeWalker.Utils;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Color = System.Drawing.Color;
using Device = SharpDX.Direct3D11.Device;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace BLRP.LiveryPreview;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 4 || !long.TryParse(args[0], out long parent) || !int.TryParse(args[1], out int owner)) return;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.Run(new PreviewHost(new IntPtr(parent), owner, args[2], args[3])); }
        catch (Exception ex) { File.WriteAllText(args[3], "ERROR: " + ex.GetBaseException().Message); }
    }
}

internal sealed class PreviewHost : Form
{
    private readonly RenderSurface surface = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 150 };
    private SceneSettingsForm? settings;
    private readonly IntPtr parent;
    private readonly int owner;
    private readonly string command;
    private readonly string status;
    private string loaded = "";
    private string loadedVehicle = "";
    private DateTime commandTime;

    public PreviewHost(IntPtr parent, int owner, string command, string status)
    {
        this.parent = parent; this.owner = owner; this.command = command; this.status = status;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Size = new Size(900, 620);
        BackColor = Color.FromArgb(22, 22, 35);
        ForeColor = Color.White;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(10), WrapContents = false };
        LiveryButton Button(string title, Action action)
        {
            var button = new LiveryButton { Text = title };
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { Report(ex); } };
            return button;
        }
        var angles = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
        angles.Items.AddRange(new object[] { "Orbit", "Front", "Rear", "Left", "Right", "Top" });
        angles.SelectedIndexChanged += (_, _) => surface.Preset(angles.Text == "Orbit" ? "Reset" : angles.Text);
        angles.SelectedIndex = 0;
        toolbar.Controls.Add(angles);
        toolbar.Controls.Add(Button("Reset view", () => surface.Preset("Reset")));
        toolbar.Controls.Add(Button("Scene settings", OpenSceneSettings));
        var save = Button("Save screenshot", () =>
        {
            using var dialog = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "livery-preview.png" };
            if (dialog.ShowDialog(this) == DialogResult.OK) surface.Screenshot(dialog.FileName, surface.ExportTransparent);
        });
        save.Primary = true;
        toolbar.Controls.Add(save);
        var notes = new Label { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(12, 6, 0, 0), Text = "Drag to orbit  ·  Right-drag to pan  ·  Scroll to zoom  ·  Offline materials and paint are approximate" };
        surface.TopLevel = false;
        surface.FormBorderStyle = FormBorderStyle.None;
        surface.Dock = DockStyle.Fill;
        Controls.Add(surface); Controls.Add(toolbar); Controls.Add(notes);
        LiveryTheme.Apply(this);
        surface.Failed += Report;
        Shown += (_, _) =>
        {
            SetWindowLong(Handle, -16, new IntPtr((GetWindowLong(Handle, -16).ToInt64() & ~0x80000000L) | 0x40000000L));
            SetParent(Handle, parent);
            surface.Show();
            timer.Start();
        };
        timer.Tick += (_, _) => Tick();
        FormClosing += (_, _) => { settings?.Dispose(); if (!surface.IsDisposed) surface.Close(); };
        FormClosed += (_, _) => timer.Dispose();
    }

    private void OpenSceneSettings()
    {
        if (settings == null || settings.IsDisposed)
        {
            settings = new SceneSettingsForm(surface);
            var area = Screen.FromControl(this).WorkingArea;
            settings.Height = Math.Min(settings.Height, area.Height - 40);
            Point position = PointToScreen(new Point(Math.Max(0, Width - settings.Width - 16), 0));
            settings.Location = new Point(Math.Max(area.Left, Math.Min(position.X, area.Right - settings.Width)), Math.Max(area.Top, Math.Min(position.Y, area.Bottom - settings.Height)));
            settings.Show(new PreviewOwner(GetAncestor(parent, 2)));
        }
        else { settings.Show(); settings.Activate(); }
    }

    private void Report(Exception ex) => File.WriteAllText(status, "ERROR: " + ex.GetBaseException().Message);

    private void Tick()
    {
        try
        {
            if (!IsWindow(parent) || Process.GetProcessById(owner).HasExited || File.Exists(Path.Combine(Path.GetDirectoryName(command)!, "close"))) { Close(); return; }
            GetClientRect(parent, out Rect rectangle);
            if (Width != rectangle.Right || Height != rectangle.Bottom) MoveWindow(Handle, 0, 0, rectangle.Right, rectangle.Bottom, true);
            if (!File.Exists(command) || File.GetLastWriteTimeUtc(command) == commandTime) return;
            commandTime = File.GetLastWriteTimeUtc(command);
            XElement scene = XDocument.Load(command).Root!;
            string model = scene.Attribute("model")!.Value;
            string vehicle = (string?)scene.Attribute("vehicle") ?? model;
            if (model != loaded)
            {
                surface.LoadVehicle(model, loaded.Length == 0 || vehicle != loadedVehicle);
                loaded = model;
                loadedVehicle = vehicle;
                File.WriteAllText(status, "LOADED:" + loaded);
            }
        }
        catch (ArgumentException) { Close(); }
        catch (Exception ex) { Report(ex); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private sealed class PreviewOwner : IWin32Window
    {
        public IntPtr Handle { get; }
        public PreviewOwner(IntPtr handle) => Handle = handle;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLong(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
}

internal static class MathFCompat { public const float Pi = (float)Math.PI; }

internal sealed partial class RenderSurface : Form, DXForm
{
    public Form Form => this;
    private Renderer renderer = null!;
    private readonly Entity target = new();
    private YftFile? model;
    private DrawableBase[] materialDrawables = Array.Empty<DrawableBase>();
    private readonly HashSet<ShaderFX> emissivePaint = new();
    public int EmissivePaintGeometryCount { get; private set; }
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private BackgroundPass? backgroundPass;
    private bool hasBackgroundImage;
    private Point lastMouse;
    private Vector3 desiredPivot;
    private string? screenshot;
    private bool screenshotAlpha;
    private float radius = 3;
    private Color background = LiveryTheme.Background;
    public string LightingPreset { get; set; } = "Studio";
    public string BackgroundPreset { get; private set; } = "Dark studio";
    public Color BackgroundColour => background;
    private bool transparent;
    public bool ExportTransparent { get; set; }
    private volatile bool stopping;
    private Thread? content;
    public float Azimuth = 2.25f, Elevation = 0.65f, Intensity = 1, Ambient = 0.5f;
    public bool Shadows = true;
    public long FrameCount { get; private set; }
    public event Action<Exception>? Failed;
    public string[] BodyNames => model?.Fragment?.Drawable?.AllModels?.Select((part, index) => $"Part {index} • bone {part.BoneIndex}").ToArray() ?? Array.Empty<string>();

    public RenderSurface()
    {
        foreach (Control control in new Control[] { this })
        {
            control.MouseDown += (_, e) => { lastMouse = e.Location; control.Focus(); control.Capture = true; };
            control.MouseUp += (_, _) => control.Capture = false;
            control.MouseMove += (_, e) =>
            {
                if (renderer == null || e.Button == MouseButtons.None) return;
                lock (renderer.RenderSyncRoot)
                {
                    int x = e.X - lastMouse.X, y = e.Y - lastMouse.Y;
                    if (e.Button == MouseButtons.Left) renderer.camera.MouseRotate(x, y);
                    else if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
                        desiredPivot += Vector3.Transform(new Vector3(-x, y, 0) * renderer.camera.TargetDistance * 0.0015f, renderer.camera.ViewInvQuaternion);
                    lastMouse = e.Location;
                }
            };
            control.MouseWheel += (_, e) => { if (renderer != null) lock (renderer.RenderSyncRoot) renderer.camera.MouseZoom(e.Delta); };
        }
        Load += (_, _) =>
        {
            renderer = new Renderer(this, null);
            renderer.renderclouds = false; renderer.renderskydome = false; renderer.rendermoon = false;
            renderer.renderfragwindows = false; renderer.SelectionFlagsTestAll = true;
            renderer.controllightdir = true;
            if (!renderer.Init()) throw new InvalidOperationException("BadWalker could not initialise Direct3D.");
            // DXManager's stock loop sleeps when an embedded form has no ActiveForm.
            // Keep its device/resize lifecycle, and render during UI idle with vsync instead.
            InitScene(renderer.DXMan.device);
            Application.Idle += RenderWhenIdle;
        };
    }

    public void InitScene(Device device)
    {
        renderer.DeviceCreated(device, ClientSize.Width, ClientSize.Height);
        renderer.camera.FollowEntity = target;
        renderer.camera.Smoothness = 16f;
        renderer.camera.Sensitivity = 0.004f;
        renderer.camera.ZoomSpeed = 0.16f;
        target.Orientation = Quaternion.LookAtLH(Vector3.Zero, Vector3.Up, Vector3.ForwardLH);
        renderer.shaders.deferred = false; renderer.shaders.hdr = false;
        backgroundPass = new BackgroundPass(device);
        content = new Thread(() =>
        {
            try
            {
                while (!stopping)
                {
                    bool pending;
                    lock (renderer.RenderSyncRoot) { if (stopping) break; pending = renderer.ContentThreadProc(); }
                    Thread.Sleep(pending ? 1 : 5);
                }
            }
            catch (Exception ex) { if (!stopping) BeginInvoke(new Action(() => Failed?.Invoke(ex))); }
        }) { IsBackground = true };
        content.Start();
    }

    public void CleanupScene()
    {
        stopping = true;
        Application.Idle -= RenderWhenIdle;
        content?.Join(2500);
        lock (renderer.RenderSyncRoot) { backgroundPass?.Dispose(); backgroundPass = null; renderer.DeviceDestroyed(); }
    }

    private void RenderWhenIdle(object? sender, EventArgs e)
    {
        while (!stopping && Visible && IsWindowVisible(Handle) && ClientSize.Width > 8 && ClientSize.Height > 8 && !PeekMessage(out _, IntPtr.Zero, 0, 0, 0))
        {
            try
            {
                var dx = renderer.DXMan;
                dx.context.OutputMerger.SetRenderTargets(dx.depthview, dx.targetview);
                dx.context.Rasterizer.SetViewport(0, 0, ClientSize.Width, ClientSize.Height);
                RenderScene(dx.context);
                dx.swapchain.Present(1, PresentFlags.None);
            }
            catch (Exception ex) { Application.Idle -= RenderWhenIdle; Failed?.Invoke(ex); return; }
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage
    {
        public IntPtr Window; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Point Point; public uint Private;
    }
    [DllImport("user32.dll")] private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

    public void BuffersResized(int width, int height) => renderer.BuffersResized(width, height);
    public bool ConfirmQuit() => true;

    public void LoadVehicle(string path, bool reset)
    {
        var next = new YftFile(); next.Load(File.ReadAllBytes(path));
        lock (renderer.RenderSyncRoot)
        {
            // Recreate the GPU cache when replacing a vehicle, releasing the previous model/textures.
            renderer.RenderableCache.OnDeviceDestroyed();
            renderer.RenderableCache.OnDeviceCreated(renderer.Device);
            model = next;
            materialDrawables = Drawables(next.Fragment).Distinct().ToArray();
            emissivePaint.Clear();
            EmissivePaintGeometryCount = 0;
            foreach (var drawable in materialDrawables)
            foreach (var shader in drawable.ShaderGroup?.Shaders?.data_items ?? Array.Empty<ShaderFX>())
            {
                if (shader.FileName.Hash != LiveryPreviewMaterials.EmissiveVehiclePaint) continue;
                // The shipped BasicShader supports both paint/livery samplers and emissive lighting.
                // Bind its explicit vehicle-paint batch and enable emission on the matching geometry.
                // Only this in-memory preview is adapted; the staged YFT and repository stay intact.
                emissivePaint.Add(shader);
                shader.FileName = LiveryPreviewMaterials.VehiclePaint4;
            }
            renderer.SelectionModelDrawFlags.Clear();
            InitializePaint();
            radius = next.Fragment.Drawable.BoundingSphereRadius;
            if (reset) Preset("Reset");
        }
    }

    public void SetBody(int index, bool visible)
    {
        if (renderer == null) return;
        lock (renderer.RenderSyncRoot)
        {
            DrawableModel[]? parts = model?.Fragment?.Drawable?.AllModels;
            if (parts == null || index >= parts.Length) return;
            if (visible) renderer.SelectionModelDrawFlags.Remove(parts[index]);
            else renderer.SelectionModelDrawFlags[parts[index]] = false;
        }
    }

    public void Preset(string view)
    {
        if (renderer == null) return;
        lock (renderer.RenderSyncRoot)
        {
            target.Position = desiredPivot = model?.Fragment?.Drawable?.BoundingCenter ?? Vector3.Zero;
            renderer.camera.TargetDistance = renderer.camera.CurrentDistance = Math.Max(1, radius * 2.0f);
            float yaw = view == "Front" ? MathFCompat.Pi : view == "Left" ? -MathFCompat.Pi / 2 : view == "Right" ? MathFCompat.Pi / 2 : view == "Reset" ? MathFCompat.Pi + 0.65f : 0;
            float pitch = view == "Top" ? 1.5f : view == "Reset" ? 0.25f : 0.05f;
            renderer.camera.TargetRotation = renderer.camera.CurrentRotation = new Vector3(yaw, pitch, 0);
        }
    }

    public void Lighting(string preset)
    {
        LightingPreset = preset;
        Intensity = preset == "Night" ? 0.35f : preset == "Soft" ? 0.7f : 1;
        Ambient = preset == "Night" ? 0.15f : preset == "Soft" ? 0.7f : 0.5f;
        Elevation = preset == "Daylight" ? 1.1f : 0.65f;
    }

    public void SetBackground(Color colour, bool alpha)
    {
        BackgroundPreset = alpha ? "Transparent" : colour == LiveryTheme.Background ? "Dark studio" : colour == Color.FromArgb(195, 201, 211) ? "Light studio" : "Solid colour…";
        if (renderer == null) { background = colour; transparent = alpha; return; }
        lock (renderer.RenderSyncRoot) { background = colour; transparent = alpha; backgroundPass?.SetImage(null); hasBackgroundImage = false; }
    }
    public void SetBackgroundImage(string path)
    {
        using var image = Image.FromFile(path);
        using var bitmap = new Bitmap(image);
        lock (renderer.RenderSyncRoot) { backgroundPass!.SetImage(bitmap); hasBackgroundImage = true; transparent = false; BackgroundPreset = "Image…"; }
    }
    public void Screenshot(string path, bool alpha)
    {
        lock (renderer.RenderSyncRoot) { screenshot = path; screenshotAlpha = alpha; }
    }

    public void RenderScene(DeviceContext context)
    {
        lock (renderer.RenderSyncRoot)
        {
            if (stopping) return;
            float seconds = (float)elapsed.Elapsed.TotalSeconds; elapsed.Restart();
            renderer.lightdirx = Azimuth; renderer.lightdiry = Elevation;
            renderer.shaders.shadows = Shadows;
            target.Position = Vector3.Lerp(target.Position, desiredPivot, 1f - (float)Math.Exp(-16f * Math.Min(seconds, 0.1f)));
            renderer.Update(seconds, lastMouse.X, lastMouse.Y);
            renderer.BeginRender(context);
            bool clearAlpha = transparent || (screenshot != null && screenshotAlpha);
            context.ClearRenderTargetView(renderer.DXMan.targetview, clearAlpha ? new Color4(0, 0, 0, 0) : new Color4(background.R / 255f, background.G / 255f, background.B / 255f, 1));
            if (hasBackgroundImage && !clearAlpha) backgroundPass!.Draw(context, ClientSize.Width, ClientSize.Height);
            ShaderGlobalLights lights = renderer.shaders.GlobalLights;
            lights.Params.LightDirColour = new Color4(Intensity, Intensity, Intensity, 1);
            lights.Params.LightDirAmbColour = new Color4(Ambient, Ambient, Ambient, 1);
            lights.Params.LightNaturalAmbUp = lights.Params.LightNaturalAmbDown = Color4.Black;
            if (model?.Fragment != null)
            {
                renderer.RenderFragment(null, null, model.Fragment);
                ApplyEmissivePaint();
                ApplyPaintOverrides();
            }
            renderer.RenderQueued(); renderer.RenderFinalPass(); renderer.EndRender();
            if (screenshot != null)
            {
                using Bitmap frame = ReadFrame(context);
                try { frame.Save(screenshot, ImageFormat.Png); }
                catch (Exception ex) { BeginInvoke(new Action(() => Failed?.Invoke(ex))); }
                screenshot = null;
            }
            FrameCount++;
        }
    }

    private static IEnumerable<DrawableBase> Drawables(FragType fragment)
    {
        if (fragment.Drawable != null) yield return fragment.Drawable;
        if (fragment.DrawableCloth != null) yield return fragment.DrawableCloth;
        foreach (var drawable in fragment.DrawableArray?.data_items ?? Array.Empty<FragDrawable>())
            if (drawable != null) yield return drawable;
        foreach (var child in fragment.PhysicsLODGroup?.PhysicsLOD1?.Children?.data_items ?? Array.Empty<FragPhysTypeChild>())
        {
            if (child.Drawable1 != null) yield return child.Drawable1;
            if (child.Drawable2 != null) yield return child.Drawable2;
        }
    }

    private void ApplyEmissivePaint()
    {
        if (emissivePaint.Count == 0) return;
        int count = 0;
        foreach (var drawable in materialDrawables)
        {
            Renderable renderable = renderer.RenderableCache.GetRenderable(drawable);
            foreach (var part in renderable.AllModels ?? Array.Empty<RenderableModel>())
            foreach (var geometry in part.Geometries ?? Array.Empty<RenderableGeometry>())
                if (emissivePaint.Contains(geometry.DrawableGeom.Shader)) { geometry.IsEmissive = true; count++; }
        }
        EmissivePaintGeometryCount = count;
    }

    private Bitmap ReadFrame(DeviceContext context)
    {
        Texture2D source = renderer.DXMan.backbuffer;
        Texture2DDescription description = source.Description;
        description.SampleDescription = new SampleDescription(1, 0);
        description.BindFlags = BindFlags.None;
        description.OptionFlags = ResourceOptionFlags.None;
        description.Usage = ResourceUsage.Default;
        description.CpuAccessFlags = CpuAccessFlags.None;
        using var resolved = new Texture2D(renderer.Device, description);
        if (source.Description.SampleDescription.Count > 1) context.ResolveSubresource(source, 0, resolved, 0, description.Format);
        else context.CopyResource(source, resolved);
        description.Usage = ResourceUsage.Staging;
        description.CpuAccessFlags = CpuAccessFlags.Read;
        using var staging = new Texture2D(renderer.Device, description);
        context.CopyResource(resolved, staging);
        DataBox mapped = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        var bitmap = new Bitmap(description.Width, description.Height, PixelFormat.Format32bppArgb);
        BitmapData bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[bitmap.Width * 4];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * mapped.RowPitch), row, 0, row.Length);
                for (int x = 0; x < row.Length; x += 4) { byte red = row[x]; row[x] = row[x + 2]; row[x + 2] = red; }
                Marshal.Copy(row, 0, IntPtr.Add(bits.Scan0, y * bits.Stride), row.Length);
            }
        }
        finally { bitmap.UnlockBits(bits); context.UnmapSubresource(staging, 0); }
        return bitmap;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Application.Idle -= RenderWhenIdle;
        base.Dispose(disposing);
    }
}
