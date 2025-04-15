using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Utility;
using TerraFX.Interop.Windows;

namespace Robust.Shared.ContentPack
{
    /// <summary>
    ///     Virtual file system for all disk resources.
    /// </summary>
    [Virtual]
    internal partial class ResourceManager : IResourceManagerInternal
    {
        [Dependency] private readonly IConfigurationManager _config = default!;
        [Dependency] private readonly ILogManager _logManager = default!;

        private (ResPath prefix, IContentRoot root)[] _contentRoots =
            new (ResPath prefix, IContentRoot root)[0];

        private StreamSeekMode _streamSeekMode;
        private readonly object _rootMutateLock = new();

        // Special file names on Windows like serial ports.
        private static readonly Regex BadPathSegmentRegex =
            new("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase);

        // Literally not characters that can't go into filenames on Windows.
        private static readonly Regex BadPathCharacterRegex =
            new("[<>:\"|?*\0\\x01-\\x1f]", RegexOptions.IgnoreCase);


        // Key is the path trying to be resolved, value is the resolved path
        private Dictionary<ResPath, ResPath> _cachedResolvedPaths = new();

        // Paths that couldnt be resolved, so we won't try to resolve them again.
        private HashSet<ResPath> _unresolvablePaths = new();

        protected ISawmill Sawmill = default!;

        /// <inheritdoc />
        public IWritableDirProvider UserData { get; private set; } = default!;

        /// <inheritdoc />
        public virtual void Initialize(string? userData)
        {
            Sawmill = _logManager.GetSawmill("res");

            if (userData != null)
            {
                UserData = new WritableDirProvider(Directory.CreateDirectory(userData));
            }
            else
            {
                UserData = new VirtualWritableDirProvider();
            }

            _config.OnValueChanged(CVars.ResStreamSeekMode, i => _streamSeekMode = (StreamSeekMode)i, true);
        }

        /// <inheritdoc />
        public void MountDefaultContentPack()
        {
            //Assert server only

            var zipPath = _config.GetCVar<string>("resource.pack");

            // no pack in config
            if (string.IsNullOrWhiteSpace(zipPath))
            {
                Sawmill.Warning("No default ContentPack to load in configuration.");
                return;
            }

            MountContentPack(zipPath);
        }

        /// <inheritdoc />
        public void MountContentPack(string pack, ResPath? prefix = null)
        {
            prefix = SanitizePrefix(prefix);

            if (!Path.IsPathRooted(pack))
                pack = PathHelpers.ExecutableRelativeFile(pack);

            var packInfo = new FileInfo(pack);

            if (!packInfo.Exists)
            {
                throw new FileNotFoundException("Specified ContentPack does not exist: " + packInfo.FullName);
            }

            //create new PackLoader

            var loader = new PackLoader(packInfo, Sawmill);
            AddRoot(prefix.Value, loader);
        }

        public void MountContentPack(Stream zipStream, ResPath? prefix = null)
        {
            prefix = SanitizePrefix(prefix);

            var loader = new PackLoader(zipStream, Sawmill);
            AddRoot(prefix.Value, loader);
        }

        public void AddRoot(ResPath prefix, IContentRoot loader)
        {
            lock (_rootMutateLock)
            {
                loader.Mount();

                // When adding a new root we atomically swap it into the existing list.
                // So the list of content roots is thread safe.
                // This does make adding new roots O(n). Oh well.
                var copy = _contentRoots;
                Array.Resize(ref copy, copy.Length + 1);
                copy[^1] = (prefix, loader);
                _contentRoots = copy;
            }
        }

        private static ResPath SanitizePrefix(ResPath? prefix)
        {
            if (prefix == null)
            {
                prefix = ResPath.Root;
            }
            else if (!prefix.Value.IsRooted)
            {
                throw new ArgumentException("Prefix must be rooted.", nameof(prefix));
            }

            return prefix.Value;
        }

        /// <inheritdoc />
        public void MountContentDirectory(string path, ResPath? prefix = null)
        {
            prefix = SanitizePrefix(prefix);

            if (!Path.IsPathRooted(path))
                path = PathHelpers.ExecutableRelativeFile(path);

            var pathInfo = new DirectoryInfo(path);
            if (!pathInfo.Exists)
            {
                throw new DirectoryNotFoundException("Specified directory does not exist: " + pathInfo.FullName);
            }

            var loader = new DirLoader(pathInfo, _logManager.GetSawmill("res"), _config.GetCVar(CVars.ResCheckPathCasing));
            AddRoot(prefix.Value, loader);
        }

        /// <inheritdoc />
        public Stream ContentFileRead(string path)
        {
            return ContentFileRead(new ResPath(path));
        }

        /// <inheritdoc />
        public Stream ContentFileRead(ResPath path)
        {
            if (TryContentFileRead(path, out var fileStream))
            {
                return fileStream;
            }

            throw new FileNotFoundException($"Path does not exist in the VFS: '{path}'");
        }

        /// <inheritdoc />
        public bool TryContentFileRead(string path, [NotNullWhen(true)] out Stream? fileStream)
        {
            return TryContentFileRead(new ResPath(path), out fileStream);
        }

        /// <inheritdoc />
        public bool TryContentFileRead(ResPath? path, [NotNullWhen(true)] out Stream? fileStream)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (!path.Value.IsRooted)
            {
                throw new ArgumentException($"Path '{path}' must be rooted", nameof(path));
            }
#if DEBUG
            if (!IsPathValid(path.Value))
            {
                throw new FileNotFoundException($"Path '{path}' contains invalid characters/filenames.");
            }
#endif

            if (path.Value.CanonPath.EndsWith(ResPath.Separator))
            {
                // This is a folder, not a file.
                fileStream = null;
                return false;
            }

            foreach (var (prefix, root) in _contentRoots)
            {
                if (!path.Value.TryRelativeTo(prefix, out var relative))
                {
                    continue;
                }

                if (root.TryGetFile(relative.Value, out var stream))
                {
                    fileStream = WrapStream(stream);
                    return true;
                }
            }

            fileStream = null;
            return false;
        }

        /// <summary>
        /// Apply <see cref="_streamSeekMode"/> to the provided stream.
        /// </summary>
        private Stream WrapStream(Stream stream)
        {
            switch (_streamSeekMode)
            {
                case StreamSeekMode.None:
                    return stream;

                case StreamSeekMode.ForceSeekable:
                    if (stream.CanSeek)
                        return stream;

                    var ms = new MemoryStream(stream.CopyToArray(), writable: false);
                    stream.Dispose();
                    return ms;

                case StreamSeekMode.ForceNonSeekable:
                    if (!stream.CanSeek)
                        return stream;

                    return new NonSeekableStream(stream);

                default:
                    throw new InvalidOperationException();
            }
        }

        /// <inheritdoc />
        public bool ContentFileExists(string path)
        {
            return ContentFileExists(new ResPath(path));
        }

        /// <inheritdoc />
        public bool ContentFileExists(ResPath path)
        {
            if (TryContentFileRead(path, out var stream))
            {
                stream.Dispose();
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public IEnumerable<ResPath> ContentFindFiles(string path, bool recursive = true)
        {
            return ContentFindFiles(new ResPath(path), recursive);
        }

        public IEnumerable<string> ContentGetDirectoryEntries(ResPath path)
        {
            ArgumentNullException.ThrowIfNull(path, nameof(path));

            if (!path.IsRooted)
                throw new ArgumentException("Path is not rooted", nameof(path));

            // If we don't do this, TryRelativeTo won't work correctly.
            if (!path.CanonPath.EndsWith("/"))
                path = new ResPath(path.CanonPath + "/");

            var entries = new HashSet<string>();

            foreach (var (prefix, root) in _contentRoots)
            {
                if (!path.TryRelativeTo(prefix, out var relative))
                {
                    continue;
                }

                entries.UnionWith(root.GetEntries(relative.Value));
            }

            // We have to add mount points too.
            // e.g. during development, /Assemblies/ is a mount point,
            // and there's no explicit /Assemblies/ folder in Resources.
            // So we need to manually add it since the previous pass won't catch it at all.
            foreach (var (prefix, _) in _contentRoots)
            {
                if (!prefix.TryRelativeTo(path, out var relative))
                    continue;

                // Return first relative segment, unless it's literally just "." (identical path).
                var segments = relative.Value.EnumerateSegments();
                if (segments is ["."])
                    continue;

                entries.Add(segments[0] + "/");
            }

            return entries;
        }

        /// <inheritdoc />
        public IEnumerable<ResPath> ContentFindFiles(ResPath? path, bool recursive = true)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (!path.Value.IsRooted)
            {
                throw new ArgumentException("Path is not rooted", nameof(path));
            }

            var alreadyReturnedFiles = new HashSet<ResPath>();

            foreach (var (prefix, root) in _contentRoots)
            {
                if (!path.Value.TryRelativeTo(prefix, out var relative))
                {
                    continue;
                }

                foreach (var filename in root.FindFiles(relative.Value, recursive))
                {
                    var newPath = prefix / filename;
                    if (!alreadyReturnedFiles.Contains(newPath))
                    {
                        alreadyReturnedFiles.Add(newPath);
                        yield return newPath;
                    }
                }
            }
        }

        /// <inheritdoc />
        public IEnumerable<ResPath> ContentFindFilesUnderDirectoriesWithName(ResPath? searchPath, string directoryName)
        {
            searchPath ??= ResPath.Root;

            var matchingDirectories = ContentFindDirectories(searchPath).Where(dir => dir.Filename == directoryName);
            var foundFiles = new List<ResPath>();

            var searchedPaths = new List<ResPath>();

            foreach (var directory in matchingDirectories)
            {
                Sawmill.Info($"Finding files under directory: '{directory.CanonPath}'");

                foundFiles.AddRange(ContentFindFiles(directory.ToRootedPath(), true));
                searchedPaths.Add(directory);
            }

            return foundFiles;
        }

        /// <inheritdoc />
        public IEnumerable<ResPath> ContentFindDirectories(ResPath? path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            if (!path.Value.IsRooted)
                throw new ArgumentException("Path is not rooted", nameof(path));

            var foundFolders = new HashSet<ResPath>();

            foreach (var (prefix, root) in _contentRoots)
            {
                //if (path.Value.CanonPath != prefix.CanonPath && !path.Value.TryRelativeTo(prefix, out var relative))
                //    continue;

                foreach (var directory in root.FindDirectories(path.Value))
                {
                    if (foundFolders.Contains(directory))
                        continue;

                    foundFolders.Add(directory);

                    yield return directory;
                }
            }
        }

        /// <inheritdoc />
        public bool ResolvePath(string rootPathName, ResPath pathToResolve, [NotNullWhen(true)] out ResPath? resolvedPath, ResPath? searchOrigin = null)
        {
            if (_cachedResolvedPaths.TryGetValue(pathToResolve, out var cachedResolvedPath))
            {
                //Sawmill.Info($"Retrieved cached path for '{pathToResolve}'");
                resolvedPath = cachedResolvedPath;
                return true;
            }

            if (_unresolvablePaths.Contains(pathToResolve))
            {
                Sawmill.Warning($"Skipped '{pathToResolve}', as it is cached as unresolvable");
                resolvedPath = null;
                return false;
            }

            searchOrigin ??= ResPath.Root;
            //var attemptedPathsForResolution = new HashSet<ResPath>();

            foreach (var (prefix, root) in _contentRoots)
            {
                foreach (var directory in root.FindDirectories(searchOrigin.Value, dir => dir.Filename == rootPathName))
                {
                    //if (attemptedPathsForResolution.Contains(directory))
                    //    continue;

                    //attemptedPathsForResolution.Add(directory);


                    // see if the path we're trying to resolve is valid
                    var attemptedPath = directory.ToRootedPath() / pathToResolve;

                    if (!root.FileExists(attemptedPath))
                        continue;

                    _cachedResolvedPaths[pathToResolve] = attemptedPath;

                    resolvedPath = attemptedPath;
                    return true;
                };
            }

            _unresolvablePaths.Add(pathToResolve);

            Sawmill.Error($"Failed to resolve any possible path for '{pathToResolve}'!");
            resolvedPath = null;
            return false;
        }

        /// <inheritdoc />
        public bool ResolvePath(string rootPathName, string pathToResolve, [NotNullWhen(true)] out ResPath? resolvedPath, ResPath? searchOrigin = null)
        {
            return ResolvePath(rootPathName, new ResPath(pathToResolve), out resolvedPath, searchOrigin);
        }

        /// <inheritdoc />
        public ResPath GetRootedPathFromRelativePath(ResPath path)
        {
            if (path.IsRooted)
                return path;

            if (!TryGetDiskFilePath(path, out var diskPath))
                throw new ArgumentException($"Path '{path}' does not exist.");

            return new ResPath(diskPath);
        }

        public bool TryGetDiskFilePath(ResPath path, [NotNullWhen(true)] out string? diskPath)
        {
            // loop over each root trying to get the file
            foreach (var (prefix, root) in _contentRoots)
            {
                if (root is not DirLoader dirLoader || !path.TryRelativeTo(prefix, out var tempPath))
                {
                    continue;
                }

                diskPath = dirLoader.GetPath(tempPath.Value);
                if (File.Exists(diskPath))
                    return true;
            }

            diskPath = null;
            return false;
        }

        public void MountStreamAt(MemoryStream stream, ResPath path)
        {
            var loader = new SingleStreamLoader(stream, path.ToRelativePath());
            AddRoot(ResPath.Root, loader);
        }

        public IEnumerable<ResPath> GetContentRoots()
        {
            foreach (var (_, root) in _contentRoots)
            {
                if (root is DirLoader loader)
                {
                    var rootDir = loader.GetPath(new ResPath(@"/"));

                    yield return new ResPath(rootDir);
                }
            }
        }

        internal static bool IsPathValid(ResPath path)
        {
            var asString = path.ToString();
            if (BadPathCharacterRegex.IsMatch(asString))
            {
                return false;
            }

            foreach (var segment in path.CanonPath.Split('/'))
            {
                if (BadPathSegmentRegex.IsMatch(segment))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
