﻿using EventCentric.Log;
using EventCentric.Messaging;
using EventCentric.Messaging.Commands;
using EventCentric.Messaging.Events;
using EventCentric.Transport;
using EventCentric.Utils;
using System;
using System.Collections.Generic;
using System.Threading;

namespace EventCentric.Publishing
{
    public class EventPublisher<T> : FSM, IEventSource,
        IMessageHandler<StartEventPublisher>,
        IMessageHandler<StopEventPublisher>,
        IMessageHandler<EventStoreHasBeenUpdated>
    {
        private static readonly string _streamType = typeof(T).Name;
        private readonly IEventDao dao;
        private const int eventsToPushMaxCount = 100;
        private const int attempsMaxCount = 300;
        private readonly object lockObject = new object();

        private volatile int eventCollectionVersion = 0;

        public EventPublisher(IBus bus, ILogger log, IEventDao dao)
            : base(bus, log)
        {
            Ensure.NotNull(dao, "dao");

            this.dao = dao;
        }

        public void Handle(EventStoreHasBeenUpdated message)
        {
            lock (this.lockObject)
            {
                if (message.EventCollectionVersion > this.eventCollectionVersion)
                    this.eventCollectionVersion = message.EventCollectionVersion;
            }
        }

        public void Handle(StopEventPublisher message)
        {
            this.log.Trace("Stopping publisher");
            base.Stop();
        }

        public void Handle(StartEventPublisher message)
        {
            this.log.Trace("Starting publisher");
            base.Start();
        }

        public PollResponse PollEvents(int lastReceivedVersion)
        {
            bool newEventsWereFound = false;
            var newEvents = new List<NewRawEvent>();
            var attemps = 0;
            while (!this.stopping && attemps <= attempsMaxCount)
            {
                attemps += 1;
                if (this.eventCollectionVersion <= lastReceivedVersion)
                    Thread.Sleep(100);
                else
                {
                    newEvents = this.dao.FindEvents(lastReceivedVersion, eventsToPushMaxCount);
                    newEventsWereFound = newEvents.Count > 0 ? true : false;
                    break;
                }
            }

            return new PollResponse(newEventsWereFound, _streamType, newEvents);
        }

        protected override void OnStarting()
        {
            try
            {
                // We handle exceptions on dao.
                var currentVersion = this.dao.GetEventCollectionVersion();

                // Event-sourcing-like approach :)
                this.bus.Publish(
                    new EventStoreHasBeenUpdated(currentVersion),
                    new EventPublisherStarted());
                this.log.Trace("Publisher started");
            }
            catch (Exception ex)
            {
                this.bus.Publish(new FatalErrorOcurred(new FatalErrorException("An exception ocurred while starting publisher", ex)));
            }
        }

        protected override void OnStopping()
        {
            this.bus.Publish(new EventPublisherStopped());
        }
    }
}
