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
using Cryptokuma.Marketing.IO.Models;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using System.IO;
using Amazon.S3;
using Amazon.S3.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace Cryptokuma.Marketing.IO
{
    public class ContactSubmitResult
    {
        public string Message { get; set; }
        public string Email { get; set; }
        public long CreatedAt { get; set; }
    }

    public class Functions
    {
        private const string TAG = "FunctionHandler";
        private Logger _logger = new Logger(TAG);

        private string _awsAccessKey;
        private string _awsSecretKey;
        protected const string REGION = "us-east-1";

        private string SEND_CONFIRMATION_LAMBDA_NAME = "";
        private string CONTACT_FROM = "info@cryptokuma.com";
        private string CONFIRMATION_SUBJECT = "";
        private string CONFIRMATION_MESSAGE = "";

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
            _logger.Log("FULL REQUEST", logTag);
            _logger.Log(JsonConvert.SerializeObject(request));

            try
            {
                // load AWS credentials
                LoadConfig();

                // check optional request param(s)
                var noConfirm = false;
                if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey("noConfirm"))
                {
                    noConfirm = true;
                }

                // load environment variables
                if (!noConfirm && String.IsNullOrEmpty(SEND_CONFIRMATION_LAMBDA_NAME))
                {
                    var sendConfirmationLambda = System.Environment.GetEnvironmentVariable("SEND_CONFIRMATION_LAMBDA_NAME");
                    if (String.IsNullOrEmpty(sendConfirmationLambda))
                    {
                        throw new NullReferenceException("SEND_CONFIRMATION_LAMBDA_NAME environment variable is not defined");
                    }

                    SEND_CONFIRMATION_LAMBDA_NAME = sendConfirmationLambda;

                    _logger.Debug($"SEND_CONFIRMATION_LAMBDA_NAME: {SEND_CONFIRMATION_LAMBDA_NAME}");
                }

                var contactRequest = JsonConvert.DeserializeObject<Contact>(request.Body);

                using (var dynamoClient = new AmazonDynamoDBClient(_awsAccessKey, _awsSecretKey, RegionEndpoint.GetBySystemName(REGION)))
                {
                    // init connection to DynamoDB table
                    _logger.Log($"Initializing connection to DynamoDB Contact table");
                    Table ddbTable = Table.LoadTable(dynamoClient, "marketing-contact");

                    // check for duplicate email, return NoContent response
                    QueryFilter dupCheckerFilter = new QueryFilter("email", QueryOperator.Equal, contactRequest.Email);
                    QueryOperationConfig dupCheckerConfig = new QueryOperationConfig()
                    {
                        Select = SelectValues.SpecificAttributes,
                        AttributesToGet = new List<string> { "email", "timestamp" },
                        ConsistentRead = true,
                        Filter = dupCheckerFilter,
                        Limit = 1,
                        BackwardSearch = true
                    };
                    Search search = ddbTable.Query(dupCheckerConfig);
                    var dupCheckResult = await search.GetNextSetAsync();

                    if (dupCheckResult.Count > 0)
                    {
                        var contactItem = dupCheckResult.FirstOrDefault();

                        if (contactItem == null)
                        {
                            return ApiGateway.GetResponseAsText("Not found", HttpStatusCode.BadRequest);
                        }

                        // init response payload
                        var existingContactResult = new ContactSubmitResult
                        {
                            Message = "EXISTS",
                            CreatedAt = contactItem["timestamp"].AsLong(),
                            Email = contactItem["email"]
                        };

                        return ApiGateway.GetResponseAsJson(existingContactResult, HttpStatusCode.OK);
                    }

                    // init DynamoDB PUT
                    PutItemOperationConfig config = new PutItemOperationConfig
                    {
                        ReturnValues = ReturnValues.AllOldAttributes
                    };

                    // init DynamoDB row
                    var contactRow = new Document();
                    var createdAt = DateHelpers.ToUnixTime(DateTime.UtcNow);
                    contactRow["email"] = contactRequest.Email;
                    contactRow["timestamp"] = createdAt;
                    contactRow["name"] = contactRequest.Name;
                    contactRow["interests"] = contactRequest.Interests;
                    contactRow["confirmed"] = "false";  //TODO CL-50-13
                    contactRow["cookiestack"] = contactRequest.CookieStack;

                    // insert row
                    var putResult = await ddbTable.PutItemAsync(contactRow, config);

                    // send email confirmation
                    if (!noConfirm)
                    {
                        using (var lambdaClient = new AmazonLambdaClient(_awsAccessKey, _awsSecretKey, RegionEndpoint.GetBySystemName(REGION)))
                        {
                            // init Lambda request
                            var sendMessageRequest = new InvokeRequest()
                            {
                                FunctionName = SEND_CONFIRMATION_LAMBDA_NAME,
                                InvocationType = InvocationType.Event,
                                Payload = request.Body
                            };

                            // invoke Lambda
                            var sendMessageResult = await lambdaClient.InvokeAsync(sendMessageRequest);

#if DEBUG
                            // DEBUG Worker response
                            string sendMessageRawResult;
                            using (var sr = new StreamReader(sendMessageResult.Payload))
                            {
                                sendMessageRawResult = sr.ReadToEnd();
                            }
#endif
                            // get Worker response
                            if (!sendMessageResult.HttpStatusCode.Equals(HttpStatusCode.Accepted))
                            {
                                _logger.Log($"ERROR: {sendMessageResult.FunctionError}");

                                return ApiGateway.GetResponseAsText($"ERROR {contactRequest.Email}", HttpStatusCode.BadRequest);
                            }
                        }
                    }

                    // init response payload
                    var newContactResult = new ContactSubmitResult
                    {
                        Message = "OK",
                        CreatedAt = createdAt,
                        Email = contactRequest.Email
                    };

                    return ApiGateway.GetResponseAsJson(newContactResult, HttpStatusCode.OK);
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
        public async Task<string> SendConfirmationAsync(Contact contact)
        {
            var logTag = "SendConfirmation";
            _logger.Log("BEGIN", logTag);

            try
            {
                // load AWS credentials
                LoadConfig();

                // load env vars
                LoadConfirmationEnvironmentVars();

                var messageTemplate = "";
                using (var s3Client = new AmazonS3Client(_awsAccessKey, _awsSecretKey, RegionEndpoint.GetBySystemName(REGION)))
                {
                    var templateRequest = new GetObjectRequest
                    {
                        BucketName = "cryptokuma-marketing-templates",
                        Key = "confirmation.html"
                    };

                    var templateResponse = await s3Client.GetObjectAsync(templateRequest);
                    using (Stream responseStream = templateResponse.ResponseStream)
                    {
                        using (StreamReader reader = new StreamReader(responseStream))
                        {
                            messageTemplate = reader.ReadToEnd();
                        }
                    }
                }

                var mailGunApi = new MailGunApi();
                var result = await mailGunApi.SendMail(contact, CONTACT_FROM, CONFIRMATION_SUBJECT, messageTemplate, true);
                if (result.IsSuccessStatusCode)
                {
                    return "OK";
                }
                else
                {
                    return "ERROR";
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
                 return "ERROR";
#endif
            }
        }

        private void LoadConfirmationEnvironmentVars()
        {
            // load environment variables
            if (String.IsNullOrEmpty(CONTACT_FROM))
            {
                var contactFrom = System.Environment.GetEnvironmentVariable("CONTACT_FROM");
                if (String.IsNullOrEmpty(contactFrom))
                {
                    throw new NullReferenceException("CONTACT_FROM environment variable is not defined");
                }

                CONTACT_FROM = contactFrom;

                _logger.Debug($"CONTACT_FROM: {CONTACT_FROM}");

            }

            if (String.IsNullOrEmpty(CONFIRMATION_SUBJECT))
            {
                var confirmSubject = System.Environment.GetEnvironmentVariable("CONFIRMATION_SUBJECT");
                if (String.IsNullOrEmpty(confirmSubject))
                {
                    throw new NullReferenceException("CONFIRMATION_SUBJECT environment variable is not defined");
                }

                CONFIRMATION_SUBJECT = confirmSubject;

                _logger.Debug($"CONFIRMATION_SUBJECT: {CONFIRMATION_SUBJECT}");
            }

            if (String.IsNullOrEmpty(CONFIRMATION_MESSAGE))
            {
                var confirmMessage = System.Environment.GetEnvironmentVariable("CONFIRMATION_MESSAGE");
                if (String.IsNullOrEmpty(confirmMessage))
                {
                    throw new NullReferenceException("CONFIRMATION_MESSAGE environment variable is not defined");
                }

                CONFIRMATION_MESSAGE = confirmMessage;

                _logger.Debug($"CONFIRMATION_MESSAGE: {CONFIRMATION_MESSAGE}");
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
