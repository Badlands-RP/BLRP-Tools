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
    private readonly CheckedListBox bodies = new() { Dock = DockStyle.Right, Width = 170, CheckOnClick = true };
    private readonly IntPtr parent;
    private readonly int owner;
    private readonly string command;
    private readonly string status;
    private string loaded = "";
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
        var settings = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 260, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16), Visible = false };
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
        toolbar.Controls.Add(Button("Scene settings", () => settings.Visible = !settings.Visible));
        var alpha = new CheckBox { Text = "Transparent screenshots", AutoSize = true };
        var save = Button("Save screenshot", () =>
        {
            using var dialog = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "livery-preview.png" };
            if (dialog.ShowDialog(this) == DialogResult.OK) surface.Screenshot(dialog.FileName, alpha.Checked);
        });
        save.Primary = true;
        toolbar.Controls.Add(save);
        settings.Controls.Add(new Label { Text = "LIGHTING", AutoSize = true });
        var lighting = new ComboBox { Width = 215, DropDownStyle = ComboBoxStyle.DropDownList };
        lighting.Items.AddRange(new object[] { "Studio", "Daylight", "Soft", "Night" });
        lighting.SelectedIndexChanged += (_, _) => surface.Lighting(lighting.Text);
        lighting.SelectedIndex = 0;
        settings.Controls.Add(lighting);
        void Slider(string title, int maximum, int value, Action<int> change)
        {
            settings.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
            var slider = new TrackBar { Minimum = 0, Maximum = maximum, Value = value, Width = 215, Height = 32, TickStyle = TickStyle.None };
            slider.ValueChanged += (_, _) => change(slider.Value);
            settings.Controls.Add(slider);
        }
        Slider("Direction", 360, 130, value => surface.Azimuth = value * MathFCompat.Pi / 180);
        Slider("Elevation", 90, 40, value => surface.Elevation = value * MathFCompat.Pi / 180);
        Slider("Intensity", 250, 100, value => surface.Intensity = value / 100f);
        Slider("Ambient light", 150, 50, value => surface.Ambient = value / 100f);
        var shadows = new CheckBox { Text = "Shadows", Checked = true, AutoSize = true };
        shadows.CheckedChanged += (_, _) => surface.Shadows = shadows.Checked;
        settings.Controls.Add(shadows);
        settings.Controls.Add(new Label { Text = "BACKGROUND", AutoSize = true, Margin = new Padding(0, 16, 0, 6) });
        var background = new ComboBox { Width = 215, DropDownStyle = ComboBoxStyle.DropDownList };
        background.Items.AddRange(new object[] { "Dark studio", "Light studio", "Solid colour", "Image…", "Transparent" });
        background.SelectedIndexChanged += (_, _) =>
        {
            try
            {
                if (background.Text == "Image…")
                {
                    using var dialog = new OpenFileDialog { Filter = "Background image|*.png;*.jpg;*.jpeg;*.bmp" };
                    if (dialog.ShowDialog(this) == DialogResult.OK) surface.SetBackgroundImage(dialog.FileName);
                }
                else
                {
                    Color colour = background.Text == "Light studio" ? Color.FromArgb(195, 201, 211) : LiveryTheme.Background;
                    if (background.Text == "Solid colour")
                    {
                        using var dialog = new ColorDialog();
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        colour = dialog.Color;
                    }
                    surface.SetBackground(colour, background.Text == "Transparent");
                }
            }
            catch (Exception ex) { Report(ex); }
        };
        settings.Controls.Add(background); background.SelectedIndex = 0;
        settings.Controls.Add(Button("Paint colour (approx.)", () =>
        {
            using var dialog = new ColorDialog { Color = Color.White };
            if (dialog.ShowDialog(this) == DialogResult.OK) surface.SetPaint(dialog.Color);
        }));
        settings.Controls.Add(alpha);
        settings.Controls.Add(new Label { Text = "BODY PARTS", AutoSize = true, Margin = new Padding(0, 16, 0, 6) });
        bodies.Dock = DockStyle.None; bodies.Width = 215; bodies.Height = 130;
        settings.Controls.Add(bodies);
        var notes = new Label { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(12, 6, 0, 0), Text = "Drag to orbit  ·  Right-drag to pan  ·  Scroll to zoom  ·  Offline materials and paint are approximate" };
        surface.TopLevel = false;
        surface.FormBorderStyle = FormBorderStyle.None;
        surface.Dock = DockStyle.Fill;
        Controls.Add(surface); Controls.Add(settings); Controls.Add(toolbar); Controls.Add(notes);
        LiveryTheme.Apply(this);
        bodies.ItemCheck += (_, e) => surface.SetBody(e.Index, e.NewValue == CheckState.Checked);
        surface.Failed += Report;
        Shown += (_, _) =>
        {
            SetWindowLong(Handle, -16, new IntPtr((GetWindowLong(Handle, -16).ToInt64() & ~0x80000000L) | 0x40000000L));
            SetParent(Handle, parent);
            surface.Show();
            timer.Start();
        };
        timer.Tick += (_, _) => Tick();
        FormClosing += (_, _) => { if (!surface.IsDisposed) surface.Close(); };
        FormClosed += (_, _) => timer.Dispose();
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
            string model = XDocument.Load(command).Root!.Attribute("model")!.Value;
            if (model != loaded)
            {
                surface.LoadVehicle(model, loaded.Length == 0);
                loaded = model;
                bodies.Items.Clear();
                foreach (string part in surface.BodyNames) bodies.Items.Add(part, true);
                File.WriteAllText(status, "LOADED:" + loaded);
            }
        }
        catch (ArgumentException) { Close(); }
        catch (Exception ex) { Report(ex); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLong(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
}

internal static class MathFCompat { public const float Pi = (float)Math.PI; }

internal sealed class RenderSurface : Form, DXForm
{
    public Form Form => this;
    private Renderer renderer = null!;
    private readonly Entity target = new();
    private YftFile? model;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private BackgroundPass? backgroundPass;
    private bool hasBackgroundImage;
    private Point lastMouse;
    private Vector3 desiredPivot;
    private string? screenshot;
    private bool screenshotAlpha;
    private float radius = 3;
    private Color background = Color.FromArgb(22, 22, 35);
    private bool transparent;
    private Texture? paintTexture;
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
            renderer.SelectionModelDrawFlags.Clear();
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
        Intensity = preset == "Night" ? 0.35f : preset == "Soft" ? 0.7f : 1;
        Ambient = preset == "Night" ? 0.15f : preset == "Soft" ? 0.7f : 0.5f;
        Elevation = preset == "Daylight" ? 1.1f : 0.65f;
    }

    public void SetPaint(Color colour)
    {
        byte[] pixels = new byte[64];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = colour.B; pixels[i + 1] = colour.G; pixels[i + 2] = colour.R; pixels[i + 3] = 255; }
        string name = "blrp_preview_paint_" + Guid.NewGuid().ToString("N");
        lock (renderer.RenderSyncRoot)
            paintTexture = new Texture { Name = name, NameHash = JenkHash.GenHash(name), Width = 4, Height = 4, Depth = 1,
                Levels = 1, Stride = 16, Format = TextureFormat.D3DFMT_A8R8G8B8, Data = new TextureData { FullData = pixels } };
    }
    public void SetBackground(Color colour, bool alpha)
    {
        if (renderer == null) { background = colour; transparent = alpha; return; }
        lock (renderer.RenderSyncRoot) { background = colour; transparent = alpha; backgroundPass?.SetImage(null); hasBackgroundImage = false; }
    }
    public void SetBackgroundImage(string path)
    {
        using var image = Image.FromFile(path);
        using var bitmap = new Bitmap(image);
        lock (renderer.RenderSyncRoot) { backgroundPass!.SetImage(bitmap); hasBackgroundImage = true; transparent = false; }
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
                if (paintTexture != null) ApplyPaintTint();
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

    private void ApplyPaintTint()
    {
        // BasicShader lacks native vehicle paint palettes. A clearly labelled studio approximation
        // replaces only the diffuse base on materials carrying the pending livery's second sampler.
        if (paintTexture == null || model?.Fragment?.Drawable == null) return;
        RenderableTexture tint = renderer.RenderableCache.GetRenderableTexture(paintTexture);
        if (!tint.IsLoaded) return;
        Renderable drawable = renderer.RenderableCache.GetRenderable(model.Fragment.Drawable);
        foreach (RenderableModel part in drawable.AllModels ?? Array.Empty<RenderableModel>())
        foreach (RenderableGeometry geometry in part.Geometries ?? Array.Empty<RenderableGeometry>())
        {
            if (geometry.Textures == null || geometry.RenderableTextures == null) continue;
            uint shader = geometry.DrawableGeom.Shader.FileName.Hash;
            bool paintMaterial = shader is 3274951810 or 1009159769 or 2045642561 or 1534086746 or 4262329590 or 60950417 or 249472155 or 354168229 or 430888562 or 4118002252;
            if (!paintMaterial) continue;
            for (int i = 0; i < geometry.TextureParamHashes.Length; i++)
                if (geometry.TextureParamHashes[i] == ShaderParamNames.DiffuseSampler) geometry.RenderableTextures[i] = tint;
        }
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
