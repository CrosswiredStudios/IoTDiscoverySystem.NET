using SQLite.Net.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IoTDiscoverySystem.NET.Models.Database
{

    public sealed class Device
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string IpAddress { get; set; }
        public string Name { get; set; }
        public string SerialNumber { get; set; }
        public string State { get; set; }
        public string TcpPort { get; set; }
    }
}
