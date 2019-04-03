using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;

namespace Roxyman.Extension
{
    public class OkExtension : IHttpActionResult
    {
        private string _value;
        public OkExtension(string value)
        {
            _value = value;
        }
        public Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage()
            {
                Content = new StringContent(_value, Encoding.UTF8, "application/json"),
            };
            response.StatusCode = HttpStatusCode.OK;

            return Task.FromResult(response);
        }
    }
}