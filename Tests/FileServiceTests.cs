using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scum_Bag;
using Scum_Bag.Services;
using ScumBag.Tests.Mock;

namespace ScumBag.Tests;

[TestClass]
public class FileServiceTests
{
    public TestContext TestContext { get; set; }

    private FileService _fileService;
    private string _tempDir;
    private IConfig _config;
    private ILoggingService _loggingService;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ScumBagTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _config = new MockConfig();
        _loggingService = new MockLoggingService();
        _fileService = new FileService(_config, _loggingService);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [TestMethod]
    public void HasChanges_BothNonExistent_ReturnsFalse()
    {
        string source = Path.Combine(_tempDir, "nonexistent1");
        string target = Path.Combine(_tempDir, "nonexistent2");
        Assert.IsFalse(_fileService.HasChanges(source, target));
    }

    [TestMethod]
    public void HasChanges_SourceExistsTargetDoesNot_ReturnsTrue()
    {
        string source = CreateFile("source.txt", "content");
        string target = Path.Combine(_tempDir, "target.txt");
        Assert.IsTrue(_fileService.HasChanges(source, target));
    }

    [TestMethod]
    public void HasChanges_BothFilesIdentical_ReturnsFalse()
    {
        string source = CreateFile("source.txt", "content");
        string target = CreateFile("target.txt", "content");
        Assert.IsFalse(_fileService.HasChanges(source, target));
    }

    [TestMethod]
    public void HasChanges_BothFilesDifferentContent_ReturnsTrue()
    {
        string source = CreateFile("source.txt", "content1");
        string target = CreateFile("target.txt", "content2");
        Assert.IsTrue(_fileService.HasChanges(source, target));
    }

    [TestMethod]
    public void PerformanceTest()
    {
        // Create test data
        string smallFile = CreateFile("small.txt", "test");
        string mediumFile = CreateFile("medium.txt", new string('x', 1024 * 1024)); // 1MB
        string largeDir = CreateDir("large");
        for (int i = 0; i < 100; i++)
            CreateFile(Path.Combine("large", $"file{i}.txt"), "content");

        string[] scenarios = { smallFile, mediumFile, largeDir };
        string[] names = { "small file", "medium file", "large dir" };

        for (int s = 0; s < scenarios.Length; s++)
        {
            long totalMicroseconds = 0;
            for (int i = 0; i < 10; i++)
            {
                var sw = Stopwatch.StartNew();
                _fileService.HasChanges(scenarios[s], scenarios[s]); // same path, should be false
                sw.Stop();
                totalMicroseconds += (long)(sw.Elapsed.TotalMilliseconds * 1000);
            }
            double avg = totalMicroseconds / 10.0;
            TestContext.WriteLine($"Average HasChanges time for {names[s]}: {avg} μs");
        }
    }

    private string CreateFile(string name, string content)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateDir(string name)
    {
        string path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }
}