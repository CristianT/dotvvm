using System.Net;
using Microsoft.Owin;

namespace DotVVM.Framework.Hosting
{
    public class DotvvmAuthenticationHelper
    {
        /// <summary>
        /// Fixes the response created by the OWIN Security Challenge call to be accepted by DotVVM client library.
        /// </summary>
        public static void ApplyRedirectResponse(IOwinContext context, string redirectUri)
        {
            if (context.Response.StatusCode == (int)HttpStatusCode.Unauthorized)
            {
                DotvvmRequestContext.SetRedirectResponse(context, redirectUri, (int)HttpStatusCode.Redirect);
            }
        }
    }
}