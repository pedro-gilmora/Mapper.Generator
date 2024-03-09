using Microsoft.CodeAnalysis;
using SourceCrafter.Binding;
using System;
using System.Text;

namespace SourceCrafter.MappingGenerator.Builders
{
    internal record struct Builder(MappingSet Set, StringBuilder Code, TypeData Left, TypeData Right)
    {
        Action Build { get; set; }
        Action BuildReverse { get; set; }
    }
    internal interface IMappingBuilder
    {
        void Build();
        void BuildReverse();
    }
}