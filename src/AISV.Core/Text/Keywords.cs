using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AISV.Core.Text;

/// <summary>
/// 规则判断用的关键词，中文为主，带常见英文。全部小写，用“|”隔开；匹配前把文本也转小写。
/// 前面留空格的英文词只给 Drama 系列用：那边匹配时会在文本两头补空格，靠它避开词中间的误伤。
/// 词尽量写成整句里的短语，因为性格信号会把村民自己的台词一起算进去。
/// </summary>
public static class Keywords
{
	public static readonly IReadOnlyList<string> Apology = Split(
		"对不起|抱歉|道歉|赔不是|赔罪|赔礼|是我不对|是我不好|是我错了|我错了|我的错|都怪我|怪我不好|原谅我|我不该|说错话|过意不去|别生我的气|消消气",
		"sorry|apologi|my bad|my fault|forgive me");

	public static readonly IReadOnlyList<string> Farmer = Split(
		"农夫|农场主|种地的|种田的|新来的|新搬来|搬来的|城里来的|老农场",
		"farmer|farm owner|newcomer");

	public static readonly IReadOnlyList<string> DramaNegative = Split(
		"撒谎|说谎|骗人|骗子|欺骗|骗了|欺负|小偷|偷东西|偷了|背叛|背着人|背着你|背着他|背着她|说坏话|坏话|虚伪|自私|粗鲁|卑鄙|无耻|阴险|下流|猥琐|"
		+ "冷血|势利|小气|懒鬼|懒惰|很懒|偷懒|作弊|赖账|欠钱不还|酗酒|酒鬼|烂醉|发脾气|吵架|吵了一架|真讨厌|很讨厌|讨厌他|讨厌她|真恶心|很恶心|"
		+ "看不起|瞧不起|没良心|忘恩负义|两面三刀|两面派|别有用心|不怀好意|没安好心|不诚实|不老实|不靠谱|靠不住|不可信|信不过|失望|很糟糕|真糟糕|混蛋|坏人|害人",
		" liar| lied| cheat| thief| stole| betray| selfish| rude| lazy| cruel| fake|dishonest|untrustworthy|backstab|disappoint|two-faced|bully");

	public static readonly IReadOnlyList<string> DramaPositive = Split(
		"善良|我支持|支持他|支持她|人很好|人真好|大好人|真是个好人|靠得住|信得过|值得信任|很可靠|很诚实|很正直|热心|体贴|温柔|勤快|勤劳|能干|了不起|"
		+ "有才华|有本事|帮了我|帮过我|帮了大忙|对我很好|友善|佩服|敬佩|感激他|感激她|冤枉|不是那种人|不是那样的人|别误会他|别误会她|替他说话|替她说话",
		" honest| trustworthy| reliable| generous| friendly| caring| brave| talented| admire|hardworking|hard-working| is kind|so kind|very kind|good person|good man|good woman|i support|stand up for|helped me");

	public static readonly IReadOnlyList<string> DramaLeak = Split(
		"听说|听人说|据说|有人说|有人看见|传言|传闻|小道消息|秘密|别告诉别人|别说出去|偷偷告诉|悄悄告诉|私底下|背地里|被我撞见|我撞见|亲眼看见",
		"i heard|rumor|rumour|secret|between you and me|people say| word is");

	public static readonly IReadOnlyList<string> DramaAttack = Split(
		"小心他|小心她|小心点|当心|提防|防着|离他远|离她远|别信|别相信|不能信|不可信|信不过|靠不住|骗子|粗鲁|混蛋|坏人|危险|没安好心|不怀好意|别有用心|两面三刀|虚伪|卑鄙|阴险|无耻",
		"watch out|beware|be careful|stay away|don't trust|do not trust|dangerous| liar| rude| creep");

	public static readonly IReadOnlyList<string> SignalSupportive = Split(
		"我支持你|我相信你|我信你|站在你这边|站你这边|我陪你|陪着你|有我在|随时找我|我帮你|帮你一把|交给我|你做得很好|你做得很棒|你很棒|你可以的|为你骄傲|为你高兴|"
		+ "你不是一个人|你已经很努力|辛苦了|辛苦你了|别担心|别难过|别放弃|别勉强|慢慢来|会好起来|我听着|我懂你|理解你|多亏有你|有你真好",
		"i believe in you|i support you|proud of you|here for you|i'm with you|you can do it|you did great|take your time|don't worry|it'll be okay|lean on me");

	public static readonly IReadOnlyList<string> SignalApology = Split(
		"对不起|抱歉|向你道歉|跟你道歉|给你道歉|想道歉|赔不是|赔罪|是我不对|是我不好|是我错了|我错了|我的错|原谅我|我不该|你的道歉|原谅你",
		"sorry|apologi|my bad|my fault|forgive me|i was wrong");

	public static readonly IReadOnlyList<string> SignalInsult = Split(
		"白痴|蠢货|蠢猪|蠢死了|你真蠢|傻逼|傻子|废物|窝囊废|没用的东西|人渣|败类|混蛋|王八蛋|贱人|贱货|滚开|滚蛋|给我滚|闭嘴|去死|你有病|神经病|"
		+ "不要脸|没教养|活该|算什么东西|你算老几|老东西|老不死|肥猪|丑八怪|真丑|很丑|好丑|丑死了|长得丑|你真恶心|恶心死了|我讨厌你|真讨厌你|我恨你|你是垃圾|真垃圾",
		"idiot|stupid|moron|dumb|loser|pathetic|worthless|ugly|jerk|bitch|bastard|asshole|fuck you|shut up|get lost|go to hell|you suck|hate you");

	public static readonly IReadOnlyList<string> SignalPhotography = Split(
		"摄影|拍照|拍的照|照片|相机|镜头|暗房|胶卷|冲洗|取景|构图",
		"photo|camera|darkroom|lens|your pictures");

	public static readonly IReadOnlyList<string> SignalAppearance = Split(
		"外表|长相|长得|颜值|脸蛋|身材|好看|漂亮|美女|打扮|化妆|穿搭|发型|金发|丑八怪|真丑|很丑|好丑|丑死了|难看",
		"beautiful|gorgeous|good looking|good-looking|so pretty|you're pretty|your looks|your hair|your outfit|your face|your body|makeup|blonde|ugly");

	public static readonly IReadOnlyList<string> SignalFlirt = Split(
		"我喜欢你|好喜欢你|喜欢上你|我爱你|爱上你|暗恋你|表白|对你动心|为你心动|让我心动|一见钟情|好想你|很想你|想你了|想死你|想亲你|亲你一下|亲一口|吻你|想抱你|抱抱|牵你的手|牵手|"
		+ "和我约会|跟我约会|约你出去|一起去约会|和我在一起|跟我在一起|在一起吧|做我女朋友|做我男朋友|做我的女朋友|做我的男朋友|当我女朋友|当我男朋友|嫁给我|娶你|娶我|"
		+ "你真美|你好美|你真漂亮|你好漂亮|你真好看|你好好看|你真迷人|你好迷人|你很迷人|撩你|调情|和我上床|跟我上床|陪我睡|和我睡|跟我睡|一起睡吧",
		"i love you!|i love you.|i love you |in love with you|i have a crush|crush on you|date with me|go out with me|kiss you|kiss me|marry me|you're beautiful|you are beautiful|you're cute|you're gorgeous|sexy|flirt");

	public static readonly IReadOnlyList<string> SignalBoundary = Split(
		"脱衣服|脱掉衣服|脱了吧|摸你|摸摸你|让我摸|给我摸|和我上床|跟我上床|陪我睡|和我睡|跟我睡|强吻|给我亲|不许拒绝|由不得你|你逃不掉|别想走|别装了|偷看你|跟踪你|骚扰|"
		+ "别碰我|不要碰我|放开我|离我远点|请自重|你越界|太过分了",
		"take off your|let me touch|sleep with me|don't touch me|get off me|leave me alone|back off|crossed the line|gone too far|harass|stalking|stalker|naked");

	public static readonly IReadOnlyList<string> SignalInfidelity = Split(
		"背着他|背着她|背着你老公|背着你老婆|瞒着他|瞒着她|瞒着你|偷情|出轨|外遇|劈腿|地下情|第三者|小三|趁他不在|趁她不在|你老公不在|你老婆不在|你丈夫不在|你妻子不在|"
		+ "离开他吧|离开她吧|甩了他|甩了她|别告诉你老公|别告诉你老婆|别告诉你丈夫|别告诉你妻子|没人会知道|不会有人知道",
		"behind his back|behind her back|affair|cheat on|leave him|leave her|your husband|your wife|no one has to know|nobody has to know");

	/// <summary>文本（转小写后）里有没有任一关键词。</summary>
	public static bool Any(string? text, IReadOnlyList<string> words)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string lower = text.ToLowerInvariant();
		return words.Any(w => lower.Contains(w, StringComparison.Ordinal));
	}

	/// <summary>去掉重音符号（é → e），给不区分重音的匹配用。</summary>
	public static string StripAccents(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return value;
		}
		string decomposed = value.Normalize(NormalizationForm.FormD);
		StringBuilder sb = new StringBuilder(decomposed.Length);
		foreach (char c in decomposed)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				sb.Append(c);
			}
		}
		return sb.ToString().Normalize(NormalizationForm.FormC);
	}

	private static string[] Split(params string[] groups) =>
		groups.SelectMany(g => g.Split('|', StringSplitOptions.RemoveEmptyEntries)).ToArray();
}
