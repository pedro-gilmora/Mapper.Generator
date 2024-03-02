using System;


namespace SourceCrafter.Bindings.Helpers
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class MaxAttribute<TIn, TOut> : Attribute;
}