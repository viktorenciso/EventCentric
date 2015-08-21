﻿using EventCentric.Database;
using EventCentric.Repository;
using EventCentric.Repository.Mapping;
using EventCentric.Serialization;
using EventCentric.Utils;
using System;
using System.Linq;
using System.Runtime.Caching;

namespace EventCentric.EventSourcing
{
    public class EventStore<T> : IEventStore<T> where T : class, IEventSourced
    {
        private static readonly string _streamType = typeof(T).Name;

        private readonly ITextSerializer serializer;
        private readonly ITimeProvider time;
        private readonly IGuidProvider guid;
        private readonly ObjectCache cache;

        private readonly Func<Guid, IMemento, T> originatorAggregateFactory;

        private readonly Action<T, EventStoreDbContext> cacheMementoAndPublishStream;
        private readonly Func<Guid, Tuple<IMemento, DateTime?>> getMementoFromCache;
        private readonly Action<Guid> markCacheAsStale;

        private readonly Func<EventStoreDbContext> contextFactory;
        private readonly ISubscriptionInboxWriter subscriptionWriter;

        public EventStore(ITextSerializer serializer, Func<EventStoreDbContext> contextFactory, ISubscriptionInboxWriter subscriptionWriter, ITimeProvider time, IGuidProvider guid)
        {
            Ensure.NotNull(serializer, "serializer");
            Ensure.NotNull(contextFactory, "contextFactory");
            Ensure.NotNull(time, "time");
            Ensure.NotNull(subscriptionWriter, "subscriptionWriter");
            Ensure.NotNull(guid, "guid");

            this.serializer = serializer;
            this.contextFactory = contextFactory;
            this.time = time;
            this.subscriptionWriter = subscriptionWriter;
            this.guid = guid;
            this.cache = new MemoryCache(_streamType);

            /// TODO: could be replaced with a compiled lambda to make it more performant.
            var mementoConstructor = typeof(T).GetConstructor(new[] { typeof(Guid), typeof(IMemento) });
            Ensure.CastIsValid(mementoConstructor, "Type T must have a constructor with the following signature: .ctor(Guid, IMemento)");

            this.originatorAggregateFactory = (id, memento) => (T)mementoConstructor.Invoke(new object[] { id, memento });

            this.cacheMementoAndPublishStream = (T originator, EventStoreDbContext context) =>
            {
                var key = originator.Id.ToString();
                var now = this.time.Now;
                var memento = ((IMementoOriginator)originator).SaveToMemento();

                // Cache in Sql Server
                var serializedMemento = this.serializer.Serialize(memento);

                context.AddOrUpdate(
                    finder: () => context.Streams.Where(s => s.StreamId == originator.Id).SingleOrDefault(),
                    add: () => new StreamEntity
                    {
                        StreamId = originator.Id,
                        Version = originator.Version,
                        Memento = serializedMemento,
                        CreationDate = now
                    },
                    update: stream =>
                    {
                        stream.Version = originator.Version;
                        stream.Memento = serializedMemento;
                        stream.CreationDate = now;
                    });

                // Cache in memory
                this.cache.Set(
                    key: key,
                    value: new Tuple<IMemento, DateTime?>(memento, now),
                    policy: new CacheItemPolicy { AbsoluteExpiration = this.time.OffSetNow.AddMinutes(30) });
            };

            this.getMementoFromCache = id =>
            {
                var memento = (Tuple<IMemento, DateTime?>)this.cache.Get(id.ToString());
                if (memento != null && memento.Item2.HasValue)
                    return memento;
                else
                {
                    // Return from SQL Server;
                    using (var context = this.contextFactory.Invoke())
                    {
                        var stream = context.Streams.Where(s => s.StreamId == id).SingleOrDefault();

                        if (stream != null)
                            return new Tuple<IMemento, DateTime?>(this.serializer.Deserialize<IMemento>(stream.Memento), null);
                        else
                            return null;
                    }
                }
            };
            this.markCacheAsStale = id =>
            {
                var key = id.ToString();
                var item = (Tuple<IMemento, DateTime?>)this.cache.Get(key);
                if (item != null && item.Item2.HasValue)
                {
                    item = new Tuple<IMemento, DateTime?>(item.Item1, null);
                    this.cache.Set(
                        key,
                        item,
                        new CacheItemPolicy { AbsoluteExpiration = this.time.OffSetNow.AddMinutes(30) });
                }
            };
        }

        public T Get(Guid id)
        {
            var cachedMemento = this.getMementoFromCache(id);
            if (cachedMemento != null)
                return this.originatorAggregateFactory.Invoke(id, cachedMemento.Item1);
            else
                throw new StreamNotFoundException(id, _streamType);
        }

        public int Save(T eventSourced, IEvent correlatedEvent)
        {
            var pendingEvents = eventSourced.PendingEvents;
            if (pendingEvents.Length == 0)
                throw new ArgumentOutOfRangeException("pendingEvents");

            using (var context = this.contextFactory.Invoke())
            {
                var versions = context.Events
                                      .Where(e => e.StreamId == eventSourced.Id)
                                      .AsCachedAnyEnumerable();

                var currentVersion = 0;
                if (versions.Any())
                    currentVersion = versions.Max(e => e.Version);

                if (currentVersion + 1 != pendingEvents[0].Version)
                    throw new EventStoreConcurrencyException();

                foreach (var @event in pendingEvents)
                {
                    context.Events.Add(
                        new EventEntity
                        {
                            StreamId = eventSourced.Id,
                            Version = eventSourced.Version,
                            EventId = this.guid.NewGuid,
                            EventType = @event.GetType().Name,
                            CorrelationId = correlatedEvent.EventId,
                            CreationDate = this.time.Now,
                            Payload = this.serializer.Serialize(@event)
                        });
                }

                this.subscriptionWriter.LogIncomingEvent(correlatedEvent, context);

                try
                {
                    this.cacheMementoAndPublishStream(eventSourced, context);

                    context.SaveChanges();

                    return context.Streams.Max(s => s.StreamCollectionVersion);
                }
                catch
                {
                    this.markCacheAsStale(eventSourced.Id);
                    throw;
                }
            }
        }

        public T Find(Guid id)
        {
            var cachedMemento = this.getMementoFromCache(id);
            if (cachedMemento != null)
                return this.originatorAggregateFactory.Invoke(id, cachedMemento.Item1);
            else
                return null;
        }
    }
}
