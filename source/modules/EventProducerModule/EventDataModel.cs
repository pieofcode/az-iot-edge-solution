using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Transport.Mqtt;
using Microsoft.Azure.Devices.Shared; 
using Newtonsoft.Json;
using Bogus;

namespace EventProducerModule
{
    public abstract class MESBaseEvent
    {
        public DateTime EventDate { get; set; }

        public string AssemblyLine { get; set; }

        public string Part { get; set; }

        public int Shift { get; set; }
    
    }

    public class PaintEvent : MESBaseEvent 
    {
        public string DefectCode { get; set; }
        public string OccurrencePhase { get; set; }

    }

    public class OperationAlert
    {
        public string AssemblyLine { get; set; }
        public double Temperature { get; set; }
        public DateTime EventDate { get; set; }
    }

    public enum ControlCommandEnum
    {
        Reset = 0,
        NoOperation = 1,
        ChangePart = 2,
        ChangeColor = 3
    }

    class ControlCommand
    {
        public ControlCommandEnum Command { get; set; }
    }

    public static class EventDispatcher
    {
        static readonly Guid BatchId = Guid.NewGuid();
        static bool Reset = false;
        static readonly Random Rnd = new Random();
        static bool sendData = true;
        static int counter = 1;

        public static void HandleCommand(ControlCommandEnum command)
        {
            Console.WriteLine($"Complete handling command: {command}");
        }

        /// <summary>
        /// Module behavior:
        ///        Sends data periodically (with default frequency of 5 seconds).
        ///        Data trend:
        ///         - Machine Temperature regularly rises from 21C to 100C in regularly with jitter
        ///         - Machine Pressure correlates with Temperature 1 to 10psi
        ///         - Ambient temperature stable around 21C
        ///         - Humidity is stable with tiny jitter around 25%
        ///                Method for resetting the data stream.
        /// </summary>
        public static async Task SendEvents(
            ModuleClient moduleClient,
            int messageCount,
            TimeSpan messageDelay,
            CancellationTokenSource cts)
        {
            var occurrencePhases = new string[] { "POST_MOLD", "PRE_PAINT", "POST_PAINT", "POST_ASSEMBLY" };
            var parts = new string[] {"MXP-AX-0830", "MXP-BY-0380", "MXP-CZ-9800"};
            var defectCodes = new string[] { 
                "", "", "", "", "",
                "PD0", "PD1", "PD2", "PD3", "PD5", "PD7", "PD8", "PD9",
                "PD10", "PD11", "PD12", "PD14", "PD15", "PD16", "PD22", 
                "", "", "", ""
                };


            while (!cts.Token.IsCancellationRequested && ((messageCount < 0) || messageCount >= counter))
            {
                int counterValue = Interlocked.Increment(ref counter);
                if (Reset)
                {
                    //Reset routine
                    Reset = false;
                }

                if (sendData)
                {
                    DateTime ts = DateTime.UtcNow;
                    var paintEventDataFaker = new Faker<PaintEvent>()
                            .RuleFor(x => x.AssemblyLine, "A")
                            .RuleFor(x => x.DefectCode, f => f.PickRandom(defectCodes))
                            .RuleFor(x => x.OccurrencePhase, f => f.PickRandom(occurrencePhases))
                            .RuleFor(x => x.Part, f => f.PickRandom(parts))
                            .RuleFor(x => x.EventDate, ts)
                            .RuleFor(x => x.Shift, (Math.Abs(ts.Minute/480) + 1));
                            
                    var paintEvent = paintEventDataFaker.Generate();
                    // Console.WriteLine(paintEvent.DumpAsJson());
                    // var eventData = new PaintEvent
                    // {
                    //     AssemblyLine = "A",
                    //     Part = "MXP-AX-0830",
                    //     OccurrencePhase = "POST_PAINT",
                    //     DefectCode = "PD10",
                    //     EventDate = ts.AddSeconds(-ts.Second)
                    // };

                    string dataBuffer = JsonConvert.SerializeObject(paintEvent);
                    var eventMessage = new Message(Encoding.UTF8.GetBytes(dataBuffer));
                    eventMessage.Properties.Add("sequenceNumber", counterValue.ToString());
                    eventMessage.Properties.Add("batchId", BatchId.ToString());
                    eventMessage.Properties.Add("messageType", "MESEvent");

                    Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {counter}, Body: {dataBuffer}");

                    await moduleClient.SendEventAsync("eventOutput", eventMessage);
                }

                await Task.Delay(messageDelay, cts.Token);
            }

            if (messageCount > 0 && messageCount < counter)
            {
                Console.WriteLine($"Done sending {messageCount} messages");
            }
        }

        public static async Task SendAlerts(
            ModuleClient moduleClient,
            int messageCount,
            TimeSpan messageDelay,
            CancellationTokenSource cts)
        {
            int counterValue = Interlocked.Increment(ref counter);
            if (Reset)
            {
                //Reset routine
                Reset = false;
            }

            while (!cts.Token.IsCancellationRequested && ((messageCount < 0) || messageCount >= counter))
            {
                var alertData = new OperationAlert
                {
                    AssemblyLine = "A",
                    Temperature = 35.55,
                    EventDate = DateTime.UtcNow
                };

                string dataBuffer = JsonConvert.SerializeObject(alertData);
                var eventMessage = new Message(Encoding.UTF8.GetBytes(dataBuffer));
                eventMessage.Properties.Add("batchId", BatchId.ToString());
                eventMessage.Properties.Add("messageType", "MESAlert");

                Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {counter}, Body: {dataBuffer}");

                await moduleClient.SendEventAsync("alertOutput", eventMessage);
            
                await Task.Delay(messageDelay, cts.Token);
            
            }
            if (messageCount > 0 && messageCount < counter)
            {
                Console.WriteLine($"Done sending {messageCount} messages");
            }

        }

        public static Task<MethodResponse> ResetMethod(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("Received direct method call to reset temperature sensor...");
            Reset = true;
            var response = new MethodResponse((int)HttpStatusCode.OK);
            return Task.FromResult(response);
        }   

    }
    
}