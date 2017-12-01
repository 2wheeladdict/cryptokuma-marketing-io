using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;
using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.APIGatewayEvents;

using Cryptokuma.Marketing.IO;

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
        }

        [Fact]
        public void ProcessContactForm()
        {
            // generate an API Gateway Request
            var request = new APIGatewayProxyRequest
            {
                Resource = "/contact",
                Path = $"/contact",
                HttpMethod = "POST",
                Headers = null,
                QueryStringParameters = { },
                PathParameters = { },
                StageVariables = null,
                RequestContext = { },
                Body = "{ \"FirstName\": \"Clay\", \"LastName\": \"Benoit\", \"Email\": \"clay@mandarincreativegroup.com\", \"CookieStack\": \"I am a cookie\" }",
                IsBase64Encoded = false
            };

            var response = _lambda.ProcessContactFormAsync(request, _context).Result;
            Assert.NotNull(response);
        }
    }
}
