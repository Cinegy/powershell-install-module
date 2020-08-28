using System;
using System.Management.Automation;

namespace Cinegy.InstallModule
{
    public abstract class CinegyCmdletBase : PSCmdlet
    {
        internal Exception Exception { get; set; }
    }
}
