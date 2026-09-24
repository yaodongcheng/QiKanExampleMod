using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// particleimport: 把骑砍的粒子 **XML**（ModuleData 里那份，编辑器源）编译成引擎运行期真正认的
    /// **二进制 Particle 资产**，打进一个全新的 tpac 包。
    ///
    /// 🔴 为什么需要它：引擎运行期的粒子表只来自 AssetPackages 里的 Particle 资产；
    /// ModuleData 的 particle_systems_*.xml + project.mbproj 注册**只服务编辑器**，运行期不读
    /// （实测：注册得好好的 XML，GetRuntimeIdByName 返回 -1；原版包 208 个粒子资产 ↔ XML 218 个 effect）。
    ///
    /// 【做法】以原版某个粒子为**骨架**（未映射的字段全部继承它的值），只覆盖我们钉死映射的那些字段。
    /// 字段映射的证据（XML 值 ↔ 二进制值逐条对上）见 Knowledge/骑砍2粒子系统.md。
    ///
    /// 用法:
    ///   tpaccli particleimport --xml &lt;我们的 XML&gt; --out &lt;出包目录&gt; [--packname lwn_prt.tpac]
    ///                         --packdir &lt;原版包目录&gt; [--template psys_game_blood_1] [--verbose]
    /// </summary>
    public static class ParticleImport
    {
        // XML 里的浮点一律 InvariantCulture 解析（本机是中文区域，别用当前区域）
        static float F(XElement e, string attr)
            => float.Parse(e.Attribute(attr)?.Value ?? "0", CultureInfo.InvariantCulture);

        static string S(XElement e, string attr) => e.Attribute(attr)?.Value ?? "";

        public static int Run(string[] args)
        {
            string xmlPath = null, outDir = ".", packName = "lwn_particles.tpac";
            string packDir = null, templateName = "psys_game_blood_1";
            bool verbose = false, split = false, allowMkdir = false;
            string probe = null;
            string cloneSrc = null, cloneNew = null;   // --clone 模式：把原版粒子**原样**（数据一字节不改）写成我们的名字，用来二分"是改写的问题还是打包的问题"
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--xml": xmlPath = args[++i]; break;
                    case "--out": outDir = args[++i]; break;
                    case "--packname": packName = args[++i]; break;
                    case "--packdir": packDir = args[++i]; break;
                    case "--template": templateName = args[++i]; break;
                    case "--verbose": verbose = true; break;
                    case "--split": split = true; break;
                    case "--mkdir": allowMkdir = true; break;
                    case "--probe": probe = args[++i]; break;   // 指纹探针：把还没解析明白的字段各填一个独一无二的值   // 一个 effect 一个 <名字>_psys.tpac（编辑器工程的 Assets/particles/ 要这个格式）
                    case "--clone": cloneSrc = args[++i]; break;
                    case "--newname": cloneNew = args[++i]; break;
                }
            }
            if (packDir == null || (xmlPath == null && cloneSrc == null && probe == null))
            {
                Console.Error.WriteLine("particleimport requires --packdir <原版包目录> and --xml <file> (或 --clone <源粒子> --newname <新名>)");
                return 1;
            }


            // 🔴 输出目录闸门：模块资产目录有 Assets / Assets_disabled **两态**（同一文件夹的两个名字，
            //    编辑器模式叫 Assets、游戏模式叫 Assets_disabled，靠 to_editor_mode.bat 改名切换）。
            //    以前这里静默 Directory.CreateDirectory —— 写错名字时会"悄悄新建一个没人读的目录"，
            //    错误被藏起来。现在：目录不存在就报错停住，确实要新建才加 --mkdir。
            Console.WriteLine($"输出目录 = {Path.GetFullPath(outDir)}");
            if (!Directory.Exists(outDir))
            {
                if (!allowMkdir)
                {
                    Console.Error.WriteLine("[STOP] 输出目录不存在 —— 已停，不自动新建。");
                    Console.Error.WriteLine("  模块资产目录有【编辑器模式 Assets / 游戏模式 Assets_disabled】两态，");
                    Console.Error.WriteLine("  先确认编辑器当前读哪一个（跑过 to_editor_mode.bat 后是 Assets）。");
                    Console.Error.WriteLine("  确认无误确实要新建时再加 --mkdir。");
                    return 1;
                }
                Directory.CreateDirectory(outDir);
                Console.WriteLine("  （该目录原本不存在，按 --mkdir 新建）");
            }
            else
            {
                // 🔴 正面检查：Assets / Assets_disabled 是**同一文件夹的两个名字**（编辑器态/游戏态），
                //    只能存在一个；两个都在 = 模块不一致，`to_game_mode.bat` 会拒绝执行。
                //    注意：产物常常写在 <模块>/Assets/particles/ 这类**子目录**里，所以要向上回溯祖先，
                //    不能只看路径最后一段。
                string dir = Path.GetFullPath(outDir);
                for (int up = 0; up < 4 && !string.IsNullOrEmpty(dir); up++)
                {
                    string leaf = Path.GetFileName(dir);
                    string twin = leaf == "Assets" ? "Assets_disabled" : (leaf == "Assets_disabled" ? "Assets" : null);
                    if (twin != null)
                    {
                        string twinPath = Path.Combine(Path.GetDirectoryName(dir), twin);
                        if (Directory.Exists(twinPath))
                            Console.Error.WriteLine("[WARN] " + dir + " 与 " + twinPath + " **同时存在** —— " +
                                                    "模块的两态目录不该并存（同一文件夹的两个名字，只能留一个）。");
                        break;
                    }
                    dir = Path.GetDirectoryName(dir);
                }
                Console.WriteLine($"  该目录现有 {Directory.GetFiles(outDir).Length} 个文件");
            }


            // ---- 1. 载入原版包：拿骨架 + 材质名→GUID 表
            var mgr = new AssetManager();
            mgr.Load(new DirectoryInfo(packDir));
            AssetItem template = null;
            var materialGuid = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in mgr.LoadedPackages)
            {
                foreach (var it in p.Items)
                {
                    if (it is Material && !materialGuid.ContainsKey(it.Name)) materialGuid[it.Name] = it.Guid;
                    if (it is Particle && string.Equals(it.Name, templateName, StringComparison.OrdinalIgnoreCase))
                        template = it;
                }
            }
            if (template == null)
            {
                Console.Error.WriteLine($"模板粒子 '{templateName}' 没在 {packDir} 里找到");
                return 1;
            }
            var tLoader = template.TypelessDataSegments.OfType<ExternalLoader<ParticleEffectData>>().FirstOrDefault();
            if (tLoader == null) { Console.Error.WriteLine("模板粒子没有数据段"); return 1; }
            var tData = tLoader.Data;
            var skeleton = tData.Emitters.FirstOrDefault();
            if (skeleton == null) { Console.Error.WriteLine("模板粒子没有 emitter"); return 1; }
            Console.WriteLine($"骨架 = {template.Name}（subVersion {skeleton.SubVersion}，{tData.Emitters.Count} 个 emitter，" +
                              $"元数据 {(template.RawMeta ?? template.WriteMetadata()).Length} 字节）");

            // ---- 1a. --probe 模式：指纹探针 —— 把尚未解析明白的字段各填一个独一无二的值，
            //      用户把它打开在编辑器里，哪个面板标签显示我填的那个数，那个字段就是谁。
            if (probe != null)
            {
                var pe = CopyEmitter(skeleton);
                pe.Name = probe;
                pe.F2 = 11.25f; pe.F19 = 12.5f; pe.F24 = 13.75f; pe.F25 = 14.5f; pe.F29 = 15.25f;
                // F28 是 emit_sphere_radius 的唯一嫌疑（关联求解 366/405 vs 次名 350/381）—— 一并探掉
                pe.F28 = 47.75f;
                pe.F3 = 16.5f; pe.F20 = 17.75f; pe.F21 = 18.5f; pe.F26 = 19.25f; pe.F27 = 20.5f; pe.F31 = 21.75f;
                // 🔴 int 字段（U*/I*）**一律不碰** —— 它们是枚举/索引的概率高，越界值会让编辑器的
                //    序列化器直接断言（实测：`rglAsset_package.h:589 Unable to serialize int value`，
                //    进去 ModKit 都进不去）。要探它们只能一次改一个、且用原版出现过的取值。
                //    这里只探 float / 向量 —— 它们不会让序列化失败。
                // 🔴 向量只填 x/y/z —— **第 4 位必须留 1.0**。引擎把这几个槽按 rglVec3 读写，
                //    上一次给 w 填了 32/34/36，编辑器一打开就断言崩：
                //    `rglBuffer.cpp:899 nearly_equals(vector->w, 1.0f) "Potential read/write miss match for rglVec3"`（2026-09-24）。
                pe.V1 = new Vector4(31.25f, 31.5f, 31.75f, 1f);
                pe.V2 = new Vector4(33.25f, 33.5f, 33.75f, 1f);
                pe.V4 = new Vector4(35.25f, 35.5f, 35.75f, 1f);
                pe.Curve1.UnknownFloat1 = 41.25f; pe.Curve1.UnknownFloat2 = 41.5f;
                pe.Curve7.CurveMultiplier = 42.25f;
                pe.Curve15 = new ParticleEffectData.EmitterParameter();
                pe.Curve15.UnknownFloat1 = 43.25f; pe.Curve15.UnknownFloat2 = 43.5f;
                pe.S11 = "lwn_probe_s11";   // S4 是父激活事件（继承来的），别动；S3 在 subVersion 0 里不存在
                pe.Validate();              // 🔴 写盘前的合法性闸门：见 ParticleEffectData.Emitter.Validate
                var pd = new ParticleEffectData();
                pd.SoundCode = String.Empty; pd.UnknownFloats.Add(0f);
                pd.Emitters.Add(pe);
                var pa = new Particle { Name = probe, Guid = DeterministicGuid(probe) };
                pa.CopyShellFrom(template);
                var pl = new ExternalLoader<ParticleEffectData>(pd);
                pl.OwnerGuid = pa.Guid;
                pa.TypelessDataSegments.Add(pl);
                var pp = new AssetPackage(); pp.Items.Add(pa);
                Directory.CreateDirectory(outDir);
                string pop = Path.Combine(outDir, probe + "_psys.tpac");
                pp.Save(pop);
                Console.WriteLine($"  + 指纹探针 '{probe}' -> {Path.GetFileName(pop)} {new FileInfo(pop).Length:N0} 字节");
                Console.WriteLine("  唯一值表：F2=11.25 F19=12.5 F24=13.75 F25=14.5 F29=15.25 | F3=16.5 F20=17.75 F21=18.5 F26=19.25 F27=20.5 F31=21.75");
                Console.WriteLine("           V1=(31.25,31.5,31.75,**1**) V2=(33.25..33.75,**1**) V4=(35.25..35.75,**1**)   [第 4 位永远是 1.0，不能探]");
                Console.WriteLine("           Curve1.f1=41.25 Curve1.f2=41.5 Curve7.mult=42.25 Curve15.f1=43.25 Curve15.f2=43.5 | S3/S4/S11=lwn_probe_*");
                return 0;
            }

            // ---- 1b. --clone 模式：原样搬运一个原版粒子的数据，只换名字
            if (cloneSrc != null)
            {
                AssetItem src = null;
                foreach (var p in mgr.LoadedPackages)
                    foreach (var it in p.Items.OfType<Particle>())
                        if (string.Equals(it.Name, cloneSrc, StringComparison.OrdinalIgnoreCase)) { src = it; break; }
                if (src == null) { Console.Error.WriteLine($"源粒子 '{cloneSrc}' 没找到"); return 1; }
                var sLoader = src.TypelessDataSegments.OfType<ExternalLoader<ParticleEffectData>>().FirstOrDefault();
                if (sLoader == null) { Console.Error.WriteLine("源粒子没有数据段"); return 1; }
                var sData = sLoader.Data;
                var verbatim = new ParticleEffectData();
                using (var ms = new MemoryStream(sData.RawData))
                using (var r = new BinaryReader(ms))
                    verbatim.ReadData(r, null, sData.RawData.Length);   // 只走 RawData，字节原样

                var cl = new Particle { Name = cloneNew, Guid = DeterministicGuid(cloneNew) };
                cl.CopyShellFrom(src);
                var clLoader = new ExternalLoader<ParticleEffectData>(verbatim);
                clLoader.OwnerGuid = cl.Guid;   // 🔴 段的 owner 必须 == 资产 guid（原版实证）—— 空 owner = 引擎找得到资产、找不到数据 = emitter 全空
                cl.TypelessDataSegments.Add(clLoader);
                var onePkg = new AssetPackage();
                onePkg.Items.Add(cl);
                Directory.CreateDirectory(outDir);
                string op = Path.Combine(outDir, cloneNew + "_psys.tpac");
                onePkg.Save(op);
                Console.WriteLine($"  + 原样克隆 '{cloneSrc}' -> '{cloneNew}'（{sData.RawData.Length} 字节数据，" +
                                  $"{sData.Emitters.Count} 个 emitter 按我们的读法）-> {Path.GetFileName(op)} {new FileInfo(op).Length:N0} 字节");
                return 0;
            }

            // ---- 2. 读我们的 XML，造资产
            var doc = XDocument.Load(xmlPath);
            var pkg = new AssetPackage();
            int effCount = 0, emCount = 0;
            foreach (var eff in doc.Descendants("effect"))
            {
                string name = S(eff, "name");
                if (string.IsNullOrEmpty(name)) continue;

                var data = new ParticleEffectData();
                data.SoundCode = String.Empty;
                data.UnknownFloats.Add(0f); // 原版实测：长度 1，值 0

                foreach (var em in eff.Descendants("emitter"))
                {
                    var e = CopyEmitter(skeleton);      // 深拷贝骨架（Write→Read，写法已过 roundtrip 闸门）
                    ApplyEmitter(e, em, materialGuid, name, verbose);
                    e.Validate();                       // 🔴 同探针：向量第 4 位非 1.0 会让编辑器断言
                    data.Emitters.Add(e);
                    emCount++;
                }

                var asset = new Particle { Name = name, Guid = DeterministicGuid(name) };
                asset.CopyShellFrom(template);   // 版本 + 元数据字节（否则 Save 时抛 NotImplementedException）
                var loader = new ExternalLoader<ParticleEffectData>(data);
                loader.OwnerGuid = asset.Guid;   // 🔴 同 clone：段 owner 必须 == 资产 guid
                asset.TypelessDataSegments.Add(loader);
                if (split)
                {
                    // 编辑器工程格式：Assets/particles/<资产名>_psys.tpac（原版 fight_tail_psys.tpac 就是这个形态）
                    var one = new AssetPackage();
                    one.Items.Add(asset);
                    string onePath = Path.Combine(outDir, name + "_psys.tpac");
                    one.Save(onePath);
                    Console.WriteLine($"  + Particle '{name}'  ({data.Emitters.Count} emitter) -> {Path.GetFileName(onePath)} " +
                                      $"{new FileInfo(onePath).Length:N0} 字节");
                }
                else
                {
                    pkg.Items.Add(asset);
                    Console.WriteLine($"  + Particle '{name}'  ({data.Emitters.Count} emitter)");
                }
                effCount++;
            }
            if (!split)
            {
                string outPath = Path.Combine(outDir, packName);
                pkg.Save(outPath);
                Console.WriteLine($"== 写出 {outPath}：{effCount} 个 effect / {emCount} 个 emitter / " +
                                  $"{new FileInfo(outPath).Length:N0} 字节 ==");
            }
            else
            {
                Console.WriteLine($"== split 模式：{effCount} 个 effect / {emCount} 个 emitter 已分别写入 {outDir} ==");
            }
            return 0;
        }

        /// <summary>深拷贝：序列化一遍再解析回来（写侧已过逐字节 roundtrip 闸门，所以这是精确拷贝）。</summary>
        static ParticleEffectData.Emitter CopyEmitter(ParticleEffectData.Emitter src)
        {
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, Encoding.UTF8))
                {
                    src.Write(w);
                    w.Flush();
                }
                bytes = ms.ToArray();
            }
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                return new ParticleEffectData.Emitter(r);
            }
        }

        static Guid DeterministicGuid(string name)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("lwn_particle:" + name)));
            }
        }

        // ------------------------------------------------------------------
        // 映射表（证据见 Knowledge/骑砍2粒子系统.md）：XML 参数 → 二进制字段
        // ------------------------------------------------------------------
        static void ApplyEmitter(ParticleEffectData.Emitter e, XElement em,
                                 Dictionary<string, Guid> materialGuid, string effName, bool verbose)
        {
            e.Name = S(em, "name");

            var pars = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in em.Descendants("parameter")) pars[S(p, "name")] = p;

            // flags：二进制只存**值为 true** 的那几个名字
            e.Flags.Clear();
            foreach (var f in em.Descendants("flag"))
            {
                if (string.Equals(S(f, "value"), "true", StringComparison.OrdinalIgnoreCase))
                    e.Flags.Add(S(f, "name"));
            }

            float C(string k) => pars.TryGetValue(k, out var p) ? F(p, "value") : 0f;
            float B(string k) => pars.TryGetValue(k, out var p) ? F(p, "base") : 0f;

            e.F1 = C("emitter_life");
            e.F4 = C("skew_with_particle_velocity_coef") / 500f;   // 🔴 实测：二进制 = XML ÷ 500
            e.F5 = C("skew_with_particle_velocity_limit");
            e.F7 = C("inherit_emitter_velocity");
            e.F9 = C("fadeout_distance");
            e.F22 = B("initial_rotation");                         // base
            e.F23 = pars.TryGetValue("initial_rotation", out var ir) ? F(ir, "bias") : 0f; // bias

            e.BacklightMultiplier = C("backlight_multiplier");
            e.DiffuseMultiplier = C("diffuse_multiplier");
            e.EmissiveMultiplier = C("emissive_multiplier");
            e.HeatmapMultiplier = C("heatmap_multiplier");
            e.ConeEmitAngle = C("cone_emit_angle");

            if (pars.TryGetValue("particle_size", out var ps))
            {
                e.ParticleSizeBase = F(ps, "base");
                e.ParticleSizeBias = F(ps, "bias");
                SetRawCurve(e.Curve8, ps); // 尺寸曲线：name="particle_life" 走 curve8（blood_1 实证）
            }
            e.ParticleSizeCurveOp = pars.TryGetValue("particle_size_curve_op", out var op) ? S(op, "value") : "add";

            SetParamCurve(e.Curve3, pars, "cone_emit_velocity");
            SetParamCurve(e.Curve4, pars, "damping");
            SetParamCurve(e.Curve5, pars, "angular_damping");
            SetParamCurve(e.Curve6, pars, "particle_life");
            SetParamCurve(e.Curve9, pars, "emission_rate");
            SetParamCurve(e.Curve10, pars, "emit_velocity_x");
            SetParamCurve(e.Curve11, pars, "emit_velocity_y");
            SetParamCurve(e.Curve12, pars, "emit_velocity_z");
            SetParamCurve(e.Curve13, pars, "emit_rotation_speed");
            SetParamCurve(e.Curve14, pars, "wind_effect");

            if (pars.TryGetValue("gravity", out var g))
            {
                var v = S(g, "value").Split(',');
                if (v.Length == 3) e.Gravity = new Vector4(Num(v[0]), Num(v[1]), Num(v[2]), 1f);
            }
            if (pars.TryGetValue("fixed_billboard_direction", out var fbd))
            {
                var v = S(fbd, "value").Split(',');
                if (v.Length == 3) e.FixedBillboardDirection = new Vector4(Num(v[0]), Num(v[1]), Num(v[2]), 1f);
            }

            if (pars.TryGetValue("texture_sprite_count", out var tsc))
            {
                var v = S(tsc, "value").Split(',');
                if (v.Length == 2) { e.TextureSpriteCountX = (int)Num(v[0]); e.TextureSpriteCountY = (int)Num(v[1]); }
            }
            e.TextureSpriteFrameCount = pars.TryGetValue("texture_sprite_frame_count", out var tfc) ? (uint)Num(S(tfc, "value")) : 1u;
            e.TextureSpriteFrameRate = C("texture_sprite_frame_rate");

            if (pars.TryGetValue("decal_min_scale", out var dmin)) e.DecalMinScale = Vec2(S(dmin, "value"));
            if (pars.TryGetValue("decal_max_scale", out var dmax)) e.DecalMaxScale = Vec2(S(dmax, "value"));
            if (pars.TryGetValue("quad_scale", out var qs)) e.QuadScale = Vec2(S(qs, "value"));
            if (pars.TryGetValue("quad_bias", out var qb)) e.QuadBias = Vec2(S(qb, "value"));
            e.SkinnedDecalStartIndex = (int)C("skinned_decal_start_index");
            e.SkinnedDecalEndIndex = (int)C("skinned_decal_end_index");
            e.MaxAliveParticleCount = (uint)Math.Max(0, Num(S(pars.GetValueOrDefault("max_alive_particle_count") ?? new XElement("x"), "value")));

            if (pars.TryGetValue("billboard_type", out var bt)) e.BillboardType = S(bt, "value");
            if (pars.TryGetValue("emit_volume_type", out var vt)) e.EmitVolumeType = S(vt, "value");
            if (pars.TryGetValue("collision_behaviour", out var cb)) e.CollisionBehaviour = S(cb, "value");
            if (pars.TryGetValue("emission_velocity_model", out var vm)) e.EmissionVelocityModel = S(vm, "value");

            // 材质：G4（实测）—— 按名字查原版材质 GUID
            if (pars.TryGetValue("material", out var mat))
            {
                string mn = S(mat, "value");
                if (!string.IsNullOrEmpty(mn))
                {
                    if (materialGuid.TryGetValue(mn, out var mg)) e.G4 = mg;
                    else Console.Error.WriteLine($"  !! {effName}/{e.Name}: 材质 '{mn}' 在原版包里找不到 —— 保持骨架材质");
                }
            }

            // 颜色/透明度曲线
            if (pars.TryGetValue("particle_color", out var pc))
            {
                var color = pc.Element("color");
                var alpha = pc.Element("alpha");
                e.Color.Colors.Clear();
                if (color != null)
                    foreach (var k in color.Descendants("key"))
                        e.Color.Colors[F(k, "time")] = Vec4(S(k, "value"));
                e.Color.Alphas.Clear();
                if (alpha != null)
                    foreach (var k in alpha.Descendants("key"))
                        e.Color.Alphas[F(k, "time")] = F(k, "value");
            }

            if (verbose)
                Console.WriteLine($"     · {e.Name}: F1={e.F1:0.###} F4={e.F4:0.#####} F5={e.F5:0.###} F7={e.F7:0.###} " +
                                  $"F9={e.F9:0.###} size={e.ParticleSizeBase:0.###}±{e.ParticleSizeBias:0.###} " +
                                  $"sprite={e.TextureSpriteCountX}x{e.TextureSpriteCountY}@{e.TextureSpriteFrameRate:0.#} " +
                                  $"flags={e.Flags.Count} material={(e.G4 == Guid.Empty ? "<继承>" : e.G4.ToString().Substring(0, 8))}");
        }

        static float Num(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);
        static Vector2 Vec2(string s) { var v = s.Split(','); return v.Length == 2 ? new Vector2(Num(v[0]), Num(v[1])) : new Vector2(1, 1); }
        static Vector4 Vec4(string s) { var v = s.Split(','); return v.Length == 3 ? new Vector4(Num(v[0]), Num(v[1]), Num(v[2]), 1f) : new Vector4(1, 1, 1, 1); }

        /// <summary>base/bias 落在 EmitterParameter 的两个 float 上，曲线落在它的 Curve 上（实测）。</summary>
        static void SetParamCurve(ParticleEffectData.EmitterParameter ep,
                                  Dictionary<string, XElement> pars, string key)
        {
            if (!pars.TryGetValue(key, out var p)) return;
            ep.UnknownFloat1 = F(p, "base");
            ep.UnknownFloat2 = F(p, "bias");
            SetRawCurve(ep.Curve, p);
        }

        static void SetRawCurve(ParticleEffectData.Curve c, XElement param)
        {
            var curve = param.Element("curve");
            if (curve == null) { c.Keys.Clear(); return; }
            c.Version = 0;
            c.Default = F(curve, "default");
            c.CurveMultiplier = F(curve, "curve_multiplier");
            c.Keys.Clear();
            foreach (var k in curve.Descendants("key"))
            {
                var t = S(k, "tangent").Split(',');
                c.Keys.Add(new Vector4(F(k, "time"), F(k, "value"),
                                       t.Length > 0 ? Num(t[0]) : 0f,
                                       t.Length > 1 ? Num(t[1]) : 0f));
            }
            // 引擎按「每 2 个 vec4 算一段」写计数 ⇒ 奇数个键补一个尾巴，避免截断
            if (c.Keys.Count % 2 != 0)
                c.Keys.Add(c.Keys[c.Keys.Count - 1]);
        }
    }
}
