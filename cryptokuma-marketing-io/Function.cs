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
using Amazon.DynamoDBv2.DocumentModel;
using Cryptokuma.Marketing.IO.Models;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using System.IO;
using Amazon.S3;
using Amazon.S3.Model;
using Cryptokuma.Marketing.IO.Utilities;

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

        private string _awsAccessKey;
        private string _awsSecretKey;
        protected const string REGION = "us-east-1";

        private string SEND_CONFIRMATION_LAMBDA_NAME = "";
        private string SEND_CONFIRMED_LAMBDA_NAME = "";
        private string CONTACT_FROM = "info@cryptokuma.com";
        private string CONFIRMATION_SUBJECT = "";
        private string CONFIRMED_SUBJECT = "";
        private string CONTACT_TABLE = "";
        private string BASE_URL = "";

        /// <summary>
        /// Default constructor that Lambda will invoke.
        /// </summary>
        public Functions()
        {
        }


        /// <summary>
        /// Lambda function to collect Contact Form fields and save to DynamoDB
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> ProcessContactFormAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            var logTag = "ProcessContactFormAsync";
            Console.WriteLine("BEGIN", logTag);
            Console.WriteLine("FULL REQUEST", logTag);
            Console.WriteLine(JsonConvert.SerializeObject(request));

            try
            {
                // load AWS credentials and env variables
                LoadConfig();
                LoadDynamoDBEnvironmentVars();

                // check optional request param(s)
                var noConfirm = false;
                if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey("noConfirm"))
                {
                    noConfirm = true;
                }

                var isWarmer = false;
                if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey("warmer"))
                {
                    isWarmer = true;
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
                }

                Contact contactRequest;

                if (isWarmer)
                {
                    contactRequest = new Contact
                    {
                        Email = "lambda-warmer@cryptokuma.com",
                        Interests = "[]",
                        Name = "Lambda Warmer"
                    };
                }
                else
                {
                    contactRequest = JsonConvert.DeserializeObject<Contact>(request.Body);
                }

                using (var dynamoClient = new AmazonDynamoDBClient(_awsAccessKey, _awsSecretKey, RegionEndpoint.GetBySystemName(REGION)))
                {
                    // init connection to DynamoDB table
                    Table ddbTable = Table.LoadTable(dynamoClient, CONTACT_TABLE);

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
                    contactRow["confirmed"] = "false";
                    contactRow["updatedAt"] = createdAt;

                    // insert row
                    var putResult = await ddbTable.PutItemAsync(contactRow, config);

                    // send Confirmation Email
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
#if DEBUG
                            var sendMessageResult = await SendConfirmationAsync(contactRequest);
                            _logger.Debug(sendMessageResult);
#else
                            var sendMessageResult = await lambdaClient.InvokeAsync(sendMessageRequest);

                            // get Worker response
                            if (!sendMessageResult.HttpStatusCode.Equals(HttpStatusCode.Accepted))
                            {
                                Console.WriteLine($"ERROR: {sendMessageResult.FunctionError}");

                                return ApiGateway.GetResponseAsText($"ERROR {contactRequest.Email}", HttpStatusCode.BadRequest);
                            }
#endif
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
        /// A Lambda function to send email to request Confirmation after the Contact Form is submitted
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<string> SendConfirmationAsync(Contact contact)
        {
            var logTag = "SendConfirmation";
            Console.WriteLine("BEGIN", logTag);

            try
            {
                // load AWS credentials
                LoadConfig();

                // load env vars
                LoadCommonContactEnvironmentVars();

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

                // load Confirmation template from S3
                var messageTemplate = "";
                using (var s3Client = new AmazonS3Client(_awsAccessKey, _awsSecretKey, RegionEndpoint.GetBySystemName(REGION)))
                {
                    var templateRequest = new GetObjectRequest
                    {
                        BucketName = "cryptokuma-marketing-templates",
                        Key = "confirmation.min.html"
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

                if (!String.IsNullOrEmpty(messageTemplate))
                {
                    // generate Email ciphertext based upon email address, then base64 encode it
                    var emailCipher = await KMS.EncryptStringAsync(contact.Email, _awsAccessKey, _awsSecretKey, REGION);
                    // poor man's Base64UrlEncoder.Encode (since Lambda dotnetcore version is still behind the rest of the world)
                    //var emailCipherBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(emailCipher));
                    string emailCipherBase64 = Base64UrlEncoder.Encode(emailCipher);

                    // substitute template vars: 
                    //  - _NAME_
                    //  - _EMAILCIPHER_
                    //  - _BASEURL_
                    messageTemplate = messageTemplate.Replace("_BASEURL_", BASE_URL).Replace("_NAME_", contact.Name).Replace("_EMAILCIPHER_", (emailCipherBase64));

                    // send confirmation email
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
                else
                {
                    return "NOT FOUND";
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

        /// <summary>
        /// Lambda Function that confirms the registered email address
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> ConfirmEmailAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            var logTag = "ConfirmEmailAsync";

            try
            {
                // load AWS credentials and environment variables
                LoadConfig();
                LoadDynamoDBEnvironmentVars();

                // get Email Ciphertext from path
                string id = "";
                if (request.PathParameters.ContainsKey("id"))
                {
                    id = request.PathParameters["id"];
                }
                else
                {
                    return ApiGateway.GetResponseAsJson("Not found", HttpStatusCode.BadRequest);
                }

                // check optional request param(s)
                var noConfirm = false;
                if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey("noConfirm"))
                {
                    noConfirm = true;
                }

                if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey("warmer"))
                {
                    return ApiGateway.GetResponseAsJson("WARMED", HttpStatusCode.OK);
                }

                // decrypt Email Ciphertext
                var emailCipher = Base64UrlEncoder.Decode(id);
                var emailPlainText = await KMS.DecryptString(emailCipher, _awsAccessKey, _awsSecretKey, REGION);
                _logger.Log(" => Got email");
                _logger.Log(emailPlainText);

                // update DynamoDB confirmed flag
                using (var dynamoClient = new AmazonDynamoDBClient(_awsAccessKey, _awsSecretKey, RegionEndpoint.GetBySystemName(REGION)))
                {
                    // init connection to DynamoDB table
                    Table ddbTable = Table.LoadTable(dynamoClient, CONTACT_TABLE);

                    // find matching row in DDB
                    QueryFilter queryFilter = new QueryFilter("email", QueryOperator.Equal, emailPlainText);
                    QueryOperationConfig queryConfig = new QueryOperationConfig()
                    {
                        Select = SelectValues.AllAttributes,
                        ConsistentRead = true,
                        Filter = queryFilter,
                        Limit = 1,
                        BackwardSearch = true
                    };

                    // get contact details from DDB
                    Search search = ddbTable.Query(queryConfig);
                    var contactDbResult = await search.GetNextSetAsync();
                    var contactItem = contactDbResult.FirstOrDefault();

                    if (contactDbResult.Count == 0 || contactItem == null)
                    {
                        return ApiGateway.GetResponseAsText("Not found", HttpStatusCode.BadRequest);
                    }

                    // init Contact object
                    Contact contactRequest = new Contact
                    {
                        Email = contactItem["email"],
                        Interests = contactItem["interests"],
                        Name = contactItem["name"]
                    };

                    // init DynamoDB PUT
                    PutItemOperationConfig config = new PutItemOperationConfig
                    {
                        ReturnValues = ReturnValues.AllOldAttributes
                    };

                    // init DynamoDB row; DEV: must set all values or they will be overwritten with null values
                    var contactRow = new Document();
                    var updatedAt = DateHelpers.ToUnixTime(DateTime.UtcNow);
                    contactRow["email"] = contactRequest.Email;
                    contactRow["timestamp"] = contactItem["timestamp"];     //DEV this really is CreatedAt
                    contactRow["name"] = contactRequest.Name;
                    contactRow["interests"] = contactRequest.Interests;
                    contactRow["confirmed"] = "true";
                    contactRow["updatedAt"] = updatedAt;

                    // update row
                    var putResult = await ddbTable.PutItemAsync(contactRow, config);

                    // send Confirmed Email 
                    if (!noConfirm)
                    {
                        // load environment variables
                        if (String.IsNullOrEmpty(SEND_CONFIRMED_LAMBDA_NAME))
                        {
                            var sendConfirmedLambda = System.Environment.GetEnvironmentVariable("SEND_CONFIRMED_LAMBDA_NAME");
                            if (String.IsNullOrEmpty(sendConfirmedLambda))
                            {
                                throw new NullReferenceException("SEND_CONFIRMED_LAMBDA_NAME environment variable is not defined");
                            }

                            SEND_CONFIRMED_LAMBDA_NAME = sendConfirmedLambda;

                            _logger.Debug($"SEND_CONFIRMED_LAMBDA_NAME: {SEND_CONFIRMED_LAMBDA_NAME}");
                        }

                        // invoke Lambda to send Confirmed email
                        using (var lambdaClient = new AmazonLambdaClient(_awsAccessKey, _awsSecretKey, RegionEndpoint.GetBySystemName(REGION)))
                        {
                            // init Lambda request
                            var sendMessageRequest = new InvokeRequest()
                            {
                                FunctionName = SEND_CONFIRMED_LAMBDA_NAME,
                                InvocationType = InvocationType.Event,
                                Payload = JsonConvert.SerializeObject(contactRequest)
                            };

                            // invoke Lambda
#if DEBUG
                            var sendMessageResult = await SendConfirmedAsync(contactRequest);
                            _logger.Debug(sendMessageResult);
#else
                            var sendMessageResult = await lambdaClient.InvokeAsync(sendMessageRequest);
                            // get Worker response
                            if (!sendMessageResult.HttpStatusCode.Equals(HttpStatusCode.Accepted))
                            {
                                _logger.Log($"ERROR: {sendMessageResult.FunctionError}");

                                return ApiGateway.GetResponseAsText($"ERROR {contactRequest.Email}", HttpStatusCode.BadRequest);
                            }
#endif
                        }
                    }

                    // init response payload
                    var confirmedResult = new ContactSubmitResult
                    {
                        Message = "CONFIRMED",
                        CreatedAt = updatedAt,
                        Email = contactRequest.Email
                    };

                    return ApiGateway.GetResponseAsJson(confirmedResult, HttpStatusCode.OK);
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
        /// Lambda Function which sends Confirmed Email to registered user after s/he confirms their email address
        /// </summary>
        /// <param name="contact"></param>
        /// <returns></returns>
        public async Task<string> SendConfirmedAsync(Contact contact)
        {
            var logTag = "SendConfirmedAsync";
            _logger.Log("BEGIN", logTag);

            try
            {
                // load AWS credentials
                LoadConfig();

                // load env vars
                LoadCommonContactEnvironmentVars();

                if (String.IsNullOrEmpty(CONFIRMED_SUBJECT))
                {
                    var confirmMessage = System.Environment.GetEnvironmentVariable("CONFIRMED_SUBJECT");
                    if (String.IsNullOrEmpty(confirmMessage))
                    {
                        throw new NullReferenceException("CONFIRMED_SUBJECT environment variable is not defined");
                    }

                    CONFIRMED_SUBJECT = confirmMessage;

                    _logger.Debug($"CONFIRMED_SUBJECT: {CONFIRMED_SUBJECT}");
                }

                // load Confirmed email template from S3
                var messageTemplate = "";
                using (var s3Client = new AmazonS3Client(_awsAccessKey, _awsSecretKey, RegionEndpoint.GetBySystemName(REGION)))
                {
                    var templateRequest = new GetObjectRequest
                    {
                        BucketName = "cryptokuma-marketing-templates",
                        Key = "confirmed.min.html"
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

                // substitute template vars: 
                //  - _NAME_
                //  - _EMAILCIPHER_
                //  - _BASEURL_
                messageTemplate = messageTemplate.Replace("_BASEURL_", BASE_URL).Replace("_NAME_", contact.Name);

                // send email
                var mailGunApi = new MailGunApi();
                var result = await mailGunApi.SendMail(contact, CONTACT_FROM, CONFIRMED_SUBJECT, messageTemplate, true);

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

        /// <summary>
        /// Loads environment variables used by Lambda Functions that interact with DynamoDB
        /// </summary>
        private void LoadDynamoDBEnvironmentVars()
        {
            if (String.IsNullOrEmpty(CONTACT_TABLE))
            {
                var contactTable = System.Environment.GetEnvironmentVariable("CONTACT_TABLE");
                if (String.IsNullOrEmpty(contactTable))
                {
                    throw new NullReferenceException("CONTACT_TABLE environment variable is not defined");
                }

                CONTACT_TABLE = contactTable;

                _logger.Debug($"CONTACT_TABLE: {CONTACT_TABLE}");
            }
        }

        /// <summary>
        /// Loads common environment variables used by Lambda Functions that send email
        /// </summary>
        private void LoadCommonContactEnvironmentVars()
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
            }

            if (String.IsNullOrEmpty(BASE_URL))
            {
                var baseUrl = System.Environment.GetEnvironmentVariable("BASE_URL");
                if (String.IsNullOrEmpty(baseUrl))
                {
                    throw new NullReferenceException("BASE_URL environment variable is not defined");
                }

                CONFIRMATION_SUBJECT = confirmSubject;
            }

            if (String.IsNullOrEmpty(CONFIRMATION_MESSAGE))
            {
                var confirmMessage = System.Environment.GetEnvironmentVariable("CONFIRMATION_MESSAGE");
                if (String.IsNullOrEmpty(confirmMessage))
                {
                    throw new NullReferenceException("CONFIRMATION_MESSAGE environment variable is not defined");
                }

                CONFIRMATION_MESSAGE = confirmMessage;
            }
        }

        /// <summary>
        /// Loads the AWS credentials from the specified source
        /// </summary>
        /// <param name="config"></param>
        private void LoadConfig()
        {
            Console.WriteLine("LoadConfig::");

            IAwsConfigurationReader config;

#if DEBUG
            // use local configuration for DEV
            config = new LocalConfig();
#else
                // use AWS configuration for PROD
                config = new ServerConfig();
#endif

            _awsAccessKey = config.AccessKey;
            _awsSecretKey = config.SecretKey;
        }

        public void LogErrorDetails(string messageHeader, Exception ex)
        {
            Console.WriteLine(messageHeader);
            Console.WriteLine(ex.Message);

            if (ex.InnerException != null && !String.IsNullOrEmpty(ex.InnerException.Message))
            {
                Console.WriteLine(ex.InnerException.Message);
            }

            if (!String.IsNullOrEmpty(ex.StackTrace))
            {
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
