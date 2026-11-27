using System.Drawing;
using CodeWalker.GameFiles;
using CodeWalker.Rendering;
using SharpDX;
using Color = System.Drawing.Color;

namespace BLRP.LiveryPreview;

internal enum PaintChannel { Primary = 1, Secondary = 2 }
internal enum PaintFinish { Original, Classic, Metallic, Matte, Satin, Chrome }

internal sealed class PaintSelection
{
    public Color? Colour { get; }
    public PaintFinish Finish { get; }
    internal Texture? Diffuse { get; }
    public PaintSelection(Color? colour, PaintFinish finish)
    {
        Colour = colour; Finish = finish;
        if (colour.HasValue) Diffuse = PaintMaterials.Texture(colour.Value, false);
    }
}

internal static class PaintMaterials
{
    private static readonly HashSet<uint> VehicleShaders = new()
    {
        753908000, 1107511867, 3274951810, 1009159769, 2045642561, 1534086746,
        4262329590, 60950417, 249472155, 354168229, 430888562, 4118002252,
        617726044, 1138799003, 2094873540, 158342452, 3355845283,
        LiveryPreviewMaterials.EmissiveVehiclePaint
    };
    public static PaintChannel? Channel(ShaderFX shader)
    {
        if (!VehicleShaders.Contains(shader.FileName.Hash)) return null;
        var parameters = shader.ParametersList;
        if (parameters?.Hashes == null || parameters.Parameters == null) return null;
        for (int i = 0; i < Math.Min(parameters.Hashes.Length, parameters.Parameters.Length); i++)
        {
            if ((uint)parameters.Hashes[i] != (uint)ShaderParamNames.matDiffuseColor || parameters.Parameters[i].Data is not Vector4 value) continue;
            // GTA paint-layer selectors are (2, 1, 1) / (2, 2, 2). Other values represent
            // fixed colours, wheels, pearl, trim or dashboard; those are deliberately untouched.
            if (Math.Abs(value.X - 2) > 0.001f || Math.Abs(value.Y - value.Z) > 0.001f) return null;
            if (Math.Abs(value.Y - 1) < 0.001f) return PaintChannel.Primary;
            if (Math.Abs(value.Y - 2) < 0.001f) return PaintChannel.Secondary;
        }
        return null;
    }

    public static Texture Texture(Color colour, bool metallic)
    {
        const int size = 32;
        byte[] pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int flake = metallic ? ((i / 4 * 73 + 41) % 61) - 30 : 0;
            pixels[i] = (byte)Math.Max(0, Math.Min(255, colour.B + flake));
            pixels[i + 1] = (byte)Math.Max(0, Math.Min(255, colour.G + flake));
            pixels[i + 2] = (byte)Math.Max(0, Math.Min(255, colour.R + flake));
            pixels[i + 3] = 255;
        }
        string name = "blrp_studio_paint_" + Guid.NewGuid().ToString("N");
        return new Texture { Name = name, NameHash = JenkHash.GenHash(name), Width = size, Height = size, Depth = 1, Levels = 1,
            Stride = size * 4, Format = TextureFormat.D3DFMT_A8R8G8B8, Data = new TextureData { FullData = pixels } };
    }
}

internal sealed partial class RenderSurface
{
    private readonly Dictionary<PaintChannel, PaintSelection> paints = new()
    {
        [PaintChannel.Primary] = new(null, PaintFinish.Original), [PaintChannel.Secondary] = new(null, PaintFinish.Original)
    };
    private readonly Dictionary<ShaderFX, PaintChannel> paintChannels = new();
    private readonly Dictionary<RenderableGeometry, PaintBaseline> paintOriginals = new();
    private readonly Dictionary<PaintFinish, Texture> finishTextures = new();
    public event Action? PaintChannelsChanged;
    public int PaintMaterialCount(PaintChannel channel) => paintChannels.Values.Count(value => value == channel);
    public PaintSelection GetPaint(PaintChannel channel) => paints[channel];

    public void SetPaint(PaintChannel channel, Color? colour, PaintFinish finish)
    {
        lock (renderer.RenderSyncRoot)
        {
            paints[channel] = new PaintSelection(colour, finish);
        }
    }

    public void ResetPaint()
    {
        SetPaint(PaintChannel.Primary, null, PaintFinish.Original);
        SetPaint(PaintChannel.Secondary, null, PaintFinish.Original);
    }

    private void InitializePaint()
    {
        paintOriginals.Clear(); paintChannels.Clear();
        foreach (var drawable in materialDrawables)
        foreach (var shader in drawable.ShaderGroup?.Shaders?.data_items ?? Array.Empty<ShaderFX>())
            if (PaintMaterials.Channel(shader) is PaintChannel channel) paintChannels[shader] = channel;
        PaintChannelsChanged?.Invoke();
    }

    private void ApplyPaintOverrides()
    {
        foreach (var drawable in materialDrawables)
        {
            Renderable renderable = renderer.RenderableCache.GetRenderable(drawable);
            foreach (var part in renderable.AllModels ?? Array.Empty<RenderableModel>())
            foreach (var geometry in part.Geometries ?? Array.Empty<RenderableGeometry>())
            {
                if (!paintChannels.TryGetValue(geometry.DrawableGeom.Shader, out PaintChannel channel) || geometry.RenderableTextures == null) continue;
                PaintSelection selection = paints[channel];
                bool original = selection.Colour == null && selection.Finish == PaintFinish.Original;
                if (!paintOriginals.TryGetValue(geometry, out PaintBaseline baseline))
                {
                    if (original) continue;
                    int diffuse = Array.IndexOf(geometry.TextureParamHashes, ShaderParamNames.DiffuseSampler);
                    int specular = Array.IndexOf(geometry.TextureParamHashes, ShaderParamNames.SpecSampler);
                    if (diffuse < 0 || geometry.RenderableTextures[diffuse]?.IsLoaded != true ||
                        (specular >= 0 && geometry.RenderableTextures[specular]?.IsLoaded != true)) continue;
                    baseline = new PaintBaseline(geometry, diffuse, specular);
                    paintOriginals.Add(geometry, baseline);
                }
                // BadWalker refreshes texture bindings as it queues each frame. Apply the preview
                // override after that refresh each time, keeping the original bindings for reset.
                RenderableTexture? tint = selection.Diffuse == null ? null : renderer.RenderableCache.GetRenderableTexture(selection.Diffuse);
                RenderableTexture? spec = null;
                if (selection.Finish != PaintFinish.Original && baseline.SpecularIndex >= 0)
                {
                    if (!finishTextures.TryGetValue(selection.Finish, out Texture texture))
                    {
                        texture = PaintMaterials.Texture(Color.FromArgb(210, 210, 210), selection.Finish == PaintFinish.Metallic);
                        finishTextures.Add(selection.Finish, texture);
                    }
                    spec = renderer.RenderableCache.GetRenderableTexture(texture);
                }
                if ((tint != null && !tint.IsLoaded) || (spec != null && !spec.IsLoaded)) continue;
                baseline.Restore(geometry);
                if (tint != null) SetTexture(geometry, baseline.DiffuseIndex, tint);
                if (spec != null) SetTexture(geometry, baseline.SpecularIndex, spec);
                if (selection.Finish != PaintFinish.Original)
                {
                    geometry.specMapIntMask = Vector3.One;
                    geometry.specularIntensityMult = selection.Finish switch
                    {
                        PaintFinish.Matte => 0.015f, PaintFinish.Satin => 0.15f, PaintFinish.Classic => 0.55f,
                        PaintFinish.Metallic => 1.25f, PaintFinish.Chrome => 2.8f, _ => baseline.Intensity
                    };
                    geometry.specularFalloffMult = selection.Finish is PaintFinish.Matte or PaintFinish.Satin ? 20 : 180;
                    geometry.specularFresnel = selection.Finish == PaintFinish.Chrome ? 0.9f : 0.15f;
                }
            }
        }
    }

    private static void SetTexture(RenderableGeometry geometry, int index, RenderableTexture texture)
    {
        geometry.RenderableTextures[index] = texture;
        if (geometry.RenderableTexturesHD != null && index < geometry.RenderableTexturesHD.Length) geometry.RenderableTexturesHD[index] = texture;
    }

    private sealed class PaintBaseline
    {
        public readonly int DiffuseIndex, SpecularIndex;
        public readonly float Intensity;
        private readonly float falloff, fresnel;
        private readonly Vector3 mask;
        private readonly RenderableTexture diffuse, spec;
        private readonly RenderableTexture? diffuseHd, specHd;
        public PaintBaseline(RenderableGeometry geometry, int diffuseIndex, int specularIndex)
        {
            DiffuseIndex = diffuseIndex; SpecularIndex = specularIndex;
            diffuse = geometry.RenderableTextures[diffuseIndex];
            spec = specularIndex >= 0 ? geometry.RenderableTextures[specularIndex] : null!;
            diffuseHd = geometry.RenderableTexturesHD?[diffuseIndex];
            specHd = specularIndex >= 0 ? geometry.RenderableTexturesHD?[specularIndex] : null;
            Intensity = geometry.specularIntensityMult; falloff = geometry.specularFalloffMult;
            fresnel = geometry.specularFresnel; mask = geometry.specMapIntMask;
        }
        public void Restore(RenderableGeometry geometry)
        {
            geometry.RenderableTextures[DiffuseIndex] = diffuse;
            if (geometry.RenderableTexturesHD != null) geometry.RenderableTexturesHD[DiffuseIndex] = diffuseHd;
            if (SpecularIndex >= 0)
            {
                geometry.RenderableTextures[SpecularIndex] = spec;
                if (geometry.RenderableTexturesHD != null) geometry.RenderableTexturesHD[SpecularIndex] = specHd;
            }
            geometry.specularIntensityMult = Intensity; geometry.specularFalloffMult = falloff;
            geometry.specularFresnel = fresnel; geometry.specMapIntMask = mask;
        }
    }
}
