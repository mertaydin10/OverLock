using System.Globalization;

namespace Overlock.Core;

internal static class TextAtoms
{
    public static List<string> Split(string text)
    {
        var atoms = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return atoms;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            atoms.Add((string)enumerator.Current!);
        }

        return atoms;
    }
}
