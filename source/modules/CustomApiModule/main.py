# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for
# full license information.
import os
import asyncio
import sys
import signal
import threading
from azure.iot.device.aio import IoTHubModuleClient

from flask import Flask

port = int(os.environ.get("API_PORT",5000))
print("port is ",port)
    
app = Flask(__name__)

@app.route('/api/hello')
def hello_world():
   return "Hello World"

# Event indicating client stop
stop_event = threading.Event()


def create_client():
    client = IoTHubModuleClient.create_from_edge_environment()

    # Define function for handling received messages
    async def receive_message_handler(message):
        # NOTE: This function only handles messages sent to "input1".
        # Messages sent to other inputs, or to the default, will be discarded
        if message.input_name == "input1":
            print("the data in the message received on input1 was ")
            print(message.data)
            print("custom properties are")
            print(message.custom_properties)
            print("forwarding mesage to output1")
            await client.send_message_to_output(message, "output1")

    try:
        # Set handler on the client
        client.on_message_received = receive_message_handler
    except:
        # Cleanup if failure occurs
        client.shutdown()
        raise

    return client


async def run_sample(client):
    # Customize this coroutine to do whatever tasks the module initiates
    # e.g. sending messages
    while True:
        await asyncio.sleep(1000)


def main():
    
    # Start a flask web server for handling API routes
    app.run(host='0.0.0.0', port=port, debug = True)

    # NOTE: Client is implicitly connected due to the handler being set on it
    client = create_client()

    # Define a handler to cleanup when module is is terminated by Edge
    def module_termination_handler(signal, frame):
        print ("IoTHubClient sample stopped by Edge")
        stop_event.set()

    # Set the Edge termination handler
    signal.signal(signal.SIGTERM, module_termination_handler)

    # Run the sample
    loop = asyncio.get_event_loop()
    try:
        loop.run_until_complete(run_sample(client))
    except Exception as e:
        print("Unexpected error %s " % e)
        raise
    finally:
        print("Shutting down IoT Hub Client...")
        loop.run_until_complete(client.shutdown())
        loop.close()


if __name__ == "__main__":
    main()
