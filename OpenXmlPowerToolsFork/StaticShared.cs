using System.Xml.Linq;

namespace OpenXmlPowerTools
{
    internal static class StaticShared
    {
        internal static readonly HashSet<string> UnknownFonts = new ();

        private static HashSet<string> _knownFamilies;
        internal static HashSet<string> KnownFamilies
        {
            get
            {
                if (_knownFamilies == null)
                {
                    _knownFamilies = new HashSet<string>();
                    var families = SKFontManager.Default.FontFamilies;
                    foreach (var famName in families)
                        _knownFamilies.Add(famName);
                }
                return _knownFamilies;
            }
        }

        public static SKFontStyle GetFontStyle(this XElement rPr)
        {
            var isBold = Util.GetBoolProp(rPr, W.b) == true || Util.GetBoolProp(rPr, W.bCs) == true;
            var isItalic = Util.GetBoolProp(rPr, W.i) == true || Util.GetBoolProp(rPr, W.iCs) == true;
            SKFontStyle fs;
            if (isBold)
                fs = isItalic ? SKFontStyle.BoldItalic : SKFontStyle.Bold;
            else if (isItalic)
                fs = SKFontStyle.Italic;
            else
                fs = SKFontStyle.Normal;
            return fs;
        }
        public static SKEncodedImageFormat ParseImageFormat(string format)
        {
            foreach (var enc in Enum.GetNames<SKEncodedImageFormat>())
            {
                if (format.ToLower() == enc.ToLower() &&
                    Enum.TryParse(enc, out SKEncodedImageFormat result))
                    return result;
            }
            throw new FileFormatException($"ParseImageFormat({format}) error: SKEncodedImageFormat not found");
        }
    }
}
