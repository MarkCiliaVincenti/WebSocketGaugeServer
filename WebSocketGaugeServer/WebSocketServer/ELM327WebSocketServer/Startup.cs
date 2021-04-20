using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.WebSockets;
using System.Threading;
using Newtonsoft.Json;
using System.Text;
using System.IO;
using System.Net;
using SZ2.WebSocketGaugeServer.WebSocketServer.ELM327WebSocketServer.Service;
using SZ2.WebSocketGaugeServer.WebSocketServer.ELM327WebSocketServer.SessionItems;
using SZ2.WebSocketGaugeServer.WebSocketServer.WebSocketCommon.JSONFormat;
using SZ2.WebSocketGaugeServer.ECUSensorCommunication.ELM327;
using SZ2.WebSocketGaugeServer.WebSocketServer.WebSocketCommon;
using SZ2.WebSocketGaugeServer.WebSocketServer.WebSocketCommon.JSONFormat.ELM327;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;

namespace SZ2.WebSocketGaugeServer.WebSocketServer.ELM327WebSocketServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ELM327COMService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory, ILogger<Startup> logger)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            var webSocketOptions = new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromSeconds(120)
            };

            loggerFactory.AddMemory();

            // Handle WebSokect connection
            app.UseWebSockets(webSocketOptions);
            app.UseRouting();
            app.Use(async (context, next) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    var cancellationToken = lifetime.ApplicationStopping;
                    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await HandleHttpConnection(context, webSocket, logger, cancellationToken);
                }
                else
                {
                    await next();
                }
            });

            // Add static file with new extension mappings for bitmaptext fnt file
            var provider = new FileExtensionContentTypeProvider();
            provider.Mappings[".fnt"] = "text/xml";
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions{
                ContentTypeProvider = provider
            });
        }

        private async Task HandleHttpConnection(HttpContext context, WebSocket webSocket, ILogger logger, CancellationToken ct)
        {
            var service = (ELM327COMService)context.RequestServices.GetRequiredService(typeof(ELM327COMService));
            var connectionID = Guid.NewGuid();
            var destAddress = context.Connection.RemoteIpAddress;

            service.AddWebSocket(connectionID, webSocket);
            var sessionParam = service.GetSessionParam(connectionID);
            logger.LogInformation("Session is connected from : " + destAddress.ToString());

            while (webSocket.State == WebSocketState.Open)
            {
                await processReceivedMessage(webSocket, service, sessionParam, destAddress, logger, ct);
            }
            service.RemoveWebSocket(connectionID);
            if (webSocket.State == WebSocketState.CloseReceived || webSocket.State == WebSocketState.CloseSent)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed normally", ct);
                logger.LogInformation("Session is disconnected from : " + destAddress.ToString());
            }
            else
            {
                logger.LogInformation("Session is aborted. " + destAddress.ToString());
            }
        }

        private async Task processReceivedMessage(WebSocket ws, ELM327COMService service, ELM327WebsocketSessionParam sessionParam, IPAddress destAddress, ILogger logger,  CancellationToken ct)
        {
            // Get mode code
            try
            {
                var wsmessage = await ReceiveWebSocketMessageAsync(ws, ct);

                // Do nothing on closing message.
                if (wsmessage.MessageType == WebSocketMessageType.Close)
                    return;
                // Throw exception on non text message.
                if (wsmessage.MessageType != WebSocketMessageType.Text)
                    throw new InvalidDataException("Received websocket message type is not Text.");

                string message = wsmessage.TextContent;
                var msg_dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(message);
                string receivedJSONmode = msg_dict["mode"];

                switch (receivedJSONmode)
                {
                    // ELM327 COM all reset
                    case (ResetJSONFormat.ModeCode):
                        sessionParam.reset();
                        await send_response_msg(ws, "ELM327COM session RESET. All send parameters are disabled.", destAddress, logger, ct);
                        break;
                    case (ELM327COMReadJSONFormat.ModeCode):
                        var msg_obj_elm327read = JsonConvert.DeserializeObject<ELM327COMReadJSONFormat>(message);
                        msg_obj_elm327read.Validate();

                        var target_code = (OBDIIParameterCode)Enum.Parse(typeof(OBDIIParameterCode), msg_obj_elm327read.code);
                        bool flag = msg_obj_elm327read.flag;

                        if (msg_obj_elm327read.read_mode == ELM327COMReadJSONFormat.FastReadModeCode)
                        {
                            sessionParam.FastSendlist[target_code] = flag;
                        }
                        else
                        {
                            sessionParam.SlowSendlist[target_code] = flag;
                        }
                        await send_response_msg(ws, "ELM327COM session read flag for : " + target_code.ToString() + " read_mode :" + msg_obj_elm327read.read_mode + " set to : " + flag.ToString(), destAddress, logger, ct);
                        break;

                    case (ELM327SLOWREADIntervalJSONFormat.ModeCode):
                        var msg_obj_interval = JsonConvert.DeserializeObject<ELM327SLOWREADIntervalJSONFormat>(message);
                        msg_obj_interval.Validate();
                        service.ELM327COM.SlowReadInterval = msg_obj_interval.interval;

                        await send_response_msg(ws, "ELM327COM slowread interval to : " + msg_obj_interval.interval.ToString(), destAddress, logger, ct);
                        break;
                    default:
                        throw new JSONFormatsException("Unsuppoted mode property.");
                }
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex is JsonException || ex is JSONFormatsException || ex is NotSupportedException)
            {
                await send_error_msg(ws, ex.GetType().ToString() + " " + ex.Message, destAddress, logger, ct);
                logger.LogWarning(ex.Message);
                logger.LogWarning(ex.StackTrace);
            }
            catch (WebSocketException ex)
            {
                logger.LogWarning(ex.Message);
                logger.LogWarning(ex.StackTrace);
            }
            catch (InvalidDataException ex)
            {
                logger.LogWarning(ex.Message);
                logger.LogWarning(ex.StackTrace);
            }
            catch (OperationCanceledException ex)
            {
                logger.LogInformation(ex.Message);
            }
        }

        protected async Task send_error_msg(WebSocket ws, string message, IPAddress destAddress, ILogger logger, CancellationToken ct)
        {
            ErrorJSONFormat json_error_msg = new ErrorJSONFormat();
            json_error_msg.msg = message;

            logger.LogError("Send Error message to " + destAddress.ToString() + " : " + message);            
            await SendWebSocketTextAsync(ws, json_error_msg.Serialize(), ct);           
        }

        protected async Task send_response_msg(WebSocket ws, string message, IPAddress destAddress, ILogger logger, CancellationToken ct)
        {
            ResponseJSONFormat json_response_msg = new ResponseJSONFormat();
            json_response_msg.msg = message;
            
            logger.LogInformation("Send Response message to " + destAddress.ToString() + " : " + message);
            await SendWebSocketTextAsync(ws, json_response_msg.Serialize(), ct);
        }

        private async Task SendWebSocketTextAsync(WebSocket webSocket, string text, CancellationToken ct)
        {
            byte[] sendBuf = Encoding.UTF8.GetBytes(text);
            await webSocket.SendAsync(new ArraySegment<byte>(sendBuf, 0, sendBuf.Length), WebSocketMessageType.Text, true, ct);
        }

        private async Task<WebSocketMessage> ReceiveWebSocketMessageAsync(WebSocket webSocket, CancellationToken ct)
        {
            var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
            WebSocketReceiveResult result = null;

            using (var ms = new MemoryStream())
            {
                do
                {
                    result = await webSocket.ReceiveAsync(buffer, ct);
                    ms.Write(buffer.Array, buffer.Offset, result.Count);
                } while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);
                switch (result.MessageType)
                {
                    case WebSocketMessageType.Text:
                        string returnStr;
                        using (var reader = new StreamReader(ms, Encoding.UTF8))
                        {
                            returnStr = reader.ReadToEnd();
                        }
                        return WebSocketMessage.CreateTextMessage(returnStr);
                    case WebSocketMessageType.Binary:
                        throw new NotSupportedException("Binary mode websocketmessage is curently not supprted.");
                    case WebSocketMessageType.Close:
                        return WebSocketMessage.CreateCloseMessage();
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
    }
}
