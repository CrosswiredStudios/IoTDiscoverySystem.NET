using IoTDiscoverySystem.NET.Models.Messages;
using Newtonsoft.Json;
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

        #region Private

        /// <summary>
        /// Flag to indicate if the system is broadcasting discovery responses
        /// </summary>
        private bool _broadcasting;

        /// <summary>
        /// Port to send and receive TCP messages on
        /// </summary>
        private string _deviceName;

        /// <summary>
        /// The type of device
        /// </summary>
        private string _deviceType;

        /// <summary>
        /// Port to send and receive TCP messages on
        /// </summary>
        private string _serialNumber;

        /// <summary>
        /// UDP Socket object
        /// </summary>
        private DatagramSocket _socket;

        /// <summary>
        /// Port to send and receive TCP messages on
        /// </summary>
        private string _tcpPort;

        /// <summary>
        /// Port to send and receive UDP messages on
        /// </summary>
        private string _udpPort;

        #endregion

        #region Public

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

        #endregion

        #region Constructors

        /// <summary>
        /// Create a Discovery System Client instance
        /// </summary>
        public DiscoverySystemClient()
        {
            _broadcasting = false;
            _socket = new DatagramSocket();
            _tcpPort = "";
            _udpPort = "";
        }

        #endregion

        #region Methods

        /// <summary>
        /// Initiates the Discovery System Client. 
        /// </summary>
        /// <param name="udpPort">This is the port the system will listen for and broadcast udp packets</param>
        /// <param name="tcpPort">This is the TCP port that your application is listening on for confirmation</param>
        /// <param name="deviceName">This is the categorical or generic name for the device acting as a discovery system client</param>
        /// <param name="serialNumber">The serial number is a unique Id that can be used to differentiate between similar devices</param>
        public async void Initialize(string udpPort, string tcpPort = "", string deviceName = "", string deviceType = "", string serialNumber = "")
        {
            Debug.WriteLine("Discovery System: Initializing");

            try
            {
                // Set initial variables
                _deviceName = deviceName;
                _deviceType = _deviceType;
                _serialNumber = serialNumber;
                _udpPort = udpPort;
                _tcpPort = tcpPort;

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
                var resultStream = result.AsStreamForRead(1024);
                using (var reader = new StreamReader(resultStream))
                {
                    // Load the raw data into a response object
                    var response = (await reader.ReadToEndAsync());

                    Debug.WriteLine("Contents: " + response);

                    // Create a Discovery Request object from the data
                    DiscoveryRequestMessage request = new DiscoveryRequestMessage(response.Trim());

                    // If the message was a valid request
                    if (request.IsValid)
                    {
                        // If it was a discovery request
                        if (request.Command == "DISCOVER")
                        {
                            // If the requestor included a list of its known devices
                            if (request.KnownDevices.Count > 0)
                            {
                                // Go through the list of known devices
                                foreach (var knownDevice in request.KnownDevices)
                                {
                                    // If this device is on the list
                                    if (knownDevice["Device"].ToString() == _deviceName &&
                                       knownDevice["IpAddress"].ToString() == IpAddress &&
                                       knownDevice["SerialNumber"].ToString() == _serialNumber)
                                    {
                                        // Disregard this discovery request
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
                        DiscoveryResponseMessage discoveryResponse = new DiscoveryResponseMessage(IpAddress, _deviceName, _serialNumber, _tcpPort);

                        // Convert the request to a JSON string
                        writer.WriteString(JsonConvert.SerializeObject(discoveryResponse));

                        // Send
                        await writer.StoreAsync();
                    }
                }
                

                // Enforce maximum of 1 minute of broadcasting
                count++;
                if (count == 60) _broadcasting = false;
                await Task.Delay(1000);
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
