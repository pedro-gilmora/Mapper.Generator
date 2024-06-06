namespace SourceCrafter.Bindings;

internal sealed class Indent(char space = ' ', int count = 4)
{
    private string spaces = new(space, count);
    private readonly char space = space;
    private readonly int factor = count;

    public static Indent operator ++(Indent from)
    {
        from.spaces += "    ";
        return from;
    }
    public static Indent operator +(Indent from, int i)
    {
        from.spaces += new string(from.space, from.factor * i);
        return from;
    }
    public static Indent operator --(Indent from)
    {
        if (from.spaces.Length > from.factor)
            from.spaces = from.spaces[..^from.factor];
        return from;
    }
    public static Indent operator -(Indent from, int i)
    {
        i *= from.factor;
        if (from.spaces.Length > i)
            from.spaces = from.spaces[..^i];
        return from;
    }

    public override string ToString() => spaces;
}
