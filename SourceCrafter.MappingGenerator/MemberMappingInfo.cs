using Microsoft.CodeAnalysis;

namespace SourceCrafter.Mapping;

internal record struct MemberMappingInfo(
    Compilation Compilation,
    ISymbol InMember,
    string InMemberName,
    ITypeSymbol InMemberType,
    string InTypeName,
    string InNonGenericTypeName,
    ISymbol OutMember,
    string OutMemberName,
    ITypeSymbol OutMemberType,
    string OutTypeName,
    string OutNonGenericTypeName
)
{

    internal bool IsInNullable { get; } = InMemberType.IsNullable();
    internal bool IsOutNullable { get; } = OutMemberType.IsNullable();
    internal readonly MemberMappingInfo Reverse() => new(Compilation,
                                                         OutMember,
                                                         OutMemberName,
                                                         OutMemberType,
                                                         OutTypeName,
                                                         OutNonGenericTypeName,
                                                         InMember,
                                                         InMemberName,
                                                         InMemberType,
                                                         InTypeName,
                                                         InNonGenericTypeName);
}
