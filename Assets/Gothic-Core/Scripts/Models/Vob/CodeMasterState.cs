namespace Gothic.Core.Models.Vob
{
    /// <summary>
    /// A zCCodeMaster's progress (CodeMasterHandler): per slave position whether it was pressed (in its turn, if
    /// ordered), and the presses since the code was last judged. Gothic's save format has no field for it, the engine
    /// keeps it (OpenGothic CodeMaster::save): CodeMastersSaveSection saves it.
    /// </summary>
    public class CodeMasterState
    {
        public bool[] PressedSlots;
        public int PressCount;

        public CodeMasterState(int slaveCount)
        {
            PressedSlots = new bool[slaveCount];
        }
    }
}
