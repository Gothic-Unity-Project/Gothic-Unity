namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// One step of a scenario as its feature file has it: keyword (Given, When, Then, And, But, *), the text with an
    /// outline's values filled in, and its line. Steps bind by text only - the keyword is for reading.
    /// </summary>
    public class ScenarioStep
    {
        public readonly string Keyword;
        public readonly string Text;
        public readonly int Line;
        public readonly bool IsBackground;

        public ScenarioStep(string keyword, string text, int line, bool isBackground)
        {
            Keyword = keyword.Trim();
            Text = text;
            Line = line;
            IsBackground = isBackground;
        }

        public override string ToString()
        {
            return $"{Keyword} {Text} (line {Line})";
        }
    }
}
