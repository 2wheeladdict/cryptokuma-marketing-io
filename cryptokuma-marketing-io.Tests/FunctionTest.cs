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

            System.Environment.SetEnvironmentVariable("CONFIRMATION_SUBJECT", "Thanks!");
            System.Environment.SetEnvironmentVariable("CONFIRMATION_MESSAGE", "Got it...BOOM");
        }

        [Fact]
        public void ProcessContactForm()
        {
            // add optional noConfirm parameter to disable email confirmation
            var queryParams = new Dictionary<string, string>();
            queryParams.Add("noConfirm", "");

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
                Body = "{ \"FirstName\": \"Clay\", \"LastName\": \"Benoit\", \"Email\": \"clay@mandarincreativegroup.com\", \"CookieStack\": \"I am a cookie\", \"Interests\": \"[true,false,true,false,true]\" }",
                IsBase64Encoded = false
            };

            var response = _lambda.ProcessContactFormAsync(request, _context).Result;
            Assert.NotNull(response);
        }

        [Fact]
        public void SendConfirmation()
        {
            var contact = new Contact
            {
                CookieStack = "Monster",
                Email = "clay@mandarincreativegroup.com",
                FirstName = "Clay",
                LastName = "Bob"
            };

            var response = _lambda.SendConfirmationAsync(contact).Result;
            Assert.NotNull(response);
        }
    }
}
