using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotVVM.Framework.Compilation.Parser;
using Microsoft.AspNet.WebUtilities;
using Microsoft.Owin;
using Newtonsoft.Json;
using DotVVM.Framework.Configuration;
using DotVVM.Framework.Controls;
using DotVVM.Framework.Runtime;
using DotVVM.Framework.Storage;

namespace DotVVM.Framework.Hosting
{
    public class DotvvmFileUploadMiddleware : OwinMiddleware
    {
        private static readonly Regex baseMimeTypeRegex = new Regex(@"/.*$");
        private static readonly Regex wildcardMimeTypeRegex = new Regex(@"/\*$");
        private readonly DotvvmConfiguration configuration;


        public DotvvmFileUploadMiddleware(OwinMiddleware next, DotvvmConfiguration configuration) : base(next)
        {
            this.configuration = configuration;
        }

        public override Task Invoke(IOwinContext context)
        {
            var url = DotvvmMiddleware.GetCleanRequestUrl(context);
            
            // file upload handler
            if (url == HostingConstants.FileUploadHandlerMatchUrl || url.StartsWith(HostingConstants.FileUploadHandlerMatchUrl  + "?", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessMultipartRequest(context);
            }
            else
            {
                return Next.Invoke(context);
            }
        }

        private async Task ProcessMultipartRequest(IOwinContext context)
        {
            // verify the request
            var isPost = context.Request.Method == "POST";
            if (isPost && !context.Request.ContentType.StartsWith("multipart/form-data", StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            var uploadedFiles = new List<UploadedFile>();
            var errorMessage = "";
            if (isPost)
            {
                try
                {
                    // get the boundary
                    var boundary = Regex.Match(context.Request.ContentType, @"boundary=""?(?<boundary>[^\n\;\"" ]*)").Groups["boundary"];
                    if (!boundary.Success || string.IsNullOrWhiteSpace(boundary.Value))
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        return;
                    }

                    // parse request and save files
                    await SaveFiles(context, boundary, uploadedFiles);
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }
            }

            // return the response
            await RenderResponse(context, isPost, errorMessage, uploadedFiles);
        }

		private bool ShouldReturnJsonResponse(IOwinContext context) =>
			context.Request.Headers.Get(HostingConstants.DotvvmFileUploadAsyncHeaderName) == "true" ||
			context.Request.Query.Get("returnJson") == "true";

        private async Task RenderResponse(IOwinContext context, bool isPost, string errorMessage, List<UploadedFile> uploadedFiles)
        {
            var outputRenderer = configuration.ServiceLocator.GetService<IOutputRenderer>();
            if (isPost && ShouldReturnJsonResponse(context))
            {
                // modern browser - return JSON
                if (string.IsNullOrEmpty(errorMessage))
                {
                    await outputRenderer.RenderPlainJsonResponse(context, uploadedFiles);
                }
                else
                {
                    await outputRenderer.RenderPlainTextResponse(context, errorMessage);
                    context.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                }
            }
            else
            {
                // old browser - return HTML
                var template = new FileUploadPageTemplate();
                template.FormPostUrl = DotvvmRequestContext.TranslateVirtualPath("~/" + HostingConstants.FileUploadHandlerMatchUrl, context);
                template.AllowMultipleFiles = context.Request.Query["multiple"] == "true";

                if (isPost)
                {
                    if (string.IsNullOrEmpty(errorMessage))
                    {
                        template.StartupScript = string.Format("reportProgress(false, 100, {0})",
                            JsonConvert.SerializeObject(uploadedFiles));
                    }
                    else
                    {
                        template.StartupScript = string.Format("reportProgress(false, 100, {0})",
                            JsonConvert.SerializeObject(errorMessage));
                    }
                }
                await outputRenderer.RenderHtmlResponse(context, template.TransformText());
            }
        }

        private async Task SaveFiles(IOwinContext context, Group boundary, List<UploadedFile> uploadedFiles)
        {
            // get the file store
            var fileStore = configuration.ServiceLocator.GetService<IUploadedFileStorage>();

            // parse the stream
            var multiPartReader = new MultipartReader(boundary.Value, context.Request.Body);
            MultipartSection section;
            while ((section = await multiPartReader.ReadNextSectionAsync()) != null)
            {
                // process the section
                var result = await StoreFile(context, section, fileStore);
                if (result != null)
                {
                    uploadedFiles.Add(result);
                }
            }
        }

        /// <summary>
        /// Stores the file and returns an object that will be sent to the client.
        /// </summary>
        private async Task<UploadedFile> StoreFile(IOwinContext context, MultipartSection section, IUploadedFileStorage fileStore)
        {
            var fileId = await fileStore.StoreFile(section.Body);
            var fileNameGroup = Regex.Match(section.ContentDisposition, @"filename=""?(?<fileName>[^\""]*)", RegexOptions.IgnoreCase).Groups["fileName"];
            var fileName = fileNameGroup.Success ? fileNameGroup.Value : string.Empty;
            var mimeType = section.ContentType ?? string.Empty;

            return new UploadedFile()
            {
                FileId = fileId,
                FileName = fileName,
                Accepted = IsAccepted(context, fileName, mimeType)
            };
        }

        private bool IsAccepted(IOwinContext context, string fileName, string mimeType)
        {
            var accept = context.Request.Query["accept"];

            if (string.IsNullOrEmpty(accept))
            {
                return true;
            }

            return accept.Split(',').Any(type =>
            {
                type = type.Trim();

                if (type.StartsWith("."))
                {
                    return string.Equals(type, Path.GetExtension(fileName), StringComparison.InvariantCultureIgnoreCase);
                }

                if (wildcardMimeTypeRegex.IsMatch(type))
                {
                    var baseMimeType = baseMimeTypeRegex.Replace(mimeType, string.Empty);
                    return baseMimeType == baseMimeTypeRegex.Replace(type, string.Empty);
                }

                if (mimeType.Length > 0)
                {
                    return type == mimeType;
                }

                return false;
            });
        }
    }
}
