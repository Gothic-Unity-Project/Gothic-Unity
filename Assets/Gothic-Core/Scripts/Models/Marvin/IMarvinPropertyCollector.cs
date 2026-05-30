using System.Collections.Generic;

namespace GUZ.Core.Models.Marvin
{
    public interface IMarvinPropertyCollector
    {
        IEnumerable<object> CollectMarvinInspectorProperties();
    }
}
