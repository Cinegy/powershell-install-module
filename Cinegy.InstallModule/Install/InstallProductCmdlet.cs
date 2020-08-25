using System;
using System.Management.Automation;
using Cinegy.InstallModule.SerializableModels;

namespace Cinegy.InstallModule.Install
{
    [Cmdlet(VerbsLifecycle.Install, "Product")]
    [OutputType(typeof(ProductDetails))]
    public class InstallProductCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true)] public string PackageName { get; set; }
        
        [Parameter(Mandatory = true)] public string VersionTag { get; set; }
        
        [Parameter] public SwitchParameter Force { get; set; }

        // This method will be called for each input received from the pipeline to this cmdlet; if no input is received, this method is not called
        protected override void ProcessRecord()
        {
            try
            {
                var appConfig = new AppConfig();
                
                var packageManager = new PackageManager(appConfig);

                packageManager.InstallSingleProductPackage(PackageName,VersionTag,Force.ToBool());
                var productDetails = packageManager.GetProductDetails(PackageName, VersionTag);
                
                WriteObject(productDetails);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "Failed getting current product details", ErrorCategory.InvalidData, this));
            }
        }
    }

}
