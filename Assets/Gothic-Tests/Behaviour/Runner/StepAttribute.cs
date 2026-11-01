using System;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// Binds a method to the steps whose text matches the pattern (a regular expression over the whole text, the
    /// keyword not included). Groups become the method's parameters (string, int, float, bool, enum). The method returns
    /// void or IEnumerator (it may wait).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class StepAttribute : Attribute
    {
        public readonly string Pattern;

        /// <summary>
        /// Kept although no feature uses it yet (FeatureCatalogTest reports unused steps otherwise). Say why in a
        /// comment.
        /// </summary>
        public bool IsReserved { get; set; }

        public StepAttribute(string pattern)
        {
            Pattern = pattern;
        }
    }
}
