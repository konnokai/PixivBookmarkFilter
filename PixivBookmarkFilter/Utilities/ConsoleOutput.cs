using System;

namespace PixivBookmarkFilter
{
    internal static class ConsoleOutput
    {
        public static void Write(string text, ConsoleColor color = ConsoleColor.Gray, bool newLine = true)
        {
            Console.ForegroundColor = color;
            if (newLine) Console.WriteLine(text);
            else Console.Write(text);
            Console.ForegroundColor = ConsoleColor.Gray;
        }
    }
}
