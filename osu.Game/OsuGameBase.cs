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
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osuTK.Input;

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

        private Container content;

        protected override Container<Drawable> Content => content;

        protected Storage Storage { get; set; }

        [Cached]
        [Cached(typeof(IBindable<RulesetInfo>))]
        protected readonly Bindable<RulesetInfo> Ruleset = new Bindable<RulesetInfo>();

        // todo: move this to SongSelect once Screen has the ability to unsuspend.
        [Cached]
        [Cached(Type = typeof(IBindable<IReadOnlyList<Mod>>))]
        protected readonly Bindable<IReadOnlyList<Mod>> Mods = new Bindable<IReadOnlyList<Mod>>(Array.Empty<Mod>());

        protected Bindable<WorkingBeatmap> Beatmap { get; private set; } // cached via load() method

        private Bindable<bool> fpsDisplayVisible;

        public virtual Version AssemblyVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version();

        public bool IsDeployedBuild => AssemblyVersion.Major > 0;

        public string Version
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
            Resources.AddStore(new DllResourceStore(@"osu.Game.Resources.dll"));

            dependencies.Cache(contextFactory = new DatabaseContextFactory(Storage));

            var largeStore = new LargeTextureStore(Host.CreateTextureLoaderStore(new NamespacedResourceStore<byte[]>(Resources, @"Textures")));
            largeStore.AddStore(Host.CreateTextureLoaderStore(new OnlineStore()));
            dependencies.Cache(largeStore);

            dependencies.CacheAs(this);
            dependencies.Cache(LocalConfig);

            AddFont(Resources, @"Fonts/osuFont");
            AddFont(Resources, @"Fonts/Exo2.0-Medium");
            AddFont(Resources, @"Fonts/Exo2.0-MediumItalic");

            AddFont(Resources, @"Fonts/Noto-Basic");
            AddFont(Resources, @"Fonts/Noto-Hangul");
            AddFont(Resources, @"Fonts/Noto-CJK-Basic");
            AddFont(Resources, @"Fonts/Noto-CJK-Compatibility");

            AddFont(Resources, @"Fonts/Exo2.0-Regular");
            AddFont(Resources, @"Fonts/Exo2.0-RegularItalic");
            AddFont(Resources, @"Fonts/Exo2.0-SemiBold");
            AddFont(Resources, @"Fonts/Exo2.0-SemiBoldItalic");
            AddFont(Resources, @"Fonts/Exo2.0-Bold");
            AddFont(Resources, @"Fonts/Exo2.0-BoldItalic");
            AddFont(Resources, @"Fonts/Exo2.0-Light");
            AddFont(Resources, @"Fonts/Exo2.0-LightItalic");
            AddFont(Resources, @"Fonts/Exo2.0-Black");
            AddFont(Resources, @"Fonts/Exo2.0-BlackItalic");

            AddFont(Resources, @"Fonts/Venera");
            AddFont(Resources, @"Fonts/Venera-Light");
            AddFont(Resources, @"Fonts/Venera-Medium");

            dependencies.Cache(SkinManager = new SkinManager(Storage, contextFactory, Host, Audio, new NamespacedResourceStore<byte[]>(Resources, "Skins/Legacy")));
            dependencies.CacheAs<ISkinSource>(SkinManager);

            if (API == null) API = new APIAccess(LocalConfig);

            dependencies.CacheAs(API);

            var defaultBeatmap = new DummyWorkingBeatmap(Audio, Textures);

            dependencies.Cache(RulesetStore = new RulesetStore(contextFactory));
            dependencies.Cache(FileStore = new FileStore(contextFactory, Storage));

            // ordering is important here to ensure foreign keys rules are not broken in ModelStore.Cleanup()
            dependencies.Cache(ScoreManager = new ScoreManager(RulesetStore, () => BeatmapManager, Storage, API, contextFactory, Host));
            dependencies.Cache(BeatmapManager = new BeatmapManager(Storage, contextFactory, RulesetStore, API, Audio, Host, defaultBeatmap));

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

            BeatmapManager.ItemRemoved += i => ScoreManager.Delete(getBeatmapScores(i), true);
            BeatmapManager.ItemAdded += i => ScoreManager.Undelete(getBeatmapScores(i), true);

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
            Beatmap.BindValueChanged(b => ScheduleAfterChildren(() =>
            {
                // compare to last beatmap as sometimes the two may share a track representation (optimisation, see WorkingBeatmap.TransferTo)
                if (b.OldValue?.TrackLoaded == true && b.OldValue?.Track != b.NewValue?.Track)
                    b.OldValue.RecycleTrack();
            }));

            dependencies.CacheAs<IBindable<WorkingBeatmap>>(Beatmap);
            dependencies.CacheAs(Beatmap);

            FileStore.Cleanup();

            if (API is APIAccess apiAccess)
                AddInternal(apiAccess);
            AddInternal(RulesetConfigCache);

            GlobalActionContainer globalBinding;

            MenuCursorContainer = new MenuCursorContainer { RelativeSizeAxes = Axes.Both };
            MenuCursorContainer.Child = globalBinding = new GlobalActionContainer(this)
            {
                RelativeSizeAxes = Axes.Both,
                Child = content = new OsuTooltipContainer(MenuCursorContainer.Cursor) { RelativeSizeAxes = Axes.Both }
            };

            base.Content.Add(CreateScalingContainer().WithChild(MenuCursorContainer));

            KeyBindingStore.Register(globalBinding);
            dependencies.Cache(globalBinding);

            PreviewTrackManager previewTrackManager;
            dependencies.Cache(previewTrackManager = new PreviewTrackManager());
            Add(previewTrackManager);
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

            if (Storage == null)
                Storage = host.Storage;

            if (LocalConfig == null)
                LocalConfig = new OsuConfigManager(Storage);
        }

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
        }

        private class OsuUserInputManager : UserInputManager
        {
            protected override MouseButtonEventManager CreateButtonManagerFor(MouseButton button)
            {
                switch (button)
                {
                    case MouseButton.Right:
                        return new RightMouseManager(button);
                }

                return base.CreateButtonManagerFor(button);
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
    }
}
