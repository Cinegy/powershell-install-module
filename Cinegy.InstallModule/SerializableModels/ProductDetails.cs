using System.Collections.Generic;

namespace Cinegy.InstallModule.SerializableModels
{
    public class ProductDetails
    {
        public string Name { get; set; }

        public string VersionTag { get; set; }

        public VersionObject InstalledVersion { get; set; }

        public VersionObject CatalogVersion { get; set; }

        public ProductStatus Status { get; set; }

        public Dictionary<string, string> Options { get; set; }
    }

    public enum ProductStatus
    {
        Uninstalled,
        Blocked,
        Recovering,
        Outdated,
        InProgress,
        PendingDownload,
        Current,
        Indeterminate
    }
}
