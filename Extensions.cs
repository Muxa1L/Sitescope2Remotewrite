using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MuxLibrary
{
    public static class Extensions
    {
        public static string Path(this XElement element)
        {
            XElement tmp = element;
            string path = string.Empty;
            while (tmp.Attribute("name") != null)
            {
                path = "/" + tmp.Attribute("name").Value + path;
                tmp = tmp.Parent;
            }
            return path;
        }

        public static long ToUnixTimeStamp(this DateTime dt){
            return (long)(dt.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        }
        public static DateTime UnixTimeStampToDateTime(this long unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }
        public static DateTime JavaTimeStampToDateTime(this long javaTimeStamp)
        {
            // Java timestamp is milliseconds past epoch
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddMilliseconds(javaTimeStamp).ToLocalTime();
            return dtDateTime;
        }
        public static byte[] Compress(this byte[] raw)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(memory,
                    CompressionMode.Compress, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return memory.ToArray();
            }
        }
        
        public static bool IsGZip(this byte[] arr)
        {
            return arr.Length >= 2 && arr[0] == 31 && arr[1] == 139;
        }
        public static byte[] Decompress(this byte[] gzip)
        {
            using (GZipStream stream = new GZipStream(new MemoryStream(gzip),
                CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            memory.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return memory.ToArray();
                }
            }
        }
        public static byte[] ToArray(this Stream s)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public async static Task<byte[]> ToArrayAsync(this Stream s)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                await s.CopyToAsync(ms);
                var result = ms.ToArray();
                return result;
            }
        }

    }
}
