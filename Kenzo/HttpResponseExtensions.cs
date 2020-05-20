using System.Net.Http;
using System.Threading.Tasks;

namespace Kenzo
{
    public delegate Task<HttpContent> RewriteContent(HttpContent upstreamContent);
    public static class HttpResponseExtensions
    {
        public static async Task<HttpResponseMessage> ReplaceContent(
            this HttpResponseMessage upstreamResponse, RewriteContent rewriteContent)
        {
            var response = new HttpResponseMessage();
            foreach (var (key, value) in upstreamResponse.Headers) response.Headers.Add(key, value);
            response.Content = await rewriteContent(upstreamResponse.Content);
            return response;
        }
    }
}