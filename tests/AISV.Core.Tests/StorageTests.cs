using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class StorageTests
{
	private sealed class Notes
	{
		public List<string> Lines { get; set; } = new List<string>();
	}

	private static readonly DocumentType<Notes> NotesV1 = new DocumentType<Notes>("notes", 1, () => new Notes());

	[Fact]
	public void MissingFileGivesDefaultWithoutWriting()
	{
		MemoryBackend backend = new MemoryBackend();
		DocumentStore store = new DocumentStore(backend);
		Notes notes = store.Get("a/notes.json", NotesV1);
		Assert.Empty(notes.Lines);
		Assert.False(store.IsDirty);
		Assert.Equal(0, store.Commit());
		Assert.Equal(0, backend.Writes);
	}

	[Fact]
	public void GetReturnsSameObjectAndCommitWritesOnlyDirty()
	{
		MemoryBackend backend = new MemoryBackend();
		DocumentStore store = new DocumentStore(backend);
		Notes a = store.Get("a.json", NotesV1);
		store.Get("b.json", NotesV1);
		Assert.Same(a, store.Get("a.json", NotesV1));
		a.Lines.Add("你好");
		store.MarkDirty("a.json");
		Assert.Equal(1, store.Commit());
		Assert.Contains("你好", backend.Files["a.json"]);
		Assert.False(backend.Files.ContainsKey("b.json"));
		Assert.Equal(0, store.Commit());
	}

	[Fact]
	public void DiscardDropsUnsavedChanges()
	{
		MemoryBackend backend = new MemoryBackend();
		DocumentStore store = new DocumentStore(backend);
		store.Get("a.json", NotesV1).Lines.Add("存了");
		store.MarkDirty("a.json");
		store.Commit();
		store.Get("a.json", NotesV1).Lines.Add("没存");
		store.MarkDirty("a.json");
		store.Discard();
		Assert.Equal(new[] { "存了" }, store.Get("a.json", NotesV1).Lines);
	}

	[Fact]
	public void OldVersionIsUpgraded()
	{
		MemoryBackend backend = new MemoryBackend();
		backend.Write("n.json", "{\"schema\":1,\"data\":{\"Text\":\"一行\"}}");
		DocumentType<Notes> v2 = new DocumentType<Notes>("notes", 2, () => new Notes(), (data, from) =>
		{
			string text = data["Text"]!.GetValue<string>();
			return new JsonObject { ["Lines"] = new JsonArray(text) };
		});
		Assert.Equal(new[] { "一行" }, new DocumentStore(backend).Get("n.json", v2).Lines);
	}

	[Fact]
	public void CorruptFileIsBackedUpAndReplacedByDefault()
	{
		MemoryBackend backend = new MemoryBackend();
		backend.Write("n.json", "{这不是 JSON");
		Notes notes = new DocumentStore(backend).Get("n.json", NotesV1);
		Assert.Empty(notes.Lines);
		Assert.Contains(backend.Files.Keys, k => k.StartsWith("n.json.corrupt-"));
	}

	[Fact]
	public void NewerVersionIsNotOverwrittenSilently()
	{
		MemoryBackend backend = new MemoryBackend();
		backend.Write("n.json", "{\"schema\":9,\"data\":{}}");
		Notes notes = new DocumentStore(backend).Get("n.json", NotesV1);
		Assert.Empty(notes.Lines);
		Assert.Contains(backend.Files.Keys, k => k.StartsWith("n.json.corrupt-"));
	}

	[Fact]
	public void DirectoryBackendRoundTripsAndRejectsEscapes()
	{
		string root = Path.Combine(Path.GetTempPath(), "aisv-test-" + Path.GetRandomFileName());
		try
		{
			DirectoryBackend backend = new DirectoryBackend(root);
			backend.Write("save/npcs/Abigail.json", "1");
			backend.Write("save/npcs/Abigail.json", "2");
			Assert.Equal("2", backend.Read("save/npcs/Abigail.json"));
			Assert.Equal(new[] { "save/npcs/Abigail.json" }, backend.List("save/npcs"));
			Assert.Null(backend.Read("save/none.json"));
			Assert.Throws<System.ArgumentException>(() => backend.Read("../outside.json"));
		}
		finally
		{
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
	}

	[Fact]
	public void FileWithoutEnvelopeIsTreatedAsCorrupt()
	{
		MemoryBackend backend = new MemoryBackend();
		backend.Write("n.json", "{\"Lines\":[\"一行\"]}");
		Assert.Empty(new DocumentStore(backend).Get("n.json", NotesV1).Lines);
	}
}
