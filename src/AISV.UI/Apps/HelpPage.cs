using System.Collections.Generic;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AISV.UI.Apps;

/// <summary>使用帮助：每个玩法怎么触发、怎么操作、会怎样、在哪里关。点标题展开。</summary>
internal sealed class HelpPage : ShellPage
{
	private sealed record Topic(string Id, string Title, string Body);

	private readonly Scroller _scroll = new Scroller();

	private readonly HashSet<string> _open = new HashSet<string>();

	private Rectangle _view;

	public HelpPage(ShellContext ctx, string? topic = null) : base(ctx)
	{
		if (topic != null)
		{
			_open.Add(topic);
		}
	}

	public override string Title => Tr.Get("apps.help", "使用帮助");

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public static string Summary() => Tr.Get("help.summary", "新玩法怎么用");

	private static IEnumerable<Topic> Topics()
	{
		yield return new Topic("kiss", Tr.Get("help.kiss.title", "亲吻和拥抱"), Tr.Get("help.kiss.body", "结婚 10 心以上，站在配偶左边或右边点 TA，头顶会冒出两个按钮：聊天、亲吻（室友是拥抱），可以用鼠标、手柄或数字键选。每天第一次亲吻加好感。\n按住 Alt（手柄按住 LB）再点任何村民，走游戏原来的互动，不经过 AI。\n不想要这个选择：设置 > 常用 > AI 里关掉「点配偶先选聊天还是亲吻」。"));
		yield return new Topic("talkfirst", Tr.Get("help.talkFirst.title", "我先开口"), Tr.Get("help.talkFirst.body", "平时点村民是 TA 先说一句。打开「我先开口」后，对话框是空的，等你先说。\n对话框里也能切换：「让 TA 先说」这次请 TA 开场；右上角的「下次我先开口」开关决定以后。\n第一次见面、剧情收尾这些 TA 有话要说的时候，还是 TA 先开口。\n设置 > 常用 > AI > 我先开口。"));
		yield return new Topic("pets", Tr.Get("help.pets.title", "宠物的心里话"), Tr.Get("help.pets.body", "宠物在你身边时，偶尔头上冒一个灰色泡泡，是它的心里话：饿了、想被摸、好奇草丛里有什么……\n只是自言自语，暂时不能和宠物聊天；点宠物还是原来的摸摸。\n设置 > 家里 > 宠物：可以关掉，或调每天几句、冒泡的机会。"));
		yield return new Topic("kids", Tr.Get("help.kids.title", "和孩子聊天"), Tr.Get("help.kids.body", "孩子会走路以后，每天第一次点孩子会打开对话框，可以聊几句。孩子说话像个小孩，不会谈恋爱、钱这些大人的事。\n给孩子写角色卡：设置 > 设定 > 村民角色卡，孩子排在最前面，可以写性格、外貌、说话方式。\n设置 > 家里 > 孩子：关掉 / 只说一句 / 可以聊天。"));
		yield return new Topic("runaway", Tr.Get("help.runaway.title", "孩子离家出走"), Tr.Get("help.runaway.body", "上午偶尔会有孩子偷偷跑到农场上躲起来玩。配偶会来告诉你（不在身边就发消息）。\n怎么找：靠近时能听到笑声，地上有小脚印，配偶每两个小时会告诉你大概的方向。找到了点一下孩子就带回家，配偶会松一口气。\n傍晚还没找到，配偶会自己去领回来，但心情会差一点。\n设置 > 家里 > 孩子：可以关掉，或调发生的机会。"));
		yield return new Topic("mart", Tr.Get("help.mart.title", "农夫小超市：在哪里建、怎么开"), Tr.Get("help.mart.body", "【在哪里建】去 Robin 的木匠铺（镇子北边、山脚下），点柜台 > 建造农场建筑 > 选「农夫小超市」（15000g、木材 300、石头 200）。在农场上挑个空地放下，两天后建好，从正面的门进去。\n【摆店】建好后送你两张陈列台和一个收银台，摆进店里（不够可以在 Robin 店里再买）。\n【上架】拿着要卖的东西点陈列台就上架；空手点陈列台可以改价钱或取下。价钱可以在原价的五成到两倍之间调，便宜卖得快。\n【营业】默认 9:00 - 17:00。你在店里时村民会进门、挑货、到收银台结账；你不在时也照样卖，钱放进钱箱，点收银台取出。\n【看账】手机 > 小超市：今天的营业额、七天走势、货架、店员。\n和 Farmer's Store 的分店是两回事，两家可以同时开。\n设置 > 小超市：开关、营业时间。"));
		yield return new Topic("compete", Tr.Get("help.compete.title", "市场竞争和上门理论"), Tr.Get("help.compete.body", "你的店卖得越好，Pierre 的杂货店和 Joja 超市生意越差：正常 > 吃紧 > 亏损 > 停业整顿。生意差了会缩短营业时间、门口贴告示，但种子一定买得到；这只影响 AI星露谷 自己的经济，原版剧情不受影响。\n亏损以后，店主可能冲进你的店里理论。你可以：讲道理（自己打字说服他）、让一步（一周内涨价两成）、动手（卡通式把人打飞，没人受伤，但会被镇长罚款、好感大降、上周报）。\n不想看到动手：设置 > 小超市 里关掉「允许动手」。手机 > 小超市 里可以入股对手，入股后不再抢生意。"));
		yield return new Topic("stolen", Tr.Get("help.stolen.title", "赃物和店员"), Tr.Get("help.stolen.body", "偷来的东西也能摆上架，但买的人可能认出来：失主最容易认出，家人和好朋友其次，越贵、越便宜得可疑越容易露馅，放久了就难认了。被认出会把东西拿走、不付钱，好感和店的口碑都会下降。\n店员：手机 > 小超市 > 店员，可以雇两位村民。营业时间他们站在收银台后面，你不在时也直接收钱，客人更多。工钱每晚从钱箱里发。"));
		yield return new Topic("spouse", Tr.Get("help.spouse.title", "配偶的日常安排"), Tr.Get("help.spouse.body", "婚后配偶每天早上会排好自己的安排：浇水、收菜、照顾动物、添干草、捡蛋、清理农场、打理温室和鱼塘，也会去镇上找人聊天、买东西、去小超市看店，或者带孩子在院子里玩。\n你在农场时能看到 TA 出门干活；你不在时 TA 也会做完。收获的东西放在收获箱里。\n手机 > 家务安排：看今天的进度、领收获、关掉某一项、让 TA 帮你做什么。聊天时说「帮我浇水」也行，配偶帮忙不收钱。\n设置 > 家里 > 配偶日程：勤快程度、收获放哪、每天花多少钱。好感低时 TA 会少做甚至不做。"));
		yield return new Topic("keys", Tr.Get("help.keys.title", "常用按键"), Tr.Get("help.keys.body", "按住 Alt（手柄 LB）再点村民：游戏原来的互动。\n对话框里 Ctrl + 回车：发送。\n其余按键在 设置 > 按键 里看和改。"));
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		int pad = area.Width < 900 ? 16 : 32;
		int x = area.X + pad, w = area.Width - pad * 2 - 12;
		Hearth.BeginClip(b, area);
		int y = area.Y + 18 - _scroll.Offset;
		foreach (string line in Hearth.WrapLines(Tr.Get("help.intro", "点标题展开。所有玩法都能在设置里关掉。"), w, 0.75f))
		{
			Hearth.Ink(b, line, new Vector2(x, y), Hearth.QuillSoft, 0.75f);
			y += Hearth.LineHeight(0.75f);
		}
		y += 10;
		foreach (Topic t in Topics())
		{
			bool open = _open.Contains(t.Id);
			Rectangle head = new Rectangle(x, y, w, 48);
			Hearth.PressPlate(b, head, (open ? "- " : "+ ") + t.Title, hover: Hearth.Hover(head, area), scale: 0.85f);
			string id = t.Id;
			hits.Add(head, () =>
			{
				if (!_open.Remove(id))
				{
					_open.Add(id);
				}
			}, area);
			y += 56;
			if (!open)
			{
				continue;
			}
			foreach (string line in Hearth.WrapLines(t.Body, w - 16, 0.78f, paragraphGap: true))
			{
				Hearth.Ink(b, line, new Vector2(x + 8, y), Hearth.Quill, 0.78f);
				y += Hearth.LineHeight(0.78f);
			}
			y += 14;
		}
		Hearth.EndClip(b);
		_scroll.Finish(b, area, y + _scroll.Offset, 12);
	}
}
