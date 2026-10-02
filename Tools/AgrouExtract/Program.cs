using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.Encryption.Aes;

if (args[0] == "reflect")
{
    var asm = System.Reflection.Assembly.Load("CUE4Parse-Conversion");
    foreach (var t in asm.GetExportedTypes().Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.FullName!, args[1])))
    {
        Console.WriteLine(t.FullName + (t.IsEnum ? " = " + string.Join(",", Enum.GetNames(t)) : ""));
        if (args.Length > 2) foreach (var m in t.GetMembers()) Console.WriteLine("   " + m);
        foreach (var c in t.GetConstructors()) Console.WriteLine("   ctor(" + string.Join(", ", c.GetParameters().Select(p => p.Name + "=" + (p.HasDefaultValue ? p.DefaultValue : "?"))) + ")");
    }
    return;
}
var provider = new DefaultFileProvider(@"C:\Program Files (x86)\Steam\steamapps\common\Agrou\Agrou\Content\Paks", SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_UE4_25));
provider.Initialize();
provider.SubmitKey(new FGuid(), new FAesKey(new byte[32]));
Console.Error.WriteLine($"files: {provider.Files.Count}");
if (args[0] == "list") { File.WriteAllLines(args[1], provider.Files.Keys.OrderBy(k => k)); return; }
if (args[0] == "sound")
{
    foreach (var path in provider.Files.Keys.Where(k => k.StartsWith(args[1], StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset")))
    {
        try
        {
            foreach (var o in provider.LoadPackage(path).GetExports().OfType<CUE4Parse.UE4.Assets.Exports.Sound.USoundWave>())
            {
                CUE4Parse_Conversion.Sounds.SoundDecoder.Decode(o, true, out var fmt, out var data);
                if (data == null) { Console.WriteLine($"vide {path}"); continue; }
                var outp = Path.Combine(args[2], o.Name + "." + fmt.ToLower());
                File.WriteAllBytes(outp, data); Console.WriteLine($"{outp} {data.Length}");
            }
        }
        catch (Exception e) { Console.WriteLine($"ERR {path}: {e.Message}"); }
    }
}
if (args[0] == "mapinfo")
{
    var pkg = provider.LoadPackage(args[1]);
    var ex = pkg.GetExports().ToList();
    foreach (var g in ex.GroupBy(e => e.ExportType).OrderByDescending(g => g.Count()).Take(25)) Console.WriteLine($"{g.Count(),5} {g.Key}");
}
if (args[0] == "types")
{
    foreach (var path in File.ReadAllLines(args[1]))
        try { Console.WriteLine($"{path} : {string.Join(",", provider.LoadPackage(path).GetExports().Select(e => e.ExportType + ":" + e.Name).Take(4))}"); }
        catch (Exception e) { Console.WriteLine($"{path} ERR {e.Message}"); }
}
if (args[0] == "export")   // export <dossier> <chemin.uasset>... : maillages glTF, textures PNG, materiaux, animations
{
    var opts = new CUE4Parse_Conversion.Options.ExportOptions(meshFormat: Environment.GetEnvironmentVariable("FMT") == "usd" ? CUE4Parse_Conversion.Options.EMeshFormat.USD : CUE4Parse_Conversion.Options.EMeshFormat.Gltf2);
    var session = new CUE4Parse_Conversion.ExportSession((a, ct) => { });
    foreach (var path in args.Skip(2))
        foreach (var o in provider.LoadPackage(path).GetExports()) try { session.Add(o); } catch (NotSupportedException) { }
    foreach (var r in await session.RunAsync(args[1], opts, null!, default))
        Console.WriteLine($"{(r.Success ? "OK" : "ERR")} {r.ObjectPath} {string.Join(";", r.DiskFilePaths ?? new List<string>())} {r.Error?.Message}");
}
if (args[0] == "imports")
    foreach (var path in args.Skip(1))
    {
        var pkg = (CUE4Parse.UE4.Assets.Package)provider.LoadPackage(path);
        Console.WriteLine(path + " -> " + string.Join(", ", pkg.ImportMap.Where(i => i.ClassName.Text is "StaticMesh" or "SkeletalMesh" or "Texture2D").Select(i => i.ObjectName.Text)));
    }
if (args[0] == "probe")
{
    var pkg = provider.LoadPackage(args[1]);
    int n = 0;
    foreach (var e in pkg.GetExports())
    {
        if (e.ExportType != "StaticMeshComponent") continue;
        var sm = e.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex>("StaticMesh");
        var tpl = e.Template;
        Console.WriteLine($"{e.Outer?.Name}/{e.Name} mesh={(sm == null || sm.IsNull ? "-" : sm.Name)} tpl={tpl?.Name} tplpath={tpl?.GetPathName()}");
        if (++n > 25) break;
    }
}
if (args[0] == "bpfix")   // bpfix <cache> <map...> : maillages des composants de Blueprint (definis dans la classe) injectes dans l'USD de la map
{
    var cache = args[1];
    foreach (var map in args.Skip(2))
    {
        var pkg = provider.LoadPackage($"Agrou/Content/Maps/{map}.umap");
        var exact = new Dictionary<(string, string), string>();
        var byComp = new Dictionary<string, HashSet<string>>();
        var need = new HashSet<string>();
        foreach (var e in pkg.GetExports())
        {
            if (e.ExportType != "StaticMeshComponent" && e.ExportType != "InstancedStaticMeshComponent") continue;
            var outer = e.Outer?.Name.Text ?? "";
            if (System.Text.RegularExpressions.Regex.IsMatch(outer, "FeuDeCamps|GoodSky|Moon|Sky", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
            var sm = e.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex>("StaticMesh");
            if (sm != null && !sm.IsNull) continue;   // deja dans l'USD
            CUE4Parse.UE4.Assets.Exports.UObject? t = null;
            try { t = e.Template?.Load(); } catch { }
            for (int depth = 0; t != null && depth < 4; depth++)
            {
                var m = t.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex>("StaticMesh");
                if (m != null && !m.IsNull)
                {
                    var path = m.ResolvedObject?.GetPathName();
                    if (path == null) break;
                    exact[(outer, e.Name)] = path; need.Add(path);
                    if (!byComp.TryGetValue(e.Name, out var hs)) byComp[e.Name] = hs = new HashSet<string>();
                    hs.Add(path);
                    break;
                }
                try { t = t.Template?.Load(); } catch { t = null; }
            }
        }
        Console.WriteLine($"{map}: {exact.Count} composants, {need.Count} maillages");
        // Export USD des maillages manquants (meme arborescence que l'export du monde).
        var opts = new CUE4Parse_Conversion.Options.ExportOptions(meshFormat: CUE4Parse_Conversion.Options.EMeshFormat.USD);
        var session = new CUE4Parse_Conversion.ExportSession((a, ct) => { });
        foreach (var p in need)
        {
            var file = p.Substring(0, p.LastIndexOf('.'));
            if (File.Exists(Path.Combine(cache, file + ".usda"))) continue;
            try { session.Add(provider.LoadPackageObject(p)); } catch (Exception ex) { Console.WriteLine("  ?? " + p + " " + ex.Message); }
        }
        foreach (var r in await session.RunAsync(cache, opts, null!, default)) if (!r.Success) Console.WriteLine("  ERR " + r.ObjectPath);
        // Injection : sous chaque Xform de composant, une reference vers le maillage.
        var usda = Path.Combine(cache, "Agrou/Content/Maps", map + ".usda");
        var lines = File.ReadAllLines(usda);
        var outl = new List<string>(lines.Length + 4096);
        var stack = new List<(string type, string name)>();
        (string type, string name)? pending = null;
        int added = 0;
        var defRx = new System.Text.RegularExpressions.Regex(@"^\s*def (\w+) ""([^""]+)""");
        foreach (var line in lines)
        {
            outl.Add(line);
            var mm = defRx.Match(line);
            if (mm.Success) { pending = (mm.Groups[1].Value, mm.Groups[2].Value); continue; }
            var tr = line.Trim();
            if (tr == "{" || tr.EndsWith("{"))
            {
                var cur = pending ?? ("", "");
                stack.Add(cur); pending = null;
                if (cur.type == "Xform")
                {
                    var actor = stack.Take(stack.Count - 1).LastOrDefault(s => s.type == "Scope").name ?? "";
                    string? mesh = exact.TryGetValue((actor, cur.name), out var x) ? x
                        : byComp.TryGetValue(cur.name, out var hs) && hs.Count == 1 ? hs.First() : null;
                    if (mesh != null)
                    {
                        var file = mesh.Substring(0, mesh.LastIndexOf('.'));
                        var rel = file.StartsWith("Agrou/Content/") ? "../" + file.Substring("Agrou/Content/".Length) : "../../../" + file;
                        var ind = new string(' ', line.Length - line.TrimStart().Length + 4);
                        outl.Add($"{ind}def Mesh \"BPMesh\" (\n{ind}    prepend references = @{rel}.usda@\n{ind})\n{ind}{{\n{ind}}}");
                        added++;
                    }
                }
            }
            else if (tr == "}" || tr.StartsWith("}")) { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
        }
        File.WriteAllLines(usda, outl);
        Console.WriteLine($"{map}: {added} maillages injectes");
    }
}
if (args[0] == "parents")
    foreach (var path in File.ReadAllLines(args[1]))
    {
        var o = provider.LoadPackage(path).GetExports().First();
        var chain = new List<string>();
        CUE4Parse.UE4.Assets.Exports.UObject? cur = o;
        for (int d = 0; cur != null && d < 6; d++)
        {
            var pk = (CUE4Parse.UE4.Assets.Package)cur.Owner!;
            var tex = pk.ImportMap.Where(i => i.ClassName.Text == "Texture2D").Select(i => i.ObjectName.Text);
            chain.Add(cur.Name + "[" + string.Join(",", tex) + "]");
            var par = cur.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex>("Parent");
            try { cur = par == null || par.IsNull ? null : par.Load(); } catch { cur = null; }
        }
        Console.WriteLine(path.Split('/').Last() + " : " + string.Join(" <- ", chain));
    }
if (args[0] == "masks")   // masks <cache> : materiaux decoupes (BlendMode 1) -> texture de masque (opacite) de leur chaine, ecrite dans <materiau>.mask
{
    var cache = args[1];
    var opts = new CUE4Parse_Conversion.Options.ExportOptions();
    foreach (var json in Directory.GetFiles(cache, "*.json", SearchOption.AllDirectories))
    {
        var text = File.ReadAllText(json);
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"""BlendMode"":\s*1")) continue;
        var rel = Path.GetRelativePath(cache, json).Replace(Path.DirectorySeparatorChar, '/');
        if (!rel.StartsWith("Agrou/")) continue;
        var pkgPath = rel.Substring(0, rel.Length - 5) + ".uasset";
        if (!provider.Files.ContainsKey(pkgPath)) continue;
        string? texPath = null;
        try
        {
            CUE4Parse.UE4.Assets.Exports.UObject? cur = provider.LoadPackage(pkgPath).GetExports().First();
            for (int d = 0; cur != null && d < 6 && texPath == null; d++)
            {
                var pk = (CUE4Parse.UE4.Assets.Package)cur.Owner!;
                foreach (var i in pk.ImportMap.Where(i => i.ClassName.Text == "Texture2D"))
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(i.ObjectName.Text, "mask|opacity|alpha", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                    if (i.ObjectName.Text.Contains("Black")) continue;
                    var outer = i.OuterIndex.IsImport ? pk.ImportMap[-i.OuterIndex.Index - 1] : null;
                    texPath = outer?.ObjectName.Text; break;
                }
                var par = cur.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex>("Parent");
                cur = par == null || par.IsNull ? null : par.Load();
            }
        }
        catch (Exception e) { Console.WriteLine("  ?? " + rel + " " + e.Message); }
        if (texPath == null) { Console.WriteLine("sans masque : " + rel); continue; }
        var tp = texPath.TrimStart('/').Replace("Game/", "Agrou/Content/");
        if (!File.Exists(Path.Combine(cache, tp + ".png")))
        {
            var session = new CUE4Parse_Conversion.ExportSession((a, ct) => { });
            try { session.Add(provider.LoadPackage(tp + ".uasset").GetExports().First()); await session.RunAsync(cache, opts, null!, default); }
            catch (Exception e) { Console.WriteLine("  ?? tex " + tp + " " + e.Message); continue; }
        }
        File.WriteAllText(json.Substring(0, json.Length - 5) + ".mask", tp + ".0");
        Console.WriteLine("masque " + rel + " -> " + tp);
    }
}
if (args[0] == "fixmats")   // fixmats <cache> : fiches de materiaux sans texture de base -> texture trouvee dans la chaine des parents
{
    var cache = args[1];
    var rx = new System.Text.RegularExpressions.Regex("noise|blend|mask|_n$|normal|ripple|default|emis|rough|metal|foam|seafoam", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    var opts = new CUE4Parse_Conversion.Options.ExportOptions();
    foreach (var json in Directory.GetFiles(cache, "*.json", SearchOption.AllDirectories))
    {
        var text = File.ReadAllText(json);
        if (text.Contains("\"PM_Diffuse\"") || !text.Contains("\"BlendMode\"")) continue;
        var rel = Path.GetRelativePath(cache, json).Replace(Path.DirectorySeparatorChar, '/');
        if (!rel.StartsWith("Agrou/")) continue;
        var pkgPath = rel.Substring(0, rel.Length - 5) + ".uasset";
        if (!provider.Files.ContainsKey(pkgPath)) continue;
        string? texPath = null;
        try
        {
            CUE4Parse.UE4.Assets.Exports.UObject? cur = provider.LoadPackage(pkgPath).GetExports().First();
            for (int d = 0; cur != null && d < 6 && texPath == null; d++)
            {
                var pk = (CUE4Parse.UE4.Assets.Package)cur.Owner!;
                foreach (var i in pk.ImportMap.Where(i => i.ClassName.Text == "Texture2D"))
                {
                    if (rx.IsMatch(i.ObjectName.Text)) continue;
                    var outer = pk.ImportMap.FirstOrDefault(o => i.OuterIndex.IsImport && o == pk.ImportMap[-i.OuterIndex.Index - 1]);
                    texPath = outer?.ObjectName.Text; break;
                }
                var par = cur.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex>("Parent");
                cur = par == null || par.IsNull ? null : par.Load();
            }
        }
        catch (Exception e) { Console.WriteLine("  ?? " + rel + " " + e.Message); }
        if (texPath == null) { Console.WriteLine("sans texture : " + rel); continue; }
        var tp = texPath.TrimStart('/').Replace("Game/", "Agrou/Content/");
        if (!File.Exists(Path.Combine(cache, tp + ".png")))
        {
            var session = new CUE4Parse_Conversion.ExportSession((a, ct) => { });
            try { session.Add(provider.LoadPackage(tp + ".uasset").GetExports().First()); await session.RunAsync(cache, opts, null!, default); }
            catch (Exception e) { Console.WriteLine("  ?? tex " + tp + " " + e.Message); continue; }
        }
        File.WriteAllText(json.Substring(0, json.Length - 5) + ".diffuse", tp + ".0");
        Console.WriteLine("ok " + rel + " -> " + tp);
    }
}
if (args[0] == "bpdump")   // bpdump <package> <sortie.json> : exports (dont le bytecode Kismet des fonctions) en JSON
{
    provider.ReadScriptData = true;
    var exps = provider.LoadPackage(args[1]).GetExports().ToList();
    File.WriteAllText(args[2], Newtonsoft.Json.JsonConvert.SerializeObject(exps, Newtonsoft.Json.Formatting.Indented));
    Console.WriteLine($"{exps.Count} exports -> {args[2]}");
}
if (args[0] == "raw")   // raw <dossier> <chemin>... : fichiers bruts (polices .ufont = TTF)
    foreach (var path in args.Skip(2))
    {
        var data = provider.Files[path].Read();
        var outp = Path.Combine(args[1], Path.GetFileNameWithoutExtension(path) + (path.EndsWith(".ufont") ? ".ttf" : Path.GetExtension(path)));
        File.WriteAllBytes(outp, data); Console.WriteLine($"{outp} {data.Length}");
    }
