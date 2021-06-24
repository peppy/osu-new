// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Realms;

namespace osu.Game.Database
{
    public interface IRealmFactory
    {
        /// <summary>
        /// The main realm context, bound to the update thread.
        /// </summary>
        Realm Context { get; }

        /// <summary>
        /// Get a fresh context for read usage.
        /// </summary>
        RealmContextFactory.RealmUsage GetForRead();

        /// <summary>
        /// Request a context for write usage.
        /// This method may block if a write is already active on a different thread.
        /// </summary>
        /// <returns>A usage containing a usable context.</returns>
        RealmContextFactory.RealmWriteUsage GetForWrite();

        /// <summary>
        /// Wrap a managed realm object in a <see cref="Live{T}"/>, for threadsafe consumption.
        /// </summary>
        /// <param name="item">The item to wrap.</param>
        /// <typeparam name="T">The type of object.</typeparam>
        /// <returns>A wrapped instance of the provided item.</returns>
        public Live<T> ConvertToLive<T>(T item)
            where T : RealmObject, IHasGuidPrimaryKey => new Live<T>(item, this);

        void BindLive(IRealmBindableActions live);
    }
}
