using System;
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

	/// <summary>带着某一条打开时，第一次画完滚到那一条。</summary>
	private string? _jump;

	public HelpPage(ShellContext ctx, string? topic = null) : base(ctx)
	{
		if (topic != null)
		{
			_open.Add(topic);
			_jump = topic;
		}
	}

	public override string Title => Tr.Get("apps.help", "使用帮助");

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public static string Summary() => Tr.Get("help.summary", "新玩法怎么用");

	private static IEnumerable<Topic> Topics()
	{
		yield return new Topic("kiss", Tr.Get("help.kiss.title", "亲吻和拥抱"), Tr.Get("help.kiss.body", "结婚 10 心以上，站在配偶左边或右边点 TA，头顶会冒出两个按钮：聊天、亲吻（室友是拥抱），可以用鼠标、手柄或数字键选。每天第一次亲吻加好感。\n按住 Alt（手柄按住 LB）再点任何村民，走游戏原来的互动，不经过 AI。\n不想要这个选择：设置 > 常用 > AI 里关掉“点配偶先选聊天还是亲吻”。"));
		yield return new Topic("talkfirst", Tr.Get("help.talkFirst.title", "我先开口"), Tr.Get("help.talkFirst.body", "平时点村民是 TA 先说一句。打开“我先开口”后，对话框是空的，等你先说。\n对话框里也能切换：“让 TA 先说”这次请 TA 开场；右上角的“下次我先开口”开关决定以后。\n第一次见面、剧情收尾这些 TA 有话要说的时候，还是 TA 先开口。\n设置 > 常用 > AI > 我先开口。"));
		yield return new Topic("pets", Tr.Get("help.pets.title", "宠物的心里话"), Tr.Get("help.pets.body", "宠物在你身边时，偶尔头上冒一个灰色泡泡，是它的心里话：饿了、想被摸、好奇草丛里有什么……\n只是自言自语，暂时不能和宠物聊天；点宠物还是原来的摸摸。\n设置 > 家里 > 宠物：可以关掉，或调每天几句、冒泡的机会。"));
		yield return new Topic("kids", Tr.Get("help.kids.title", "和孩子聊天"), Tr.Get("help.kids.body", "孩子会走路以后，每天第一次点孩子会打开对话框，可以聊几句。孩子说话像个小孩，不会谈恋爱、钱这些大人的事。\n给孩子写角色卡：设置 > 设定 > 村民角色卡，孩子排在最前面，可以写性格、外貌、说话方式。\n设置 > 家里 > 孩子：关掉 / 只说一句 / 可以聊天。"));
		yield return new Topic("runaway", Tr.Get("help.runaway.title", "孩子离家出走"), Tr.Get("help.runaway.body", "上午偶尔会有孩子偷偷跑到农场上躲起来玩。配偶会来告诉你（不在身边就发消息）。\n怎么找：靠近时能听到笑声，地上有小脚印，配偶每两个小时会告诉你大概的方向。找到了点一下孩子就带回家，配偶会松一口气。\n傍晚还没找到，配偶会自己去领回来，但心情会差一点。\n设置 > 家里 > 孩子：可以关掉，或调发生的机会。"));
		yield return new Topic("growing", Tr.Get("help.growing.title", "孩子长大"), Tr.Get("help.growing.body", "孩子学会走路以后还会长大：默认再过两季到上学的年纪，再过一年成少年（设置 > 家里 > 孩子里可以改快慢，也可以关掉）。长大那天会换个样子，聊天时有了头像，说的话也更长、更像那个年纪；第一次见到你会跑过来说一句，配偶会来信。\n养成什么性子看你怎么陪：常聊天、常陪着玩的活泼，很少理会的文静，常去看上课的好奇，每次很快找到的黏人，跑出去很久才找回来的独立、调皮。长大那天按这些写下孩子现在的样子；角色卡里写了性格的，还是以角色卡为准。\n上学：佩妮上课的日子（原版是星期二、三、五上午），上学年纪的孩子会去博物馆和贾斯、文森特一起上课，下午两点回家；你去教室看一眼，孩子会很开心，佩妮聊天时也会说起这孩子学得怎么样。\n少年：周末（不下雨时）上午十点到下午四点去镇上逛；配偶带孩子的时候，少年会帮着浇几块地。少年不再离家出走。\n联机：孩子的阶段由房主那边推进，大家看到的一样；性子、长大那天的话和来信在父母自己那边。"));
		yield return new Topic("mart", Tr.Get("help.mart.title", "农夫小超市：在哪里建、怎么开"), Tr.Get("help.mart.body2", "【在哪里建】去 Robin 的木匠铺（镇子北边、山脚下），点柜台 > 建造农场建筑 > 选“农夫小超市”（15000g、木材 300、石头 200）。在农场上挑个空地放下，两天后建好，从正面的门进去。\n【店里】照皮埃尔杂货店的样子：左上角是柜台和收银机，已经摆好两张白货架、一个玻璃冷柜、一个木柜、一个促销桶，右下角的大箱子是库房。还想要货架（窄货架、保温柜……）去 Robin 店里买。\n【上架】拿着东西点货架就放上去；空手点货架打开货架窗口，下面是背包，点东西上架（右键只放一个），点格子改价或取下。鱼和蛋奶放冷柜坏得慢，熟食放保温柜。\n【新鲜度】从上架那天算：当日上架 > 新鲜 > 正常 > 临期 > 过期。过期的基本没人买，还会被差评。\n【收银】营业时间（默认 9:00 - 17:00）你在店里时，村民进门挑货、到柜台排队。点柜台开始收银：一样样扫进袋子，用硬币凑出找零，又快又对有小费。等太久顾客会放下东西走掉。不想玩可以雇收银员，或在设置里关掉“收银小游戏”。你不在时照样卖，钱进钱箱，点柜台取出。\n【经营】手机 > 小超市：选店型、看今天的行情、进货（明早送到库房）、贴海报、放橱窗、吆喝、雇店员（收银 / 理货 / 迎宾）、看评价。评价越好来的人越多。\n和 Farmer's Store 的分店是两回事，两家可以同时开。\n设置 > 小超市：开关、营业时间、收银小游戏。"));
		yield return new Topic("compete", Tr.Get("help.compete.title", "市场竞争和上门理论"), Tr.Get("help.compete.body", "你的店卖得越好，Pierre 的杂货店和 Joja 超市生意越差：正常 > 吃紧 > 亏损 > 停业整顿。生意差了会缩短营业时间、门口贴告示，但种子一定买得到；这只影响 AI星露谷 自己的经济，原版剧情不受影响。\n亏损以后，店主可能冲进你的店里理论。你可以：讲道理（自己打字说服他）、让一步（一周内涨价两成）、动手（像原版过场那样给一拳，对方退几步就走了，没人受伤，但会被镇长罚款、好感大降、上周报）。\n不想看到动手：设置 > 小超市 里关掉“允许动手”。手机 > 小超市 里可以入股对手，入股后不再抢生意。"));
		yield return new Topic("stolen", Tr.Get("help.stolen.title", "赃物和店员"), Tr.Get("help.stolen.body2", "偷来的东西也能摆上架，买的人偶尔会认出来（概率不大）：失主最容易认出，家人和好朋友其次，路人很少认得出；越贵、越便宜得可疑越容易露馅，放久了就难认了。\n【被认出以后】东西被拿走、不付钱，镇长罚款（货值 5 倍，300～3000g），第二天来信。认出的人按脾气当场翻脸：暴脾气的跳脚大骂，冷脸的一句狠话从此不理你，心软的委屈得快哭，嘴毒的往死里挖苦。好感和心情都会掉；失主之后会找上门来吵，皮埃尔和爱看热闹的人会来取笑你；这事会在镇上传开，还会上周报，店的口碑变差。\n【一季里越多越重】第 2 次罚款翻倍、停业整顿 3 天；第 3 次起罚三倍、赃物全部没收、停业 7 天，你被带走（装了 Law and Order 关进牢里，没装由镇长押回家），当天结束。换季清零。设置 > 小超市可以关掉“赃物可能被认出来”，设置 > 后果可以只关掉被带走。\n店员：手机 > 小超市 > 店员，最多三位，要 4 心以上。收银员站在柜台后面替你结账；理货员 9、12、15 点撤下过期的、把库房的货摆上去；迎宾在门口打招呼，评价更好。合同签 7 天或 28 天（28 天的每天便宜一点），到期可以续约。工钱每晚发，先从钱箱里扣。"));
		yield return new Topic("consequences", Tr.Get("help.consequences.title", "做错事的后果"), Tr.Get("help.consequences.body", "做了对不起人的事，除了原本就有的后果（掉好感、被说几句），同一季里犯得越多越重，换季清零。\n【放人鸽子】约好了没去：对方记成你说话不算话，心情变差；第二回传开；第三回这一周不肯再和你约。\n【答应的事没办】托付、请求到期没办：记成说话不算话，好感掉一点；第二回传开，大家说你不靠谱。\n【欠钱不还】借的钱、在格斯那里赊的账到期没还：镇长来信；第二回传开；第三回起两周内谁也不肯借钱给你，格斯也不让赊账。\n【在店里动手】上门理论时动手：第二回罚款加倍、镇长来信；第三回被带走，当天结束。\n【偷东西被抓】装了 Stardew Thief 时，屡次当场被抓会传开，第三回镇长来信（罚款和牢房仍由 Stardew Thief 管）。\n【翻垃圾桶】同一季屡次翻别人家门口的垃圾桶：主人来信，第三回大家开始给你起外号。\n【当着恋人调情】当着恋人、配偶的面和别人调情：对方会来质问你，伤心好几天，好感和心情都掉；配偶这几天不帮你干农活。\n每一种都能在 设置 > 后果 里单独关掉；“屡犯会被带走”也在那里。"));
		yield return new Topic("life", Tr.Get("help.life.title", "村民自己的人生"), Tr.Get("help.life.body", "村民也有自己想做成的事：海莉想办摄影展，山姆的乐队想第一次登台，罗宾想在镇上搭一座凉亭……同时有几位在忙（默认 3 位），每周可能往前走一步，进展会传成八卦、上周报，聊天时他们也会提起。\n【帮忙】走到某一步时，村民会托你找样东西（任务日志里能看到）。送到了，心愿更快达成。\n【达成】村民会来信请你去看。信里写的那一周、那个时间去那里，就能看到一段结局；日记里会夹一幅画。有的人之后会换工作，账本和聊天里跟着变。\n【恋爱】暗生好感的两位村民可能开始交往，每周晚上约会一次（和你的约见、帮工撞上时给你让路）；交往久了可能订婚，一周后在镇广场办婚礼（上午九点到下午四点去镇上就能看到），婚后还住在各自原来的地方。订婚、结婚以后家谱里也画在一起。也可能分手。你在追求、交往或已经娶嫁的村民不会和别人谈；你和其中一位谈起恋爱，那一对就散了。\n设置 > 镇上的生活 > 村民的人生：总开关、同时几位、婚礼、换工作。村民之间的恋爱也受“村民之间可能产生好感”控制。"));
		yield return new Topic("meetup", Tr.Get("help.meetup.title", "约见和陪你走走"), Tr.Get("help.meetup.body", "聊天时村民可能约你某天几点在某处见面，答应以后到时去那里就行。\n【迎上来】到点后你在同一张地图上，对方看见你会迎上来，先开口说说你们约好的事，接着可以回话。\n【进屋找你】过了二十分钟你还没到、人在附近的屋里，对方会推门进来找你；你家要 4 心以上（伴侣、恋人不用）才进，不然在门口等。\n【陪你走】见到以后跟着你走两小时左右（最晚晚上十点），换地图也跟着，路上偶尔说一句；矿洞、赌场、更衣室这些地方不进，到那儿就道别。手机 > 今天里点“先到这儿吧”可以让对方先回去。\n【同类】欠钱到期不还，借钱的人会当面来找你要；请来帮工的人到了先过来打招呼；有心事的村民你进了屋也会跟进来找你。\n约好了没去算放鸽子。设置 > 世界：见面后陪你走一段、进屋找你。"));
		yield return new Topic("spouse", Tr.Get("help.spouse.title", "配偶的日常安排"), Tr.Get("help.spouse.body", "婚后配偶每天早上会排好自己的安排：浇水、收菜、照顾动物、添干草、捡蛋、清理农场、打理温室和鱼塘，也会去镇上找人聊天、买东西、去小超市看店，或者带孩子在院子里玩。\n你在农场时能看到 TA 出门干活；你不在时 TA 也会做完。收获的东西放在收获箱里。\n手机 > 家务安排：看今天的进度、领收获、关掉某一项、让 TA 帮你做什么。聊天时说“帮我浇水”也行，配偶帮忙不收钱。\n设置 > 家里 > 配偶日程：勤快程度、收获放哪、每天花多少钱。好感低时 TA 会少做甚至不做。"));
		yield return new Topic("multiplayer", Tr.Get("help.multiplayer.title", "联机一起玩"), Tr.Get("help.multiplayer.body", "【怎么用】大家装同一版本的 AI 星露谷就行。全村的事由房主那边算：八卦、关系网、村民的人生、周报、今天的心情、村民账本和委托板，农场帮手那边会自动和房主保持一样。版本不一样时农场帮手会看到提示，他那边的功能只在自己这里，不和房主同步。\n【农场帮手能做的】和村民聊天（聊的事晚上会传成全村的八卦，村民也知道今天别的农夫找他聊过什么）；当面聊天时约村民见面、请村民来农场帮工（村民由房主那边安排着走过来，好感、退钱和提示落在你自己身上；发消息约还只有房主能用）；在手机上接委托、托村民办事、向村民借钱和还钱；结了婚的话，配偶每天也会排好自己的安排，帮你收的东西放进你自己的收获箱。\n【各算各的】做错事的后果落在做事的人身上：罚款从他身上扣（共用钱包时从大家的钱里扣），来信、被带走也只轮到他。信件、日记、村民心里对你的看法，每个人都是自己的。\n【还只有房主能用的】农夫小超市、在手机上发消息约村民、孩子离家出走。\n【分屏】同一台电脑上分屏两个人玩时，第二个画面用不了需要和房主同步的功能。"));
		yield return new Topic("trouble", Tr.Get("help.trouble.title", "出了问题怎么办"), Tr.Get("help.trouble.body", "村民不说话、说的话不对劲、某个玩法没反应时：\n【自检】设置 > 常用 > AI 状态 > 自检（或在 SMAPI 窗口输入 aisv_selfcheck）。会看 AI 能不能来回一次、存档数据读不读得出、素材文件齐不齐、和别的 mod 有没有冲突、各个玩法现在的状态，几秒后在按钮下面告诉你结果。\n【反馈问题】完整结果写在 Mods/AIStardewValley/debug/selfcheck.txt，密钥已经遮住了。反馈时把它和 SMAPI 日志一起发过来。\n【测试连接】同一页的“测试连接”只试一次 AI：连不连得上、多久回话。"));
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
		int jumpTo = -1;
		foreach (Topic t in Topics())
		{
			bool open = _open.Contains(t.Id);
			if (t.Id == _jump)
			{
				jumpTo = y + _scroll.Offset - area.Y - 18;
			}
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
		if (_jump != null)
		{
			_jump = null;
			_scroll.By(Math.Max(0, jumpTo) - _scroll.Offset);
		}
	}
}
