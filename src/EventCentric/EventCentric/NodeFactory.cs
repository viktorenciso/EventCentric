﻿using EventCentric.Database;
using EventCentric.EventSourcing;
using EventCentric.Messaging;
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
    public class NodeFactory<T> where T : class, IEventSourced
    {
        public static INode CreateNode(IUnityContainer container, Func<IUnityContainer, EventProcessor<T>> processorFactory, bool setLocalTime = true, bool setSequentialGuid = true)
        {
            DbConfiguration.SetConfiguration(new TransientFaultHandlingDbConfiguration());

            var connectionProvider = ConnectionManager.GetConnectionProvider();
            var connectionString = connectionProvider.ConnectionString;

            var serializer = new JsonTextSerializer();
            var time = setLocalTime ? new LocalTimeProvider() as ITimeProvider : new UtcTimeProvider() as ITimeProvider;
            var guid = setSequentialGuid ? new SequentialGuid() as IGuidProvider : new DefaultGuidProvider() as IGuidProvider;

            var streamDao = new StreamDao(() => new ReadOnlyStreamDbContext(connectionString));
            var subscriptionDao = new SubscriptionDao(() => new ReadOnlySubscriptionDbContext(connectionString));
            var subscriptionWriter = new SubscriptionInboxWriter(() => new EventStoreDbContext(connectionString), time, serializer);

            var eventStore = new EventStore<T>(serializer, () => new EventStoreDbContext(connectionString), subscriptionWriter, time, guid);

            var bus = new Bus();

            var publisher = new EventPublisher<T>(bus, streamDao, serializer);
            var puller = new EventPuller(bus, subscriptionDao, subscriptionWriter, new HttpPoller(), serializer);
            var fsm = new Node(bus);

            // Register processor dependencies
            container.RegisterInstance<IBus>(bus);
            container.RegisterInstance<IEventStore<T>>(eventStore);
            container.RegisterInstance<ISubscriptionInboxWriter>(subscriptionWriter);
            var processor = processorFactory.Invoke(container);

            // Register all in bus
            bus.Register(publisher, puller, processor, fsm);

            // Register for DI
            container.RegisterInstance<IEventSource>(publisher);

            return fsm;
        }

        public static INode CreateNode(IUnityContainer container, Func<IUnityContainer, EventProcessor<T>> processorFactory, Func<string, IEventStoreDbContext> storeContextFactory, bool setLocalTime = true, bool setSequentialGuid = true)
        {
            DbConfiguration.SetConfiguration(new TransientFaultHandlingDbConfiguration());

            var connectionProvider = ConnectionManager.GetConnectionProvider();
            var connectionString = connectionProvider.ConnectionString;

            var serializer = new JsonTextSerializer();
            var time = setLocalTime ? new LocalTimeProvider() as ITimeProvider : new UtcTimeProvider() as ITimeProvider;
            var guid = setSequentialGuid ? new SequentialGuid() as IGuidProvider : new DefaultGuidProvider() as IGuidProvider;

            var streamDao = new StreamDao(() => new ReadOnlyStreamDbContext(connectionString));
            var subscriptionDao = new SubscriptionDao(() => new ReadOnlySubscriptionDbContext(connectionString));
            var subscriptionWriter = new SubscriptionInboxWriter(() => new EventStoreDbContext(connectionString), time, serializer);

            var eventStore = new EventStore<T>(serializer, () => storeContextFactory.Invoke(connectionString), subscriptionWriter, time, guid);

            var bus = new Bus();

            var publisher = new EventPublisher<T>(bus, streamDao, serializer);
            var puller = new EventPuller(bus, subscriptionDao, subscriptionWriter, new HttpPoller(), serializer);
            var fsm = new Node(bus);

            // Register processor dependencies
            container.RegisterInstance<IBus>(bus);
            container.RegisterInstance<IEventStore<T>>(eventStore);
            container.RegisterInstance<ISubscriptionInboxWriter>(subscriptionWriter);
            var processor = processorFactory.Invoke(container);

            // Register all in bus
            bus.Register(publisher, puller, processor, fsm);

            // Register for DI
            container.RegisterInstance<IEventSource>(publisher);

            return fsm;
        }
    }
}
