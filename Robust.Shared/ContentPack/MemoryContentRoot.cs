using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using Robust.Shared.Utility;

namespace Robust.Shared.ContentPack;

/// <summary>
///     A content root stored in memory, backed by a dictionary.
/// </summary>
public sealed class MemoryContentRoot : IContentRoot, IDisposable
{
    // _files includes directories.
    private readonly Dictionary<ResPath, byte[]> _files = new();
    // _directories is only paths to added folders, not their contents.
    private readonly HashSet<ResPath> _directories = new();

    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>
    ///     Adds a file to the content root, or updates it if that path already exists.
    /// </summary>
    /// <param name="relPath">The relative path of the file.</param>
    /// <param name="data">The data byte array to store in the content root. Stored as is, without being copied or cloned.</param>
    public void AddOrUpdateFile(ResPath relPath, byte[] data)
    {
        // Just in case, we ensure it's a clean relative path.
        relPath = relPath.Clean().ToRelativePath();
        var isFolder = relPath.IsDirectory;

        _lock.EnterWriteLock();
        try
        {
            _files[relPath] = data;

            if (isFolder)
                _directories.Add(relPath);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    ///     Remove a file from this content root.
    /// </summary>
    /// <param name="relPath">The relative path to the file.</param>
    /// <returns></returns>
    public bool RemoveFile(ResPath relPath)
    {
        _lock.EnterWriteLock();
        try
        {
            return _files.Remove(relPath) && _directories.Remove(relPath);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    ///     Removes ALL files from this content root.
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _files.Clear();
            _directories.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool FileExists(ResPath relPath)
    {
        _lock.EnterReadLock();
        try
        {
            return _files.ContainsKey(relPath);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public bool TryGetFile(ResPath relPath, [NotNullWhen(true)] out Stream? stream)
    {
        _lock.EnterReadLock();
        try
        {
            if (!_files.TryGetValue(relPath, out var data))
            {
                stream = null;
                return false;
            }

            // Non-writable stream, as this needs to be thread-safe.
            stream = new MemoryStream(data, false);
            return true;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public IEnumerable<ResPath> FindFiles(ResPath path, bool recursive = true)
    {
        _lock.EnterReadLock();
        try
        {
            foreach (var (file, _) in _files)
            {
                if (file.TryRelativeTo(path, out _) && (!recursive || file.IsDirectlyUnder(path)))
                    yield return file;
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public IEnumerable<ResPath> FindDirectories(ResPath path, Func<ResPath, bool>? predicate = null)
    {
        _lock.EnterReadLock();
        try
        {
            foreach (var directory in _directories)
            {
                if (predicate != null && !predicate(directory))
                    continue;

                if (directory.TryRelativeTo(path, out _))
                    yield return directory;
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> GetRelativeFilePaths()
    {
        _lock.EnterReadLock();
        try
        {
            foreach (var (file, _) in _files)
            {
                yield return file.ToString();
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    ///     Enumerates all files and their resource paths on this content root.
    /// </summary>
    /// <remarks>Do not modify or keep around the returned byte array, it's meant to be read-only.</remarks>
    public IEnumerable<(ResPath relPath, byte[] data)> GetAllFiles()
    {
        _lock.EnterReadLock();
        try
        {
            foreach (var (p, d) in _files)
            {
                yield return (p, d);
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public void Mount()
    {
        // Nada. We don't need to perform any special logic here.
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
    }
}
