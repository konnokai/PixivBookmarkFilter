using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PixivBookmarkFilter
{
    public static class StringEncrypt
    {
        static string UserDataPath { get { return AppDomain.CurrentDomain.BaseDirectory + "UserData.dat"; } }

        /// <summary>
        /// 字串加密(非對稱式)
        /// </summary>
        /// <param name="Source">加密前字串</param>
        /// <param name="CryptoKey">加密金鑰</param>
        /// <returns>加密後字串</returns>
        public static string AesEncryptBase64(string SourceStr, string CryptoKey)
        {
            string encrypt = "";
            try
            {
                using (Aes aes = Aes.Create())
                using (MD5 md5 = MD5.Create())
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] key = sha256.ComputeHash(Encoding.UTF8.GetBytes(CryptoKey));
                    byte[] iv = md5.ComputeHash(Encoding.UTF8.GetBytes(CryptoKey));
                    aes.Key = key;
                    aes.IV = iv;

                    byte[] dataByteArray = Encoding.UTF8.GetBytes(SourceStr);
                    using (MemoryStream ms = new MemoryStream())
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(dataByteArray, 0, dataByteArray.Length);
                        cs.FlushFinalBlock();
                        encrypt = Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            return encrypt;
        }

        /// <summary>
        /// 字串解密(非對稱式)
        /// </summary>
        /// <param name="Source">解密前字串</param>
        /// <param name="CryptoKey">解密金鑰</param>
        /// <returns>解密後字串</returns>
        public static string AesDecryptBase64(string SourceStr, string CryptoKey)
        {
            string decrypt = "";
            try
            {
                using (Aes aes = Aes.Create())
                using (MD5 md5 = MD5.Create())
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] key = sha256.ComputeHash(Encoding.UTF8.GetBytes(CryptoKey));
                    byte[] iv = md5.ComputeHash(Encoding.UTF8.GetBytes(CryptoKey));
                    aes.Key = key;
                    aes.IV = iv;

                    byte[] dataByteArray = Convert.FromBase64String(SourceStr);
                    using (MemoryStream ms = new MemoryStream())
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(dataByteArray, 0, dataByteArray.Length);
                        cs.FlushFinalBlock();
                        decrypt = Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            return decrypt;
        }

        public static void SaveUserCookies(CookieContainer source, Uri url)
        {
            string text = "";
            foreach (Cookie item in source.GetCookies(url))
            {
                if (text != "") text += ";";
                text += item.Name + "=" + item.Value;
            }

            //File.WriteAllText(UserDataPath, AesEncryptBase64(text, "Create_by_jun"));
            File.WriteAllText(UserDataPath, text);
        }

        public static CookieContainer LoadUserCookies()
        {
            if (!File.Exists(UserDataPath)) return null;

            CookieContainer cookieContainer = new CookieContainer();
            //string[] text = AesDecryptBase64(File.ReadAllText(UserDataPath), "Create_by_jun").Split(new char[] { '&' });
            string[] text = File.ReadAllText(UserDataPath).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in text)
            {
                string[] temp = item.Split(new char[] { '=' });
                cookieContainer.Add(new Cookie(temp[0], temp[1], "/", ".pixiv.net"));
            }
            return cookieContainer;
        }
    }
}
