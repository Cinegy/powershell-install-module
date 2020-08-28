using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Cinegy.InstallModule
{
    public class HttpClientDownloadWithProgress : IDisposable
    {
        private readonly string _downloadUrl;
        private readonly string _destinationFilePath;

        private HttpClient _httpClient;

        public delegate void ProgressChangedHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage, string destinationFilePath);

        public event ProgressChangedHandler ProgressChanged;

        public HttpClientDownloadWithProgress(string downloadUrl, string destinationFilePath)
        {
            _downloadUrl = downloadUrl;
            _destinationFilePath = destinationFilePath;
        }

        public async Task StartDownload()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromDays(1) };
            var response = await _httpClient.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            await DownloadFileFromHttpResponseMessage(response);
            response.Dispose();
        }

        private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;

            var contentStream = await response.Content.ReadAsStreamAsync();
            await ProcessContentStream(totalBytes, contentStream);
            contentStream.Dispose();
        }

        private async Task ProcessContentStream(long? totalDownloadSize, Stream contentStream)
        {
            const int bufferSize = 8128*128;
            var totalBytesRead = 0L;
            var readCount = 0L;
            double? lastNotifiedPercentComplete = 0.0;
            var buffer = new byte[bufferSize];
            var isMoreToRead = true;

            var fileStream = new FileStream(_destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);
            do
            {
                var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    isMoreToRead = false;
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                    continue;
                }

                await fileStream.WriteAsync(buffer, 0, bytesRead);

                totalBytesRead += bytesRead;
                var percentComplete = GetRoundedPercentage(totalDownloadSize, totalBytesRead);

                if (percentComplete !=null)
                {
                    if (!(percentComplete - lastNotifiedPercentComplete > 2)) continue;
                    lastNotifiedPercentComplete = percentComplete;
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                }
                else
                {
                    readCount += 1;

                    if (readCount % 200 == 0)
                        TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                }
            }
            while (isMoreToRead);
            fileStream.Dispose();
        }

        private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead)
        {
            if (ProgressChanged == null)
                return;

            var progressPercentage = GetRoundedPercentage(totalDownloadSize, totalBytesRead);
            
            ProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage,_destinationFilePath);
        }

        private static double? GetRoundedPercentage(long? totalDownloadSize, long totalBytesRead)
        {
            double? progressPercentage = null;
            if (totalDownloadSize.HasValue)
                progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);

            return progressPercentage;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
