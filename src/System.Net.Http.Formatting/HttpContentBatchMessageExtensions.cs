// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Net.Http.Formatting.Parsers;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;

namespace System.Net.Http
{
    public static class HttpContentBatchMessageExtensions
    {
        private const int DefaultBufferSize = 32 * 1024;

        public static async Task<HttpRequestMessage> ReadAsBatchHttpRequestMessageAsync(this HttpContent content)
        {
            Stream stream = await content.ReadAsStreamAsync();
            HttpUnsortedRequest httpRequest = new HttpUnsortedRequest();
            HttpRequestHeaderParser parser = new HttpRequestHeaderParser(httpRequest, HttpRequestHeaderParser.DefaultMaxRequestLineSize, HttpRequestHeaderParser.DefaultMaxHeaderSize);
            ParserState parseStatus;

            byte[] buffer = new byte[DefaultBufferSize];
            int bytesRead = 0;
            int headerConsumed = 0;

            while (true)
            {
                try
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
                catch (Exception e)
                {
                    throw new IOException(Properties.Resources.HttpMessageErrorReading, e);
                }

                try
                {
                    parseStatus = parser.ParseBuffer(buffer, bytesRead, ref headerConsumed);
                }
                catch (Exception)
                {
                    parseStatus = ParserState.Invalid;
                }

                if (parseStatus == ParserState.Done)
                {
                    return CreateHttpRequestMessage(httpRequest, stream, bytesRead - headerConsumed);
                }
                else if (parseStatus != ParserState.NeedMoreData)
                {
                    throw Error.InvalidOperation(Properties.Resources.HttpMessageParserError, headerConsumed, buffer);
                }
                else if (bytesRead == 0)
                {
                    throw new IOException(Properties.Resources.ReadAsHttpMessageUnexpectedTermination);
                }
            }
        }

        /// <summary>
        /// Creates an <see cref="HttpRequestMessage"/> based on information provided in <see cref="HttpUnsortedRequest"/>.
        /// </summary>
        /// <param name="httpRequest">The unsorted HTTP request.</param>
        /// <param name="contentStream">The input <see cref="Stream"/> used to form any <see cref="HttpContent"/> being part of this HTTP request.</param>
        /// <param name="rewind">Start location of any request entity within the <paramref name="contentStream"/>.</param>
        /// <returns>A newly created <see cref="HttpRequestMessage"/> instance.</returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "caller becomes owner.")]
        private static HttpRequestMessage CreateHttpRequestMessage(HttpUnsortedRequest httpRequest, Stream contentStream, int rewind)
        {
            Contract.Assert(httpRequest != null, "httpRequest must be non null");
            Contract.Assert(contentStream != null, "contentStream must be non null");

            HttpRequestMessage httpRequestMessage = new HttpRequestMessage();

            // Set method, requestURI, and version
            httpRequestMessage.Method = httpRequest.Method;
            httpRequestMessage.RequestUri = CreateRequestUri(httpRequest);
            httpRequestMessage.Version = httpRequest.Version;

            // Set the header fields and content if any
            httpRequestMessage.Content = CreateHeaderFields(httpRequest.HttpHeaders, httpRequestMessage.Headers, contentStream, rewind);

            return httpRequestMessage;
        }

        private static Uri CreateRequestUri(HttpUnsortedRequest httpRequest)
        {
            Contract.Assert(httpRequest != null, "httpRequest cannot be null.");

            // We don't use UriBuilder as hostValues.ElementAt(0) contains 'host:port' and UriBuilder needs these split out into separate host and port.
            string requestUri = String.Format(CultureInfo.InvariantCulture, "http://localhost{0}", httpRequest.RequestUri);
            return new Uri(requestUri);
        }

        private static HttpContent CreateHeaderFields(HttpHeaders source, HttpHeaders destination, Stream contentStream, int rewind)
        {
            Contract.Assert(source != null, "source headers cannot be null");
            Contract.Assert(destination != null, "destination headers cannot be null");
            Contract.Assert(contentStream != null, "contentStream must be non null");
            HttpContentHeaders contentHeaders = null;
            HttpContent content = null;

            // Set the header fields
            foreach (KeyValuePair<string, IEnumerable<string>> header in source)
            {
                if (!destination.TryAddWithoutValidation(header.Key, header.Value))
                {
                    if (contentHeaders == null)
                    {
                        contentHeaders = FormattingUtilities.CreateEmptyContentHeaders();
                    }

                    contentHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // If we have content headers then create an HttpContent for this Response
            if (contentHeaders != null)
            {
                // Need to rewind the input stream to be at the position right after the HTTP header
                // which we may already have parsed as we read the content stream.
                if (!contentStream.CanSeek)
                {
                    throw Error.InvalidOperation(Properties.Resources.HttpMessageContentStreamMustBeSeekable, "ContentReadStream", FormattingUtilities.HttpResponseMessageType.Name);
                }

                contentStream.Seek(0 - rewind, SeekOrigin.Current);
                content = new StreamContent(contentStream);
                contentHeaders.CopyTo(content.Headers);
            }

            return content;
        }
    }
}
