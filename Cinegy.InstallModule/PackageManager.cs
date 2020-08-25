using System;
using System.IO;
using System.Net;
using System.Net.Http;
using Cinegy.InstallModule.SerializableModels;
using Newtonsoft.Json;

namespace Cinegy.InstallModule
{

    public class PackageManager
    {
        private readonly AppConfig _appConfig;
        private readonly HttpClient _client = new HttpClient();
        
        #region Constructor

        public PackageManager(AppConfig appConfig)
        {
            _appConfig = appConfig;
        }
        
        #endregion

        public ProductDetails InstallSingleProductPackage(string packageName, string versionTag, bool forceInstall = false)
        {           
            if (forceInstall)
            {
                //_logger.LogInformation("Reinstall flag detected, erasing cached package to force reinstallation");
                RemoveCachedProduct(new ProductDetails() { Name = packageName, VersionTag = versionTag });
            }

            //check if we managed to get a properly formatted package name from the console...
            if(packageName==null)
            {
                //try to read a package from the command line or ask interactively
                //_logger.LogError("Please specify a package to install (force=true for reinstall), e.g. package=Thirdparty-Firefox-Stable,prod force=true");
                return null;
            }

            var productDetails = GetProductDetails(packageName, versionTag);
            
            if (productDetails.Status == ProductStatus.Current)
            {
                //_logger.LogWarning($"Package {_products[0].Name} is already installed - reinstall with force=true flag");
            }

            InstallProduct(productDetails);

            productDetails = GetProductDetails(packageName, versionTag);

            return productDetails;
        }

        public ProductDetails GetProductDetails(string productName, string versionTag)
        {
            try
            {
                var product = new ProductDetails { Name = productName, VersionTag = versionTag };

                var productDir = new DirectoryInfo($"{_appConfig.ProductsDownloadFolder}\\{product.Name}\\{product.VersionTag}");

                //grab current server version details and deserialize
                var remoteProductUrl = $"{_appConfig.ProductsRepository}{product.Name}\\{product.VersionTag}\\";
                var response = _client.GetAsync(remoteProductUrl + "version.txt").Result;
                var versionContent = response.Content.ReadAsStringAsync().Result;
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    //_logger.LogError($"Can't access version details for: {product.Name},{product.VersionTag} (Status: {response.StatusCode})");
                    product.Status = ProductStatus.Indeterminate;
                    return product;
                }
                product.CatalogVersion = JsonConvert.DeserializeObject<VersionObject>(versionContent);
                var newProductFile = new FileInfo($"{productDir.FullName}\\{product.CatalogVersion.PackageFile}");

                //check to see if the product has anything downloaded at all
                //if not, status is 'uninstalled' and return
                if (!Directory.Exists(productDir.FullName))
                {
                    product.Status = ProductStatus.Uninstalled;
                    return product;
                }

                //check for any in-progress or blocked flags
                //if so, status is 'in-progress' or 'blocked' and return
                if (File.Exists(productDir.FullName + "\\installblocked.flag"))
                {
                    product.Status = ProductStatus.Blocked;
                    return product;
                }
                if (File.Exists(productDir.FullName + "\\installrecovery.flag"))
                {
                    product.Status = ProductStatus.Recovering;
                    return product;
                }

                //verify that version txt file exists 
                //if not, consider app uninstalled
                if (!File.Exists(productDir.FullName + "\\version.json"))
                {
                    product.Status = ProductStatus.Uninstalled;
                    return product;
                }
                
                //read local version TXT from local machine and deserialize, ready for comparison
                using (var file = File.OpenText(productDir.FullName + "\\version.json"))
                using (var reader = new JsonTextReader(file))
                {
                    var serializer = new JsonSerializer();
                    product.InstalledVersion = serializer.Deserialize<VersionObject>(reader);
                }

                //check if version values are not identical (absolute comparison, not numerical)
                //if not identical, consider product 'Outdated'
                if (string.CompareOrdinal(product?.InstalledVersion?.Version, product.CatalogVersion.Version) != 0)
                {
                    product.Status = ProductStatus.Outdated;
                    return product;
                }

                //verify data file referenced has been downloaded
                //if not, mark status as pending download
                if (!File.Exists(newProductFile.FullName))
                {
                    product.Status = ProductStatus.PendingDownload;
                    return product;
                }

                //finally, if no other cases have been detected then product must already be installed
                product.Status = ProductStatus.Current;
                return product;
            }
            catch (Exception ex)
            {
                //_logger.LogError($"Problem determing product {product.Name} status: {ex.Message}");
                return null;
            }
        }

        private void RemoveCachedProduct(ProductDetails product)
        {
            try
            {
                var productDir = $"{_appConfig.ProductsDownloadFolder}\\{product.Name}\\{product.VersionTag}";

                if (Directory.Exists(productDir))
                {
                    Directory.Delete(productDir,true);
                }                
            }
            catch (Exception ex)
            {
                //_logger.LogError($"Problem removing product install cache folder: {ex.Message}");
            }
        }

        private void InstallProduct(ProductDetails product)
        {
            if (product.Status == ProductStatus.Current) return;

            try
            {
                //_logger.LogInformation($"Checking {product.Name} - current status: {product.Status}");

                var installer = new ProductInstaller(_appConfig);

                installer.Run(product);

            }
            catch (Exception ex)
            {
                //_logger.LogError($"Problem running product install job: {ex.Message}");
            }
        }

    }
}
