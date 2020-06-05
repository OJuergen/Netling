using MufflonUtil;
using UnityEngine;

namespace Netling
{
    [CreateAssetMenu(menuName = "Networking/Net Asset Manager")]
    public sealed class NetAssetManager : AssetManager<NetAssetManager, NetAsset>
    {
        protected override void OnIDAssigned(NetAsset asset, int id)
        {
            base.OnIDAssigned(asset, id);
            asset.NetID = id;
        }

        [ContextMenu("Find Assets")]
        public new void FindAssets()
        {
            base.FindAssets();
        }
    }
}