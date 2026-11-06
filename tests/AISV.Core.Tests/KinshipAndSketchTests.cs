using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AISV.Core.Apps;
using AISV.Core.Social.Diary;
using Xunit;

namespace AISV.Core.Tests;

public class KinshipAndSketchTests
{
	private static readonly string[] Town = { "Pierre", "Caroline", "Abigail", "Robin", "Demetrius", "Sebastian", "Maru", "Marnie", "Shane", "Jas", "George", "Evelyn", "Alex", "Linus" };

	private static KinTree TreeOf(KinRegister reg, string person) => reg.Trees.Single(t => !t.IsFarmers && t.Everyone.Contains(person));

	private static KinCell CellOf(KinTree tree, string person) => tree.Root.SelfAndBelow().Single(c => c.People.Contains(person));

	[Theory]
	[InlineData("[LocalizedText Strings\\Characters:Relative_Mom]", ElderRole.Parent)]
	[InlineData("stepdad", ElderRole.Parent)]
	[InlineData("妈妈", ElderRole.Parent)]
	[InlineData("grandmother", ElderRole.Grandparent)]
	[InlineData("godfather", ElderRole.Godparent)]
	[InlineData("uncle", ElderRole.AuntOrUncle)]
	public void RelativeCalledElderIsTheElder(string title, ElderRole role)
	{
		Assert.Equal(new KinClaim("Tom", "Mia", null, role), Kinship.ReadTitle("Mia", "Tom", title));
	}

	[Theory]
	[InlineData("daughter", ElderRole.Parent)]
	[InlineData("granddaughter", ElderRole.Grandparent)]
	[InlineData("godson", ElderRole.Godparent)]
	[InlineData("nephew", ElderRole.AuntOrUncle)]
	public void OwnerIsTheElderOfYoungerRelatives(string title, ElderRole role)
	{
		Assert.Equal(new KinClaim("Mia", "Tom", null, role), Kinship.ReadTitle("Mia", "Tom", title));
	}

	[Theory]
	[InlineData("wife", PairKind.Married)]
	[InlineData("fiance", PairKind.Engaged)]
	[InlineData("女朋友", PairKind.Dating)]
	[InlineData("twin", PairKind.Siblings)]
	public void PairsKeepBothNames(string title, PairKind pair)
	{
		Assert.Equal(new KinClaim("Mia", "Tom", pair, null), Kinship.ReadTitle("Mia", "Tom", title));
	}

	[Theory]
	[InlineData("best friend")]
	[InlineData("")]
	[InlineData(null)]
	public void UnknownTitlesAreIgnored(string? title)
	{
		Assert.Null(Kinship.ReadTitle("Mia", "Tom", title));
		Assert.Null(Kinship.ReadTitle("Mia", "Mia", "wife"));
	}

	[Fact]
	public void TownFamiliesBecomeTreesAndLonersAreListed()
	{
		KinRegister reg = Kinship.Build(new KinSources { Villagers = Town });
		KinTree pierre = TreeOf(reg, "Pierre");
		Assert.Equal("Pierre", pierre.Root.People[0]);
		Assert.Equal(new[] { "Abigail", "Caroline", "Pierre" }, pierre.Everyone.OrderBy(n => n));
		Assert.Contains("Linus", reg.Alone);
		Assert.DoesNotContain("Maru", reg.Alone);
	}

	[Fact]
	public void ChildrenHangBelowTheirParentsAndParentsSitCentered()
	{
		KinTree tree = TreeOf(Kinship.Build(new KinSources { Villagers = Town }), "Robin");
		Assert.Same(tree.Root, CellOf(tree, "Demetrius"));
		KinBelow maru = tree.Root.Below.Single(b => b.Person == "Maru");
		KinBelow seb = tree.Root.Below.Single(b => b.Person == "Sebastian");
		Assert.Equal(new[] { "Robin", "Demetrius" }, maru.Elders);
		Assert.Equal(new[] { "Robin" }, seb.Elders);
		Assert.Equal(1, maru.Cell.Depth);
		Assert.Equal(1f, Math.Abs(maru.Cell.Left - seb.Cell.Left));
		Assert.Equal((maru.Cell.Left + seb.Cell.Left) / 2f, tree.Root.Center);
	}

	[Fact]
	public void NearestElderHoldsTheChildAndOthersBecomeSides()
	{
		KinTree tree = TreeOf(Kinship.Build(new KinSources { Villagers = Town }), "Jas");
		Assert.Equal(new[] { 0, 1, 2 }, new[] { "Marnie", "Shane", "Jas" }.Select(n => CellOf(tree, n).Depth));
		Assert.Equal(new KinSide("Marnie", "Jas", ElderRole.AuntOrUncle), Assert.Single(tree.Sides));
		Assert.Equal(1, CellOf(TreeOf(Kinship.Build(new KinSources { Villagers = Town }), "Alex"), "Alex").Depth);
	}

	[Fact]
	public void WideSubtreesDoNotOverlap()
	{
		string[] people = { "A", "B", "C", "D", "E", "F" };
		(string, string, string)[] declared = { ("B", "A", "mother"), ("C", "A", "mother"), ("D", "B", "father"), ("E", "B", "father"), ("F", "C", "mother") };
		KinTree tree = Kinship.Build(new KinSources { Villagers = people, Declared = declared }).Trees.Single();
		Assert.Equal(3f, tree.Width);
		float[] row = new[] { "D", "E", "F" }.Select(n => CellOf(tree, n).Left).ToArray();
		Assert.Equal(new[] { 0f, 1f, 2f }, row);
		Assert.Equal(0.5f, CellOf(tree, "B").Left);
		Assert.Equal(2f, CellOf(tree, "C").Left);
		Assert.Equal(1f, tree.Root.Left);
	}

	[Fact]
	public void MarriageBeatsDatingAndDatingDoesNotJoinFamilies()
	{
		KinRegister dating = Kinship.Build(new KinSources { Villagers = new[] { "Pierre", "Abigail", "Caroline", "Mia" }, Declared = new[] { ("Mia", "Abigail", "girlfriend") } });
		Assert.Contains("Mia", dating.Alone);
		KinRegister married = Kinship.Build(new KinSources { Villagers = new[] { "A", "B" }, Declared = new[] { ("A", "B", "girlfriend"), ("A", "B", "wife") } });
		Assert.Equal(PairKind.Married, married.Trees.Single().Root.Pairs.Single());
	}

	[Fact]
	public void DeclaredLoopsDoNotHang()
	{
		KinRegister reg = Kinship.Build(new KinSources { Villagers = new[] { "A", "B" }, Declared = new[] { ("A", "B", "mother"), ("B", "A", "mother") } });
		Assert.Equal(2, reg.Trees.SelectMany(t => t.Everyone).Count());
	}

	[Fact]
	public void AbsentRelativesAreLeftOut()
	{
		KinRegister reg = Kinship.Build(new KinSources { Villagers = new[] { "Pierre", "Abigail" }, Away = new[] { "Abigail" } });
		Assert.Single(reg.Trees);
		Assert.Contains("Abigail", reg.Away);
		Assert.DoesNotContain("Abigail", reg.Alone);
	}

	[Fact]
	public void FarmerFamilyComesFirstWithSpouseAndChildren()
	{
		string kid = Kinship.Outsider("小豆");
		KinRegister reg = Kinship.Build(new KinSources { Villagers = Town, Farmer = new FarmerHome { Spouse = "Abigail", Children = new[] { kid } } });
		KinTree mine = reg.Trees[0];
		Assert.True(mine.IsFarmers);
		Assert.Equal(new[] { Kinship.Me, "Abigail" }, mine.Root.People);
		KinBelow child = Assert.Single(mine.Root.Below);
		Assert.Equal(new[] { Kinship.Me, "Abigail" }, child.Elders);
		Assert.Equal(0.5f, child.Cell.Left);
		Assert.Equal("小豆", Kinship.OutsiderName(kid));
	}

	[Fact]
	public void MoodsStayInsideTheFamily()
	{
		KinRegister reg = Kinship.Build(new KinSources { Villagers = Town, Moods = new[] { new KinMood("Pierre", "Abigail", Bond.Tense), new KinMood("Pierre", "Alex", Bond.Rivals) } });
		Assert.Equal(Bond.Tense, TreeOf(reg, "Pierre").Moods.Single().Bond);
	}

	private static DiaryEntry Entry(int day, string text, string season = "spring") => new DiaryEntry { Day = day, Season = season, Year = 1, Text = text };

	private static readonly DiarySketch[] TwoOfHers = { new DiarySketch("a.png", "Haley"), new DiarySketch("b.png", "Haley") };

	[Fact]
	public void SketchGoesToFirstEntryNamingTheVillager()
	{
		List<PinnedSketch> pins = DiarySketches.Pin(new[] { Entry(3, "和海莉聊天"), Entry(1, "今天种地") }, n => n == "Haley" ? "海莉" : n, TwoOfHers);
		Assert.Equal(3, Assert.Single(pins).Entry.Day);
	}

	[Fact]
	public void SameVillagerWaitsBetweenSketches()
	{
		DiaryEntry[] diary = { Entry(1, "Haley"), Entry(2, "Haley"), Entry(14, "Haley"), Entry(15, "Haley"), Entry(16, "Haley") };
		List<PinnedSketch> pins = DiarySketches.Pin(diary, n => n, TwoOfHers);
		Assert.Equal(new[] { 1, 15 }, pins.Select(p => p.Entry.Day));
		Assert.Equal(new[] { "a.png", "b.png" }, pins.Select(p => p.Sketch.File));
	}

	[Fact]
	public void SketchesStayInPlaceAsTheDiaryGrows()
	{
		List<DiaryEntry> diary = new List<DiaryEntry> { Entry(5, "Haley") };
		string first = DiarySketches.Pin(diary, n => n, TwoOfHers)[0].Sketch.File;
		diary.Add(Entry(25, "Haley"));
		Assert.Equal(first, DiarySketches.Pin(diary, n => n, TwoOfHers)[0].Sketch.File);
	}

	[Theory]
	[InlineData("I met Sam today", "Sam", true)]
	[InlineData("今天和Sam聊天", "Sam", true)]
	[InlineData("the same as before", "Sam", false)]
	[InlineData("和山姆聊天", "山姆", true)]
	[InlineData("anything", "", false)]
	public void NamesMustBeWholeWords(string text, string name, bool expected)
	{
		Assert.Equal(expected, DiarySketches.Mentions(text, name));
	}

	[Fact]
	public void EveryBuiltInSketchFileExists()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AISV.sln")))
		{
			dir = dir.Parent;
		}
		foreach (string file in DiarySketches.All.Select(s => s.File).Append(DiarySketches.CoverFile))
		{
			Assert.True(File.Exists(Path.Combine(dir!.FullName, "src/AISV.Mod/assets/diary", file)), file);
		}
	}

	[Fact]
	public void PdfOffsetsPointAtTheirObjectsAndImagesInflateBack()
	{
		byte[] rgb = Enumerable.Range(0, 2 * 3 * 3).Select(i => (byte)i).ToArray();
		PdfBook book = new PdfBook();
		book.AddPage(rgb, 2, 3);
		book.AddPage(rgb, 3, 2);
		using MemoryStream output = new MemoryStream();
		book.Save(output, "牛哥的日记");
		byte[] bytes = output.ToArray();
		string text = Encoding.Latin1.GetString(bytes);
		Assert.StartsWith("%PDF-", text);
		Assert.Contains("/Count 2", text);
		string[] tail = text.Substring(text.LastIndexOf("startxref", StringComparison.Ordinal)).Split('\n');
		int table = int.Parse(tail[1]);
		string[] rows = text.Substring(table).Split('\n');
		Assert.Equal("xref", rows[0]);
		int count = int.Parse(rows[1].Split(' ')[1]);
		Assert.Equal(10, count);
		for (int id = 1; id < count; id++)
		{
			int at = int.Parse(rows[2 + id].Substring(0, 10));
			Assert.StartsWith($"{id} 0 obj", text.Substring(at));
		}
		int stream = text.IndexOf("/FlateDecode", StringComparison.Ordinal);
		int start = text.IndexOf("stream\n", stream, StringComparison.Ordinal) + 7;
		using ZLibStream zlib = new ZLibStream(new MemoryStream(bytes, start, bytes.Length - start), CompressionMode.Decompress);
		byte[] back = new byte[rgb.Length];
		zlib.ReadExactly(back);
		Assert.Equal(rgb, back);
	}

	[Fact]
	public void PdfNeedsPagesOfTheRightSize()
	{
		PdfBook book = new PdfBook();
		Assert.Throws<ArgumentException>(() => book.AddPage(new byte[5], 2, 3));
		Assert.Throws<InvalidOperationException>(() => book.Save(new MemoryStream(), "x"));
	}
}
