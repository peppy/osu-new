// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Cursor;
using osu.Game.Online.API;
using osu.Framework.Graphics.Performance;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input;
using osu.Game.Audio;
using osu.Game.Database;
using osu.Game.Input;
using osu.Game.Input.Bindings;
using osu.Game.IO;
using osu.Game.Overlays;
using osu.Game.Resources;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osuTK.Input;
using RuntimeInfo = osu.Framework.RuntimeInfo;

namespace osu.Game
{
    /// <summary>
    /// The most basic <see cref="Game"/> that can be used to host osu! components and systems.
    /// Unlike <see cref="OsuGame"/>, this class will not load any kind of UI, allowing it to be used
    /// for provide dependencies to test cases without interfering with them.
    /// </summary>
    public class OsuGameBase : Framework.Game, ICanAcceptFiles
    {
        public const string CLIENT_STREAM_NAME = "lazer";

        public const int SAMPLE_CONCURRENCY = 6;

        protected OsuConfigManager LocalConfig;

        protected BeatmapManager BeatmapManager;

        protected ScoreManager ScoreManager;

        protected SkinManager SkinManager;

        protected RulesetStore RulesetStore;

        protected FileStore FileStore;

        protected KeyBindingStore KeyBindingStore;

        protected SettingsStore SettingsStore;

        protected RulesetConfigCache RulesetConfigCache;

        protected IAPIProvider API;

        protected MenuCursorContainer MenuCursorContainer;

        protected MusicController MusicController;

        private Container content;

        protected override Container<Drawable> Content => content;

        protected Storage Storage { get; set; }

        [Cached]
        [Cached(typeof(IBindable<RulesetInfo>))]
        protected readonly Bindable<RulesetInfo> Ruleset = new Bindable<RulesetInfo>();

        // todo: move this to SongSelect once Screen has the ability to unsuspend.
        [Cached]
        [Cached(typeof(IBindable<IReadOnlyList<Mod>>))]
        protected readonly Bindable<IReadOnlyList<Mod>> SelectedMods = new Bindable<IReadOnlyList<Mod>>(Array.Empty<Mod>());

        /// <summary>
        /// Mods available for the current <see cref="Ruleset"/>.
        /// </summary>
        public readonly Bindable<Dictionary<ModType, IReadOnlyList<Mod>>> AvailableMods = new Bindable<Dictionary<ModType, IReadOnlyList<Mod>>>();

        protected Bindable<WorkingBeatmap> Beatmap { get; private set; } // cached via load() method

        private Bindable<bool> fpsDisplayVisible;

        public virtual Version AssemblyVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version();

        /// <summary>
        /// MD5 representation of the game executable.
        /// </summary>
        public string VersionHash { get; private set; }

        public bool IsDeployedBuild => AssemblyVersion.Major > 0;

        public virtual string Version
        {
            get
            {
                if (!IsDeployedBuild)
                    return @"local " + (DebugUtils.IsDebugBuild ? @"debug" : @"release");

                var version = AssemblyVersion;
                return $@"{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        public OsuGameBase()
        {
            Name = @"osu!lazer";
        }

        private DependencyContainer dependencies;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent) =>
            dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        private DatabaseContextFactory contextFactory;

        protected override UserInputManager CreateUserInputManager() => new OsuUserInputManager();

        [BackgroundDependencyLoader]
        private void load()
        {
            try
            {
                using (var str = File.OpenRead(typeof(OsuGameBase).Assembly.Location))
                    VersionHash = str.ComputeMD5Hash();
            }
            catch
            {
                // special case for android builds, which can't read DLLs from a packed apk.
                // should eventually be handled in a better way.
                VersionHash = $"{Version}-{RuntimeInfo.OS}".ComputeMD5Hash();
            }

            Resources.AddStore(new DllResourceStore(OsuResources.ResourceAssembly));

            dependencies.Cache(contextFactory = new DatabaseContextFactory(Storage));

            dependencies.CacheAs(Storage);

            var largeStore = new LargeTextureStore(Host.CreateTextureLoaderStore(new NamespacedResourceStore<byte[]>(Resources, @"Textures")));
            largeStore.AddStore(Host.CreateTextureLoaderStore(new OnlineStore()));
            dependencies.Cache(largeStore);

            dependencies.CacheAs(this);
            dependencies.Cache(LocalConfig);

            AddFont(Resources, @"Fonts/osuFont");

            AddFont(Resources, @"Fonts/Torus-Regular");
            AddFont(Resources, @"Fonts/Torus-Light");
            AddFont(Resources, @"Fonts/Torus-SemiBold");
            AddFont(Resources, @"Fonts/Torus-Bold");

            AddFont(Resources, @"Fonts/Noto-Basic");
            AddFont(Resources, @"Fonts/Noto-Hangul");
            AddFont(Resources, @"Fonts/Noto-CJK-Basic");
            AddFont(Resources, @"Fonts/Noto-CJK-Compatibility");
            AddFont(Resources, @"Fonts/Noto-Thai");

            AddFont(Resources, @"Fonts/Venera-Light");
            AddFont(Resources, @"Fonts/Venera-Bold");
            AddFont(Resources, @"Fonts/Venera-Black");

            Audio.Samples.PlaybackConcurrency = SAMPLE_CONCURRENCY;

            dependencies.Cache(SkinManager = new SkinManager(Storage, contextFactory, Host, Audio, new NamespacedResourceStore<byte[]>(Resources, "Skins/Legacy")));
            dependencies.CacheAs<ISkinSource>(SkinManager);

            API ??= new APIAccess(LocalConfig);

            dependencies.CacheAs(API);

            var defaultBeatmap = new DummyWorkingBeatmap(Audio, Textures);

            dependencies.Cache(RulesetStore = new RulesetStore(contextFactory, Storage));
            dependencies.Cache(FileStore = new FileStore(contextFactory, Storage));

            // ordering is important here to ensure foreign keys rules are not broken in ModelStore.Cleanup()
            dependencies.Cache(ScoreManager = new ScoreManager(RulesetStore, () => BeatmapManager, Storage, API, contextFactory, Host));
            dependencies.Cache(BeatmapManager = new BeatmapManager(Storage, contextFactory, RulesetStore, API, Audio, Host, defaultBeatmap, true));

            // this should likely be moved to ArchiveModelManager when another case appears where it is necessary
            // to have inter-dependent model managers. this could be obtained with an IHasForeign<T> interface to
            // allow lookups to be done on the child (ScoreManager in this case) to perform the cascading delete.
            List<ScoreInfo> getBeatmapScores(BeatmapSetInfo set)
            {
                List<ScoreInfo> scores = new List<ScoreInfo>();

                foreach (var beatmap in BeatmapManager.QueryBeatmaps(b => b.BeatmapSet == set))
                    scores.AddRange(ScoreManager.QueryScores(s => s.Beatmap == beatmap));

                return scores;
            }

            BeatmapManager.ItemRemoved.BindValueChanged(i =>
            {
                if (i.NewValue.TryGetTarget(out var item))
                    ScoreManager.Delete(getBeatmapScores(item), true);
            });

            BeatmapManager.ItemUpdated.BindValueChanged(i =>
            {
                if (i.NewValue.TryGetTarget(out var item))
                    ScoreManager.Undelete(getBeatmapScores(item), true);
            });

            var difficultyManager = new BeatmapDifficultyManager();
            dependencies.Cache(difficultyManager);
            AddInternal(difficultyManager);

            dependencies.Cache(KeyBindingStore = new KeyBindingStore(contextFactory, RulesetStore));
            dependencies.Cache(SettingsStore = new SettingsStore(contextFactory));
            dependencies.Cache(RulesetConfigCache = new RulesetConfigCache(SettingsStore));
            dependencies.Cache(new SessionStatics());
            dependencies.Cache(new OsuColour());

            fileImporters.Add(BeatmapManager);
            fileImporters.Add(ScoreManager);
            fileImporters.Add(SkinManager);

            // tracks play so loud our samples can't keep up.
            // this adds a global reduction of track volume for the time being.
            Audio.Tracks.AddAdjustment(AdjustableProperty.Volume, new BindableDouble(0.8));

            Beatmap = new NonNullableBindable<WorkingBeatmap>(defaultBeatmap);

            dependencies.CacheAs<IBindable<WorkingBeatmap>>(Beatmap);
            dependencies.CacheAs(Beatmap);

            FileStore.Cleanup();

            if (API is APIAccess apiAccess)
                AddInternal(apiAccess);
            AddInternal(RulesetConfigCache);

            MenuCursorContainer = new MenuCursorContainer { RelativeSizeAxes = Axes.Both };

            GlobalActionContainer globalBindings;

            MenuCursorContainer.Child = globalBindings = new GlobalActionContainer(this)
            {
                RelativeSizeAxes = Axes.Both,
                Child = content = new OsuTooltipContainer(MenuCursorContainer.Cursor) { RelativeSizeAxes = Axes.Both }
            };

            base.Content.Add(CreateScalingContainer().WithChild(MenuCursorContainer));

            KeyBindingStore.Register(globalBindings);
            dependencies.Cache(globalBindings);

            PreviewTrackManager previewTrackManager;
            dependencies.Cache(previewTrackManager = new PreviewTrackManager());
            Add(previewTrackManager);

            AddInternal(MusicController = new MusicController());
            dependencies.CacheAs(MusicController);

            Ruleset.BindValueChanged(onRulesetChanged);
        }

        private void onRulesetChanged(ValueChangedEvent<RulesetInfo> r)
        {
            var dict = new Dictionary<ModType, IReadOnlyList<Mod>>();

            if (r.NewValue?.Available == true)
            {
                foreach (ModType type in Enum.GetValues(typeof(ModType)))
                    dict[type] = r.NewValue.CreateInstance().GetModsFor(type).ToList();
            }

            if (!SelectedMods.Disabled)
                SelectedMods.Value = Array.Empty<Mod>();
            AvailableMods.Value = dict;
        }

        protected virtual Container CreateScalingContainer() => new DrawSizePreservingFillContainer();

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // TODO: This is temporary until we reimplement the local FPS display.
            // It's just to allow end-users to access the framework FPS display without knowing the shortcut key.
            fpsDisplayVisible = LocalConfig.GetBindable<bool>(OsuSetting.ShowFpsDisplay);
            fpsDisplayVisible.ValueChanged += visible => { FrameStatistics.Value = visible.NewValue ? FrameStatisticsMode.Minimal : FrameStatisticsMode.None; };
            fpsDisplayVisible.TriggerChange();

            FrameStatistics.ValueChanged += e => fpsDisplayVisible.Value = e.NewValue != FrameStatisticsMode.None;
        }

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);

            // may be non-null for certain tests
            Storage ??= host.Storage;

            LocalConfig ??= new OsuConfigManager(Storage);
        }

        protected override Storage CreateStorage(GameHost host, Storage defaultStorage) => new OsuStorage(host, defaultStorage);

        private readonly List<ICanAcceptFiles> fileImporters = new List<ICanAcceptFiles>();

        public async Task Import(params string[] paths)
        {
            var extension = Path.GetExtension(paths.First())?.ToLowerInvariant();

            foreach (var importer in fileImporters)
            {
                if (importer.HandledExtensions.Contains(extension))
                    await importer.Import(paths);
            }
        }

        public string[] HandledExtensions => fileImporters.SelectMany(i => i.HandledExtensions).ToArray();

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            RulesetStore?.Dispose();
            BeatmapManager?.Dispose();

            contextFactory.FlushConnections();
        }

        private class OsuUserInputManager : UserInputManager
        {
            protected override MouseButtonEventManager CreateButtonEventManagerFor(MouseButton button)
            {
                switch (button)
                {
                    case MouseButton.Right:
                        return new RightMouseManager(button);
                }

                return base.CreateButtonEventManagerFor(button);
            }

            private class RightMouseManager : MouseButtonEventManager
            {
                public RightMouseManager(MouseButton button)
                    : base(button)
                {
                }

                public override bool EnableDrag => true; // allow right-mouse dragging for absolute scroll in scroll containers.
                public override bool EnableClick => false;
                public override bool ChangeFocusOnClick => false;
            }
        }

        public void Migrate(string path)
        {
            contextFactory.FlushConnections();
            (Storage as OsuStorage)?.Migrate(path);
        }
    }
}
