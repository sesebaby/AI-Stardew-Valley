using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Diagnostics;
using AISV.Core.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Objects;

namespace AISV.Game.Phone;

/// <summary>
/// 背包里的一部手机：拿在手上按使用键，或面前没有能互动的东西时按动作键，打开外壳。
/// 新开的农场第一天直接放进背包；中途装上 mod 的农场第二天早上刘易斯寄来；哪里都找不到了第二天再寄一部。
/// </summary>
public sealed class PhoneItem
{
	public const string ItemId = "AIStardewValley.Core_Phone";

	public const string QualifiedItemId = "(O)" + ItemId;

	public const string TextureAsset = "Mods/AIStardewValley.Core/PhoneItem";

	public const string MailId = "AIStardewValley.Core_PhoneMail";

	public const string ResendMailId = "AIStardewValley.Core_PhoneMailAgain";

	public const string ReceivedFlag = "AIStardewValley.Core/PhoneReceived";

	private readonly IInputHelper _input;

	private readonly string _texturePath;

	private readonly ILog _log;

	private readonly Action _open;

	public PhoneItem(string modDirectory, IInputHelper input, ILog log, Action open)
	{
		_texturePath = System.IO.Path.Combine(modDirectory, "assets", "world", "phone_item.png");
		_input = input;
		_log = log;
		_open = open;
	}

	public void OnAssetRequested(AssetRequestedEventArgs e)
	{
		if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
		{
			e.Edit(asset => asset.AsDictionary<string, ObjectData>().Data[ItemId] = new ObjectData
			{
				Name = "AIStardewValley Phone",
				DisplayName = Tr.Get("phoneItem.name", "手机"),
				Description = Tr.Get("phoneItem.description", "村民的消息、群聊、今日安排都在里面。拿在手上点一下就能打开。"),
				Type = "Basic",
				Category = 0,
				Price = 0,
				Edibility = -300,
				Texture = TextureAsset,
				SpriteIndex = 0,
				CanBeGivenAsGift = false,
				CanBeTrashed = true,
				ExcludeFromFishingCollection = true,
				ExcludeFromShippingCollection = true,
				ExcludeFromRandomSale = true,
				// not_placeable：拿在手上时地上不出放置预览。
				ContextTags = new List<string> { "aisv_phone", "not_giftable", "not_placeable" }
			}, AssetEditPriority.Late);
		}
		else if (e.NameWithoutLocale.IsEquivalentTo(TextureAsset))
		{
			e.LoadFrom(() =>
			{
				using System.IO.FileStream stream = System.IO.File.OpenRead(_texturePath);
				return Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
			}, AssetLoadPriority.Exclusive);
		}
	}

	public static void EditMail(IDictionary<string, string> data)
	{
		data[MailId] = Mail(Tr.Get("phoneItem.mail", "亲爱的@：^^镇公所给每户居民都配了一部手机，这是你的。村民的消息、镇上的群聊、今天的安排都能在上面看。^拿在手上点一下就能打开。^^   -刘易斯镇长"), Tr.Get("phoneItem.mailTitle", "你的手机"));
		data[ResendMailId] = Mail(Tr.Get("phoneItem.mailAgain", "亲爱的@：^^听说你的手机找不到了？镇公所又帮你补了一部。这次可要收好。^^   -刘易斯镇长"), Tr.Get("phoneItem.mailAgainTitle", "补办的手机"));
	}

	private static string Mail(string body, string title) =>
		$"{body.Replace("%", "").Replace("[#]", "")}%item id {QualifiedItemId} 1 %%[#]{title.Replace("%", "").Replace("[#]", "")}";

	public void OnDayStarted()
	{
		if (!Context.IsWorldReady || Game1.player == null)
		{
			return;
		}
		try
		{
			Deliver(Game1.player);
		}
		catch (Exception ex)
		{
			_log.Warn("手机没发成：" + ex.Message);
		}
	}

	public string Deliver(Farmer player)
	{
		if (player.mailbox.Contains(MailId) || player.mailbox.Contains(ResendMailId))
		{
			return "信箱里有";
		}
		if (!player.mailReceived.Contains(ReceivedFlag) && !player.mailReceived.Contains(MailId))
		{
			player.mailReceived.Add(ReceivedFlag);
			if (HasPhone(player))
			{
				return "已经有了";
			}
			if (Game1.stats.DaysPlayed <= 1 && player.addItemToInventoryBool(ItemRegistry.Create(QualifiedItemId)))
			{
				Game1.addHUDMessage(new HUDMessage(Tr.Get("phoneItem.got", "背包里多了一部手机：拿在手上点一下就能打开。"), HUDMessage.newQuest_type));
				_log.Debug("新存档：手机放进背包了。");
				return "放进背包";
			}
			player.mailbox.Add(MailId);
			_log.Debug("这个存档还没有手机，刘易斯明天寄一部。");
			return "寄信";
		}
		if (!PhoneExistsAnywhere(player))
		{
			player.mailbox.Add(ResendMailId);
			_log.Debug("哪里都找不到手机，刘易斯补寄一部。");
			return "补寄";
		}
		return "在";
	}

	public static bool IsPhone(Item? item) => item != null && string.Equals(item.QualifiedItemId, QualifiedItemId, StringComparison.OrdinalIgnoreCase);

	private static bool HasPhone(Farmer player) => player.Items.Any(IsPhone);

	private static bool PhoneExistsAnywhere(Farmer player)
	{
		if (HasPhone(player))
		{
			return true;
		}
		bool found = false;
		Utility.ForEachItem(item =>
		{
			found = IsPhone(item);
			return !found;
		});
		return found;
	}

	public void OnButtonPressed(ButtonPressedEventArgs e)
	{
		if (!Context.IsWorldReady || Context.ScreenId != 0 || !Context.IsPlayerFree || _input.IsSuppressed(e.Button) || !IsPhone(Game1.player?.CurrentItem))
		{
			return;
		}
		bool use = e.Button.IsUseToolButton();
		bool action = e.Button.IsActionButton() && !SomethingToInteract(e.Cursor.GrabTile) && !SomethingToInteract(e.Cursor.Tile);
		if (!use && !action)
		{
			return;
		}
		_input.Suppress(e.Button);
		Open();
	}

	private static bool SomethingToInteract(Vector2 tile)
	{
		GameLocation? loc = Game1.currentLocation;
		if (loc == null)
		{
			return false;
		}
		int x = (int)tile.X, y = (int)tile.Y;
		return loc.isCharacterAtTile(tile) != null || loc.isActionableTile(x, y, Game1.player) || loc.objects.ContainsKey(tile) || loc.GetFurnitureAt(tile) != null;
	}

	private void Open()
	{
		Game1.playSound("bigSelect");
		_open();
	}

	public string Command(string[] args)
	{
		Farmer p = Game1.player;
		switch (args.Length > 0 ? args[0].ToLowerInvariant() : "dump")
		{
			case "give":
				return Deliver(p);
			case "hold":
				for (int i = 0; i < p.Items.Count; i++)
				{
					if (IsPhone(p.Items[i]))
					{
						p.CurrentToolIndex = i;
						return "拿在手上了（第 " + i + " 格）";
					}
				}
				return "背包里没有";
			case "lose":
				int n = 0;
				for (int i = 0; i < p.Items.Count; i++)
				{
					if (IsPhone(p.Items[i]))
					{
						p.Items[i] = null;
						n++;
					}
				}
				return "扔掉了 " + n + " 部";
			default:
				return $"背包里 {p.Items.Count(IsPhone)} 部，收过={p.mailReceived.Contains(ReceivedFlag)}，信箱={string.Join(",", p.mailbox.Where(m => m.StartsWith("AIStardewValley.Core_Phone", StringComparison.Ordinal)))}，名字={ItemRegistry.Create(QualifiedItemId).DisplayName}";
		}
	}
}
