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
	[InlineData("stepdad", ElderRole.Stepparent)]
	[InlineData("adoptive mother", ElderRole.Parent)]
	[InlineData("grand mother", ElderRole.Grandparent)]
	[InlineData("god father", ElderRole.Godparent)]
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
	[InlineData("brother-in-law")]
	[InlineData("mother-in-law")]
	[InlineData("ex-wife")]
	[InlineData("best friend's sister")]
	[InlineData("[LocalizedText Mods\\Kid.Pack\\Strings:Relative_Friend]")]
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
		KinRegister reg = Kinship.Build(new KinSources { Residents = Town });
		KinTree pierre = TreeOf(reg, "Pierre");
		Assert.Equal("Pierre", pierre.Root.People[0]);
		Assert.Equal(new[] { "Abigail", "Caroline", "Pierre" }, pierre.Everyone.OrderBy(n => n));
		Assert.Contains("Linus", reg.Solo);
		Assert.DoesNotContain("Maru", reg.Solo);
	}

	[Fact]
	public void ChildrenHangBelowTheirParentsAndParentsSitCentered()
	{
		KinTree tree = TreeOf(Kinship.Build(new KinSources { Residents = Town }), "Robin");
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
		KinTree tree = TreeOf(Kinship.Build(new KinSources { Residents = Town }), "Jas");
		Assert.Equal(new[] { 0, 1, 2 }, new[] { "Marnie", "Shane", "Jas" }.Select(n => CellOf(tree, n).Depth));
		Assert.Equal(new KinSide("Marnie", "Jas", ElderRole.AuntOrUncle), Assert.Single(tree.Sides));
		Assert.Equal(1, CellOf(TreeOf(Kinship.Build(new KinSources { Residents = Town }), "Alex"), "Alex").Depth);
	}

	[Fact]
	public void WideSubtreesDoNotOverlap()
	{
		string[] people = { "A", "B", "C", "D", "E", "F" };
		(string, string, string)[] declared = { ("B", "A", "mother"), ("C", "A", "mother"), ("D", "B", "father"), ("E", "B", "father"), ("F", "C", "mother") };
		KinTree tree = Kinship.Build(new KinSources { Residents = people, Declared = declared }).Trees.Single();
		Assert.Equal(3f, tree.Width);
		float[] row = new[] { "D", "E", "F" }.Select(n => CellOf(tree, n).Left).ToArray();
		Assert.Equal(new[] { 0f, 1f, 2f }, row);
		Assert.Equal(0.5f, CellOf(tree, "B").Left);
		Assert.Equal(2f, CellOf(tree, "C").Left);
		Assert.Equal(1f, tree.Root.Left);
	}

	[Fact]
	public void MarriageBeatsDating()
	{
		KinRegister married = Kinship.Build(new KinSources { Residents = new[] { "A", "B" }, Declared = new[] { ("A", "B", "girlfriend"), ("A", "B", "wife") } });
		Assert.Equal(PairKind.Married, married.Trees.Single().Root.Pairs.Single());
		Assert.Empty(married.Trees.Single().Links);
	}

	[Fact]
	public void TownFactsRepeatedInCharacterDataShowOnce()
	{
		(string, string, string)[] declared = { ("Abigail", "Pierre", "dad"), ("Abigail", "Caroline", "mom"), ("Pierre", "Caroline", "wife"), ("Caroline", "Pierre", "husband"), ("Jas", "Marnie", "aunt"), ("Emily", "Haley", "sister"), ("Haley", "Emily", "sister") };
		KinRegister reg = Kinship.Build(new KinSources { Residents = Town.Concat(new[] { "Emily", "Haley" }).ToArray(), Declared = declared });
		KinTree pierre = TreeOf(reg, "Pierre");
		Assert.Equal(new[] { "Pierre", "Caroline" }, pierre.Root.Below.Single().Elders);
		Assert.Single(TreeOf(reg, "Jas").Sides);
		Assert.Single(TreeOf(reg, "Emily").Root.Pairs);
	}

	[Fact]
	public void PartnerWithoutFamilyStandsBesideTheOneWithFamily()
	{
		KinRegister reg = Kinship.Build(new KinSources { Residents = new[] { "Pierre", "Abigail", "Caroline", "Mia" }, Declared = new[] { ("Mia", "Abigail", "girlfriend") } });
		Assert.DoesNotContain("Mia", reg.Solo);
		Assert.Contains("Mia", CellOf(TreeOf(reg, "Pierre"), "Abigail").People);
	}

	[Fact]
	public void CouplesFromTwoFamiliesStayApartButBothKnow()
	{
		KinRegister reg = Kinship.Build(new KinSources { Residents = Town, Declared = new[] { ("Abigail", "Sebastian", "boyfriend") } });
		Assert.NotSame(TreeOf(reg, "Abigail"), TreeOf(reg, "Sebastian"));
		Assert.Equal(PairKind.Dating, TreeOf(reg, "Abigail").Links.Single().Pair);
		Assert.Equal(PairKind.Dating, TreeOf(reg, "Sebastian").Links.Single().Pair);
	}

	[Fact]
	public void SecondSpouseAndThirdSiblingStillAppear()
	{
		KinRegister wives = Kinship.Build(new KinSources { Residents = new[] { "A", "B", "C" }, Declared = new[] { ("A", "B", "wife"), ("A", "C", "wife") } });
		Assert.Equal(new[] { "A", "B", "C" }, wives.Trees.Single().Root.People);
		KinRegister kids = Kinship.Build(new KinSources { Residents = new[] { "A", "B", "C" }, Declared = new[] { ("A", "B", "sister"), ("C", "A", "brother") } });
		Assert.Equal(3, kids.Trees.Single().Root.People.Count);
		Assert.Empty(kids.Solo);
	}

	[Fact]
	public void SiblingOfSomeoneWithParentsStandsWithThem()
	{
		KinRegister reg = Kinship.Build(new KinSources { Residents = new[] { "P", "A", "B" }, Declared = new[] { ("A", "P", "mother"), ("A", "B", "sister") } });
		KinTree tree = reg.Trees.Single();
		Assert.Same(CellOf(tree, "A"), CellOf(tree, "B"));
		Assert.Empty(reg.Solo);
	}

	[Fact]
	public void EachSpousesParentsAreStillRelatives()
	{
		KinRegister reg = Kinship.Build(new KinSources { Residents = new[] { "A", "B", "Pa", "Pb" }, Declared = new[] { ("A", "B", "wife"), ("A", "Pa", "mother"), ("B", "Pb", "father") } });
		Assert.Empty(reg.Solo);
		Assert.Contains(reg.Trees.SelectMany(t => t.Sides), s => s.Elder == "Pb" && s.Younger == "B");
		Assert.All(reg.Trees.Where(t => t.Everyone.Contains("Pb")), t => Assert.NotEmpty(t.Sides));
	}

	[Fact]
	public void CoParentsWhoAreNotACoupleBothKeepTheChild()
	{
		KinRegister reg = Kinship.Build(new KinSources { Residents = new[] { "X", "Y", "K" }, Declared = new[] { ("K", "X", "mother"), ("K", "Y", "father") } });
		Assert.Empty(reg.Solo);
		Assert.Contains(reg.Trees.Single(t => t.Everyone.Contains("Y")).Sides, s => s.Elder == "Y" && s.Younger == "K");
	}

	[Fact]
	public void StepparentIsNotCalledParent()
	{
		KinRegister reg = Kinship.Build(new KinSources { Residents = Town, Declared = new[] { ("Sebastian", "Demetrius", "stepdad") } });
		KinTree tree = TreeOf(reg, "Robin");
		Assert.Equal(new[] { "Robin" }, tree.Root.Below.Single(b => b.Person == "Sebastian").Elders);
		Assert.Contains(new KinSide("Demetrius", "Sebastian", ElderRole.Stepparent), tree.Sides);
	}

	[Fact]
	public void RoommateIsNotASpouseAndChildrenAreYours()
	{
		KinTree mine = Kinship.Build(new KinSources { Residents = Town, Farmer = new FarmerHome { Roommate = "Krobus", Children = new[] { Kinship.Outsider("小豆") } } }).Trees[0];
		Assert.Equal(PairKind.Roommates, mine.Root.Pairs.Single());
		Assert.Equal(new[] { Kinship.Me }, mine.Root.Below.Single().Elders);
	}

	[Fact]
	public void DeclaredLoopsDoNotHang()
	{
		KinRegister reg = Kinship.Build(new KinSources { Residents = new[] { "A", "B" }, Declared = new[] { ("A", "B", "mother"), ("B", "A", "mother") } });
		Assert.Equal(2, reg.Trees.SelectMany(t => t.Everyone).Count());
	}

	[Fact]
	public void AbsentRelativesAreLeftOut()
	{
		KinRegister reg = Kinship.Build(new KinSources { Residents = new[] { "Pierre", "Abigail" }, Elsewhere = new[] { "Abigail" } });
		Assert.Single(reg.Trees);
		Assert.Contains("Abigail", reg.Elsewhere);
		Assert.DoesNotContain("Abigail", reg.Solo);
	}

	[Fact]
	public void FarmerFamilyComesFirstWithSpouseAndChildren()
	{
		string kid = Kinship.Outsider("小豆");
		KinRegister reg = Kinship.Build(new KinSources { Residents = Town, Farmer = new FarmerHome { Spouse = "Abigail", Children = new[] { kid } } });
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
		KinRegister reg = Kinship.Build(new KinSources { Residents = Town, Moods = new[] { new KinMood("Pierre", "Abigail", Bond.Tense), new KinMood("Pierre", "Alex", Bond.Rivals) } });
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
