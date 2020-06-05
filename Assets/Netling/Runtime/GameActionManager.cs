﻿using MufflonUtil;
using UnityEngine;

namespace Networking
{
    [CreateAssetMenu(menuName = "Networking/Game Action Manager")]
    public class GameActionManager : SingletonAssetManager<GameActionManager, GameAction>
    {
        [ContextMenu("Find Assets")]
        private new void FindAssets()
        {
            base.FindAssets();
        }
    }
}