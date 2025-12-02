using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace S3.Services;

public interface IS3ClientService
{
    Task<List<S3ObjectInfo>> ListFilesAsync(string prefix = "", CancellationToken ct = default);
    Task<string> UploadFileAsync(string key, Stream stream, string contentType = null, CancellationToken ct = default);
    Task<string> UploadFileAsync(string key, string filePath, string contentType = null, CancellationToken ct = default);
    Task<Stream> DownloadFileAsync(string key, CancellationToken ct = default);
    Task DownloadFileToPathAsync(string key, string destinationPath, CancellationToken ct = default);
    Task<bool> DeleteFileAsync(string key, CancellationToken ct = default);
    Task<bool> FileExistsAsync(string key, CancellationToken ct = default);
}

public record S3ObjectInfo(string Key, long Size, DateTime LastModified, string ETag);

public class S3ClientService : IS3ClientService, IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly TransferUtility _transferUtility;

    public S3ClientService(string bucketName, RegionEndpoint region)
    {
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _s3Client = new AmazonS3Client(region);
        _transferUtility = new TransferUtility(_s3Client);
    }

    public S3ClientService(string bucketName, string accessKey, string secretKey, RegionEndpoint region)
    {
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _s3Client = new AmazonS3Client(accessKey, secretKey, region);
        _transferUtility = new TransferUtility(_s3Client);
    }

    // Constructor for DI - accepts IAmazonS3 directly
    public S3ClientService(IAmazonS3 s3Client, string bucketName)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _transferUtility = new TransferUtility(_s3Client);
    }

    /// <summary>
    /// Lists all files in the bucket, optionally filtered by prefix
    /// </summary>
    public async Task<List<S3ObjectInfo>> ListFilesAsync(string prefix = "", CancellationToken ct = default)
    {
        var results = new List<S3ObjectInfo>();
        string continuationToken = null;

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = prefix,
                ContinuationToken = continuationToken
            };

            var response = await _s3Client.ListObjectsV2Async(request, ct);

            results.AddRange(response.S3Objects.Select(obj => new S3ObjectInfo(
                obj.Key,
                obj.Size,
                obj.LastModified,
                obj.ETag
            )));

            continuationToken = response.IsTruncated ? response.NextContinuationToken : null;

        } while (continuationToken != null);

        return results;
    }

    /// <summary>
    /// Uploads a file from a stream
    /// </summary>
    public async Task<string> UploadFileAsync(string key, Stream stream, string contentType = null, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = stream
        };

        if (!string.IsNullOrEmpty(contentType))
            request.ContentType = contentType;

        await _s3Client.PutObjectAsync(request, ct);
        
        return $"s3://{_bucketName}/{key}";
    }

    /// <summary>
    /// Uploads a file from a local path (uses TransferUtility for large files)
    /// </summary>
    public async Task<string> UploadFileAsync(string key, string filePath, string contentType = null, CancellationToken ct = default)
    {
        var request = new TransferUtilityUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
            FilePath = filePath
        };

        if (!string.IsNullOrEmpty(contentType))
            request.ContentType = contentType;

        await _transferUtility.UploadAsync(request, ct);
        
        return $"s3://{_bucketName}/{key}";
    }

    /// <summary>
    /// Downloads a file and returns it as a stream
    /// </summary>
    public async Task<Stream> DownloadFileAsync(string key, CancellationToken ct = default)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        };

        var response = await _s3Client.GetObjectAsync(request, ct);
        
        // Copy to MemoryStream so caller owns the stream
        var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream, ct);
        memoryStream.Position = 0;
        
        return memoryStream;
    }

    /// <summary>
    /// Downloads a file directly to a local path
    /// </summary>
    public async Task DownloadFileToPathAsync(string key, string destinationPath, CancellationToken ct = default)
    {
        await _transferUtility.DownloadAsync(destinationPath, _bucketName, key, ct);
    }

    /// <summary>
    /// Deletes a file from S3
    /// </summary>
    public async Task<bool> DeleteFileAsync(string key, CancellationToken ct = default)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        };

        var response = await _s3Client.DeleteObjectAsync(request, ct);
        
        return response.HttpStatusCode == System.Net.HttpStatusCode.NoContent 
            || response.HttpStatusCode == System.Net.HttpStatusCode.OK;
    }

    /// <summary>
    /// Checks if a file exists in the bucket
    /// </summary>
    public async Task<bool> FileExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            await _s3Client.GetObjectMetadataAsync(request, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _transferUtility?.Dispose();
        _s3Client?.Dispose();
    }
}
