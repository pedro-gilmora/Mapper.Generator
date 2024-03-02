using SourceCrafter.Binding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SourceCrafter.MappingGenerator.Builders
{
    internal struct TypeBuilder(Builder builder)  : IMappingBuilder
    {

        public void Build()
        {
            comma = null;

            code.AppendFormat(@"new {0}
{1}    {{", right.Type.FullName.TrimEnd('?'), indent);
            indent++;
            builder. ($"{path}{right.Id}+", leftPart, rightPart, indent, ref comma);
            indent--;

            builder.Code.AppendFormat(@"
{0}    }}", indent);
        }

        public void BuildReverse()
        {
            throw new NotImplementedException();
        }
    }
}
