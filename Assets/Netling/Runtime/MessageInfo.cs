namespace Netling
{
    public struct MessageInfo
    {
        public float SentServerTime { get; set; }
        public int SenderClientID { get; set; }

        public static MessageInfo ServerNow => new MessageInfo
        {
            SentServerTime = Server.Time,
            SenderClientID = Server.ServerClientID
        };

        public static MessageInfo ClientNow => new MessageInfo
        {
            SentServerTime = Server.Time,
            SenderClientID = Client.Instance.ID
        };
    }
}