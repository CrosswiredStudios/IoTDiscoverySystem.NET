using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SQLite.Net;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using IoTDiscoverySystem.NET.Models.Messages;
using IoTDiscoverySystem.NET.Models.Database;
using System.Collections.Generic;
using Windows.Networking.Connectivity;
using System.Threading;
using Windows.System.Threading;
using System.Net.Http;

namespace PotPiServer.Models
{

    /// <summary>
    /// The Discovery System is used for gathering information about the other devices that are on the local network.
    /// </summary>
    public sealed class DiscoverySystemServer : IDisposable
    {
        #region Properties

        #region Private

        /// <summary>
        /// A database to hold device data
        /// </summary>
        private SQLiteConnection _database;

        /// <summary>
        /// The device that is hosting the Discovery system Server
        /// </summary>
        private string _deviceName;

        /// <summary>
        /// A timer to periodically find smart devices on the network
        /// </summary>
        private Timer _discoverSmartDevicesTimer;

        /// <summary>
        /// The serial number of the device that is hosting the Discovery System Server
        /// </summary>
        private string _serialNumber;

        /// <summary>
        /// A socket to broadcast discovery requests
        /// </summary>
        private DatagramSocket _socket;

        /// <summary>
        /// Port to send to and listen for UDP packets from other devices
        /// </summary>
        private string _udpPort;

        #endregion

        #region Public

        /// <summary>
        /// A list of all the devices the Discovery System is aware of
        /// </summary>
        public IEnumerable<Device> Devices
        {
            get
            {
                return _database.Table<Device>();
            }
        }

        /// <summary>
        /// IP Address of the DiscoverySystem
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
        /// Port to send to and listen for UDP packets from other devices
        /// </summary>
        public string UdpPort
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
        /// An uninitialized Discovery System
        /// </summary>
        public DiscoverySystemServer()
        {

            _database = new SQLiteConnection(new SQLite.Net.Platform.WinRT.SQLitePlatformWinRT(),
                                             Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "iotDiscSys.sqlite"));
            _database.DropTable<Device>();
            _database.CreateTable<Device>();

            _socket = new DatagramSocket();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Initialize the Discovery System
        /// </summary>
        /// <returns></returns>
        public async void Initialize(string udpPort, string deviceName = "", string serialNumber = "")
        {
            Debug.WriteLine("Discovery System: Initializing");

            try
            {
                _udpPort = udpPort;

                // Set the message received function
                _socket.MessageReceived += ReceiveDiscoveryResponse;

                // Start the server
                await _socket.BindServiceNameAsync(UdpPort);

                // Set a timer to discover new devices every minute
                _discoverSmartDevicesTimer = new Timer(SendDiscoveryRequest, null, 0, 60000);

                Debug.WriteLine("Discovery System: Success");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Discovery System: Failure");
                Debug.WriteLine("Reason: " + ex.Message);
            }
        }

        /// <summary>
        /// Callback fired when a packet is received on the port.
        /// </summary>
        /// <param name="ds"></param>
        /// <param name="args"></param>
        /// Sample Message {"IpAddress":"10.0.0.202","Product":"PotPiServer","Command":"DiscoveryRequest"}
        /// Sample Message {"IpAddress":"10.0.0.202","Product":"PotPiPowerBox","SerialNumber":"1234-abcd","TcpPort":"215"}
        public async void ReceiveDiscoveryResponse(DatagramSocket ds, DatagramSocketMessageReceivedEventArgs args)
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
                    var response = new DiscoveryResponseMessage(await reader.ReadToEndAsync());

                    // If the response is valid
                    if (response.IsValid)
                    {
                        Debug.WriteLine("Contents: " + JsonConvert.SerializeObject(response));

                        #region Handle Previously Existing Devices

                        // Go through all the current devices
                        foreach (var device in Devices)
                        {
                            // If the stored device matches the device responding to the discovery request
                            if (device.Title == response.Device &&
                               device.SerialNumber == response.SerialNumber)
                            {
                                // If the match is exact
                                if (device.IpAddress == response.IpAddress)
                                {
                                    Debug.WriteLine("Discovery System: Device already in system");

                                    // We already know about this device - do nothing
                                    return;
                                }
                                else // If the device got a new IP address
                                {
                                    Debug.WriteLine("Discovery System: Device already in system - updating IP Address");

                                    // Update the IP address 
                                    device.IpAddress = response.IpAddress;

                                    // Save the changes
                                    _database.Update(device);

                                    // We're done
                                    return;
                                }
                            }
                        }

                        #endregion

                        #region Handle New Devices

                        Device newDevice = new Device();
                        newDevice.DeviceType = response.DeviceType;
                        newDevice.IpAddress = response.IpAddress;
                        newDevice.Title = response.Device;
                        newDevice.SerialNumber = response.SerialNumber;
                        newDevice.State = "";
                        newDevice.TcpPort = response.TcpPort;
                        _database.Insert(newDevice);

                        #endregion

                        #region Inform Device Of Acceptance
                        if (!String.IsNullOrEmpty(response.TcpPort))
                        {

                            // Create a TCP connection with the device
                            StreamSocket _tcpConnection = new StreamSocket();
                            await _tcpConnection.ConnectAsync(new HostName(response.IpAddress), response.TcpPort);

                            // Create an Accept Message
                            DiscoveryAcceptMessage acceptMessage = new DiscoveryAcceptMessage("DISCOVERED", _deviceName, IpAddress);

                            byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(acceptMessage));

                            // Send the message
                            await _tcpConnection.OutputStream.WriteAsync(buffer.AsBuffer());
                        }
                        #endregion

                    }
                    else // If the respons was not valid
                    {
                        Debug.WriteLine("Discovery System: UDP packet not valid");
                        // Ignore the packet
                        return;
                    }

                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Discovery System - Failure: " + ex.Message);
            }
        }

        /// <summary>
        /// Sends a discovery request UDP packet
        /// </summary>
        public async void SendDiscoveryRequest(object state = null)
        {
            Debug.WriteLine("DiscoverSystemServer: Sending Discovery Request");
            try
            {
                // Get an output stream to all IPs on the given port
                using (var stream = await _socket.GetOutputStreamAsync(new HostName("255.255.255.255"), UdpPort))
                {
                    // Get a data writing stream
                    using (var writer = new DataWriter(stream))
                    {
                        // Include all known devices in the request to minimize traffic (smart devices can use this info to determine if they need to respond)
                        JArray jDevices = new JArray();
                        foreach (var device in Devices)
                        {
                            JObject jDevice = new JObject();
                            jDevice.Add("Device", device.Title);
                            jDevice.Add("IpAddress", device.IpAddress);
                            jDevice.Add("SerialNumber", device.SerialNumber);
                            jDevices.Add(jDevice);
                        }

                        // Create a discovery request message
                        DiscoveryRequestMessage discoveryRequestMessage = new DiscoveryRequestMessage("DISCOVER", "PotPiServer", IpAddress, jDevices);

                        // Convert the request to a JSON string
                        writer.WriteString(JsonConvert.SerializeObject(discoveryRequestMessage));

                        // Send
                        await writer.StoreAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Discovery System Server - Send Discovery Request Failed: " + ex.Message);
            }
        }

        public async Task UpdateDeviceStates()
        {
            foreach (var device in _database.Table<Device>())
            {
                if (device.DeviceType.ToLower() == "powerbox")
                {
                    using (HttpClient httpClient = new HttpClient())
                    {
                        HttpResponseMessage response = await httpClient.GetAsync(new Uri("http://" + device.IpAddress + "/api/outlets"));
                        device.State = await response.Content.ReadAsStringAsync();
                        _database.Update(device);
                    }
                }
            }
        }

        

        #endregion
    }
}
