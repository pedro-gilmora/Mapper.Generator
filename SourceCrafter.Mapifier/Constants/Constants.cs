using System;

namespace SourceCrafter.Mapifier.Constants;

public enum IgnoreBind { None, Source, Target, Both }

internal enum EnumerableType { Queue, Stack, Enumerable, ReadOnlyCollection, ReadOnlySpan, Collection, Array, Span, Dictionary }

[Flags] public enum MappingKind { All, Normal, Fill }
