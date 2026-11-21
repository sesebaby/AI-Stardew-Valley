using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Social;
using StardewModdingAPI;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using StardewValley;
using StardewValley.Characters;

namespace AISV.Game.Social;

/// <summary>
/// 宠物的心里话：你在宠物身边时，每 10 分钟按概率让它冒一句内心独白（灰色思考泡，先播一个表情）。
/// 只冒泡，点宠物还是游戏原来的摸摸；AI 用不了时就不说。
/// </summary>
public sealed class PetThoughts
{
	public const string Request = "pet_thought";

	private const int NearTiles = 12;

	private readonly Func<ModConfig> _config;

	private readonly ILog _log;

	private readonly Random _random = new Random();

	private readonly Dictionary<string, (int Count, int Last, List<string> Said)> _today = new Dictionary<string, (int, int, List<string>)>(StringComparer.OrdinalIgnoreCase);

	private int _day = -1;

	private bool _busy;

	public PetThoughts(Func<ModConfig> config, ILog log)
	{
		_config = config;
		_log = log;
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	/// <summary>第一次冒心里话时提示一句（只一次）。</summary>
	public Action? FirstTime { get; set; }

	public void OnTimeChanged()
	{
		ModConfig c = _config();
		if (!c.AiOn || !c.PetThoughts || _busy || Ai == null || !Context.IsPlayerFree || Game1.eventUp || Game1.isFestival() || Game1.currentLocation == null)
		{
			return;
		}
		int day = Game1.Date.TotalDays;
		if (day != _day)
		{
			_day = day;
			_today.Clear();
		}
		List<Pet> pets = Game1.currentLocation.characters.OfType<Pet>().Where(Near).ToList();
		if (pets.Count == 0)
		{
			return;
		}
		Pet pet = pets[_random.Next(pets.Count)];
		string key = pet.petId.Value.ToString();
		(int count, int last, List<string> said) = _today.TryGetValue(key, out var t) ? t : (0, -1, new List<string>());
		if (!PetThoughtRules.CanThink(count, c.PetThoughtsDaily, last, Game1.timeOfDay) || !PetThoughtRules.Rolls(_random.NextDouble(), c.PetThoughtsPercent))
		{
			return;
		}
		_today[key] = (count + 1, Game1.timeOfDay, said);
		Think(pet, key, said);
	}

	/// <summary>调试：把当前地图上的一只宠物叫到身边，马上冒一句心里话（不看概率和次数）。</summary>
	public string Force()
	{
		if (Ai == null || _busy)
		{
			return "AI 没接上或正在生成。";
		}
		Pet? pet = Game1.currentLocation?.characters.OfType<Pet>().FirstOrDefault() ?? Utility.getAllPets().FirstOrDefault();
		if (pet == null)
		{
			return "没有宠物。";
		}
		if (pet.currentLocation != Game1.currentLocation)
		{
			Game1.warpCharacter(pet, Game1.currentLocation, Game1.player.Tile + new Microsoft.Xna.Framework.Vector2(1, 1));
		}
		else
		{
			pet.Position = (Game1.player.Tile + new Microsoft.Xna.Framework.Vector2(1, 1)) * 64f;
		}
		string key = pet.petId.Value.ToString();
		List<string> said = _today.TryGetValue(key, out var t) ? t.Said : new List<string>();
		Think(pet, key, said);
		return $"让 {pet.Name}（{pet.petType.Value}）想一句。";
	}

	private static bool Near(Pet pet)
	{
		if (pet.IsInvisible || !Utility.isOnScreen(pet.StandingPixel.ToVector2(), 64))
		{
			return false;
		}
		return Vector2Distance(pet.Tile, Game1.player.Tile) <= NearTiles;
	}

	private static float Vector2Distance(Microsoft.Xna.Framework.Vector2 a, Microsoft.Xna.Framework.Vector2 b) => Microsoft.Xna.Framework.Vector2.Distance(a, b);

	private void Think(Pet pet, string key, List<string> said)
	{
		string system = Prompt(pet, said);
		_busy = true;
		IAiSender ai = Ai!;
		Task.Run(async () =>
		{
			(string Mood, string Line)? thought = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(Request, system, "冒一句心里话。", 300, 0.95)).ConfigureAwait(false);
				thought = r.Ok ? PetThoughtRules.Parse(r.Text) : null;
			}
			catch (Exception ex)
			{
				_log.Debug($"宠物 {pet.Name} 的心里话没生成：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				_busy = false;
				if (thought is not { } th || pet.currentLocation != Game1.currentLocation || Game1.activeClickableMenu != null || Game1.eventUp)
				{
					return;
				}
				said.Add(th.Line);
				pet.doEmote(PetThoughtRules.Emote(th.Mood));
				NpcSpeech.Think(pet, th.Line);
				_log.Debug($"宠物 {pet.Name}（{th.Mood}）：{th.Line}");
				if (Game1.player.mailReceived.Add("AISV_PetThoughtHint"))
				{
					FirstTime?.Invoke();
				}
			});
		});
	}

	private string Prompt(Pet pet, List<string> said)
	{
		Farmer p = Game1.player;
		string species = pet.petType.Value switch
		{
			"Cat" => "猫",
			"Dog" => "狗",
			"Turtle" => "乌龟",
			string other => other
		};
		bool petted = pet.lastPetDay.TryGetValue(p.UniqueMultiplayerID, out int last) && last == Game1.Date.TotalDays;
		bool water = pet.GetPetBowl()?.watered.Value == true;
		bool asleep = pet.CurrentBehavior == Pet.behavior_Sleep;
		int love = pet.friendshipTowardFarmer.Value;
		string bond = love >= 900 ? "特别黏主人" : love >= 500 ? "很喜欢主人" : love >= 200 ? "和主人慢慢熟了" : "还有点怕生";
		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"这次只用{Language?.Invoke() ?? "中文"}回答。");
		sb.AppendLine();
		sb.AppendLine($"你是星露谷一座农场上的{species}，名字叫{pet.Name}，主人是{p.Name}。你不会说人话，下面写的是你脑子里一闪而过的念头。");
		sb.AppendLine();
		sb.AppendLine("# 现在的情况（只是资料）");
		sb.AppendLine($"- {AISV.Core.Dialogue.HistoryFormatter.SeasonName(Game1.currentSeason)}，{Game1.timeOfDay / 100}点左右，{(Game1.isRaining ? "在下雨" : Game1.isSnowing ? "在下雪" : "天气不错")}，你在{Game1.currentLocation.DisplayName}。");
		sb.AppendLine($"- 今天{(petted ? "已经被主人摸过了" : "主人还没摸过你")}；水盆里{(water ? "有水" : "没水")}。");
		sb.AppendLine($"- 你{bond}。主人就在附近。");
		if (asleep)
		{
			sb.AppendLine("- 你正在打盹，迷迷糊糊的。");
		}
		if (said.Count > 0)
		{
			sb.AppendLine("- 今天已经想过（别重复）：" + string.Join(" / ", said.TakeLast(5)));
		}
		sb.AppendLine();
		sb.AppendLine("# 要求");
		sb.AppendLine($"- 用{species}的眼光看世界：只关心吃的、睡觉、玩、主人、气味、虫子、怪声音这类事，不懂人类的复杂事情（钱、工作、恋爱、八卦）。");
		sb.AppendLine($"- 一句话，{PetThoughtRules.MaxChars} 个字以内，口语，可以有点傻、有点可爱。不写动作描写，不加引号。");
		sb.AppendLine("- 只输出 JSON：{\"mood\":\"happy|bored|hungry|sleepy|curious|sad\",\"line\":\"心里话\"}");
		return sb.ToString();
	}
}
