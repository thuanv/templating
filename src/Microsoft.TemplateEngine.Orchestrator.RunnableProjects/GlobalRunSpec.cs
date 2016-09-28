using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Core;
using Microsoft.TemplateEngine.Core.Contracts;
using Microsoft.TemplateEngine.Core.Expressions.Cpp;
using Microsoft.TemplateEngine.Core.Operations;
using Microsoft.TemplateEngine.Core.Util;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Config;

namespace Microsoft.TemplateEngine.Orchestrator.RunnableProjects
{
    public class GlobalRunSpec : IGlobalRunSpec
    {
        private static IReadOnlyDictionary<string, IOperationConfig> _operationConfigLookup;

        private static void EnsureOperationConfigs(IComponentManager componentManager)
        {
            if (_operationConfigLookup == null)
            {
                List<IOperationConfig> operationConfigReaders = new List<IOperationConfig>(componentManager.OfType<IOperationConfig>());
                Dictionary<string, IOperationConfig> operationConfigLookup = new Dictionary<string, IOperationConfig>();

                foreach (IOperationConfig opConfig in operationConfigReaders)
                {
                    operationConfigLookup[opConfig.Key] = opConfig;
                }

                _operationConfigLookup = operationConfigLookup;
            }
        }

        public IReadOnlyList<IPathMatcher> Exclude { get; }

        public IReadOnlyList<IPathMatcher> Include { get; }

        public IReadOnlyList<IOperationProvider> Operations { get; }

        public IVariableCollection RootVariableCollection { get; }

        public IReadOnlyDictionary<IPathMatcher, IRunSpec> Special { get; }

        public IReadOnlyList<IPathMatcher> CopyOnly { get; }

        public IReadOnlyDictionary<string, string> Rename { get; }

        public string PlaceholderFilename { get; }

        public bool TryGetTargetRelPath(string sourceRelPath, out string targetRelPath)
        {
            return Rename.TryGetValue(sourceRelPath, out targetRelPath);
        }

        public GlobalRunSpec(ITemplateEngineHost host,
            FileSource source,
            IDirectory templateRoot,
            IComponentManager componentManager,
            IParameterSet parameters, 
            IVariableCollection variables,
            IGlobalRunConfig globalConfig,
            IReadOnlyList<KeyValuePair<string, IGlobalRunConfig>> fileGlobConfigs,
            string placeholderFilename)
        {
            EnsureOperationConfigs(componentManager);

            Include = SetupPathInfoFromSource(source.Include);
            CopyOnly = SetupPathInfoFromSource(source.CopyOnly);
            Exclude = SetupPathInfoFromSource(source.Exclude);
            Rename = source.Rename ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PlaceholderFilename = placeholderFilename;

            // regular operations
            RootVariableCollection = variables;
            Operations = ResolveOperations(host, globalConfig, templateRoot, variables, parameters);

            // file glob specific operations
            Dictionary<IPathMatcher, IRunSpec> specials = new Dictionary<IPathMatcher, IRunSpec>();

            if (fileGlobConfigs != null)
            {
                foreach (KeyValuePair<string, IGlobalRunConfig> specialEntry in fileGlobConfigs)
                {
                    IReadOnlyList<IOperationProvider> specialOps = null;
                    IVariableCollection specialVariables = variables;

                    if (specialEntry.Value != null)
                    {
                        specialOps = ResolveOperations(host, specialEntry.Value, templateRoot, variables, parameters);
                        specialVariables = VariableCollection.SetupVariables(parameters, specialEntry.Value.VariableSetup);
                    }

                    RunSpec spec = new RunSpec(specialOps, specialVariables ?? variables);
                    specials[new GlobbingPatternMatcher(specialEntry.Key)] = spec;
                }
            }

            Special = specials;
        }

        // Returns a list of operations which contains the custom operations and the default operations.
        // If there are custom Conditional operations, don't include the default Conditionals.
        //
        // Note: we may need a more robust filtering mechanism in the future.
        private static IReadOnlyList<IOperationProvider> ResolveOperations(ITemplateEngineHost host, IGlobalRunConfig runConfig, IDirectory templateRoot, IVariableCollection variables, IParameterSet parameters)
        {
            IReadOnlyList<IOperationProvider> customOperations = SetupCustomOperations(host, runConfig.CustomOperations, templateRoot, variables);
            IReadOnlyList<IOperationProvider> defaultOperations = SetupOperations(parameters, runConfig);

            List<IOperationProvider> operations = new List<IOperationProvider>(customOperations);

            if (customOperations.Any(x => x is Conditional))
            {
                operations.AddRange(defaultOperations.Where(op => !(op is Conditional)));
            }
            else
            {
                operations.AddRange(defaultOperations);
            }

            return operations;
        }

        private static IReadOnlyList<IOperationProvider> SetupOperations(IParameterSet parameters, IGlobalRunConfig runConfig)
        {
            // default operations
            List<IOperationProvider> operations = new List<IOperationProvider>();
            operations.AddRange(runConfig.Operations);

            // replacements
            if (runConfig.Replacements != null)
            {
                foreach (IReplacementTokens replaceSetup in runConfig.Replacements)
                {
                    IOperationProvider replacement = ReplacementConfig.Setup(replaceSetup, parameters);
                    if (replacement != null)
                    {
                        operations.Add(replacement);
                    }
                }
            }

            if (runConfig.VariableSetup.Expand)
            {
                operations?.Add(new ExpandVariables(null));
            }

            return operations;
        }

        private static IReadOnlyList<IOperationProvider> SetupCustomOperations(ITemplateEngineHost host, IReadOnlyList<ICustomOperationModel> customModel, IDirectory templateRoot, IVariableCollection variables)
        {
            List<IOperationProvider> customOperations = new List<IOperationProvider>();

            foreach (ICustomOperationModel opModelUntyped in customModel)
            {
                CustomOperationModel opModel = opModelUntyped as CustomOperationModel;
                if (opModel == null)
                {
                    host.LogMessage($"Operation type = [{opModel.Type}] from configuration is unknown.");
                    continue;
                }
                    
                string opType = opModel.Type;
                string condition = opModel.Condition;

                if (string.IsNullOrEmpty(condition)
                    || CppStyleEvaluatorDefinition.EvaluateFromString(condition, variables))
                {
                    IOperationConfig realConfigObject;
                    if (_operationConfigLookup.TryGetValue(opType, out realConfigObject))
                    {
                        customOperations.AddRange(
                            realConfigObject.ConfigureFromJObject(opModel.Configuration, templateRoot));
                    }
                }
            }

            return customOperations;
        }

        private static IReadOnlyList<IPathMatcher> SetupPathInfoFromSource(IReadOnlyList<string> fileSources)
        {
            int expect = fileSources?.Count ?? 0;
            List<IPathMatcher> paths = new List<IPathMatcher>(expect);
            if (fileSources != null && expect > 0)
            {
                foreach (string source in fileSources)
                {
                    paths.Add(new GlobbingPatternMatcher(source));
                }
            }

            return paths;
        }

        internal class ProcessorState : IProcessorState
        {
            public ProcessorState(IVariableCollection vars, byte[] buffer, Encoding encoding)
            {
                Config = new EngineConfig(vars);
                CurrentBuffer = buffer;
                CurrentBufferPosition = 0;
                Encoding = encoding;
                EncodingConfig = new EncodingConfig(Config, encoding);
            }

            public IEngineConfig Config { get; }

            public byte[] CurrentBuffer { get; private set; }

            public int CurrentBufferLength => CurrentBuffer.Length;

            public int CurrentBufferPosition { get; }

            public Encoding Encoding { get; set; }

            public IEncodingConfig EncodingConfig { get; }

            public bool AdvanceBuffer(int bufferPosition)
            {
                byte[] tmp = new byte[CurrentBufferLength - bufferPosition];
                Buffer.BlockCopy(CurrentBuffer, bufferPosition, tmp, 0, CurrentBufferLength - bufferPosition);
                CurrentBuffer = tmp;

                return true;
            }

            public void SeekBackUntil(ITokenTrie match)
            {
                throw new NotImplementedException();
            }

            public void SeekBackUntil(ITokenTrie match, bool consume)
            {
                throw new NotImplementedException();
            }

            public void SeekBackWhile(ITokenTrie match)
            {
                throw new NotImplementedException();
            }

            public void SeekForwardUntil(ITokenTrie trie, ref int bufferLength, ref int currentBufferPosition)
            {
                throw new NotImplementedException();
            }

            public void SeekForwardThrough(ITokenTrie trie, ref int bufferLength, ref int currentBufferPosition)
            {
                throw new NotImplementedException();
            }

            public void SeekForwardWhile(ITokenTrie trie, ref int bufferLength, ref int currentBufferPosition)
            {
                throw new NotImplementedException();
            }
        }
    }
}