using Amazon.Lambda.APIGatewayEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Finexus.IO.Utilities
{
    public class ApiGateway
    {
        /// <summary>
        /// Utility method to create an APIGatewayProxyResponse as JSON
        /// </summary>
        /// <param name="body"></param>
        /// <param name="statusCode"></param>
        /// <returns></returns>
        public static APIGatewayProxyResponse GetResponseAsJson(object response, HttpStatusCode statusCode, bool alreadySerialized = false)
        {
            var body = "";

            if (alreadySerialized)
            {
                body = response.ToString();
            }
            else
            {
                body = JsonConvert.SerializeObject(response,
                    new JsonSerializerSettings()
                    {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    });
            }

            return new APIGatewayProxyResponse()
            {
                Body = body,
                StatusCode = (int)statusCode,
                Headers = new Dictionary<string, string> {
                    { "Content-Type", "application/json" },
                    { "Access-Control-Allow-Origin", "*" }
                }
            };
        }

        /// <summary>
        /// Utility method to create an APIGatewayProxyResponse as text
        /// </summary>
        /// <param name="body"></param>
        /// <param name="statusCode"></param>
        /// <returns></returns>
        public static APIGatewayProxyResponse GetResponseAsText(string body, HttpStatusCode statusCode)
        {
            return new APIGatewayProxyResponse()
            {
                Body = body,
                StatusCode = (int)statusCode,
                Headers = new Dictionary<string, string> {
                    { "Content-Type", "text/plain" },
                    { "Access-Control-Allow-Origin", "*" }
                }
            };
        }
    }
}