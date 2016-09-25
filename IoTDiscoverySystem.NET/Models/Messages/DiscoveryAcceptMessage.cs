namespace IoTDiscoverySystem.NET.Models.Messages
{
    class DiscoveryAcceptMessage
    {
        public string IpAddress { get; set; }
        public string Device { get; set; }
        public string Command { get; set; }
    }
}
