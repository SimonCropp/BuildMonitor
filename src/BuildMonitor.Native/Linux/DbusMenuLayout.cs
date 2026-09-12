/// <summary>
/// The tray menu as dbusmenu sees it: numbered nodes under a root with id 0. Pure, so it is
/// tested without a bus.
/// </summary>
sealed class DbusMenuLayout
{
    public static readonly DbusMenuLayout Empty = new(new(0, null, "", true, false, null, []), []);

    DbusMenuLayout(DbusMenuNode root, List<DbusMenuNode> all)
    {
        Root = root;
        All = all;
        Signature = string.Join("\n", all.Select(_ => $"{_.Id}|{_.ItemId}|{_.Label}|{_.Enabled}|{_.Separator}|{_.Icon}|{_.Children.Count}"));
    }

    public DbusMenuNode Root { get; }

    public IReadOnlyList<DbusMenuNode> All { get; }

    /// <summary>
    /// What the menu is, as text, so two layouts are compared without walking them.
    /// </summary>
    public string Signature { get; }

    public DbusMenuNode? Find(int id) =>
        id == 0 ? Root : All.FirstOrDefault(_ => _.Id == id);

    public static DbusMenuLayout Build(IReadOnlyList<TrayMenuItem> items)
    {
        var all = new List<DbusMenuNode>();
        var next = 1;
        var children = items.Select(_ => Convert(_, all, ref next)).ToList();
        var root = new DbusMenuNode(0, null, "", true, false, null, children);
        return new(root, all);
    }

    static DbusMenuNode Convert(TrayMenuItem item, List<DbusMenuNode> all, ref int next)
    {
        var id = next++;
        var children = new List<DbusMenuNode>();
        if (item.Children is not null)
        {
            foreach (var child in item.Children)
            {
                children.Add(Convert(child, all, ref next));
            }
        }

        var node = new DbusMenuNode(id, item.Separator ? null : item.Id, item.Label, item.Enabled, item.Separator, item.IconName, children);
        all.Add(node);
        return node;
    }
}

record DbusMenuNode(int Id, string? ItemId, string Label, bool Enabled, bool Separator, string? Icon, IReadOnlyList<DbusMenuNode> Children)
{
    /// <summary>
    /// The glyph as PNG, resolved once per node so a property read costs no decoding.
    /// </summary>
    public byte[]? IconPng { get; } = Icon is null ? null : Images.Glyph(Icon, 16);

    public IEnumerable<string> PropertyNames
    {
        get
        {
            if (Separator)
            {
                yield return "type";
                yield break;
            }

            if (Id != 0)
            {
                yield return "label";
                yield return "enabled";
                yield return "visible";
                if (IconPng is not null)
                {
                    yield return "icon-data";
                }
            }

            if (Children.Count > 0)
            {
                yield return "children-display";
            }
        }
    }
}
