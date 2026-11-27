using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CodeWalker.Rendering;
using SharpDX;
using SharpDX.Direct3D11;
using Device = SharpDX.Direct3D11.Device;
using Rectangle = System.Drawing.Rectangle;
using RectangleF = System.Drawing.RectangleF;

namespace BLRP.LiveryPreview;

// Uses BadWalker's existing fullscreen quad and copy shaders. The background is drawn
// before vehicle geometry, without depth writes or any CPU frame-compositing overlay.
internal sealed class BackgroundPass : IDisposable
{
    private readonly Device device;
    private readonly UnitQuad quad;
    private readonly VertexShader vertex;
    private readonly PixelShader pixel;
    private readonly InputLayout layout;
    private readonly GpuVarsBuffer<PostProcessorReduceCSVars> dimensions;
    private readonly DepthStencilState noDepth;
    private readonly RasterizerState rasterizer;
    private readonly BlendState opaque;
    private GpuSBuffer<Vector4>? pixels;
    private int width, height;
    private Bitmap? source;

    public BackgroundPass(Device device)
    {
        this.device = device;
        byte[] vs = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Shaders", "PPFinalPassVS.cso"));
        vertex = new VertexShader(device, vs);
        pixel = new PixelShader(device, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Shaders", "PPCopyPixelsPS.cso")));
        quad = new UnitQuad(device, true);
        layout = new InputLayout(device, vs, quad.GetLayout());
        dimensions = new GpuVarsBuffer<PostProcessorReduceCSVars>(device);
        noDepth = DXUtility.CreateDepthStencilState(device, false, DepthWriteMask.Zero);
        rasterizer = DXUtility.CreateRasterizerState(device, FillMode.Solid, CullMode.None, false, false, true);
        opaque = new BlendState(device, new BlendStateDescription { RenderTarget = { [0] = new RenderTargetBlendDescription { IsBlendEnabled = false, RenderTargetWriteMask = ColorWriteMaskFlags.All } } });
    }

    public void SetImage(Bitmap? image)
    {
        source?.Dispose(); source = image == null ? null : new Bitmap(image);
        pixels?.Dispose(); pixels = null;
    }

    public void Draw(DeviceContext context, int targetWidth, int targetHeight)
    {
        if (source == null) return;
        if (pixels == null || width != targetWidth || height != targetHeight) Upload(targetWidth, targetHeight);
        context.OutputMerger.DepthStencilState = noDepth;
        context.OutputMerger.BlendState = opaque;
        context.Rasterizer.State = rasterizer;
        context.VertexShader.Set(vertex);
        context.PixelShader.Set(pixel);
        context.PixelShader.SetShaderResource(0, pixels!.SRV);
        dimensions.Vars.dimx = (uint)width;
        dimensions.Vars.dimy = (uint)height;
        dimensions.Update(context);
        dimensions.SetPSCBuffer(context, 0);
        context.InputAssembler.InputLayout = layout;
        quad.Draw(context);
        context.PixelShader.SetShaderResource(0, null);
    }

    private void Upload(int targetWidth, int targetHeight)
    {
        width = targetWidth; height = targetHeight;
        using var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(LiveryTheme.Background);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            float scale = Math.Max((float)width / source!.Width, (float)height / source.Height);
            float w = source.Width * scale, h = source.Height * scale;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
            graphics.DrawImage(source, Rectangle.Round(new RectangleF((width - w) / 2, (height - h) / 2, w, h)),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        BitmapData bits = canvas.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var data = new Vector4[checked(width * height)];
        try
        {
            byte[] row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, row.Length);
                for (int x = 0; x < width; x++) data[y * width + x] = new Vector4(row[x * 4 + 2] / 255f, row[x * 4 + 1] / 255f, row[x * 4] / 255f, 1);
            }
        }
        finally { canvas.UnlockBits(bits); }
        pixels?.Dispose(); pixels = new GpuSBuffer<Vector4>(device, data);
    }

    public void Dispose()
    {
        source?.Dispose(); pixels?.Dispose(); opaque.Dispose(); rasterizer.Dispose(); noDepth.Dispose();
        dimensions.Dispose(); layout.Dispose(); pixel.Dispose(); vertex.Dispose(); quad.Dispose();
    }
}
