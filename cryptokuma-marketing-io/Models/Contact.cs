using System;
using System.Collections.Generic;
using System.Text;

namespace Cryptokuma.Marketing.IO.Models
{
    public class Contact
    {
        public Contact()
        {
        }

        public string Email { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string CookieStack { get; set; }
        public string Interests { get; set; }
    }
}
