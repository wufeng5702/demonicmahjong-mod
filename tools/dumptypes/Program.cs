using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Program
{
    static void Main(string[] args)
    {
        var asm = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { ReadSymbols = false });

        if (args.Length > 1 && args[1] == "--xref")
        {
            XRef(asm, args[2], args.Length > 3 ? args[3] : "*");
            return;
        }

        string pat = args.Length > 1 ? args[1] : "";
        bool contains = pat.StartsWith("*");
        if (contains) pat = pat.Substring(1);
        foreach (var t in All(asm.MainModule.Types))
        {
            bool match = contains ? t.FullName.Contains(pat, StringComparison.Ordinal)
                                  : string.Equals(t.FullName, pat, StringComparison.Ordinal);
            if (!match) continue;
            Console.WriteLine("TYPE " + t.FullName
                + " [kind=" + (t.IsEnum ? "enum" : t.IsValueType ? "struct" : t.IsInterface ? "iface" : "class")
                + " base=" + (t.BaseType?.FullName ?? "")
                + "] impl=" + string.Join("; ", t.Interfaces.Select(x => x.InterfaceType.FullName)));
            foreach (var f in t.Fields.Where(f => f.Name != "value__" && !f.Name.StartsWith("Native")))
                Console.WriteLine("  F " + f.Attributes + " " + f.Name + " : " + f.FieldType.FullName);
            foreach (var p in t.Properties)
                Console.WriteLine("  P " + (p.GetMethod?.IsPublic == true ? "public " : "") + p.PropertyType.FullName + " " + p.Name);
            foreach (var m in t.Methods.Where(m => m.Name != ".ctor" && m.Name != ".cctor" && m.IsPublic))
                Console.WriteLine("  M " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.Parameters.Select(x => x.ParameterType.FullName)) + ")");
        }
    }

    static void XRef(AssemblyDefinition asm, string calleeType, string calleeMethod)
    {
        var calls = new List<string>();
        foreach (var t in All(asm.MainModule.Types))
        {
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)
                        continue;
                    var mr = ins.Operand as MethodReference;
                    if (mr == null) continue;
                    bool typeMatch = calleeType.EndsWith("*")
                        ? mr.DeclaringType.FullName.Contains(calleeType.TrimEnd('*'))
                        : mr.DeclaringType.FullName == calleeType;
                    if (!typeMatch) continue;
                    if (calleeMethod != "*" && mr.Name != calleeMethod) continue;
                    calls.Add(m.DeclaringType.FullName + "::" + m.Name + " ==> " + mr.DeclaringType.FullName + "::" + mr.Name);
                }
            }
        }
        foreach (var c in calls.Distinct()) Console.WriteLine(c);
        Console.WriteLine("TOTAL=" + calls.Distinct().Count());
    }

    static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts)
    {
        foreach (var t in ts) { yield return t; foreach (var n in All(t.NestedTypes)) yield return n; }
    }
}