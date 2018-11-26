using Finexus.Aws.Utilities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finexus.IO.Config
{
    /// <summary>
    /// AWS access credential configuration when running in Release mode from Finexus AWS account
    /// </summary>
    public class ServerConfig : IAwsConfigurationReader
    {
        private const string TAG = "Finexus.IO.Config.LocalConfig";

        public ServerConfig()
        {
            Console.WriteLine("ServerConfig::ctor:");
        }

        public string AccessKey => KMS.DecryptEnvironmentVariable("FINEXUS_ACCESS_KEY").Result;

        public string SecretKey => KMS.DecryptEnvironmentVariable("FINEXUS_SECRET_ACCESS_KEY").Result;
    }
}
