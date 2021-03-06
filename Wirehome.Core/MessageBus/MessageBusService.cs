﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Wirehome.Core.Contracts;
using Wirehome.Core.Diagnostics;
using Wirehome.Core.Storage;
using Wirehome.Core.System;

namespace Wirehome.Core.MessageBus
{
    public sealed class MessageBusService : IService, IDisposable
    {
        readonly BlockingCollection<MessageBusMessage> _messageQueue = new BlockingCollection<MessageBusMessage>();
        readonly MessageBusMessageHistory _messageHistory = new MessageBusMessageHistory();
        readonly Dictionary<string, MessageBusSubscriber> _subscribers = new Dictionary<string, MessageBusSubscriber>();

        readonly SystemCancellationToken _systemCancellationToken;

        readonly OperationsPerSecondCounter _inboundCounter;
        readonly OperationsPerSecondCounter _processingRateCounter;

        readonly MessageBusServiceOptions _options;

        readonly ILogger _logger;

        public MessageBusService(
            StorageService storageService,
            SystemStatusService systemStatusService,
            DiagnosticsService diagnosticsService,
            SystemCancellationToken systemCancellationToken,
            ILogger<MessageBusService> logger)
        {
            _systemCancellationToken = systemCancellationToken ?? throw new ArgumentNullException(nameof(systemCancellationToken));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (storageService == null) throw new ArgumentNullException(nameof(storageService));
            storageService.TryReadOrCreate(out _options, DefaultDirectoryNames.Configuration, MessageBusServiceOptions.Filename);

            if (diagnosticsService == null) throw new ArgumentNullException(nameof(diagnosticsService));
            _inboundCounter = diagnosticsService.CreateOperationsPerSecondCounter("message_bus.inbound_rate");
            _processingRateCounter = diagnosticsService.CreateOperationsPerSecondCounter("message_bus.processing_rate");

            if (systemStatusService == null) throw new ArgumentNullException(nameof(systemStatusService));
            systemStatusService.Set("message_bus.queued_messages_count", () => _messageQueue.Count);
            systemStatusService.Set("message_bus.subscribers_count", () => _subscribers.Count);
            systemStatusService.Set("message_bus.inbound_rate", () => _inboundCounter.Count);
            systemStatusService.Set("message_bus.processing_rate", () => _processingRateCounter.Count);

            _options.ToString(); // TODO: Remove or add new settings.
        }

        public void Start()
        {
            var dispatcherThread = new Thread(DispatchMessageBusMessages)
            {
                Name = nameof(MessageBusService),
                IsBackground = true
            };

            dispatcherThread.Start();
        }

        public void Publish(IDictionary<object, object> message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var busMessage = new MessageBusMessage
            {
                Message = message
            };

            Publish(busMessage);
        }

        public void Publish(MessageBusMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            message.EnqueuedTimestamp = DateTime.UtcNow;
            _messageQueue.Add(message);

            _inboundCounter.Increment();
        }

        public void EnableHistory(int maxMessagesCount)
        {
            _messageHistory.Enable(maxMessagesCount);
        }

        public void DisableHistory()
        {
            _messageHistory.Disable();
        }

        public void ClearHistory()
        {
            _messageHistory.Clear();
        }

        public List<MessageBusMessage> GetHistory()
        {
            return _messageHistory.GetMessages();
        }

        public List<MessageBusSubscriber> GetSubscribers()
        {
            return new List<MessageBusSubscriber>(_subscribers.Values);
        }

        public string Subscribe(string uid, IDictionary<object, object> filter, Action<IDictionary<object, object>> callback)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            if (string.IsNullOrEmpty(uid))
            {
                uid = Guid.NewGuid().ToString("D");
            }

            lock (_subscribers)
            {
                _subscribers[uid] = new MessageBusSubscriber(uid, filter, callback, _logger);
            }

            return uid;
        }

        public void Unsubscribe(string uid)
        {
            if (uid == null) throw new ArgumentNullException(nameof(uid));

            lock (_subscribers)
            {
                _subscribers.Remove(uid, out _);
            }
        }

        public void Dispose()
        {
            _messageQueue.Dispose();
        }

        void DispatchMessageBusMessages()
        {
            while (!_systemCancellationToken.Token.IsCancellationRequested)
            {
                try
                {
                    var message = _messageQueue.Take(_systemCancellationToken.Token);
                    if (_systemCancellationToken.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (message == null)
                    {
                        continue;
                    }

                    _messageHistory.Add(message);

                    List<MessageBusSubscriber> subscribers;
                    lock (_subscribers)
                    {
                        subscribers = new List<MessageBusSubscriber>(_subscribers.Values);
                    }

                    foreach (var subscriber in subscribers)
                    {
                        if (MessageBusFilterComparer.IsMatch(message.Message, subscriber.Filter))
                        {
                            ThreadPool.QueueUserWorkItem(_ => subscriber.ProcessMessage(message.Message));
                        }
                    }
                }
                catch (ThreadAbortException)
                {
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error while dispatching messages.");
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
            }
        }
    }
}
