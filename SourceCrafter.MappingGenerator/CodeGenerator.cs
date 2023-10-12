using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceCrafter.Mapping.Constants;
using System.Linq;
using System.Collections.Generic;

namespace SourceCrafter.Mapping;

internal sealed class CodeGenerator
{

    static int lastId = 0;
    static int NextId() => ++lastId;

    internal MappingBuilder AddMember = default!;
    internal MappingBuilder? PreInitialize;
    internal string? Comma;

    internal int id = NextId();
    private sealed class NestingInfo(int nCount, int count) { internal int _nCount = nCount; internal int _count = count; };
    private readonly Dictionary<int, NestingInfo> roots = new();
    readonly int threshold = 2;

    internal bool CanBuild;

    public static CodeGenerator operator +(CodeGenerator a, CodeGenerator b)
    {
        a.PreInitialize += b.PreInitialize;
        a.AddMember += b.AddMember;
        return a;
    }

    internal bool CanCreateNullableMapper(int rootId)
    {
        if (!roots.TryGetValue(rootId, out var stack))
        {
            roots[rootId] = new(1, 0);
            return true;
        }
        return ++stack._nCount < threshold;
    }

    internal bool CanCreateMapper(int rootId)
    {
        if (!roots.TryGetValue(rootId, out var stack))
        {
            roots[rootId] = new(0, 1);
            return true;
        }
        return ++stack._count < threshold;
    }
}
