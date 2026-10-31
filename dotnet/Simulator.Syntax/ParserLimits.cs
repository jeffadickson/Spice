namespace Simulator.Syntax;

/// <summary>Explicit allocation bounds for generated recognition; these are not language validity rules.</summary>
public sealed class ParserLimits
{
    /// <summary>The conservative interactive profile retained for existing callers.</summary>
    public static ParserLimits Default { get; } = new();
    /// <summary>Maximum UTF-16 characters admitted before tokenization.</summary>
    public int MaxInputCharacters { get; }
    /// <summary>Maximum generated tokens, including hidden trivia and EOF.</summary>
    public int MaxTokens { get; }
    /// <summary>Try ANTLR SLL prediction first, falling back to full LL on failure.</summary>
    public bool UseSllFirst { get; }
    /// <summary>Creates a bounded profile. Larger sample qualification must opt in explicitly.</summary>
    public ParserLimits(int maxInputCharacters=1_048_576,int maxTokens=100_000,bool useSllFirst=true)
    {
        if(maxInputCharacters is <1 or >67_108_864)throw new ArgumentOutOfRangeException(nameof(maxInputCharacters));
        if(maxTokens is <1 or >4_000_000)throw new ArgumentOutOfRangeException(nameof(maxTokens));
        MaxInputCharacters=maxInputCharacters;MaxTokens=maxTokens;UseSllFirst=useSllFirst;
    }
}
