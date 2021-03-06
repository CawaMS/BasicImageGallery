﻿using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using ImageGallery.Model;
using CoreImageGallery.Data;
using CoreImageGallery.Extensions;
using System.Linq;
using System;

namespace CoreImageGallery.Services
{
    public class AzStorageService : IStorageService
    {
        private static bool ResourcesInitialized { get; set; } = false;

        private const string ImagePrefix = "img_";
        private readonly CloudStorageAccount _account;
        private readonly CloudBlobClient _client;
        private readonly string _connectionString;
        private CloudBlobContainer _uploadContainer;
        private CloudBlobContainer _publicContainer;

        private ApplicationDbContext _dbContext;

        public AzStorageService(IConfiguration config, ApplicationDbContext dbContext)
        {
            _connectionString = config["AzureStorageConnection"];
            _account = CloudStorageAccount.Parse(_connectionString);
            _client = _account.CreateCloudBlobClient();
            _uploadContainer = _client.GetContainerReference(Config.UploadContainer);
            _publicContainer = _client.GetContainerReference(Config.WatermarkedContainer);

            _dbContext = dbContext;
        }

        public async Task AddImageAsync(Stream stream, string originalName, string userName)
        {
            await InitializeResourcesAsync();

            UploadUtilities.GetImageProperties(originalName, userName, out string uploadId, out string fileName, out string userId);

            var imageBlob = _uploadContainer.GetBlockBlobReference(fileName);
            await imageBlob.UploadFromStreamAsync(stream);

            //added code: get the URI from _publicContainer instead of _uploadContainer
            string imagePath = imageBlob.Uri.ToString().Replace("images", "images-watermarked");

            //store the _publicContainer URI instead of upload container URI
            //await UploadUtilities.RecordImageUploadedAsync(_dbContext, uploadId, fileName, imageBlob.Uri.ToString(), userId);
            await UploadUtilities.RecordImageUploadedAsync(_dbContext, uploadId, fileName, imagePath, userId);
        }

        public async Task<IEnumerable<UploadedImage>> GetImagesAsync()
        {
            await InitializeResourcesAsync();

            //var imageList = new List<UploadedImage>();
            //var token = new BlobContinuationToken();
            //var blobList = await _publicContainer.ListBlobsSegmentedAsync(ImagePrefix, true, BlobListingDetails.All, 100, token, null, null);

            //foreach (var blob in blobList.Results)
            //{
            //    var image = new UploadedImage
            //    {
            //        ImagePath = blob.Uri.ToString()
            //    };

            //    imageList.Add(image);
            //}

            //added code: pull imagelist from database
            var imageList = _dbContext.Images;

            return imageList.Select(i => new UploadedImage { FileName = i.FileName, Id = i.Id, ImagePath = TransformBlobPathToLocalUri(i.ImagePath), UploadTime = i.UploadTime, UserHash = i.UserHash });
        }

        private static string TransformBlobPathToLocalUri(string imagePath)
        {
            var uri = new Uri(imagePath);
            var localPath = uri.LocalPath;
            localPath = localPath.Replace("/images-watermarked/", "/image/");
            return localPath;
        }

        private async Task InitializeResourcesAsync()
        {
            if (!ResourcesInitialized)
            {
                //first Azure Storage resources
                await _publicContainer.CreateIfNotExistsAsync();
                await _uploadContainer.CreateIfNotExistsAsync();

                var permissions = await _publicContainer.GetPermissionsAsync();
                if (permissions.PublicAccess == BlobContainerPublicAccessType.Off || permissions.PublicAccess == BlobContainerPublicAccessType.Unknown)
                {
                    // If blob isn't public, we can't directly link to the pictures
                    await _publicContainer.SetPermissionsAsync(new BlobContainerPermissions() { PublicAccess = BlobContainerPublicAccessType.Blob });
                }

                ResourcesInitialized = true;
            }

        }

    }
}
