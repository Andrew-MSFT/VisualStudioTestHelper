﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TestHelpers.NetCoreTests.xUnit")]
namespace Hallsoft.TestHelpers
{
    public class TestFileHelper
    {
        /// <summary>
        /// Gets or sets the advanced configuration options
        /// </summary>
        public TestFileHelperConfiguration Configuration { get; private set; }

        /// <summary>
        /// DirectoryInfo of the folder hosting the current test project
        /// </summary>
        public DirectoryInfo ProjectDirectory { get; private set; }

        /// <summary>
        /// Full name of the folder hosting the current test project
        /// </summary>
        public string ProjectDirectoryName => this.ProjectDirectory.Name;
        
        /// <summary>
        /// Full name of the folder hosting the current test project
        /// </summary>
        public string ProjectDirectoryFullName => this.ProjectDirectory.FullName;

        /// <summary>
        /// If the test is currently running under Live Unit Testing
        /// </summary>
        public bool IsRunningAsLiveUnitTest { get; private set; }

        /// <summary>
        /// Detected test framework
        /// </summary>
        public TestFrameworks TestFramework { get; private set; }

        //Private properties
        readonly StackFrame[] _stackFrames = new StackTrace().GetFrames();


        /// <summary>
        /// Creates a new instance of VsTestHelper and assumes the output assembly's name matches the test project name.
        /// </summary>
        public TestFileHelper() : this(new TestFileHelperConfiguration())
        {

        }

        /// <summary>
        /// Creates a new instance of VsTestHelper
        /// </summary>
        /// <param name="configuration">Advanced configuration options</param>
        public TestFileHelper(TestFileHelperConfiguration configuration)
        {
            this.Configuration = configuration;

#if DEBUG
            this.IsRunningAsLiveUnitTest = configuration.IsLut ? configuration.IsLut : IsRunningUnderLut();
#else
            this.IsRunningAsLiveUnitTest = IsRunningUnderLut();
#endif

            this.TestFramework = configuration.TestFramework != TestFrameworks.Unknown ? configuration.TestFramework : DetectTestFramework();
            this.ProjectDirectory = GetTestProjectDirectory();
        }

        private TestFrameworks DetectTestFramework()
        {
            LogMessage("Walking stack for test framework detection");

            // write call stack method names
            foreach (StackFrame stackFrame in _stackFrames)
            {
                MethodBase method = stackFrame.GetMethod();
                string assemblyName = method.Module.Assembly.GetName().Name;

                LogMessage($"{assemblyName}::{method}", 1);

                if (assemblyName.StartsWith("Microsoft.VisualStudio.TestPlatform.MSTest", StringComparison.InvariantCultureIgnoreCase))
                {
                    return TestFrameworks.MsTest;
                }
                else if (assemblyName.StartsWith("xunit", StringComparison.InvariantCultureIgnoreCase))
                {
                    return TestFrameworks.xUnit;
                }
                else if (assemblyName.StartsWith("nunit", StringComparison.InvariantCultureIgnoreCase))
                {
                    return TestFrameworks.NUnit;
                }

            }

            return TestFrameworks.Unknown;
        }

        internal bool FindProjectDirectory(string startingPath, string projectFolderName, out string projectFolder, int levelsToSearch = 5)
        {
            IEnumerable<string> subDirectories = Directory.EnumerateDirectories(startingPath);
            projectFolder = null;
            levelsToSearch--;
            bool found = false;

            LogMessage($"Search for project folder {projectFolderName} in {startingPath}.  Subfolder search depth {levelsToSearch}", 1);

            foreach (string dir in subDirectories)
            {
                DirectoryInfo currentSubDir = new DirectoryInfo(dir);
                string subDirName = currentSubDir.Name;

                //Nomrally directories starting with . are for configuration. 
                //If Live Unit Testing is enabled, .vs directory will always find a match but it will be wrong
                if ((!this.Configuration.SearchDirectoriesStartingWithPeriod && subDirName.StartsWith(".")) 
                    || (!this.Configuration.SearchBinDirectories && subDirName == "bin")
                    || (!this.Configuration.SearchObjDirectories && subDirName == "obj")
                    || (this.Configuration.DirectoriesToIgnore.Contains(subDirName))
                    || subDirName == ".vs")
                {
                    continue;
                }
                if (subDirName == projectFolderName)
                {
                    LogMessage($"Project folder found: {currentSubDir.FullName}");

                    projectFolder = dir;
                    return true;
                }
                else if (levelsToSearch > 0)
                {
                    found = FindProjectDirectory(currentSubDir.FullName, projectFolderName, out projectFolder, levelsToSearch);
                    if (found)
                    {
                        break;
                    }
                }
            }

            return found;
        }

        

        /// <summary>
        /// Tries to find the path for the current test project
        /// </summary>
        /// <returns></returns>
        private DirectoryInfo GetTestProjectDirectory()
        {
            string rootPath;
            string codeBase = GetCallingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            string dir = this.Configuration.MockBinaryRootPath ?? Path.GetDirectoryName(path);

            string projectFolderName = this.Configuration.CurrentProjectFolderName ?? GetTestProjectNameFromCallingAssembly(this.Configuration.LogWriter);

            LogMessage($"Assembly executing in {dir}");

            if (this.IsRunningAsLiveUnitTest && dir.IndexOf(@"\.vs\") > 0)
            {
                LogMessage($"Finding Project Directory: Current dir={dir}");
                string solutionRoot = dir.Substring(0, dir.IndexOf(@"\.vs\") + 1);
                bool directoryFound = FindProjectDirectory(solutionRoot, projectFolderName, out string detectedFolder, this.Configuration.TestDirectorySearchDepth);
                if (directoryFound)
                {
                    rootPath = detectedFolder;
                }
                else
                {
                    throw new DirectoryNotFoundException($"Could not find directory for project {projectFolderName}.  You may need to explicitly specify the test project folder");
                }
            }
            else
            {
                rootPath = dir.Substring(0, dir.IndexOf(@"\bin\") + 1);
            }

            return new DirectoryInfo(rootPath);
        }

        internal string GetTestProjectNameFromCallingAssembly(ITestLogWriter logger = null)
        {
            Assembly callingAssembly = GetCallingAssembly();
            string name = callingAssembly.GetName().Name;

            logger?.LogMessage($"Calling assembly name: {name}");

            return name;
        }

        private Assembly GetCallingAssembly()
        {
            LogMessage("Finding calling assembly");
            string thisAssembly = Assembly.GetExecutingAssembly().GetName().Name;
            

            //Have to find the first assembly in the callstack that isn't this one
            foreach (StackFrame stackFrame in _stackFrames)
            {
                MethodBase method = stackFrame.GetMethod();
                Assembly assembly = method.Module.Assembly;
                string assemblyName = assembly.GetName().Name;

                LogMessage($"{assembly.CodeBase}!{method.Name}", 1);

                if (assemblyName != thisAssembly)
                {
                    return method.Module.Assembly;
                }
            }

            return null;
        }

        private bool IsRunningUnderLut()
        {
            string assemblyLocation = GetCallingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(assemblyLocation);
            string path = Uri.UnescapeDataString(uri.Path);
            string dir = Path.GetDirectoryName(path);

            LogMessage($"Detecting LUT based on exe path: exeDir={dir}");

            //running in the context of LUT, and the path needs to be adjusted
            if (dir.Contains(@"\.vs\") && dir.Contains("lut"))
            {
                LogMessage("Detected as LUT");
                return true;
            }

            LogMessage("LUT not detected");
            return false;
        }

        private void LogMessage(string message, int indentLevel = 0)
        {
            if (this.Configuration.LogWriter != null)
            {
                int totalWidth = message.Length + indentLevel * 4;
                this.Configuration.LogWriter.LogMessage(message.PadLeft(totalWidth));
            }
        }

        /// <summary>
        /// Opens a StreamReader on the specified file using the current test project's directory as the starting location
        /// </summary>
        /// <param name="fileName">Name of file to open</param>
        /// <returns>StreamReader</returns>
        public StreamReader OpenFile(string fileName)
        {
            return OpenFile(new string[] { fileName });
        }

        /// <summary>
        /// Opens a StreamReader on the specified file using the current test project's directory as the starting location
        /// </summary>
        /// <param name="fileName">Name of file to open</param>
        /// <param name="pathRelativeToTestProject">Path relative to current test project's directory</param>
        /// <returns>StreamReader</returns>
        public StreamReader OpenFile(params string[] paths)
        {
            string testProjectDirectory = this.ProjectDirectory.FullName;
            string[] args = new string[paths.Length + 1];
            args[0] = testProjectDirectory;
            for (int i = 0; i < paths.Length; i++)
            {
                args[i + 1] = paths[i];
            }
            string fullPath = Path.Combine(args);

            LogMessage($"Attempting to open {fullPath}");

            return new StreamReader(fullPath);
        }

    }
}
