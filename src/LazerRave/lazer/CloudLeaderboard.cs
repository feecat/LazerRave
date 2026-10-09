using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.Rankings.Tables;
using osu.Game.Screens.OnlinePlay.Match.Components;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace LazerRave.Lazer;

internal partial class CloudLeaderboard(CloudClient client, Guid match, bool compact) : CompositeDrawable
{
    [Cached] private readonly OverlayColourProvider colours = new(OverlayColourScheme.Purple);
    private readonly CloudRoomMap users = new();
    private readonly Dictionary<Guid, CloudScoreRow> rows = [];
    private FillFlowContainer list = null!;
    private SectionHeader header = null!;
    private OsuSpriteText status = null!;
    private long version = -1;
    private bool connected;
    public CloudMember[] DisplayedMembers { get; private set; } = [];
    protected override void LoadComplete()
    {
        base.LoadComplete();
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Color4Extensions.FromHex("211c2a") },
            new GridContainer
            {
                RelativeSizeAxes = Axes.Both, Padding = new MarginPadding(12),
                RowDimensions = [new Dimension(GridSizeMode.Absolute, 42), new Dimension(GridSizeMode.Absolute, 38), new Dimension()],
                Content = new Drawable?[][]
                {
                    [header = new SectionHeader(compact ? "Live ranking" : "Results")],
                    [status = new OsuSpriteText { RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: compact ? 14 : 18) }],
                    [new OsuScrollContainer { RelativeSizeAxes = Axes.Both, Child = list = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 4),
                        LayoutDuration = 250, LayoutEasing = Easing.OutQuint,
                    } }],
                },
            },
        };
    }
    protected override void Update()
    {
        base.Update();
        var room = client.Room;
        if (version == room?.Version && connected == client.Connected) return;
        version = room?.Version ?? -1; connected = client.Connected;
        if (room?.MatchId != match) { status.Text = "Round unavailable"; return; }
        var completed = room.State == "results" || room.State == "lobby" && room.Results is { Length: > 0 };
        // Preserve departed players in the completed round, without adding new lobby entrants.
        var members = room.Results is { Length: > 0 } ? room.Results : room.Members;
        DisplayedMembers = members.OrderByDescending(value => value.ExScore).ThenBy(value => value.Misses)
            .ThenByDescending(value => value.MaxCombo).ThenBy(value => value.Id).ToArray();
        header.DetailsText.Value = $"{members.Length} players";
        status.Text = !connected ? "Connection lost · last received scores" : completed ? "Round complete · EX SCORE" : $"{members.Count(value => value.Finished)} / {members.Length} finished · EX SCORE";
        foreach (var removed in rows.Keys.Except(members.Select(value => value.Id)).ToArray()) { list.Remove(rows[removed], true); rows.Remove(removed); }
        int rank = 0;
        for (int index = 0; index < DisplayedMembers.Length; index++)
        {
            var member = DisplayedMembers[index];
            if (index == 0 || member.ExScore != DisplayedMembers[index - 1].ExScore) rank = index + 1;
            if (!rows.TryGetValue(member.Id, out var row)) { rows[member.Id] = row = new CloudScoreRow(compact); list.Add(row); }
            row.Set(member, users.User(member, client), rank, member.Id == client.User?.Id, completed);
            list.SetLayoutPosition(row, index);
        }
    }
}

internal partial class CloudScoreRow(bool compact) : CompositeDrawable
{
    private UpdateableAvatar avatar = null!;
    private OsuSpriteText rank = null!, score = null!, details = null!;
    private TruncatingSpriteText name = null!;
    private Box progress = null!, highlight = null!;
    private CloudMember? previous;
    protected override void LoadComplete()
    {
        base.LoadComplete(); RelativeSizeAxes = Axes.X; Height = compact ? 98 : 94;
        InternalChildren = new Drawable[]
        {
            new TableRowBackground { RelativeSizeAxes = Axes.Both },
            highlight = new Box { RelativeSizeAxes = Axes.Y, Width = 3, Colour = Color4.Cyan, Alpha = 0 },
            rank = new OsuSpriteText { Position = new Vector2(8, 14), Font = OsuFont.Numeric.With(size: 20) },
            avatar = new UpdateableAvatar(isInteractive: false) { Position = new Vector2(38, 9), RelativeSizeAxes = Axes.None, Size = new Vector2(32) },
            name = new TruncatingSpriteText { Position = new Vector2(78, 11), RelativeSizeAxes = Axes.X, Width = 1, Padding = new MarginPadding { Right = 82 }, Font = OsuFont.GetFont(size: 17) },
            score = new OsuSpriteText { Position = new Vector2(38, 43), Font = OsuFont.Numeric.With(size: 23), Colour = Color4.White },
            details = new OsuSpriteText { Position = new Vector2(38, 72), Font = OsuFont.GetFont(size: compact ? 11 : 16) },
            new Container { Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, RelativeSizeAxes = Axes.X, Height = 2,
                Child = progress = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Cyan, Width = 0 } },
        };
    }
    public void Set(CloudMember member, osu.Game.Online.API.Requests.Responses.APIUser user, int position, bool self, bool completed)
    {
        if (!IsLoaded) { Schedule(() => Set(member, user, position, self, completed)); return; }
        rank.Text = position.ToString(); name.Text = member.DisplayName;
        score.Text = $"{member.ExScore:N0}";
        string state = member.Disconnected ? "Disconnected" : member.Aborted ? "DNF" : member.Finished ? ClearName(member.ClearType) : "Playing";
        details.Text = compact ? $"{member.Combo}x · MAX {member.MaxCombo} · MISS {member.Misses} · {state}" : $"UID {member.Uid} · COMBO {member.Combo} · MAX {member.MaxCombo} · MISS {member.Misses} · {state}";
        details.Colour = member.Disconnected || member.Aborted ? Color4.Orange : member.Finished ? Color4.LightGreen : Color4.White;
        highlight.Alpha = self ? 1 : 0;
        progress.ResizeWidthTo((float)member.Progress, 120);
        if (previous?.AvatarUrl != member.AvatarUrl || previous?.Uid != member.Uid || previous?.DisplayName != member.DisplayName) { avatar.User = null; avatar.User = user; }
        previous = member;
    }
    private static string ClearName(int value) => value switch { 1 => "Failed", 2 => "Easy", 3 => "Normal", 4 => "Hard", 5 => "Full combo", _ => "Finished" };
}
