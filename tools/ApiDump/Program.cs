using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;

// Usage:
//   ApiDump <assembly> --list [filterRegex]        -> one line per type
//   ApiDump <assembly> --type <ExactTypeName>      -> full member dump
//   ApiDump <assembly> --grep <regex>              -> types + matching member names
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: ApiDump <assembly> --list|--type|--grep <arg>");
            return 1;
        }

        var path = args[0];
        var mode = args[1];
        var arg = args.Length > 2 ? args[2] : "";

        using var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = MakeResolver(path) });
        var types = AllTypes(asm.MainModule).ToList();

        switch (mode)
        {
            case "--list":
            {
                var rx = arg.Length > 0 ? new Regex(arg, RegexOptions.IgnoreCase) : null;
                foreach (var t in types)
                {
                    var full = FullName(t);
                    if (rx != null && !rx.IsMatch(full)) continue;
                    Console.WriteLine($"{full} : {BaseName(t)}");
                }
                return 0;
            }
            case "--grep":
            {
                var rx = new Regex(arg, RegexOptions.IgnoreCase);
                foreach (var t in types)
                {
                    var hits = new List<string>();
                    if (rx.IsMatch(t.Name)) hits.Add("<type>");
                    foreach (var m in t.Methods)
                        if (rx.IsMatch(m.Name)) hits.Add("M " + m.Name);
                    foreach (var f in t.Fields)
                        if (rx.IsMatch(f.Name)) hits.Add("F " + f.Name);
                    foreach (var p in t.Properties)
                        if (rx.IsMatch(p.Name)) hits.Add("P " + p.Name);
                    if (hits.Count == 0) continue;
                    Console.WriteLine($"{FullName(t)} : {BaseName(t)}");
                    foreach (var h in hits.Distinct().Take(40)) Console.WriteLine("    " + h);
                }
                return 0;
            }
            case "--refs":
            {
                foreach (var t in types)
                {
                    var hits = new List<string>();
                    foreach (var p in t.Properties)
                        if (TypeNameIs(p.PropertyType, arg)) hits.Add("P " + p.Name);
                    foreach (var f in t.Fields)
                        if (TypeNameIs(f.FieldType, arg) && !f.Name.Contains("NativeFieldInfoPtr")) hits.Add("F " + f.Name);
                    foreach (var m in t.Methods)
                    {
                        if (m.Name.Contains("NativeMethodInfoPtr")) continue;
                        if (TypeNameIs(m.ReturnType, arg)) hits.Add("M " + m.Name + " -> " + m.ReturnType.Name);
                        foreach (var p in m.Parameters)
                            if (TypeNameIs(p.ParameterType, arg)) hits.Add("M " + m.Name + "(" + p.ParameterType.Name + " " + p.Name + ")");
                    }
                    if (hits.Count == 0) continue;
                    Console.WriteLine($"{FullName(t)} : {BaseName(t)}");
                    foreach (var h in hits.Distinct().Take(30)) Console.WriteLine("    " + h);
                }
                return 0;
            }
            case "--type":
            {
                foreach (var t in types.Where(t => FullName(t) == arg || t.Name == arg || FullName(t).EndsWith("." + arg)))
                {
                    DumpType(t);
                    Console.WriteLine();
                }
                return 0;
            }
            default:
                Console.WriteLine("unknown mode " + mode);
                return 1;
        }
    }

    private static DefaultAssemblyResolver MakeResolver(string assemblyPath)
    {
        var r = new DefaultAssemblyResolver();
        r.AddSearchDirectory(System.IO.Path.GetDirectoryName(assemblyPath));
        r.AddSearchDirectory(@"D:\SteamLibrary\steamapps\common\Soulstone Survivors\BepInEx\interop");
        r.AddSearchDirectory(@"D:\SteamLibrary\steamapps\common\Soulstone Survivors\BepInEx\core");
        return r;
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var t in module.Types)
            foreach (var x in Walk(t)) yield return x;
    }

    private static IEnumerable<TypeDefinition> Walk(TypeDefinition t)
    {
        yield return t;
        foreach (var n in t.NestedTypes)
            foreach (var x in Walk(n)) yield return x;
    }

    private static bool TypeNameIs(TypeReference tr, string name)
    {
        if (tr == null) return false;
        if (tr.Name == name) return true;
        if (tr is GenericInstanceType g)
            foreach (var a in g.GenericArguments)
                if (TypeNameIs(a, name)) return true;
        if (tr is ArrayType arr) return TypeNameIs(arr.ElementType, name);
        if (tr is ByReferenceType br) return TypeNameIs(br.ElementType, name);
        return false;
    }

    private static string FullName(TypeDefinition t)
    {
        var name = t.FullName.Replace('/', '.');
        return string.IsNullOrEmpty(t.Namespace) ? name : name;
    }

    private static string BaseName(TypeDefinition t)
    {
        var b = t.BaseType;
        return b == null ? "(none)" : b.FullName;
    }

    private static void DumpType(TypeDefinition t)
    {
        Console.WriteLine($"=== {FullName(t)} : {BaseName(t)}");
        if (t.IsEnum)
        {
            foreach (var f in t.Fields.Where(f => f.HasConstant))
                Console.WriteLine($"  {f.Name} = {f.Constant}");
            return;
        }

        foreach (var f in t.Fields)
            if (!f.Name.Contains("NativeFieldInfoPtr") && !f.Name.Contains("NativeMethodInfoPtr"))
                Console.WriteLine($"  field {(f.IsStatic ? "static " : "")}{f.FieldType.Name} {f.Name}");

        foreach (var p in t.Properties)
            Console.WriteLine($"  prop {p.PropertyType.Name} {p.Name}");

        foreach (var m in t.Methods)
        {
            if (m.Name.Contains("NativeMethodInfoPtr") || m.Name.StartsWith("get_") || m.Name.StartsWith("set_")) continue;
            var ps = string.Join(", ", m.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            var access = m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "private";
            Console.WriteLine($"  {access} {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({ps})");
        }
    }
}
