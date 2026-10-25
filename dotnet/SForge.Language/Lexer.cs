namespace SForge.Language;

/// <summary>Token classes for the SForge v0.1 source front end.</summary>
public enum TokenKind { Identifier, Number, Symbol, End }

/// <summary>Human-readable source coordinate; offsets remain UTF-16 indices for .NET slicing.</summary>
public readonly record struct SourceLocation(int Offset, int Line, int Column);

/// <summary>A token with lossless text and start location. Comments and whitespace are retained as trivia by omission from this stream.</summary>
public readonly record struct Token(TokenKind Kind, string Text, SourceSpan Span, SourceLocation Location);

/// <summary>Deterministic lexer with line/column diagnostics and no culture-dependent number handling.</summary>
public static class SForgeLexer
{
    public static (IReadOnlyList<Token> Tokens, IReadOnlyList<Diagnostic> Diagnostics) Lex(string source)
    {
        source ??= string.Empty;
        var tokens = new List<Token>(); var diagnostics = new List<Diagnostic>();
        var index = 0; var line = 1; var column = 1;
        void Advance()
        {
            if (source[index++] == '\n') { line++; column = 1; } else { column++; }
        }
        while (index < source.Length)
        {
            if (char.IsWhiteSpace(source[index])) { Advance(); continue; }
            if (source[index] == '#') { while (index < source.Length && source[index] != '\n') Advance(); continue; }
            var start = index; var location = new SourceLocation(index, line, column);
            if (char.IsLetter(source[index]) || source[index] is '_' or '$')
            {
                Advance(); while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '$' or '-')) Advance();
                tokens.Add(new(TokenKind.Identifier, source[start..index], new(start, index - start), location)); continue;
            }
            if (char.IsDigit(source[index]) || source[index] is '+' or '-' or '.')
            {
                var hasDigit = false; if (source[index] is '+' or '-') Advance();
                while (index < source.Length && char.IsDigit(source[index])) { hasDigit = true; Advance(); }
                if (index < source.Length && source[index] == '.') { Advance(); while (index < source.Length && char.IsDigit(source[index])) { hasDigit = true; Advance(); } }
                if (index < source.Length && source[index] is 'e' or 'E') { Advance(); if (index < source.Length && source[index] is '+' or '-') Advance(); var exponentDigits = 0; while (index < source.Length && char.IsDigit(source[index])) { exponentDigits++; Advance(); } if (exponentDigits == 0) diagnostics.Add(new(new(start, index - start), "exponent requires digits")); }
                if (!hasDigit) { diagnostics.Add(new(new(start, Math.Max(1, index - start)), "number requires digits")); continue; }
                tokens.Add(new(TokenKind.Number, source[start..index], new(start, index - start), location)); continue;
            }
            if ("{}(),:;<>".Contains(source[index])) { Advance(); tokens.Add(new(TokenKind.Symbol, source[start..index], new(start, index - start), location)); continue; }
            diagnostics.Add(new(new(start, 1), $"unexpected character U+{(int)source[index]:X4}")); Advance();
        }
        tokens.Add(new(TokenKind.End, string.Empty, new(source.Length, 0), new(source.Length, line, column)));
        return (tokens, diagnostics);
    }
}
