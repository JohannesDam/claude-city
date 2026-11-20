using Colossal.Logging;

namespace ClaudeCityBridge
{
    /// <summary>Logs to %LocalLow%\Colossal Order\Cities Skylines II\Logs\ClaudeCityBridge.log</summary>
    public static class Log
    {
        static ILog log;
        static ILog L
        {
            get
            {
                if (log == null)
                {
                    log = LogManager.GetLogger("ClaudeCityBridge");
                    log.showsErrorsInUI = false;
                }
                return log;
            }
        }
        public static void Info(string s) => L.Info(s);
        public static void Warn(string s) => L.Warn(s);
        public static void Error(string s) => L.Error(s);
        public static void Error(System.Exception e, string s) => L.Error(e, s);
    }
}
