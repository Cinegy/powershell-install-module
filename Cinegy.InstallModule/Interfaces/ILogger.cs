using System;

namespace Cinegy.InstallModule.Interfaces
{

    public interface ILogger
    {
        void Debug(string message);
        void Error(string message);
        void Error(string message, Exception ex);
        void Fatal(string message);
        void Fatal(string message, Exception ex);
        void Info(string message);
        void Telemetry(LogLevel level, string key, string message, object obj);
        void Trace(string message);
        void Warn(string message);
        void Progress(int activityId, string activity, string statusDescription, int percentComplete);
    }

    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,
        Fatal = 5,
        Off = 6
    }

}
