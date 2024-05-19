using Microsoft.CodeAnalysis;

using System;
using System.Collections.Immutable;

namespace SourceCrafter.Bindings;

internal sealed record Member(
    int Id,
    string Name,
    bool IsNullable,
    bool IsReadable = true,
    bool IsWritable = true,
    ImmutableArray<AttributeData> Attributes = default,
    bool IsInit = false,
    bool CanMap = true,
    bool IsProperty = false)
{
    internal string?
        DefaultBang,
        Bang;

    internal short MaxDepth = 1;

    internal TypeData Type = null!;

    internal TypeData? OwningType;

    internal bool MaxDepthReached(string s)
    {
        var t = MaxDepth;

        if (t == 0)
            return true;

        string n = $"+{Id}+", ss;

        for (int nL = n.Length, start = Math.Abs(s.Length - n.Length), end = s.Length; start > -1 && t > -1 && end - start >= nL;)
        {
            if ((ss = s[start..end]) == n)
            {
                if (t-- == 0) return true;
                end = start + 1;
                start = end - nL;
            }
            else if (ss[0] == '+' && ss[^1] == '+')
            {
                end = start + 1;
                start = end - nL;
            }
            else
            {
                end--;
                start--;
            }
        }
        return false;
    }
    public override string ToString() => $"({(Type?.ToString() ?? "?")}) {Name}";
}
