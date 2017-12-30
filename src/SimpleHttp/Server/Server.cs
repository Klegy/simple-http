﻿using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttp
{
    /// <summary>
    /// HTTP server listener class.
    /// </summary>
    public static class HttpServer
    {
        /// <summary>
        /// Creates and starts a new instance of the http(s) server.
        /// </summary>
        /// <param name="port">The http/https URI listening port.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="onHttpRequestAsync">Action executed on HTTP request.</param>
        /// <param name="useHttps">True to add 'https://' prefix insteaad of 'http://'.</param>
        /// <returns>Server listening task.</returns>
        public static async Task ListenAsync(int port, CancellationToken token, Func<HttpListenerRequest, HttpListenerResponse, Task> onHttpRequestAsync, bool useHttps = false)
        {
            if (port < 0 || port > UInt16.MaxValue)
                throw new NotSupportedException($"The provided port value must in the range: [0..{UInt16.MaxValue}");

            var s = useHttps ? "s" : String.Empty;
            await ListenAsync($"http{s}://+:{port}/", token, onHttpRequestAsync);
        }

        /// <summary>
        /// Creates and starts a new instance of the http(s) / websocket server.
        /// </summary>
        /// <param name="httpListenerPrefix">The http/https URI listening prefix.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="onHttpRequestAsync">Action executed on HTTP request.</param>
        /// <returns>Server listening task.</returns>
        public static async Task ListenAsync(string httpListenerPrefix, CancellationToken token, Func<HttpListenerRequest, HttpListenerResponse, Task> onHttpRequestAsync)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token), "The provided token must not be null.");

            if (onHttpRequestAsync == null)
                throw new ArgumentNullException(nameof(onHttpRequestAsync), "The provided HTTP request/response action must not be null.");


            var listener = new HttpListener();
            try { listener.Prefixes.Add(httpListenerPrefix); }
            catch (Exception ex) { throw new ArgumentException("The provided prefix is not supported. Prefixes have the format: 'http(s)://+:(port)/'", ex); }

            try { listener.Start(); }
            catch (Exception ex) when ((ex as HttpListenerException)?.ErrorCode == 5)
            {
                throw new UnauthorizedAccessException($"The HTTP server can not be started, as the namespace reservation does not exist.\n" +
                                                       $"Please run (elevated): 'netsh http add urlacl url={httpListenerPrefix} user=\"Everyone\"'." +
                                                       $"\nRemarks:\n" +
                                                       $"  If using 'localhost', put 'delete' instead of 'add' and type the http prefix instead of 'localhost'.", ex);
            }

            using (var r = token.Register(() => listener.Close()))
            {
                bool shouldStop = false;
                while (!shouldStop)
                {
                    try
                    {
                        var ctx = await listener.GetContextAsync();

                        if (ctx.Request.IsWebSocketRequest)
                        {
                            ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                            ctx.Response.Close();
                        }
                        else
                            Task.Factory.StartNew(() => onHttpRequestAsync(ctx.Request, ctx.Response), TaskCreationOptions.None).Wait(0);
                    }
                    catch (Exception)
                    {
                        if (!token.IsCancellationRequested)
                            throw;
                    }
                    finally
                    {
                        if (token.IsCancellationRequested)
                            shouldStop = true;
                    }
                }
            }
        }

    }
}
