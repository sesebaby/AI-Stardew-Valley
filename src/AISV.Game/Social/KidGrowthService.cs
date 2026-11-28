using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Companions;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Family;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Store;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Pathfinding;

namespace AISV.Game.Social;

/// <summary>
/// 孩子长大：学会走路以后再过一阵到上学的年纪、再过一阵成少年。
/// 阶段记在孩子身上（modData，房主推进，联机自动同步），换成生图工具画的行走图和头像；
/// 上学的孩子在佩妮上课的日子去教室，少年周末去镇上逛（房主放替身，真身先藏起来）；
/// 长大那天父母那边记下养出来的性子，孩子来说一句，配偶来信。
/// </summary>
public sealed class KidGrowthService
{
	public const string StageKey = "AIStardewValley.Core/LifeStage";

	/// <summary>孩子身上：今天出门去哪了（school / town，空 = 在家）。</summary>
	public const string AwayKey = "AIStardewValley.Core/KidAway";

	/// <summary>替身身上：是哪个孩子（<see cref="Kids.Key"/>）。</summary>
	public const string KidKeyData = "AIStardewValley.Core/KidKey";

	public const string AssetPrefix = "Mods/AIStardewValley.Core/Kids/";

	public const string SchoolReason = "kid_school";

	public const string TownReason = "kid_town";

	private static KidGrowthService? _instance;

	private readonly Func<ModConfig> _config;

	private readonly IModHelper _helper;

	private readonly GrowthBook _book;

	private readonly ILog _log;

	private readonly Dictionary<string, NPC> _away = new Dictionary<string, NPC>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _noted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>长大了、还没当面说过那一句的孩子（名字）。</summary>
	private readonly HashSet<string> _toGreet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>写好了、当时没机会说的那一句。</summary>
	private readonly Dictionary<string, string> _greetReady = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>正在等 AI 写某个孩子的那一句（一次只等一个，免得两句撞在一起丢一句）。</summary>
	private bool _greeting;

	private SchoolRules.Lesson? _lesson;

	/// <summary>长大后的样子一个一个写（同一天长大的兄弟姐妹别写成一样）。</summary>
	private Task _writing = Task.CompletedTask;

	private readonly Dictionary<string, string> _written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private Point _teacherTile;

	private int _ticks;

	public KidGrowthService(Func<ModConfig> config, IModHelper helper, GrowthBook book, ILog log)
	{
		_config = config;
		_helper = helper;
		_book = book;
		_log = log;
	}

	public GrowthBook Book => _book;

	public IAiSender? Ai { get; set; }

	public ChildTalk? Talk { get; set; }

	public ChildBook? KidBook { get; set; }

	public Func<string, string> Display { get; set; } = n => n;

	/// <summary>记一件全村的小事（谁知道、说什么）。</summary>
	public Action<string, string>? Remember { get; set; }

	/// <summary>寄一封今天就到的信（村民、触发键、标题、正文）。</summary>
	public Action<string, string, string, string>? Letter { get; set; }

	/// <summary>今天的课（只有房主算）。</summary>
	public SchoolRules.Lesson? Lesson => _lesson;

	public static string Stage(Child c) => c.Age >= 3 && c.modData.TryGetValue(StageKey, out string? v) ? KidStages.Parse(v) : KidStages.Toddler;

	public static bool Grown(Child c) => Stage(c) != KidStages.Toddler;

	/// <summary>头顶比幼儿高多少屏幕像素（帽子、表情气泡跟着抬）。</summary>
	public static int Lift(string stage) => stage switch
	{
		KidStages.School => 32,
		KidStages.Teen => 44,
		_ => 0
	};

	private static string Look(Child c, string stage) => $"{stage}_{(c.Gender == Gender.Male ? "boy" : "girl")}{(c.darkSkinned.Value ? "_dark" : "")}";

	// ---------------- 样子 ----------------

	public void Apply(Harmony harmony)
	{
		_instance = this;
		harmony.Patch(AccessTools.Method(typeof(Child), nameof(Child.reloadSprite)), postfix: new HarmonyMethod(typeof(KidGrowthService), nameof(AfterReloadSprite)));
		harmony.Patch(AccessTools.Method(typeof(Child), nameof(Child.getMugShotSourceRect)), postfix: new HarmonyMethod(typeof(KidGrowthService), nameof(AfterMugShot)));
		harmony.Patch(AccessTools.Method(typeof(SocialPage), nameof(SocialPage.CreateSpriteComponent)), postfix: new HarmonyMethod(typeof(KidGrowthService), nameof(AfterSocialSprite)));
		harmony.Patch(AccessTools.Method(typeof(Child), nameof(Child.draw), new[] { typeof(SpriteBatch), typeof(float) }), prefix: new HarmonyMethod(typeof(KidGrowthService), nameof(BeforeDrawAlpha)));
		harmony.Patch(AccessTools.Method(typeof(Child), nameof(Child.draw), new[] { typeof(SpriteBatch) }), prefix: new HarmonyMethod(typeof(KidGrowthService), nameof(BeforeDraw)));
		harmony.CreateReversePatcher(AccessTools.Method(typeof(NPC), nameof(NPC.draw), new[] { typeof(SpriteBatch), typeof(float) }), new HarmonyMethod(typeof(KidGrowthService), nameof(NpcDraw))).Patch();
	}

	public void OnAssetRequested(AssetRequestedEventArgs e)
	{
		string name = e.NameWithoutLocale.BaseName.Replace('\\', '/');
		if (!name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		string file = Path.Combine("assets", "kids", name[AssetPrefix.Length..] + ".png");
		if (File.Exists(Path.Combine(_helper.DirectoryPath, file)))
		{
			e.LoadFromModFile<Texture2D>(file, AssetLoadPriority.Exclusive);
		}
	}

	/// <summary>长大后的图按两倍精度画（每帧 44×64），游戏里缩一半画，个头和 22×32 一样，脸看得清。</summary>
	public const float GrownScale = 0.5f;

	/// <summary>长大了：换成这一档的图（每帧 44×64，按半尺寸画、按一格宽定位）和头像。</summary>
	private static void AfterReloadSprite(Child __instance)
	{
		try
		{
			string stage = Stage(__instance);
			if (stage == KidStages.Toddler)
			{
				if (__instance.Age >= 3 && __instance.forceOneTileWide.Value)
				{
					__instance.forceOneTileWide.Value = false;
					__instance.Portrait = null;
					__instance.Scale = 1f;
					__instance.Breather = true;
				}
				return;
			}
			string look = Look(__instance, stage);
			__instance.Sprite.textureName.Value = AssetPrefix + look;
			__instance.Sprite.SpriteWidth = 44;
			__instance.Sprite.SpriteHeight = 64;
			__instance.Scale = GrownScale;
			// 原版呼吸是按 16 像素宽的图切胸口那块画的，放大的图对不上
			__instance.Breather = false;
			__instance.Sprite.UpdateSourceRect();
			__instance.forceOneTileWide.Value = true;
			__instance.Portrait = Game1.content.Load<Texture2D>(AssetPrefix + "portrait_" + look);
		}
		catch (Exception ex)
		{
			_instance?._log.Warn($"孩子 {__instance.Name} 换长大的样子出错：{ex.Message}");
		}
	}

	private static void AfterMugShot(Child __instance, ref Rectangle __result)
	{
		string stage = Stage(__instance);
		if (stage != KidStages.Toddler)
		{
			__result = new Rectangle(6, stage == KidStages.Teen ? 2 : 8, 32, 48);
		}
	}

	/// <summary>社交页按 4 倍画头像那一块：长大的图是两倍精度，缩一半。</summary>
	private static void AfterSocialSprite(SocialPage.SocialEntry entry, ClickableTextureComponent __result)
	{
		if (entry.Character is Child c && Grown(c))
		{
			__result.scale = __result.baseScale = 4f * GrownScale;
		}
	}

	/// <summary>原版戴了遮头发的帽子会把幼儿的头顶裁掉；长大的孩子头更高，直接按普通村民画。</summary>
	private static bool BeforeDrawAlpha(Child __instance, SpriteBatch b, float alpha)
	{
		if (!Grown(__instance))
		{
			return true;
		}
		NpcDraw(__instance, b, alpha);
		return false;
	}

	/// <summary>NPC.draw(b, alpha) 本身（不经过孩子的重写）。</summary>
	private static void NpcDraw(NPC instance, SpriteBatch b, float alpha) => throw new NotImplementedException("reverse patch");

	/// <summary>表情气泡和帽子：照原版幼儿的位置，往上抬到长大后的头顶。</summary>
	private static bool BeforeDraw(Child __instance, SpriteBatch b)
	{
		string stage = Stage(__instance);
		if (stage == KidStages.Toddler)
		{
			return true;
		}
		__instance.draw(b, 1f);
		if (__instance.IsInvisible)
		{
			return false;
		}
		int lift = Lift(stage);
		if (__instance.IsEmoting && !Game1.eventUp)
		{
			Vector2 at = __instance.getLocalPosition(Game1.viewport);
			at.Y -= 32 + __instance.Sprite.SpriteHeight * 4 * __instance.Scale - 64 + lift;
			b.Draw(Game1.emoteSpriteSheet, at, new Rectangle(__instance.CurrentEmoteIndex * 16 % Game1.emoteSpriteSheet.Width, __instance.CurrentEmoteIndex * 16 / Game1.emoteSpriteSheet.Width * 16, 16, 16), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, __instance.StandingPixel.Y / 10000f);
		}
		if (__instance.hat.Value is StardewValley.Objects.Hat hat)
		{
			bool flip = __instance.Sprite.CurrentAnimation != null && __instance.Sprite.CurrentAnimation[__instance.Sprite.currentAnimationIndex].flip;
			(int dx, int dy, int dir) = HatOffset(__instance.Sprite.CurrentFrame, flip);
			Vector2 shake = __instance.shakeTimer > 0 ? new Vector2(Game1.random.Next(-1, 2), Game1.random.Next(-1, 2)) : Vector2.Zero;
			hat.draw(b, __instance.getLocalPosition(Game1.viewport) + new Vector2(-36 + dx * 4, -12 + dy * 4) + shake + new Vector2(30f, -42f - lift), 1.3333334f, 1f, __instance.StandingPixel.Y / 10000f + 1E-07f, dir);
		}
		return false;
	}

	/// <summary>原版幼儿各帧帽子的偏移（游戏像素）和朝向；新图的各帧动作和原版一样排。</summary>
	private static (int Dx, int Dy, int Dir) HatOffset(int frame, bool flip) => frame switch
	{
		1 => (0, -1, 2),
		3 => (0, 1, 2),
		4 or 6 or 20 => (0, 0, 1),
		5 => (0, -1, 1),
		7 => (0, 1, 1),
		8 or 10 => (0, 0, 0),
		9 => (0, -1, 0),
		11 => (0, 1, 0),
		12 or 14 => (0, 0, 3),
		13 => (0, 1, 3),
		15 => (0, -1, 3),
		18 or 19 => (0, 1, 2),
		21 => (flip ? 1 : -1, 1, flip ? 3 : 1),
		22 => (flip ? 2 : -2, 2, flip ? 3 : 1),
		23 => (flip ? 2 : -2, 3, flip ? 3 : 1),
		_ => (0, 0, 2)
	};

	/// <summary>替身照孩子的样子画（宽图也按一格定位、不呼吸）。</summary>
	public static void Dress(NPC standIn, Child real)
	{
		standIn.forceOneTileWide.Value = real.forceOneTileWide.Value;
		standIn.Scale = real.Scale;
		standIn.Breather = false;
		standIn.HideShadow = false;
		if (Grown(real))
		{
			standIn.Portrait = real.Portrait;
		}
	}

	// ---------------- 每天 ----------------

	public void OnDayStarted()
	{
		_noted.Clear();
		_lesson = null;
		Sweep();
		int today = Game1.Date.TotalDays;
		// 第一次见到的孩子先记成幼儿，免得房主刚推进就错过"长大那天"。
		foreach (Child kid in Mine())
		{
			if (_book.Find(Kids.Key(kid)) == null && _book.Get(Kids.Key(kid)) is KidGrowth g)
			{
				g.Since = today;
				_book.Save();
			}
		}
		if (Context.IsMainPlayer)
		{
			ModConfig c = _config();
			foreach (Child kid in Kids.All().Where(k => k.Age >= 3))
			{
				string now = Stage(kid);
				string next = c.KidsGrowUp ? GrowthRules.Step(now, GrowthRules.StageFor(kid.daysOld.Value, c.KidSchoolDays, c.KidTeenDays)) : now;
				if (next != now || !kid.modData.ContainsKey(StageKey))
				{
					SetStage(kid, next);
				}
			}
			_lesson = Utility.isFestivalDay() ? null : FindLesson(out _teacherTile);
			if (_lesson is SchoolRules.Lesson l)
			{
				_log.Debug($"今天有课：{l.Location} {l.Start}～{l.End}。");
			}
		}
		CheckGrown();
	}

	/// <summary>改孩子的阶段（房主），马上换图。</summary>
	public void SetStage(Child kid, string stage)
	{
		string before = Stage(kid);
		kid.modData[StageKey] = stage;
		kid.reloadSprite();
		if (before != stage)
		{
			_log.Info($"孩子 {kid.Name}（出生 {kid.daysOld.Value} 天）长到了：{KidStages.Label(stage)}。");
		}
	}

	/// <summary>这台电脑上的农夫自己的孩子。</summary>
	private static IEnumerable<Child> Mine() => Game1.player == null ? Enumerable.Empty<Child>() : Game1.player.getChildren().Where(c => c.Age >= 3);

	/// <summary>父母这边：孩子身上的阶段比上次看到的大，就是长大了。</summary>
	private void CheckGrown()
	{
		foreach (Child kid in Mine())
		{
			KidGrowth? g = _book.Get(Kids.Key(kid));
			if (g == null)
			{
				continue;
			}
			string stage = Stage(kid);
			int now = KidStages.Index(stage), seen = KidStages.Index(g.Seen);
			if (now > seen)
			{
				GrewUp(kid, g, stage);
			}
			else if (now < seen)
			{
				g.Seen = stage;
				_book.Save();
			}
		}
	}

	private void GrewUp(Child kid, KidGrowth g, string stage)
	{
		int today = Game1.Date.TotalDays;
		string key = Kids.Key(kid);
		g.Seen = stage;
		g.Since = today;
		g.Traits = GrowthRules.Traits(g);
		g.Persona = "";
		_book.Save();
		string label = KidStages.Label(stage);
		string traits = string.Join(Tr.Get("kids.traitJoin", "、"), g.Traits.Select(GrowthRules.TraitLabel));
		KidBook?.Note(key, today, stage == KidStages.Teen ? "今天起不再是小孩子了，个子蹿得比以前高" : "今天起到了上学的年纪，可以去佩妮那里上课了");
		Hints.Hud(Tr.Get("kids.grewHud", "{{kid}}长大了：{{stage}}（{{traits}}）。", ("kid", kid.displayName), ("stage", label), ("traits", traits)), 7000);
		_toGreet.Add(kid.Name);
		Farmer p = Game1.player;
		string? spouse = string.IsNullOrWhiteSpace(p.spouse) ? null : p.spouse;
		if (spouse != null && Game1.getCharacterFromName(spouse) != null)
		{
			Letter?.Invoke(spouse, $"kids:grown:{key}:{stage}",
				Tr.Get("kids.letterSubject", "{{kid}}长大了", ("kid", kid.displayName)),
				stage == KidStages.Teen
					? Tr.Get("kids.letterTeen", "你发现了吗？{{kid}}这阵子个子蹿得好快，说话也有自己的主意了，已经是个{{traits}}的少年啦。有空多陪陪{{kid}}，这个年纪的孩子嘴上不说，心里很在乎你。", ("kid", kid.displayName), ("traits", traits))
					: Tr.Get("kids.letterSchool", "{{kid}}到上学的年纪了！佩妮说上课的日子可以去博物馆找她。这孩子是个{{traits}}的小家伙，我已经开始想象{{kid}}背着书包的样子了。", ("kid", kid.displayName), ("traits", traits)));
			Remember?.Invoke(spouse, stage == KidStages.Teen
				? $"{p.Name}家的孩子{kid.displayName}长成了少年"
				: $"{p.Name}家的孩子{kid.displayName}到了上学的年纪，要去佩妮那里上课了");
		}
		WritePersona(kid, g, stage);
	}

	/// <summary>AI 写"现在是个什么样的孩子"（写不成用规则拼的）。</summary>
	private void WritePersona(Child kid, KidGrowth g, string stage)
	{
		string fallback = GrowthRules.FallbackPersona(kid.displayName, kid.Gender == Gender.Male, stage, g.Traits);
		if (Ai is not IAiSender ai || !_config().AiOn || Talk == null)
		{
			g.Persona = fallback;
			_book.Save();
			return;
		}
		ChildContext ctx = Talk.Context(kid);
		string key = Kids.Key(kid);
		// 兄弟姐妹已经写下的样子：同一天长大的排队写，后写的能看到先写的。
		List<(string Key, string Name, string Persona)> siblings = Mine().Where(k => k != kid)
			.Select(k => (Kids.Key(k), k.displayName, _book.Find(Kids.Key(k))?.Persona ?? "")).ToList();
		string system = "你在帮一个农场模拟游戏写角色设定。只写设定本身，不加标题、引号和解释，120 个字以内，用" + ctx.Language + "。";
		string turn = $"农夫{ctx.ParentName}（{ctx.ParentRole}）的孩子{ctx.Name}（{(ctx.Boy ? "男孩" : "女孩")}）今天{(stage == KidStages.Teen ? "长成了十三四岁的少年" : "到了上学的年纪（七八岁）")}。"
			+ $"从小养成的性子：{string.Join("；", g.Traits.Select(GrowthRules.TraitText))}。"
			+ $"小时候：聊过 {g.Chats} 次天，陪着玩过 {g.Plays} 次，跑出去玩过 {g.Runaways} 次，上过 {g.Classes} 次课。"
			+ "写一段现在的样子：性格、爱好、说话的习惯、和家里人的关系。不写恋爱、不写钱。";
		Task Write(Task _) => Task.Run(async () =>
		{
			string text = fallback;
			try
			{
				string others = string.Join("；", siblings.Select(s => s.Name + (Written(s.Key, s.Persona) is { Length: > 0 } p ? "：" + p : "")));
				string ask = siblings.Count == 0 ? turn : turn + $"家里还有{others}。爱好和说话的样子要写得和他们不一样。";
				AiResult r = await ai.SendAsync(new ChatRequest("kid_grown", system, ask, 500, 0.9)).ConfigureAwait(false);
				if (r.Ok && !string.IsNullOrWhiteSpace(r.Text))
				{
					text = TextTools.Truncate(r.Text.Trim().Trim('"', '“', '”'), 160);
				}
			}
			catch (Exception ex)
			{
				_log.Debug($"孩子 {ctx.Name} 长大后的样子没写成：{ex.Message}");
			}
			lock (_written)
			{
				_written[key] = text;
			}
			MainThread.Run(() =>
			{
				g.Persona = text;
				_book.Save();
				_log.Info($"孩子 {ctx.Name} 长大后：{text}");
			});
		});
		lock (_written)
		{
			_writing = _writing.ContinueWith(Write).Unwrap();
		}
	}

	/// <summary>刚写好、可能还没回到主线程存进去的样子。</summary>
	private string Written(string key, string saved)
	{
		lock (_written)
		{
			return _written.TryGetValue(key, out string? text) ? text : saved;
		}
	}

	// ---------------- 每帧、每 10 分钟 ----------------

	public void OnUpdateTicked()
	{
		if (!Context.IsWorldReady || ++_ticks % 60 != 0)
		{
			return;
		}
		CheckGrown();
		if (_toGreet.Count > 0 && !_greeting && Context.IsPlayerFree && !Game1.eventUp)
		{
			Child? kid = Mine().FirstOrDefault(k => _toGreet.Contains(k.Name) && k.currentLocation == Game1.currentLocation && !k.IsInvisible
				&& Vector2.Distance(k.Tile, Game1.player.Tile) < 8);
			if (kid != null)
			{
				_toGreet.Remove(kid.Name);
				Greet(kid);
			}
		}
	}

	/// <summary>长大那天第一次见到你：跑过来冒个心，说一句。</summary>
	private void Greet(Child kid)
	{
		kid.Halt();
		kid.faceGeneralDirection(Game1.player.getStandingPosition(), 0, opposite: false, useTileCalculations: false);
		kid.doEmote(20);
		string stage = Stage(kid);
		string fallback = stage == KidStages.Teen
			? Tr.Get("kids.greetTeen", "……别一直看我啦。我知道我长高了。")
			: Tr.Get("kids.greetSchool", "你看你看！我是大孩子了，可以去上学了！");
		if (_greetReady.Remove(kid.Name, out string? ready))
		{
			KidBook?.Add(Kids.Key(kid), Game1.Date.TotalDays, "child", ready);
			Game1.DrawDialogue(new StardewValley.Dialogue(kid, null, "$h" + ready));
			return;
		}
		if (Ai is not IAiSender ai || !_config().AiOn || Talk == null)
		{
			Game1.DrawDialogue(new StardewValley.Dialogue(kid, null, fallback));
			return;
		}
		ChildContext ctx = Talk.Context(kid);
		string system = ChildPrompts.System(ctx, KidBook?.Recent(ctx.Key, 6) ?? Array.Empty<ChildTurn>(), opening: true);
		string name = kid.Name;
		_greeting = true;
		Task.Run(async () =>
		{
			string? line = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(ChildPrompts.OpeningRequest, system, "今天你长大了一些（" + ctx.Stage + "）。对" + ctx.ParentRole + "说一句。", 400, 0.9)).ConfigureAwait(false);
				line = r.Ok ? ChildPrompts.Clean(r.Text, ctx.StageId) : null;
			}
			catch (Exception ex)
			{
				_log.Debug($"孩子 {name} 长大那天的话没生成：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				_greeting = false;
				string said = string.IsNullOrWhiteSpace(line) ? fallback : line!;
				if (Game1.activeClickableMenu != null || Game1.getCharacterFromName(name, mustBeVillager: false) is not Child k || k.currentLocation != Game1.currentLocation)
				{
					// 正忙或走开了：话留着，下次走近再说。
					_greetReady[name] = said;
					_toGreet.Add(name);
					return;
				}
				KidBook?.Add(ctx.Key, ctx.Day, "child", said);
				Game1.DrawDialogue(new StardewValley.Dialogue(k, null, "$h" + said));
			});
		});
	}

	public void OnTimeChanged()
	{
		if (!Context.IsWorldReady)
		{
			return;
		}
		NoteOutings();
		ModConfig c = _config();
		if (!Context.IsMainPlayer)
		{
			return;
		}
		if (!c.KidsGrowUp || !c.KidOutings)
		{
			BringBack(null);
			return;
		}
		int t = Game1.timeOfDay;
		if (_lesson is SchoolRules.Lesson l && t >= l.Start && t < l.End)
		{
			int seat = 0;
			foreach (Child kid in Kids.All().Where(k => Stage(k) == KidStages.School))
			{
				Point tile = Seat(Game1.getLocationFromName(l.Location), seat++);
				SendAway(kid, SchoolReason, l.Location, tile, faceUp: true);
			}
		}
		else
		{
			BringBack(SchoolReason);
		}
		// 过节那天镇上换成庆典的地图，不去。
		bool townDay = GrowthRules.TeenOutingDay(Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)) && !Game1.isRaining && !Game1.isSnowing && !Utility.isFestivalDay();
		if (townDay && t >= 1000 && t < 1600)
		{
			int i = 0;
			foreach (Child kid in Kids.All().Where(k => Stage(k) == KidStages.Teen))
			{
				SendAway(kid, TownReason, "Town", OpenNear(Game1.getLocationFromName("Town"), new Point(52, 68), i), faceUp: false);
				i++;
			}
		}
		else
		{
			BringBack(TownReason);
		}
	}

	/// <summary>父母这边：孩子上课记一笔；你走进教室看见了，再记一笔。</summary>
	private void NoteOutings()
	{
		int today = Game1.Date.TotalDays;
		foreach (Child kid in Mine())
		{
			string key = Kids.Key(kid);
			kid.modData.TryGetValue(AwayKey, out string? away);
			NPC? here = Game1.currentLocation?.characters.FirstOrDefault(n => n.modData.TryGetValue(KidKeyData, out string? k) && k == key);
			if ((away == SchoolReason || Why(here) == SchoolReason) && _noted.Add(key + ":class"))
			{
				_book.Note(key, "class", today);
			}
			if (here != null && Why(here) == SchoolReason && _noted.Add(key + ":watched"))
			{
				_book.Note(key, "watched", today);
				here.doEmote(32);
				KidBook?.Note(key, today, $"今天上课的时候{(Game1.player.IsMale ? "爸爸" : "妈妈")}来教室看我了");
				Hints.Hud(Tr.Get("kids.watchedHud", "你来看{{kid}}上课了。", ("kid", kid.displayName)), 4000);
			}
		}
	}

	private static string? Why(NPC? standIn) => standIn != null && standIn.modData.TryGetValue(NpcBusy.StandInKey, out string? why) ? why : null;

	/// <summary>教室里老师对面的空位（原版：佩妮在桌子上边 17,9，文森特、贾斯坐两边 16,10、19,10，桌子下边 18,12 还有个坐垫）。</summary>
	private Point Seat(GameLocation? room, int index)
	{
		Point[] around = { new(1, 3), new(0, 3), new(2, 3), new(-1, 3), new(-1, 2), new(2, 2), new(-2, 1), new(3, 1) };
		int found = 0;
		foreach (Point d in around)
		{
			Point p = new Point(_teacherTile.X + d.X, _teacherTile.Y + d.Y);
			if (room == null || (room.isTilePassable(new xTile.Dimensions.Location(p.X, p.Y), Game1.viewport) && room.isCharacterAtTile(p.ToVector2()) == null))
			{
				if (found++ == index)
				{
					return p;
				}
			}
		}
		return new Point(_teacherTile.X + 1 + index, _teacherTile.Y + 1);
	}

	/// <summary>镇广场（约见用的那一格）附近第 index 个能站人的空地。</summary>
	private static Point OpenNear(GameLocation? where, Point center, int index)
	{
		if (where == null)
		{
			return center;
		}
		int found = 0;
		for (int r = 0; r <= 5; r++)
		{
			for (int dy = -r; dy <= r; dy++)
			{
				for (int dx = -r; dx <= r; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
					{
						continue;
					}
					Point p = new Point(center.X + dx * 2, center.Y + dy);
					if (where.isTilePassable(new xTile.Dimensions.Location(p.X, p.Y), Game1.viewport) && where.isTileOnMap(p.ToVector2()) && !where.isWaterTile(p.X, p.Y)
						&& where.isCharacterAtTile(p.ToVector2()) == null && where.getObjectAtTile(p.X, p.Y) == null && !where.terrainFeatures.ContainsKey(p.ToVector2()) && found++ == index)
					{
						return p;
					}
				}
			}
		}
		return center;
	}

	private void SendAway(Child kid, string reason, string location, Point tile, bool faceUp)
	{
		if (_away.ContainsKey(kid.Name) || kid.IsInvisible || NpcBusy.Is(kid.Name) || Game1.getLocationFromName(location) is not GameLocation where)
		{
			return;
		}
		NPC standIn = MartService.Clone(kid, where, tile, reason);
		standIn.modData[KidKeyData] = Kids.Key(kid);
		Dress(standIn, kid);
		if (faceUp)
		{
			standIn.faceDirection(0);
		}
		else
		{
			// 事件演员造出来的 NPC 没有原地中心点（默认在地图左上角），不设的话会一路走远。
			standIn.lastCrossroad = new Rectangle(tile.X * 64, tile.Y * 64, 64, 64);
			standIn.walkInSquare(4, 3, 2500);
		}
		kid.IsInvisible = true;
		kid.modData[AwayKey] = reason;
		NpcBusy.Hold(kid.Name, reason);
		_away[kid.Name] = standIn;
		_log.Info($"孩子 {kid.Name} {(reason == SchoolReason ? "去上课了" : "去镇上逛了")}（{location} {tile.X},{tile.Y}）。");
	}

	/// <summary>收回替身（reason 为空 = 全部）。</summary>
	public void BringBack(string? reason)
	{
		foreach ((string name, NPC standIn) in _away.ToList())
		{
			if (reason != null && Why(standIn) != reason)
			{
				continue;
			}
			_away.Remove(name);
			standIn.currentLocation?.characters.Remove(standIn);
			if (Kids.All().FirstOrDefault(k => k.Name == name) is Child kid)
			{
				kid.IsInvisible = false;
				kid.modData.Remove(AwayKey);
			}
			NpcBusy.Release(name);
		}
	}

	public void Cleanup()
	{
		BringBack(null);
		_toGreet.Clear();
		_greetReady.Clear();
		_greeting = false;
		lock (_written)
		{
			_written.Clear();
		}
	}

	/// <summary>替身万一存进了存档：清掉，孩子回家。</summary>
	public static void Sweep()
	{
		if (!Context.IsMainPlayer)
		{
			return;
		}
		foreach (GameLocation l in Game1.locations)
		{
			l.characters.RemoveWhere(c => c != null && c.modData.TryGetValue(NpcBusy.StandInKey, out string? why) && why is SchoolReason or TownReason);
		}
		foreach (Child c in Kids.All())
		{
			if (c.modData.Remove(AwayKey))
			{
				c.IsInvisible = false;
			}
		}
	}

	/// <summary>老师今天的日程里有没有课：原版佩妮带着贾斯和文森特。</summary>
	private static SchoolRules.Lesson? FindLesson(out Point teacherTile)
	{
		teacherTile = new Point(17, 9);
		NPC? teacher = Game1.getCharacterFromName("Penny");
		if (teacher?.Schedule == null)
		{
			return null;
		}
		List<SchoolRules.Stop> stops = Stops(teacher);
		List<List<SchoolRules.Stop>> classmates = new[] { "Jas", "Vincent" }.Select(n => Game1.getCharacterFromName(n)).Where(n => n?.Schedule != null).Select(n => Stops(n!)).ToList();
		SchoolRules.Lesson? lesson = SchoolRules.Today(stops, classmates);
		if (lesson is SchoolRules.Lesson l)
		{
			foreach ((int time, SchedulePathDescription d) in teacher.Schedule)
			{
				if (time == l.Start)
				{
					teacherTile = d.targetTile;
				}
			}
		}
		return lesson;
	}

	private static List<SchoolRules.Stop> Stops(NPC npc) => npc.Schedule.OrderBy(kv => kv.Key).Select(kv => new SchoolRules.Stop(kv.Key, kv.Value.targetLocationName ?? "")).ToList();

	// ---------------- 给聊天用 ----------------

	/// <summary>孩子长大后的说话材料：阶段、性子、长大那天写下的样子。</summary>
	public (string StageId, string StageText, string Grown, IReadOnlyList<string> Traits) Material(Child kid)
	{
		string stage = Stage(kid);
		string key = Kids.Key(kid);
		KidGrowth? g = _book.Find(key);
		string text = stage == KidStages.Toddler ? Kids.StageText(kid.Age) : GrowthRules.StageText(stage);
		if (stage == KidStages.School)
		{
			text += "。" + GrowthRules.SchoolFacts(g?.Classes ?? 0, Context.IsMainPlayer, _lesson, _noted.Contains(key + ":class"), Game1.timeOfDay);
		}
		return (stage, text, g?.Persona ?? "", (IReadOnlyList<string>?)g?.Traits ?? Array.Empty<string>());
	}

	/// <summary>佩妮聊天时知道她在教你家的孩子。</summary>
	public string PromptBlock(string npc)
	{
		if (!npc.Equals("Penny", StringComparison.OrdinalIgnoreCase) || !_config().KidsGrowUp)
		{
			return "";
		}
		List<string> lines = new List<string>();
		foreach (Child kid in Mine().Where(k => Stage(k) != KidStages.Toddler))
		{
			KidGrowth? g = _book.Find(Kids.Key(kid));
			string traits = g == null ? "" : string.Join("、", g.Traits.Select(GrowthRules.TraitText));
			lines.Add(Stage(kid) == KidStages.School
				? $"- 你在教农夫的孩子{kid.displayName}上课（{(kid.Gender == Gender.Male ? "男孩" : "女孩")}，{traits}），已经上过 {g?.Classes ?? 0} 次课，农夫来教室看过 {g?.ClassesWatched ?? 0} 次；可以和农夫聊聊这孩子学得怎么样。"
				: $"- 农夫的孩子{kid.displayName}以前在你这里上过课，现在已经是个少年了（{traits}）。");
		}
		return lines.Count == 0 ? "" : "# 农夫的孩子\n" + string.Join("\n", lines);
	}

	public string Describe()
	{
		List<string> lines = new List<string>();
		foreach (Child kid in Kids.All())
		{
			KidGrowth? g = _book.Find(Kids.Key(kid));
			kid.modData.TryGetValue(AwayKey, out string? away);
			lines.Add($"{kid.Name}：出生 {kid.daysOld.Value} 天，年龄 {kid.Age}，阶段 {Stage(kid)}{(string.IsNullOrEmpty(away) ? "" : "，出门 " + away)}"
				+ (g == null ? "" : $"；聊 {g.Chats} 玩 {g.Plays} 送 {g.Gifts} 跑 {g.Runaways}（快 {g.FoundQuick} 慢 {g.FoundSlow}） 课 {g.Classes} 看 {g.ClassesWatched}；性子 {string.Join("/", g.Traits)}；样子 {g.Persona}"));
		}
		lines.Add(_lesson is SchoolRules.Lesson l ? $"今天的课：{l.Location} {l.Start}～{l.End}（老师在 {_teacherTile.X},{_teacherTile.Y}）" : "今天没课（或不是房主）。");
		foreach (string n in new[] { "Penny", "Jas", "Vincent" })
		{
			NPC? npc = Game1.getCharacterFromName(n);
			lines.Add($"{n} 今天：" + (npc?.Schedule == null ? "没有日程" : string.Join(" / ", Stops(npc).Select(x => x.Time + " " + x.Location))));
		}
		return string.Join("\n", lines);
	}
}
