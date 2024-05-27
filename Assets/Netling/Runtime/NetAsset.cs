using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using MufflonUtil;
using Unity.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Netling
{
    /// <summary>
    /// A <see cref="ScriptableObject"/> that has network capabilities.
    /// Supports synchronized variables and remote procedure calls.
    /// </summary>
    public abstract class NetAsset : ScriptableObject, IDirtyMaskProvider, IManagedAsset
    {
        [SerializeField, NotEditable] private string[] _rpcMethodNames;

        public int NetID => NetAssetManager.Instance.GetID(this);

        private delegate void RPCDelegate(MessageInfo messageInfo, params object[] args);

        public delegate void RPC(MessageInfo messageInfo);

        private RPCDelegate[] _rpcDelegates;
        private Type[][] _argumentTypes;
        private Dictionary<string, int> _methodNameToIndex;
        private byte _dirtyMask = 255;

        protected void OnValidate()
        {
            MethodInfo[] rpcMethods =
                GetBaseTypes(GetType())
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    .Where(info => info.GetCustomAttributes(typeof(NetlingRPCAttribute), false).Length > 0)
                    .ToArray();
            _rpcMethodNames = rpcMethods.Select(info => info.Name).OrderBy(n => n).ToArray();
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
            OnEnable();
        }

        private Type[] GetBaseTypes(Type type)
        {
            if (type == null) return new Type[0];
            var types = new[] { type };
            if (type.BaseType == null) return types;
            Type t = type;
            while (t.BaseType != null)
            {
                t = t.BaseType;
                var newTypes = new Type[types.Length + 1];
                Array.Copy(types, newTypes, types.Length);
                newTypes[types.Length] = t;
                types = newTypes;
                if (t == typeof(NetAsset)) break;
            }

            return types;
        }

        protected virtual void OnEnable()
        {
            MethodInfo[] unsortedRPCMethods =
                GetBaseTypes(GetType())
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    .Where(info => info.GetCustomAttributes(typeof(NetlingRPCAttribute), false).Length > 0)
                    .ToArray();
            if (_rpcMethodNames == null) return;
            _rpcDelegates = new RPCDelegate[_rpcMethodNames.Length];
            _methodNameToIndex = new Dictionary<string, int>();
            _argumentTypes = new Type[_rpcMethodNames.Length][];
            foreach (MethodInfo rpcMethod in unsortedRPCMethods)
            {
                int index = Array.IndexOf(_rpcMethodNames, rpcMethod.Name);
                _rpcDelegates[index] = GetRPCDelegate(rpcMethod);
                _argumentTypes[index] = rpcMethod.GetParameters()
                    .Select(p => p.ParameterType)
                    .Where(type => type != typeof(MessageInfo))
                    .ToArray();
                _methodNameToIndex[rpcMethod.Name] = index;
            }
        }

        protected void SendRPC(string methodName, params object[] args)
        {
            if (!_methodNameToIndex.ContainsKey(methodName))
                throw new ArgumentException(
                    $"Cannot invoke RPC {methodName} on {this}: Method not found. MufflonRPC attribute missing?");
            if (Server.IsActive)
                Server.Instance.SendRPC(this, methodName, args);
            else
                Client.Instance.SendRPC(this, methodName, args);
        }

        public void SerializeRPC(ref DataStreamWriter writer, string methodName, params object[] args)
        {
            int index = _methodNameToIndex[methodName];
            writer.WriteInt(index);
            Type[] argumentTypes = _argumentTypes[index];
            writer.WriteObjects(args, argumentTypes);
        }

        public RPC DeserializeRPC(ref DataStreamReader reader)
        {
            int index = reader.ReadInt();
            if (index < 0 || index > _argumentTypes.Length)
                throw new NetException($"Cannot deserialize rpc with index {index}");
            Type[] argumentTypes = _argumentTypes[index];
            object[] arguments = reader.ReadObjects(argumentTypes);
            return info => _rpcDelegates[index].Invoke(info, arguments);
        }

        /// <summary>
        /// Creates a delegate from method info (<see cref="MethodInfo"/>).
        /// The delegate will take as parameters: the message network info (<see cref="MessageInfo"/>)
        /// and the parameters of the method that are <i>not</i> of type MessageInfo.
        /// The delegate will invoke the method on this asset using the message info to populate any
        /// arguments of that type.
        ///
        /// (MessageInfo mi, object[] args) => methodInfo.Invoke(this, argsWithMessageInfo)
        /// </summary>
        /// <param name="methodInfo">The method to create the delegate for.</param>
        /// <returns>A delegate to invoke the method on this asset with given message info and arguments.</returns>
        /// <exception cref="ArgumentNullException">Thrown when passed method info is null.</exception>
        /// <exception cref="ArgumentException">Thrown when passed method info is not suited for delegate preparation, e.g., static methods.</exception>
        private RPCDelegate GetRPCDelegate(MethodInfo methodInfo)
        {
            if (methodInfo == null) throw new ArgumentNullException(nameof(methodInfo));
            if (methodInfo.DeclaringType == null)
                throw new ArgumentException($"Cannot prepare rpc delegate for static method {methodInfo.Name}");

            ParameterExpression argsExpression = Expression.Parameter(typeof(object[]), "args");
            ParameterExpression messageInfoExpression = Expression.Parameter(typeof(MessageInfo), "messageInfo");

            var argsIndex = 0;
            var methodArgumentExpressions = new List<Expression>();
            foreach (ParameterInfo parameterInfo in methodInfo.GetParameters())
            {
                Type paramType = parameterInfo.ParameterType;
                if (paramType == typeof(MessageInfo)) methodArgumentExpressions.Add(messageInfoExpression);
                else
                {
                    ConstantExpression indexExpression = Expression.Constant(argsIndex++);
                    IndexExpression arrayAccessExpression = Expression.ArrayAccess(argsExpression, indexExpression);
                    methodArgumentExpressions.Add(Expression.Convert(arrayAccessExpression, paramType));
                }
            }

            Expression targetExpression = Expression.Constant(this);
            MethodCallExpression callExpression =
                Expression.Call(targetExpression, methodInfo, methodArgumentExpressions);
            Expression<RPCDelegate> lambda =
                Expression.Lambda<RPCDelegate>(callExpression, messageInfoExpression, argsExpression);

            return
                lambda.Compile(); // (MessageInfo mi, object[] args) => methodInfo.Invoke(this, (T[]) args) .
            // args are cast to respective parameter type or replaced with MessageInfo mi.
        }

        public void Serialize(ref DataStreamWriter writer, bool fullLoad)
        {
            if (fullLoad) _dirtyMask = 255;
            writer.WriteByte(_dirtyMask);
            Serialize(ref writer, _dirtyMask);
            _dirtyMask = 0;
        }

        protected virtual void Serialize(ref DataStreamWriter writer, byte dirtyMask)
        { }

        public void Deserialize(ref DataStreamReader reader)
        {
            byte dirtyMask = reader.ReadByte();
            Deserialize(ref reader, dirtyMask);
            SetDirty(dirtyMask);
        }

        protected virtual void Deserialize(ref DataStreamReader reader, byte dirtyMask)
        { }

        /// <summary>
        /// Test if asset is dirty, i.e., has state that needs synchronization.
        /// </summary>
        /// <returns>True, iff any bit set in the current dirty mask.</returns>
        public bool IsDirty()
        {
            return _dirtyMask != 0;
        }

        /// <summary>
        /// Test whether the given <paramref name="bitIndex"/> corresponds to state that needs synchronization.
        /// </summary>
        /// <param name="bitIndex">Index of bit to test</param>
        /// <returns>True, iff the current dirty mask is set at the <paramref name="bitIndex"/></returns>
        public bool IsDirtyAt(byte bitIndex)
        {
            return IsDirtyAt(_dirtyMask, bitIndex);
        }

        /// <summary>
        /// Test the given <paramref name="bitIndex"/> against the given <paramref name="dirtyMask"/>.
        /// </summary>
        /// <param name="dirtyMask">Mask defining dirty bits</param>
        /// <param name="bitIndex">Index of bit to test</param>
        /// <returns>True, iff the <paramref name="dirtyMask"/> is set at the <paramref name="bitIndex"/></returns>
        public bool IsDirtyAt(byte dirtyMask, byte bitIndex)
        {
            return (dirtyMask & (1 << bitIndex)) == 1 << bitIndex;
        }

        /// <summary>
        /// Marks state corresponding to the given <paramref name="bitMask"/> as dirty.
        /// </summary>
        /// <param name="bitMask">Mask with bits that are set dirty</param>
        protected void SetDirty(byte bitMask)
        {
            _dirtyMask |= bitMask;
        }

        /// <summary>
        /// Sets all state dirty.
        /// </summary>
        public new void SetDirty()
        {
            _dirtyMask = 255;
        }

        /// <summary>
        /// Marks state corresponding to <paramref name="bitIndex"/> as dirty.
        /// </summary>
        /// <param name="bitIndex">Index of bit to set dirty</param>
        /// <exception cref="IndexOutOfRangeException">If index is out of range</exception>
        public void SetDirtyAt(byte bitIndex)
        {
            if (bitIndex > 7) throw new IndexOutOfRangeException("Bit index cannot be larger than 7");
            _dirtyMask |= (byte)(1 << bitIndex);
        }

        protected void SetField<T>(ref T field, T value, byte bitIndex)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                SetDirtyAt(bitIndex);
            }
        }
    }
}