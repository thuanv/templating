using System.Collections.Generic;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Core.Contracts;

namespace Microsoft.TemplateEngine.Orchestrator.RunnableProjects
{
    public interface IRunnableProjectConfig
    {
        IReadOnlyDictionary<string, Parameter> Parameters { get; }

        IReadOnlyList<KeyValuePair<string, IGlobalRunConfig>> SpecialOperationConfig { get; }

        IGlobalRunConfig OperationConfig { get; }

        IReadOnlyList<FileSource> Sources { get; }

        string DefaultName { get; }

        string Description { get; }

        string Name { get; }

        string ShortName { get; }

        string Author { get; }

        IReadOnlyDictionary<string, string> Tags { get; }

        IReadOnlyList<string> Classifications { get; }

        string GroupIdentity { get; }

        IFile SourceFile { set; }

        string Identity { get; }

        string PlaceholderFilename { get; set; }

        IReadOnlyList<IPostActionModel> PostActionModel { get; }

        IReadOnlyList<ICreationPathModel> PrimaryOutputs { get; }

        void Evaluate(IParameterSet parameters, IVariableCollection rootVariableCollection, IFileSystemInfo configFile);
    }
}