using System;
using System.IO;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Transport.Mqtt;
using Microsoft.Azure.Devices.Shared; 
using Newtonsoft.Json;


namespace EventProducerModule
{
    class Program
    {
        static int counter;
        static int eventFrequency;
        static string ignoreDefectCode;

        static int messageCountThreshold = -1;

        static void Main(string[] args)
        {

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();

            //Initialize the module client and handlers
            Init(cts).Wait();

            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init(CancellationTokenSource cts)
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            // Register callback to be called when a message is received by the module
            // await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);

            var moduleTwin = await ioTHubModuleClient.GetTwinAsync();
            await OnDesiredPropertiesUpdate(moduleTwin.Properties.Desired, ioTHubModuleClient);
            // Attach a callback for updates to the module twin's desired properties.
            await ioTHubModuleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdate, null);

            //Attach a callback for updates to the device twin's desired properties.

            // Register a callback for messages that are received by the module. Messages received on the inputFromSensor endpoint are sent to the FilterMessages method.
            await ioTHubModuleClient.SetInputMessageHandlerAsync("inputFromSensor", FilterMessages, ioTHubModuleClient);

            await ioTHubModuleClient.SetInputMessageHandlerAsync("control", ControlMessageHandle, ioTHubModuleClient);

            List<Task> tasks = new List<Task>();
            tasks.Add(EventDispatcher.SendEvents(ioTHubModuleClient, messageCountThreshold, new TimeSpan(0, 0, 10), cts));
            tasks.Add(EventDispatcher.SendAlerts(ioTHubModuleClient, messageCountThreshold, new TimeSpan(0, 1, 0), cts));

            Task.WaitAll(tasks.ToArray());
        }

        static Task OnDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            try
            {
                Console.WriteLine("Desired property change:");
                Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

                if (desiredProperties["EventFrequency"]!=null)
                    eventFrequency = desiredProperties["EventFrequency"];
                
                if (desiredProperties["IgnoreDefectCode"]!=null)
                    ignoreDefectCode = desiredProperties["IgnoreDefectCode"];

                if (desiredProperties["MessageCountThreshold"]!=null)
                {
                    messageCountThreshold = desiredProperties["MessageCountThreshold"];
                    Console.WriteLine($"MessageCountThreshold: {messageCountThreshold}");
                }

                    

            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error when receiving desired property: {0}", exception);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Error when receiving desired property: {0}", ex.Message);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> FilterMessages(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);
            try 
            {
                var moduleClient = userContext as ModuleClient;
                if (moduleClient == null)
                {
                    throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
                }

                byte[] messageBytes = message.GetBytes();
                string messageString = Encoding.UTF8.GetString(messageBytes);
                Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");
                // Get the message body.
                var messageBody = JsonConvert.DeserializeObject<PaintEvent>(messageString);
                if (messageBody != null && messageBody.DefectCode != ignoreDefectCode)
                {
                    Console.WriteLine($"Paint event generated with a valid defect code {messageBody.DefectCode}");
                    using (var filteredMessage = new Message(messageBytes))
                    {
                        foreach (KeyValuePair<string, string> prop in message.Properties)
                        {
                            filteredMessage.Properties.Add(prop.Key, prop.Value);
                        }

                        filteredMessage.Properties.Add("MessageType", "Alert");
                        await moduleClient.SendEventAsync("output1", filteredMessage);
                    }
                }
                // Indicate that the message treatment is completed.
                return MessageResponse.Completed;
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error in sample: {0}", exception);
                }
                // Indicate that the message treatment is not completed.
                var moduleClient = (ModuleClient)userContext;
                return MessageResponse.Abandoned;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Error in sample: {0}", ex.Message);
                // Indicate that the message treatment is not completed.
                ModuleClient moduleClient = (ModuleClient)userContext;
                return MessageResponse.Abandoned;
            }
        }

        // Control Message expected to be:
        // {
        //     "command" : "reset"
        // }
        static Task<MessageResponse> ControlMessageHandle(Message message, object userContext)
        {
            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);

            Console.WriteLine($"Received message Body: [{messageString}]");

            try
            {
                var messages = JsonConvert.DeserializeObject<ControlCommand[]>(messageString);

                foreach (ControlCommand messageBody in messages)
                {
                    if (messageBody.Command == ControlCommandEnum.Reset)
                    {
                        Console.WriteLine("Resetting temperature sensor..");
                        EventDispatcher.HandleCommand(ControlCommandEnum.Reset);
                    }
                }
            }
            catch (JsonSerializationException)
            {
                var messageBody = JsonConvert.DeserializeObject<ControlCommand>(messageString);

                if (messageBody.Command == ControlCommandEnum.Reset)
                {
                    Console.WriteLine("Resetting temperature sensor..");
                    EventDispatcher.HandleCommand(ControlCommandEnum.Reset);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Failed to deserialize control command with exception: [{ex}]");
            }

            return Task.FromResult(MessageResponse.Completed);
        }

    }
}
