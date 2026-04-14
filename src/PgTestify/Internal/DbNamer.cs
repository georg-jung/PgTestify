using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PgTestify.Internal;

internal static class DbNamer
{
    private const int MaxIdentifierLength = 63;

    /// <summary>
    /// Derives a default template name from the provided assembly and a label
    /// (typically the DbContext type name). Format: pgtestify_{assembly16}_{label}
    /// </summary>
    internal static string DefaultTemplateName(Assembly assembly, string label)
    {
        var assemblyName = assembly.GetName().Name ?? "unknown";
        // Limit assembly prefix to 16 chars to leave room for the context name
        var assemblyPrefix = Sanitize(assemblyName)[..Math.Min(16, Sanitize(assemblyName).Length)];
        var rawName = $"pgtestify_{assemblyPrefix}_{Sanitize(label)}";
        return TruncateIdentifier(rawName);
    }

    /// <summary>
    /// Returns a raw template name given a user-supplied override.
    /// Sanitizes and truncates to 63 bytes.
    /// </summary>
    internal static string SanitizeTemplateName(string name) =>
        TruncateIdentifier(Sanitize(name));

    /// <summary>
    /// Generates the Nth pool database name: {template}_{n}
    /// </summary>
    internal static string PoolDatabaseName(string templateName, int index) =>
        TruncateIdentifier($"{templateName}_{index}");

    private static string Sanitize(string name)
    {
        // Lowercase, replace anything non-alphanumeric-or-underscore with _
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }
        return sb.ToString().Trim('_');
    }

    internal static string TruncateIdentifier(string name)
    {
        if (name.Length <= MaxIdentifierLength)
            return name;

        // Keep first (max-9) chars + underscore + 8-char hex hash
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant()[..8];
        return $"{name[..(MaxIdentifierLength - 9)]}_{hash}";
    }
}
