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
        public IEnumerable<SmartDevice> SmartDevices
        {
            get
            {
                return _database.Table<SmartDevice>();
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
            // Connect to the database
            _database = new SQLiteConnection(new SQLite.Net.Platform.WinRT.SQLitePlatformWinRT(),
                                             Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "iotDiscSys.sqlite"));

            // Clear the smart device table 
            _database.DropTable<SmartDevice>();
            _database.CreateTable<SmartDevice>();

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
        public async void Initialize(string udpPort, JObject deviceInfo)
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
                var resultStream = args.GetDataStream().AsStreamForRead();
                using (var reader = new StreamReader(resultStream))
                {
                    string discoveryResponseString = await reader.ReadToEndAsync();
                    JObject jDiscoveryResponse = JObject.Parse(discoveryResponseString);

                    // The device must broadcast a brand, model, and serial number
                    if (jDiscoveryResponse["brand"] != null &&
                       jDiscoveryResponse["model"] != null &&
                       jDiscoveryResponse["serialNumber"] != null)
                    {
                        // Create a strongly typed model of this new device
                        SmartDevice newSmartDevice = new SmartDevice();
                        newSmartDevice.DeviceInfo = JsonConvert.SerializeObject(jDiscoveryResponse);
                        newSmartDevice.IpAddress = args.RemoteAddress.DisplayName;
                        newSmartDevice.SerialNumber = jDiscoveryResponse.Value<string>("serialNumber");

                        // Get the current smart devices
                        TableQuery<SmartDevice> smartDevices = _database.Table<SmartDevice>();

                        // Go through the existing devices
                        foreach (SmartDevice smartDevice in smartDevices)
                        {
                            // Convert the existing devices info to a JObject 
                            JObject smartDeviceInfo = JObject.Parse(smartDevice.DeviceInfo);

                            // If this brand and serial number exist in the system
                            if (smartDeviceInfo.Value<string>("brand") == jDiscoveryResponse.Value<string>("brand") &&
                               smartDeviceInfo.Value<string>("serialNumber") == jDiscoveryResponse.Value<string>("serialNumber"))
                            {
                                // Silence the device to avoid repeat responses
                                SilenceSmartDevice(newSmartDevice.IpAddress + jDiscoveryResponse.Value<string>("discoverySilenceUrl"));

                                // If the IP address has changed
                                if (smartDevice.IpAddress != newSmartDevice.IpAddress)
                                {
                                    // Update the smart device in the database
                                    smartDevice.IpAddress = newSmartDevice.IpAddress;
                                    _database.Update(smartDevice);
                                    return;
                                }
                                else // If its a perfect match
                                {
                                    // Ignore the response
                                    return;
                                }
                            }
                        }

                        // Silence the device to avoid repeat responses
                        SilenceSmartDevice(newSmartDevice.IpAddress + jDiscoveryResponse.Value<string>("discoverySilenceUrl"));

                        // Add it to the database
                        Debug.WriteLine("Added: " + newSmartDevice.DeviceInfo);
                        _database.Insert(newSmartDevice);

                    }
                    else // If the response was not valid
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
                        foreach (var smartDevice in SmartDevices)
                        {
                            // Convert the existing device info to a JObject 
                            JObject smartDeviceInfo = JObject.Parse(smartDevice.DeviceInfo);

                            JObject jDevice = new JObject();
                            jDevice.Add("brand", smartDeviceInfo.Value<string>("brand"));
                            jDevice.Add("ipAddress", smartDevice.IpAddress);
                            jDevice.Add("model", smartDeviceInfo.Value<string>("model"));
                            jDevice.Add("serialNumber", smartDevice.SerialNumber);
                            jDevices.Add(jDevice);
                        }

                        // Create a discovery request message
                        DiscoveryRequestMessage discoveryRequestMessage = new DiscoveryRequestMessage("DISCOVER", "PotPiServer", IpAddress, jDevices);

                        // Convert the request to a JSON string
                        writer.WriteString(JsonConvert.SerializeObject(discoveryRequestMessage));

                        Debug.WriteLine(JsonConvert.SerializeObject(discoveryRequestMessage));

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

        private async void SilenceSmartDevice(string apiUrl)
        {
            Debug.WriteLine("Silencing device: " + apiUrl);
            HttpClient httpClient = new HttpClient();
            HttpResponseMessage response = await httpClient.GetAsync("http://" + apiUrl);
        }

        #endregion
    }
}
