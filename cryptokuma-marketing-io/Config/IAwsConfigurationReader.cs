using System;
using System.Collections.Generic;
using System.Text;

namespace Finexus.IO.Config
{
    public interface IAwsConfigurationReader
    {
        string AccessKey { get; }
        string SecretKey { get; }
    }
}
