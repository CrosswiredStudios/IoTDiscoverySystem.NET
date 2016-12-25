using Newtonsoft.Json;
using SQLite.Net.Attributes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IoTDiscoverySystem.NET.Models.Database
{

    public sealed class SmartDevice
    {
        [JsonProperty(PropertyName = "deviceInfo")]
        public string DeviceInfo { get; set; }

        [JsonProperty(PropertyName = "id")]
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [JsonProperty(PropertyName = "ipAddress")]
        public string IpAddress { get; set; }

        [JsonProperty(PropertyName = "serialNumber")]
        public string SerialNumber { get; set; }  
    }
}
