// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using osu.Framework.Logging;
using osu.Game.Database;

namespace osu.Game.Rulesets
{
    public class RulesetStore : DatabaseBackedStore, IDisposable
    {
        private const string ruleset_library_prefix = "osu.Game.Rulesets";

        private readonly Dictionary<Assembly, Type> loadedAssemblies = new Dictionary<Assembly, Type>();

        public RulesetStore(IDatabaseContextFactory factory)
            : base(factory)
        {
            // On android in release configuration assemblies are loaded from the apk directly into memory.
            // We cannot read assemblies from cwd, so should check loaded assemblies instead.
            loadFromAppDomain();
            loadFromDisk();
            addMissingRulesets();

            AppDomain.CurrentDomain.AssemblyResolve += resolveRulesetAssembly;
        }

        /// <summary>
        /// Retrieve a ruleset using a known ID.
        /// </summary>
        /// <param name="onlineID">The ruleset's internal ID.</param>
        /// <returns>A ruleset, if available, else null.</returns>
        public RealmWrapper<RulesetInfo> GetRuleset(int onlineID) => available.FirstOrDefault(r => r.OnlineID == onlineID).Wrap(ContextFactory);

        /// <summary>
        /// Retrieve a ruleset using a known short name.
        /// </summary>
        /// <param name="shortName">The ruleset's short name.</param>
        /// <returns>A ruleset, if available, else null.</returns>
        public RealmWrapper<RulesetInfo> GetRuleset(string shortName) => available.FirstOrDefault(r => r.ShortName == shortName).Wrap(ContextFactory);

        /// <summary>
        /// All available rulesets.
        /// </summary>
        public IEnumerable<RealmWrapper<RulesetInfo>> AvailableRulesets => available.AsEnumerable().Select(r => r.Wrap(ContextFactory));

        private IEnumerable<RulesetInfo> available => ContextFactory.Get().All<RulesetInfo>().Where(r => r.Available);

        private Assembly resolveRulesetAssembly(object sender, ResolveEventArgs args) => loadedAssemblies.Keys.FirstOrDefault(a => a.FullName == args.Name);

        private void addMissingRulesets()
        {
            using (var usage = ContextFactory.GetForWrite())
            {
                var context = usage.Context;

                var instances = loadedAssemblies.Values.Select(r => (Ruleset)Activator.CreateInstance(r)).ToList();

                //add all legacy rulesets first to ensure they have exclusive choice of primary key.
                foreach (var r in instances.Where(r => r is ILegacyRuleset))
                {
                    if (context.All<RulesetInfo>().SingleOrDefault(dbRuleset => dbRuleset.OnlineID == r.RulesetInfo.OnlineID) == null)
                        context.Add(r.RulesetInfo);
                }

                //add any other modes
                foreach (var r in instances.Where(r => !(r is ILegacyRuleset)))
                {
                    if (context.All<RulesetInfo>().FirstOrDefault(ri => ri.InstantiationInfo == r.RulesetInfo.InstantiationInfo) == null)
                        context.Add(r.RulesetInfo);
                }

                //perform a consistency check
                foreach (var r in context.All<RulesetInfo>())
                {
                    try
                    {
                        var instanceInfo = ((Ruleset)Activator.CreateInstance(Type.GetType(r.InstantiationInfo, asm =>
                        {
                            // for the time being, let's ignore the version being loaded.
                            // this allows for debug builds to successfully load rulesets (even though debug rulesets have a 0.0.0 version).
                            asm.Version = null;
                            return Assembly.Load(asm);
                        }, null))).RulesetInfo;

                        r.Name = instanceInfo.Name;
                        r.ShortName = instanceInfo.ShortName;
                        r.InstantiationInfo = instanceInfo.InstantiationInfo;

                        r.Available = true;
                    }
                    catch
                    {
                        r.Available = false;
                    }
                }
            }
        }

        private void loadFromAppDomain()
        {
            foreach (var ruleset in AppDomain.CurrentDomain.GetAssemblies())
            {
                string rulesetName = ruleset.GetName().Name;

                if (!rulesetName.StartsWith(ruleset_library_prefix, StringComparison.InvariantCultureIgnoreCase) || ruleset.GetName().Name.Contains("Tests"))
                    continue;

                addRuleset(ruleset);
            }
        }

        private void loadFromDisk()
        {
            try
            {
                string[] files = Directory.GetFiles(Environment.CurrentDirectory, $"{ruleset_library_prefix}.*.dll");

                foreach (string file in files.Where(f => !Path.GetFileName(f).Contains("Tests")))
                    loadRulesetFromFile(file);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Could not load rulesets from directory {Environment.CurrentDirectory}");
            }
        }

        private void loadRulesetFromFile(string file)
        {
            var filename = Path.GetFileNameWithoutExtension(file);

            if (loadedAssemblies.Values.Any(t => t.Namespace == filename))
                return;

            try
            {
                addRuleset(Assembly.LoadFrom(file));
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Failed to load ruleset {filename}");
            }
        }

        private void addRuleset(Assembly assembly)
        {
            if (loadedAssemblies.ContainsKey(assembly))
                return;

            try
            {
                loadedAssemblies[assembly] = assembly.GetTypes().First(t => t.IsPublic && t.IsSubclassOf(typeof(Ruleset)));
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Failed to add ruleset {assembly}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolveRulesetAssembly;
        }
    }
}
