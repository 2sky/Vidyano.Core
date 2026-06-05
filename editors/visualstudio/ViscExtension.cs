// Extension entry point for the Vidyano Script (.visc) Visual Studio extension.
namespace Vidyano.Visc.VisualStudio;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

/// <summary>
/// The first type instantiated when the extension loads. It carries only the extension's identity;
/// all behavior lives in <see cref="ViscLanguageServerProvider"/>.
/// </summary>
[VisualStudioContribution]
internal class ViscExtension : Extension
{
    /// <inheritdoc/>
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "Vidyano.Visc.VS.b6f4e2a1-8c3d-4e9f-a1b2-3c4d5e6f7a8b",
            version: this.ExtensionAssemblyVersion,
            publisherName: "vidyano",
            displayName: "Vidyano Script (.visc)",
            description: "Syntax highlighting and language server support for Vidyano .visc scripts."),
    };

    /// <inheritdoc/>
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
    }
}
