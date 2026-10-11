using WolvenKit.Core.Interfaces;

namespace REDbridge;

/// <summary>
/// A folder index over every file in the loaded game archives, so a client can expand the
/// archives one folder at a time. Built once from the flat file lists (about 580,000 paths)
/// and kept until the game is reloaded.
/// </summary>
public sealed class ArchiveTree
{
    public sealed class Folder
    {
        public SortedDictionary<string, Folder> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(IGameFile file, string archive)> Files { get; } = [];
        /// <summary>Files in this folder and every folder below it.</summary>
        public int Count { get; set; }
    }

    public Folder Root { get; } = new();

    public static ArchiveTree Build(IEnumerable<IGameArchive> archives)
    {
        var tree = new ArchiveTree();
        foreach (var archive in archives)
        {
            foreach (var file in archive.Files.Values)
            {
                var parts = file.Name.Split('\\');
                var node = tree.Root;
                node.Count++;
                for (var i = 0; i < parts.Length - 1; i++)
                {
                    if (!node.Folders.TryGetValue(parts[i], out var child))
                    {
                        child = new Folder();
                        node.Folders[parts[i]] = child;
                    }
                    node = child;
                    node.Count++;
                }
                node.Files.Add((file, archive.Name));
            }
        }
        return tree;
    }

    /// <summary>The folder at a backslash path, or null. An empty path is the root.</summary>
    public Folder? Find(string path)
    {
        var node = Root;
        foreach (var part in path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!node.Folders.TryGetValue(part, out var child))
            {
                return null;
            }
            node = child;
        }
        return node;
    }
}
