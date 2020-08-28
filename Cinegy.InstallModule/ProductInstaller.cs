using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Cinegy.InstallModule.Interfaces;
using Cinegy.InstallModule.SerializableModels;
using Newtonsoft.Json;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;

namespace Cinegy.InstallModule
{
    public class ProductInstaller
    {
        private readonly ILogger _logger;
        private readonly AppConfig _appConfig;
        private DateTime _lastReportedDownloadStatus;
        private long _lastReportedDownloadBytes= -1;

        public ProductInstaller(AppConfig appConfig,ILogger logger)
        {
            _appConfig = appConfig;
            _logger = logger;
        }

        public void Run(ProductDetails product)
        {
            try
            {
                var productDir = new DirectoryInfo($"{_appConfig.ProductsDownloadFolder}\\{product.Name}\\{product.VersionTag}");

                switch (product.Status)
                {
                    case ProductStatus.Current:
                        _logger.Warn($"Requested {product.Name} be processed, but status is already current... no action taken.");
                        return;
                    case ProductStatus.Blocked:
                        _logger.Warn($"Installation of {product.Name} has been blocked - please clear installblocked.flag.");
                        return;
                    case ProductStatus.Indeterminate:
                        _logger.Warn($"Installation of {product.Name} is indeterminate (server issue or unknown package).");
                        return;
                    //prevent endless looping - convert to blocked state if repeatedly fails
                    case ProductStatus.InProgress when File.Exists(productDir.FullName + "\\installrecovery.flag"):
                        _logger.Warn($"Repeated installation failure - skipping package {product.Name}, manual package cache clear required.");
                        File.Create(productDir.FullName + "\\installblocked.flag").Close();
                        return;
                    //this must be a second attempt installing, so cleanup everything to try again
                    case ProductStatus.InProgress:
                        Directory.Delete(productDir.FullName, true);
                        Directory.CreateDirectory(productDir.FullName);
                        File.Create(productDir.FullName + "\\installrecovery.flag").Close();
                        break;
                    case ProductStatus.Outdated:
                        _logger.Trace($"Versions are different. Downloading {product.Name}.");
                        //clean out the old version, and set to in progress to attempt a normal install
                        productDir.Delete(true);
                        product.Status = ProductStatus.InProgress;
                        break;
                }

                PerformInstall(product);
            }
            catch (Exception ex)
            {
                _logger.Error($"Problem managing status of product {product.Name}: {ex.Message}",ex);
            }
        }

        public void PerformInstall(ProductDetails product)
        {
            var productRepository = _appConfig.ProductsRepository;
            var productDir = new DirectoryInfo($"{_appConfig.ProductsDownloadFolder}\\{product.Name}\\{product.VersionTag}");
            var downloadOnly = false; //TODO: Read from config or manifest

            try
            {
                var remoteProductUrl = $"{productRepository}{product.Name}\\{product.VersionTag}\\";
                _logger.Info($"{product.Name} does not exist or a different version - will upgrade.");
                productDir.Create();

                _logger.Info($"{product.Name} download folder is {productDir.FullName}.");


                _logger.Info($"Start downloading {product.CatalogVersion.Name} version {product.CatalogVersion.Version}.");
                _logger.Info($"Package file is {product.CatalogVersion.PackageFile}.");

                var newProductFile = new FileInfo($"{productDir.FullName}\\{product.CatalogVersion.PackageFile}");

                var downloadClient = new HttpClientDownloadWithProgress(remoteProductUrl + product.CatalogVersion.PackageFile, newProductFile.FullName);

                downloadClient.ProgressChanged += DownloadClientOnProgressChanged;
                var downloadTask = downloadClient.StartDownload();
                
                downloadTask.Wait();

                if (downloadTask.Exception != null)
                {
                    _logger.Error($"Download failure: {downloadTask.Exception.Message}",downloadTask.Exception);
                    return;
                }

                _logger.Info($"Successfully downloaded {product.CatalogVersion.Name} version {product.CatalogVersion.Version}.");

                //save version file from product
                using (var fs = File.Open(productDir.FullName + "\\version.json",FileMode.CreateNew))
                {
                    var sw = new StreamWriter(fs);
                    var writer = new JsonTextWriter(sw) {Formatting = Formatting.Indented};
                    var serializer = new JsonSerializer();
                    serializer.Serialize(writer,product.CatalogVersion);
                    writer.Close();
                    sw.Close();
                    sw.Dispose();
                }

                if (downloadOnly)
                {
                    //remove inprogress flag file
                    File.Delete(newProductFile.FullName + ".inprogress");
                    return;
                }

                var destDirectory = new DirectoryInfo(productDir.FullName + "\\extract");

                if (newProductFile.Extension.ToLowerInvariant() == ".zip" |
                    (newProductFile.Extension.ToLowerInvariant() == ".7z"))
                {
                    UnzipArchive(newProductFile.FullName, destDirectory.FullName);
                }
                else
                {
                    destDirectory = new DirectoryInfo(newProductFile.Directory?.FullName ?? throw new InvalidOperationException());
                }

                _logger.Info($"Extraction of {product.CatalogVersion.Name} version {product.CatalogVersion.Version} finished successfully.");

                _logger.Info($"Start installing {product.CatalogVersion.Name} version {product.CatalogVersion.Version}.");

                //check if unattended-install.ps1 exists
                if (File.Exists(destDirectory.FullName + "\\Support\\Unattended-Install.ps1"))
                {
                    var args = new Dictionary<string, string>
                    {
                        {"rootPath", destDirectory.ToString()}
                    };

                    if (RunScript(destDirectory.FullName + "\\Support\\Unattended-Install.ps1", args))
                    {
                        _logger.Info($"Successfully installed {product.CatalogVersion.Name} version {product.CatalogVersion.Version}.");
                    }
                    else
                    {
                        _logger.Warn($"Failed to installed {product.CatalogVersion.Name} version {product.CatalogVersion.Version}.");
                    }
                }
                else
                {
                    if (product.CatalogVersion.AllowUnscriptedInstall)
                    {
                        //no specific unattended file - let's see if we just have a loose MSI or EXE...
                        var extractFolder = new DirectoryInfo(destDirectory.FullName);
                        foreach (var enumerateFile in extractFolder.EnumerateFiles())
                        {
                            //if there is a specific target set, only install that
                            if (!string.IsNullOrWhiteSpace(product.CatalogVersion.InstallationTarget) &&
                                              enumerateFile.Name != product.CatalogVersion.InstallationTarget)
                                continue;

                            switch (enumerateFile.Extension.ToLowerInvariant())
                            {
                                case ".msi":

                                    //run MSI install for all MSI files found with default args
                                    if (InstallMsi(enumerateFile.FullName))
                                    {
                                        _logger.Info($"Successfully installed {product.CatalogVersion.Name} version {product.CatalogVersion.Version}.");
                                    }
                                    else
                                    {
                                        _logger.Warn($"Failed to installed {product.CatalogVersion.Name} version {product.CatalogVersion.Version}.");
                                    }
                                    break;
                                case ".exe":
                                    //run EXE install for all EXE files found with args found in version manifest
                                    if (InstallExe(enumerateFile.FullName, product.CatalogVersion.InstallationArguments))
                                    {
                                        _logger.Info($"Successfully installed {product.CatalogVersion.Name} version {product.CatalogVersion.Version}.");
                                    }
                                    else
                                    {
                                        _logger.Warn($"Failed to installed {product.CatalogVersion.Name} version {product.CatalogVersion.Version}.");
                                    }
                                    break;
                            }
                        }
                    }
                }

                //remove inprogress flag file
                File.Delete(newProductFile.FullName + ".inprogress");
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message,ex);
            }
        }

        private void DownloadClientOnProgressChanged(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage, string destinationFile)
        {
            File.WriteAllText(destinationFile + ".inprogress", $"{progressPercentage}");
            var downloadRate = 0.0;

            if(_lastReportedDownloadBytes == -1)
            {
                _lastReportedDownloadBytes = totalBytesDownloaded;
            }
            else
            {
                var timeElapsed = DateTime.UtcNow.Subtract(_lastReportedDownloadStatus);
                var newlyDownloadedBytes = totalBytesDownloaded - _lastReportedDownloadBytes;
                if (newlyDownloadedBytes > 0)
                {
                    downloadRate = (newlyDownloadedBytes / timeElapsed.TotalSeconds) / 131072;
                }
            }

            if (DateTime.UtcNow <= _lastReportedDownloadStatus.AddSeconds(2)) return;

            var intProgressPercentage = 0;
            if (progressPercentage.HasValue)
            {
                intProgressPercentage = (int)progressPercentage;
            }

            var description = $"Downloading file {destinationFile}";

            if (downloadRate > 0)
            {
                description = $"Downloading file {destinationFile} ({downloadRate:0.0}Mbit/s)";
            }

            ProgressRecordChanged?.Invoke(0, "Download Package", description, intProgressPercentage);

            _lastReportedDownloadStatus = DateTime.UtcNow;
            _lastReportedDownloadBytes = totalBytesDownloaded;
        }

        public delegate void ProgressRecordChangedHandler(int activityId, string activity, string statusDescription, int percentComplete);

        public event ProgressRecordChangedHandler ProgressRecordChanged;

        private bool RunScript(string scriptName, Dictionary<string, string> args)
        {
            try
            {
                if (!File.Exists(scriptName)) throw new FileNotFoundException("Cannot find file",scriptName);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                        $"./{Path.GetFileName(scriptName)}")
                    {
                        WorkingDirectory = Path.GetDirectoryName(scriptName) ?? string.Empty,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();

                var reader = process.StandardOutput;
                process.WaitForExit();
                
                try
                {
                    _logger.Trace(reader.ReadToEnd());
                }
                catch (Exception ex)
                {
                    _logger.Error($"PowerShell script pushed unexpected object to output - check package script: {ex.Message}", ex);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"PowerShell script execution problem: {ex.Message}", ex);
                return false;
            }
           

            return true;
        }

        private void UnzipArchive(string archiveName, string targetPath)
        {
            _logger.Debug($"Start extracting {archiveName}.");

            if(!Directory.Exists(targetPath)) Directory.CreateDirectory(targetPath);

            try
            {
                var archive = Path.GetExtension(archiveName).ToLowerInvariant() == ".7z"
                    ? (IArchive) SevenZipArchive.Open(archiveName)
                    : ZipArchive.Open(archiveName);

                var reader = archive.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                {
                    //create any directories in the archive, ready for the stream to write into them
                    var directoryPath = Path.GetDirectoryName($"{targetPath}\\{reader.Entry.Key}");
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    if (reader.Entry.IsDirectory) continue;

                    _logger.Debug($"Extracting {reader.Entry.Key}");
                    var uncompressedFileStream = File.Open(targetPath + $"\\{reader.Entry.Key}", FileMode.OpenOrCreate);
                    reader.WriteEntryTo(uncompressedFileStream);
                    uncompressedFileStream.Dispose();
                }

                archive.Dispose();

            }
            catch (Exception ex)
            {
                _logger.Error($"Unzip operation encountered a problem: {ex.Message}",ex);
            }
        }

        private bool InstallMsi(string msiPath)
        {
            try
            {
                var p = new Process
                {
                    StartInfo =
                    {
                        FileName = "msiexec",
                        Arguments = $"/i \"{msiPath}\" /quiet /qn",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                p.StandardOutput.ReadToEnd();

            }
            catch (Exception ex)
            {
                _logger.Error($"Problem installing MSI from path: {msiPath}.",ex);
                return false;
            }

            return true;
        }

        private bool InstallExe(string exePath, string installArguments)
        {
            try
            {
                var p = new Process
                {
                    StartInfo =
                    {
                        FileName = exePath,
                        Arguments = installArguments,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                p.Start();
                p.StandardOutput.ReadToEnd();

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Problem executing installation EXE from path: {exePath} with arguments {installArguments}.", ex);
                return false;
            }
        }
    }
}
