﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using Newtonsoft.Json;
using osu.Game.Database;
using osu.Game.IO.Serialization;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using Realms;

namespace osu.Game.Beatmaps
{
    [Serializable]
    public class BeatmapInfo : RealmObject, IEquatable<BeatmapInfo>, IJsonSerializable, IHasPrimaryKey
    {
        [PrimaryKey]
        public string ID { get; set; }

        public int BeatmapVersion;

        [JsonProperty("id")]
        public int? OnlineBeatmapID { get; set; }

        [JsonIgnore]
        public int BeatmapSetInfoID { get; set; }

        public int StatusInt { get; set; } = (int)BeatmapSetOnlineStatus.None;

        public BeatmapSetOnlineStatus Status
        {
            get => (BeatmapSetOnlineStatus)StatusInt;
            set => StatusInt = (int)value;
        }

        // [System.ComponentModel.DataAnnotations.Required]
        public BeatmapSetInfo BeatmapSet { get; set; }

        public BeatmapMetadata Metadata { get; set; }

        [JsonIgnore]
        public int BaseDifficultyID { get; set; }

        public BeatmapDifficulty BaseDifficulty { get; set; }

        [Ignored]
        public BeatmapMetrics Metrics { get; set; }

        [Ignored]
        public BeatmapOnlineInfo OnlineInfo { get; set; }

        /// <summary>
        /// The playable length in milliseconds of this beatmap.
        /// </summary>
        public double Length { get; set; }

        [Backlink(nameof(ScoreInfo.Beatmap))]
        public IQueryable<ScoreInfo> Scores { get; }

        /// <summary>
        /// The most common BPM of this beatmap.
        /// </summary>
        public double BPM { get; set; }

        public string Path { get; set; }

        [JsonProperty("file_sha2")]
        public string Hash { get; set; }

        [JsonIgnore]
        public bool Hidden { get; set; }

        /// <summary>
        /// MD5 is kept for legacy support (matching against replays, osu-web-10 etc.).
        /// </summary>
        [JsonProperty("file_md5")]
        public string MD5Hash { get; set; }

        // General
        public double AudioLeadIn { get; set; }
        public bool Countdown { get; set; } = true;
        public float StackLeniency { get; set; } = 0.7f;
        public bool SpecialStyle { get; set; }

        public int RulesetID { get; set; }

        public RulesetInfo Ruleset { get; set; }

        public bool LetterboxInBreaks { get; set; }
        public bool WidescreenStoryboard { get; set; }

        // Editor
        // This bookmarks stuff is necessary because DB doesn't know how to store int[]
        [JsonIgnore]
        public string StoredBookmarks
        {
            get => string.Join(",", Bookmarks);
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    Bookmarks = new int[0];
                    return;
                }

                Bookmarks = value.Split(',').Select(v =>
                {
                    bool result = int.TryParse(v, out int val);
                    return new { result, val };
                }).Where(p => p.result).Select(p => p.val).ToArray();
            }
        }

        [Ignored]
        public int[] Bookmarks { get; set; } = new int[0];

        public double DistanceSpacing { get; set; }
        public int BeatDivisor { get; set; }
        public int GridSize { get; set; }
        public double TimelineZoom { get; set; }

        // Metadata
        public string Version { get; set; }

        [JsonProperty("difficulty_rating")]
        public double StarDifficulty { get; set; }

        [JsonIgnore]
        public DifficultyRating DifficultyRating
        {
            get
            {
                var rating = StarDifficulty;

                if (rating < 2.0) return DifficultyRating.Easy;
                if (rating < 2.7) return DifficultyRating.Normal;
                if (rating < 4.0) return DifficultyRating.Hard;
                if (rating < 5.3) return DifficultyRating.Insane;
                if (rating < 6.5) return DifficultyRating.Expert;

                return DifficultyRating.ExpertPlus;
            }
        }

        public override string ToString() => $"{Metadata} [{Version}]".Trim();

        public bool Equals(BeatmapInfo other)
        {
            //todo: unnecessary?

            if (ID == null || other?.ID == null)
                // one of the two BeatmapInfos we are comparing isn't sourced from a database.
                // fall back to reference equality.
                return ReferenceEquals(this, other);

            return ID == other?.ID;
        }

        public bool AudioEquals(BeatmapInfo other) => other != null && BeatmapSet != null && other.BeatmapSet != null &&
                                                      BeatmapSet.Hash == other.BeatmapSet.Hash &&
                                                      (Metadata ?? BeatmapSet.Metadata).AudioFile == (other.Metadata ?? other.BeatmapSet.Metadata).AudioFile;

        public bool BackgroundEquals(BeatmapInfo other) => other != null && BeatmapSet != null && other.BeatmapSet != null &&
                                                           BeatmapSet.Hash == other.BeatmapSet.Hash &&
                                                           (Metadata ?? BeatmapSet.Metadata).BackgroundFile == (other.Metadata ?? other.BeatmapSet.Metadata).BackgroundFile;

        /// <summary>
        /// Returns a shallow-clone of this <see cref="BeatmapInfo"/>.
        /// </summary>
        public BeatmapInfo Clone() => (BeatmapInfo)MemberwiseClone();
    }
}
