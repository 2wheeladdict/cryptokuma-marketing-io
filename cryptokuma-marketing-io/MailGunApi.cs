using Cryptokuma.Marketing.IO.Models;
using Finexus.IO.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Net;

namespace Cryptokuma.Marketing.IO
{
    public class MailGunApi
    {
        /// <summary>
        /// Initializes a new API instance to the MailGun URL
        /// </summary>
        /// <param name="handler"></param>
        public MailGunApi()
        {

        }

        public async Task<HttpResponseMessage> SendMail(Contact contact, string fromEmail, string subject, string message)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    var apiKeyBytes = System.Text.Encoding.UTF8.GetBytes("api:key-3e12e822df5da30736e6ff6fdcdcf510");

                    httpClient.BaseAddress = new Uri("https://api.mailgun.net");
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {System.Convert.ToBase64String(apiKeyBytes)}");

                    var queryString = $"from={fromEmail}&to={contact.Email}&subject={subject}!&text={message}";
                    var result = await httpClient.PostAsync($"v3/mg.cryptokuma.com/messages?{queryString}", new StringContent(""));

                    return result;
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                throw ex;
#else
                Console.WriteLine($"Error in SendMail");
                Console.WriteLine(ex.Message);

                if (ex.InnerException != null)
                {
                    Console.WriteLine(ex.InnerException.Message);
                }

                return null;
#endif
            }
        }
    }
}
