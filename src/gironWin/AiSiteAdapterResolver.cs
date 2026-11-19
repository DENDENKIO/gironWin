using System.Collections.Generic;
using System.Linq;

namespace gironWin
{
    public class AiSiteAdapterResolver
    {
        private readonly List<IAiSiteAdapter> _adapters = new()
        {
            new PerplexityAdapter(),
            new GeminiAdapter()
        };
        private readonly Dictionary<IAiSiteAdapter, IAiSiteAdapter> _verifiedAdapters;

        public AiSiteAdapterResolver()
        {
            _verifiedAdapters = _adapters.ToDictionary(
                adapter => adapter,
                adapter => (IAiSiteAdapter)new InputVerifiedAdapter(adapter));
        }

        // Preserve access to the original adapters for diagnostics and setup.
        public IReadOnlyList<IAiSiteAdapter> Adapters => _adapters;

        public IAiSiteAdapter? Resolve(string url)
        {
            var adapter = _adapters.FirstOrDefault(candidate => candidate.CanHandle(url));
            return adapter == null ? null : _verifiedAdapters[adapter];
        }
    }
}
