using System;
using System.Management.Automation;
using Cinegy.InstallModule.Interfaces;

namespace Cinegy.InstallModule.LogWrapper
{

    public class PowershellLogger : ILogger
    {
        private readonly CinegyCmdletBase _cmdlet;

        public PowershellLogger(CinegyCmdletBase cmdlet)
        {
            _cmdlet = cmdlet;
        }

        public void Debug(string message)
        {
            _cmdlet.WriteDebug(message);
        }

        public void Error(string message)
        {
            _cmdlet.WriteError(new ErrorRecord(_cmdlet.Exception, "message", ErrorCategory.NotSpecified, _cmdlet));
        }

        public void Error(string message, Exception ex)
        {
            _cmdlet.Exception = ex;
            Error(message);
        }

        public void Fatal(string message)
        {
            Error(message);
        }
        public void Fatal(string message, Exception ex)
        {
            _cmdlet.Exception = ex;
            Error(message);
        }

        public void Info(string message)
        {
            _cmdlet.WriteInformation(new InformationRecord(message, _cmdlet.ToString()));
        }

        public void Telemetry(LogLevel level, string key, string message, object obj)
        {
            throw new NotImplementedException();
        }

        public void Trace(string message)
        {
            _cmdlet.WriteDebug(message);
        }

        public void Warn(string message)
        {
            _cmdlet.WriteWarning(message);
        }

        public void Progress(int activityId,string activity,string statusDescription,int percentComplete)
        {
            var progressRecord = new ProgressRecord(activityId, activity, statusDescription)
            {
                PercentComplete = percentComplete
            };

            _cmdlet.WriteProgress(progressRecord);
        }
    }
}
