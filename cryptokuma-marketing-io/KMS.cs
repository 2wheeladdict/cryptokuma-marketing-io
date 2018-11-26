using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Finexus.Aws.Utilities
{
    public static class KMS
    {
        private const string TAG = "Finexus.Aws.Utilities.KMS";

        public static async Task<string> DecryptString(string cipherText, string awsAccessKey, string awsSecretKey, string awsRegion)
        {
            using (var client = new AmazonKeyManagementServiceClient(awsAccessKey, awsSecretKey, RegionEndpoint.GetBySystemName(awsRegion)))
            {
                // get encrypted and Base64 encoded Api Key and Secret from config file
                var cipherBytes = Convert.FromBase64String(cipherText);

                try
                {
                    // init decryptor
                    var keyRequest = new DecryptRequest
                    {
                        CiphertextBlob = new System.IO.MemoryStream(cipherBytes)
                    };

                    // decrypt
                    var keyDecryptor = await client.DecryptAsync(keyRequest);

                    // read plain text
                    var plainText = Encoding.UTF8.GetString(keyDecryptor.Plaintext.ToArray());

                    //Console.WriteLine($"Done");
                    return plainText;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        public static async Task<string> DecryptEnvironmentVariable(string name)
        {
            try
            {
                // get environment variable text
                var encryptedBase64Text = Environment.GetEnvironmentVariable(name);

                // convert Base64 Encoded text to bytes
                var encryptedBytes = Convert.FromBase64String(encryptedBase64Text);

                // init KMS client
                using (var client = new AmazonKeyManagementServiceClient())
                {
                    // construct request
                    var decryptRequest = new DecryptRequest
                    {
                        CiphertextBlob = new MemoryStream(encryptedBytes),
                    };

                    // call KMS to decrypt data
                    var response = await client.DecryptAsync(decryptRequest);
                    using (var plaintextStream = response.Plaintext)
                    {
                        // get decrypted bytes
                        var plaintextBytes = plaintextStream.ToArray();

                        // convert decrypted bytes to ASCII text
                        var plaintext = Encoding.UTF8.GetString(plaintextBytes);

                        return plaintext;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);

                if (ex.InnerException != null && !String.IsNullOrEmpty(ex.InnerException.Message))
                {
                    Console.WriteLine(ex.InnerException.Message);
                }

                if (!String.IsNullOrEmpty(ex.StackTrace))
                {
                    Console.WriteLine(ex.StackTrace);
                }

                throw ex;
            }
        }
    }
}
