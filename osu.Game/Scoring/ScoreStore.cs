﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Platform;
using osu.Game.Database;

namespace osu.Game.Scoring
{
    public class ScoreStore : MutableDatabaseBackedStoreWithFileIncludes<ScoreInfo, ScoreFileInfo>
    {
        public ScoreStore(IDatabaseContextFactory factory, Storage storage)
            : base(factory, storage)
        {
        }
    }
}
