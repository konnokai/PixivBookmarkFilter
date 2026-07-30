using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PixivBookmarkFilter
{
    internal static class Program
    {
        private static async Task Main()
        {
            System.Console.OutputEncoding = Encoding.UTF8;
            System.Console.InputEncoding = Encoding.Unicode;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;

            using var application = new BookmarkFilterApplication();
            await application.RunAsync();
        }
    }
}
