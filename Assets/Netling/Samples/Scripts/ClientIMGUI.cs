using System.Collections;
using UnityEngine;

namespace Networking.Samples
{
    public class ClientIMGUI : MonoBehaviour
    {
        [SerializeField] private KeyCode _keyCode;
        [SerializeField] private float _trackingPeriod = 1;
        private bool _active;
        private int _messagesReceived;
        private int _bytesReceived;
        private int _messagesSent;
        private int _bytesSent;

        private void OnEnable()
        {
            Client.DataReceived += OnReceivedData;
            Client.DataSent += OnSentData;
        }

        private void OnSentData(int bytes)
        {
            StartCoroutine(OnSentDataCoroutine(bytes));
        }

        private IEnumerator OnSentDataCoroutine(int bytes)
        {
            _bytesSent += bytes;
            _messagesSent++;
            yield return new WaitForSeconds(_trackingPeriod);
            _bytesSent -= bytes;
            _messagesSent--;
        }

        private void OnReceivedData(int bytes)
        {
            StartCoroutine(OnReceivedDataCoroutine(bytes));
        }

        private IEnumerator OnReceivedDataCoroutine(int bytes)
        {
            _bytesReceived += bytes;
            _messagesReceived++;
            yield return new WaitForSeconds(_trackingPeriod);
            _bytesReceived -= bytes;
            _messagesReceived--;
        }

        private void OnDisable()
        {
            Client.DataReceived -= OnReceivedData;
            Client.DataSent -= OnSentData;
        }

        private void Update()
        {
            if (Input.GetKeyDown(_keyCode)) _active = !_active;
        }

        private void OnGUI()
        {
            if (!_active) return;
            GUILayout.TextArea($"Round-trip time: {Client.Instance.RoundTripTime * 1000:0.00} ms\n"
                               + $"Latency: {Client.Instance.Latency * 1000:0.00} ms\n"
                               + $"Received: {_messagesReceived / _trackingPeriod:0.00} messages/s\n"
                               + $"Received: {_bytesReceived / _trackingPeriod / 1000:0.00} kB/s\n"
                               + $"Sent: {_messagesSent / _trackingPeriod:0.00} messages/s\n"
                               + $"Sent: {_bytesSent / _trackingPeriod / 1000:0.00} kB/s\n"
                               + $"Last message received: {Client.Instance.LastPongTime:0.00} s\n"
            );
        }
    }
}