using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Finexus.Aws.Utilities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finexus.IO.Config
{
    /// <summary>
    /// Local AWS access credential configuration when running in Debug mode from a local DEV machine.
    /// ** Requires a configured AWS stored profile on the machine. **
    /// </summary>
    public class LocalConfig : IAwsConfigurationReader
    {
        private ImmutableCredentials _credentials;
        private const string TAG = "Finexus.IO.Config.LocalConfig";
        private static Logger _logger = new Logger(TAG);

        public LocalConfig()
        {
            _logger.Log("LocalConfig::ctor:");

            var chain = new CredentialProfileStoreChain();
            AWSCredentials awsCredentials;
            chain.TryGetAWSCredentials("finexus", out awsCredentials);

            if (awsCredentials == null)
            {
                throw new Exception("Unable to obtain local AWS credentials for the configured profile");
            }

            _credentials = awsCredentials.GetCredentialsAsync().Result;
        }

        string IAwsConfigurationReader.AccessKey { get => _credentials.AccessKey; }

        string IAwsConfigurationReader.SecretKey { get => _credentials.SecretKey; }
    }
}
