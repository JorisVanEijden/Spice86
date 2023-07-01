namespace Spice86.Core.Emulator.OperatingSystem;

/// <summary>
/// Translates DOS filepaths to host file paths and vice-versa.
/// </summary>
public interface IDosFilePathResolver {
    /// <summary>
    /// Converts the DOS path to a full host path.<br/>
    /// </summary>
    /// <param name="driveMap">THe map between DOS drive letters and host folder paths.</param>
    /// <param name="hostDirectory">The host directory path to use as the current folder.</param>
    /// <param name="dosPath">The file name to convert.</param>
    /// <param name="forCreation">if true, it will try to find the case sensitive match for only the parent of the path</param>
    /// <returns>A string containing the full file path in the host file system, or <c>null</c> if nothing was found.</returns>
    string? ToHostCaseSensitiveFullName(IDictionary<char, string> driveMap, string hostDirectory, string dosPath, bool forCreation);

    /// <summary>
    /// Returns the full host file path, including casing.
    /// </summary>
    /// <param name="dosFilePath">The DOS file path.</param>
    /// <returns>A string containing the host file path, or <c>null</c> if not found.</returns>
    string? TryGetFullHostFileName(string dosFilePath);

    /// <summary>
    /// Prefixes the given filename by either the mapped drive folder or the current folder depending on whether there is
    /// a root in the filename or not.<br/>
    /// Does not convert to case sensitive filename. <br/>
    /// Does not search for the file or folder on disk.
    /// </summary>
    /// <param name="driveMap">The map between DOS drive letters and host folder paths.</param>
    /// <param name="hostDirectory">The host directory to use as the current directory.</param>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the host directory, combined with the DOS file name.</returns>
    string PrefixWithHostDirectory(IDictionary<char, string> driveMap, string hostDirectory, string dosPath);

    /// <summary>
    /// Returns whether the DOS path is absolute.
    /// </summary>
    /// <param name="dosPath">The path to test.</param>
    /// <returns>Whether the DOS path is absolute.</returns>
    bool IsDosPathRooted(string dosPath);

    /// <summary>
    /// Returns the full path to the parent directory.
    /// </summary>
    /// <param name="path">The starting path.</param>
    /// <returns>A string containing the full path to the parent directory, or the original value if not found.</returns>
    string GetFullNameForParentDirectory(string path);
}