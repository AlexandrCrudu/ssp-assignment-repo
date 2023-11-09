using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SSP_assignment.Helpers;
using SSP_assignment.Models;

namespace SSP_assignment.Functions
{
    public class ProcessImageFunction
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly string _blobStorageConnectionString = "DefaultEndpointsProtocol=https;AccountName=imagestoragessp;AccountKey=DUSq7JOwFuaYdzNjncs0fxmQpWSGD3PcmZ4YiqpkepkFZAcHGVsVXqnh2EnOIC1uGA0md0FU69y6+AStq/n2aA==;EndpointSuffix=core.windows.net"; // TODO: replace with your actual connection string
        private readonly string _tableStorageConnectionString = "DefaultEndpointsProtocol=https;AccountName=imagestoragessp;AccountKey=DUSq7JOwFuaYdzNjncs0fxmQpWSGD3PcmZ4YiqpkepkFZAcHGVsVXqnh2EnOIC1uGA0md0FU69y6+AStq/n2aA==;EndpointSuffix=core.windows.net"; // Your connection string
        private readonly string _tableName = "imageStatuses";
        public ProcessImageFunction(ILogger<ProcessImageFunction> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        [Function(nameof(ProcessImageFunction))]
        public async Task Run([QueueTrigger("image-queue", Connection = "AzureWebJobsStorage")] string imageQueueItem)
        {
            try
            {
                var jobDetails = JsonConvert.DeserializeObject<JobDetails>(imageQueueItem);
                if (jobDetails == null)
                {
                    _logger.LogError("Queue item is not in the expected format.");
                    return;
                }

                Stream imageStream = await _httpClient.GetStreamAsync(jobDetails.ImageUrl);
                byte[] imageBytes = await StreamToByteArrayAsync(imageStream);

                byte[] processedImageBytes = ImageHelper.AddWeatherDataToImage(imageBytes, jobDetails.WeatherData);
                using var processedImageStream = new MemoryStream(processedImageBytes);

                processedImageStream.Position = 0;
                await UploadToBlobStorageAsync(processedImageStream, jobDetails.ImageId);
                await UpdateImageStatusAsync(jobDetails.JobId, jobDetails.ImageId, "Processed");
                _logger.LogError($"updated status of image with id : {jobDetails.ImageId}");

            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing image: {ex.Message}");
            }
        }




        private async Task UploadToBlobStorageAsync(Stream imageStream, string JobId)
        {

            var blobServiceClient = new BlobServiceClient(_blobStorageConnectionString);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient("processed-images"); 
            var blobClient = blobContainerClient.GetBlobClient($"{JobId}.png");

            await blobClient.UploadAsync(imageStream, overwrite: true);
        }

        private async Task<byte[]> StreamToByteArrayAsync(Stream input)
        {
            using var memoryStream = new MemoryStream();
            await input.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }

        private async Task UpdateImageStatusAsync(string jobId, string imageId, string status)
        {
            var tableClient = new TableClient(_tableStorageConnectionString, _tableName);

            try
            {
                // Retrieve the entity to update
                var entity = await tableClient.GetEntityAsync<ImageJobEntity>(jobId, imageId);

                // Check if entity exists
                if (entity != null)
                {
                    // Update the status
                    entity.Value.Status = status;
                    entity.Value.Timestamp = DateTime.UtcNow; // Update timestamp

                    // Replace the existing entity with the updated one
                    await tableClient.UpdateEntityAsync(entity.Value, entity.Value.ETag, TableUpdateMode.Replace);
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogError($"Entity with PartitionKey '{jobId}' and RowKey '{imageId}' not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating entity: {ex.Message}");
            }
        }
    }
}
