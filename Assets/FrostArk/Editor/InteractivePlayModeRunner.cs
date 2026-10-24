using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace FrostArk.Unity.Editor
{
    public static class InteractivePlayModeRunner
    {
        private static bool _originalSettingsRecorded;
        private static bool _origEnterPlayModeOptionsEnabled;
        private static EnterPlayModeOptions _origEnterPlayModeOptions;

        private class TestCallbacks : ICallbacks
        {
            private readonly string _outputPath;

            public TestCallbacks(string outputPath)
            {
                _outputPath = outputPath;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                Time.captureFramerate = 60;
                Debug.Log($"[InteractivePlayModeRunner] PlayMode test run started: {testsToRun.Name} ({testsToRun.TestCaseCount} tests)");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Time.captureFramerate = 0;
                Debug.Log($"[InteractivePlayModeRunner] RunFinished. Total: {result.PassCount + result.FailCount + result.SkipCount}, Passed: {result.PassCount}, Failed: {result.FailCount}, Skipped: {result.SkipCount}");
                try
                {
                    var dir = Path.GetDirectoryName(_outputPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    TestRunnerApi.SaveResultToFile(result, _outputPath);
                    Debug.Log($"[InteractivePlayModeRunner] Test results saved to: {_outputPath}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[InteractivePlayModeRunner] Failed to save test results XML: {ex}");
                }

                try
                {
                    if (_originalSettingsRecorded)
                    {
                        EditorSettings.enterPlayModeOptionsEnabled = _origEnterPlayModeOptionsEnabled;
                        EditorSettings.enterPlayModeOptions = _origEnterPlayModeOptions;
                    }
                }
                catch { }

                int exitCode = result.FailCount > 0 ? 1 : 0;
                Debug.Log($"[InteractivePlayModeRunner] Exiting Editor with code: {exitCode}");
                EditorApplication.Exit(exitCode);
            }

            public void TestStarted(ITestAdaptor test)
            {
                if (!test.IsSuite)
                {
                    Time.captureFramerate = 60;
                    Debug.Log($"[InteractivePlayModeRunner] TestStarted: {test.FullName}");
                }
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.Test.IsSuite)
                {
                    Debug.Log($"[InteractivePlayModeRunner] TestFinished: {result.Test.FullName} -> {result.TestStatus}");
                }
            }
        }

        public static void RunCombatPlayModeTests()
        {
            RunTests(new[] { "FrostArk.Unity.Tests.CombatPlayModeTests" }, "results-playmode.xml");
        }

        public static void RunAllPlayModeTests()
        {
            RunTests(null, "results-playmode.xml");
        }

        public static void RunTests(string[] groupNames, string resultFileName)
        {
            string resultsPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", resultFileName);
            Debug.Log($"[InteractivePlayModeRunner] Starting PlayMode test run for groups: {(groupNames != null ? string.Join(", ", groupNames) : "<ALL>")}");

            if (!_originalSettingsRecorded)
            {
                _origEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
                _origEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;
                _originalSettingsRecorded = true;
            }

            // Disable Domain Reload to prevent losing batchmode coroutine execution.
            // Allow Scene Reload so PlaymodeTestsController.Start() executes and registers ActiveController.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new TestCallbacks(resultsPath));

            var filter = new Filter
            {
                testMode = TestMode.PlayMode,
                groupNames = (groupNames != null && groupNames.Length > 0) ? groupNames : null
            };

            var executionSettings = new ExecutionSettings(filter);
            api.Execute(executionSettings);
        }

        private class InEditorCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log($"[InEditorRunner] RunStarted: {testsToRun.Name}");
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                Debug.Log($"[InEditorRunner] RunFinished. Total: {result.PassCount + result.FailCount + result.SkipCount}, Passed: {result.PassCount}, Failed: {result.FailCount}");
            }
            public void TestStarted(ITestAdaptor test)
            {
                Debug.Log($"[InEditorRunner] TestStarted: {test.FullName}");
            }
            public void TestFinished(ITestResultAdaptor result)
            {
                Debug.Log($"[InEditorRunner] TestFinished: {result.Test.FullName} -> {result.TestStatus}");
            }
        }

        public static void RunAllPlayModeTestsInEditor()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new InEditorCallbacks());
            var filter = new Filter
            {
                testMode = TestMode.PlayMode
            };
            api.Execute(new ExecutionSettings(filter));
        }

        public static void RunPlayModeTestInEditor(string testName)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new InEditorCallbacks());
            var filter = new Filter
            {
                testMode = TestMode.PlayMode,
                testNames = new[] { testName }
            };
            api.Execute(new ExecutionSettings(filter));
        }
    }
}
