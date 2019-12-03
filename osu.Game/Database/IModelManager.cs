﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Realms;

namespace osu.Game.Database
{
    /// <summary>
    /// Represents a model manager that publishes events when <typeparamref name="TModel"/>s are added or removed.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    public interface IModelManager<TModel>
        where TModel : RealmObject, IHasPrimaryKey
    {
        event Action<RealmWrapper<TModel>> ItemAdded;

        event Action<RealmWrapper<TModel>> ItemRemoved;
    }
}
