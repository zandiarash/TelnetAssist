using System.Text.RegularExpressions;

namespace Telnet
{
   public static class StringClass
    {
        public static string TrimNonAscii(this string value)
        {
            //string pattern = "[^ -~]+";
            string pattern = $"[{(char)0}-{(char)31}{(char)127}�]+";
            Regex reg_exp = new Regex(pattern);
            return reg_exp.Replace(value, "");
        }
    }
}
