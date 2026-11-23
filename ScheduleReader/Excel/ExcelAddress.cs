using System.Globalization;

namespace ScheduleReader.Excel;

public static class ExcelAddress
{
    public static string From(int row, int column)
    {
        if (row < 1 || column < 1) return "";
        Span<char> letters = stackalloc char[4];
        var i = letters.Length;
        var n = column;
        while (n > 0)
        {
            n--;
            letters[--i] = (char)('A' + n % 26);
            n /= 26;
        }

        return string.Concat(new string(letters[i..]), row.ToString(CultureInfo.InvariantCulture));
    }
}
