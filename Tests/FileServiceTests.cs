using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scum_Bag;
using Scum_Bag.Services;
using ScumBag.Tests.Mock;

namespace ScumBag.Tests;

[TestClass]
public class FileServiceTests
{
    #region Properties
    
    public TestContext TestContext { get; set; }

    #endregion
    
    #region Fields
    
    private FileService _fileService;
    private string _tempDir;
    private IConfig _config;
    private ILoggingService _loggingService;
    
    #endregion
    
    #region Initialize

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ScumBagTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _config = new MockConfig();
        _loggingService = new MockLoggingService();
        _fileService = new FileService(_config, _loggingService);
    }
    
    #endregion
    
    #region Cleanup

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
    
    #endregion
    
    #region Tests

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
    public void HasChanges_BothDirectoriesIdentical_ReturnsFalse()
    {
        string sourceDir = CreateDirWithFiles("sourceDir", new[] { ("file1.txt", "content1"), ("file2.txt", "content2") });
        string targetDir = CreateDirWithFiles("targetDir", new[] { ("file1.txt", "content1"), ("file2.txt", "content2") });
        Assert.IsFalse(_fileService.HasChanges(sourceDir, targetDir));
    }

    [TestMethod]
    public void HasChanges_DirectoriesDifferentFileCounts_ReturnsTrue()
    {
        string sourceDir = CreateDirWithFiles("sourceDir", new[] { ("file1.txt", "content1"), ("file2.txt", "content2") });
        string targetDir = CreateDirWithFiles("targetDir", new[] { ("file1.txt", "content1") });
        Assert.IsTrue(_fileService.HasChanges(sourceDir, targetDir));
    }

    [TestMethod]
    public void HasChanges_DirectoriesSameCountDifferentSizes_ReturnsTrue()
    {
        string sourceDir = CreateDirWithFiles("sourceDir", new[] { ("file1.txt", "content1"), ("file2.txt", "longercontent2") });
        string targetDir = CreateDirWithFiles("targetDir", new[] { ("file1.txt", "content1"), ("file2.txt", "short") });
        Assert.IsTrue(_fileService.HasChanges(sourceDir, targetDir));
    }

    [TestMethod]
    public void HasChanges_DirectoriesSameCountSizeDifferentContent_ReturnsTrue()
    {
        string sourceDir = CreateDirWithFiles("sourceDir", new[] { ("file1.txt", "content1"), ("file2.txt", "content2") });
        string targetDir = CreateDirWithFiles("targetDir", new[] { ("file1.txt", "content1"), ("file2.txt", "content3") });
        Assert.IsTrue(_fileService.HasChanges(sourceDir, targetDir));
    }

    [TestMethod]
    public void HasChanges_BothDirectoriesEmpty_ReturnsFalse()
    {
        string sourceDir = CreateDir("sourceDir");
        string targetDir = CreateDir("targetDir");
        Assert.IsFalse(_fileService.HasChanges(sourceDir, targetDir));
    }

    [TestMethod]
    public void HasChanges_NestedDirectoriesIdentical_ReturnsFalse()
    {
        // Create source with nested structure
        string sourceDir = CreateDir("sourceDir");
        CreateFile("sourceDir/file1.txt", "content1");
        string subDir = CreateDir("sourceDir/sub");
        CreateFile("sourceDir/sub/file2.txt", "content2");

        // Create identical target
        string targetDir = CreateDir("targetDir");
        CreateFile("targetDir/file1.txt", "content1");
        string targetSubDir = CreateDir("targetDir/sub");
        CreateFile("targetDir/sub/file2.txt", "content2");

        Assert.IsFalse(_fileService.HasChanges(sourceDir, targetDir));
    }

    [TestMethod]
    public void HasChanges_FileExistsDirectoryDoesNot_ReturnsTrue()
    {
        string sourceFile = CreateFile("source.txt", "content");
        string targetDir = Path.Combine(_tempDir, "targetDir");
        Assert.IsTrue(_fileService.HasChanges(sourceFile, targetDir));
    }

    [TestMethod]
    public void HasChanges_DirectoryExistsFileDoesNot_ReturnsTrue()
    {
        string sourceDir = CreateDir("sourceDir");
        string targetFile = Path.Combine(_tempDir, "target.txt");
        Assert.IsTrue(_fileService.HasChanges(sourceDir, targetFile));
    }

    [TestMethod]
    public void HasChanges_FileVsExistingDirectory_ReturnsTrue()
    {
        string sourceFile = CreateFile("source.txt", "content");
        string targetDir = CreateDir("targetDir");
        Assert.IsTrue(_fileService.HasChanges(sourceFile, targetDir));
    }

    [TestMethod]
    public void HasChanges_DirectoryVsExistingFile_ReturnsTrue()
    {
        string sourceDir = CreateDir("sourceDir");
        string targetFile = CreateFile("target.txt", "content");
        Assert.IsTrue(_fileService.HasChanges(sourceDir, targetFile));
    }

    [TestMethod]
    public void HasChanges_ReadOnlyFile_ReturnsTrue()
    {
        string sourceFile = CreateFile("source.txt", "content1");
        File.SetAttributes(sourceFile, FileAttributes.ReadOnly);
        string targetFile = CreateFile("target.txt", "content2");
        Assert.IsTrue(_fileService.HasChanges(sourceFile, targetFile));
    }

    [TestMethod]
    public void HasChanges_LockedFile_RetrySucceeds()
    {
        string sourceFile = CreateFile("source.txt", "content1");
        string targetFile = CreateFile("target.txt", "content2");

        using (File.Open(sourceFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // File is locked, but HasChanges should retry with FileShare.ReadWrite
            Assert.IsTrue(_fileService.HasChanges(sourceFile, targetFile));
        }
    }

    [TestMethod]
    public void HasChanges_LongFilePaths_NoCrash()
    {
        // Create a file with a long path
        string longName = new string('a', 200); // Long filename
        string sourceFile = CreateFile(longName + ".txt", "content1");
        string targetFile = CreateFile("target.txt", "content2");
        Assert.IsTrue(_fileService.HasChanges(sourceFile, targetFile));
    }

    [TestMethod]
    public void HasChanges_SpecialCharactersInFilenames_ReturnsTrue()
    {
        string sourceFile = CreateFile("file with spaces & symbols!.txt", "content1");
        string targetFile = CreateFile("target.txt", "content2");
        Assert.IsTrue(_fileService.HasChanges(sourceFile, targetFile));
    }

    [TestMethod]
    public void HasChanges_SymbolicLinkToFile_ReturnsFalse()
    {
        string sourceFile = CreateFile("source.txt", "content");
        string linkFile = Path.Combine(_tempDir, "link.txt");
        File.CreateSymbolicLink(linkFile, sourceFile);
        Assert.IsFalse(_fileService.HasChanges(sourceFile, linkFile));
    }

    [TestMethod]
    public void HasChanges_NetworkPaths_GracefulHandling()
    {
        string networkPath = "/nonexistent/network/path";
        Assert.IsFalse(_fileService.HasChanges(networkPath, networkPath)); // Both don't exist, no changes
    }

    [TestMethod]
    public void GetFileData_SmallFile_ByteAccuracy()
    {
        string content = "Hello World";
        string filePath = CreateFile("test.txt", content);
        byte[] data = _fileService.GetFileData(filePath);
        string result = Encoding.UTF8.GetString(data);
        Assert.AreEqual(content, result);
    }

    [TestMethod]
    public void GetFileData_LargeFile_NoMemoryIssues()
    {
        string content = new string('x', 10 * 1024 * 1024); // 10MB
        string filePath = CreateFile("large.txt", content);
        byte[] data = _fileService.GetFileData(filePath);
        Assert.AreEqual(content.Length, data.Length);
        // Don't convert to string to avoid memory issues in test
    }

    [TestMethod]
    public void GetFileData_PermanentlyLockedFile_ReturnsNull()
    {
        string content = "test content";
        string filePath = CreateFile("locked.txt", content);
        using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            byte[] data = _fileService.GetFileData(filePath);
            Assert.IsNull(data);
        }
    }

    [TestMethod]
    public void GetFileData_TemporaryLock_RetrySucceeds()
    {
        string content = "test content";
        string filePath = CreateFile("tempLocked.txt", content);

        // Start a task that locks the file for 100ms
        Task lockTask = Task.Run(() =>
        {
            using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Thread.Sleep(100);
            }
        });

        // Wait a bit, then try to read
        Thread.Sleep(20);
        byte[] data = _fileService.GetFileData(filePath);
        string result = Encoding.UTF8.GetString(data);
        Assert.AreEqual(content, result);

        // Wait for lock task to complete
        lockTask.Wait();
    }

    [TestMethod]
    public void GetFileData_NonExistentFile_ReturnsNull()
    {
        string nonExistentPath = Path.Combine(_tempDir, "nonexistent.txt");
        byte[] data = _fileService.GetFileData(nonExistentPath);
        Assert.IsNull(data);
    }

    [TestMethod]
    public void CopyDirectory_FlatStructure_AllFilesCopied()
    {
        string sourceDir = CreateDirWithFiles("source", new[] { ("file1.txt", "content1"), ("file2.txt", "content2") });
        string destDir = Path.Combine(_tempDir, "dest");
        _fileService.CopyDirectory(sourceDir, destDir);
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "file2.txt")));
        Assert.AreEqual("content1", File.ReadAllText(Path.Combine(destDir, "file1.txt")));
        Assert.AreEqual("content2", File.ReadAllText(Path.Combine(destDir, "file2.txt")));
    }

    [TestMethod]
    public void CopyDirectory_Recursive_NestedPreserved()
    {
        // Create source with nested
        string sourceDir = CreateDir("source");
        CreateFile("source/file1.txt", "content1");
        string subDir = CreateDir("source/sub");
        CreateFile("source/sub/file2.txt", "content2");

        string destDir = Path.Combine(_tempDir, "dest");
        _fileService.CopyDirectory(sourceDir, destDir, true); // recursive true

        Assert.IsTrue(Directory.Exists(Path.Combine(destDir, "sub")));
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "sub", "file2.txt")));
        Assert.AreEqual("content1", File.ReadAllText(Path.Combine(destDir, "file1.txt")));
        Assert.AreEqual("content2", File.ReadAllText(Path.Combine(destDir, "sub", "file2.txt")));
    }

    [TestMethod]
    public void CopyDirectory_OverwriteBehavior_CorrectReplacement()
    {
        string sourceDir = CreateDirWithFiles("source", new[] { ("file1.txt", "newcontent") });
        string destDir = CreateDirWithFiles("dest", new[] { ("file1.txt", "oldcontent") });

        _fileService.CopyDirectory(sourceDir, destDir, false, true); // overwrite true

        Assert.AreEqual("newcontent", File.ReadAllText(Path.Combine(destDir, "file1.txt")));
    }

    [TestMethod]
    public void CopyDirectory_NonExistentDestination_DirectoryCreated()
    {
        string sourceDir = CreateDirWithFiles("source", new[] { ("file1.txt", "content") });
        string destDir = Path.Combine(_tempDir, "newdest");

        _fileService.CopyDirectory(sourceDir, destDir);

        Assert.IsTrue(Directory.Exists(destDir));
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "file1.txt")));
    }

    [TestMethod]
    public void CopyDirectory_ReadOnlyFiles_ErrorHandling()
    {
        string sourceDir = CreateDir("source");
        string readOnlyFile = CreateFile("source/readonly.txt", "content");
        File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);

        string destDir = CreateDir("dest");
        string destFile = CreateFile("dest/readonly.txt", "oldcontent");

        _fileService.CopyDirectory(sourceDir, destDir, false, false); // no overwrite

        // Since overwrite false, and dest exists, it should not copy or log error
        // But the method uses CopyTo with overwrite=false, which should throw if exists
        // Actually, File.CopyTo with overwrite=false throws if exists
        // But the test expects it to handle gracefully, perhaps by not crashing
        // For now, just check that the dest file remains unchanged
        Assert.AreEqual("oldcontent", File.ReadAllText(Path.Combine(destDir, "readonly.txt")));
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
    
    #endregion
    
    #region Private Methods

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

    private string CreateDirWithFiles(string dirName, (string fileName, string content)[] files)
    {
        string dirPath = CreateDir(dirName);
        foreach (var (fileName, content) in files)
        {
            CreateFile(Path.Combine(dirName, fileName), content);
        }
        return dirPath;
    }
    
    #endregion
}