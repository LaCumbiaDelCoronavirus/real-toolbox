using JetBrains.Annotations;
using Robust.Shared.Utility;

namespace Robust.Shared.ContentPack
{
    /// <summary>
    /// Provides an API for reading and manipulation of files and directories, inside of a rooted folder.
    /// </summary>
    [PublicAPI]
    public interface IWritableDirProvider : IDirProvider
    {
        /// <summary>
        /// Creates a directory. If the directory exists, does nothing.
        /// </summary>
        /// <param name="path">Path of directory to create.</param>
        void CreateDir(ResPath path);

        /// <summary>
        /// Deletes a file or directory. If the file or directory
        /// does not exist, does nothing.
        /// </summary>
        /// <param name="path">Path of object to delete.</param>
        void Delete(ResPath path);

        /// <summary>
        /// Tests if a file or directory exists.
        /// </summary>
        /// <param name="path">Path to test.</param>
        /// <returns>If the object exists.</returns>

        /// <summary>
        /// Attempts to rename a file.
        /// </summary>
        /// <param name="oldPath">Path of the file to rename.</param>
        /// <param name="newPath">New name of the file.</param>
        /// <returns></returns>
        void Rename(ResPath oldPath, ResPath newPath);
    }
}
