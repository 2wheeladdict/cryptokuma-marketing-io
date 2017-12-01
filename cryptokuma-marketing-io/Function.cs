using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Newtonsoft.Json;
using Finexus.IO.Utilities;
using Amazon.DynamoDBv2;
using Amazon;
using Finexus.IO.Config;
using Finexus.Aws.Utilities;
using Amazon.DynamoDBv2.DocumentModel;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace Cryptokuma.Marketing.IO
{
    public class Contact {
        public string Email { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string CookieStack { get; set; }
    }

    public class Functions
    {
        private const string TAG = "FunctionHandler";
        private Logger _logger = new Logger(TAG);

        private string _awsAccessKey;
        private string _awsSecretKey;
        protected const string REGION = "us-east-1";

        /// <summary>
        /// Default constructor that Lambda will invoke.
        /// </summary>
        public Functions()
        {
        }


        /// <summary>
        /// A Lambda function to collect Contact Form fields and save to DynamoDB
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> ProcessContactFormAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            var logTag = "ProcessContactFormAsync";
            _logger.Log("BEGIN", logTag);

            try
            {
                // load AWS credentials
                LoadConfig();

                var contactRequest = JsonConvert.DeserializeObject<Contact>(request.Body);

                using (var dynamoClient = new AmazonDynamoDBClient(_awsAccessKey, _awsSecretKey, RegionEndpoint.GetBySystemName(REGION)))
                {
                    // init connection to DynamoDB table
                    _logger.Log($"Initializing connection to DynamoDB Contact table");
                    Table ddbTable = Table.LoadTable(dynamoClient, "marketing-contact");

                    // init DynamoDB PUT
                    PutItemOperationConfig config = new PutItemOperationConfig
                    {
                        ReturnValues = ReturnValues.AllOldAttributes
                    };

                    // init DynamoDB row
                    var contactRow = new Document();
                    contactRow["email"] = contactRequest.Email;
                    contactRow["timestamp"] = DateHelpers.ToUnixTime(DateTime.UtcNow);
                    contactRow["firstname"] = contactRequest.FirstName;
                    contactRow["lastname"] = contactRequest.LastName;
                    contactRow["cookiestack"] = contactRequest.CookieStack;

                    // insert row
                    var putResult = await ddbTable.PutItemAsync(contactRow, config);

                    return ApiGateway.GetResponseAsText($"OK {contactRequest.Email}", HttpStatusCode.OK);
                }
            }
            catch (Exception ex)
            {
                // Log to CloudWatch
                LogErrorDetails(logTag, ex);
#if DEBUG
                throw ex;
#else
                // Lambda error response
                return ApiGateway.GetResponseAsText(ex.Message, HttpStatusCode.BadRequest);
#endif
            }
        }

        /// <summary>
        /// A Lambda function to send email confirmation after Contact Form is submitted
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public APIGatewayProxyResponse SendConfirmation(APIGatewayProxyRequest request, ILambdaContext context)
        {
            var logTag = "ProcessContactFormAsync";
            _logger.Log("BEGIN", logTag);

            try
            {
                
            }
            catch (Exception ex)
            {
                // Log to CloudWatch
                LogErrorDetails(logTag, ex);
#if DEBUG
                throw ex;
#else
                // Lambda error response
                return ApiGateway.GetResponseAsText(ex.Message, HttpStatusCode.BadRequest);
#endif
            }
        }

        /// <summary>
        /// Loads the AWS credentials from the specified source
        /// </summary>
        /// <param name="config"></param>
        private void LoadConfig()
        {
            _logger.Log("LoadConfig::");

            IAwsConfigurationReader config;

#if DEBUG
            // use local configuration for DEV
            config = new LocalConfig();
#else
                // use AWS configuration for PROD
                config = new ServerConfig();
#endif

            _logger.Debug("LoadConfig::");
            _awsAccessKey = config.AccessKey;
            _awsSecretKey = config.SecretKey;
        }

        public void LogErrorDetails(string messageHeader, Exception ex)
        {
            _logger.Log(messageHeader);
            _logger.Log(ex.Message);

            if (ex.InnerException != null && !String.IsNullOrEmpty(ex.InnerException.Message))
            {
                _logger.Log(ex.InnerException.Message);
            }

            if (!String.IsNullOrEmpty(ex.StackTrace))
            {
                _logger.Log(ex.StackTrace);
            }
        }
    }
}
