namespace Simulator.Syntax;

// A captured report is caller-owned data, never a filesystem write. Keep the
// same leaf-name contract at recognition and execution boundaries so forged
// command metadata cannot introduce paths or shell syntax.
internal static class ControlReportTarget
{
    internal static bool IsSafe(string target)=>target.Length is >0 and <=64
        && (char.IsAsciiLetterOrDigit(target[0]) || target[0]=='_')
        && target[^1]!='.' && !target.Contains("..",StringComparison.Ordinal)
        && target.All(c=>char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
}
