using System.Text;

namespace S1Interop.Core;

internal static class CSharpSourceScanner
{
    private enum SourceScanState
    {
        Code,
        LineComment,
        BlockComment,
        RegularString,
        VerbatimString,
        CharLiteral
    }

    public static IEnumerable<CSharpSourceSegment> EnumerateCodeSegments(string source)
    {
        SourceScanState state = SourceScanState.Code;
        int segmentStart = 0;
        int index = 0;
        while (index < source.Length)
        {
            char ch = source[index];
            char next = index + 1 < source.Length ? source[index + 1] : '\0';

            switch (state)
            {
                case SourceScanState.Code:
                    if (ch == '/' && next == '/')
                    {
                        yield return new CSharpSourceSegment(source[segmentStart..index], segmentStart);
                        state = SourceScanState.LineComment;
                        index++;
                    }
                    else if (ch == '/' && next == '*')
                    {
                        yield return new CSharpSourceSegment(source[segmentStart..index], segmentStart);
                        state = SourceScanState.BlockComment;
                        index++;
                    }
                    else if (ch == '"')
                    {
                        yield return new CSharpSourceSegment(source[segmentStart..index], segmentStart);
                        state = IsVerbatimStringStart(source, index)
                            ? SourceScanState.VerbatimString
                            : SourceScanState.RegularString;
                    }
                    else if (ch == '\'')
                    {
                        yield return new CSharpSourceSegment(source[segmentStart..index], segmentStart);
                        state = SourceScanState.CharLiteral;
                    }

                    break;

                case SourceScanState.LineComment:
                    if (ch is '\r' or '\n')
                    {
                        state = SourceScanState.Code;
                        segmentStart = index;
                    }

                    break;

                case SourceScanState.BlockComment:
                    if (ch == '*' && next == '/')
                    {
                        index++;
                        state = SourceScanState.Code;
                        segmentStart = index + 1;
                    }

                    break;

                case SourceScanState.RegularString:
                    if (ch == '\\' && next != '\0')
                    {
                        index++;
                    }
                    else if (ch == '"')
                    {
                        state = SourceScanState.Code;
                        segmentStart = index + 1;
                    }

                    break;

                case SourceScanState.VerbatimString:
                    if (ch == '"' && next == '"')
                    {
                        index++;
                    }
                    else if (ch == '"')
                    {
                        state = SourceScanState.Code;
                        segmentStart = index + 1;
                    }

                    break;

                case SourceScanState.CharLiteral:
                    if (ch == '\\' && next != '\0')
                    {
                        index++;
                    }
                    else if (ch == '\'')
                    {
                        state = SourceScanState.Code;
                        segmentStart = index + 1;
                    }

                    break;
            }

            index++;
        }

        if (state == SourceScanState.Code && segmentStart < source.Length)
        {
            yield return new CSharpSourceSegment(source[segmentStart..], segmentStart);
        }
    }

    public static IEnumerable<string> EnumerateStringLiteralValues(string source)
    {
        SourceScanState state = SourceScanState.Code;
        var literal = new StringBuilder();
        int index = 0;
        while (index < source.Length)
        {
            char ch = source[index];
            char next = index + 1 < source.Length ? source[index + 1] : '\0';

            switch (state)
            {
                case SourceScanState.Code:
                    if (ch == '/' && next == '/')
                    {
                        state = SourceScanState.LineComment;
                        index++;
                    }
                    else if (ch == '/' && next == '*')
                    {
                        state = SourceScanState.BlockComment;
                        index++;
                    }
                    else if (ch == '"')
                    {
                        literal.Clear();
                        state = IsVerbatimStringStart(source, index)
                            ? SourceScanState.VerbatimString
                            : SourceScanState.RegularString;
                    }
                    else if (ch == '\'')
                    {
                        state = SourceScanState.CharLiteral;
                    }

                    break;

                case SourceScanState.LineComment:
                    if (ch is '\r' or '\n')
                    {
                        state = SourceScanState.Code;
                    }

                    break;

                case SourceScanState.BlockComment:
                    if (ch == '*' && next == '/')
                    {
                        index++;
                        state = SourceScanState.Code;
                    }

                    break;

                case SourceScanState.RegularString:
                    if (ch == '\\' && next != '\0')
                    {
                        literal.Append(next);
                        index++;
                    }
                    else if (ch == '"')
                    {
                        yield return literal.ToString();
                        state = SourceScanState.Code;
                    }
                    else
                    {
                        literal.Append(ch);
                    }

                    break;

                case SourceScanState.VerbatimString:
                    if (ch == '"' && next == '"')
                    {
                        literal.Append('"');
                        index++;
                    }
                    else if (ch == '"')
                    {
                        yield return literal.ToString();
                        state = SourceScanState.Code;
                    }
                    else
                    {
                        literal.Append(ch);
                    }

                    break;

                case SourceScanState.CharLiteral:
                    if (ch == '\\' && next != '\0')
                    {
                        index++;
                    }
                    else if (ch == '\'')
                    {
                        state = SourceScanState.Code;
                    }

                    break;
            }

            index++;
        }
    }

    private static bool IsVerbatimStringStart(string source, int quoteIndex)
    {
        int previous = quoteIndex - 1;
        if (previous < 0)
        {
            return false;
        }

        if (source[previous] == '@')
        {
            return true;
        }

        return source[previous] == '$' && previous - 1 >= 0 && source[previous - 1] == '@';
    }
}

internal sealed record CSharpSourceSegment(string Text, int StartIndex);
