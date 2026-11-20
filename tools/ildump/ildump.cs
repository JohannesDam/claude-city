// Minimal IL/metadata dumper for studying game assemblies (no external dependencies
// besides System.Reflection.Metadata, which ships with MSBuild).
//
// Usage:
//   ildump types   <dll> <regex>                 list matching type names
//   ildump members <dll> <exactOrRegexType>      fields / methods / properties of a type
//   ildump il      <dll> <typeRegex> [methodRegex] IL of matching methods with resolved tokens
//   ildump find    <dll> <regex>                 methods whose IL references a member/string matching regex
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

static class Program
{
    static MetadataReader md;
    static PEReader pe;
    static readonly Dictionary<short, OpCode> ops = new Dictionary<short, OpCode>();

    static int Main(string[] a)
    {
        if (a.Length < 3) { Console.WriteLine("usage: ildump types|members|il|find <dll> <pattern> [method]"); return 1; }
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        { var o = (OpCode)f.GetValue(null); ops[o.Value] = o; }
        pe = new PEReader(File.OpenRead(a[1]));
        md = pe.GetMetadataReader();
        var rx = new Regex(a[2]);
        switch (a[0])
        {
            case "types":
                foreach (var h in md.TypeDefinitions) { var n = TypeDefName(h); if (rx.IsMatch(n)) Console.WriteLine(n); }
                break;
            case "members":
                foreach (var h in md.TypeDefinitions) { var n = TypeDefName(h); if (n == a[2] || rx.IsMatch(n)) DumpMembers(h); }
                break;
            case "il":
                {
                    var mrx = a.Length > 3 ? new Regex(a[3]) : null;
                    foreach (var h in md.TypeDefinitions)
                    {
                        var n = TypeDefName(h); if (!(n == a[2] || rx.IsMatch(n))) continue;
                        var td = md.GetTypeDefinition(h);
                        foreach (var mh in td.GetMethods())
                        {
                            var m = md.GetMethodDefinition(mh); var mn = md.GetString(m.Name);
                            if (mrx != null && !mrx.IsMatch(mn)) continue;
                            DumpIL(n, mh);
                        }
                    }
                }
                break;
            case "find":
                foreach (var h in md.TypeDefinitions)
                {
                    var td = md.GetTypeDefinition(h);
                    foreach (var mh in td.GetMethods())
                    {
                        var hits = Refs(mh).Where(s => rx.IsMatch(s)).Distinct().ToList();
                        if (hits.Count > 0) Console.WriteLine(TypeDefName(h) + "::" + md.GetString(md.GetMethodDefinition(mh).Name) + "  <- " + string.Join(" | ", hits.Take(4)));
                    }
                }
                break;
        }
        return 0;
    }

    static string TypeDefName(TypeDefinitionHandle h)
    {
        var t = md.GetTypeDefinition(h);
        var name = md.GetString(t.Name);
        var ns = md.GetString(t.Namespace);
        var decl = t.GetDeclaringType();
        if (!decl.IsNil) return TypeDefName(decl) + "/" + name;
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    static string TypeName(EntityHandle h)
    {
        try
        {
            switch (h.Kind)
            {
                case HandleKind.TypeDefinition: return TypeDefName((TypeDefinitionHandle)h);
                case HandleKind.TypeReference:
                    {
                        var r = md.GetTypeReference((TypeReferenceHandle)h);
                        var ns = md.GetString(r.Namespace); var n = md.GetString(r.Name);
                        if (r.ResolutionScope.Kind == HandleKind.TypeReference) return TypeName((TypeReferenceHandle)r.ResolutionScope) + "/" + n;
                        return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
                    }
                case HandleKind.TypeSpecification:
                    {
                        var s = md.GetTypeSpecification((TypeSpecificationHandle)h);
                        return s.DecodeSignature(new Prov(), null);
                    }
            }
        }
        catch (Exception e) { return "?" + e.GetType().Name; }
        return h.Kind.ToString();
    }

    static string MemberName(EntityHandle h)
    {
        try
        {
            switch (h.Kind)
            {
                case HandleKind.MethodDefinition:
                    {
                        var m = md.GetMethodDefinition((MethodDefinitionHandle)h);
                        var sig = m.DecodeSignature(new Prov(), null);
                        return TypeDefName(m.GetDeclaringType()) + "::" + md.GetString(m.Name) + "(" + string.Join(", ", sig.ParameterTypes) + ")";
                    }
                case HandleKind.FieldDefinition:
                    {
                        var f = md.GetFieldDefinition((FieldDefinitionHandle)h);
                        return TypeDefName(f.GetDeclaringType()) + "::" + md.GetString(f.Name);
                    }
                case HandleKind.MemberReference:
                    {
                        var r = md.GetMemberReference((MemberReferenceHandle)h);
                        string extra = "";
                        if (r.GetKind() == MemberReferenceKind.Method)
                        {
                            var sig = r.DecodeMethodSignature(new Prov(), null);
                            extra = "(" + string.Join(", ", sig.ParameterTypes) + ")";
                        }
                        return TypeName(r.Parent) + "::" + md.GetString(r.Name) + extra;
                    }
                case HandleKind.MethodSpecification:
                    {
                        var s = md.GetMethodSpecification((MethodSpecificationHandle)h);
                        var args = s.DecodeSignature(new Prov(), null);
                        return MemberName(s.Method) + "<" + string.Join(", ", args) + ">";
                    }
                default: return TypeName(h);
            }
        }
        catch (Exception e) { return "?" + e.GetType().Name; }
    }

    static IEnumerable<string> Refs(MethodDefinitionHandle mh)
    {
        var m = md.GetMethodDefinition(mh);
        if (m.RelativeVirtualAddress == 0) yield break;
        var body = pe.GetMethodBody(m.RelativeVirtualAddress);
        var il = body.GetILBytes();
        int i = 0;
        while (i < il.Length)
        {
            short v = il[i++];
            if (v == 0xFE) v = (short)(0xFE00 | il[i++]);
            OpCode op; if (!ops.TryGetValue(v, out op)) break;
            switch (op.OperandType)
            {
                case OperandType.InlineMethod: case OperandType.InlineField: case OperandType.InlineType: case OperandType.InlineTok:
                    yield return MemberName(MetadataTokens.EntityHandle(BitConverter.ToInt32(il, i))); i += 4; break;
                case OperandType.InlineString:
                    yield return "\"" + md.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, i) & 0xFFFFFF)) + "\""; i += 4; break;
                case OperandType.InlineSwitch: { int n = BitConverter.ToInt32(il, i); i += 4 + 4 * n; break; }
                default: i += OperandSize(op); break;
            }
        }
    }

    static int OperandSize(OpCode op)
    {
        switch (op.OperandType)
        {
            case OperandType.InlineNone: return 0;
            case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: return 1;
            case OperandType.InlineVar: return 2;
            case OperandType.InlineI8: case OperandType.InlineR: return 8;
            default: return 4;
        }
    }

    static void DumpIL(string typeName, MethodDefinitionHandle mh)
    {
        var m = md.GetMethodDefinition(mh);
        string sigs = "";
        try { var s = m.DecodeSignature(new Prov(), null); sigs = s.ReturnType + " (" + string.Join(", ", s.ParameterTypes) + ")"; } catch { }
        Console.WriteLine("=== " + typeName + "::" + md.GetString(m.Name) + " " + sigs);
        if (m.RelativeVirtualAddress == 0) { Console.WriteLine("  (no body)"); return; }
        var body = pe.GetMethodBody(m.RelativeVirtualAddress);
        if (!body.LocalSignature.IsNil)
        {
            try
            {
                var ls = md.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(new Prov(), null);
                for (int k = 0; k < ls.Length; k++) Console.WriteLine("  .local " + k + " " + ls[k]);
            }
            catch { }
        }
        var il = body.GetILBytes();
        int i = 0;
        while (i < il.Length)
        {
            int start = i;
            short v = il[i++];
            if (v == 0xFE) v = (short)(0xFE00 | il[i++]);
            OpCode op; if (!ops.TryGetValue(v, out op)) { Console.WriteLine("  ??"); break; }
            string arg = "";
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget: arg = "IL_" + (i + 1 + (sbyte)il[i]).ToString("X4"); i += 1; break;
                case OperandType.InlineBrTarget: arg = "IL_" + (i + 4 + BitConverter.ToInt32(il, i)).ToString("X4"); i += 4; break;
                case OperandType.ShortInlineI: arg = ((sbyte)il[i]).ToString(); i += 1; break;
                case OperandType.ShortInlineVar: arg = il[i].ToString(); i += 1; break;
                case OperandType.InlineVar: arg = BitConverter.ToUInt16(il, i).ToString(); i += 2; break;
                case OperandType.InlineI: arg = BitConverter.ToInt32(il, i).ToString(); i += 4; break;
                case OperandType.InlineI8: arg = BitConverter.ToInt64(il, i).ToString(); i += 8; break;
                case OperandType.ShortInlineR: arg = BitConverter.ToSingle(il, i).ToString(); i += 4; break;
                case OperandType.InlineR: arg = BitConverter.ToDouble(il, i).ToString(); i += 8; break;
                case OperandType.InlineString: arg = "\"" + md.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, i) & 0xFFFFFF)) + "\""; i += 4; break;
                case OperandType.InlineSwitch:
                    {
                        int n = BitConverter.ToInt32(il, i); i += 4; int bas = i + 4 * n;
                        var t = new List<string>(); for (int k = 0; k < n; k++) t.Add("IL_" + (bas + BitConverter.ToInt32(il, i + 4 * k)).ToString("X4"));
                        i = bas; arg = string.Join(",", t); break;
                    }
                default: arg = MemberName(MetadataTokens.EntityHandle(BitConverter.ToInt32(il, i))); i += 4; break;
            }
            Console.WriteLine("  IL_" + start.ToString("X4") + ": " + op.Name + " " + arg);
        }
    }

    static void DumpMembers(TypeDefinitionHandle h)
    {
        var t = md.GetTypeDefinition(h);
        string bt = t.BaseType.IsNil ? "" : " : " + TypeName(t.BaseType);
        var ifs = t.GetInterfaceImplementations().Select(ih => TypeName(md.GetInterfaceImplementation(ih).Interface)).ToList();
        Console.WriteLine("type " + TypeDefName(h) + bt + (ifs.Count > 0 ? " , " + string.Join(", ", ifs) : "") + "  [" + t.Attributes + "]");
        foreach (var fh in t.GetFields())
        {
            var f = md.GetFieldDefinition(fh);
            string ft = "?"; try { ft = f.DecodeSignature(new Prov(), null); } catch { }
            string cv = "";
            if (!f.GetDefaultValue().IsNil)
            {
                var c = md.GetConstant(f.GetDefaultValue()); var br = md.GetBlobReader(c.Value);
                try { cv = " = " + br.ReadConstant(c.TypeCode); } catch { }
            }
            Console.WriteLine("  field " + ((f.Attributes & FieldAttributes.Static) != 0 ? "static " : "") + ft + " " + md.GetString(f.Name) + cv);
        }
        foreach (var ph in t.GetProperties())
        {
            var p = md.GetPropertyDefinition(ph);
            string pt = "?"; try { pt = p.DecodeSignature(new Prov(), null).ReturnType; } catch { }
            Console.WriteLine("  prop " + pt + " " + md.GetString(p.Name));
        }
        foreach (var mh in t.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            string s = "?";
            try
            {
                var sig = m.DecodeSignature(new Prov(), null);
                var pn = m.GetParameters().Select(x => md.GetParameter(x)).Where(x => x.SequenceNumber > 0).OrderBy(x => x.SequenceNumber).Select(x => md.GetString(x.Name)).ToList();
                var ps = new List<string>();
                for (int k = 0; k < sig.ParameterTypes.Length; k++) ps.Add(sig.ParameterTypes[k] + " " + (k < pn.Count ? pn[k] : ""));
                s = sig.ReturnType + " " + md.GetString(m.Name) + "(" + string.Join(", ", ps) + ")";
            }
            catch { s = md.GetString(m.Name); }
            Console.WriteLine("  method " + ((m.Attributes & MethodAttributes.Static) != 0 ? "static " : "") + ((m.Attributes & MethodAttributes.Public) == MethodAttributes.Public ? "public " : ((m.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family ? "protected " : ((m.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Assembly ? "internal " : ((m.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamORAssem ? "protected-internal " : "")))) + ((m.Attributes & MethodAttributes.Abstract) != 0 ? "abstract " : ((m.Attributes & MethodAttributes.Virtual) != 0 ? "virtual " : "")) + s);
        }
        foreach (var nh in t.GetNestedTypes()) Console.WriteLine("  nested " + TypeDefName(nh));
    }

    class Prov : ISignatureTypeProvider<string, object>
    {
        public string GetArrayType(string e, ArrayShape s) { return e + "[" + new string(',', s.Rank - 1) + "]"; }
        public string GetByReferenceType(string e) { return "ref " + e; }
        public string GetFunctionPointerType(MethodSignature<string> s) { return "fnptr"; }
        public string GetGenericInstantiation(string g, ImmutableArray<string> a) { return g + "<" + string.Join(", ", a) + ">"; }
        public string GetGenericMethodParameter(object c, int i) { return "!!" + i; }
        public string GetGenericTypeParameter(object c, int i) { return "!" + i; }
        public string GetModifiedType(string m, string u, bool r) { return u; }
        public string GetPinnedType(string e) { return e + " pinned"; }
        public string GetPointerType(string e) { return e + "*"; }
        public string GetPrimitiveType(PrimitiveTypeCode c) { return c.ToString(); }
        public string GetSZArrayType(string e) { return e + "[]"; }
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) { return TypeDefName(h); }
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) { return TypeName(h); }
        public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) { return TypeName(h); }
    }
}
