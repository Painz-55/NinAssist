using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace NinOnlineAutoAssist.Diagnostics;

public sealed class FieldMetadata : ISignatureTypeProvider<string, object?>
{
    private static string Full(MetadataReader r, TypeDefinitionHandle h) { var t = r.GetTypeDefinition(h); return r.GetString(t.Namespace) + "." + r.GetString(t.Name); }
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => Full(r, h);
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) { var t = r.GetTypeReference(h); return r.GetString(t.Namespace) + "." + r.GetString(t.Name); }
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
    public string GetPrimitiveType(PrimitiveTypeCode c) => "System." + c;
    public string GetArrayType(string t, ArrayShape s) => t + "[" + new string(',', s.Rank - 1) + "]";
    public string GetSZArrayType(string t) => t + "[]";
    public string GetByReferenceType(string t) => t + "&";
    public string GetPointerType(string t) => t + "*";
    public string GetPinnedType(string t) => t;
    public string GetModifiedType(string m, string t, bool required) => t;
    public string GetGenericInstantiation(string t, ImmutableArray<string> a) => t + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object? c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? c, int i) => "!" + i;
    public string GetFunctionPointerType(MethodSignature<string> s) => "function";

    public static int Run(string path, bool enums = false)
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader(); var provider = new FieldMetadata();
        if (enums)
        {
            var values = md.TypeDefinitions.Select(h => md.GetTypeDefinition(h))
                .Where(t => new[] { "Directions", "Vitals", "TileAttributes", "VillageType" }.Contains(md.GetString(t.Name)))
                .Select(t => new { Name = md.GetString(t.Name), Values = t.GetFields().Select(h => md.GetFieldDefinition(h))
                    .Where(f => !f.GetDefaultValue().IsNil).Select(f => new { Name = md.GetString(f.Name),
                        Hex = Convert.ToHexString(md.GetBlobBytes(md.GetConstant(f.GetDefaultValue()).Value)) }).ToArray() }).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(values)); return 0;
        }
        var types = md.TypeDefinitions.Select(h => new { Handle = h, Type = md.GetTypeDefinition(h) })
            .Where(t => t.Type.GetFields().Count >= 80)
            .Select(t => new { Name = Full(md, t.Handle), Fields = t.Type.GetFields().Select(h => md.GetFieldDefinition(h))
                .Select(f => new { Name = md.GetString(f.Name), Type = f.DecodeSignature(provider, (object?)null) }).ToArray() }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(types)); return 0;
    }
}
