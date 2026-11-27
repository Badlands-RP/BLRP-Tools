using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace Badlands.LiveryTool;

internal static class PreviewMaterialSupport
{
    // Supported explicit shader batches from the packaged BadWalker master-c900f24 ShaderManager.
    // Unknown shaders must not silently use its generic BasicShader fallback.
    private static readonly HashSet<uint> Supported =
    [
        413996436, // default.sps
        1581835696, // default_um.sps
        200682448, // default_tnt.sps
        1891750326, // default_spec.sps
        3080824371, // default_detail.sps
        3939256279, // default_terrain_wet.sps
        2948410525, // normal.sps
        3326705511, // normal_um.sps
        1891558834, // normal_pxm.sps
        3348158737, // normal_pxm_tnt.sps
        346521853, // normal_spec.sps
        1092618307, // normal_spec_dpm.sps
        505619005, // normal_spec_detail.sps
        10880875, // normal_spec_detail_tnt.sps
        3376941572, // normal_spec_detail_dpm_tnt.sps
        1315370658, // normal_spec_detail_dpm.sps
        4246136089, // normal_spec_detail_dpm_vertdecal_tnt.sps
        810202407, // normal_spec_tnt.sps
        2121232575, // normal_spec_tnt_pxm.sps
        1489449972, // normal_spec_pxm.sps
        1117835835, // normal_spec_pxm_tnt.sps
        3085209681, // normal_spec_um.sps
        1332909972, // normal_spec_emissive.sps
        3424303599, // normal_spec_reflect.sps
        2072061694, // normal_spec_reflect_emissivenight.sps
        3564197994, // normal_spec_cubemap_reflect.sps
        1808536605, // normal_detail.sps
        557170358, // normal_detail_dpm.sps
        3420295952, // normal_diffspec.sps
        559317884, // normal_diffspec_detail_dpm_tnt.sps
        2735427639, // normal_diffspec_detail_dpm.sps
        1845339284, // normal_diffspec_detail.sps
        601788410, // normal_diffspec_detail_tnt.sps
        3756536307, // normal_diffspec_tnt.sps
        2988770526, // normal_tnt.sps
        1728634142, // normal_tnt_pxm.sps
        3362038760, // normal_terrain_wet.sps
        1685791092, // normal_reflect.sps
        2626821864, // normal_cubemap_reflect.sps
        2635608835, // emissive.sps
        443538781, // emissive_clip.sps
        2049580179, // emissive_speclum.sps
        1193295596, // emissive_tnt.sps
        1434302180, // emissivenight.sps
        1897917258, // emissivenight_geomnightonly.sps
        140448747, // emissivestrong.sps
        2247429097, // spec.sps
        1396339721, // spec_const.sps
        689627308, // spec_tnt.sps
        873285799, // spec_reflect.sps
        3634763545, // reflect.sps
        1658580369, // mirror_default.sps
        129155404, // mirror_crack.sps
        2067933913, // parallax.sps
        1084945164, // parallax_specmap.sps
        3052800384, // blend_2lyr.sps
        154074948, // spec_twiddle_tnt.sps
        14185869, // weapon_normal_spec_tnt.sps
        1106229739, // cloth_default.sps
        1800418130, // cloth_normal_spec.sps
        476099326, // cloth_spec_cutout.sps
        170694389, // cloth_normal_spec_cutout.sps
        38543133, // cloth_normal_spec_tnt.sps
        1530399584, // cutout.sps
        3190732435, // cutout_um.sps
        3959636627, // cutout_tnt.sps
        2219447268, // cutout_fence.sps
        3091995132, // cutout_fence_normal.sps
        3187789425, // cutout_hard.sps
        3339370144, // cutout_spec_tnt.sps
        1264076685, // normal_cutout.sps
        46387092, // normal_cutout_tnt.sps
        748520668, // normal_cutout_um.sps
        807996366, // normal_spec_cutout.sps
        3300978494, // normal_spec_cutout_tnt.sps
        582493193, // trees_lod.sps
        2322653400, // trees.sps
        3334613197, // trees_tnt.sps
        3192134330, // trees_normal.sps
        1224713457, // trees_normal_spec.sps
        1229591973, // trees_normal_spec_tnt.sps
        4265705004, // trees_normal_diffspec.sps
        2245870123, // trees_normal_diffspec_tnt.sps
        2021887493, // normal_spec_alpha.sps
        1086592620, // normal_spec_reflect_alpha.sps
        1436689415, // normal_spec_reflect_emissivenight_alpha.sps
        4237650253, // normal_spec_screendooralpha.sps
        1564459451, // normal_alpha.sps
        763839200, // normal_reflect_alpha.sps
        179247185, // emissive_alpha.sps
        1314864030, // emissive_alpha_tnt.sps
        1478174766, // emissive_additive_alpha.sps
        3733846327, // emissivenight_alpha.sps
        3174327089, // emissivestrong_alpha.sps
        257450439, // spec_alpha.sps
        2116642565, // spec_reflect_alpha.sps
        298255408, // alpha.sps
        181295180, // reflect_alpha.sps
        1896243360, // normal_screendooralpha.sps
        3724703640, // spec_screendooralpha.sps
        2161953435, // cloth_spec_alpha.sps
        3203310712, // cloth_normal_spec_alpha.sps
        1529202445, // water_river.sps
        4064804434, // water_riverlod.sps
        2871265627, // water_riverocean.sps
        1507348828, // water_rivershallow.sps
        3945561843, // water_fountain.sps
        4234404348, // water_shallow.sps
        1077877097, // water_poolenv.sps
        3053856997, // water_riverfoam.sps
        3066724854, // water_terrainfoam.sps
        1471966282, // water_decal.sps
        3928756789, // glass.sps
        4018753208, // glass_pv.sps
        2800545026, // glass_pv_env.sps
        1263059426, // glass_env.sps
        3398951093, // glass_spec.sps
        1520288031, // glass_reflect.sps
        3924045432, // glass_emissive.sps
        837003310, // glass_emissivenight.sps
        485710087, // glass_emissivenight_alpha.sps
        1359281054, // glass_breakable.sps
        4237090538, // glass_breakable_screendooralpha.sps
        430314084, // glass_displacement.sps
        2866652360, // glass_normal_spec_reflect.sps
        2055615352, // glass_emissive_alpha.sps
        3140040342, // decal.sps
        1093522222, // decal_tnt.sps
        3948167519, // decal_glue.sps
        3880384844, // decal_spec_only.sps
        341123999, // decal_normal_only.sps
        2918136469, // decal_emissive_only.sps
        2698880237, // decal_emissivenight_only.sps
        600733812, // decal_amb_only.sps
        1145906525, // normal_decal.sps
        1342302630, // normal_decal_pxm.sps
        2269053854, // normal_decal_pxm_tnt.sps
        108580378, // normal_decal_tnt.sps
        2189252961, // normal_spec_decal.sps
        992110385, // normal_spec_decal_detail.sps
        941334042, // normal_spec_decal_nopuddle.sps
        2739041469, // normal_spec_decal_tnt.sps
        3606424483, // normal_spec_decal_pxm.sps
        2842248626, // spec_decal.sps
        3717415672, // spec_reflect_decal.sps
        2457676400, // reflect_decal.sps
        2655725442, // decal_dirt.sps
        2706821972, // mirror_decal.sps
        1851110504, // distance_map.sps
        4113118754, // trees_lod2.sps
        2131453483, // terrain_cb_w_4lyr.sps
        404312839, // terrain_cb_w_4lyr_lod.sps
        1196319128, // terrain_cb_w_4lyr_spec.sps
        1539365454, // terrain_cb_w_4lyr_spec_pxm.sps
        2415127391, // terrain_cb_w_4lyr_pxm_spm.sps
        4024079163, // terrain_cb_w_4lyr_pxm.sps
        1437909171, // terrain_cb_w_4lyr_cm_pxm.sps
        1185891401, // terrain_cb_w_4lyr_cm_tnt.sps
        3254053796, // terrain_cb_w_4lyr_cm_pxm_tnt.sps
        1567099655, // terrain_cb_w_4lyr_cm.sps
        3908107877, // terrain_cb_w_4lyr_2tex.sps
        341330913, // terrain_cb_w_4lyr_2tex_blend.sps
        673057355, // terrain_cb_w_4lyr_2tex_blend_lod.sps
        1046220944, // terrain_cb_w_4lyr_2tex_blend_pxm.sps
        2344506399, // terrain_cb_w_4lyr_2tex_blend_pxm_spm.sps
        3444846649, // terrain_cb_w_4lyr_2tex_pxm.sps
        3369161623, // terrain_cb_4lyr.sps
        1003015712, // terrain_cb_w_4lyr_spec_int_pxm.sps
        1927591706, // terrain_cb_w_4lyr_spec_int.sps
        3550605441, // terrain_cb_4lyr_lod.sps
        3854885487, // cable.sps
        753908000, // vehicle_basic.sps
        1107511867, // vehicle_generic.sps
        3274951810, // vehicle_paint1.sps
        1009159769, // vehicle_paint2.sps
        2045642561, // vehicle_paint3.sps
        1534086746, // vehicle_paint3_enveff.sps
        4262329590, // vehicle_paint4.sps
        60950417, // vehicle_paint4_enveff.sps
        249472155, // vehicle_paint5_enveff.sps
        354168229, // vehicle_paint6_enveff.sps
        617726044, // vehicle_mesh.sps
        1138799003, // vehicle_mesh2_enveff.sps
        2162256878, // vehicle_tire.sps
        1337209217, // vehicle_tire_emissive.sps
        3106021319, // vehicle_interior.sps
        2837548125, // vehicle_interior2.sps
        2094873540, // vehicle_shuts.sps
        2405328911, // vehicle_licenseplate.sps
        2226589567, // vehicle_lights.sps
        364912658, // vehicle_lightsemissive.sps
        3030872505, // vehicle_emissive_opaque.sps
        1930196358, // vehicle_emissive_alpha.sps
        146667297, // vehicle_badges.sps
        4162395624, // vehicle_dash_emissive.sps
        254152173, // vehicle_dash_emissive_opaque.sps
        3355845283, // vehicle_detail2.sps
        4097152976, // vehicle_track.sps
        4268056926, // vehicle_track2.sps
        3631243954, // vehicle_blurredrotor.sps
        457610770, // vehicle_nosplash.sps
        3621563260, // vehicle_nowater.sps
        430888562, // vehicle_paint8.sps
        4118002252, // vehicle_paint9.sps
        158342452, // vehicle_detail.sps
        482429992, // vehicle_track_emissive.sps
        1041778472, // vehicle_decal.sps
        1462664157, // vehicle_decal2.sps
        15603050, // vehicle_blurredrotor_emissive.sps
        3096299666, // vehicle_vehglass.sps
        588619930, // vehicle_vehglass_inner.sps
        3986926894, // vehicle_cloth.sps
        2617558500, // vehicle_cutout.sps
    ];

    public static void Validate(XDocument model)
    {
        string[] unsupported = model.Descendants("Shaders").Elements("Item").Select(item => item.Element("FileName")?.Value ?? "")
            .Distinct().Where(name => !Supported.Contains(Hash(name))).ToArray();
        if (unsupported.Length > 0)
            throw new InvalidDataException("The bundled renderer does not support these materials: " + string.Join(", ", unsupported) + ". Preview stopped instead of using generic replacement shaders.");
    }

    private static uint Hash(string name) => name.StartsWith("hash_", StringComparison.OrdinalIgnoreCase) &&
        uint.TryParse(name[5..], System.Globalization.NumberStyles.HexNumber, null, out uint hash) ? hash : JenkHash.GenHash(name.ToLowerInvariant());
}