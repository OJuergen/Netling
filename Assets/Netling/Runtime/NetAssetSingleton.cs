using System.Linq;
using MufflonUtil;
using UnityEngine;

namespace Netling
{
    public abstract class NetAssetSingleton<T> : NetAsset, ISingleton where T : Object
    {
        private static T _instance;
        public static T Instance => _instance ??= Resources.FindObjectsOfTypeAll<T>().First();
    }
}