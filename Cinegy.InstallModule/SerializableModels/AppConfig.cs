namespace Cinegy.InstallModule
{
    public class AppConfig
    {
        public string ProductsRepository { get; set; } = "http://caas-deploy.s3.amazonaws.com/v1/";
        
        public string ProductsDownloadFolder { get; set; } = @"C:\ProgramData\Cinegy\Agent\Products";

    }
}
