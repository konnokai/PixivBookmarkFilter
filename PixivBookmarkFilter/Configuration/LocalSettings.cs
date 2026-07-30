using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace PixivBookmarkFilter
{
    internal static class LocalSettings
    {
        public static Dictionary<string, string> LoadTagConvertList()
        {
            return LoadJsonFile("TagConvertList.json", new Dictionary<string, string>());
        }

        public static List<string> LoadIgnoreDownloadTagList()
        {
            return LoadJsonFile("IgnoreDownloadTag.json", new List<string>());
        }

        private static T LoadJsonFile<T>(string fileName, T defaultValue)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                return File.Exists(path)
                    ? JsonConvert.DeserializeObject<T>(File.ReadAllText(path))
                    : defaultValue;
            }
            catch (Exception ex)
            {
                ConsoleOutput.Write(ex.Message, ConsoleColor.DarkRed);
                throw;
            }
        }
    }
}
