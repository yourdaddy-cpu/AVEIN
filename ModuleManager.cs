using System;
using System.Collections.Generic;
using System.Linq;

namespace AVEIN
{
    public sealed class ModuleManager
    {
        private readonly Dictionary<string, IModule> _registered = new Dictionary<string, IModule>();
        private readonly HashSet<string> _initialized = new HashSet<string>();
        private readonly AppConfig _config;

        public ModuleManager(AppConfig config)
        {
            _config = config;
        }

        public void Register(IModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            _registered[module.Id] = module;
        }

        public IModule Get(string moduleId)
        {
            if (!_registered.TryGetValue(moduleId, out var module)) return null;
            if (!_config.IsModuleEnabled(moduleId)) return null;

            if (!_initialized.Contains(moduleId))
            {
                module.Init();
                _initialized.Add(moduleId);
            }
            return module;
        }

        public void ShutdownAll()
        {
            foreach (var id in _initialized.ToList())
                _registered[id].Shutdown();
            _initialized.Clear();
        }
    }
}
