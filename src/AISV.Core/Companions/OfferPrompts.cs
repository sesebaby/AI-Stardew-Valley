using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AISV.Core.Companions;

/// <summary>回复 JSON 里提议字段的说明。</summary>
public static class OfferPrompts
{
	public const string None = ",\n  \"plan_offer\": 一律 null。";

	public const string AlreadyToday = None + "今天你已经跟农夫提过一回约了，今天不再提新的；他再邀你，就说改天。";

	private static string MeetupHint() =>
		$"约见：when 写 today、tomorrow、in 2 days 或 in 3 days，time 写 {Clock.Text(MeetupRules.EarliestTime)} 到 {Clock.Text(MeetupRules.LatestTime)} 之间的 HH:MM，where 从这些里挑一个：{MeetupPlaces.PromptList()}。只有你真想在某个时间地点见农夫（或者农夫约你）时才提，says 里自然说出来。镇上过节那天不另约：节日大家都会去，想一块儿逛就在 says 里说，plan_offer 留空。";

	private static string VisitHint() =>
		$"帮工：不写 time 就是现在在农夫身边帮。要约好时间来农夫的农场（只限 water、harvest、animals、clear），when 写 today 或 tomorrow，time 写 {Clock.Text(FarmHelpRules.EarliestTime)} 到 {Clock.Text(FarmHelpRules.LatestStart)} 之间的 HH:MM。有工作的人只挑下班以后或者休息日。过节那天不约来帮工。";

	private static string TextingHint(bool work)
	{
		string kinds = work ? "两种提议算数：meetup，以及今天或明天来农夫农场帮工的 work（water、harvest、animals、clear）；follow 和别的活都不行" : "一种提议算数：meetup";
		string fill = work ? "农夫叫你过去、约你见面或者请你来农场帮忙，而你也愿意时" : "农夫叫你过去或者约你见面，而你也愿意时";
		string fields = work ? "（when、time，约见再加 where，帮工再加 task）" : "（when、time、where）";
		return $"你们这是在发消息，人不在一块儿。消息里只有{kinds}。{fill}，一定要把 plan_offer 填上{fields}，不填就什么也不会发生，所以别不填就答应要来。{(work ? "有工作的人只在下班后或者休息日来帮忙。" : "")}大家都靠走路，穿过山谷到农场要一两个小时，别说几分钟就到。农夫要你马上过去时，when 写 today、time 写 now，几点到由游戏算好告诉农夫。去不了或者不想去，就好好说不，别光嘴上答应。";
	}

	/// <summary>交情还不够答应的那几种，让村民自己推掉。</summary>
	private static string NotYet(List<string> things) =>
		things.Count == 0 ? "" : $"以你们现在的交情，你还不会{string.Join("、", things)}；农夫这么邀你，就照你的脾气推掉，别嘴上答应。";

	/// <param name="squad">接上了 Squad：可以提 follow 和 work。</param>
	/// <param name="texting">在发消息。</param>
	/// <param name="hearts">村民对农夫的心数；交情不够的提议不列出来。</param>
	/// <param name="partner">恋人或配偶，不看心数。</param>
	public static string Json(bool squad, bool texting, int hearts = 14, bool partner = false)
	{
		bool Can(string type) => partner || hearts >= OfferLedger.MinHearts(type);
		bool meetup = Can(OfferLedger.Meetup);
		bool work = squad && Can(OfferLedger.Work);
		bool follow = squad && !texting && Can(OfferLedger.Follow);
		List<string> notYet = new List<string>();
		if (!meetup)
		{
			notYet.Add("单独跟农夫约时间地点见面");
		}
		if (squad && !texting && !follow)
		{
			notYet.Add("陪他出门");
		}
		if (squad && !work)
		{
			notYet.Add(texting ? "上门帮他干活" : "帮他干活");
		}
		if (!meetup)
		{
			return None + NotYet(notYet);
		}
		List<string> wants = new List<string>();
		List<string> types = new List<string>();
		if (follow)
		{
			wants.Add("今天陪农夫出门（follow，比如下矿、去海边、进森林，只是陪着，不替他干活）");
			types.Add("follow");
		}
		if (work)
		{
			wants.Add(texting ? "约好时间来农夫的农场帮工（work）" : "帮农夫干活（work：现在就在身边帮，或者约好今天明天几点来农场；说要帮他浇水、收菜、喂动物、清地，哪怕是跟他一起回去干，也填 work）");
			types.Add("work");
		}
		wants.Add(texting ? "约个时间地点见农夫（meetup）" : "约个时间地点见面（meetup）");
		types.Add("meetup");
		StringBuilder sb = new StringBuilder(",\n  \"plan_offer\": 通常 null，最多一条。");
		sb.Append("只有你真心愿意").Append(string.Join(texting ? "，或者" : "、", wants)).Append("时才填");
		sb.Append(texting ? "：" : $"；农夫开口{(follow ? "请你陪同、" : "")}{(work ? "请你帮忙或者" : "")}约你，而你也愿意时同样要填：");
		sb.Append("{ \"kind\": \"").Append(string.Join(types.Count > 2 ? "、" : " 或 ", types.Take(types.Count - 1))).Append(types.Count > 1 ? " 或 " : "").Append(types[^1]).Append("\", ");
		if (work)
		{
			sb.Append(texting ? "\"task\": \"work 才填：water、harvest、animals、clear\", " : "\"task\": \"work 才填，从 water、harvest、forage、clear、animals、fish、mine、fight 里选\", ");
		}
		sb.Append("\"summary\": \"一句话说清这个提议，用玩家的语言\", \"when\": \"today、tomorrow 这样的日子\", \"time\": \"约见或者约好时间的帮工：HH:MM\", \"where\": \"地点，没有就空着\" }。");
		sb.Append("\"says\" 里也要把这个意思自然说出来。不许提钱、报价或者许诺东西，收费由游戏处理。");
		sb.Append(MeetupHint());
		if (work)
		{
			sb.Append(VisitHint());
		}
		if (texting)
		{
			sb.Append(TextingHint(work));
		}
		sb.Append(NotYet(notYet));
		return sb.ToString();
	}

	/// <summary>居民群里怎么约。</summary>
	public static string GroupRule(bool squad)
	{
		StringBuilder sb = new StringBuilder("农夫在群里叫人过去（比如去农场）、约人见面");
		sb.Append(squad ? "或者请人来农场帮忙时" : "时");
		sb.Append("，最多一位愿意的村民答应，这时一定要给他填 \"meetup\"，不填就什么也不会发生，所以谁都别不填就答应；其他人可以说自己没空。");
		if (squad)
		{
			sb.Append("来农场帮忙还要写 task（water、harvest、animals、clear，只限今天或明天，有工作的人只挑下班后或者休息日）；普通见面 task 留空。");
		}
		sb.Append("大家都靠走路，穿过山谷到农场要一两个小时，没人几分钟就到；要人马上过来时 when 写 today、time 写 now，几点到由游戏来算。");
		sb.Append($"时间在 {Clock.Text(MeetupRules.EarliestTime)} 到 {Clock.Text(MeetupRules.LatestTime)} 之间，where 从这些里挑：{MeetupPlaces.PromptList()}。");
		return sb.ToString();
	}

	/// <summary>居民群输出里的 meetup 字段。</summary>
	public const string GroupField = "\"meetup\" 是 null，或者 {\"speaker\":\"<答应的那一位，照抄名字>\",\"when\":\"today、tomorrow、in 2 days 或 in 3 days\",\"time\":\"HH:MM 或 now\",\"where\":\"地点\",\"task\":\"留空，或者 water、harvest、animals、clear\",\"summary\":\"一句话，用农夫的语言\"}。";
}
