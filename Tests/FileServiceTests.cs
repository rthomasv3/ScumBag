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
[DoNotParallelize]
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
        if (string.IsNullOrEmpty(_tempDir) || !Directory.Exists(_tempDir))
        {
            return;
        }

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_tempDir, true);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt == 4)
                {
                    throw;
                }

                Thread.Sleep(100);
            }
            catch (IOException)
            {
                if (attempt == 4)
                {
                    throw;
                }

                Thread.Sleep(100);
            }
        }
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
        string sourceDir = CreateDir("sourceDir");
        CreateFile(Path.Combine("sourceDir", "file1.txt"), "content1");
        CreateDir(Path.Combine("sourceDir", "sub"));
        CreateFile(Path.Combine("sourceDir", "sub", "file2.txt"), "content2");

        string targetDir = CreateDir("targetDir");
        CreateFile(Path.Combine("targetDir", "file1.txt"), "content1");
        CreateDir(Path.Combine("targetDir", "sub"));
        CreateFile(Path.Combine("targetDir", "sub", "file2.txt"), "content2");

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
        bool result;

        using (File.Open(sourceFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = _fileService.HasChanges(sourceFile, targetFile);
        }

        Assert.IsTrue(result);
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
        string filePath = CreateFile("locked.txt", "content");
        byte[] data;

        using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            data = _fileService.GetFileData(filePath);
        }

        Assert.IsNull(data);
    }

    [TestMethod]
    public void GetFileData_TemporaryLock_RetrySucceeds()
    {
        string filePath = CreateFile("tempLocked.txt", "test content");

        Task lockTask = Task.Run(() =>
        {
            using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Thread.Sleep(100);
            }
        });

        Thread.Sleep(20);
        byte[] data = _fileService.GetFileData(filePath);
        string result = Encoding.UTF8.GetString(data);
        Assert.AreEqual("test content", result);

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
        string sourceDir = CreateDir("source");
        CreateFile(Path.Combine("source", "file1.txt"), "content1");
        CreateDir(Path.Combine("source", "sub"));
        CreateFile(Path.Combine("source", "sub", "file2.txt"), "content2");

        string destDir = Path.Combine(_tempDir, "dest");
        _fileService.CopyDirectory(sourceDir, destDir, true);

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
        string readOnlyFile = CreateFile(Path.Combine("source", "readonly.txt"), "content");
        File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);

        string destDir = CreateDir("dest");
        string destFile = CreateFile(Path.Combine("dest", "readonly.txt"), "oldcontent");

        _fileService.CopyDirectory(sourceDir, destDir, false, false);

        Assert.AreEqual("oldcontent", File.ReadAllText(Path.Combine(destDir, "readonly.txt")));
    }

    [TestMethod]
    public void PerformanceTest()
    {
        string smallFile = CreateFile("small.txt", "test");
        string mediumFile = CreateFile("medium.txt", new string('x', 1024 * 1024));
        string largeDir = CreateDir("large");
        for (int i = 0; i < 100; i++)
        {
            CreateFile(Path.Combine("large", $"file{i}.txt"), "content");
        }

        string[] scenarios = { smallFile, mediumFile, largeDir };
        string[] names = { "small file", "medium file", "large dir" };

        for (int s = 0; s < scenarios.Length; s++)
        {
            long totalMicroseconds = 0;
            for (int i = 0; i < 10; i++)
            {
                var sw = Stopwatch.StartNew();
                _fileService.HasChanges(scenarios[s], scenarios[s]);
                sw.Stop();
                totalMicroseconds += (long)(sw.Elapsed.TotalMilliseconds * 1000);
            }
            double avg = totalMicroseconds / 10.0;
            TestContext.WriteLine($"Average HasChanges time for {names[s]}: {avg} μs");
        }
    }

    [TestMethod]
    public void PerformanceTest_DirectoryComparison_Large()
    {
        string sourceDir = CreateDir("perfSource");
        for (int i = 0; i < 1000; i++)
        {
            CreateFile(Path.Combine("perfSource", $"file{i}.txt"), $"content{i}");
        }

        string targetDir = CreateDir("perfTarget");
        for (int i = 0; i < 1000; i++)
        {
            CreateFile(Path.Combine("perfTarget", $"file{i}.txt"), $"content{i}");
        }

        long totalMicroseconds = 0;
        for (int i = 0; i < 10; i++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            _fileService.HasChanges(sourceDir, targetDir);
            sw.Stop();
            totalMicroseconds += (long)(sw.Elapsed.TotalMilliseconds * 1000);
        }
        double avg = totalMicroseconds / 10.0;
        TestContext.WriteLine($"Average DirectoryComparison (1000 files): {avg} μs");
    }

    [TestMethod]
    public void PerformanceTest_GetFileData_LargeFile()
    {
        string largeFile = CreateFile("perfLarge.txt", new string('x', 10 * 1024 * 1024));

        long totalMicroseconds = 0;
        for (int i = 0; i < 10; i++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            byte[] data = _fileService.GetFileData(largeFile);
            sw.Stop();
            totalMicroseconds += (long)(sw.Elapsed.TotalMilliseconds * 1000);
        }
        double avg = totalMicroseconds / 10.0;
        TestContext.WriteLine($"Average GetFileData (10MB): {avg} μs");
    }

    [TestMethod]
    public void PerformanceTest_GetHash_LargeDirectory()
    {
        string largeDir = CreateDir("perfHashDir");
        for (int i = 0; i < 1000; i++)
        {
            CreateFile(Path.Combine("perfHashDir", $"file{i}.txt"), $"content{i}");
        }

        long totalMicroseconds = 0;
        for (int i = 0; i < 10; i++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string hash = _fileService.GetHash(largeDir);
            sw.Stop();
            totalMicroseconds += (long)(sw.Elapsed.TotalMilliseconds * 1000);
        }
        double avg = totalMicroseconds / 10.0;
        TestContext.WriteLine($"Average GetHash (1000 files): {avg} μs");
    }

    [TestMethod]
    public void PerformanceTest_DirectoryComparison_FirstFileDiffers()
    {
        string sourceDir = CreateDir("perfSourceDiff");
        string targetDir = CreateDir("perfTargetDiff");

        for (int i = 0; i < 1000; i++)
        {
            string content = i == 0 ? "DIFFERENT" : $"content{i}";
            CreateFile(Path.Combine("perfSourceDiff", $"file{i:D4}.txt"), content);
            CreateFile(Path.Combine("perfTargetDiff", $"file{i:D4}.txt"), $"content{i}");
        }

        long totalMicroseconds = 0;
        for (int i = 0; i < 10; i++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            bool result = _fileService.HasChanges(sourceDir, targetDir);
            sw.Stop();
            Assert.IsTrue(result);
            totalMicroseconds += (long)(sw.Elapsed.TotalMilliseconds * 1000);
        }
        double avg = totalMicroseconds / 10.0;
        TestContext.WriteLine($"Average DirectoryComparison (1000 files, first differs): {avg} μs");
    }

    [TestMethod]
    public void PerformanceTest_FileComparison_BufferSizeImpact()
    {
        int[] testSizes = { 10 * 1024, 100 * 1024, 1024 * 1024, 10 * 1024 * 1024 };
        string[] sizeNames = { "10KB", "100KB", "1MB", "10MB" };

        for (int s = 0; s < testSizes.Length; s++)
        {
            string size1Content = new string('x', testSizes[s]);
            string size1File = CreateFile($"identical1_{sizeNames[s]}.dat", size1Content);
            string size2File = CreateFile($"identical2_{sizeNames[s]}.dat", size1Content);

            string diff1Content = new string('x', testSizes[s]);
            string diff2Content = "y" + new string('x', testSizes[s] - 1);
            string diff1File = CreateFile($"different1_{sizeNames[s]}.dat", diff1Content);
            string diff2File = CreateFile($"different2_{sizeNames[s]}.dat", diff2Content);

            long identicalTime = 0;
            long differentTime = 0;

            for (int i = 0; i < 5; i++)
            {
                Stopwatch sw1 = Stopwatch.StartNew();
                _fileService.HasChanges(size1File, size2File);
                sw1.Stop();
                identicalTime += (long)(sw1.Elapsed.TotalMilliseconds * 1000);

                Stopwatch sw2 = Stopwatch.StartNew();
                _fileService.HasChanges(diff1File, diff2File);
                sw2.Stop();
                differentTime += (long)(sw2.Elapsed.TotalMilliseconds * 1000);
            }

            TestContext.WriteLine($"{sizeNames[s]} files - Identical: {identicalTime / 5.0:F1} μs, Different (first byte): {differentTime / 5.0:F1} μs");
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
            CreateFile(Path.Combine(dirPath, Path.GetFileName(fileName)), content);
        }
        return dirPath;
    }

    #endregion
}
