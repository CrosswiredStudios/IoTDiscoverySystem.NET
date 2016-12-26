using IoTDiscoverySystem.NET.Models.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace PotPiPowerBox.Models
{
    /// <summary>
    /// The Discovery Server is an object that listens for and responds to a UDP Identification Request 
    /// </summary>
    public sealed class DiscoverySystemClient
    {
        #region Properties
        
        /// <summary>
        /// Flag to indicate if the system is broadcasting discovery responses
        /// </summary>
        private bool _broadcasting;

        /// <summary>
        /// A JSON object that contains all the information about the device
        /// </summary>
        private JObject _deviceInfo;

        /// <summary>
        /// UDP Socket object
        /// </summary>
        private DatagramSocket _socket;

        /// <summary>
        /// Port to send and receive UDP messages on
        /// </summary>
        private string _udpPort;

        /// <summary>
        /// The IpAddress of the device
        /// </summary>
        public string IpAddress
        {
            get
            {
                var hosts = NetworkInformation.GetHostNames();
                foreach (var host in hosts)
                {
                    if (host.Type == HostNameType.Ipv4)
                    {
                        return host.DisplayName;
                    }
                }
                return "";
            }
        }

        /// <summary>
        /// Is the Discovery System broadcasting discovery response messages
        /// </summary>
        public bool IsBroadcasting
        {
            get
            {
                return _broadcasting;
            }
        }

        /// <summary>
        /// Port the Discovery System will send and receive messages on
        /// </summary>
        public string Port
        {
            get
            {
                return _udpPort;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Create a Discovery System Client instance
        /// </summary>
        public DiscoverySystemClient()
        {
            _broadcasting = false;
            _socket = new DatagramSocket();
            _udpPort = "";
        }

        #endregion

        #region Methods

        /// <summary>
        /// Initiates the Discovery System Client. 
        /// </summary>
        /// <param name="udpPort">This is the port the system will listen for and broadcast udp packets</param>
        /// <param name="deviceInfo">A JSON object containing all the relevant device info</param>
        public async void Initialize(string udpPort, JObject deviceInfo)
        {
            Debug.WriteLine("Discovery System: Initializing");

            try
            {
                // Set initial variables
                _deviceInfo = deviceInfo;
                _udpPort = udpPort;

                // Setup a UDP socket listener
                _socket.MessageReceived += ReceivedDiscoveryRequest;
                await _socket.BindServiceNameAsync(_udpPort);
                Debug.WriteLine("Discovery System: Success");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Discovery System: Failure");
                Debug.WriteLine("Reason: " + ex.Message);
            }
        }

        /// <summary>
        /// Asynchronously handle receiving a UDP packet
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="eventArguments"></param>
        private async void ReceivedDiscoveryRequest(DatagramSocket socket, DatagramSocketMessageReceivedEventArgs args)
        {
            Debug.WriteLine("Discovery System: Received UDP packet");

            try
            {
                // Get the data from the packet
                var result = args.GetDataStream();
                var resultStream = result.AsStreamForRead();
                using (var reader = new StreamReader(resultStream))
                {
                    // Load the raw data into a response object
                    var potentialRequest = (await reader.ReadToEndAsync());

                    JObject jRequest = JObject.Parse(potentialRequest);
                    Debug.WriteLine("Contents: " + potentialRequest);

                    // If the message was a valid request
                    if (jRequest["command"] != null)
                    {
                        // If it was a discovery request
                        if (jRequest.Value<string>("command").ToLower() == "discover")
                        {
                            // If the requestor included a list of its known devices
                            if (jRequest["knownDevices"] != null)
                            {
                                // Go through the list of known devices
                                foreach (var knownDevice in jRequest["KnownDevices"])
                                {
                                    if(knownDevice.Value<string>("brand") == _deviceInfo.Value<string>("brand") &&
                                        knownDevice.Value<string>("ipAddress") == _deviceInfo.Value<string>("brand") &&
                                        knownDevice.Value<string>("model") == _deviceInfo.Value<string>("model") &&
                                        knownDevice.Value<string>("serialNumber") == _deviceInfo.Value<string>("serialNumber"))
                                    {
                                        return;
                                    }
                                }
                            }

                            // Begin Broadcasting a discovery response until we get an acceptance or reach the timeout.
                            StartBroadcasting();
                        }
                    }
                }
            }
            catch
            {
                return;
            }
        }

        /// <summary>
        /// Start broadcasting discovery response messages
        /// </summary>
        public async void StartBroadcasting()
        {
            _broadcasting = true;
            int count = 0;

            while (_broadcasting)
            {
                // Get an output stream to all IPs on the given port
                using (var stream = await _socket.GetOutputStreamAsync(new HostName("255.255.255.255"), _udpPort))
                {
                    // Get a data writing stream
                    using (var writer = new DataWriter(stream))
                    {
                        // Create a discovery response message
                       // DiscoveryResponseMessage discoveryResponse = new DiscoveryResponseMessage(_deviceName, _deviceType, IpAddress, _potPiDeviceId, _serialNumber, _tcpPort);

                        // Convert the request to a JSON string
                        writer.WriteString(JsonConvert.SerializeObject(_deviceInfo));

                        Debug.WriteLine(JsonConvert.SerializeObject(_deviceInfo));
                        
                        // Send
                        await writer.StoreAsync();
                    }
                }
                

                // Enforce maximum of 10 seconds of broadcasting
                count++;
                if (count == 10) _broadcasting = false;
                await Task.Delay(2000);
            }
        }

        /// <summary>
        /// Stops the Discovery System Client from broadcasting response messages.
        /// </summary>
        public void StopBroadcasting()
        {
            Debug.WriteLine("Discovery System Client: Stopping Discovery Response broadcast");
            _broadcasting = false;
        }

        #endregion
    }
}
