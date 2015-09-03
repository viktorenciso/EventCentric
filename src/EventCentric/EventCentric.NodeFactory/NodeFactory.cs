﻿using EventCentric.Config;
using EventCentric.Database;
using EventCentric.EventSourcing;
using EventCentric.Log;
using EventCentric.Messaging;
using EventCentric.NodeFactory.Log;
using EventCentric.Polling;
using EventCentric.Processing;
using EventCentric.Publishing;
using EventCentric.Pulling;
using EventCentric.Repository;
using EventCentric.Serialization;
using EventCentric.Transport;
using EventCentric.Utils;
using Microsoft.Practices.Unity;
using System;
using System.Data.Entity;

namespace EventCentric
{
    public class NodeFactory<TAggregate, THandler>
        where TAggregate : class, IEventSourced
        where THandler : EventProcessor<TAggregate>
    {
        public static INode CreateNode(IUnityContainer container, bool setLocalTime = true, bool setSequentialGuid = true)
        {
            DbConfiguration.SetConfiguration(new TransientFaultHandlingDbConfiguration());

            var eventStoreConfig = EventStoreConfig.GetConfig();
            var pollerConfig = PollerConfig.GetConfig();

            var connectionString = eventStoreConfig.ConnectionString;

            Func<bool, EventStoreDbContext> storeContextFactory = isReadOnly => new EventStoreDbContext(isReadOnly, connectionString);
            Func<bool, EventQueueDbContext> queueContextFactory = isReadOnly => new EventQueueDbContext(isReadOnly, connectionString);

            var serializer = new JsonTextSerializer();
            var time = setLocalTime ? new LocalTimeProvider() as ITimeProvider : new UtcTimeProvider() as ITimeProvider;
            var guid = setSequentialGuid ? new SequentialGuid() as IGuidProvider : new DefaultGuidProvider() as IGuidProvider;

            var subscriptionRepository = new SubscriptionRepository(storeContextFactory, serializer, time);
            var eventDao = new EventDao(queueContextFactory);

            var eventStore = new EventStore<TAggregate>(serializer, storeContextFactory, time, guid);

            var bus = new Bus();
            var log = Logger.ResolvedLogger;

            var http = new HttpLongPoller(bus, log, new TimeSpan(0, 0, pollerConfig.Timeout));

            var buffer = new BufferPool(bus, subscriptionRepository, http, serializer, log, pollerConfig.BufferQueueMaxCount, pollerConfig.EventsToFlushMaxCount);
            var pollster = new Poller(bus, log, buffer);
            var publisher = new Publisher<TAggregate>(bus, log, eventDao, eventStoreConfig.PushMaxCount, eventStoreConfig.PollAttemptsMaxCount);
            var fsm = new Node(bus, log);

            // Register processor dependencies
            container.RegisterInstance<IBus>(bus);
            container.RegisterInstance<IEventStore<TAggregate>>(eventStore);

            var constructor = typeof(THandler).GetConstructor(new[] { typeof(IBus), typeof(ILogger), typeof(IEventStore<TAggregate>) });
            Ensure.CastIsValid(constructor, "Type THandler must have a valid constructor with the following signature: .ctor(IBus, ILogger, IEventStore<T>)");
            var processor = (THandler)constructor.Invoke(new object[] { bus, log, eventStore });

            // Register for DI
            container.RegisterInstance<IEventSource>(publisher);

            return fsm;
        }

        public static INode CreateDenormalizerNode<TDbContext>(IUnityContainer container, bool setLocalTime = true, bool setSequentialGuid = true) where TDbContext : IEventStoreDbContext
        {
            DbConfiguration.SetConfiguration(new TransientFaultHandlingDbConfiguration());

            var eventStoreConfig = EventStoreConfig.GetConfig();
            var pollerConfig = PollerConfig.GetConfig();

            var connectionString = eventStoreConfig.ConnectionString;

            Func<bool, EventStoreDbContext> storeContextFactory = isReadOnly => new EventStoreDbContext(isReadOnly, connectionString);
            Func<bool, EventQueueDbContext> queueContextFactory = isReadOnly => new EventQueueDbContext(isReadOnly, connectionString);

            var serializer = new JsonTextSerializer();
            var time = setLocalTime ? new LocalTimeProvider() as ITimeProvider : new UtcTimeProvider() as ITimeProvider;
            var guid = setSequentialGuid ? new SequentialGuid() as IGuidProvider : new DefaultGuidProvider() as IGuidProvider;


            var eventDao = new EventDao(queueContextFactory);

            var dbContextConstructor = typeof(TDbContext).GetConstructor(new[] { typeof(bool), typeof(string) });
            Ensure.CastIsValid(dbContextConstructor, "Type TDbContext must have a constructor with the following signature: ctor(bool, string)");
            Func<bool, IEventStoreDbContext> dbContextFactory = isReadOnly => (TDbContext)dbContextConstructor.Invoke(new object[] { isReadOnly, connectionString });
            var eventStore = new EventStore<TAggregate>(serializer, dbContextFactory, time, guid);

            var bus = new Bus();
            var log = Logger.ResolvedLogger;

            var http = new HttpLongPoller(bus, log, new TimeSpan(0, 0, pollerConfig.Timeout));

            var subscriptionRepository = new SubscriptionRepository(storeContextFactory, serializer, time);
            var buffer = new BufferPool(bus, subscriptionRepository, http, serializer, log, pollerConfig.BufferQueueMaxCount, pollerConfig.EventsToFlushMaxCount);
            var pollster = new Poller(bus, log, buffer);
            var publisher = new Publisher<TAggregate>(bus, log, eventDao, eventStoreConfig.PushMaxCount, eventStoreConfig.PollAttemptsMaxCount);
            var fsm = new Node(bus, log);

            // Register processor dependencies
            container.RegisterInstance<IBus>(bus);
            container.RegisterInstance<IEventStore<TAggregate>>(eventStore);

            var processorConstructor = typeof(THandler).GetConstructor(new[] { typeof(IBus), typeof(ILogger), typeof(IEventStore<TAggregate>) });
            Ensure.CastIsValid(processorConstructor, "Type THandler must have a valid constructor with the following signature: .ctor(IBus, ILogger, IEventStore<T>)");
            var processor = (THandler)processorConstructor.Invoke(new object[] { bus, log, eventStore });

            // Register for DI
            container.RegisterInstance<IEventSource>(publisher);

            return fsm;
        }
    }
}
