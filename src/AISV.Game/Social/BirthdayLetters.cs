using AISV.Core.Social;
using AISV.Core.Text;

namespace AISV.Game.Social;

/// <summary>农夫生日那天早上寄到的信（朋友、恋人、配偶三种口气，各四封，随机挑一封）。</summary>
public static class BirthdayLetters
{
	public static string Subject(string player) => Tr.Get("birthday.letterSubject", "生日快乐", ("player", player));

	public static string Body(BirthdayTone tone, int variant, string player)
	{
		string key = $"birthday.letter.{BirthdayRules.ToneKey(tone)}.{variant + 1}";
		string text = (tone, variant) switch
		{
			(BirthdayTone.Friend, 0) => "{{player}}：^^听说今天是你的生日，就赶在早上把这封信塞进了你的信箱。信封里顺手放了点小东西，别嫌弃。^^今天少干点活，好好过。",
			(BirthdayTone.Friend, 1) => "嘿，{{player}}！^^生日快乐！我不太会写这种信，反正意思就是——很高兴镇上多了你这么个人。^^附上一点心意，祝你今天顺顺利利。",
			(BirthdayTone.Friend, 2) => "{{player}}：^^又长大一岁啦。农场的事明天再操心也不迟，今天就当给自己放个假。^^小礼物在信封里，希望你用得上。",
			(BirthdayTone.Friend, _) => "{{player}}，见信好：^^日历翻到今天，我才敢确定没记错。祝你生辰愉快！^^小东西不值几个钱，合不合心意下回见面告诉我。",
			(BirthdayTone.Romance, 0) => "{{player}}：^^本想当面对你说，可又忍不住想让你一睁眼就收到——生日快乐。^^信封里的东西，看到的时候要想起我哦。",
			(BirthdayTone.Romance, 1) => "亲爱的{{player}}：^^今天醒来第一件事就是想到你。生日快乐！^^礼物不大，心意可不小。晚点来找我，好吗？",
			(BirthdayTone.Romance, 2) => "{{player}}：^^今天整个山谷都该为你高兴一下，至少我是这么觉得的。^^随信附上一份小礼物。今天留一点时间给我吧。",
			(BirthdayTone.Romance, _) => "给{{player}}：^^写了好几遍才写成这几行。生日快乐，有你在的日子总是特别一点。^^礼物在里面，等你来告诉我喜不喜欢。",
			(BirthdayTone.Spouse, 0) => "{{player}}：^^天天见面，偏要写这封信，就想看你拆信时的样子。又是你的生日了。^^礼物压在信纸底下。太阳落山前回来。",
			(BirthdayTone.Spouse, 1) => "{{player}}：^^和你一起过的每个生日，我都想好好记住。今天也一样。^^信封里有份小礼物。剩下的，等你回家再说。",
			(BirthdayTone.Spouse, 2) => "我的{{player}}：^^今天你最大。地里的活放一放，家里的事交给我。^^生日快乐，礼物收好。",
			_ => "给最重要的{{player}}：^^又陪你过了一岁，真好。生日快乐！^^先寄一份小礼物给你，算是今天的开头。爱你。"
		};
		return Tr.Get(key, text, ("player", player)).Trim();
	}

	/// <summary>生日当天上午发来的祝福消息。</summary>
	public static string Text(BirthdayTone tone, int variant, string player)
	{
		string key = $"birthday.text.{BirthdayRules.ToneKey(tone)}.{variant + 1}";
		string text = (tone, variant) switch
		{
			(BirthdayTone.Friend, 0) => "嘿，{{player}}，今天是你的大日子吧？生日快乐！给自己放半天假。",
			(BirthdayTone.Friend, 1) => "日历上画了个圈，原来是你生日。{{player}}，又长一岁啦，恭喜恭喜！",
			(BirthdayTone.Friend, 2) => "{{player}}，生日快乐。今天庄稼自己会长，你就负责开心。",
			(BirthdayTone.Friend, _) => "差点忘了跟你说——生日快乐，{{player}}！有你在，镇上热闹多了。",
			(BirthdayTone.Romance, 0) => "今天想第一个跟你说：{{player}}，生日快乐。",
			(BirthdayTone.Romance, 1) => "寿星今天有什么安排？要是还有空，留一点给我吧。生日快乐，{{player}}。",
			(BirthdayTone.Romance, 2) => "{{player}}，生日快乐。认识你以后，这一天对我也变得特别了。",
			(BirthdayTone.Romance, _) => "早上看到太阳就想，今天一定是好天气，因为是你生日。生日快乐呀，{{player}}。",
			(BirthdayTone.Spouse, 0) => "{{player}}，生日快乐。今天家里的事都归我，你只管好好过。",
			(BirthdayTone.Spouse, 1) => "你出门太早，我话还没说完呢：生日快乐！晚饭我来做，早点回来，{{player}}。",
			(BirthdayTone.Spouse, 2) => "又陪你过了一个生日，{{player}}。往后每一年，我都想这样陪着。",
			_ => "你前脚刚走，我后脚就惦记上了。{{player}}，寿星今晚有肉吃，别在外头耽搁。"
		};
		return Tr.Get(key, text, ("player", player)).Trim();
	}
}
