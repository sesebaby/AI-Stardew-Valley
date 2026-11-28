using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Companions;
using AISV.Core.Dialogue;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace AISV.Game.Companions;

/// <summary>
/// 约见的"找你、陪你"：到点后你在同一张地图上，村民看见你就迎上来；过了一会儿你还在附近的屋里，就推门进来找你；
/// 走到身边主动开口（AI 按约好的事写开场白），之后陪你走一段（默认两小时，最晚 22 点），换地图跟着，矿洞这类地方不进，
/// 你家不够熟在门口等；路上偶尔冒一句。到点说再见，自己回家。都在房主那边动村民。
/// </summary>
public sealed partial class MeetupService
{
	private sealed class Company
	{
		public Company(Meetup meetup, Seeker.Escort escort)
		{
			Meetup = meetup;
			Escort = escort;
		}

		public Meetup Meetup { get; }

		public Seeker.Escort Escort { get; }

		public string LastPlace { get; set; } = "";

		public int Remarks { get; set; }

		public int LastRemark { get; set; } = -1;

		public int LostChecks { get; set; }

		public bool Knocked { get; set; }
	}

	/// <summary>正在找你的（约见 id → 走过来的那位）。</summary>
	private readonly Dictionary<string, Seeker.Escort> _seeking = new Dictionary<string, Seeker.Escort>();

	/// <summary>见到以后陪着你的。</summary>
	private readonly Dictionary<string, Company> _company = new Dictionary<string, Company>();

	/// <summary>已经提示过"在门口等你"的约见。</summary>
	private readonly HashSet<string> _knocked = new HashSet<string>();

	public const string EndCompanyOp = "meetup.endCompany";

	/// <summary>这位村民在和这边的农夫聊天（陪着走时停下来）。</summary>
	public Func<string, bool>? Talking { get; set; }

	/// <summary>陪着走到新地方时冒的一句（村民、地方名 → AI 写的话，写不成 null）。</summary>
	public Func<NPC, string, Task<string?>>? Remark { get; set; }

	private bool CompanyOn => _config().MeetupCompany;

	/// <summary>这位村民正陪着这边的农夫。</summary>
	public bool IsWithMe(string npc) => All().Any(m => Mine(m) && m.State == MeetupStates.Kept && m.ClosedDay == Today && m.CompanyUntil > Game1.timeOfDay
		&& string.Equals(m.Npc, npc, StringComparison.OrdinalIgnoreCase));

	/// <summary>这边的农夫今天正被陪着的约（手机上"让他先回去"）。</summary>
	public IReadOnlyList<Meetup> WithMe() => All().Where(m => Mine(m) && m.State == MeetupStates.Kept && m.ClosedDay == Today && m.CompanyUntil > Game1.timeOfDay).ToList();

	/// <summary>你说"先到这儿吧"：村民道别回家。</summary>
	public void EndCompany(string id)
	{
		if (CompanionNet.Remote)
		{
			CompanionNet.Net!.ToHost(EndCompanyOp, new IdAsk(id));
			if (Find(id) is Meetup m)
			{
				m.CompanyUntil = Game1.timeOfDay;
				Version++;
			}
			return;
		}
		if (Find(id) is Meetup mine && _company.ContainsKey(id))
		{
			EndOne(mine, PlanReasons.FarmerCancelled, Tr.Get("meetup.byeEarly", "好，那今天就到这儿，下次再一起！|嗯，你去忙吧，今天很开心。"));
		}
	}

	private void ClearCompany()
	{
		foreach (Seeker.Escort e in _seeking.Values)
		{
			e.Release();
		}
		foreach (Company c in _company.Values)
		{
			c.Escort.Release();
		}
		_seeking.Clear();
		_company.Clear();
		_knocked.Clear();
	}

	/// <summary>见到之前：同一张地图看见你就迎上来，过了一会儿去附近的屋里找你；返回 true 表示正在找（先别按距离算见到）。</summary>
	private bool Seek(Meetup m, NPC npc, Farmer player, ScheduleOverlay o)
	{
		if (!CompanyOn || m.Owner == MeetupRules.StoryOwner)
		{
			return false;
		}
		if (_seeking.TryGetValue(m.Id, out Seeker.Escort? escort))
		{
			Seeker.Result r = escort.Tick(player, anywhere: false);
			if (r == Seeker.Result.Beside)
			{
				_seeking.Remove(m.Id);
				Keep(m, npc, o);
				return true;
			}
			if (r is Seeker.Result.Refused or Seeker.Result.Lost)
			{
				_seeking.Remove(m.Id);
				escort.Release();
				_log.Debug($"约见 {m.Id}：{npc.Name} 找不过去（{r}），原地等。");
			}
			return r is Seeker.Result.Walking or Seeker.Result.Entered;
		}
		if (o.State != OverlayStates.Arrived || Clock.Minutes(Game1.timeOfDay) < Clock.Minutes(m.Time) - MeetupRules.KeptEarlyMinutes)
		{
			return false;
		}
		GameLocation? there = player.currentLocation;
		bool sees = npc.currentLocation == there && Vector2.Distance(npc.Tile, player.Tile) <= CompanyRules.SeeTiles;
		bool look = !sees && _config().SeekIndoors && CompanyRules.TimeToSeek(m.Time, Game1.timeOfDay) && npc.currentLocation != there && Seeker.CanReach(npc, there);
		if (!sees && !look)
		{
			Knock(m, npc, player);
			return false;
		}
		_seeking[m.Id] = new Seeker.Escort(npc);
		npc.doEmote(Character.exclamationEmote);
		_log.Info($"约见 {m.Id}：{npc.Name} {(sees ? "看见你了，迎上来" : "去" + there?.NameOrUniqueName + "找你")}。");
		return true;
	}

	/// <summary>约在农场、你在屋里但村民不够熟进不了门：提示一次"在门口等你"。</summary>
	private void Knock(Meetup m, NPC npc, Farmer player)
	{
		if (player.currentLocation is FarmHouse house && npc.currentLocation is Farm && !Seeker.MayEnter(house, npc) && _knocked.Add(m.Id))
		{
			Tell(m, Tr.Get("meetup.atDoor", "{{npc}}到了，在门口等你。", ("npc", NpcName(m.Npc))));
		}
	}

	/// <summary>见到了：开始陪着走（不陪的话照旧再待一小时）。</summary>
	private int StartCompany(Meetup m, NPC npc)
	{
		int end = CompanyRules.End(Game1.timeOfDay);
		m.CompanyUntil = end;
		_company[m.Id] = new Company(m, new Seeker.Escort(npc)) { LastPlace = npc.currentLocation?.NameOrUniqueName ?? "" };
		return end;
	}

	/// <summary>每 15 tick：陪着的人跟着走、换地图、冒一句、到点道别。</summary>
	private void TickCompany()
	{
		foreach (Company c in _company.Values.ToList())
		{
			Meetup m = c.Meetup;
			NPC npc = c.Escort.Npc;
			Farmer? player = CompanionNet.FarmerOf(m.Farmer);
			ScheduleOverlay? o = _schedules.Find(m.OverlayId);
			if (player?.currentLocation == null || o == null || !OverlayStates.IsHolding(o.State) || npc.currentLocation == null)
			{
				EndOne(m, PlanReasons.LostTrack, null);
				continue;
			}
			if (Game1.timeOfDay >= m.CompanyUntil)
			{
				EndOne(m, PlanReasons.TimeUp, Tr.Get("meetup.bye", "今天跟你待着挺开心的，我先回去啦。|时间不早了，我得回去了，下次再约！|今天就到这儿吧，谢谢你陪我。"));
				continue;
			}
			if (player.isInBed.Value || Game1.eventUp || Talking?.Invoke(m.Npc) == true && m.Farmer == CompanionNet.Me)
			{
				npc.Halt();
				continue;
			}
			Seeker.Result r = c.Escort.Tick(player, anywhere: true, closeTiles: 2);
			switch (r)
			{
				case Seeker.Result.Refused when CompanyRules.NoFollow(player.currentLocation.NameOrUniqueName) || player.currentLocation is MineShaft or VolcanoDungeon:
					EndOne(m, PlanReasons.FarmerLeft, Tr.Get("meetup.noFollow", "那里我就不去了，今天先到这儿吧！|我在这儿就跟你分开啦，注意安全。"));
					continue;
				case Seeker.Result.Refused:
					// 你家不够熟：在门口等你出来。
					if (!c.Knocked)
					{
						c.Knocked = true;
						Tell(m, Tr.Get("meetup.waitOutside", "{{npc}}在门口等你出来。", ("npc", NpcName(m.Npc))));
					}
					continue;
				case Seeker.Result.Lost:
					if (++c.LostChecks > 40)
					{
						EndOne(m, PlanReasons.LostTrack, null);
					}
					continue;
			}
			c.LostChecks = 0;
			c.Knocked = false;
			string place = npc.currentLocation.NameOrUniqueName;
			if (place != c.LastPlace)
			{
				c.LastPlace = place;
				MaybeRemark(c, npc);
			}
		}
	}

	private void MaybeRemark(Company c, NPC npc)
	{
		if (Remark == null || !CompanyRules.MayRemark(c.Remarks, c.LastRemark, Game1.timeOfDay))
		{
			return;
		}
		c.Remarks++;
		c.LastRemark = Game1.timeOfDay;
		Meetup m = c.Meetup;
		string place = npc.currentLocation?.DisplayName ?? "";
		Remark(npc, place).ContinueWith(t =>
		{
			string? line = t.IsCompletedSuccessfully ? t.Result : null;
			_log.Debug($"约见 {m.Id}：{m.Npc} 陪着走到{place}，{(string.IsNullOrWhiteSpace(line) ? "这次没说话" : "说：" + line)}。");
			if (!string.IsNullOrWhiteSpace(line))
			{
				MainThread.Run(() =>
				{
					if (_company.ContainsKey(m.Id))
					{
						CompanionNet.Send(m.Farmer, new CompanionEffect(m.Npc) { Line = line! });
					}
				});
			}
		});
	}

	private void EndOne(Meetup m, string reason, string? bye)
	{
		if (_company.Remove(m.Id, out Company? c))
		{
			c.Escort.Release();
		}
		m.CompanyUntil = Math.Min(m.CompanyUntil, Game1.timeOfDay);
		if (bye != null)
		{
			// 你已经走开了（下矿、进了不跟的地方）：看不到头顶那句，改成提示。
			string said = Pick(bye);
			bool apart = CompanionNet.FarmerOf(m.Farmer)?.currentLocation != c?.Escort.Npc.currentLocation;
			CompanionNet.Send(m.Farmer, apart
				? new CompanionEffect(m.Npc) { Hud = Tr.Get("meetup.saidBye", "{{npc}}：{{line}}", ("npc", NpcName(m.Npc)), ("line", said)) }
				: new CompanionEffect(m.Npc) { Line = said });
		}
		_schedules.Cancel(m.OverlayId, reason);
		_log.Info($"约见 {m.Id}：{m.Npc} 不陪了（{reason}），{Game1.timeOfDay}。");
		Version++;
		MarkDirty();
	}

	/// <summary>开口那一句要 AI 照着说的：约好的事。</summary>
	private static string GreetFocus(Meetup m) =>
		$"你和农夫约好今天这个时候在{PlaceLabel(m)}见面{(m.Summary.Length > 0 ? "（约好的事：" + m.Summary + "）" : "")}。农夫刚到，你主动迎上去打招呼，接着说说你们约好要做的事，等会儿你会陪着农夫走一段。";
}
