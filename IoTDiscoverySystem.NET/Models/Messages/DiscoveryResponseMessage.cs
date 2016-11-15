using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace IoTDiscoverySystem.NET.Models.Messages
{

    public sealed class DiscoveryResponseMessage
    {
        #region Properties

        #region Private

        private string _device;

        private string _deviceType;

        private string _error;

        private string _ipAddress; 

        private string _serialNumber;

        private string _tcpPort;

        #endregion

        #region Public

        [JsonIgnore]
        [JsonProperty(Required = Required.Default)]
        public string Error { get { return _error; } }

        /// <summary>
        /// IP Address of the responding device
        /// </summary>
        public string IpAddress { get { return _ipAddress; } }

        /// <summary>
        /// Tells us if the Discovery Response meets the minimum requirements of being valid. Ignored when going to and from JSON.
        /// </summary>
        [JsonIgnore]
        [JsonProperty(Required = Required.Default)]
        public bool IsValid
        {
            get
            {
                // Make sure our minimum requirements are met
                return !String.IsNullOrEmpty(IpAddress) &&
                       !String.IsNullOrEmpty(Device) &&
                       !String.IsNullOrEmpty(SerialNumber) &&
                       !String.IsNullOrEmpty(TcpPort);
            }
        }

        /// <summary>
        /// The type of device responding
        /// </summary>
        public string Device { get { return _device;  } }

        /// <summary>
        /// Serial Number of the responder
        /// </summary>
        public string SerialNumber { get { return _serialNumber; } }

        /// <summary>
        /// TCP port used to send commands to teh responder
        /// </summary>
        public string TcpPort { get { return _tcpPort; } }

        #endregion

        #endregion

        #region Constructors

        public DiscoveryResponseMessage() { }

        public DiscoveryResponseMessage(string responseString)
        {
            try
            {
                JObject json = JObject.Parse(responseString);
                _device = json["Device"].ToString();
                _ipAddress = json["IpAddress"].ToString();
                _serialNumber = json["SerialNumber"].ToString();
                _tcpPort = json["TcpPort"].ToString();
            }
            catch(Exception ex)
            {
                _error = ex.Message;
            }
        }

        public DiscoveryResponseMessage(string ipAddress, string device, string serialNumber, string tcpPort)
        {
            _ipAddress = ipAddress;
            _device = device;
            _serialNumber = serialNumber;
            _tcpPort = tcpPort;
        }

        #endregion

    }
}
