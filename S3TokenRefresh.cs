using System;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Microsoft.Extensions.Logging;

namespace RH.Functions
{
    static class S3TokenRefresh
    {
        static ILogger Log;

        internal async static void Run(ILogger log)
        {
            Log = log;

            Log.LogInformation("Starting...");
            Console.WriteLine("starting token refresh....");

            try
            {
                Credentials tempCredentials = await GetTemporaryCredentialsAsync();
                await StoreInKeyVault(tempCredentials);

                Log.LogInformation("Done :-)");
                await Task.CompletedTask;
            }
            catch (AmazonS3Exception s3Exception)
            {
                Log.LogError(s3Exception, s3Exception.Message);
                Console.WriteLine(s3Exception.Message, s3Exception.InnerException);
                throw;
            }
            catch (AmazonSecurityTokenServiceException stsException)
            {
                Log.LogError(stsException, stsException.Message);
                Console.WriteLine(stsException.Message, stsException.InnerException);
                throw;
            }
            catch (System.Exception ex)
            {
                Log.LogError(ex, ex.Message);
                throw;
            }
        }

        private static AWSCredentials GetAwsCredentials()
        {
            var isLocal = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
            Log.LogInformation($"is local: {isLocal}");
            var options = new CredentialProfileOptions
            {
                AccessKey = Environment.GetEnvironmentVariable(isLocal ? "PlainUsername" : "UsernameFromKeyVault", EnvironmentVariableTarget.Process),
                SecretKey = Environment.GetEnvironmentVariable(isLocal ? "PlainPassword" : "PasswordFromKeyVault", EnvironmentVariableTarget.Process)
            };
            var profile = new CredentialProfile("s3_reader", options);
            profile.Region = RegionEndpoint.EUCentral1;
            var netSDKFile = new NetSDKCredentialsFile();
            netSDKFile.RegisterProfile(profile);

            Log.LogInformation($"Calling GetAWSCredentials for profile '{profile.Name}', AccessKey '{profile.Options.AccessKey}'");
            AWSCredentials awsCredentials = AWSCredentialsFactory.GetAWSCredentials(profile, netSDKFile, true);
            Log.LogInformation("Got AWS Credentials");
            return awsCredentials;
        }

        /// <summary>
        /// https://docs.aws.amazon.com/AmazonS3/latest/dev/AuthUsingTempSessionTokenDotNet.html
        /// </summary>
        /// <returns></returns>
        private static async Task<Credentials> GetTemporaryCredentialsAsync()
        {
            var awsCredentials = GetAwsCredentials();
            using (var stsClient = new AmazonSecurityTokenServiceClient(awsCredentials))
            {
                var getSessionTokenRequest = new GetSessionTokenRequest
                {
                    DurationSeconds = 7200 // seconds
                };

                Log.LogInformation($"GetSessionToken from AWS with a lifetime of {getSessionTokenRequest.DurationSeconds}s");
                GetSessionTokenResponse sessionTokenResponse = await stsClient.GetSessionTokenAsync(getSessionTokenRequest);
                Credentials credentials = sessionTokenResponse.Credentials;
                Log.LogInformation($"Got Session Token: AccessKeyId {credentials.AccessKeyId}'");
                // Log.LogInformation($"Got Session Token: AccessKeyId {credentials.AccessKeyId}', SecretAccessKey'{credentials.SecretAccessKey}', SessionToken '{credentials.SessionToken}'");

                return credentials;
            }
        }

        private static async Task StoreInKeyVault(Credentials awsCredentials)
        {
            var kvUrl = Environment.GetEnvironmentVariable("KeyVaultUrl");
            Log.LogInformation($"Storing Session Token in KeyVault '{kvUrl}'");
            var isLocal = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
            var defaultAzureCredentialOptions = new DefaultAzureCredentialOptions
            {
                ExcludeAzureCliCredential = true,
                ExcludeEnvironmentCredential = true,
                ExcludeInteractiveBrowserCredential = true,
                ExcludeManagedIdentityCredential = true,
                ExcludeSharedTokenCacheCredential = true,
                ExcludeVisualStudioCodeCredential = true,
                ExcludeVisualStudioCredential = true
            };

            // need to decide where to get the credentials from to connect to KeyVault
            if (isLocal)
            {
                defaultAzureCredentialOptions.ExcludeAzureCliCredential = false;
            }
            else
            {
                defaultAzureCredentialOptions.ExcludeManagedIdentityCredential = false;
            }

            Log.LogInformation("Storing secrets in KeyVault");
            var client = new SecretClient(new Uri(kvUrl), new DefaultAzureCredential(defaultAzureCredentialOptions));
            // Log.LogInformation("Storing 'AccessKeyId': '{awsCredentials.AccessKeyId}'");
            await client.SetSecretAsync("AccessKeyId", awsCredentials.AccessKeyId);

            // Log.LogInformation("Storing 'SecretAccessKey': '{awsCredentials.SecretAccessKey}'");
            await client.SetSecretAsync("SecretAccessKey", awsCredentials.SecretAccessKey);

            // Log.LogInformation("Storing 'SessionToken': '{awsCredentials.SessionToken}'");
            await client.SetSecretAsync("SessionToken", awsCredentials.SessionToken);
            Log.LogInformation("Secrets saved in KeyVault");
        }
    }
}
