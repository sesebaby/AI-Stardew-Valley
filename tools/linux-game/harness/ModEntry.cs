using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Network;

namespace AISV.TestHarness;

/// <summary>
/// 开发专用：从 $AISV_HARNESS_DIR/in/*.json 读命令（{"cmd":..,"args":[..]}），一条一条在主线程执行，
/// 结果写到 out/&lt;同名&gt;.json（{"ok":..,"result":..,"error":..}）。客户端见 tools/linux-game/h.py。
/// </summary>
public sealed class ModEntry : Mod
{
	private string _in = "", _out = "";
	private IEnumerator? _running;
	private string? _runningId;
	private JToken? _result;
	private string? _shotPath;
	private bool _hosting;

	public override void Entry(IModHelper helper)
	{
		string? dir = Environment.GetEnvironmentVariable("AISV_HARNESS_DIR");
		if (string.IsNullOrEmpty(dir))
		{
			Monitor.Log("没有 AISV_HARNESS_DIR，测试工具不工作。", LogLevel.Warn);
			return;
		}
		_in = Path.Combine(dir, "in");
		_out = Path.Combine(dir, "out");
		Directory.CreateDirectory(_in);
		Directory.CreateDirectory(_out);
		// 游戏的 Windows 版只带了 liblwjgl_lz4.dll，Linux 上联机发消息会崩；换成游戏自带的不压缩实现。
		// 房主和农场帮手都装了测试工具，两边一致。
		Type program = typeof(Game1).Assembly.GetType("StardewValley.Program")!;
		Type none = typeof(Game1).Assembly.GetType("StardewValley.Network.Compress.NullNetCompression")!;
		program.GetField("netCompression", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, Activator.CreateInstance(none));
		helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
		helper.Events.GameLoop.SaveLoaded += (_, _) =>
		{
			Game1.options.pauseWhenOutOfFocus = false;
			if (_hosting)
				Game1.options.ipConnectionsEnabled = true;
		};
		// 截图要等这一帧连界面、对话框都画完：在交给屏幕之前（Game.EndDraw）读后台缓冲。
		_instance = this;
		new HarmonyLib.Harmony(ModManifest.UniqueID).Patch(
			AccessTools.Method(typeof(Game), "EndDraw"),
			prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeEndDraw)));
		Monitor.Log($"[Harness] ready, io={dir}", LogLevel.Info);
	}

	private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
	{
		if (_running == null)
		{
			string? next = Directory.EnumerateFiles(_in, "*.json").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
			if (next == null)
				return;
			_runningId = Path.GetFileNameWithoutExtension(next);
			JObject cmd;
			try
			{
				cmd = JObject.Parse(File.ReadAllText(next));
			}
			catch (Exception ex)
			{
				File.Delete(next);
				Finish(false, null, "bad json: " + ex.Message);
				return;
			}
			File.Delete(next);
			string name = cmd.Value<string>("cmd") ?? "";
			string[] args = cmd["args"]?.Select(t => t.ToString()).ToArray() ?? Array.Empty<string>();
			Monitor.Log($"[Harness] {_runningId} {name} {string.Join(' ', args)}", LogLevel.Info);
			_result = null;
			try
			{
				_running = Run(name, args);
			}
			catch (Exception ex)
			{
				Finish(false, null, ex.ToString());
				return;
			}
		}
		try
		{
			if (!_running.MoveNext())
				Finish(true, _result, null);
		}
		catch (Exception ex)
		{
			Finish(false, null, ex.ToString());
		}
	}

	private void Finish(bool ok, JToken? result, string? error)
	{
		string path = Path.Combine(_out, _runningId + ".json");
		File.WriteAllText(path + ".tmp", new JObject { ["ok"] = ok, ["result"] = result, ["error"] = error }.ToString(Formatting.Indented));
		File.Move(path + ".tmp", path, true);
		Monitor.Log($"[Harness] {_runningId} -> {(ok ? "ok" : "FAIL: " + error)}", ok ? LogLevel.Info : LogLevel.Warn);
		_running = null;
		_runningId = null;
	}

	private IEnumerator Run(string name, string[] a)
	{
		switch (name)
		{
			case "status": _result = Status(); return Nothing();
			case "load": return Load(a[0], a.Length > 1 && a[1] == "host");
			case "join": return Join(a.Length > 0 ? a[0] : "127.0.0.1", a.Length > 1 ? a[1] : "Farmhand");
			case "wait": return Wait(a[0], a.Skip(1).ToArray());
			case "console": return Console(string.Join(' ', a));
			case "screenshot": return Screenshot(a[0]);
			case "warp": return Warp(a[0], int.Parse(a[1]), int.Parse(a[2]), a.Length > 3 ? int.Parse(a[3]) : 2);
			case "sleep": return Sleep();
			case "talk": return Talk(a[0], interact: true);
			case "near": return Talk(a[0], interact: false);
			case "give": return Give(a[0]);
			case "click": Game1.activeClickableMenu?.receiveLeftClick(int.Parse(a[0]), int.Parse(a[1])); return Nothing();
			case "text": Game1.keyboardDispatcher.Subscriber?.RecieveTextInput(string.Join(' ', a)); return Nothing();
			case "key": Game1.activeClickableMenu?.receiveKeyPress((Microsoft.Xna.Framework.Input.Keys)Enum.Parse(typeof(Microsoft.Xna.Framework.Input.Keys), a[0])); return Nothing();
			case "close": return Close();
			case "time": Game1.timeOfDay = int.Parse(a[0]); return Nothing();
			case "npc": _result = NpcState(a[0]); return Nothing();
			case "font": _result = JToken.FromObject(new string(Game1.smallFont.Characters.ToArray()) + new string(Game1.dialogueFont.Characters.Where(Game1.smallFont.Characters.Contains).ToArray())); return Nothing();
			case "lang": LocalizedContentManager.CurrentLanguageCode = Enum.Parse<LocalizedContentManager.LanguageCode>(a[0]); return Nothing();
			case "dump-asset": _result = JToken.FromObject(a.Length > 1
				? typeof(StardewModdingAPI.IGameContentHelper).GetMethod("Load", new[] { typeof(string) })!.MakeGenericMethod(AccessTools.TypeByName(a[1])).Invoke(Helper.GameContent, new object[] { a[0] })!
				: Helper.GameContent.Load<Dictionary<string, string>>(a[0])); return Nothing();
			case "build-cabin": _result = BuildCabin(int.Parse(a[0]), int.Parse(a[1])); return Nothing();
			case "money": Game1.player.Money = int.Parse(a[0]); return Nothing();
			case "friend": _result = Friend(a[0], int.Parse(a[1])); return Nothing();
			case "marry": _result = Marry(a[0]); return Nothing();
			case "answer": Answer(int.Parse(a[0])); return Nothing();
			case "hold": return Hold(a[0], a.Length > 1 ? int.Parse(a[1]) : 1);
			case "dump-texture": return DumpTexture(a[0], a[1]);
			case "wallets": Game1.player.team.useSeparateWallets.Value = a[0] == "separate"; return Nothing();
			case "get": _result = Get(a[0]); return Nothing();
			case "invoke": _result = Invoke(a[0], a[1], a.Skip(2).ToArray()); return Nothing();
			case "save": return SaveNow();
			case "title": Game1.ExitToTitle(); return Nothing();
			default: throw new InvalidOperationException("unknown command: " + name);
		}
	}

	/// <summary>SMAPI 4 没有从代码里执行控制台命令的接口：run.sh 让游戏从一个 FIFO 读标准输入，这里往里写一行。</summary>
	private IEnumerator Console(string line)
	{
		string fifo = Environment.GetEnvironmentVariable("AISV_HARNESS_STDIN") ?? throw new InvalidOperationException("no AISV_HARNESS_STDIN");
		File.AppendAllText(fifo, line + "\n");
		// 控制台线程收到后排进下一帧执行；多等几帧再报完成。
		for (int i = 0; i < 10; i++)
			yield return null;
	}

	/// <summary>把东西放进背包第一格并拿在手上（送礼、交任务用）。</summary>
	private IEnumerator Hold(string itemId, int count)
	{
		Item item = ItemRegistry.Create(itemId, count);
		Game1.player.Items[0] = item;
		Game1.player.CurrentToolIndex = 0;
		_result = item.DisplayName + " x" + item.Stack;
		yield break;
	}

	/// <summary>把一张贴图存成 png（找原版素材的位置用）。</summary>
	private IEnumerator DumpTexture(string asset, string file)
	{
		Texture2D t = Game1.content.Load<Texture2D>(asset);
		using (FileStream f = File.Create(file))
		{
			t.SaveAsPng(f, t.Width, t.Height);
		}
		_result = $"{t.Width}x{t.Height}";
		yield break;
	}

	private static IEnumerator Nothing()
	{
		yield break;
	}

	private JObject Status()
	{
		JObject o = new JObject
		{
			["world"] = Context.IsWorldReady,
			["free"] = Context.IsPlayerFree,
			["main"] = Context.IsMainPlayer,
			["mode"] = Game1.multiplayerMode,
			["menu"] = Game1.activeClickableMenu?.GetType().FullName,
			["sub"] = (Game1.activeClickableMenu as TitleMenu) != null ? TitleMenu.subMenu?.GetType().FullName : null,
			["event"] = Game1.eventUp,
			["dialogue"] = Game1.dialogueUp,
		};
		if (Context.IsWorldReady)
		{
			o["player"] = Game1.player.Name;
			o["id"] = Game1.player.UniqueMultiplayerID;
			o["location"] = Game1.currentLocation?.NameOrUniqueName;
			o["tile"] = $"{Game1.player.TilePoint.X},{Game1.player.TilePoint.Y}";
			o["time"] = Game1.timeOfDay;
			o["date"] = $"{Game1.year}-{Game1.currentSeason}-{Game1.dayOfMonth}";
			o["money"] = Game1.player.Money;
			o["farmers"] = new JArray(Game1.getOnlineFarmers().Select(f => $"{f.Name}#{f.UniqueMultiplayerID}"));
			o["speaker"] = Game1.currentSpeaker?.Name;
		}
		return o;
	}

	private IEnumerator Load(string slot, bool host)
	{
		while (Game1.activeClickableMenu is not TitleMenu)
			yield return null;
		(Game1.activeClickableMenu as TitleMenu)!.skipToTitleButtons();
		yield return null;
		_hosting = host;
		Game1.multiplayerMode = (byte)(host ? 2 : 0);
		SaveGame.Load(slot);
		Game1.exitActiveMenu();
		int ticks = 0;
		while (!Context.IsWorldReady)
		{
			if (++ticks > 60 * 120)
				throw new TimeoutException("load timed out");
			yield return null;
		}
		Game1.options.pauseWhenOutOfFocus = false;
		if (host)
			Game1.options.ipConnectionsEnabled = true;
		_result = Status();
	}

	private IEnumerator Join(string address, string farmerName)
	{
		while (Game1.activeClickableMenu is not TitleMenu)
			yield return null;
		(Game1.activeClickableMenu as TitleMenu)!.skipToTitleButtons();
		yield return null;
		TitleMenu.subMenu = new FarmhandMenu(Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue().InitClient(new LidgrenClient(address)));
		int ticks = 0;
		while (!Context.IsWorldReady)
		{
			if (++ticks > 60 * 120)
				throw new TimeoutException("join timed out");
			IClickableMenu? sub = TitleMenu.subMenu ?? Game1.activeClickableMenu;
			if (sub is FarmhandMenu fm && fm.MenuSlots != null)
			{
				LoadGameMenu.MenuSlot? slot = fm.MenuSlots.FirstOrDefault(s => s is FarmhandMenu.FarmhandSlot fs && !fs.BelongsToAnotherPlayer());
				if (slot != null)
				{
					slot.Activate();
					yield return null;
					continue;
				}
			}
			if (Game1.activeClickableMenu is CharacterCustomization cc || TitleMenu.subMenu is CharacterCustomization)
			{
				CharacterCustomization c = (Game1.activeClickableMenu as CharacterCustomization ?? (CharacterCustomization)TitleMenu.subMenu!);
				Helper.Reflection.GetField<TextBox>(c, "nameBox").GetValue().Text = farmerName;
				Helper.Reflection.GetField<TextBox>(c, "favThingBox").GetValue().Text = "Testing";
				Game1.player.favoriteThing.Value = "Testing";
				Game1.player.Name = farmerName;
				Helper.Reflection.GetMethod(c, "optionButtonClick").Invoke("OK");
			}
			yield return null;
		}
		Game1.options.pauseWhenOutOfFocus = false;
		_result = Status();
	}

	private IEnumerator Wait(string what, string[] a)
	{
		int timeout = 60 * 60;
		int ticks = 0;
		while (!Check(what, a))
		{
			if (++ticks > timeout)
				throw new TimeoutException("wait " + what + " timed out");
			yield return null;
		}
		_result = Status();
	}

	private static bool Check(string what, string[] a) => what switch
	{
		"world" => Context.IsWorldReady,
		"free" => Context.IsWorldReady && Context.IsPlayerFree,
		"title" => Game1.activeClickableMenu is TitleMenu,
		"menu" => Game1.activeClickableMenu?.GetType().Name == a[0],
		"nomenu" => Game1.activeClickableMenu == null,
		"dialogue" => Game1.activeClickableMenu is DialogueBox,
		"farmers" => Game1.getOnlineFarmers().Count >= int.Parse(a[0]),
		"time" => Context.IsWorldReady && Game1.timeOfDay >= int.Parse(a[0]),
		"day" => Context.IsWorldReady && Game1.Date.TotalDays >= int.Parse(a[0]) && Context.IsPlayerFree,
		"ticks" => (_waitTicks = (_waitTicks + 1)) >= int.Parse(a[0]) && ((_waitTicks = 0) == 0),
		_ => throw new InvalidOperationException("unknown wait condition: " + what),
	};

	private static int _waitTicks;

	private IEnumerator Screenshot(string name)
	{
		_shotPath = Path.Combine(_out, name + ".png");
		while (_shotPath != null)
			yield return null;
		_result = Path.Combine(_out, name + ".png");
	}

	private static ModEntry? _instance;

	private static void BeforeEndDraw()
	{
		if (_instance?._shotPath != null)
			_instance.Capture();
	}

	private void Capture()
	{
		GraphicsDevice gd = Game1.graphics.GraphicsDevice;
		int w = gd.PresentationParameters.BackBufferWidth, h = gd.PresentationParameters.BackBufferHeight;
		Color[] data = new Color[w * h];
		// 这时可能还绑着游戏的界面渲染目标；切回屏幕再读，才是合成好的画面。
		gd.SetRenderTarget(null);
		gd.GetBackBufferData(data);
		// 世界画面合成进后台缓冲时透明度不是 255，存 png 前补满，否则看起来是黑的。
		for (int i = 0; i < data.Length; i++)
			data[i].A = 255;
		using Texture2D tex = new Texture2D(gd, w, h);
		tex.SetData(data);
		using FileStream fs = File.Create(_shotPath);
		tex.SaveAsPng(fs, w, h);
		_shotPath = null;
	}

	/// <summary>对话框像玩家那样一页页点掉（这样对话结束后的回调会照常执行）；别的菜单直接关。</summary>
	private IEnumerator Close()
	{
		int ticks = 0;
		while (Game1.activeClickableMenu is DialogueBox box && ++ticks < 600)
		{
			if (ticks % 10 == 0)
			{
				box.receiveLeftClick(Game1.uiViewport.Width / 2, Game1.uiViewport.Height - 64);
			}
			yield return null;
		}
		// 升级窗口要走“确定”，直接关掉不会真的升级，下一晚又弹出来
		while (Game1.activeClickableMenu is StardewValley.Menus.LevelUpMenu level && !level.isProfessionChooser && ++ticks < 1200)
		{
			if (level.isActive)
			{
				level.receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons.Start);
			}
			yield return null;
		}
		if (Game1.activeClickableMenu != null)
		{
			Game1.exitActiveMenu();
		}
		_result = Status();
	}

	private IEnumerator Warp(string location, int x, int y, int facing)
	{
		Game1.warpFarmer(location, x, y, facing);
		yield return null;
		int ticks = 0;
		while (Game1.isWarping || !(Game1.currentLocation?.NameOrUniqueName == location || Game1.currentLocation?.Name == location) || !Context.IsPlayerFree)
		{
			if (++ticks > 60 * 30)
				throw new TimeoutException("warp timed out, at " + Game1.currentLocation?.NameOrUniqueName);
			yield return null;
		}
		_result = Status();
	}

	private IEnumerator Sleep()
	{
		int day = Game1.Date.TotalDays;
		FarmHouse home = Utility.getHomeOfFarmer(Game1.player);
		Point bed = home.GetPlayerBedSpot();
		Game1.warpFarmer(home.NameOrUniqueName, bed.X, bed.Y, 2);
		while (Game1.currentLocation != home || Game1.isWarping)
			yield return null;
		// 从别的地图过来时，换图后的落脚点可能是门口：再放到床上。
		Game1.player.Position = new Vector2(bed.X, bed.Y) * 64f;
		Helper.Reflection.GetMethod(home, "startSleep").Invoke();
		int ticks = 0;
		while (!(Context.IsWorldReady && Game1.Date.TotalDays > day && Context.IsPlayerFree))
		{
			if (++ticks > 60 * 300)
				throw new TimeoutException("sleep timed out");
			// 早上的升级、存档之类的菜单都点掉。
			// 升级窗口点“确定”的坐标常常对不上，用手柄的开始键确认（原版就是这样处理的）。
			if (Game1.activeClickableMenu is LevelUpMenu { isActive: true, isProfessionChooser: false } level && ticks % 30 == 0)
			{
				level.receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons.Start);
			}
			else if (Game1.activeClickableMenu is LevelUpMenu or ShippingMenu && ticks % 30 == 0)
			{
				ClickableComponent? ok = Helper.Reflection.GetField<ClickableTextureComponent>(Game1.activeClickableMenu, "okButton", required: false)?.GetValue();
				if (ok != null)
					Game1.activeClickableMenu.receiveLeftClick(ok.bounds.Center.X, ok.bounds.Center.Y);
			}
			yield return null;
		}
		_result = Status();
	}

	private IEnumerator Talk(string npcName, bool interact)
	{
		NPC npc = Game1.getCharacterFromName(npcName) ?? throw new InvalidOperationException("no npc " + npcName);
		if (npc.currentLocation != Game1.currentLocation)
		{
			Game1.warpFarmer(npc.currentLocation.NameOrUniqueName, npc.TilePoint.X, npc.TilePoint.Y + 1, 0);
			while (Game1.isWarping || Game1.currentLocation != npc.currentLocation)
				yield return null;
		}
		Game1.player.Position = npc.Position + new Vector2(0, 64);
		Game1.player.faceDirection(0);
		if (interact)
			npc.checkAction(Game1.player, Game1.currentLocation);
		_result = Status();
	}

	/// <summary>走到村民旁边，把手上的东西给他（送礼、交任务）。</summary>
	private IEnumerator Give(string npcName)
	{
		IEnumerator walk = Talk(npcName, interact: false);
		while (walk.MoveNext())
			yield return walk.Current;
		NPC npc = Game1.getCharacterFromName(npcName);
		_result = npc.tryToReceiveActiveObject(Game1.player);
	}

	private IEnumerator SaveNow()
	{
		IClickableMenu? before = Game1.activeClickableMenu;
		Game1.activeClickableMenu = new SaveGameMenu();
		while (Game1.activeClickableMenu is SaveGameMenu)
			yield return null;
		Game1.activeClickableMenu = before;
	}

	private static JToken BuildCabin(int x, int y)
	{
		bool ok = Game1.getFarm().buildStructure("Cabin", new Vector2(x, y), Game1.player, out _, skipSafetyChecks: true);
		return ok;
	}

	/// <summary>读一个静态成员链，比如 StardewValley.Game1.player.Money。</summary>
	private static JToken? Get(string path)
	{
		string[] parts = path.Split('.');
		for (int i = parts.Length - 1; i > 0; i--)
		{
			Type? t = FindType(string.Join('.', parts.Take(i)));
			if (t == null)
				continue;
			object? cur = null;
			Type curType = t;
			foreach (string member in parts.Skip(i))
			{
				cur = ReadMember(curType, cur, member);
				if (cur == null)
					return null;
				curType = cur.GetType();
			}
			return ToToken(cur);
		}
		throw new InvalidOperationException("type not found in " + path);
	}

	private static JToken? Invoke(string typeName, string method, string[] args)
	{
		Type t = FindType(typeName) ?? throw new InvalidOperationException("no type " + typeName);
		MethodInfo m = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).First(x => x.Name == method && x.GetParameters().Length == args.Length);
		object?[] values = m.GetParameters().Select((p, i) => Convert.ChangeType(args[i], p.ParameterType)).ToArray();
		return ToToken(m.Invoke(null, values));
	}

	private static object? ReadMember(Type t, object? target, string name)
	{
		BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | (target == null ? BindingFlags.Static : BindingFlags.Instance);
		if (t.GetProperty(name, f) is { } p)
			return p.GetValue(target);
		if (t.GetField(name, f) is { } fi)
			return fi.GetValue(target);
		throw new InvalidOperationException($"no member {name} on {t.FullName}");
	}

	private static Type? FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

	/// <summary>把这边农夫和某位村民的好感设成多少点（250 点一颗心）。</summary>
	private static JToken Friend(string npc, int points)
	{
		if (!Game1.player.friendshipData.TryGetValue(npc, out Friendship? f))
		{
			Game1.player.friendshipData.Add(npc, f = new Friendship(0));
		}
		f.Points = points;
		return JToken.FromObject(new { npc, points = f.Points });
	}

	/// <summary>选问题对话框里的第几个回答（从 0 数）。</summary>
	private static void Answer(int index)
	{
		if (Game1.activeClickableMenu is not DialogueBox box || !box.isQuestion)
			throw new InvalidOperationException("no question dialogue");
		box.selectedResponse = index;
		box.receiveLeftClick(box.x + 64, box.y + 64);
	}

	/// <summary>直接把这边的农夫和某位村民记成已婚（只改记录，不搬家、不演婚礼）。</summary>
	private static JToken Marry(string npc)
	{
		Friend(npc, 2500);
		Friendship f = Game1.player.friendshipData[npc];
		f.Status = FriendshipStatus.Married;
		f.WeddingDate = new WorldDate(Game1.Date);
		Game1.player.spouse = npc;
		// 没升级的房子没有婚后地图（原版要先升级才能结婚），进屋会崩。
		Game1.player.HouseUpgradeLevel = Math.Max(1, Game1.player.HouseUpgradeLevel);
		return JToken.FromObject(new { npc, spouse = Game1.player.spouse, status = f.Status.ToString() });
	}

	private static JToken? ToToken(object? o)
	{
		if (o == null)
			return null;
		try
		{
			return JToken.FromObject(o, JsonSerializer.Create(new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MaxDepth = 3 }));
		}
		catch
		{
			return o.ToString();
		}
	}

	/// <summary>调试：村民此刻的状态（在哪、谁在带着走、为什么走不动）。</summary>
	private static JToken NpcState(string name)
	{
		NPC? n = Game1.getCharacterFromName(name);
		if (n == null)
		{
			return JValue.CreateNull();
		}
		bool Field(string f) => (bool?)AccessTools.Field(typeof(Character), f)?.GetValue(n) ?? false;
		return JToken.FromObject(new
		{
			location = n.currentLocation?.NameOrUniqueName,
			tile = $"{n.TilePoint.X},{n.TilePoint.Y}",
			controller = n.controller?.GetType().FullName,
			path = n.controller?.pathToEndPoint?.Count,
			end = n.controller == null ? null : $"{n.controller.endPoint.X},{n.controller.endPoint.Y}",
			temporary = n.temporaryController?.GetType().Name,
			freezeMotion = Field("freezeMotion"),
			n.movementPause,
			n.speed,
			endAnim = n.doingEndOfRouteAnimation.Value,
			n.ignoreScheduleToday,
			n.followSchedule,
			n.IsInvisible,
			n.isCharging,
			n.IsWalkingInSquare
		});
	}

}

