using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Group;

public sealed record GroupLine(string Speaker, string Text, string Emotion = "neutral");

/// <summary>群里有人答应过来：哪一位、什么时候、哪儿；Task 不空就是上门帮工。</summary>
public sealed record GroupMeetup(string Speaker, string When, string Time, string Where, string Summary, string Task = "");

public sealed record GroupTurn(IReadOnlyList<GroupLine> Lines, IReadOnlyDictionary<string, int> Friendship, GroupMeetup? Meetup = null)
{
	public static readonly GroupTurn Empty = new GroupTurn(Array.Empty<GroupLine>(), new Dictionary<string, int>());
}

/// <summary>一位成员放进提示词的资料。</summary>
public sealed record GroupMember(string Name, string Personality, int Hearts, IReadOnlyList<string> RecentTalks, string Mood = "", string Feelings = "", string Gossip = "", string Awareness = "");

public enum GroupSetting
{
	FaceToFace,
	OnMap,
	Phone
}

public sealed record GroupRequest
{
	public string PlayerName { get; init; } = "";

	public IReadOnlyList<GroupMember> Members { get; init; } = Array.Empty<GroupMember>();

	/// <summary>内部名 → 游戏里显示的名字。</summary>
	public IReadOnlyDictionary<string, string> Display { get; init; } = new Dictionary<string, string>();

	public GroupSetting Setting { get; init; }

	public string Location { get; init; } = "";

	public int Time { get; init; } = 600;

	public string Season { get; init; } = "";

	public int Day { get; init; }

	public int Year { get; init; }

	public string Weather { get; init; } = "";

	public string Shared { get; init; } = "";

	public IReadOnlyList<string> Relations { get; init; } = Array.Empty<string>();

	public IReadOnlyList<GroupLine> Transcript { get; init; } = Array.Empty<GroupLine>();

	/// <summary>农夫这一轮说的话；null 表示村民自己往下聊。</summary>
	public string? PlayerText { get; init; }

	public bool Final { get; init; }

	/// <summary>居民群里允许约见时给 AI 的说明；空表示这一轮不能约。</summary>
	public string MeetupRule { get; init; } = "";

	/// <summary>村民自己凑一起聊（不是冲着农夫来的）。</summary>
	public bool AmongThemselves { get; init; }

	/// <summary>农夫离群聊中心几格；负数表示不知道。</summary>
	public int FarmerDistance { get; init; } = -1;

	public bool FarmerJoined { get; init; }

	/// <summary>这次闲聊从农夫最近的哪件事聊起（某位成员听说或亲眼看见的）；空表示照常聊自己的。</summary>
	public string FarmerTopic { get; init; } = "";

	public IReadOnlyList<string> JustJoined { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> Addressed { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> Names => Members.Select(m => m.Name).ToList();

	public bool PlayerSpeaks => !string.IsNullOrWhiteSpace(PlayerText);
}
