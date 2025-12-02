using Amazon;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using S3.Services;

// ============================================================
// NUGET PACKAGES REQUIRED:
// ============================================================
// dotnet add package AWSSDK.S3
// dotnet add package AWSSDK.Extensions.NETCore.Setup  (for DI)


// ============================================================
// OPTION 1: Dependency Injection Setup (Recommended)
// ============================================================

public static class S3ServiceExtensions
{
    public static IServiceCollection AddS3Client(
        this IServiceCollection services,
        string bucketName,
        RegionEndpoint region)
    {
        // Uses default credential chain (env vars, AWS profile, IAM role, etc.)
        services.AddDefaultAWSOptions(new Amazon.Extensions.NETCore.Setup.AWSOptions
        {
            Region = region
        });
        
        services.AddAWSService<IAmazonS3>();
        services.AddSingleton<IS3ClientService>(sp =>
            new S3ClientService(sp.GetRequiredService<IAmazonS3>(), bucketName));

        return services;
    }
}

// In Program.cs or Startup.cs:
// builder.Services.AddS3Client("my-bucket-name", RegionEndpoint.USEast1);


// ============================================================
// OPTION 2: Direct Usage Examples
// ============================================================

public class S3UsageExamples
{
    public static async Task RunExamples()
    {
        // Create client (uses default AWS credentials from environment/profile)
        using var s3Service = new S3ClientService(
            bucketName: "my-bucket",
            region: RegionEndpoint.USEast1
        );

        // Or with explicit credentials (not recommended for production)
        // using var s3Service = new S3ClientService(
        //     bucketName: "my-bucket",
        //     accessKey: "YOUR_ACCESS_KEY",
        //     secretKey: "YOUR_SECRET_KEY",
        //     region: RegionEndpoint.USEast1
        // );

        // ----- LIST FILES -----
        Console.WriteLine("=== Listing Files ===");
        var files = await s3Service.ListFilesAsync();
        foreach (var file in files)
        {
            Console.WriteLine($"  {file.Key} - {file.Size} bytes - {file.LastModified}");
        }

        // List with prefix filter
        var pdfFiles = await s3Service.ListFilesAsync(prefix: "documents/");

        // ----- UPLOAD FILE -----
        Console.WriteLine("\n=== Uploading Files ===");
        
        // From local path
        var uploadedPath = await s3Service.UploadFileAsync(
            key: "uploads/myfile.pdf",
            filePath: "/path/to/local/file.pdf",
            contentType: "application/pdf"
        );
        Console.WriteLine($"Uploaded to: {uploadedPath}");

        // From stream (e.g., from HTTP request)
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Hello S3!"));
        await s3Service.UploadFileAsync(
            key: "uploads/hello.txt",
            stream: stream,
            contentType: "text/plain"
        );

        // ----- DOWNLOAD FILE -----
        Console.WriteLine("\n=== Downloading Files ===");
        
        // To stream
        using var downloadStream = await s3Service.DownloadFileAsync("uploads/hello.txt");
        using var reader = new StreamReader(downloadStream);
        var content = await reader.ReadToEndAsync();
        Console.WriteLine($"Content: {content}");

        // To local path
        await s3Service.DownloadFileToPathAsync(
            key: "uploads/myfile.pdf",
            destinationPath: "/path/to/destination/file.pdf"
        );

        // ----- CHECK IF EXISTS -----
        var exists = await s3Service.FileExistsAsync("uploads/hello.txt");
        Console.WriteLine($"\nFile exists: {exists}");

        // ----- DELETE FILE -----
        Console.WriteLine("\n=== Deleting File ===");
        var deleted = await s3Service.DeleteFileAsync("uploads/hello.txt");
        Console.WriteLine($"Deleted: {deleted}");
    }
}


// ============================================================
// ASP.NET Core Controller Example
// ============================================================

// [ApiController]
// [Route("api/[controller]")]
// public class FilesController : ControllerBase
// {
//     private readonly IS3ClientService _s3Service;
//
//     public FilesController(IS3ClientService s3Service)
//     {
//         _s3Service = s3Service;
//     }
//
//     [HttpGet]
//     public async Task<IActionResult> List([FromQuery] string prefix = "")
//     {
//         var files = await _s3Service.ListFilesAsync(prefix);
//         return Ok(files);
//     }
//
//     [HttpPost("upload")]
//     public async Task<IActionResult> Upload(IFormFile file)
//     {
//         using var stream = file.OpenReadStream();
//         var key = $"uploads/{Guid.NewGuid()}/{file.FileName}";
//         var result = await _s3Service.UploadFileAsync(key, stream, file.ContentType);
//         return Ok(new { Key = key, Uri = result });
//     }
//
//     [HttpGet("download/{*key}")]
//     public async Task<IActionResult> Download(string key)
//     {
//         if (!await _s3Service.FileExistsAsync(key))
//             return NotFound();
//
//         var stream = await _s3Service.DownloadFileAsync(key);
//         return File(stream, "application/octet-stream", Path.GetFileName(key));
//     }
//
//     [HttpDelete("{*key}")]
//     public async Task<IActionResult> Delete(string key)
//     {
//         var deleted = await _s3Service.DeleteFileAsync(key);
//         return deleted ? NoContent() : NotFound();
//     }
// }
