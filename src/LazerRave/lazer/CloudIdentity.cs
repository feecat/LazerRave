using osu.Game.Online.API.Requests.Responses;
using osu.Framework.Bindables;

namespace LazerRave.Lazer;

internal static class CloudIdentity
{
    public static APIUser Create(CloudUser? user, string? avatarUrl, string localPlayer) => new()
    {
        Id = user is null ? -1 : (int)Math.Clamp(user.Uid, 1, int.MaxValue),
        Username = user is null ? localPlayer : string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
        AvatarUrl = avatarUrl,
    };

    public static void Apply(Bindable<APIUser> target, APIUser identity, string localPlayer)
    {
        // APIUser equality only compares IDs, so same-account profile changes require a local identity transition.
        if (target.Value.Equals(identity) && (target.Value.Username != identity.Username || target.Value.AvatarUrl != identity.AvatarUrl))
            target.Value = Create(null, null, localPlayer);
        target.Value = identity;
    }
}
