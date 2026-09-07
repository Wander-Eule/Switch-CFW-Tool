namespace SwitchCfwTool;

/// <summary>
/// How a log line should read. Shared so services can report through the same channel the
/// main window writes to, instead of each caller inventing its own severity scheme.
/// </summary>
public enum LogKind
{
    /// <summary>Ordinary progress chatter.</summary>
    Normal,

    /// <summary>Something the user should notice that didn't break anything.</summary>
    Info,

    /// <summary>A step completed successfully.</summary>
    Success,

    /// <summary>A step failed.</summary>
    Error
}
