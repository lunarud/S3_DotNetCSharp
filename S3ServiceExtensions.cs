using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Microsoft.Extensions.DependencyInjection;

namespace S3.Services;

// ============================================================
// OPTION 1: Default Credential Chain (Recommended)
// ============================================================
// The AWS SDK automatically detects IRSA/Web Identity credentials
// when these environment variables are present in the pod:
//   - AWS_ROLE_ARN
//   - AWS_WEB_IDENTITY_TOKEN_FILE
//   - AWS_REGION (optional, but recommended)
//
// NO CODE CHANGES NEEDED - just use the existing S3ClientService
// with the parameterless/region-only constructor:
//
//   var s3Service = new S3ClientService("my-bucket", RegionEndpoint.USEast1);
//
// The SDK credential chain tries (in order):
//   1. Environment variables (AWS_ACCESS_KEY_ID, etc.)
//   2. Web Identity Token (IRSA) ← This is what OpenShift uses
//   3. ECS Task Role
//   4. EC2 Instance Profile


// ============================================================
// OPTION 2: Explicit Web Identity Configuration
// ============================================================
// Use this if you need more control or troubleshooting

public static class S3ServiceExtensions
{
    /// <summary>
    /// Registers S3 client for OpenShift/Kubernetes with Web Identity (IRSA)
    /// </summary>
    public static IServiceCollection AddS3ClientForOpenShift(
        this IServiceCollection services,
        string bucketName,
        RegionEndpoint region)
    {
        services.AddSingleton<IAmazonS3>(sp =>
        {
            // SDK auto-detects Web Identity from environment
            var config = new AmazonS3Config { RegionEndpoint = region };
            return new AmazonS3Client(config);
        });

        services.AddSingleton<IS3ClientService>(sp =>
            new S3ClientService(sp.GetRequiredService<IAmazonS3>(), bucketName));

        return services;
    }

    /// <summary>
    /// Explicit Web Identity credentials (for debugging or custom token paths)
    /// </summary>
    public static IServiceCollection AddS3ClientWithWebIdentity(
        this IServiceCollection services,
        string bucketName,
        string roleArn,
        string tokenFilePath,
        RegionEndpoint region)
    {
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var credentials = new AssumeRoleWithWebIdentityCredentials(
                webIdentityTokenFile: tokenFilePath,
                roleArn: roleArn,
                roleSessionName: $"openshift-{Environment.MachineName}"
            );

            return new AmazonS3Client(credentials, region);
        });

        services.AddSingleton<IS3ClientService>(sp =>
            new S3ClientService(sp.GetRequiredService<IAmazonS3>(), bucketName));

        return services;
    }
}


// ============================================================
// Configuration Helper - Reads from OpenShift environment
// ============================================================

public class AwsOpenShiftConfig
{
    public string BucketName { get; set; } = default!;
    public string Region { get; set; } = "us-east-1";
    
    // These are auto-injected by OpenShift/IRSA - usually don't need to set manually
    public string? RoleArn { get; set; }
    public string? WebIdentityTokenFile { get; set; }

    public static AwsOpenShiftConfig FromEnvironment()
    {
        return new AwsOpenShiftConfig
        {
            BucketName = Environment.GetEnvironmentVariable("AWS_S3_BUCKET") 
                ?? throw new InvalidOperationException("AWS_S3_BUCKET not set"),
            Region = Environment.GetEnvironmentVariable("AWS_REGION") 
                ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") 
                ?? "us-east-1",
            RoleArn = Environment.GetEnvironmentVariable("AWS_ROLE_ARN"),
            WebIdentityTokenFile = Environment.GetEnvironmentVariable("AWS_WEB_IDENTITY_TOKEN_FILE")
        };
    }

    public RegionEndpoint GetRegionEndpoint() => RegionEndpoint.GetBySystemName(Region);

    public void Validate()
    {
        if (string.IsNullOrEmpty(RoleArn))
            throw new InvalidOperationException("AWS_ROLE_ARN not set. Ensure IRSA is configured.");
        
        if (string.IsNullOrEmpty(WebIdentityTokenFile))
            throw new InvalidOperationException("AWS_WEB_IDENTITY_TOKEN_FILE not set.");
        
        if (!File.Exists(WebIdentityTokenFile))
            throw new InvalidOperationException($"Token file not found: {WebIdentityTokenFile}");
    }
}


// ============================================================
// Program.cs / Startup Example
// ============================================================

public class StartupExample
{
    public static void ConfigureServices(IServiceCollection services)
    {
        var config = AwsOpenShiftConfig.FromEnvironment();
        
        // Option 1: Let SDK auto-detect (recommended)
        services.AddS3ClientForOpenShift(config.BucketName, config.GetRegionEndpoint());

        // Option 2: Explicit (if auto-detect fails)
        // config.Validate();
        // services.AddS3ClientWithWebIdentity(
        //     config.BucketName,
        //     config.RoleArn!,
        //     config.WebIdentityTokenFile!,
        //     config.GetRegionEndpoint()
        // );
    }
}


// ============================================================
// Health Check - Verify AWS Credentials Work
// ============================================================

public class S3HealthCheck
{
    private readonly IS3ClientService _s3Service;
    private readonly ILogger<S3HealthCheck> _logger;

    public S3HealthCheck(IS3ClientService s3Service, ILogger<S3HealthCheck> logger)
    {
        _s3Service = s3Service;
        _logger = logger;
    }

    public async Task<bool> CheckAsync()
    {
        try
        {
            // Simple list operation to verify credentials
            await _s3Service.ListFilesAsync(prefix: "__healthcheck__");
            _logger.LogInformation("S3 health check passed");
            return true;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 health check failed: {Message}", ex.Message);
            return false;
        }
    }
}
