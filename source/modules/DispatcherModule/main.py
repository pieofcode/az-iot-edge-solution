# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for
# full license information.

import asyncio
import sys
import signal
import threading
from azure.iot.device import IoTHubModuleClient

# global counters
RECEIVED_MESSAGES = 0
# Event indicating client stop
stop_event = threading.Event()


def create_client():
    client = IoTHubModuleClient.create_from_edge_environment()
    print("Iothub Module client is created")

    # Define function for handling received messages
    def receive_message_handler(message):
        # NOTE: This function only handles messages sent to "rawEvent".
        # Messages sent to other inputs, or to the default, will be discarded

        # print(f"Received message: {message}")
        global RECEIVED_MESSAGES
        if message.input_name == "rawEvent":
            RECEIVED_MESSAGES += 1
            # print(f"Message received on rawEvent")
            print( f"    Message #{RECEIVED_MESSAGES}: <<{message.data}>>" )
            print( f"    Properties: {message.custom_properties}")
            # print( "    Total calls received: {}".format(RECEIVED_MESSAGES))
            # print("Forwarding message to output1")
            client.send_message_to_output(message, "output1")
            # print("Message successfully forwarded")
        else:
            print("Message received on unknown input: {}".format(message.input_name))

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
    print ( "\nPython {}\n".format(sys.version) )
    print ( "IoT Hub Client for Python" )

    # Event indicating sample stop
    stop_event = threading.Event()

    # Define a signal handler that will indicate Edge termination of the Module
    def module_termination_handler(signal, frame):
        print ("IoTHubClient sample stopped")
        stop_event.set()

    # Attach the signal handler
    signal.signal(signal.SIGTERM, module_termination_handler)

    # Create the client
    client = create_client()

    try:
        # This will be triggered by Edge termination signal
        stop_event.wait()
    except Exception as e:
        print("Unexpected error %s " % e)
        raise
    finally:
        # Graceful exit
        print("Shutting down client")
        client.shutdown()

if __name__ == "__main__":
    main()
