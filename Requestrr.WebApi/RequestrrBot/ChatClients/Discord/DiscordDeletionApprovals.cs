using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.Locale;

namespace Requestrr.WebApi.RequestrrBot.ChatClients.Discord
{
    public enum DeletionMediaType
    {
        Movie, TvShow
    }

    /// <summary>
    /// Sends deletion requests to the members of the configured approval roles and tracks their answers.
    /// Pending requests only live in memory, a restart cancels them instead of risking an unintended deletion.
    /// </summary>
    public class DiscordDeletionApprovals
    {
        private static readonly TimeSpan RequestLifetime = TimeSpan.FromHours(24);

        private readonly ConcurrentDictionary<string, PendingDeletion> _pendingDeletions = new ConcurrentDictionary<string, PendingDeletion>();
        private readonly ILogger _logger;

        public DiscordDeletionApprovals(ILogger logger)
        {
            _logger = logger;
        }

        public async Task RequestApprovalAsync(ComponentInteractionCreateEventArgs e, DiscordSettings settings, DeletionMediaType mediaType, int mediaId, string title, DiscordEmbed embed)
        {
            RemoveExpiredRequests();

            var pending = new PendingDeletion
            {
                Id = Guid.NewGuid().ToString("N"),
                MediaType = mediaType,
                MediaId = mediaId,
                Title = title,
                RequesterId = e.User.Id,
                GuildId = e.Guild.Id,
                ChannelId = e.Channel.Id,
                CreatedAt = DateTime.UtcNow,
            };

            if (_pendingDeletions.Values.Any(x => x.MediaType == mediaType && x.MediaId == mediaId))
            {
                await EditRequesterMessageAsync(e, FormatMessage(Language.Current.DiscordCommandDeleteApprovalSent, title));
                return;
            }

            _pendingDeletions[pending.Id] = pending;

            var approvers = (await e.Guild.GetAllMembersAsync())
                .Where(x => !x.IsBot && x.Id != e.User.Id && HasApprovalRole(x, settings))
                .ToArray();

            var content = FormatMessage(Language.Current.DiscordCommandDeleteApprovalRequest, title, e.User.Username);

            foreach (var approver in approvers)
            {
                try
                {
                    var builder = new DiscordMessageBuilder().WithContent(content).AddEmbed(embed).AddComponents(CreateButtons(pending.Id, false));
                    pending.Messages.Add(await approver.SendMessageAsync(builder));
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, $"Could not send deletion approval request to user {approver.Id}: " + ex.Message);
                }
            }

            if (pending.Messages.IsEmpty)
            {
                _pendingDeletions.TryRemove(pending.Id, out _);
                await EditRequesterMessageAsync(e, Language.Current.DiscordCommandDeleteApprovalNoApprovers);
                return;
            }

            _logger.LogWarning($"User {e.User.Username} ({e.User.Id}) requested the deletion of {mediaType} {mediaId}");
            await EditRequesterMessageAsync(e, FormatMessage(Language.Current.DiscordCommandDeleteApprovalSent, title));
        }

        public async Task HandleAnswerAsync(ComponentInteractionCreateEventArgs e, DiscordClient client, DiscordSettings settings, Func<DeletionMediaType, int, DiscordInteraction, Task<bool>> deleteMedia)
        {
            var id = e.Id.Split("/").Last();
            var approved = e.Id.StartsWith("DAA", StringComparison.InvariantCultureIgnoreCase);

            if (!_pendingDeletions.TryGetValue(id, out var pending) || IsExpired(pending))
            {
                _pendingDeletions.TryRemove(id, out _);
                await EditAnswerMessageAsync(e, id, Language.Current.DiscordCommandDeleteApprovalExpired);
                return;
            }

            //Roles can change after the request was sent, so validate against the current ones
            if (e.User.Id == pending.RequesterId || !await IsApproverAsync(client, pending.GuildId, e.User.Id, settings))
            {
                await EditAnswerMessageAsync(e, id, Language.Current.DiscordCommandDeleteApprovalNotApprover);
                return;
            }

            //Only the first answer wins
            if (!_pendingDeletions.TryRemove(id, out _))
            {
                await EditAnswerMessageAsync(e, id, Language.Current.DiscordCommandDeleteApprovalExpired);
                return;
            }

            var result = Language.Current.DiscordCommandDeleteApprovalDenied;

            if (approved)
            {
                _logger.LogWarning($"User {e.User.Username} ({e.User.Id}) approved the deletion of {pending.MediaType} {pending.MediaId} requested by user {pending.RequesterId}");
                result = await TryDeleteAsync(e, pending, deleteMedia) ? Language.Current.DiscordCommandDeleteApprovalApproved : Language.Current.DiscordCommandDeleteApprovalFailed;
            }
            else
            {
                _logger.LogWarning($"User {e.User.Username} ({e.User.Id}) denied the deletion of {pending.MediaType} {pending.MediaId} requested by user {pending.RequesterId}");
            }

            var content = FormatMessage(result, pending.Title, e.User.Username);

            await EditAnswerMessageAsync(e, id, content);
            await UpdateOtherApproversAsync(pending, e.Message.Id, content);
            await NotifyRequesterAsync(client, pending, content);
        }

        private async Task<bool> TryDeleteAsync(ComponentInteractionCreateEventArgs e, PendingDeletion pending, Func<DeletionMediaType, int, DiscordInteraction, Task<bool>> deleteMedia)
        {
            try
            {
                return await deleteMedia(pending.MediaType, pending.MediaId, e.Interaction);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"Error while deleting {pending.MediaType} {pending.MediaId}: " + ex.Message);
                return false;
            }
        }

        private async Task<bool> IsApproverAsync(DiscordClient client, ulong guildId, ulong userId, DiscordSettings settings)
        {
            try
            {
                var guild = await client.GetGuildAsync(guildId);
                return HasApprovalRole(await guild.GetMemberAsync(userId), settings);
            }
            catch (NotFoundException)
            {
                return false;
            }
        }

        private static bool HasApprovalRole(DiscordMember member, DiscordSettings settings)
        {
            return member.Roles.Any(x => settings.DeleteApprovalRoles.Contains(x.Id.ToString()));
        }

        private async Task UpdateOtherApproversAsync(PendingDeletion pending, ulong answeredMessageId, string content)
        {
            foreach (var message in pending.Messages.Where(x => x.Id != answeredMessageId))
            {
                try
                {
                    await message.ModifyAsync(new DiscordMessageBuilder().WithContent(content).AddEmbeds(message.Embeds).AddComponents(CreateButtons(pending.Id, true)));
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "Could not update deletion approval message: " + ex.Message);
                }
            }
        }

        private async Task NotifyRequesterAsync(DiscordClient client, PendingDeletion pending, string content)
        {
            try
            {
                var channel = await client.GetChannelAsync(pending.ChannelId);
                await channel.SendMessageAsync($"<@{pending.RequesterId}> {content}");
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Could not notify requester of deletion answer: " + ex.Message);
            }
        }

        private static Task EditRequesterMessageAsync(ComponentInteractionCreateEventArgs e, string content)
        {
            var requestedButton = new DiscordButtonComponent(ButtonStyle.Secondary, "0/1/0", Language.Current.DiscordCommandDeleteButton, true);

            return e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbeds(e.Message.Embeds).AddComponents(requestedButton).WithContent(content));
        }

        private static Task EditAnswerMessageAsync(ComponentInteractionCreateEventArgs e, string id, string content)
        {
            return e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbeds(e.Message.Embeds).AddComponents(CreateButtons(id, true)).WithContent(content));
        }

        private static DiscordComponent[] CreateButtons(string id, bool disabled)
        {
            return new DiscordComponent[]
            {
                new DiscordButtonComponent(ButtonStyle.Danger, $"DAA/{id}", Language.Current.DiscordCommandDeleteApprovalApproveButton, disabled),
                new DiscordButtonComponent(ButtonStyle.Secondary, $"DAD/{id}", Language.Current.DiscordCommandDeleteApprovalDenyButton, disabled),
            };
        }

        private static string FormatMessage(string message, string title, string username = "")
        {
            return message.Replace(LanguageTokens.DeletionTitle, title).Replace(LanguageTokens.DeletionUsername, username);
        }

        private void RemoveExpiredRequests()
        {
            foreach (var pending in _pendingDeletions.Values.Where(IsExpired))
            {
                _pendingDeletions.TryRemove(pending.Id, out _);
            }
        }

        private static bool IsExpired(PendingDeletion pending)
        {
            return DateTime.UtcNow - pending.CreatedAt >= RequestLifetime;
        }

        private class PendingDeletion
        {
            public string Id { get; set; }
            public DeletionMediaType MediaType { get; set; }
            public int MediaId { get; set; }
            public string Title { get; set; }
            public ulong RequesterId { get; set; }
            public ulong GuildId { get; set; }
            public ulong ChannelId { get; set; }
            public DateTime CreatedAt { get; set; }
            public ConcurrentBag<DiscordMessage> Messages { get; } = new ConcurrentBag<DiscordMessage>();
        }
    }
}
