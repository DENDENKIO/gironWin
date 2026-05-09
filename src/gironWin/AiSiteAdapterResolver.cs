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

        public IAiSiteAdapter Resolve(string url)
        {
            return _adapters.FirstOrDefault(x => x.CanHandle(url));
        }
    }
}
