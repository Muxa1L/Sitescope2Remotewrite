using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using MuxLibrary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MetricReceiver
{
    public class XDocumentInputFormatter : InputFormatter, IInputFormatter, IApiRequestFormatMetadataProvider
    {
        public XDocumentInputFormatter()
        {
            SupportedMediaTypes.Add("text/xml");
            SupportedMediaTypes.Add("application/xml");
        }

        protected override bool CanReadType(Type type)
        {
            if (type.IsAssignableFrom(typeof(XDocument)))
                return true;
            return base.CanReadType(type);
        }

        public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
        {
            try
            {
                var contents = await context.HttpContext.Request.Body.ToArrayAsync();
                if (contents.IsGZip())
                    contents = contents.Decompress();

                var xml = ReplaceHexadecimalSymbols(Encoding.UTF8.GetString(contents));
                var xmlDoc = XDocument.Parse(xml);
                return InputFormatterResult.Success(xmlDoc);
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine("Error on parsing XML " + ex.Message);
                return null;
            }
        }

        static string ReplaceHexadecimalSymbols(string txt)
        {
            string r = "[\x00-\x08\x0B\x0C\x0E-\x1F\x26]";
            return Regex.Replace(txt, r, "", RegexOptions.Compiled);
        }
    }
}
