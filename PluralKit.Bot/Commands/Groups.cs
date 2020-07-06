﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DSharpPlus.Entities;

using PluralKit.Core;

namespace PluralKit.Bot
{
    public class Groups
    {
        private readonly IDatabase _db;

        public Groups(IDatabase db)
        {
            _db = db;
        }

        public async Task CreateGroup(Context ctx)
        {
            ctx.CheckSystem();
            
            var groupName = ctx.RemainderOrNull() ?? throw new PKSyntaxError("You must pass a group name.");
            if (groupName.Length > Limits.MaxGroupNameLength)
                throw new PKError($"Group name too long ({groupName.Length}/{Limits.MaxMemberNameLength} characters).");
            
            await using var conn = await _db.Obtain();
            var newGroup = await conn.CreateGroup(ctx.System.Id, groupName);

            await ctx.Reply($"{Emojis.Success} Group \"**{groupName}**\" (`{newGroup.Hid}`) registered!\nYou can now start adding members to the group:\n- **pk;group {newGroup.Hid} add <members...>**");
        }

        public async Task RenameGroup(Context ctx, PKGroup target)
        {
            ctx.CheckOwnGroup(target);
            
            var newName = ctx.RemainderOrNull() ?? throw new PKSyntaxError("You must pass a new group name.");
            if (newName.Length > Limits.MaxGroupNameLength)
                throw new PKError($"New group name too long ({newName.Length}/{Limits.MaxMemberNameLength} characters).");

            await using var conn = await _db.Obtain();
            await conn.UpdateGroup(target.Id, new GroupPatch {Name = newName});

            await ctx.Reply($"{Emojis.Success} Group name changed from \"**{target.Name}**\" to \"**{newName}**\".");
        }
        
        public async Task GroupDescription(Context ctx, PKGroup target)
        {
            if (ctx.MatchClear())
            {
                ctx.CheckOwnGroup(target);

                var patch = new GroupPatch {Description = Partial<string>.Null()};
                await _db.Execute(conn => conn.UpdateGroup(target.Id, patch));
                await ctx.Reply($"{Emojis.Success} Group description cleared.");
            } 
            else if (!ctx.HasNext())
            {
                if (target.Description == null)
                    if (ctx.System?.Id == target.System)
                        await ctx.Reply($"This group does not have a description set. To set one, type `pk;group {target.Hid} description <description>`.");
                    else
                        await ctx.Reply("This group does not have a description set.");
                else if (ctx.MatchFlag("r", "raw"))
                    await ctx.Reply($"```\n{target.Description}\n```");
                else
                    await ctx.Reply(embed: new DiscordEmbedBuilder()
                        .WithTitle("Group description")
                        .WithDescription(target.Description)
                        .AddField("\u200B", $"To print the description with formatting, type `pk;group {target.Hid} description -raw`." 
                                    + (ctx.System?.Id == target.System ? $" To clear it, type `pk;group {target.Hid} description -clear`." : ""))
                        .Build());
            }
            else
            {
                ctx.CheckOwnGroup(target);

                var description = ctx.RemainderOrNull().NormalizeLineEndSpacing();
                if (description.IsLongerThan(Limits.MaxDescriptionLength))
                    throw Errors.DescriptionTooLongError(description.Length);
        
                var patch = new GroupPatch {Description = Partial<string>.Present(description)};
                await _db.Execute(conn => conn.UpdateGroup(target.Id, patch));
                
                await ctx.Reply($"{Emojis.Success} Group description changed.");
            }
        }

        public async Task ListSystemGroups(Context ctx, PKSystem system)
        {
            if (system == null)
            {
                ctx.CheckSystem();
                system = ctx.System;
            }
            
            // TODO: integrate with the normal "search" system
            await using var conn = await _db.Obtain();

            var groups = (await conn.QueryGroupsInSystem(system.Id)).ToList();
            if (groups.Count == 0)
            {
                if (system.Id == ctx.System?.Id)
                    await ctx.Reply($"This system has no groups. To create one, use the command `pk;group new <name>`.");
                else
                    await ctx.Reply($"This system has no groups.");
                return;
            }

            var title = system.Name != null ? $"Groups of {system.Name} (`{system.Hid}`)" : $"Groups of `{system.Hid}`";
            await ctx.Paginate(groups.ToAsyncEnumerable(), groups.Count, 25, title, Renderer);
            
            Task Renderer(DiscordEmbedBuilder eb, IEnumerable<PKGroup> page)
            {
                var sb = new StringBuilder();
                foreach (var g in page)
                {
                    sb.Append($"[`{g.Hid}`] **{g.Name}**\n");
                }

                eb.WithDescription(sb.ToString());
                eb.WithFooter($"{groups.Count} total");
                return Task.CompletedTask;
            }
        }

        public async Task ShowGroupCard(Context ctx, PKGroup target)
        {
            await using var conn = await _db.Obtain();
            
            var system = await GetGroupSystem(ctx, target, conn);

            var nameField = target.Name;
            if (system.Name != null)
                nameField = $"{nameField} ({system.Name})";

            var eb = new DiscordEmbedBuilder()
                .WithAuthor(nameField)
                .AddField("Description", target.Description)
                .WithFooter($"System ID: {system.Hid} | Group ID: {target.Hid} | Created on {target.Created.FormatZoned(system)}");

            await ctx.Reply(embed: eb.Build());
        }

        private static async Task<PKSystem> GetGroupSystem(Context ctx, PKGroup target, IPKConnection conn)
        {
            var system = ctx.System;
            if (system?.Id == target.System)
                return system;
            return await conn.QuerySystem(target.System)!;
        }
    }
}