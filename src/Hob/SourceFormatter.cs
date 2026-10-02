using System.Text;

internal sealed record SourceFormattingResult(string? Text, IReadOnlyList<Diagnostic> Diagnostics);

internal static class SourceFormatter
{
    private sealed record Item(Token? Token, string? Comment, int Line);

    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal)
    {
        "=", "=>", "->", "+", "-", "*", "/", "==", "!=", "<", "<=", ">", ">="
    };

    private static readonly HashSet<string> TightBefore = new(StringComparer.Ordinal)
    {
        ",", ";", ")", "]", ".", "::", "?"
    };

    private static readonly HashSet<string> TightAfter = new(StringComparer.Ordinal)
    {
        "(", "[", ".", "::"
    };
    private static readonly HashSet<string> KeywordsBeforeGrouping = new(StringComparer.Ordinal)
    {
        "await", "for", "if", "in", "match", "return"
    };

    public static SourceFormattingResult Format(string source, string file)
    {
        var diagnostics = new List<Diagnostic>();
        var tokens = Lexer.Scan(source, file, diagnostics);
        if (diagnostics.Count != 0)
            return new SourceFormattingResult(null, diagnostics);

        var program = new Parser(tokens, file, diagnostics).Parse();
        if (program is null || diagnostics.Count != 0)
        {
            if (diagnostics.Count == 0)
                diagnostics.Add(new Diagnostic("E_SYNTAX", "Could not parse source module", file, new Range(1, 1, 1, 1)));
            return new SourceFormattingResult(null, diagnostics);
        }

        var items = CreateItems(source, tokens);
        var output = FormatItems(items, program);
        var verificationDiagnostics = new List<Diagnostic>();
        var formattedTokens = Lexer.Scan(output, file, verificationDiagnostics);
        if (verificationDiagnostics.Count != 0 || !SameTokenSequence(tokens, formattedTokens))
        {
            diagnostics.Add(new Diagnostic(
                "E_FORMAT",
                "Canonical formatting changed the source token sequence",
                file,
                new Range(1, 1, 1, 1)));
            return new SourceFormattingResult(null, diagnostics);
        }

        var reparsed = new Parser(formattedTokens, file, verificationDiagnostics).Parse();
        if (reparsed is null || verificationDiagnostics.Count != 0)
        {
            diagnostics.Add(new Diagnostic(
                "E_FORMAT",
                "Canonical formatting did not produce parseable source",
                file,
                new Range(1, 1, 1, 1)));
            return new SourceFormattingResult(null, diagnostics);
        }

        return new SourceFormattingResult(output, diagnostics);
    }

    private static List<Item> CreateItems(string source, IReadOnlyList<Token> tokens)
    {
        var lineStarts = GetLineStarts(source);
        var items = new List<Item>(tokens.Count);
        var cursor = 0;
        foreach (var token in tokens)
        {
            if (token.Kind == "eof")
                break;

            var tokenStart = GetOffset(token, lineStarts);
            AddComments(source, cursor, tokenStart, lineStarts, items);
            items.Add(new Item(token, null, token.Line));
            cursor = tokenStart + token.Text.Length;
        }

        AddComments(source, cursor, source.Length, lineStarts, items);
        return items;
    }

    private static void AddComments(
        string source,
        int start,
        int end,
        IReadOnlyList<int> lineStarts,
        List<Item> items)
    {
        for (var index = start; index < end;)
        {
            if (source[index] != '/' || index + 1 >= end || source[index + 1] != '/')
            {
                index++;
                continue;
            }

            var commentStart = index;
            while (index < end && source[index] is not ('\r' or '\n'))
                index++;
            items.Add(new Item(null, source[commentStart..index], GetLine(commentStart, lineStarts)));
        }
    }

    private static int[] GetLineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\r')
            {
                if (index + 1 < source.Length && source[index + 1] == '\n')
                    index++;
                starts.Add(index + 1);
            }
            else if (source[index] == '\n')
                starts.Add(index + 1);
        }

        return starts.ToArray();
    }

    private static int GetOffset(Token token, IReadOnlyList<int> lineStarts) =>
        lineStarts[token.Line - 1] + token.Column - 1;

    private static int GetLine(int offset, IReadOnlyList<int> lineStarts)
    {
        var index = 0;
        while (index + 1 < lineStarts.Count && lineStarts[index + 1] <= offset)
            index++;
        return index + 1;
    }

    private static string FormatItems(IReadOnlyList<Item> items, ParsedProgram program)
    {
        var output = new StringBuilder();
        var nextTokens = new Token?[items.Count];
        var genericAngles = FindGenericAngles(items);
        var (effectBraces, commentedEffectBraces) = FindEffectBraces(items, program);
        var unaryMinuses = FindUnaryMinuses(items);
        Token? nextToken = null;
        for (var index = items.Count - 1; index >= 0; index--)
        {
            nextTokens[index] = nextToken;
            if (items[index].Token is not null)
                nextToken = items[index].Token;
        }

        var indent = 0;
        var parenDepth = 0;
        var bracketDepth = 0;
        var angleDepth = 0;
        var pendingLineBreaks = 0;
        var lineHasContent = false;
        var commentSincePreviousToken = false;
        var insideCommentedEffects = false;
        Token? previousToken = null;
        var previousSourceLine = 0;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Token is null)
            {
                if (lineHasContent && item.Line == previousSourceLine)
                {
                    output.Append(' ');
                    output.Append(item.Comment);
                    pendingLineBreaks = Math.Max(pendingLineBreaks, 1);
                }
                else
                {
                    BreakLines(output, ref lineHasContent, pendingLineBreaks);
                    pendingLineBreaks = 0;
                    AppendIndent(output, indent, ref lineHasContent);
                    output.Append(item.Comment);
                    lineHasContent = true;
                    pendingLineBreaks = 1;
                }

                previousSourceLine = item.Line;
                commentSincePreviousToken = true;
                continue;
            }

            var token = item.Token;
            var lookahead = nextTokens[index];
            var isClosingBrace = token.Text == "}";
            var isEffectBrace = effectBraces.Contains(index);
            var isCommentedEffectBrace = commentedEffectBraces.Contains(index);

            if (isClosingBrace && isCommentedEffectBrace && lineHasContent)
                pendingLineBreaks = Math.Max(pendingLineBreaks, 1);
            else if (isClosingBrace && !isEffectBrace && previousToken?.Text != "{" &&
                     lineHasContent && pendingLineBreaks == 0)
                pendingLineBreaks = 1;

            if (!commentSincePreviousToken && previousToken?.Text == "}" &&
                (token.Text == "else" || IsContinuationAfterBrace(token.Text)))
                pendingLineBreaks = 0;

            BreakLines(output, ref lineHasContent, pendingLineBreaks);
            pendingLineBreaks = 0;

            if (isClosingBrace && (!isEffectBrace || isCommentedEffectBrace))
                indent = Math.Max(0, indent - 1);

            if (lineHasContent && previousToken is not null &&
                NeedsSpace(
                    previousToken,
                    token,
                    previousToken.Text is "<" or ">" && genericAngles.Contains(FindPreviousTokenIndex(items, index - 1)),
                    token.Text is "<" or ">" && genericAngles.Contains(index),
                    unaryMinuses.Contains(FindPreviousTokenIndex(items, index - 1)),
                    unaryMinuses.Contains(index)))
                output.Append(' ');

            AppendIndent(output, indent, ref lineHasContent);
            if (token.Text == "{" && index + 1 < items.Count && items[index + 1].Token?.Text == "}")
            {
                output.Append("{}");
                lineHasContent = true;
                previousToken = items[index + 1].Token;
                previousSourceLine = items[index + 1].Line;
                commentSincePreviousToken = false;
                pendingLineBreaks = isEffectBrace || nextTokens[index + 1] is { } afterEmptyBlock &&
                    IsContinuationAfterBrace(afterEmptyBlock.Text)
                    ? 0
                    : indent == 0 ? 2 : 1;
                index++;
                continue;
            }

            output.Append(token.Text);
            lineHasContent = true;

            switch (token.Text)
            {
                case "{":
                    if (isEffectBrace)
                    {
                        if (isCommentedEffectBrace)
                        {
                            indent++;
                            insideCommentedEffects = true;
                            pendingLineBreaks = 1;
                        }
                        else
                            pendingLineBreaks = 0;
                    }
                    else
                    {
                        indent++;
                        pendingLineBreaks = 1;
                    }
                    break;
                case "}":
                    if (isEffectBrace)
                    {
                        if (isCommentedEffectBrace)
                        {
                            insideCommentedEffects = false;
                        }
                        pendingLineBreaks = 0;
                    }
                    else
                        pendingLineBreaks = lookahead is not null && IsContinuationAfterBrace(lookahead.Text)
                            ? 0
                            : indent == 0 ? 2 : 1;
                    break;
                case ";":
                    pendingLineBreaks = indent == 0 ? 2 : 1;
                    break;
                case ",":
                    pendingLineBreaks = insideCommentedEffects ||
                        indent > 0 && parenDepth == 0 && bracketDepth == 0 && angleDepth == 0
                        ? 1
                        : 0;
                    break;
            }

            if (token.Text == "(") parenDepth++;
            else if (token.Text == ")") parenDepth = Math.Max(0, parenDepth - 1);
            else if (token.Text == "[") bracketDepth++;
            else if (token.Text == "]") bracketDepth = Math.Max(0, bracketDepth - 1);
            else if (token.Text == "<" && genericAngles.Contains(index)) angleDepth++;
            else if (token.Text == ">" && genericAngles.Contains(index) && angleDepth > 0) angleDepth--;

            previousToken = token;
            previousSourceLine = item.Line;
            commentSincePreviousToken = false;
        }

        BreakLines(output, ref lineHasContent, 1);
        while (output.Length != 0 && output[^1] == '\n')
            output.Length--;
        output.Append('\n');
        return output.ToString();
    }

    private static void BreakLines(StringBuilder output, ref bool lineHasContent, int count)
    {
        if (count <= 0 || output.Length == 0)
            return;

        if (lineHasContent)
        {
            output.Append('\n');
            lineHasContent = false;
            count--;
        }

        while (count > 0)
        {
            output.Append('\n');
            count--;
        }
    }

    private static void AppendIndent(StringBuilder output, int indent, ref bool lineHasContent)
    {
        if (lineHasContent)
            return;

        output.Append(' ', indent * 4);
    }

    private static bool NeedsSpace(
        Token previous,
        Token current,
        bool previousGenericAngle,
        bool currentGenericAngle,
        bool previousUnaryMinus,
        bool currentUnaryMinus)
    {
        if (current.Text is "(" or "[")
            return KeywordsBeforeGrouping.Contains(previous.Text);
        if (TightBefore.Contains(current.Text))
            return false;
        if (TightAfter.Contains(previous.Text))
            return false;
        if (current.Text == "{")
            return true;
        if (previous.Text == "}" && current.Text == "else")
            return true;
        if (current.Text == ":")
            return false;
        if (previous.Text == ",")
            return true;
        if (previous.Text == ">" && previousGenericAngle && IsWordLike(current))
            return true;
        if (previous.Text == ":")
            return true;
        if (current.Text is "<" or ">" && !currentGenericAngle)
            return true;
        if (previous.Text is "<" or ">" && !previousGenericAngle)
            return true;
        if (current.Text == "-" && currentUnaryMinus)
            return IsWordLike(previous) || previous.Text is ")" or "]" or "?" or "}" ||
                Operators.Contains(previous.Text) && !(previous.Text == "-" && previousUnaryMinus);
        if (Operators.Contains(previous.Text) && !(previous.Text == "-" && previousUnaryMinus) &&
            !(previous.Text is "<" or ">" && previousGenericAngle))
            return true;
        if (Operators.Contains(current.Text) && !(current.Text == "-" && currentUnaryMinus) &&
            !(current.Text is "<" or ">" && currentGenericAngle))
            return true;
        return IsWordLike(previous) && IsWordLike(current);
    }

    private static bool IsWordLike(Token token) =>
        token.Kind is "id" or "number" or "text";

    private static bool IsContinuationAfterBrace(string token) =>
        token is "else" or "{" or "." or "(" or ")" or "[" or "]" or "," or ";" or "?";

    private static HashSet<int> FindUnaryMinuses(IReadOnlyList<Item> items)
    {
        var minuses = new HashSet<int>();
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Token?.Text != "-")
                continue;

            var previousIndex = FindPreviousTokenIndex(items, index - 1);
            var previous = previousIndex >= 0 ? items[previousIndex].Token : null;
            if (previous is null || previous.Text is "(" or "[" or "{" or "," or ";" or "=" or "=>" or ":" or
                "+" or "-" or "*" or "/" or "==" or "!=" or "<" or "<=" or ">" or ">=" or
                "await" or "if" or "in" or "match" or "return" or "as")
                minuses.Add(index);
        }

        return minuses;
    }

    private static HashSet<int> FindGenericAngles(IReadOnlyList<Item> items)
    {
        var angles = new HashSet<int>();
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Token?.Text != "<")
                continue;

            var previousIndex = FindPreviousTokenIndex(items, index - 1);
            if (previousIndex < 0 || items[previousIndex].Token is not { } previous ||
                !IsWordLike(previous) && previous.Text is not (">" or "::"))
                continue;

            var nesting = 1;
            for (var lookahead = index + 1; lookahead < items.Count; lookahead++)
            {
                if (items[lookahead].Token is not { } candidate)
                    continue;

                if (candidate.Text == "<")
                    nesting++;
                else if (candidate.Text == ">" && --nesting == 0)
                {
                    var following = lookahead + 1;
                    while (following < items.Count && items[following].Token is null)
                        following++;
                    var hasTypeContinuation = following >= items.Count || items[following].Token is not { } after ||
                        after.Text is "(" or "{" or "." or "?" or ")" or "," or ";" or "=" or "effects" or ">";
                    if (hasTypeContinuation)
                    {
                        angles.Add(index);
                        angles.Add(lookahead);
                    }
                    break;
                }
            }
        }

        return angles;
    }

    private static (HashSet<int> Braces, HashSet<int> CommentedBraces) FindEffectBraces(
        IReadOnlyList<Item> items,
        ParsedProgram program)
    {
        var braces = new HashSet<int>();
        var commentedBraces = new HashSet<int>();
        var declarations = program.Functions.Select(function => function.At)
            .Concat(program.Traits.SelectMany(trait => trait.Methods.Select(method => method.At)));
        foreach (var declaration in declarations)
        {
            var declarationIndex = -1;
            for (var index = 0; index < items.Count; index++)
            {
                if (items[index].Token?.Equals(declaration) == true)
                {
                    declarationIndex = index;
                    break;
                }
            }

            if (declarationIndex < 0)
                continue;

            var inReturnType = false;
            for (var candidate = FindNextTokenIndex(items, declarationIndex + 1);
                 candidate >= 0;
                 candidate = FindNextTokenIndex(items, candidate + 1))
            {
                if (items[candidate].Token?.Text == "->")
                {
                    inReturnType = true;
                    continue;
                }

                if (!inReturnType || items[candidate].Token?.Text != "effects")
                    continue;

                var opening = FindNextTokenIndex(items, candidate + 1);
                if (opening < 0 || items[opening].Token?.Text != "{")
                    continue;

                var depth = 1;
                for (var closing = opening + 1; closing < items.Count; closing++)
                {
                    if (items[closing].Token?.Text == "{")
                        depth++;
                    else if (items[closing].Token?.Text == "}" && --depth == 0)
                    {
                        braces.Add(opening);
                        braces.Add(closing);
                        if (items.Skip(opening + 1).Take(closing - opening - 1).Any(item => item.Comment is not null))
                        {
                            commentedBraces.Add(opening);
                            commentedBraces.Add(closing);
                        }
                        break;
                    }
                }

                if (braces.Contains(opening))
                    break;
            }
        }

        return (braces, commentedBraces);
    }

    private static int FindNextTokenIndex(IReadOnlyList<Item> items, int index)
    {
        while (index < items.Count && items[index].Token is null)
            index++;
        return index < items.Count ? index : -1;
    }

    private static int FindPreviousTokenIndex(IReadOnlyList<Item> items, int index)
    {
        while (index >= 0 && items[index].Token is null)
            index--;
        return index;
    }

    private static bool SameTokenSequence(IReadOnlyList<Token> left, IReadOnlyList<Token> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Kind, right[index].Kind, StringComparison.Ordinal) ||
                !string.Equals(left[index].Text, right[index].Text, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
