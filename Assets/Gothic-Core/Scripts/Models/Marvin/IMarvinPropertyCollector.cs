using System.Collections.Generic;

namespace Gothic.Core.Models.Marvin
{
    public interface IMarvinPropertyCollector
    {
        IEnumerable<object> CollectMarvinInspectorProperties();
    }
}
