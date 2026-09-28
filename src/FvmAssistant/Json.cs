using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace FvmAssistant
{
    internal static class Json
    {
        private static JavaScriptSerializer Create() =>
            new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };

        public static string Serialize(object value) => Create().Serialize(value);

        public static T Deserialize<T>(string json) => Create().Deserialize<T>(json);

        public static Dictionary<string, object> Parse(string json) => Create().Deserialize<Dictionary<string, object>>(json);
    }
}
