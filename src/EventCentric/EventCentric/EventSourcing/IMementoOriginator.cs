﻿namespace EventCentric.EventSourcing
{
    /// <summary>
    /// Defines that the implementor can create memento objects (snapshots), 
    /// that can be used to recreate the current state of an aggregate.
    /// </summary>
    public interface IMementoOriginator
    {
        /// <summary>
        /// Saves the object's state to an opaque memento object (a snapshot) 
        /// that can be used to restore the state.
        /// </summary>
        /// <returns>An opaque memento object that can be used to restore the state.</returns>
        IMemento SaveToMemento();
    }

    public interface IMemento
    {
        /// <summary>
        /// The version of the <see cref="IEventSourced"/>
        /// </summary>
        long Version { get; }
    }
}
