using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;
using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.APIGatewayEvents;

using Cryptokuma.Marketing.IO;
using Cryptokuma.Marketing.IO.Models;
using Finexus.IO.Utilities;

namespace Cryptokuma.Marketing.IO.Tests
{
    public class FunctionTest
    {
        protected Functions _lambda;
        protected TestLambdaContext _context;

        public FunctionTest()
        {
            _lambda = new Functions();
            _context = new TestLambdaContext();

            System.Environment.SetEnvironmentVariable("CONFIRMATION_SUBJECT", "Please confirm your email address");
            System.Environment.SetEnvironmentVariable("CONFIRMED_SUBJECT", "Confirmed.  Thanks!");
            System.Environment.SetEnvironmentVariable("CONTACT_TABLE", "marketing-contact-dev");
            System.Environment.SetEnvironmentVariable("SEND_CONFIRMATION_LAMBDA_NAME", "DUMMY");
            System.Environment.SetEnvironmentVariable("SEND_CONFIRMED_LAMBDA_NAME", "DUMMY");
            System.Environment.SetEnvironmentVariable("BASE_URL", "//dev.cryptokuma.com");
        }

        [Fact]
        public void ProcessContactForm()
        {
            // add optional noConfirm parameter to disable email confirmation
            var queryParams = new Dictionary<string, string>();
            //queryParams.Add("noConfirm", "");

            // generate an API Gateway Request
            var request = new APIGatewayProxyRequest
            {
                Resource = "/contact",
                Path = $"/contact",
                HttpMethod = "POST",
                Headers = null,
                QueryStringParameters = queryParams,
                PathParameters = { },
                StageVariables = null,
                RequestContext = { },
                Body = "{ \"Name\": \"Joe UnitTest\", \"Email\": \"clay@mandarincreativegroup.com\", \"Interests\": \"[true,false,true,false,true]\" }",
                IsBase64Encoded = false
            };

            var response = _lambda.ProcessContactFormAsync(request, _context).Result;
            Assert.NotNull(response);
        }

        [Fact]
        public void ConfirmEmail()
        {
            var emailId = "QVFJQ0FIalpuTy90UE0vV1dMR3BLSW94N1dDMHZLZldtdVhLdGVTd2R2WUxsajMwRmdId1kwUWhwbkNPV0JUcjZlRjlJRUZ6QUFBQWZEQjZCZ2txaGtpRzl3MEJCd2FnYlRCckFnRUFNR1lHQ1NxR1NJYjNEUUVIQVRBZUJnbGdoa2dCWlFNRUFTNHdFUVFNM2ZpSDBKYlFKRk15N1lQZEFnRVFnRGt5UmhFYk04OVdhUHNYTUlqb1NVdGtJelRTTDg1R3JId2pFbitxTVRvV3IrNys1ejhhSk5VYTdaMGJ3b01GeXRlZ0lhN2hwWFJETERFPQ";
            var pathParams = new Dictionary<string, string>();
            pathParams.Add("id", emailId);

            // add optional noConfirm parameter to disable email confirmation
            var queryParams = new Dictionary<string, string>();
            //queryParams.Add("noConfirm", "");

            // generate an API Gateway Request
            var request = new APIGatewayProxyRequest
            {
                Resource = "/confirm/{id}",
                Path = $"/confirm",
                HttpMethod = "POST",
                Headers = null,
                QueryStringParameters = queryParams,
                PathParameters = pathParams,
                StageVariables = null,
                RequestContext = { },
                Body = null,
                IsBase64Encoded = false
            };

            var response = _lambda.ConfirmEmailAsync(request, _context).Result;
            Assert.NotNull(response);
        }

        [Fact]
        public void SendConfirmation()
        {
            var contact = new Contact
            {
                Email = "clay@mandarincreativegroup.com",
                Name = "Joe UnitTest",
                Interests = "[]"
            };

            var response = _lambda.SendConfirmationAsync(contact).Result;
            Assert.NotNull(response);
        }

        [Fact]
        public void SendConfirmed()
        {
            var contact = new Contact
            {
                Email = "clay@mandarincreativegroup.com",
                Name = "Joe UnitTest",
                Interests = "[]"
            };

            var response = _lambda.SendConfirmedAsync(contact).Result;
            Assert.NotNull(response);
        }
    }
}
