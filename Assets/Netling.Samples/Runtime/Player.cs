namespace Netling.Samples
{
    public sealed class Player : NetBehaviour
    {
        private void Start()
        {
            PlayerManager.Instance.Register(this);
            gameObject.name = IsLocal ? "Local Player" : $"Remote Player ({OwnerClientID})";
        }

        private void OnDestroy()
        {
            PlayerManager.Instance.Unregister(this);
        }
    }
}