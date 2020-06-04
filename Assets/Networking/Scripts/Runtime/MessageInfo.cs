namespace Networking
{
    public struct MessageInfo
    {
        public float SentServerTime { get; set; }
        public int SenderActorNumber { get; set; }

        public static MessageInfo ServerNow => new MessageInfo
        {
            SentServerTime = Server.Time,
            SenderActorNumber = Server.ServerActorNumber
        };

        public static MessageInfo ClientNow => new MessageInfo
        {
            SentServerTime = Server.Time,
            SenderActorNumber = Client.Instance.ActorNumber
        };
    }
}