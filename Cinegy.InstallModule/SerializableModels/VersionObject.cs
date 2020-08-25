namespace Cinegy.InstallModule.SerializableModels
{
    public class VersionObject
    {
        public string Name { get; set; }
        public string PackageFile { get; set; }
        public string Version { get; set; }
        public string MinAgent { get; set; }
        public string InstallationArguments { get; set; }
        public bool AllowUnscriptedInstall { get; set; }
        public string InstallationTarget { get; set; }
    }
}
