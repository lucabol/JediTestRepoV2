using System;

namespace common;

public abstract record PolicyContentFormat
{
    public sealed record RawXml : PolicyContentFormat;
    public sealed record Xml : PolicyContentFormat;

    public string ToFormatString() => this switch
    {
        RawXml => "rawxml",
        Xml => "xml",
        _ => throw new NotSupportedException($"Policy content format '{GetType().Name}' is not supported.")
    };
}
