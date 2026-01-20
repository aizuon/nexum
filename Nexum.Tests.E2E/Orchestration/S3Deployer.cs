using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Serilog;

namespace Nexum.Tests.E2E.Orchestration
{
    public class S3Deployer : IDisposable
    {
        private readonly string _bucketName;
        private readonly ILogger _logger;
        private readonly AmazonS3Client _s3Client;

        public S3Deployer(string bucketName = null)
        {
            _logger = Log.ForContext<S3Deployer>();
            _s3Client = new AmazonS3Client();
            _bucketName = bucketName ?? AwsConfig.S3BucketName;
        }

        public void Dispose()
        {
            _s3Client?.Dispose();
        }

        public async Task EnsureBucketExistsAsync()
        {
            try
            {
                await _s3Client.GetBucketLocationAsync(_bucketName);
                _logger.Information("S3 bucket {BucketName} already exists", _bucketName);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.Information("Creating S3 bucket {BucketName}", _bucketName);

                var request = new PutBucketRequest
                {
                    BucketName = _bucketName,
                    UseClientRegion = true
                };

                await _s3Client.PutBucketAsync(request);
                _logger.Information("S3 bucket {BucketName} created", _bucketName);
            }
        }

        public async Task UploadFileAsync(string localFilePath, string s3Key)
        {
            _logger.Information("Uploading {LocalFile} to s3://{Bucket}/{Key}",
                localFilePath, _bucketName, s3Key);

            using var transferUtility = new TransferUtility(_s3Client);
            await transferUtility.UploadAsync(localFilePath, _bucketName, s3Key);

            _logger.Information("Upload complete: {Key}", s3Key);
        }

        public async Task UploadDirectoryAsync(string localDirectory, string s3KeyPrefix)
        {
            _logger.Information("Uploading directory {LocalDir} to s3://{Bucket}/{KeyPrefix}",
                localDirectory, _bucketName, s3KeyPrefix);

            using var transferUtility = new TransferUtility(_s3Client);
            await transferUtility.UploadDirectoryAsync(localDirectory, _bucketName, s3KeyPrefix, default(SearchOption));

            _logger.Information("Directory upload complete: {KeyPrefix}", s3KeyPrefix);
        }

        public string GetS3Uri(string key)
        {
            return $"s3://{_bucketName}/{key}";
        }

        public string GetHttpsUrl(string key, string region)
        {
            return $"https://{_bucketName}.s3.{region}.amazonaws.com/{key}";
        }

        public async Task DeleteObjectAsync(string s3Key)
        {
            try
            {
                await _s3Client.DeleteObjectAsync(_bucketName, s3Key);
                _logger.Debug("Deleted s3://{Bucket}/{Key}", _bucketName, s3Key);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete s3://{Bucket}/{Key}", _bucketName, s3Key);
            }
        }
    }
}
