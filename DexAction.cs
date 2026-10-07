using System;

namespace AVEIN
{
    /// <summary>
    /// The kind of action the Aven Dex agent wants to perform.
    /// Each value maps to an action tag the AI can output.
    /// </summary>
    public enum DexActionType
    {
        /// <summary>[READ_FILE:path] — read a file's contents.</summary>
        ReadFile,

        /// <summary>[WRITE_FILE:path] <<<content>>> [END_WRITE] — create or overwrite a file.</summary>
        WriteFile,

        /// <summary>[EDIT_FILE:path] old:<<<...>>> new:<<<...>>> [END_EDIT] — exact string replacement.</summary>
        EditFile,

        /// <summary>[RUN_CMD:command] — run a shell command inside the project folder.</summary>
        RunCmd,

        /// <summary>[TODO:item1 | item2 | item3] — track a multi-step task list.</summary>
        Todo,

        /// <summary>[DONE] — signal that the task is complete.</summary>
        Done
    }

    /// <summary>
    /// A single parsed action from the AI's response.
    /// </summary>
    public sealed class DexAction
    {
        /// <summary>What the agent wants to do.</summary>
        public DexActionType Type { get; set; }

        /// <summary>
        /// The primary argument for the action.
        /// For ReadFile/WriteFile/EditFile: the relative file path.
        /// For RunCmd: the full command string.
        /// For Todo: the pipe-separated list of todo items.
        /// For Done: empty.
        /// </summary>
        public string Arg { get; set; }

        /// <summary>
        /// For WriteFile: the full file content to write.
        /// For EditFile: the new string to replace OldString with.
        /// For all other actions: null.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// For EditFile: the exact old string that must exist uniquely in the file.
        /// For all other actions: null.
        /// </summary>
        public string OldString { get; set; }

        public DexAction()
        {
            Arg = string.Empty;
            Content = null;
            OldString = null;
        }

        public override string ToString()
        {
            switch (Type)
            {
                case DexActionType.ReadFile: return $"[READ_FILE: {Arg}]";
                case DexActionType.WriteFile: return $"[WRITE_FILE: {Arg}] ({Content?.Length ?? 0} chars)";
                case DexActionType.EditFile: return $"[EDIT_FILE: {Arg}] ({(OldString?.Length ?? 0)} → {Content?.Length ?? 0} chars)";
                case DexActionType.RunCmd: return $"[RUN_CMD: {Arg}]";
                case DexActionType.Todo: return $"[TODO: {Arg}]";
                case DexActionType.Done: return "[DONE]";
                default: return base.ToString();
            }
        }
    }
}
