namespace SourceCrafter.Mapping;

internal sealed class Indent
{
    private string spaces = "    ";
    public static Indent operator ++(Indent from)
    {
        from.spaces += "    ";
        return from;
    }
    public static Indent operator --(Indent from)
    {
        if (from.spaces.Length > 1)
            from.spaces = from.spaces[..^4];
        return from;
    }

    public override string ToString() => spaces;
}
